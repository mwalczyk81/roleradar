namespace RoleRadar.Application.Insights;

/// <summary>The window is written as the API names it, "last90Days" or "allTime" (see <see cref="InsightsWindows"/>).</summary>
public sealed record InsightsStatsDto(
    DateOnly AsOf,
    string Window,
    DateOnly? WindowStart,
    ResponseTimeDto ResponseTime,
    IReadOnlyList<WeeklyApplicationsDto> WeeklyApplications,
    SearchRunYieldDto SearchRunYield);

/// <summary>Median days from applied to first response, and how many responses it is based on.</summary>
public sealed record ResponseTimeDto(double? MedianDays, int SampleSize);

/// <summary>Applications whose applied date falls in the Monday-start UTC week beginning on <c>WeekStart</c>.</summary>
public sealed record WeeklyApplicationsDto(DateOnly WeekStart, int Count);

/// <summary>Search-run yield, oldest first, at most the latest <see cref="InsightsStatsCalculator.MaxRunYield"/> runs.</summary>
public sealed record SearchRunYieldDto(IReadOnlyList<SearchRunYieldPointDto> Runs, bool Truncated);

public sealed record SearchRunYieldPointDto(DateTimeOffset StartedAt, int NewPostings);
