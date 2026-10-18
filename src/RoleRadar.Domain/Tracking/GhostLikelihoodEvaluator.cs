namespace RoleRadar.Domain.Tracking;

public static class GhostLikelihoodEvaluator
{
    public static string Evaluate(Posting posting, IReadOnlyList<PostingSignal> signals, DateTimeOffset now)
    {
        return GhostLikelihoodRule.Evaluate(posting, signals, now).Category;
    }

    private static bool IsDeadlinePassed(string? deadlineDate, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(deadlineDate) || !DateOnly.TryParse(deadlineDate, out var date))
        {
            return false;
        }

        var today = DateOnly.FromDateTime(now.UtcDateTime);
        return today > date;
    }
}
