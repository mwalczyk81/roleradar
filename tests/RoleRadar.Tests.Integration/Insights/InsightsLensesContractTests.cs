namespace RoleRadar.Tests.Integration.Insights;

using System.Text.Json.Nodes;
using Xunit;

/// <summary>
/// T049. <c>contracts/insights-lenses.json</c> must list exactly the lenses the oracle knows, and its <c>expected</c>
/// counts must be what the oracle computes from the fictional seed. With <c>UPDATE_SNAPSHOTS=1</c> only the
/// <c>expected</c> section is rewritten, from the oracle; <c>filters</c> belong to the web snapshot test.
/// </summary>
public sealed class InsightsLensesContractTests
{
    private static readonly string[] Owners = InsightsSeed.AllOwners;
    private static readonly string[] CoverageKeys = ["coverage-applications", "coverage-postings"];
    private static readonly bool[] Windows = [true, false];

    [Fact]
    public void Snapshot_ListsExactlyTheOracleLensKeys()
    {
        var snapshot = InsightsLensSnapshot.Load();

        var keys = snapshot["lenses"]!.AsObject().Select(p => p.Key).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        Assert.Equal(InsightsOracle.LensKeys.OrderBy(k => k, StringComparer.Ordinal).ToArray(), keys);

        var coverage = snapshot["coverage"]!.AsObject().Select(p => p.Key).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        Assert.Equal(CoverageKeys, coverage);
        Assert.Equal(InsightsSeed.FixedAsOf.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), snapshot["asOf"]!.GetValue<string>());
    }

    [Fact]
    public void Snapshot_ExpectedCounts_EqualTheOracle_OrAreRegenerated()
    {
        var snapshot = InsightsLensSnapshot.Load();

        if (InsightsLensSnapshot.Updating)
        {
            foreach (var key in InsightsOracle.LensKeys)
            {
                var expected = new JsonObject();
                foreach (var owner in Owners)
                {
                    expected[owner] = new JsonObject
                    {
                        ["90d"] = InsightsOracle.Count(owner, key, last90Days: true),
                        ["all"] = InsightsOracle.Count(owner, key, last90Days: false)
                    };
                }

                snapshot["lenses"]![key]!.AsObject()["expected"] = expected;
            }

            // A coverage check counts the rows that carry the flag; on a fully written seed that is the whole cohort.
            foreach (var (key, cohort) in new[] { ("coverage-postings", "leads"), ("coverage-applications", "applied") })
            {
                var expected = new JsonObject();
                foreach (var owner in Owners)
                {
                    expected[owner] = new JsonObject
                    {
                        ["90d"] = InsightsOracle.Count(owner, cohort, last90Days: true),
                        ["all"] = InsightsOracle.Count(owner, cohort, last90Days: false)
                    };
                }

                snapshot["coverage"]![key]!.AsObject()["expected"] = expected;
            }

            InsightsLensSnapshot.Save(snapshot);
            snapshot = InsightsLensSnapshot.Load();
        }

        var problems = new List<string>();
        foreach (var key in InsightsOracle.LensKeys)
        {
            foreach (var owner in Owners)
            {
                foreach (var last90Days in Windows)
                {
                    var stored = InsightsLensSnapshot.Expected(snapshot, "lenses", key, owner, last90Days);
                    var oracle = InsightsOracle.Count(owner, key, last90Days);
                    if (stored != oracle)
                    {
                        problems.Add($"{key} {owner} {(last90Days ? "90d" : "all")}: snapshot {(stored.HasValue ? stored.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "missing")}, oracle {oracle}");
                    }
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void Snapshot_UsesOnlyFictionalOwnersAndNoRealData()
    {
        var text = File.ReadAllText(InsightsLensSnapshot.FilePath);

        Assert.DoesNotContain("@", text, StringComparison.Ordinal);
        Assert.DoesNotContain("http", text, StringComparison.OrdinalIgnoreCase);
        foreach (var owner in Owners)
        {
            Assert.StartsWith("insights-owner-", owner, StringComparison.Ordinal);
        }
    }
}
