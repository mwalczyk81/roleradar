using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace RoleRadar.Tests.Unit.Demo;

/// <summary>
/// The demo mounts its generated client secrets as <c>appsettings.Demo.json</c> and runs Identity with
/// <c>ASPNETCORE_ENVIRONMENT=Demo</c> (research R-014). That is only safe if "Demo" takes the same security-relevant
/// code paths as "Production". These tests pin the one place where environment names matter: the only environment
/// predicate in the hosts is <c>IsDevelopment()</c> in the API (HTTPS metadata and the developer-only endpoints), and
/// "Demo" is not "Development". Static scan: it proves no other branch exists, not the behaviour of a running host.
/// </summary>
public sealed partial class DemoEnvironmentParityTests
{
    private static readonly string Root = FindRepoRoot();

    private static readonly string[] ScannedProjects = ["RoleRadar.Api", "RoleRadar.Identity", "RoleRadar.Infrastructure", "RoleRadar.Application", "RoleRadar.Cli"];

    private static readonly string[] HostProjects = ["RoleRadar.Api", "RoleRadar.Identity", "RoleRadar.Cli"];

    private static IEnumerable<string> SourceFiles(string project) =>
        Directory.EnumerateFiles(Path.Combine(Root, "src", project), "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".cshtml", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    [Fact]
    public void DemoIsNotDevelopment()
    {
        Assert.NotEqual("Demo", Environments.Development);
        Assert.False(new FakeEnvironment("Demo").IsDevelopment());
        Assert.False(new FakeEnvironment("Demo").IsProduction());
    }

    [Fact]
    public void TheOnlyEnvironmentPredicates_AreTheTwoIsDevelopmentChecksInTheApi()
    {
        var hits = new List<string>();
        foreach (var project in ScannedProjects)
        {
            foreach (var file in SourceFiles(project))
            {
                var rel = Path.GetRelativePath(Root, file).Replace('\\', '/');
                foreach (Match match in EnvironmentPredicate().Matches(File.ReadAllText(file)))
                {
                    hits.Add($"{rel}:{match.Value}");
                }
            }
        }

        // Exactly the two known checks, both IsDevelopment() in the API. Anything else is a new environment branch
        // that must be reviewed against the Demo environment before it ships.
        Assert.Equal(2, hits.Count);
        Assert.All(hits, h =>
        {
            Assert.StartsWith("src/RoleRadar.Api/Program.cs:", h, StringComparison.Ordinal);
            Assert.Contains("IsDevelopment", h, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void NoHostBranchesOnTheLiteralEnvironmentName()
    {
        var offenders = HostProjects
            .SelectMany(SourceFiles)
            .Where(f => EnvironmentNameLiteral().IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(Root, f).Replace('\\', '/'))
            .ToArray();

        Assert.True(offenders.Length == 0, $"Hosts must not branch on an environment name: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void WebBranchesOnlyOnDevelopmentAndTest_SoProductionAndDemoTakeTheSamePaths()
    {
        var offenders = new List<string>();
        foreach (var file in new[] { "web/app/lib/oidc.server.ts", "web/app/lib/session.server.ts", "web/server.ts" })
        {
            var text = File.ReadAllText(Path.Combine(Root, file.Replace('/', Path.DirectorySeparatorChar)));
            foreach (Match match in NodeEnvComparison().Matches(text))
            {
                if (match.Groups[1].Value is not ("development" or "test"))
                {
                    offenders.Add($"{file}:{match.Groups[1].Value}");
                }
            }
        }

        Assert.True(offenders.Count == 0, $"Unexpected NODE_ENV branches: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void DemoCompose_RunsWebAsProduction_AndNeverSetsDevelopment()
    {
        var path = Path.Combine(Root, "deploy", "demo", "docker-compose.yml");
        Assert.True(File.Exists(path), "Expected deploy/demo/docker-compose.yml.");
        var text = File.ReadAllText(path);

        Assert.Matches(@"(?m)NODE_ENV\s*[:=]\s*""?production""?\s*$", text);
        Assert.Matches(@"(?m)ASPNETCORE_ENVIRONMENT\s*[:=]\s*""?Demo""?\s*$", text);
        Assert.DoesNotContain("Development", text, StringComparison.Ordinal);
        Assert.DoesNotContain("DOTNET_ENVIRONMENT", text, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"\.(IsDevelopment|IsProduction|IsStaging|IsEnvironment)\s*\(")]
    private static partial Regex EnvironmentPredicate();

    [GeneratedRegex(@"EnvironmentName\s*(==|!=|\.Equals)|IsEnvironment\s*\(\s*""|""(Production|Staging|Development)""")]
    private static partial Regex EnvironmentNameLiteral();

    [GeneratedRegex(@"process\.env\.NODE_ENV\s*[!=]==\s*""([A-Za-z]+)""")]
    private static partial Regex NodeEnvComparison();

    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "scratch";

        public string ContentRootPath { get; set; } = ".";

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RoleRadar.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir.FullName;
    }
}
