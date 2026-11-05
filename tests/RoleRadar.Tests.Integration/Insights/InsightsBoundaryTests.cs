namespace RoleRadar.Tests.Integration.Insights;

using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

/// <summary>
/// T048 boundary rows. The seed is built relative to today so the live <c>ageInDays</c> the API computes from the real
/// clock can be compared with the lens cutoffs. The lens filters are the strings from
/// <c>contracts/insights-lenses.json</c> with every date shifted by the distance between the snapshot's as-of date and
/// today; every date in a filter is an offset from asOf, so the shift is exact.
/// </summary>
public sealed partial class InsightsBoundaryTests(InsightsTodayFixture fixture) : IClassFixture<InsightsTodayFixture>
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private ODataTestHost Host => fixture.Host;

    private sealed record Row(Guid Id, int AgeInDays, DateOnly AppliedDate, string Status, bool Responded);

    private string ShiftedFilter(string key, bool last90Days)
    {
        var snapshot = InsightsLensSnapshot.Load();
        var snapshotAsOf = DateOnly.Parse(snapshot["asOf"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture);
        var delta = Today.DayNumber - snapshotAsOf.DayNumber;
        var filter = InsightsLensSnapshot.Filter(snapshot, "lenses", key, last90Days);
        return MidnightLiteral().Replace(filter, m =>
        {
            var date = DateOnly.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture).AddDays(delta);
            return InsightsTestSupport.Literal(date);
        });
    }

    private async Task<IReadOnlyList<Row>> ApplicationsAsync(string key, bool last90Days)
    {
        var filter = ShiftedFilter(key, last90Days);
        var path = $"/odata/Applications?$filter={Uri.EscapeDataString(filter)}&$select=id,ageInDays,appliedAt,status,firstResponseAt&$top=100&$count=true";
        var (status, body) = await Host.GetJsonAsync(InsightsSeed.OwnerA, path);
        using (body)
        {
            Assert.True(status == System.Net.HttpStatusCode.OK, $"{path} returned {(int)status}");
            Assert.True(body.RootElement.GetProperty("@odata.count").GetInt32() <= 100, "the seed must fit one page");
            return body.RootElement.GetProperty("value").EnumerateArray().Select(row => new Row(
                Guid.Parse(row.GetProperty("id").GetString()!),
                row.GetProperty("ageInDays").GetInt32(),
                DateOnly.FromDateTime(row.GetProperty("appliedAt").GetDateTimeOffset().UtcDateTime),
                row.GetProperty("status").GetString()!,
                row.TryGetProperty("firstResponseAt", out var first) && first.ValueKind != JsonValueKind.Null)).ToList();
        }
    }

    private static HashSet<Guid> Ids(IEnumerable<Row> rows) => rows.Select(r => r.Id).ToHashSet();

    [Fact]
    public async Task Mature_IsExactlyApplicationsDisplayedAs14DaysOldOrMore()
    {
        var all = await ApplicationsAsync("applied", last90Days: false);
        var mature = await ApplicationsAsync("mature", last90Days: false);

        Assert.Equal(Ids(all.Where(r => r.AgeInDays >= 14 && r.AppliedDate <= Today)), Ids(mature));
        Assert.Contains(InsightsSeed.ApplicationId("age-14-interview"), Ids(mature));
        Assert.DoesNotContain(InsightsSeed.ApplicationId("age-13-interview"), Ids(mature));
    }

    [Fact]
    public async Task GhostEligible_IsExactlyApplicationsDisplayedAs30DaysOldOrMore()
    {
        var all = await ApplicationsAsync("applied", last90Days: false);
        var eligible = await ApplicationsAsync("ghost-eligible", last90Days: false);

        Assert.Equal(Ids(all.Where(r => r.AgeInDays >= 30 && r.AppliedDate <= Today)), Ids(eligible));
        Assert.Contains(InsightsSeed.ApplicationId("age-30-silent"), Ids(eligible));
        Assert.DoesNotContain(InsightsSeed.ApplicationId("age-29-silent"), Ids(eligible));
    }

    [Fact]
    public async Task Ghosted_IsExactlyStillAppliedUnansweredAndDisplayedAsOlderThan30Days()
    {
        var all = await ApplicationsAsync("applied", last90Days: false);
        var ghosted = await ApplicationsAsync("ghosted", last90Days: false);

        var expected = all.Where(r => r.Status == "applied" && !r.Responded && r.AgeInDays > 30 && r.AppliedDate <= Today);
        Assert.Equal(Ids(expected), Ids(ghosted));
        Assert.Contains(InsightsSeed.ApplicationId("age-31-silent"), Ids(ghosted));
        Assert.DoesNotContain(InsightsSeed.ApplicationId("age-30-silent"), Ids(ghosted));
    }

    [Fact]
    public async Task GhostRate_Boundary_At29_30_And31Days_IsPinned()
    {
        // FR-013 deliberately uses two cutoffs: the denominator is "30 days old or older" (ghost-eligible, lt asOf-29d)
        // and the numerator is "older than 30 days" (ghosted, lt asOf-30d). An application exactly 30 days old is in the
        // denominator and cannot be ghosted yet. Changing either cutoff must change this test on purpose.
        var eligible = Ids(await ApplicationsAsync("ghost-eligible", last90Days: false));
        var ghosted = Ids(await ApplicationsAsync("ghosted", last90Days: false));
        var a29 = InsightsSeed.ApplicationId("age-29-silent");
        var a30 = InsightsSeed.ApplicationId("age-30-silent");
        var a31 = InsightsSeed.ApplicationId("age-31-silent");

        Assert.False(eligible.Contains(a29), "29 days old: not in the ghost rate denominator");
        Assert.False(ghosted.Contains(a29), "29 days old: not ghosted");
        Assert.True(eligible.Contains(a30), "30 days old: in the ghost rate denominator");
        Assert.False(ghosted.Contains(a30), "30 days old: in the denominator but not ghosted");
        Assert.True(eligible.Contains(a31), "31 days old: in the ghost rate denominator");
        Assert.True(ghosted.Contains(a31), "31 days old: ghosted");
    }

    [Fact]
    public async Task NinetyDayWindow_Applications_IncludeAsOfAndTheFirstWindowDay_AndExcludeTheDayBeforeAndTheFuture()
    {
        var all = await ApplicationsAsync("applied", last90Days: false);
        var windowed = await ApplicationsAsync("applied", last90Days: true);

        var expected = all.Where(r => r.AppliedDate <= Today && Today.DayNumber - r.AppliedDate.DayNumber <= 89);
        Assert.Equal(Ids(expected), Ids(windowed));

        Assert.Contains(InsightsSeed.ApplicationId("age-00-edge"), Ids(windowed));          // applied on asOf itself
        Assert.Contains(InsightsSeed.ApplicationId("window-first-instant"), Ids(windowed)); // 89 days before asOf
        Assert.DoesNotContain(InsightsSeed.ApplicationId("window-just-outside"), Ids(windowed)); // 90 days before asOf
        Assert.DoesNotContain(InsightsSeed.ApplicationId("window-future-dated"), Ids(windowed)); // dated after asOf
    }

    [Fact]
    public async Task NinetyDayWindow_Postings_UseFirstSeenAndTheSameEdges()
    {
        async Task<HashSet<Guid>> PostingIdsAsync(bool last90Days)
        {
            var filter = ShiftedFilter("leads", last90Days);
            var path = $"/odata/Postings?$filter={Uri.EscapeDataString(filter)}&$select=id,firstSeenAt&$top=100";
            var (status, body) = await Host.GetJsonAsync(InsightsSeed.OwnerA, path);
            using (body)
            {
                Assert.Equal(System.Net.HttpStatusCode.OK, status);
                return body.RootElement.GetProperty("value").EnumerateArray().Select(r => Guid.Parse(r.GetProperty("id").GetString()!)).ToHashSet();
            }
        }

        var windowed = await PostingIdsAsync(true);

        Assert.Contains(InsightsSeed.PostingId("window-first-seen-edge-89"), windowed);
        Assert.DoesNotContain(InsightsSeed.PostingId("window-first-seen-edge-90"), windowed);
        Assert.DoesNotContain(InsightsSeed.PostingId("window-first-seen-future"), windowed);
        Assert.Contains(InsightsSeed.PostingId("lead-5"), windowed);
    }

    [GeneratedRegex(@"(\d{4}-\d{2}-\d{2})T00:00:00Z")]
    private static partial Regex MidnightLiteral();
}
