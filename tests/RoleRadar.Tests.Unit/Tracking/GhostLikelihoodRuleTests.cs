using RoleRadar.Domain.Tracking;
using Xunit;

namespace RoleRadar.Tests.Unit.Tracking;

public sealed class GhostLikelihoodRuleTests
{
    [Fact]
    public void GhostLikelihoodRule_EvaluatesAllSignalWeights_AndClampsProperly()
    {
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

        // Posting fresh (age < 7 days, verified now) => base raw: 0 - 15 = -15 => clamped to 0 => low
        var pFresh = Posting.Create("owner-1", companyId, "Fresh Job", now: now);

        // Verification freshness test: fresh (<7d) dampens by -15
        var resFresh = GhostLikelihoodRule.Evaluate(pFresh, [], now);
        Assert.Equal("low", resFresh.Category);
        Assert.Equal(0, resFresh.ClampedScore);

        // Age > 60d (+20) with lastVerified > 7d ago
        var p65 = Posting.Create("owner-1", companyId, "Job 65d", now: now.AddDays(-65));
        p65.UpdateLastVerified(now.AddDays(-20));
        var res65 = GhostLikelihoodRule.Evaluate(p65, [], now);
        Assert.Equal(20, res65.ClampedScore);
        Assert.Equal("low", res65.Category); // 0..24 is Low

        // Age > 90d (+35) with lastVerified > 7d ago
        var p95 = Posting.Create("owner-1", companyId, "Job 95d", now: now.AddDays(-95));
        p95.UpdateLastVerified(now.AddDays(-20));
        var res95 = GhostLikelihoodRule.Evaluate(p95, [], now);
        Assert.Equal(35, res95.ClampedScore);
        Assert.Equal("moderate", res95.Category); // 25..49 is Moderate

        // Test individual signal weights on neutral baseline (age 30d, verified 10d ago => age=0, fresh=0 => baseline 0)
        var pNeutral = Posting.Create("owner-1", companyId, "Neutral Job", now: now.AddDays(-30));
        pNeutral.UpdateLastVerified(now.AddDays(-10));

        // 'gone' (+50%)
        var sigGone = new PostingSignal(Guid.NewGuid(), "owner-1", pNeutral.Id, SignalType.Gone, "404", now);
        var resGone = GhostLikelihoodRule.Evaluate(pNeutral, [sigGone], now);
        Assert.Equal(50, resGone.ClampedScore);
        Assert.Equal("high", resGone.Category); // 50..74 is High

        // 'phantom' (+40%)
        var sigPhantom = new PostingSignal(Guid.NewGuid(), "owner-1", pNeutral.Id, SignalType.Phantom, "ghost post", now);
        var resPhantom = GhostLikelihoodRule.Evaluate(pNeutral, [sigPhantom], now);
        Assert.Equal(40, resPhantom.ClampedScore);
        Assert.Equal("moderate", resPhantom.Category);

        // 'deadline' passed (+30%)
        var sigDeadlinePassed = new PostingSignal(Guid.NewGuid(), "owner-1", pNeutral.Id, SignalType.Deadline, "expired", now, "2026-10-15");
        var resDeadlinePassed = GhostLikelihoodRule.Evaluate(pNeutral, [sigDeadlinePassed], now);
        Assert.Equal(30, resDeadlinePassed.ClampedScore);
        Assert.Equal("moderate", resDeadlinePassed.Category);

        // 'deadline' future (0%)
        var sigDeadlineFuture = new PostingSignal(Guid.NewGuid(), "owner-1", pNeutral.Id, SignalType.Deadline, "future", now, "2026-10-25");
        var resDeadlineFuture = GhostLikelihoodRule.Evaluate(pNeutral, [sigDeadlineFuture], now);
        Assert.Equal(0, resDeadlineFuture.ClampedScore);
        Assert.Equal("low", resDeadlineFuture.Category);

        // 'repost' (+25%)
        var sigRepost = new PostingSignal(Guid.NewGuid(), "owner-1", pNeutral.Id, SignalType.Repost, "reposted", now);
        var resRepost = GhostLikelihoodRule.Evaluate(pNeutral, [sigRepost], now);
        Assert.Equal(25, resRepost.ClampedScore);
        Assert.Equal("moderate", resRepost.Category);

        // 'parallel_posting' (+20%)
        var sigParallel = new PostingSignal(Guid.NewGuid(), "owner-1", pNeutral.Id, SignalType.ParallelPosting, "multi-city", now);
        var resParallel = GhostLikelihoodRule.Evaluate(pNeutral, [sigParallel], now);
        Assert.Equal(20, resParallel.ClampedScore);
        Assert.Equal("low", resParallel.Category);

        // 'no_salary' (+15%)
        var sigNoSal = new PostingSignal(Guid.NewGuid(), "owner-1", pNeutral.Id, SignalType.NoSalary, "no salary", now);
        var resNoSal = GhostLikelihoodRule.Evaluate(pNeutral, [sigNoSal], now);
        Assert.Equal(15, resNoSal.ClampedScore);
        Assert.Equal("low", resNoSal.Category);

        // Combination reaching Critical (75..100): gone (+50) + deadline passed (+30) = 80
        var resCritical = GhostLikelihoodRule.Evaluate(pNeutral, [sigGone, sigDeadlinePassed], now);
        Assert.Equal(80, resCritical.ClampedScore);
        Assert.Equal("critical", resCritical.Category); // 75..100 is Critical

        // Clamping upper bound: gone (+50) + phantom (+40) + deadline passed (+30) + age > 90 (+35) = 155 => clamped to 100
        var resOver100 = GhostLikelihoodRule.Evaluate(p95, [sigGone, sigPhantom, sigDeadlinePassed], now);
        Assert.Equal(100, resOver100.ClampedScore);
        Assert.Equal("critical", resOver100.Category);
    }
}
