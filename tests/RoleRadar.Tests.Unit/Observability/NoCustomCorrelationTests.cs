using Xunit;

namespace RoleRadar.Tests.Unit.Observability;

/// <summary>
/// Feature 014 US2 (SC-007, ADR 0044): the shipped source has no custom correlation header, middleware or error envelope.
/// W3C Trace Context (<c>traceparent</c>) and RFC 9457 problem documents replaced them.
/// </summary>
public sealed class NoCustomCorrelationTests
{
    private static readonly string[] Forbidden =
    [
        "X-Correlation-ID",
        "CorrelationMiddleware",
        "ErrorEnvelope",
        "acceptCorrelationId",
        "newCorrelationId",
    ];

    [Fact]
    public void ShippedSource_HasNoCustomCorrelationOrErrorEnvelope()
    {
        var root = FindRepoRoot();
        var files = DotNetSources(root).Concat(WebSources(root)).ToList();

        // The scan must not pass vacuously: 50 .NET files and 30 web files at least.
        Assert.True(DotNetSources(root).Count() >= 50, "The scan read too few .NET source files.");
        Assert.True(WebSources(root).Count() >= 30, "The scan read too few web source files.");

        var hits = new List<string>();
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            hits.AddRange(Forbidden
                .Where(token => text.Contains(token, StringComparison.Ordinal))
                .Select(token => $"{Path.GetRelativePath(root, file)}: {token}"));
        }

        Assert.True(hits.Count == 0, "A custom correlation mechanism or error envelope remains:" + Environment.NewLine + string.Join(Environment.NewLine, hits));
    }

    private static IEnumerable<string> DotNetSources(string root)
    {
        var src = Path.Combine(root, "src");
        return Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
            {
                var relative = Path.GetRelativePath(src, path).Replace('\\', '/');
                return !relative.Contains("/obj/", StringComparison.Ordinal) && !relative.Contains("/bin/", StringComparison.Ordinal);
            });
    }

    private static IEnumerable<string> WebSources(string root)
    {
        var web = Path.Combine(root, "web");
        var app = Path.Combine(web, "app");
        var files = Directory.EnumerateFiles(app, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".ts", StringComparison.Ordinal) || path.EndsWith(".tsx", StringComparison.Ordinal))
            .Where(path => !Path.GetRelativePath(app, path).Replace('\\', '/').StartsWith("ds/", StringComparison.Ordinal));

        var server = Path.Combine(web, "server.ts");
        return File.Exists(server) ? files.Append(server) : files;
    }

    private static string FindRepoRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "RoleRadar.sln")) || Directory.Exists(Path.Combine(current, ".git")))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new InvalidOperationException("Could not find the repository root.");
    }
}
