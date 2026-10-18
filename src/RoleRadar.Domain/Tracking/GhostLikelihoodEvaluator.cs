using RoleRadar.Domain.Tracking;

namespace RoleRadar.Domain.Tracking;

public static class GhostLikelihoodEvaluator
{
    public static string Evaluate(Posting posting, IReadOnlyList<PostingSignal> signals, DateTimeOffset now)
    {
        var rawScore = 0;

        foreach (var signal in signals)
        {
            rawScore += signal.SignalType switch
            {
                SignalType.Gone => 50,
                SignalType.Phantom => 40,
                SignalType.Deadline => IsDeadlinePassed(signal.DeadlineDate, now) ? 30 : 0,
                SignalType.Repost => 25,
                SignalType.ParallelPosting => 20,
                SignalType.NoSalary => 15,
                _ => 0
            };
        }

        var ageDays = (now - posting.FirstSeenAt).TotalDays;
        if (ageDays > 90)
        {
            rawScore += 35;
        }
        else if (ageDays > 60)
        {
            rawScore += 20;
        }

        var verifiedDaysAgo = (now - posting.LastVerifiedAt).TotalDays;
        if (verifiedDaysAgo < 7)
        {
            rawScore -= 15;
        }

        var clamped = Math.Max(0, Math.Min(100, rawScore));

        return clamped switch
        {
            >= 75 => "critical",
            >= 50 => "high",
            >= 25 => "moderate",
            _ => "low"
        };
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
