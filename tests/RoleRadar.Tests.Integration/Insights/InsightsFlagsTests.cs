namespace RoleRadar.Tests.Integration.Insights;

using Xunit;

/// <summary>
/// The five derived fields, as written by the real repositories during seeding, must equal the independent oracle
/// for every seeded row (ADR 0031).
/// </summary>
public sealed class InsightsFlagsTests(InsightsFixture fixture) : IClassFixture<InsightsFixture>
{
    [Fact]
    public async Task EveryPosting_HasApplication_MatchesWhetherAnApplicationExists()
    {
        var problems = new List<string>();
        foreach (var seed in InsightsSeed.Postings)
        {
            var doc = await InsightsTestSupport.PostingDocAsync(fixture.Database, seed.Key);
            var stored = InsightsTestSupport.Flag(doc, "has_application");
            if (stored != seed.HasApplication)
            {
                problems.Add($"{seed.Key}: has_application stored={stored?.ToString() ?? "missing"}, expected {seed.HasApplication}");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public async Task EveryApplication_OfferRejectionAndBeforeInterviewFlags_MatchTheOracle()
    {
        var problems = new List<string>();
        foreach (var seed in InsightsSeed.Postings.Where(p => p.HasApplication))
        {
            var expected = InsightsOracle.Facts(seed);
            var doc = await InsightsTestSupport.ApplicationDocAsync(fixture.Database, seed.Key);

            Check(problems, seed.Key, "has_offer", InsightsTestSupport.Flag(doc, "has_offer"), expected.HasOffer);
            Check(problems, seed.Key, "has_rejection", InsightsTestSupport.Flag(doc, "has_rejection"), expected.HasRejection);
            Check(problems, seed.Key, "rejected_before_interview", InsightsTestSupport.Flag(doc, "rejected_before_interview"), expected.RejectedBeforeInterview && expected.HasRejection);
            Check(problems, seed.Key, "has_interview", InsightsTestSupport.Flag(doc, "has_interview"), expected.HasInterview);
            Check(problems, seed.Key, "status=applied (open)", doc["status"].AsString == "applied", expected.Open);
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public async Task RejectedBeforeInterview_IsFalseWhenThereIsNoRejection()
    {
        var doc = await InsightsTestSupport.ApplicationDocAsync(fixture.Database, "age-05");

        Assert.False(InsightsTestSupport.Flag(doc, "has_rejection"));
        Assert.False(InsightsTestSupport.Flag(doc, "rejected_before_interview"));
    }

    private static void Check(List<string> problems, string key, string field, bool? stored, bool expected)
    {
        if (stored != expected)
        {
            problems.Add($"{key}: {field} stored={stored?.ToString() ?? "missing"}, expected {expected}");
        }
    }
}
