using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace RoleRadar.Tests.Integration.Hosting;

/// <summary>
/// Docker-free guards for the container scan evaluation (feature 010, contracts/scan-report-evaluation.md). Reports are
/// built in code from fictional ids; nothing here touches Docker, the repository's TestResults folder or any real finding.
/// </summary>
[Trait("Lane", "default")]
public sealed class TrivyReportEvaluatorTests : IDisposable
{
    private static readonly IReadOnlySet<string> CriticalOnly = TrivyScanPolicy.Parse("CRITICAL");

    private static readonly IReadOnlySet<string> HighAndCritical = TrivyScanPolicy.Parse("HIGH,CRITICAL");

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rr-scan-eval-" + Guid.NewGuid().ToString("N"));

    public TrivyReportEvaluatorTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static string Report(string target, string severity, string id = "CVE-0000-0001") =>
        "{\"SchemaVersion\":2,\"ArtifactName\":\"roleradar-" + target + ":test\",\"ArtifactType\":\"container_image\","
        + "\"Results\":[{\"Target\":\"fictional-layer\",\"Class\":\"lang-pkgs\",\"Type\":\"fictional\","
        + "\"Vulnerabilities\":[{\"VulnerabilityID\":\"" + id + "\",\"PkgName\":\"fictional-lib\",\"InstalledVersion\":\"1.0.0\","
        + "\"FixedVersion\":\"1.0.1\",\"Severity\":\"" + severity + "\"}]}]}";

    private static string Clean(string target) =>
        "{\"SchemaVersion\":2,\"ArtifactName\":\"roleradar-" + target + ":test\",\"ArtifactType\":\"container_image\",\"Results\":[]}";

    private void WriteAll(Func<string, string> content)
    {
        foreach (var target in TrivyReportEvaluator.Targets)
        {
            WriteOne(target, content(target));
        }
    }

    private void WriteOne(string target, string content) =>
        File.WriteAllText(Path.Combine(_dir, $"trivy-report-{target}.json"), content, new UTF8Encoding(false));

    private string[] Files() => [.. Directory.GetFiles(_dir, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)];

    [Fact]
    public void AHighReport_PassesUnderCritical_AndFailsUnderHighAndCritical()
    {
        WriteAll(Clean);
        WriteOne("cli", Report("cli", "HIGH"));

        var underCritical = TrivyReportEvaluator.Evaluate(_dir, CriticalOnly, requireReports: true);
        var underBoth = TrivyReportEvaluator.Evaluate(_dir, HighAndCritical, requireReports: true);

        Assert.Equal(ScanOutcome.Pass, underCritical.Outcome);
        Assert.Equal(ScanOutcome.Fail, underBoth.Outcome);
        Assert.Contains("CVE-0000-0001", underBoth.Message);
    }

    [Fact]
    public void ACriticalReport_FailsUnderBothPolicies_AndNamesTheFinding()
    {
        WriteAll(Clean);
        WriteOne("web", Report("web", "CRITICAL", "CVE-0000-0002"));

        foreach (var policy in new[] { CriticalOnly, HighAndCritical })
        {
            var verdict = TrivyReportEvaluator.Evaluate(_dir, policy, requireReports: true);
            Assert.Equal(ScanOutcome.Fail, verdict.Outcome);
            Assert.Contains("CVE-0000-0002", verdict.Message);
            Assert.Contains("web", verdict.Message);
        }
    }

    [Fact]
    public void HighAndCriticalCounts_AreAlwaysReportedPerImage_EvenWhenTheVerdictIsPass()
    {
        WriteAll(Clean);
        WriteOne("cli", Report("cli", "HIGH"));

        var verdict = TrivyReportEvaluator.Evaluate(_dir, CriticalOnly, requireReports: true);

        Assert.Equal(ScanOutcome.Pass, verdict.Outcome);
        Assert.Equal(TrivyReportEvaluator.Targets.Length, verdict.Counts.Count);
        var cli = verdict.Counts.Single(c => c.Target == "cli");
        Assert.Equal(1, cli.High);
        Assert.Equal(0, cli.Critical);
        Assert.All(verdict.Counts.Where(c => c.Target != "cli"), c => Assert.Equal((0, 0), (c.High, c.Critical)));
        Assert.Contains("cli", verdict.Message);
        Assert.Contains("HIGH=1", verdict.Message);
    }

    [Fact]
    public void ACleanSetOfReports_Passes_IncludingAnEmptyResultsArray()
    {
        WriteAll(Clean);

        var verdict = TrivyReportEvaluator.Evaluate(_dir, CriticalOnly, requireReports: true);

        Assert.Equal(ScanOutcome.Pass, verdict.Outcome);
    }

    [Fact]
    public void MissingReports_Fail_WhenRequired_AndNameThePath()
    {
        var verdict = TrivyReportEvaluator.Evaluate(_dir, CriticalOnly, requireReports: true);

        Assert.Equal(ScanOutcome.Fail, verdict.Outcome);
        Assert.Contains(Path.Combine(_dir, "trivy-report-cli.json"), verdict.Message);
    }

    [Fact]
    public void MissingReports_Skip_WithAnExplicitReason_WhenNotRequired()
    {
        var verdict = TrivyReportEvaluator.Evaluate(_dir, CriticalOnly, requireReports: false);

        Assert.Equal(ScanOutcome.Skip, verdict.Outcome);
        Assert.Contains("no Trivy report", verdict.Message);
    }

    [Fact]
    public void APartialSetNeverPasses_WhenNotRequiredItSkipsNamingWhatIsMissing()
    {
        WriteOne("cli", Clean("cli"));

        var verdict = TrivyReportEvaluator.Evaluate(_dir, CriticalOnly, requireReports: false);

        Assert.Equal(ScanOutcome.Skip, verdict.Outcome);
        Assert.Contains("mcp", verdict.Message);
    }

    [Fact]
    public void AFindingInAPartialSet_StillFails_EvenWhenNotRequired()
    {
        WriteOne("cli", Report("cli", "CRITICAL"));

        var verdict = TrivyReportEvaluator.Evaluate(_dir, CriticalOnly, requireReports: false);

        Assert.Equal(ScanOutcome.Fail, verdict.Outcome);
    }

    [Fact]
    public void NoCase_WritesOrChangesAnyFile()
    {
        var before = Files();
        TrivyReportEvaluator.Evaluate(_dir, CriticalOnly, requireReports: false);
        TrivyReportEvaluator.Evaluate(_dir, CriticalOnly, requireReports: true);
        Assert.Equal(before, Files());

        WriteAll(Clean);
        var withReports = Files();
        var bytes = withReports.Select(File.ReadAllBytes).ToArray();
        TrivyReportEvaluator.Evaluate(_dir, HighAndCritical, requireReports: true);
        Assert.Equal(withReports, Files());
        Assert.Equal(bytes, Files().Select(File.ReadAllBytes).ToArray());
    }

    [Theory]
    [InlineData("this is not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"Results\":[]}")]
    public void MalformedReports_Fail(string content)
    {
        WriteAll(Clean);
        WriteOne("mcp", content);

        var verdict = TrivyReportEvaluator.Evaluate(_dir, CriticalOnly, requireReports: false);

        Assert.Equal(ScanOutcome.Fail, verdict.Outcome);
        Assert.Contains("mcp", verdict.Message);
    }

    [Fact]
    public void AReportForADifferentImage_Fails()
    {
        WriteAll(Clean);
        WriteOne("cli", Clean("mcp"));

        var verdict = TrivyReportEvaluator.Evaluate(_dir, CriticalOnly, requireReports: true);

        Assert.Equal(ScanOutcome.Fail, verdict.Outcome);
        Assert.Contains("roleradar-mcp", verdict.Message);
    }

    [Fact]
    public void TheImageTag_IsIgnored_SoCiLocalAndGateReviewTagsBothMatch()
    {
        foreach (var tag in new[] { "local", "review-0123456789abcdef", "test" })
        {
            WriteAll(target => Clean(target).Replace(":test", ":" + tag, StringComparison.Ordinal));
            Assert.Equal(ScanOutcome.Pass, TrivyReportEvaluator.Evaluate(_dir, CriticalOnly, requireReports: true).Outcome);
        }
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("", "", false)]
    [InlineData("true", null, true)]
    [InlineData(null, "1", true)]
    [InlineData("", "0", false)]
    public void ReportsAreRequired_InCi_OrWithTheRequireFlag(string? ci, string? requireScan, bool expected)
    {
        string? Env(string name) => name switch
        {
            "CI" => ci,
            "ROLERADAR_REQUIRE_SCAN" => requireScan,
            _ => null
        };

        Assert.Equal(expected, TrivyScanPolicy.RequireReports(Env));
    }

    [Fact]
    public void ThePolicyIsCritical_ByOwnerDecision_AndParsesCommaLists()
    {
        Assert.Equal("CRITICAL", TrivyScanPolicy.FailingSeverities);
        Assert.Equal(["CRITICAL"], TrivyScanPolicy.Parse("CRITICAL").Order().ToArray());
        Assert.Equal(["CRITICAL", "HIGH"], TrivyScanPolicy.Parse("high, CRITICAL").Order().ToArray());
    }

    [Fact]
    public void EverySeverityFlag_InCiAndTheGate_EqualsTheEvaluatorPolicy()
    {
        var root = ContainerTestHarness.FindRepoRoot();
        var files = new[]
        {
            Path.Combine(root, ".github", "workflows", "ci.yml"),
            Path.Combine(root, "scripts", "review-gate.ps1")
        };

        var expected = TrivyScanPolicy.Parse(TrivyScanPolicy.FailingSeverities).Order().ToArray();
        var found = new List<(string File, string Value)>();
        foreach (var file in files)
        {
            Assert.True(File.Exists(file), $"{file} is missing");
            foreach (Match match in Regex.Matches(File.ReadAllText(file), "--severity[ =]([A-Za-z,]+)"))
            {
                found.Add((Path.GetFileName(file), match.Groups[1].Value));
            }
        }

        Assert.Contains(found, f => f.File == "ci.yml");
        Assert.Contains(found, f => f.File == "review-gate.ps1");
        Assert.All(found, f => Assert.Equal(expected, TrivyScanPolicy.Parse(f.Value).Order().ToArray()));
    }
}
