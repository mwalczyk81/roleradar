using RoleRadar.Domain.Tracking;

namespace RoleRadar.Domain.Tracking;

public sealed record GhostLikelihoodEvaluationResult(
    int ClampedScore,
    string Category);

public static class GhostLikelihoodRule
{
    public static GhostLikelihoodEvaluationResult Evaluate(
        Posting posting,
        IReadOnlyList<PostingSignal> signals,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(posting);
        ArgumentNullException.ThrowIfNull(signals);

        throw new NotImplementedException("GhostLikelihoodRule stub for test-first failure.");
    }
}
