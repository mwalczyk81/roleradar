using System.Text.RegularExpressions;
using RoleRadar.Cli.Demo;
using RoleRadar.Infrastructure.Mongo;
using Xunit;

namespace RoleRadar.Tests.Unit.Demo;

/// <summary>
/// The demo exists only in its own compose project. The default compose files, the shipped images' default
/// configuration and every committed file carry no demo account, password or TOTP secret (FR-002, FR-007, FR-007c,
/// FR-008, SC-003). Failures name files only, never matched text.
/// </summary>
public sealed partial class DemoAbsentFromDefaultsTests
{
    private static readonly string Root = FindRepoRoot();

    private static readonly string[] SkippedFolders =
    [
        "bin", "obj", "node_modules", ".git", "StrykerOutput", "TestResults", ".pnpm-store", ".nx", "dist", "mongodump_bin", "build", "reports", "allure-results"
    ];

    private static readonly string[] ShippedProjects = ["RoleRadar.Identity", "RoleRadar.Api", "RoleRadar.Cli", "RoleRadar.Mcp.Stdio"];

    private static string DemoCompose => Path.Combine(Root, "deploy", "demo", "docker-compose.yml");

    private static IEnumerable<string> DemoMarkers() =>
    [
        DemoAccount.Username, DemoAccount.UserId, DemoModeGuard.DemoDatabaseName, DemoModeGuard.DemoModeVariable, DemoModeGuard.DemoMongoHost
    ];

    private static List<string> DefaultConfigurationFiles()
    {
        var files = new List<string>
        {
            Path.Combine(Root, "deploy", "docker-compose.yml"),
            Path.Combine(Root, "deploy", "docker-compose.apps.yml"),
            Path.Combine(Root, "Dockerfile")
        };
        files.AddRange(Directory.EnumerateFiles(Path.Combine(Root, "deploy"), "Dockerfile.*"));
        foreach (var project in ShippedProjects)
        {
            files.AddRange(Walk(Path.Combine(Root, "src", project)).Where(f => Path.GetFileName(f).StartsWith("appsettings", StringComparison.OrdinalIgnoreCase)));
        }

        return files;
    }

    [Fact]
    public void DefaultComposeFilesAndImageConfiguration_ContainNoDemoMarker()
    {
        var files = DefaultConfigurationFiles().ToArray();
        Assert.True(files.Length >= 5, "Expected the default compose files, Dockerfiles and appsettings to be found.");

        var offenders = files
            .Where(File.Exists)
            .Where(f => DemoMarkers().Any(m => File.ReadAllText(f).Contains(m, StringComparison.OrdinalIgnoreCase)))
            .Select(f => Path.GetRelativePath(Root, f).Replace('\\', '/'))
            .ToArray();

        Assert.True(offenders.Length == 0, $"Demo markers found in default configuration: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void NoCommittedFile_HoldsADemoTotpSecret()
    {
        var offenders = Walk(Root)
            .Where(IsTextFile)
            .Where(f => DemoTotpSecret().IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(Root, f).Replace('\\', '/'))
            .ToArray();

        Assert.True(offenders.Length == 0, $"A demo TOTP secret is committed in: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void NoFileUnderTheDemoPaths_HoldsASecretLikeValue()
    {
        var demoPaths = new[]
        {
            Path.Combine(Root, "deploy", "demo"),
            Path.Combine(Root, "src", "RoleRadar.Cli", "Demo"),
            Path.Combine(Root, "docs", "demo.md")
        };

        var files = demoPaths.SelectMany(p => Directory.Exists(p) ? Walk(p) : File.Exists(p) ? [p] : []).Where(IsTextFile).Where(f => !f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.NotEmpty(files);

        var offenders = files
            .Where(f => SecretAssignment().IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(Root, f).Replace('\\', '/'))
            .ToArray();

        Assert.True(offenders.Length == 0, $"Secret-like values found in: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void DemoAccountConstants_AreTheOnlyDemoValuesInSource_AndHoldNoSecret()
    {
        var file = File.ReadAllText(Path.Combine(Root, "src", "RoleRadar.Cli", "Demo", "DemoAccount.cs"));

        Assert.DoesNotMatch(SecretAssignment(), file);
        Assert.Equal("demo-owner", DemoAccount.Username);
        Assert.True(Guid.TryParseExact(DemoAccount.UserId, "D", out var id));
        Assert.Equal(7, id.Version);
    }

    // ---- the demo compose file ----

    [Fact]
    public void DemoCompose_IsStandaloneAndCannotReachTheRealStack()
    {
        Assert.True(File.Exists(DemoCompose), "Expected deploy/demo/docker-compose.yml.");
        var text = File.ReadAllText(DemoCompose);

        Assert.Matches(@"(?m)^name:\s*roleradar-demo\s*$", text);
        Assert.DoesNotContain("roleradar-mongo", text, StringComparison.Ordinal);
        Assert.DoesNotContain("container_name", text, StringComparison.Ordinal);
        Assert.DoesNotContain("external: true", text, StringComparison.Ordinal);
        Assert.DoesNotContain("docker-compose.apps.yml", text, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"(?m)^\s*-\s*[""']?(\S*:)?27017:", text);
        Assert.DoesNotMatch(@"(?m)ROLERADAR_MONGO_DATABASE[=:]\s*[""']?roleradar[""']?\s*$", text);
    }

    [Fact]
    public void DemoCompose_PublishesLoopbackPortsOnly_AndMongoPublishesNone()
    {
        var text = File.ReadAllText(DemoCompose);

        foreach (Match port in PublishedPort().Matches(text))
        {
            Assert.StartsWith("127.0.0.1:", port.Groups[1].Value, StringComparison.Ordinal);
        }

        var mongo = ServiceBlock(text, DemoModeGuard.DemoMongoHost);
        Assert.NotNull(mongo);
        Assert.DoesNotContain("ports:", mongo, StringComparison.Ordinal);
    }

    [Fact]
    public void DemoCompose_MongoServiceNameEqualsTheGuardsHostConstant()
    {
        var text = File.ReadAllText(DemoCompose);

        Assert.NotNull(ServiceBlock(text, DemoModeGuard.DemoMongoHost));
        Assert.Matches($@"(?m){DemoModeGuard.DemoModeVariable}\s*[:=]\s*""?1""?\s*$", text);
        Assert.Matches($@"(?m)ROLERADAR_MONGO_DATABASE\s*[:=]\s*""?{DemoModeGuard.DemoDatabaseName}""?\s*$", text);
        Assert.Contains($"mongodb://{DemoModeGuard.DemoMongoHost}:27017", text, StringComparison.Ordinal);
    }

    [Fact]
    public void DemoCompose_NamesOnlyDemoVolumes_AndSetsTheFixedNonSecretSubject()
    {
        var text = File.ReadAllText(DemoCompose);

        foreach (Match volume in VolumeName().Matches(text))
        {
            Assert.StartsWith("roleradar-demo-", volume.Groups[1].Value, StringComparison.Ordinal);
        }

        Assert.Contains(DemoAccount.UserId, text, StringComparison.Ordinal);
    }

    [Fact]
    public void DemoCompose_DefinesTheCertificateSecretsAndToolsServices()
    {
        var text = File.ReadAllText(DemoCompose);

        foreach (var service in new[] { "demo-certs", "demo-secrets", "demo-seed", "demo-cli", "identity", "api", "web" })
        {
            Assert.NotNull(ServiceBlock(text, service));
        }

        Assert.Contains("profiles: [\"tools\"]", ServiceBlock(text, "demo-cli")!, StringComparison.Ordinal);
    }

    private static string? ServiceBlock(string compose, string service)
    {
        var match = Regex.Match(compose, $@"(?ms)^  {Regex.Escape(service)}:\s*\n(.*?)(?=^  [A-Za-z0-9_-]+:\s*\n|^[A-Za-z]|\z)");
        return match.Success ? match.Value : null;
    }

    private static IEnumerable<string> Walk(string folder)
    {
        if (!Directory.Exists(folder))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(folder))
        {
            yield return file;
        }

        foreach (var sub in Directory.EnumerateDirectories(folder))
        {
            if (SkippedFolders.Contains(Path.GetFileName(sub), StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var file in Walk(sub))
            {
                yield return file;
            }
        }
    }

    private static bool IsTextFile(string file)
    {
        var extension = Path.GetExtension(file).ToLowerInvariant();
        return extension is ".md" or ".cs" or ".yml" or ".yaml" or ".json" or ".ts" or ".tsx" or ".mjs" or ".js" or ".ps1" or ".sh" or ".props" or ".csproj" or ".txt" or ".toml" or ".env"
               && new FileInfo(file).Length < 2_000_000;
    }

    // otpauth URI for the demo user with a real-looking Base32 secret. Sample secrets in tests use another user name.
    [GeneratedRegex(@"otpauth://totp/RoleRadar:demo-owner\?secret=[A-Z2-7]{16,}")]
    private static partial Regex DemoTotpSecret();

    // A password, secret or key assigned a literal that is not a variable, path, placeholder or code reference.
    [GeneratedRegex(@"(?im)\b(password|passwd|secret|totp[_ ]?key|session[_-]?encryption[_-]?key)\b[""']?\s*[:=]\s*[""']?(?![$/<{(@%])(?!generated)(?!printed)(?!the\b)[A-Za-z0-9+/=_\-!@#%^*]{12,}")]
    private static partial Regex SecretAssignment();

    [GeneratedRegex(@"(?m)^\s*-\s*[""']?([0-9.]+:\d+:\d+|\d+:\d+)[""']?\s*$")]
    private static partial Regex PublishedPort();

    [GeneratedRegex(@"(?m)^[ \t]+name:[ \t]*([\w-]+)[ \t]*$")]
    private static partial Regex VolumeName();

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
