namespace RoleRadar.Tests.Integration.Insights;

using RoleRadar.Domain.Tracking;
using RoleRadar.Infrastructure.Mongo.Repositories;
using Xunit;

/// <summary>
/// ADR 0031: <c>has_application</c> goes false to true once and never changes afterwards. A full-document replace of
/// the posting must not wipe it, and no application status or event change may alter it.
/// </summary>
public sealed class InsightsFlagSurvivalTests(InsightsFixture fixture) : IClassFixture<InsightsFixture>
{
    private static readonly DateTimeOffset Later = new(2026, 9, 30, 15, 0, 0, TimeSpan.Zero);

    private MongoPostingRepository Postings => new(fixture.Database.Client, fixture.Database);

    private MongoApplicationRepository Applications => new(fixture.Database.Client, fixture.Database);

    [Fact]
    public async Task EditingAPostingThatHasAnApplication_KeepsHasApplicationTrue()
    {
        const string key = "age-50-silent";
        var loaded = await Postings.GetByIdAsync(InsightsSeed.OwnerA, InsightsSeed.PostingId(key));
        var posting = loaded.Value!;

        posting.ApplyExplicitUpdate(null, "Fictional renamed posting", null, null, null, null, null, null, null, null, 8.5m, null, null, null, null, Later);
        Assert.True((await Postings.UpsertPostingAsync(posting)).IsSuccess);
        Assert.True(await HasApplicationAsync(key));

        foreach (var target in Enum.GetValues<PostingDisposition>())
        {
            posting.Transition(target, "fictional transition", null, ResurfacePolicy.Never, Later);
            Assert.True((await Postings.UpsertPostingAsync(posting)).IsSuccess);
            Assert.True(await HasApplicationAsync(key), $"has_application was cleared by a transition to {target}");
        }
    }

    [Fact]
    public async Task EveryApplicationEventType_LeavesHasApplicationTrue()
    {
        const string key = "age-05";
        var loaded = await Applications.GetByIdAsync(InsightsSeed.OwnerA, InsightsSeed.ApplicationId(key));
        var application = loaded.Value!;

        var day = 1;
        foreach (var type in Enum.GetValues<ApplicationEventType>())
        {
            var when = Later.AddMinutes(day++);
            var date = when.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            Assert.True((await Applications.AppendEventAsync(ApplicationEvent.Create(InsightsSeed.OwnerA, application.Id, date, type, now: when))).IsSuccess);
            application.ApplyEvent(type, null, when);
            Assert.True((await Applications.UpsertApplicationAsync(application)).IsSuccess);
            Assert.True(await HasApplicationAsync(key), $"has_application changed after a {type} event");
        }
    }

    [Fact]
    public async Task EditingAPostingWithoutAnApplication_KeepsHasApplicationFalse()
    {
        const string key = "lead-5";
        var posting = (await Postings.GetByIdAsync(InsightsSeed.OwnerA, InsightsSeed.PostingId(key))).Value!;
        posting.ApplyExplicitUpdate(null, "Fictional renamed lead", null, null, null, null, null, null, null, null, null, null, null, null, null, Later);

        Assert.True((await Postings.UpsertPostingAsync(posting)).IsSuccess);

        var doc = await InsightsTestSupport.PostingDocAsync(fixture.Database, key);
        Assert.Equal(false, InsightsTestSupport.Flag(doc, "has_application"));
    }

    private async Task<bool> HasApplicationAsync(string key) =>
        InsightsTestSupport.Flag(await InsightsTestSupport.PostingDocAsync(fixture.Database, key), "has_application") == true;
}
