namespace RoleRadar.Tests.Integration.Insights;

using System.Net;
using Xunit;

/// <summary>
/// The new filterable properties through the real 006 pipeline, with lens-shaped filters compared with the
/// independent oracle. Also pins owner isolation and that <c>ageInDays</c> stays blocked from <c>$filter</c>.
/// </summary>
public sealed class InsightsODataFilterTests(InsightsFixture fixture) : IClassFixture<InsightsFixture>
{
    private ODataTestHost Host => fixture.Host;

    private DateOnly AsOf => fixture.AsOf;

    private const string Reviewed = "((disposition ne 'lead' and disposition ne 'unscored') or hasApplication eq true)";

    private Task<int> Posting(string owner, string filter) => InsightsTestSupport.CountAsync(Host, owner, "Postings", filter);

    private Task<int> Application(string owner, string filter) => InsightsTestSupport.CountAsync(Host, owner, "Applications", filter);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PostingLenses_MatchTheOracle_InBothWindows(bool last90Days)
    {
        var window = InsightsTestSupport.WindowClause("firstSeenAt", AsOf, last90Days);
        var owner = InsightsSeed.OwnerA;

        Assert.Equal(InsightsOracle.Count(owner, "leads", last90Days), await Posting(owner, window));
        Assert.Equal(InsightsOracle.Count(owner, "reviewed", last90Days), await Posting(owner, $"{Reviewed} and {window}"));
        Assert.Equal(InsightsOracle.Count(owner, "applied-to", last90Days), await Posting(owner, $"{Reviewed} and hasApplication eq true and {window}"));
        Assert.Equal(InsightsOracle.Count(owner, "screened-out", last90Days), await Posting(owner, $"{Reviewed} and disposition eq 'screened_out' and {window}"));
        Assert.Equal(InsightsOracle.Count(owner, "expired", last90Days), await Posting(owner, $"disposition eq 'closed' and hasApplication eq false and {window}"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ApplicationLenses_MatchTheOracle_InBothWindows(bool last90Days)
    {
        var window = InsightsTestSupport.WindowClause("appliedAt", AsOf, last90Days);
        var owner = InsightsSeed.OwnerA;
        var mature = $"appliedAt lt {InsightsTestSupport.Literal(AsOf.AddDays(-13))}";
        var ghostEligible = $"appliedAt lt {InsightsTestSupport.Literal(AsOf.AddDays(-29))}";
        var ghosted = $"status eq 'applied' and firstResponseAt eq null and appliedAt lt {InsightsTestSupport.Literal(AsOf.AddDays(-30))}";

        Assert.Equal(InsightsOracle.Count(owner, "applied", last90Days), await Application(owner, window));
        Assert.Equal(InsightsOracle.Count(owner, "responded", last90Days), await Application(owner, $"firstResponseAt ne null and {window}"));
        Assert.Equal(InsightsOracle.Count(owner, "interviewed", last90Days), await Application(owner, $"hasInterview eq true and {window}"));
        Assert.Equal(InsightsOracle.Count(owner, "offers", last90Days), await Application(owner, $"hasOffer eq true and {window}"));
        Assert.Equal(InsightsOracle.Count(owner, "offers-interviewed", last90Days), await Application(owner, $"hasOffer eq true and hasInterview eq true and {window}"));
        Assert.Equal(InsightsOracle.Count(owner, "mature", last90Days), await Application(owner, $"{mature} and {window}"));
        Assert.Equal(InsightsOracle.Count(owner, "mature-interviewed", last90Days), await Application(owner, $"{mature} and hasInterview eq true and {window}"));
        Assert.Equal(InsightsOracle.Count(owner, "rejected", last90Days), await Application(owner, $"hasRejection eq true and {window}"));
        Assert.Equal(InsightsOracle.Count(owner, "rejected-before-interview", last90Days), await Application(owner, $"hasRejection eq true and rejectedBeforeInterview eq true and {window}"));
        Assert.Equal(InsightsOracle.Count(owner, "rejected-after-interview", last90Days), await Application(owner, $"hasRejection eq true and rejectedBeforeInterview eq false and {window}"));
        Assert.Equal(InsightsOracle.Count(owner, "ghost-eligible", last90Days), await Application(owner, $"{ghostEligible} and {window}"));
        Assert.Equal(InsightsOracle.Count(owner, "ghosted", last90Days), await Application(owner, $"{ghosted} and {window}"));
        Assert.Equal(InsightsOracle.Count(owner, "open", last90Days), await Application(owner, $"status eq 'applied' and {window}"));
    }

    [Fact]
    public async Task HasApplication_IsTheAppliedToPostingsFilter_AndFalseComplementsIt()
    {
        var owner = InsightsSeed.OwnerA;
        var withApp = InsightsSeed.Postings.Count(p => p.Owner == owner && p.HasApplication);
        var without = InsightsSeed.Postings.Count(p => p.Owner == owner && !p.HasApplication);

        Assert.Equal(withApp, await Posting(owner, "hasApplication eq true"));
        Assert.Equal(without, await Posting(owner, "hasApplication eq false"));
    }

    [Fact]
    public async Task FirstSeenAt_FilterEdges_AreInclusiveLowerAndExclusiveUpper()
    {
        var owner = InsightsSeed.OwnerA;
        var start = InsightsTestSupport.Literal(AsOf.AddDays(-89));
        var end = InsightsTestSupport.Literal(AsOf.AddDays(1));

        var inclusive = await Posting(owner, $"firstSeenAt ge {start} and firstSeenAt lt {end}");
        var oneDayLater = await Posting(owner, $"firstSeenAt ge {InsightsTestSupport.Literal(AsOf.AddDays(-88))} and firstSeenAt lt {end}");
        var oneDayEarlier = await Posting(owner, $"firstSeenAt ge {InsightsTestSupport.Literal(AsOf.AddDays(-90))} and firstSeenAt lt {end}");

        Assert.Equal(InsightsOracle.Count(owner, "leads", true), inclusive);
        var firstDay = InsightsSeed.Postings.Count(p => p.Owner == owner && p.FirstSeenDaysBeforeAsOf == 89);
        Assert.True(firstDay > 0);
        Assert.Equal(firstDay, inclusive - oneDayLater);   // exactly the postings first seen on the first window day
        Assert.True(oneDayEarlier > inclusive);       // the day before the window holds postings that must be excluded
    }

    [Fact]
    public async Task OtherOwners_NeverLeakIntoOwnerACounts_AndEmptyOwnerCountsZero()
    {
        Assert.Equal(InsightsSeed.Postings.Count(p => p.Owner == InsightsSeed.OwnerA), await Posting(InsightsSeed.OwnerA, "contains(tolower(title),'fictional')"));
        Assert.Equal(3, await Application(InsightsSeed.OwnerB, "appliedAt lt 2099-01-01T00:00:00Z"));
        Assert.Equal(1, await Application(InsightsSeed.OwnerB, "hasOffer eq true"));
        Assert.Equal(2, await Application(InsightsSeed.OwnerD, "appliedAt lt 2099-01-01T00:00:00Z"));
        Assert.Equal(0, await Posting(InsightsSeed.OwnerC, "hasApplication eq true"));
        Assert.Equal(0, await Application(InsightsSeed.OwnerC, "hasOffer eq true"));
    }

    [Fact]
    public async Task AgeInDays_RemainsBlockedFromFilter()
    {
        var (status, _) = await InsightsTestSupport.RawAsync(
            Host, InsightsSeed.OwnerA, "/odata/Applications?$filter=" + Uri.EscapeDataString("ageInDays ge 14") + "&$top=1");

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task NewProperties_CannotBeUsedForSorting()
    {
        foreach (var path in new[]
        {
            "/odata/Postings?$orderby=hasApplication",
            "/odata/Postings?$orderby=firstSeenAt",
            "/odata/Applications?$orderby=hasOffer",
            "/odata/Applications?$orderby=hasRejection",
            "/odata/Applications?$orderby=rejectedBeforeInterview"
        })
        {
            var (status, _) = await InsightsTestSupport.RawAsync(Host, InsightsSeed.OwnerA, path);
            Assert.True(status == HttpStatusCode.BadRequest, $"{path} returned {(int)status}, expected 400 (no allowlist widening)");
        }
    }

    [Fact]
    public async Task NewProperties_AreReturnedOnRows()
    {
        var (status, body) = await InsightsTestSupport.RawAsync(
            Host, InsightsSeed.OwnerA, "/odata/Applications?$filter=" + Uri.EscapeDataString("hasOffer eq true") + "&$top=1");
        Assert.Equal(HttpStatusCode.OK, status);

        var row = InsightsTestSupport.ParseRoot(body).GetProperty("value")[0];
        Assert.True(row.GetProperty("hasOffer").GetBoolean());
        Assert.True(row.TryGetProperty("hasRejection", out _));
        Assert.True(row.TryGetProperty("rejectedBeforeInterview", out _));
    }
}
