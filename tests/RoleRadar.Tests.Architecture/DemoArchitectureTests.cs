namespace RoleRadar.Tests.Architecture;

using System.Text.RegularExpressions;
using Xunit;

/// <summary>
/// Placement rules for the demo (feature 012, ADR 0039). They scan source text, so they hold without loading the
/// hosts. Sentinels fail when a guarded folder or file is missing, so no rule can pass because the code is absent.
/// </summary>
public sealed partial class DemoArchitectureTests
{
    private static readonly string Root = LocateRepositoryRoot();

    // Literals that identify the demo. Production code outside the allowed places must use the guard's constants.
    private static readonly string[] DemoLiterals = ["roleradar-demo", "demo-owner", "demo-mongo", "ROLERADAR_DEMO"];

    private static string Src(params string[] parts) => Path.Combine([Root, "src", .. parts]);

    private static string RelativePath(string file) => Path.GetRelativePath(Root, file).Replace('\\', '/');

    private static IEnumerable<string> SourceFiles(string folder) =>
        Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static IEnumerable<string> ProductionFiles() => SourceFiles(Path.Combine(Root, "src"));

    private static bool IsAllowedForDemoConstants(string file)
    {
        var rel = RelativePath(file);
        return rel.StartsWith("src/RoleRadar.Cli/Demo/", StringComparison.Ordinal)
               || rel == "src/RoleRadar.Infrastructure/Mongo/DemoModeGuard.cs";
    }

    // ---- sentinels ----

    [Fact]
    public void Sentinel_TheGuardFileExists()
    {
        Assert.True(File.Exists(Src("RoleRadar.Infrastructure", "Mongo", "DemoModeGuard.cs")),
            "Expected src/RoleRadar.Infrastructure/Mongo/DemoModeGuard.cs.");
    }

    [Fact]
    public void Sentinel_TheCliDemoFolderIsNotEmpty()
    {
        var folder = Src("RoleRadar.Cli", "Demo");
        Assert.True(Directory.Exists(folder) && SourceFiles(folder).Any(),
            "Expected demo code under src/RoleRadar.Cli/Demo/.");
    }

    // ---- placement ----

    [Fact]
    public void TheGuard_LivesInInfrastructure()
    {
        var assembly = typeof(RoleRadar.Infrastructure.Mongo.DemoModeGuard).Assembly.GetName().Name;
        Assert.Equal("RoleRadar.Infrastructure", assembly);
    }

    [Theory]
    [InlineData("RoleRadar.Domain")]
    [InlineData("RoleRadar.Application")]
    public void DomainAndApplication_ContainNoDemoCode(string project)
    {
        var offenders = SourceFiles(Src(project))
            .Where(f => DemoWord().IsMatch(File.ReadAllText(f))
                        || DemoLiterals.Any(l => File.ReadAllText(f).Contains(l, StringComparison.Ordinal)))
            .Select(RelativePath)
            .ToArray();

        Assert.True(offenders.Length == 0, $"{project} must hold no demo code. Offending files: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void NoProductionProject_ReferencesATestProject()
    {
        var offenders = Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(f => ProjectReferenceToTests().IsMatch(File.ReadAllText(f)))
            .Select(RelativePath)
            .ToArray();

        Assert.True(offenders.Length == 0, $"Production projects must not reference test projects: {string.Join(", ", offenders)}");
    }

    // ---- guard callers ----

    [Fact]
    public void ProductionCode_NeverCallsTheExplicitHostOverloadOfTheGuard()
    {
        var offenders = ProductionFiles()
            .Where(f => RelativePath(f) != "src/RoleRadar.Infrastructure/Mongo/DemoModeGuard.cs")
            .Where(f => ExplicitHostCall().IsMatch(File.ReadAllText(f)))
            .Select(RelativePath)
            .ToArray();

        Assert.True(offenders.Length == 0,
            $"Production callers must use CheckForDemoStack or CheckServe (the fixed demo host). Offending files: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void OnlyTheGuardTestsAndTheIntegrationSeedTest_CallTheExplicitHostOverload()
    {
        var allowedPrefixes = new[]
        {
            "tests/RoleRadar.Tests.Unit/Demo/",
            "tests/RoleRadar.Tests.Integration/Demo/",
            "tests/RoleRadar.Tests.Architecture/DemoArchitectureTests.cs"
        };

        var offenders = SourceFiles(Path.Combine(Root, "tests"))
            .Where(f => ExplicitHostCall().IsMatch(File.ReadAllText(f)))
            .Select(RelativePath)
            .Where(rel => !allowedPrefixes.Any(p => rel.StartsWith(p, StringComparison.Ordinal)))
            .ToArray();

        Assert.True(offenders.Length == 0, $"Only the guard tests and the demo seed test may pass their own host: {string.Join(", ", offenders)}");
    }

    // ---- constants ----

    [Fact]
    public void DemoLiterals_AppearInProductionCodeOnlyInCliDemoAndTheGuard()
    {
        var offenders = ProductionFiles()
            .Where(f => !IsAllowedForDemoConstants(f))
            .Where(f =>
            {
                var text = File.ReadAllText(f);
                return DemoLiterals.Any(l => text.Contains($"\"{l}\"", StringComparison.Ordinal));
            })
            .Select(RelativePath)
            .ToArray();

        Assert.True(offenders.Length == 0,
            $"Demo constants must be referenced only from src/RoleRadar.Cli/Demo/ and the guard. Offending files: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void DemoAccountAndSeedTypes_AreReferencedOnlyFromCliDemo()
    {
        var offenders = ProductionFiles()
            .Where(f => !RelativePath(f).StartsWith("src/RoleRadar.Cli/Demo/", StringComparison.Ordinal))
            .Where(f => DemoAccountTypes().IsMatch(File.ReadAllText(f)))
            .Select(RelativePath)
            .ToArray();

        Assert.True(offenders.Length == 0, $"Demo account and seed types must stay in src/RoleRadar.Cli/Demo/. Offending files: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void CliDemo_HasNoStringLiteralOwnerIds()
    {
        var folder = Src("RoleRadar.Cli", "Demo");
        Assert.True(Directory.Exists(folder), "Expected src/RoleRadar.Cli/Demo/.");

        var offenders = new List<string>();
        foreach (var file in SourceFiles(folder))
        {
            var rel = RelativePath(file);
            foreach (Match literal in StringLiteral().Matches(File.ReadAllText(file)))
            {
                var value = literal.Groups[1].Value;
                var mentionsOwner = value.Contains("owner", StringComparison.OrdinalIgnoreCase);
                var isTheDocumentedUsername = value == "demo-owner" && rel.EndsWith("/DemoAccount.cs", StringComparison.Ordinal);
                if (value == OwnerLiteralDefault || (mentionsOwner && !isTheDocumentedUsername && !IsProse(value)))
                {
                    offenders.Add($"{rel}: \"{value}\"");
                }
            }

            if (OwnerIdWithLiteral().IsMatch(File.ReadAllText(file)))
            {
                offenders.Add($"{rel}: OwnerId built from a string literal");
            }
        }

        Assert.True(offenders.Count == 0, $"Cli/Demo must take owner ids from OwnerId only. Offenders: {string.Join("; ", offenders)}");
    }

    // ---- credentials code ----

    [Fact]
    public void CliDemo_UsesNoLogger()
    {
        var folder = Src("RoleRadar.Cli", "Demo");
        Assert.True(Directory.Exists(folder), "Expected src/RoleRadar.Cli/Demo/.");

        var offenders = SourceFiles(folder)
            .Where(f => LoggerUse().IsMatch(File.ReadAllText(f)))
            .Select(RelativePath)
            .ToArray();

        Assert.True(offenders.Length == 0,
            $"Demo code prints through the console only; a secret must never reach a logger. Offending files: {string.Join(", ", offenders)}");
    }

    // The default owner literal; kept as a constant so this file does not itself look like an offender to readers.
    private const string OwnerLiteralDefault = "local-owner";

    // A literal with spaces is an English message, not an identifier.
    private static bool IsProse(string value) => value.Contains(' ', StringComparison.Ordinal);

    // InternalsVisibleTo entries name test assemblies without referencing them; only a ProjectReference counts.
    [GeneratedRegex("<ProjectReference[^>]*Tests", RegexOptions.IgnoreCase)]
    private static partial Regex ProjectReferenceToTests();

    [GeneratedRegex(@"\bDemo[A-Z][A-Za-z]*")]
    private static partial Regex DemoWord();

    [GeneratedRegex(@"DemoModeGuard\s*\.\s*Check\s*\(")]
    private static partial Regex ExplicitHostCall();

    [GeneratedRegex(@"\b(DemoAccount|DemoSeeder|DemoDataset|CredentialPrinter|CredentialGenerator)\b")]
    private static partial Regex DemoAccountTypes();

    [GeneratedRegex("\"((?:[^\"\\\\\\r\\n]|\\\\.)*)\"")]
    private static partial Regex StringLiteral();

    [GeneratedRegex("new\\s+OwnerId\\s*\\(\\s*\"")]
    private static partial Regex OwnerIdWithLiteral();

    [GeneratedRegex(@"\bILogger(Factory|Provider)?\b|\bLogInformation\b|\bLogWarning\b|\bLogError\b|\bLogDebug\b|\bLogTrace\b|\bLogCritical\b|Microsoft\.Extensions\.Logging")]
    private static partial Regex LoggerUse();

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

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
