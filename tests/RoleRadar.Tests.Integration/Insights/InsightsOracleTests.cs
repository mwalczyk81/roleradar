namespace RoleRadar.Tests.Integration.Insights;

using Xunit;

/// <summary>
/// No Docker needed. Pins the oracle itself against counts worked out by hand from the seed tables, and checks that
/// the two funnels nest. The seed is fictional.
/// </summary>
public sealed class InsightsOracleTests
{
    [Fact]
    public void SmallOwner_CountsMatchHandWorkedValues()
    {
        // Owner D: d-lead(5), d-scored(15), d-applied-1(applied 20d, interview), d-applied-2(applied 40d, silent).
        const string d = InsightsSeed.OwnerD;

        Assert.Equal(4, InsightsOracle.Count(d, "leads", true));
        Assert.Equal(3, InsightsOracle.Count(d, "reviewed", true));
        Assert.Equal(2, InsightsOracle.Count(d, "applied-to", true));
        Assert.Equal(2, InsightsOracle.Count(d, "applied", true));
        Assert.Equal(1, InsightsOracle.Count(d, "interviewed", true));
        Assert.Equal(2, InsightsOracle.Count(d, "mature", true));
        Assert.Equal(1, InsightsOracle.Count(d, "mature-interviewed", true));
        Assert.Equal(1, InsightsOracle.Count(d, "ghost-eligible", true));
        Assert.Equal(1, InsightsOracle.Count(d, "ghosted", true));
    }

    [Fact]
    public void OwnerB_CountsMatchHandWorkedValues()
    {
        // Owner B applications: offer after interview (30d), rejected with no interview (35d), silent (70d).
        const string b = InsightsSeed.OwnerB;

        Assert.Equal(3, InsightsOracle.Count(b, "applied", false));
        Assert.Equal(2, InsightsOracle.Count(b, "responded", false));
        Assert.Equal(1, InsightsOracle.Count(b, "offers", false));
        Assert.Equal(1, InsightsOracle.Count(b, "offers-interviewed", false));
        Assert.Equal(1, InsightsOracle.Count(b, "rejected", false));
        Assert.Equal(1, InsightsOracle.Count(b, "rejected-before-interview", false));
        Assert.Equal(0, InsightsOracle.Count(b, "rejected-after-interview", false));
        Assert.Equal(2, InsightsOracle.Count(b, "open", false));
        Assert.Equal(1, InsightsOracle.Count(b, "ghosted", false));
    }

    [Fact]
    public void EmptyOwner_IsZeroEverywhere()
    {
        foreach (var lens in InsightsOracle.LensKeys)
        {
            Assert.Equal(0, InsightsOracle.Count(InsightsSeed.OwnerC, lens, true));
            Assert.Equal(0, InsightsOracle.Count(InsightsSeed.OwnerC, lens, false));
        }
    }

    [Theory]
    [InlineData(InsightsSeed.OwnerA, true)]
    [InlineData(InsightsSeed.OwnerA, false)]
    [InlineData(InsightsSeed.OwnerB, false)]
    [InlineData(InsightsSeed.OwnerD, true)]
    public void EveryFunnel_Nests_EachStageAtMostItsPredecessor(string owner, bool last90Days)
    {
        foreach (var funnel in InsightsOracle.Funnels)
        {
            for (var i = 1; i < funnel.Length; i++)
            {
                var stage = InsightsOracle.Count(owner, funnel[i], last90Days);
                var predecessor = InsightsOracle.Count(owner, funnel[i - 1], last90Days);
                Assert.True(stage <= predecessor, $"{owner} {funnel[i]} ({stage}) exceeds {funnel[i - 1]} ({predecessor}), last90Days={last90Days}");
            }
        }
    }

    [Fact]
    public void SeedCoversTheCasesTheLensesMustSeparate()
    {
        const string a = InsightsSeed.OwnerA;

        // An offer logged without an interview exists, so Funnel B's Offers bar (offers-interviewed) is below offers.
        Assert.True(InsightsOracle.Count(a, "offers", false) > InsightsOracle.Count(a, "offers-interviewed", false));
        // Both rejection buckets are populated.
        Assert.True(InsightsOracle.Count(a, "rejected-before-interview", false) > 0);
        Assert.True(InsightsOracle.Count(a, "rejected-after-interview", false) > 0);
        // Window edges matter: the 90 day window holds fewer applications than all time.
        Assert.True(InsightsOracle.Count(a, "applied", true) < InsightsOracle.Count(a, "applied", false));
        // Applied-to is a cohort of postings, applied is a cohort of applications: they differ (posting first seen before the window).
        Assert.NotEqual(InsightsOracle.Count(a, "applied-to", true), InsightsOracle.Count(a, "applied", true));
        // Expired postings exist.
        Assert.True(InsightsOracle.Count(a, "expired", false) > 0);
    }
}
