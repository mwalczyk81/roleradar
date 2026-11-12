namespace RoleRadar.Tests.Integration;

using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using Xunit;

/// <summary>
/// docs/api/odata-queries.md only contains examples that were checked. Every <c>curl</c> line in it is sent to a real
/// API host over a Testcontainers Mongo, once through HttpClient and once as the literal shell command (when bash and
/// curl exist), and must produce the status the line's preceding comment states (200 when there is none).
/// </summary>
public sealed partial class ODataDocsExamplesTests : IAsyncLifetime
{
    private const string Owner = "docs-examples-owner";

    private readonly ODataTestHost _host = new("odata_docs_examples_test_db", Owner);

    public async ValueTask InitializeAsync()
    {
        await _host.StartDatabaseAsync();
        await _host.StartApiAsync();
    }

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    private sealed record Example(string Line, string Url, string Method, int ExpectedStatus);

    [Fact]
    public async Task EveryDocumentedExample_ReturnsTheStatusTheDocSays_AndTheEnvelopeItDescribes()
    {
        var examples = LoadExamples();

        var missing = new List<string>();
        foreach (var set in new[] { "Postings", "Companies", "Applications", "Notes", "Criteria", "SearchRuns" })
        {
            foreach (var option in new[] { "$filter", "$orderby", "$select", "$count", "$top", "$skip" })
            {
                if (!examples.Any(e => e.Url.StartsWith("/odata/" + set + "?", StringComparison.Ordinal) && e.Url.Contains(option, StringComparison.Ordinal)))
                {
                    missing.Add($"{set} {option}");
                }
            }
        }

        Assert.True(missing.Count == 0, "docs/api/odata-queries.md has no curl example for: " + string.Join(", ", missing));
        Assert.True(examples.Count >= 30, $"expected at least 30 curl examples, found {examples.Count}");

        var problems = new List<string>();
        foreach (var example in examples)
        {
            Assert.DoesNotContain(' ', example.Url);

            using var request = new HttpRequestMessage(new HttpMethod(example.Method), example.Url);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _host.Token(Owner));
            using var response = await _host.Client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if ((int)response.StatusCode != example.ExpectedStatus)
            {
                problems.Add($"{example.Method} {example.Url}: {(int)response.StatusCode}, doc says {example.ExpectedStatus}. {body}");
                continue;
            }

            switch (example.ExpectedStatus)
            {
                case 200:
                    using (var json = System.Text.Json.JsonDocument.Parse(body))
                    {
                        if (json.RootElement.GetProperty("value").ValueKind != System.Text.Json.JsonValueKind.Array)
                        {
                            problems.Add($"{example.Url}: no value array");
                        }

                        if (example.Url.Contains("$count=true", StringComparison.Ordinal) && !json.RootElement.TryGetProperty("@odata.count", out _))
                        {
                            problems.Add($"{example.Url}: no @odata.count");
                        }
                    }

                    break;
                case 400:
                    using (var json = System.Text.Json.JsonDocument.Parse(body))
                    {
                        if (json.RootElement.GetProperty("status").GetInt32() != 400 ||
                            json.RootElement.GetProperty("errorCode").GetString() != "validation_error" ||
                            string.IsNullOrWhiteSpace(json.RootElement.GetProperty("detail").GetString()))
                        {
                            problems.Add($"{example.Url}: 400 body is not the documented problem document: {body}");
                        }
                    }

                    break;
                case 405:
                    if (!response.Content.Headers.Allow.Contains("GET") || response.Content.Headers.Allow.Count != 1)
                    {
                        problems.Add($"{example.Url}: 405 without Allow: GET");
                    }

                    break;
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    private static readonly string[] InsightsProperties = ["hasApplication", "firstSeenAt", "hasOffer", "hasRejection", "rejectedBeforeInterview"];

    [Fact]
    public void EveryInsightsProperty_HasADocumentedExample()
    {
        // 007: each property the Insights drill-downs filter on needs a curl example (all examples are executed above).
        var examples = LoadExamples();
        var missing = InsightsProperties
            .Where(property => !examples.Any(e => e.Url.Contains("$filter=", StringComparison.Ordinal) &&
                                                  e.Url.Contains(property, StringComparison.Ordinal)))
            .ToList();

        Assert.True(missing.Count == 0, "docs/api/odata-queries.md has no $filter curl example for: " + string.Join(", ", missing));
    }

    [Fact]
    public async Task DocumentedClaims_AboutMetadata_ServiceDocument_AndMissingToken_Hold()
    {
        foreach (var path in new[] { "/odata", "/odata/$metadata" })
        {
            using var response = await _host.GetAsync(Owner, path);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path}: {(int)response.StatusCode}");
        }

        using var anonymous = await _host.Client.GetAsync("/odata/Postings");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        // The unsupported $expand and an unknown field are rejected, as the rules section says.
        foreach (var path in new[] { "/odata/Postings?$expand=x", "/odata/Postings?$orderby=requisitionId", "/odata/Notes?$filter=contains(tolower(content),'x')" })
        {
            using var response = await _host.GetAsync(Owner, path);
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{path}: {(int)response.StatusCode}");
        }
    }

    [Fact]
    public async Task EveryDocumentedExample_Works_AsTheLiteralCurlCommand()
    {
        var bash = FindOnPath("bash");
        if (bash is null || FindOnPath("curl") is null)
        {
            Assert.Skip("bash and curl are not on PATH; the HttpClient test above still checked every example.");
            return;
        }

        var problems = new List<string>();
        foreach (var example in LoadExamples())
        {
            // The shell line exactly as documented, plus a trailer that prints the status code.
            var script = example.Line + " -w '\\n%{http_code}'";
            var info = new ProcessStartInfo(bash) { RedirectStandardOutput = true, RedirectStandardError = true };
            info.ArgumentList.Add("-c");
            info.ArgumentList.Add(script);
            info.Environment["BASE"] = _host.Client.BaseAddress!.ToString().TrimEnd('/');
            info.Environment["TOKEN"] = _host.Token(Owner);

            using var process = Process.Start(info)!;
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            var status = output.TrimEnd().Split('\n').Last().Trim();
            if (status != example.ExpectedStatus.ToString(System.Globalization.CultureInfo.InvariantCulture))
            {
                problems.Add($"{example.Line}: status {status}, doc says {example.ExpectedStatus}. {error}");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    private static List<Example> LoadExamples()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "RoleRadar.sln")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        var lines = File.ReadAllLines(Path.Combine(root.FullName, "docs", "api", "odata-queries.md"));

        var examples = new List<Example>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith("curl ", StringComparison.Ordinal))
            {
                continue;
            }

            var quoted = QuotedUrl().Match(lines[i]);
            Assert.True(quoted.Success, $"no $BASE url in: {lines[i]}");
            var url = quoted.Groups[1].Value.Replace("\\$", "$", StringComparison.Ordinal);

            var method = MethodFlag().Match(lines[i]) is { Success: true } m ? m.Groups[1].Value : "GET";
            var expected = i > 0 && StatusComment().Match(lines[i - 1]) is { Success: true } c ? int.Parse(c.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : 200;
            examples.Add(new Example(lines[i], url, method, expected));
        }

        return examples;
    }

    private static string? FindOnPath(string name)
    {
        if (OperatingSystem.IsWindows() && string.Equals(name, "bash", StringComparison.OrdinalIgnoreCase))
        {
            var gitBash = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe");
            if (File.Exists(gitBash))
            {
                return gitBash;
            }
        }

        var extensions = OperatingSystem.IsWindows() ? new[] { ".exe", ".cmd", "" } : new[] { "" };
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
        {
            foreach (var ext in extensions)
            {
                var candidate = Path.Combine(dir, name + ext);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    [GeneratedRegex("\"\\$BASE(/odata[^\"]*)\"")]
    private static partial Regex QuotedUrl();

    [GeneratedRegex("-X (\\w+)")]
    private static partial Regex MethodFlag();

    [GeneratedRegex("-> (\\d{3})")]
    private static partial Regex StatusComment();
}
