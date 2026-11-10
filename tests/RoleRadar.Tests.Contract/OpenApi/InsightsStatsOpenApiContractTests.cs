using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace RoleRadar.Tests.Contract.OpenApi;

/// <summary>
/// T068: the committed OpenAPI snapshot and the generated TypeScript client describe <c>GET /api/v1/insights/stats</c>
/// exactly as <c>specs/007-insights-dashboard/contracts/insights-stats.md</c> says (ADR 0019): <c>window</c> is a string
/// enum, <c>asOf</c> is a string with <c>format: date</c>, both are required, and the client types carry the same shape.
/// Regenerate with a build of src/RoleRadar.Api, then <c>pnpm --dir contracts run generate-client</c>.
/// </summary>
public sealed partial class InsightsStatsOpenApiContractTests
{
    private const string StatsPath = "/api/v1/insights/stats";

    private static JsonElement LoadSnapshot()
    {
        var path = Path.Combine(RepoRoot(), "contracts", "openapi", "roleradar-api.json");
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
    }

    private static JsonElement StatsOperation(JsonElement snapshot)
    {
        Assert.True(
            snapshot.GetProperty("paths").TryGetProperty(StatsPath, out var item),
            $"Expected '{StatsPath}' in contracts/openapi/roleradar-api.json. Build src/RoleRadar.Api to regenerate it.");
        return item;
    }

    private static JsonElement Parameter(JsonElement operation, string name) =>
        operation.GetProperty("parameters").EnumerateArray().Single(p => p.GetProperty("name").GetString() == name);

    [Fact]
    public void Snapshot_HasOnlyTheGetOperation_ForTheStatsPath()
    {
        var item = StatsOperation(LoadSnapshot());

        Assert.Equal(["get"], item.EnumerateObject().Select(p => p.Name).ToArray());
    }

    [Fact]
    public void Snapshot_AddsNoInsightsPathOtherThanStats()
    {
        var insightsPaths = LoadSnapshot().GetProperty("paths").EnumerateObject()
            .Select(p => p.Name)
            .Where(n => n.Contains("insights", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Equal([StatsPath], insightsPaths);
    }

    [Fact]
    public void Snapshot_Window_IsARequiredStringEnum_WithTheTwoWireValues()
    {
        var window = Parameter(StatsOperation(LoadSnapshot()).GetProperty("get"), "window");

        Assert.Equal("query", window.GetProperty("in").GetString());
        Assert.True(window.GetProperty("required").GetBoolean());
        var schema = window.GetProperty("schema");
        Assert.Equal("string", schema.GetProperty("type").GetString());
        Assert.Equal(["last90Days", "allTime"], schema.GetProperty("enum").EnumerateArray().Select(e => e.GetString()).ToArray());
    }

    [Fact]
    public void Snapshot_AsOf_IsARequiredStringWithFormatDate()
    {
        var asOf = Parameter(StatsOperation(LoadSnapshot()).GetProperty("get"), "asOf");

        Assert.Equal("query", asOf.GetProperty("in").GetString());
        Assert.True(asOf.GetProperty("required").GetBoolean());
        var schema = asOf.GetProperty("schema");
        Assert.Equal("string", schema.GetProperty("type").GetString());
        Assert.Equal("date", schema.GetProperty("format").GetString());
    }

    [Fact]
    public void Snapshot_Responses_Are200WithTheStatsDto_And400WithAValidationProblemDocument()
    {
        var responses = StatsOperation(LoadSnapshot()).GetProperty("get").GetProperty("responses");

        var ok = responses.GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema");
        Assert.EndsWith("/InsightsStatsDto", ok.GetProperty("$ref").GetString(), StringComparison.Ordinal);

        var bad = responses.GetProperty("400").GetProperty("content").GetProperty("application/problem+json").GetProperty("schema");
        Assert.EndsWith("/HttpValidationProblemDetails", bad.GetProperty("$ref").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Snapshot_StatsDto_HasTheContractFields_WithExplicitTypes()
    {
        var schemas = LoadSnapshot().GetProperty("components").GetProperty("schemas");
        var dto = schemas.GetProperty("InsightsStatsDto").GetProperty("properties");

        Assert.Equal("date", dto.GetProperty("asOf").GetProperty("format").GetString());
        var window = dto.GetProperty("window");
        Assert.Equal(["last90Days", "allTime"], window.GetProperty("enum").EnumerateArray().Select(e => e.GetString()).ToArray());
        Assert.Equal("date", NonNullFormat(dto.GetProperty("windowStart")));
        Assert.True(dto.TryGetProperty("responseTime", out _));
        Assert.True(dto.TryGetProperty("weeklyApplications", out _));
        Assert.True(dto.TryGetProperty("searchRunYield", out _));

        var responseTime = schemas.GetProperty("ResponseTimeDto").GetProperty("properties");
        Assert.True(responseTime.TryGetProperty("medianDays", out var median));
        Assert.Contains("number", median.GetRawText(), StringComparison.Ordinal);
        Assert.Contains("null", median.GetRawText(), StringComparison.Ordinal);
        Assert.Equal("integer", responseTime.GetProperty("sampleSize").GetProperty("type").GetString());

        var week = schemas.GetProperty("WeeklyApplicationsDto").GetProperty("properties");
        Assert.Equal("date", week.GetProperty("weekStart").GetProperty("format").GetString());
        Assert.Equal("integer", week.GetProperty("count").GetProperty("type").GetString());

        var yield = schemas.GetProperty("SearchRunYieldDto").GetProperty("properties");
        Assert.Equal("boolean", yield.GetProperty("truncated").GetProperty("type").GetString());
        var point = schemas.GetProperty("SearchRunYieldPointDto").GetProperty("properties");
        Assert.Equal("date-time", point.GetProperty("startedAt").GetProperty("format").GetString());
        Assert.Equal("integer", point.GetProperty("newPostings").GetProperty("type").GetString());
    }

    [Fact]
    public void GeneratedClientTypes_CarryTheStatsPath_TheWindowUnion_AndTheDtoFields()
    {
        var ts = File.ReadAllText(Path.Combine(RepoRoot(), "contracts", "client", "api-types.ts"));

        Assert.Contains($"\"{StatsPath}\"", ts, StringComparison.Ordinal);
        Assert.Contains("window: \"last90Days\" | \"allTime\"", ts, StringComparison.Ordinal);
        Assert.Matches(AsOfQueryParameter(), ts);
        Assert.Contains("InsightsStatsDto: {", ts, StringComparison.Ordinal);
        Assert.Contains("weeklyApplications: components[\"schemas\"][\"WeeklyApplicationsDto\"][]", ts, StringComparison.Ordinal);
        Assert.Contains("searchRunYield: components[\"schemas\"][\"SearchRunYieldDto\"]", ts, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratedClientTypes_ListEveryPathOfTheSnapshot()
    {
        var snapshot = LoadSnapshot();
        var ts = File.ReadAllText(Path.Combine(RepoRoot(), "contracts", "client", "api-types.ts"));

        foreach (var path in snapshot.GetProperty("paths").EnumerateObject().Select(p => p.Name))
        {
            Assert.True(
                ts.Contains($"\"{path}\"", StringComparison.Ordinal),
                $"contracts/client/api-types.ts is missing '{path}'. Run: pnpm --dir contracts run generate-client");
        }
    }

    private static string? NonNullFormat(JsonElement schema)
    {
        if (schema.TryGetProperty("format", out var format))
        {
            return format.GetString();
        }

        // A nullable date may be written as oneOf [{type: null}, {type: string, format: date}].
        foreach (var key in new[] { "oneOf", "anyOf" })
        {
            if (schema.TryGetProperty(key, out var options))
            {
                foreach (var option in options.EnumerateArray())
                {
                    if (option.TryGetProperty("format", out var f))
                    {
                        return f.GetString();
                    }
                }
            }
        }

        return null;
    }

    [GeneratedRegex(@"asOf: string;")]
    private static partial Regex AsOfQueryParameter();

    private static string RepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "RoleRadar.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root containing RoleRadar.sln.");
    }
}
