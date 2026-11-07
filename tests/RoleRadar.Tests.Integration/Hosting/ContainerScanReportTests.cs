using Xunit;

namespace RoleRadar.Tests.Integration.Hosting;

/// <summary>
/// Requires the reports of a real container scan (Trivy, <c>TestResults/trivy-report-&lt;target&gt;.json</c>) and judges them by
/// the scan's own severity policy. It never writes a report, so it cannot pass on its own. Missing reports fail in CI
/// and under <c>ROLERADAR_REQUIRE_SCAN=1</c> (what <c>scripts/review-gate.ps1</c> sets); a plain local run skips with the reason.
/// It has its own lane because the reports only exist after the images are built and scanned, which in CI is after the
/// main test run (feature 010, contracts/scan-report-evaluation.md).
/// </summary>
[Trait("Lane", "scan")]
public sealed class ContainerScanReportTests
{
    [Fact]
    public void ContainerSecurityScanReport_ExistsAndPasses()
    {
        var reports = Path.Combine(ContainerTestHarness.FindRepoRoot(), "TestResults");
        var verdict = TrivyReportEvaluator.Evaluate(
            reports,
            TrivyScanPolicy.Parse(TrivyScanPolicy.FailingSeverities),
            TrivyScanPolicy.RequireReports(Environment.GetEnvironmentVariable));

        // Always shown, so a HIGH count under the CRITICAL policy is visible without failing the run.
        var counts = string.Join("; ", verdict.Counts.Select(c => $"{c.ArtifactName} CRITICAL={c.Critical} HIGH={c.High}"));
        Console.WriteLine($"container scan counts: {(counts.Length == 0 ? "(no reports)" : counts)}");
        TestContext.Current.TestOutputHelper?.WriteLine(verdict.Message);

        switch (verdict.Outcome)
        {
            case ScanOutcome.Skip:
                Assert.Skip(verdict.Message);
                break;
            case ScanOutcome.Fail:
                Assert.Fail(verdict.Message);
                break;
            default:
                Assert.Equal(ScanOutcome.Pass, verdict.Outcome);
                break;
        }
    }

    /// <summary>
    /// The real CLI scan (run with <c>--list-all-pkgs</c>) must list the Kerberos libraries the image ships, so the report
    /// is a scan of the image and not an empty file. It reads the report written by Trivy; it never writes one.
    /// </summary>
    [Fact]
    public void TrivyScanReport_CliImage_InventoriesKerberosPackages()
    {
        var reportPath = Path.Combine(ContainerTestHarness.FindRepoRoot(), "TestResults", "trivy-report-cli.json");
        if (!File.Exists(reportPath))
        {
            var message = $"no Trivy report for: {reportPath}. Run the CI scan step or scripts/review-gate.ps1.";
            if (TrivyScanPolicy.RequireReports(Environment.GetEnvironmentVariable))
            {
                Assert.Fail(message);
            }

            Assert.Skip(message);
        }

        var json = File.ReadAllText(reportPath);
        Assert.Contains("libgssapi-krb5-2", json, StringComparison.Ordinal);
        Assert.Contains("libkrb5-3", json, StringComparison.Ordinal);
    }
}
