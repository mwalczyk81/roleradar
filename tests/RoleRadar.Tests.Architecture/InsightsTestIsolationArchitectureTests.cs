namespace RoleRadar.Tests.Architecture;

using Xunit;

/// <summary>
/// 007: the Insights integration tests use a fictional seed and must never read the developer's configuration or
/// reach a real database. Type-based rules cannot see this, so the folder is scanned as source.
/// </summary>
public sealed class InsightsTestIsolationArchitectureTests
{
    private static readonly string[] ForbiddenTokens =
    [
        "MongoConnectionStringSource",
        "UserSecrets",
        "GetEnvironmentVariable",
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
