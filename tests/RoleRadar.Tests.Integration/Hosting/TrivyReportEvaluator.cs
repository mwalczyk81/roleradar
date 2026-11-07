using System.Text.Json;

namespace RoleRadar.Tests.Integration.Hosting;

internal enum ScanOutcome
{
    Pass,
    Fail,
    Skip
}

/// <summary>What one Trivy report contains, whatever the policy: the HIGH and CRITICAL counts per image.</summary>
internal sealed record ImageScanCounts(string Target, string ArtifactName, int Critical, int High);

internal sealed record ScanVerdict(ScanOutcome Outcome, string Message, IReadOnlyList<ImageScanCounts> Counts);

/// <summary>
/// The scan severity policy (feature 010, owner decision at T024; research R6, contracts/scan-report-evaluation.md).
/// <see cref="FailingSeverities"/> is the one value shared by the evaluator, the CI Trivy steps and
/// <c>scripts/review-gate.ps1</c> (their <c>--severity</c> flag); <c>EverySeverityFlag_InCiAndTheGate_EqualsTheEvaluatorPolicy</c>
/// keeps them equal. Tightening later means changing this value to <c>HIGH,CRITICAL</c> together with those two flags
/// and fixing the findings tracked on the board.
/// </summary>
internal static class TrivyScanPolicy
{
    public const string FailingSeverities = "CRITICAL";

    public static IReadOnlySet<string> Parse(string severities) =>
        severities.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToUpperInvariant())
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>Reports are mandatory in CI and when the gate sets <c>ROLERADAR_REQUIRE_SCAN=1</c>; a plain local run may skip.</summary>
    public static bool RequireReports(Func<string, string?> environment) =>
        !string.IsNullOrEmpty(environment("CI")) || environment("ROLERADAR_REQUIRE_SCAN") == "1";
}

/// <summary>
/// Reads the real <c>trivy-report-&lt;target&gt;.json</c> files and judges them by a severity policy. It only reads: it
/// never writes a report, so it cannot make itself pass.
/// </summary>
internal static class TrivyReportEvaluator
{
    public static readonly string[] Targets = ["cli", "mcp", "identity", "api", "web"];

    public static ScanVerdict Evaluate(string reportsDirectory, IReadOnlySet<string> failingSeverities, bool requireReports)
    {
        var counts = new List<ImageScanCounts>();
        var findings = new List<string>();
        var problems = new List<string>();
        var missing = new List<string>();

        foreach (var target in Targets)
        {
            var path = Path.Combine(reportsDirectory, $"trivy-report-{target}.json");
            if (!File.Exists(path))
            {
                missing.Add(path);
                continue;
            }

            ReadReport(target, path, failingSeverities, counts, findings, problems);
        }

        var summary = Summary(counts, failingSeverities);

        if (problems.Count > 0)
        {
            return new ScanVerdict(ScanOutcome.Fail, "Invalid Trivy report: " + string.Join("; ", problems) + Environment.NewLine + summary, counts);
        }

        if (findings.Count > 0)
        {
            return new ScanVerdict(
                ScanOutcome.Fail,
                $"Container scan found {findings.Count} finding(s) in the failing severities ({string.Join(",", failingSeverities.Order())}): "
                + string.Join("; ", findings) + Environment.NewLine + summary,
                counts);
        }

        if (missing.Count > 0)
        {
            var names = string.Join(", ", missing);
            return requireReports
                ? new ScanVerdict(ScanOutcome.Fail, $"Missing Trivy report(s), and a real scan is required here (CI or ROLERADAR_REQUIRE_SCAN=1): {names}", counts)
                : new ScanVerdict(
                    ScanOutcome.Skip,
                    $"no Trivy report for: {names}. Run the CI scan step or scripts/review-gate.ps1, or set ROLERADAR_REQUIRE_SCAN=1 to make this a failure.",
                    counts);
        }

        return new ScanVerdict(ScanOutcome.Pass, summary, counts);
    }

    private static void ReadReport(
        string target,
        string path,
        IReadOnlySet<string> failingSeverities,
        List<ImageScanCounts> counts,
        List<string> findings,
        List<string> problems)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(path));
        }
        catch (JsonException ex)
        {
            problems.Add($"{target}: {path} is not valid JSON ({ex.Message})");
            return;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("ArtifactName", out var artifact)
                || artifact.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(artifact.GetString()))
            {
                problems.Add($"{target}: {path} is not a Trivy report (no ArtifactName)");
                return;
            }

            var artifactName = artifact.GetString()!;
            var expected = "roleradar-" + target;
            if (!RepositoryOf(artifactName).EndsWith(expected, StringComparison.Ordinal))
            {
                problems.Add($"{target}: {path} scanned '{artifactName}', expected an image from repository '{expected}'");
                return;
            }

            int critical = 0, high = 0;
            if (root.TryGetProperty("Results", out var results) && results.ValueKind == JsonValueKind.Array)
            {
                foreach (var result in results.EnumerateArray())
                {
                    if (!result.TryGetProperty("Vulnerabilities", out var vulnerabilities) || vulnerabilities.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var vulnerability in vulnerabilities.EnumerateArray())
                    {
                        var severity = vulnerability.TryGetProperty("Severity", out var s) ? (s.GetString() ?? string.Empty).ToUpperInvariant() : string.Empty;
                        critical += severity == "CRITICAL" ? 1 : 0;
                        high += severity == "HIGH" ? 1 : 0;
                        if (failingSeverities.Contains(severity))
                        {
                            var id = vulnerability.TryGetProperty("VulnerabilityID", out var v) ? v.GetString() : "(no id)";
                            var package = vulnerability.TryGetProperty("PkgName", out var p) ? p.GetString() : "(no package)";
                            findings.Add($"{target}: {id} {package} {severity}");
                        }
                    }
                }
            }

            counts.Add(new ImageScanCounts(target, artifactName, critical, high));
        }
    }

    /// <summary>The image repository without tag or digest, so CI's <c>:local</c> and the gate's <c>:review-&lt;guid&gt;</c> both match.</summary>
    private static string RepositoryOf(string artifactName)
    {
        var name = artifactName;
        var at = name.IndexOf('@', StringComparison.Ordinal);
        if (at >= 0)
        {
            name = name[..at];
        }

        var colon = name.LastIndexOf(':');
        return colon > name.LastIndexOf('/') ? name[..colon] : name;
    }

    private static string Summary(IReadOnlyList<ImageScanCounts> counts, IReadOnlySet<string> failingSeverities) =>
        $"Trivy reports, failing severities [{string.Join(",", failingSeverities.Order())}]: "
        + string.Join("; ", counts.Select(c => $"{c.Target} CRITICAL={c.Critical} HIGH={c.High}"))
        + " (counts are what each report holds: a scan run at the CRITICAL policy lists no HIGH findings, so HIGH=0 is not a clean HIGH result)";
}
