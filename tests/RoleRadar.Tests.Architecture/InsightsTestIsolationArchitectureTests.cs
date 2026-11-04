namespace RoleRadar.Tests.Architecture;

using System.Text.RegularExpressions;
using Xunit;

/// <summary>
/// 007: the Insights integration tests use a fictional seed and must never read the developer's configuration or
/// reach a real database. Type-based rules cannot see this, so the folder is scanned as source.
/// </summary>
public sealed partial class InsightsTestIsolationArchitectureTests
{
    // The only environment variable the Insights tests may read: it switches snapshot regeneration on.
    private static readonly string[] AllowedEnvironmentVariables = ["UPDATE_SNAPSHOTS"];

    private static readonly string[] ForbiddenTokens =
    [
        "MongoConnectionStringSource",
        "UserSecrets",
        "AddUserSecrets",
        "AddEnvironmentVariables",
        "appsettings",
        ".env.local",
        "deploy/.env"
    ];

    private static readonly string[] ExpectedSupportFiles =
    [
        "InsightsSeedGuard.cs",
        "InsightsSeed.cs",
        "InsightsOracle.cs",
        "InsightsFixture.cs"
    ];

    [Fact]
    public void InsightsSupportFiles_Exist()
    {
        var folder = InsightsFolder();
        foreach (var expected in ExpectedSupportFiles)
        {
            Assert.True(File.Exists(Path.Combine(folder, expected)), $"Expected tests/RoleRadar.Tests.Integration/Insights/{expected}");
        }
    }

    [Fact]
    public void InsightsTests_NeverTouchConnectionStringSourcesOrAmbientConfiguration()
    {
        var folder = InsightsFolder();
        var files = Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories).ToArray();
        Assert.NotEmpty(files);

        var offenders = new List<string>();
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var token in ForbiddenTokens)
            {
                if (text.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    offenders.Add($"{Path.GetFileName(file)} mentions '{token}'");
                }
            }
        }

        Assert.True(offenders.Count == 0, "Insights tests must only use their own Testcontainers database:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void InsightsTests_ReadNoEnvironmentVariableExceptTheSnapshotSwitch()
    {
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(InsightsFolder(), "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            foreach (Match call in EnvironmentRead().Matches(text))
            {
                var literal = call.Groups[1].Success ? call.Groups[1].Value : null;
                if (literal is null || !AllowedEnvironmentVariables.Contains(literal, StringComparer.Ordinal))
                {
                    offenders.Add($"{Path.GetFileName(file)} reads an environment variable other than UPDATE_SNAPSHOTS ({call.Value})");
                }
            }
        }

        Assert.True(offenders.Count == 0, string.Join("\n", offenders));
    }

    // GetEnvironmentVariable("NAME") or any other form (GetEnvironmentVariables, a non-literal argument).
    [GeneratedRegex("GetEnvironmentVariable(?:s)?\\(\\s*(?:\"([^\"]+)\")?")]
    private static partial Regex EnvironmentRead();

    private static string InsightsFolder() =>
        Path.Combine(LocateRepositoryRoot(), "tests", "RoleRadar.Tests.Integration", "Insights");

    private static string LocateRepositoryRoot()
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
