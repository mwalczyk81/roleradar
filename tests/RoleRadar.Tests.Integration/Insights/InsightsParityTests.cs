namespace RoleRadar.Tests.Integration.Insights;

using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

/// <summary>
/// T048. The number on the dashboard and the number of rows in its drill-down list must agree. The filters come from
/// <c>contracts/insights-lenses.json</c> (the strings the web lens module emits); every lens is run through the real
/// API for both windows and compared with an independent oracle over the fictional seed and with the list request's
/// own <c>@odata.count</c>. Owner isolation, funnel nesting and the open / coverage definitions are checked too.
/// </summary>
public sealed class InsightsParityTests(InsightsFixture fixture) : IClassFixture<InsightsFixture>
{
    public static TheoryData<string, bool> LensesAndWindows
    {
        get
        {
            var data = new TheoryData<string, bool>();
            foreach (var key in InsightsOracle.LensKeys)
            {
                data.Add(key, true);
                data.Add(key, false);
            }

            return data;
        }
    }

    private ODataTestHost Host => fixture.Host;

    private async Task<int> CountAsync(string owner, string set, string filter) =>
        await InsightsTestSupport.CountAsync(Host, owner, set, filter);

    /// <summary>The list page's own request: paged, ordered, with the total.</summary>
    private async Task<int> ListTotalAsync(string owner, string set, string filter)
    {
        var path = $"/odata/{set}?$filter={Uri.EscapeDataString(filter)}&$top=25&$count=true";
        var (status, body) = await Host.GetJsonAsync(owner, path);
        using (body)
        {
            Assert.True(status == System.Net.HttpStatusCode.OK, $"{path} returned {(int)status}");
            return body.RootElement.GetProperty("@odata.count").GetInt32();
        }
    }

    [Theory]
    [MemberData(nameof(LensesAndWindows))]
    public async Task EveryLens_DashboardCount_EqualsOracle_SnapshotAndTheListTotal(string key, bool last90Days)
    {
        var snapshot = InsightsLensSnapshot.Load();
        var filter = InsightsLensSnapshot.Filter(snapshot, "lenses", key, last90Days);
        var set = InsightsLensSnapshot.EntitySet(InsightsLensSnapshot.Entity(snapshot, "lenses", key));

        var dashboard = await CountAsync(InsightsLensSnapshot.Owner, set, filter);
        var list = await ListTotalAsync(InsightsLensSnapshot.Owner, set, filter);
        var oracle = InsightsOracle.Count(InsightsLensSnapshot.Owner, key, last90Days);

        Assert.Equal(oracle, dashboard);
        Assert.Equal(dashboard, list);

        if (!InsightsLensSnapshot.Updating)
        {
            var expected = InsightsLensSnapshot.Expected(snapshot, "lenses", key, InsightsLensSnapshot.Owner, last90Days);
            Assert.True(expected.HasValue, $"{key} has no expected count in contracts/insights-lenses.json; regenerate with UPDATE_SNAPSHOTS=1");
            Assert.Equal(expected.Value, dashboard);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OffersWithoutInterview_List_HoldsExactlyOffersMinusOffersInterviewed(bool last90Days)
    {
        // The dashboard shows offers minus offers-interviewed as a number and links it to the offers-without-interview
        // list. The two must be the same set arithmetic, so the number and the list agree.
        var snapshot = InsightsLensSnapshot.Load();
        var owner = InsightsLensSnapshot.Owner;

        var offers = await CountAsync(owner, "Applications", InsightsLensSnapshot.Filter(snapshot, "lenses", "offers", last90Days));
        var withInterview = await CountAsync(owner, "Applications", InsightsLensSnapshot.Filter(snapshot, "lenses", "offers-interviewed", last90Days));
        var withoutFilter = InsightsLensSnapshot.Filter(snapshot, "lenses", "offers-without-interview", last90Days);

        Assert.True(offers - withInterview > 0, "the seed must contain an offer logged without an interview");
        Assert.Equal(offers - withInterview, await CountAsync(owner, "Applications", withoutFilter));
        Assert.Equal(offers - withInterview, await ListTotalAsync(owner, "Applications", withoutFilter));
    }

    [Theory]
    [InlineData(InsightsSeed.OwnerB)]
    [InlineData(InsightsSeed.OwnerC)]
    [InlineData(InsightsSeed.OwnerD)]
    public async Task OtherOwners_AreCountedOnlyByTheirOwnRows(string owner)
    {
        var snapshot = InsightsLensSnapshot.Load();
        foreach (var key in InsightsOracle.LensKeys)
        {
            foreach (var last90Days in new[] { true, false })
            {
                var filter = InsightsLensSnapshot.Filter(snapshot, "lenses", key, last90Days);
                var set = InsightsLensSnapshot.EntitySet(InsightsLensSnapshot.Entity(snapshot, "lenses", key));

                Assert.Equal(InsightsOracle.Count(owner, key, last90Days), await CountAsync(owner, set, filter));
            }
        }
    }

    [Theory]
    [InlineData(InsightsSeed.OwnerA, true)]
    [InlineData(InsightsSeed.OwnerA, false)]
    [InlineData(InsightsSeed.OwnerB, false)]
    [InlineData(InsightsSeed.OwnerD, true)]
    public async Task EveryFunnel_Nests_AgainstTheLiveApi(string owner, bool last90Days)
    {
        var snapshot = InsightsLensSnapshot.Load();
        foreach (var funnel in InsightsOracle.Funnels)
        {
            var previous = int.MaxValue;
            var previousKey = "(start)";
            foreach (var key in funnel)
            {
                var filter = InsightsLensSnapshot.Filter(snapshot, "lenses", key, last90Days);
                var set = InsightsLensSnapshot.EntitySet(InsightsLensSnapshot.Entity(snapshot, "lenses", key));
                var count = await CountAsync(owner, set, filter);

                Assert.True(count <= previous, $"{owner} {key} ({count}) exceeds {previousKey} ({previous}), last90Days={last90Days}");
                previous = count;
                previousKey = key;
            }
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CoverageChecks_EqualTheirCohort_WhenEveryRowCarriesTheFlag(bool last90Days)
    {
        var snapshot = InsightsLensSnapshot.Load();
        var owner = InsightsLensSnapshot.Owner;

        var postingsFlagged = await CountAsync(owner, "Postings", InsightsLensSnapshot.Filter(snapshot, "coverage", "coverage-postings", last90Days));
        var applicationsFlagged = await CountAsync(owner, "Applications", InsightsLensSnapshot.Filter(snapshot, "coverage", "coverage-applications", last90Days));

        Assert.Equal(InsightsOracle.Count(owner, "leads", last90Days), postingsFlagged);
        Assert.Equal(InsightsOracle.Count(owner, "applied", last90Days), applicationsFlagged);
    }

    [Fact]
    public async Task OpenLens_IsExactlyTheApplicationsWithNoTerminalEvent()
    {
        var snapshot = InsightsLensSnapshot.Load();
        var filter = InsightsLensSnapshot.Filter(snapshot, "lenses", "open", last90Days: false);

        var (status, body) = await Host.GetJsonAsync(
            InsightsLensSnapshot.Owner, $"/odata/Applications?$filter={Uri.EscapeDataString(filter)}&$select=id&$top=100");
        using (body)
        {
            Assert.Equal(System.Net.HttpStatusCode.OK, status);
            var returned = body.RootElement.GetProperty("value").EnumerateArray()
                .Select(row => Guid.Parse(row.GetProperty("id").GetString()!)).ToHashSet();

            var expected = InsightsOracle.ForOwner(InsightsLensSnapshot.Owner)
                .Where(p => p.HasApplication && InsightsOracle.InWindow(p.AppliedDaysBeforeAsOf!.Value, last90Days: false))
                .Where(p => InsightsOracle.Facts(p).Open)
                .Select(p => InsightsSeed.ApplicationId(p.Key)).ToHashSet();

            Assert.Equal(expected, returned);
        }
    }

    [Fact]
    public async Task EveryTerminalEventType_TakesAnApplicationOutOfOpen()
    {
        // The seed has an application for each terminal event type (rejected, offer accepted, offer declined, withdrawn).
        var snapshot = InsightsLensSnapshot.Load();
        var filter = InsightsLensSnapshot.Filter(snapshot, "lenses", "open", last90Days: false);
        var (_, body) = await Host.GetJsonAsync(
            InsightsLensSnapshot.Owner, $"/odata/Applications?$filter={Uri.EscapeDataString(filter)}&$select=id&$top=100");
        using (body)
        {
            var open = body.RootElement.GetProperty("value").EnumerateArray().Select(r => Guid.Parse(r.GetProperty("id").GetString()!)).ToHashSet();

            foreach (var key in new[] { "rej-no-interview", "offer-accepted", "offer-declined", "withdrawn" })
            {
                Assert.DoesNotContain(InsightsSeed.ApplicationId(key), open);
            }

            foreach (var key in new[] { "age-05", "offer-after-interview", "interview-mid" })
            {
                Assert.Contains(InsightsSeed.ApplicationId(key), open);
            }
        }
    }
}
