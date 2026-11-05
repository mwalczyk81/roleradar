namespace RoleRadar.Application.Insights;

/// <summary>The instants that bound a window: <c>Start</c> inclusive (null for all time), <c>EndExclusive</c> exclusive.</summary>
public readonly record struct InsightsWindowBounds(DateTimeOffset? Start, DateTimeOffset EndExclusive)
{
    public bool Contains(DateTimeOffset instant) =>
        instant < EndExclusive && (Start is null || instant >= Start.Value);
}

/// <summary>
/// The pure calculations behind GET /api/v1/insights/stats (ADR 0030). No I/O. Window math is identical to the web lens
/// module: <c>last90Days</c> is <c>[asOf-89d 00:00Z, asOf+1d 00:00Z)</c>, <c>allTime</c> has no lower bound and the same
/// upper bound, so the numbers agree with the dashboard pinned to the same as-of date.
/// </summary>
public static class InsightsStatsCalculator
{
    public const int MaxRunYield = 120;

    public static InsightsWindowBounds Bounds(InsightsWindow window, DateOnly asOf)
    {
        var end = Midnight(asOf.AddDays(1));
        return window == InsightsWindow.Last90Days
            ? new InsightsWindowBounds(Midnight(asOf.AddDays(-89)), end)
            : new InsightsWindowBounds(null, end);
    }

    /// <summary>
    /// Whole UTC calendar days from the applied date to the first-response date. A response dated before the applied date
    /// counts as 0 (it stays in the sample rather than being dropped, so the sample size matches the "responded" lens).
    /// </summary>
    public static int ResponseDays(DateTimeOffset appliedAt, DateTimeOffset firstResponseAt)
    {
        var days = DateOnly.FromDateTime(firstResponseAt.UtcDateTime).DayNumber - DateOnly.FromDateTime(appliedAt.UtcDateTime).DayNumber;
        return Math.Max(0, days);
    }

    /// <summary>The median: the middle value for an odd count, the mean of the two middle values for an even count, null when empty.</summary>
    public static double? MedianDays(IEnumerable<int> days)
    {
        ArgumentNullException.ThrowIfNull(days);
        var sorted = days.Order().ToArray();
        if (sorted.Length == 0)
        {
            return null;
        }

        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + (double)sorted[middle]) / 2d;
    }

    /// <summary>Median response time over the applications applied inside the window that have a first response.</summary>
    public static ResponseTimeDto ResponseTime(
        IEnumerable<(DateTimeOffset AppliedAt, DateTimeOffset? FirstResponseAt)> applications,
        InsightsWindow window,
        DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(applications);
        var bounds = Bounds(window, asOf);
        var days = applications
            .Where(a => a.FirstResponseAt is not null && bounds.Contains(a.AppliedAt))
            .Select(a => ResponseDays(a.AppliedAt, a.FirstResponseAt!.Value))
            .ToArray();
        return new ResponseTimeDto(MedianDays(days), days.Length);
    }

    /// <summary>
    /// Applications per Monday-start UTC week, oldest first, zero-filled. Only applications applied inside the window are
    /// counted. <c>last90Days</c> lists every week that overlaps the window; <c>allTime</c> runs from the week of the
    /// earliest application to the week of the as-of date, and is empty when there is no application.
    /// </summary>
    public static IReadOnlyList<WeeklyApplicationsDto> WeeklyApplications(
        IEnumerable<DateTimeOffset> appliedAt,
        InsightsWindow window,
        DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(appliedAt);
        var bounds = Bounds(window, asOf);
        var counts = new Dictionary<DateOnly, int>();
        foreach (var instant in appliedAt.Where(bounds.Contains))
        {
            var week = WeekStart(DateOnly.FromDateTime(instant.UtcDateTime));
            counts[week] = counts.GetValueOrDefault(week) + 1;
        }

        DateOnly first;
        if (window == InsightsWindow.Last90Days)
        {
            first = WeekStart(asOf.AddDays(-89));
        }
        else if (counts.Count == 0)
        {
            return [];
        }
        else
        {
            first = counts.Keys.Min();
        }

        var last = WeekStart(asOf);
        var weeks = new List<WeeklyApplicationsDto>();
        for (var week = first; week <= last; week = week.AddDays(7))
        {
            weeks.Add(new WeeklyApplicationsDto(week, counts.GetValueOrDefault(week)));
        }

        return weeks;
    }

    /// <summary>The latest <see cref="MaxRunYield"/> runs inside the window, oldest first; <c>Truncated</c> when more fell in the window.</summary>
    public static SearchRunYieldDto RunYield(
        IEnumerable<(DateTimeOffset RunDate, int NewPostings)> runs,
        InsightsWindow window,
        DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(runs);
        var bounds = Bounds(window, asOf);
        var inWindow = runs.Where(r => bounds.Contains(r.RunDate)).OrderBy(r => r.RunDate).ToArray();
        var truncated = inWindow.Length > MaxRunYield;
        var kept = truncated ? inWindow[^MaxRunYield..] : inWindow;
        return new SearchRunYieldDto(kept.Select(r => new SearchRunYieldPointDto(r.RunDate, r.NewPostings)).ToArray(), truncated);
    }

    private static DateOnly WeekStart(DateOnly date)
    {
        var sinceMonday = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-sinceMonday);
    }

    private static DateTimeOffset Midnight(DateOnly date) =>
        new(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), TimeSpan.Zero);
}
