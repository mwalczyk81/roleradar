namespace RoleRadar.Tests.Unit.Insights;

using RoleRadar.Application.Insights;
using Xunit;

/// <summary>
/// T063 and T064: the pure calculations behind GET /api/v1/insights/stats. All dates are fictional. Window math is the
/// web lens module's: last90Days is [asOf-89d 00:00Z, asOf+1d 00:00Z), allTime has no lower bound.
/// </summary>
public sealed class InsightsStatsCalculatorTests
{
    private static readonly DateOnly AsOf = new(2026, 9, 30); // a Wednesday; the 90 day window starts Friday 2026-07-03

    private static DateTimeOffset Utc(int year, int month, int day, int hour = 0, int minute = 0) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);

    // ---- T063: median response time -----------------------------------------------------------------------------

    [Fact]
    public void MedianDays_OddCount_IsTheMiddleValue() =>
        Assert.Equal(4d, InsightsStatsCalculator.MedianDays([9, 1, 4, 30, 2]));

    [Fact]
    public void MedianDays_EvenCount_IsTheMeanOfTheTwoMiddleValues() =>
        Assert.Equal(2.5d, InsightsStatsCalculator.MedianDays([1, 2, 3, 40]));

    [Fact]
    public void MedianDays_EvenCountOfEqualValues_IsThatValue() =>
        Assert.Equal(3d, InsightsStatsCalculator.MedianDays([3, 3]));

    [Fact]
    public void MedianDays_SingleValue_IsThatValue() =>
        Assert.Equal(7d, InsightsStatsCalculator.MedianDays([7]));

    [Fact]
    public void MedianDays_Empty_IsNull() =>
        Assert.Null(InsightsStatsCalculator.MedianDays([]));

    [Fact]
    public void MedianDays_ZeroDays_IsZeroNotNull() =>
        Assert.Equal(0d, InsightsStatsCalculator.MedianDays([0, 0, 0]));

    [Fact]
    public void MedianDays_IsNotTheMean() =>
        // mean = 8.8, median = 3
        Assert.Equal(3d, InsightsStatsCalculator.MedianDays([1, 2, 3, 4, 34]));

    [Fact]
    public void ResponseDays_CountsWholeUtcCalendarDays_NotElapsedHours()
    {
        // 2 minutes apart but on different UTC dates is 1 day; 23 hours apart on the same UTC date is 0 days.
        Assert.Equal(1, InsightsStatsCalculator.ResponseDays(Utc(2026, 9, 1, 23, 59), Utc(2026, 9, 2, 0, 1)));
        Assert.Equal(0, InsightsStatsCalculator.ResponseDays(Utc(2026, 9, 1, 0, 0), Utc(2026, 9, 1, 23, 0)));
        Assert.Equal(14, InsightsStatsCalculator.ResponseDays(Utc(2026, 9, 1, 12, 0), Utc(2026, 9, 15, 3, 0)));
    }

    [Fact]
    public void ResponseDays_UsesTheUtcDate_WhenGivenANonUtcOffset()
    {
        // 2026-09-02 00:30 at +02:00 is 2026-09-01 22:30Z, the same UTC date as the applied instant.
        var applied = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
        var response = new DateTimeOffset(2026, 9, 2, 0, 30, 0, TimeSpan.FromHours(2));

        Assert.Equal(0, InsightsStatsCalculator.ResponseDays(applied, response));
    }

    [Fact]
    public void ResponseDays_ADateBeforeApplied_IsClampedToZero() =>
        Assert.Equal(0, InsightsStatsCalculator.ResponseDays(Utc(2026, 9, 10), Utc(2026, 9, 7)));

    [Fact]
    public void ResponseTime_CountsOnlyAnsweredApplicationsInTheWindow_AndKeepsAClampedResponseInTheSample()
    {
        var applications = new (DateTimeOffset AppliedAt, DateTimeOffset? FirstResponseAt)[]
        {
            (Utc(2026, 9, 1), Utc(2026, 9, 4)),     // 3 days
            (Utc(2026, 9, 2), Utc(2026, 9, 12)),    // 10 days
            (Utc(2026, 9, 10), Utc(2026, 9, 7)),    // before applied: counted as 0
            (Utc(2026, 9, 5), null),                // never answered: not in the sample
            (Utc(2026, 6, 1), Utc(2026, 6, 2)),     // outside the 90 day window
        };

        var result = InsightsStatsCalculator.ResponseTime(applications, InsightsWindow.Last90Days, AsOf);

        Assert.Equal(3, result.SampleSize);
        Assert.Equal(3d, result.MedianDays); // 0, 3, 10
    }

    [Fact]
    public void ResponseTime_AllTime_IncludesOldApplications()
    {
        var applications = new (DateTimeOffset AppliedAt, DateTimeOffset? FirstResponseAt)[]
        {
            (Utc(2026, 6, 1), Utc(2026, 6, 3)),     // 2
            (Utc(2026, 9, 1), Utc(2026, 9, 5)),     // 4
        };

        var result = InsightsStatsCalculator.ResponseTime(applications, InsightsWindow.AllTime, AsOf);

        Assert.Equal(2, result.SampleSize);
        Assert.Equal(3d, result.MedianDays);
    }

    [Fact]
    public void ResponseTime_NoAnsweredApplications_HasNullMedianAndZeroSample()
    {
        var result = InsightsStatsCalculator.ResponseTime(
            [(Utc(2026, 9, 1), (DateTimeOffset?)null)], InsightsWindow.Last90Days, AsOf);

        Assert.Null(result.MedianDays);
        Assert.Equal(0, result.SampleSize);
    }

    // ---- T064: window math ---------------------------------------------------------------------------------------

    [Fact]
    public void Bounds_Last90Days_IsAsOfMinus89DaysInclusiveToAsOfPlusOneDayExclusive()
    {
        var bounds = InsightsStatsCalculator.Bounds(InsightsWindow.Last90Days, AsOf);

        Assert.Equal(Utc(2026, 7, 3), bounds.Start);
        Assert.Equal(Utc(2026, 10, 1), bounds.EndExclusive);
    }

    [Fact]
    public void Bounds_AllTime_HasNoLowerBound_AndTheSameUpperBound()
    {
        var bounds = InsightsStatsCalculator.Bounds(InsightsWindow.AllTime, AsOf);

        Assert.Null(bounds.Start);
        Assert.Equal(Utc(2026, 10, 1), bounds.EndExclusive);
    }

    // ---- T064: weekly applications -------------------------------------------------------------------------------

    [Fact]
    public void WeeklyApplications_Last90Days_ListsEveryMondayWeekOverlappingTheWindow_OldestFirst_ZeroFilled()
    {
        var weeks = InsightsStatsCalculator.WeeklyApplications([], InsightsWindow.Last90Days, AsOf);

        Assert.Equal(14, weeks.Count);
        Assert.Equal(new DateOnly(2026, 6, 29), weeks[0].WeekStart);   // the Monday on or before 2026-07-03
        Assert.Equal(new DateOnly(2026, 9, 28), weeks[^1].WeekStart);  // the Monday on or before asOf
        Assert.All(weeks, w => Assert.Equal(DayOfWeek.Monday, w.WeekStart.DayOfWeek));
        Assert.All(weeks, w => Assert.Equal(0, w.Count));
        Assert.Equal(weeks.Select(w => w.WeekStart).OrderBy(d => d).ToList(), weeks.Select(w => w.WeekStart).ToList());
    }

    [Fact]
    public void WeeklyApplications_SundayBelongsToTheWeekBeforeAndMondayStartsANewOne()
    {
        var applied = new[]
        {
            Utc(2026, 9, 27, 23, 59),  // Sunday: week of Monday 2026-09-21
            Utc(2026, 9, 28, 0, 0),    // Monday 00:00Z: week of 2026-09-28
            Utc(2026, 9, 28, 0, 0),
            Utc(2026, 9, 30, 12, 0),   // Wednesday of that week
        };

        var weeks = InsightsStatsCalculator.WeeklyApplications(applied, InsightsWindow.Last90Days, AsOf);

        Assert.Equal(1, weeks.Single(w => w.WeekStart == new DateOnly(2026, 9, 21)).Count);
        Assert.Equal(3, weeks.Single(w => w.WeekStart == new DateOnly(2026, 9, 28)).Count);
        Assert.Equal(4, weeks.Sum(w => w.Count));
    }

    [Fact]
    public void WeeklyApplications_UsesTheUtcInstant_NotTheOffsetItWasWrittenWith()
    {
        // Monday 2026-09-28 01:00 at +02:00 is Sunday 2026-09-27 23:00Z, so it belongs to the week of 2026-09-21.
        var applied = new[] { new DateTimeOffset(2026, 9, 28, 1, 0, 0, TimeSpan.FromHours(2)) };

        var weeks = InsightsStatsCalculator.WeeklyApplications(applied, InsightsWindow.Last90Days, AsOf);

        Assert.Equal(1, weeks.Single(w => w.WeekStart == new DateOnly(2026, 9, 21)).Count);
        Assert.Equal(0, weeks.Single(w => w.WeekStart == new DateOnly(2026, 9, 28)).Count);
    }

    [Fact]
    public void WeeklyApplications_CountsOnlyInWindowApplications_AtBothEdges()
    {
        var applied = new[]
        {
            Utc(2026, 7, 2, 23, 59),   // one minute before the window: excluded, although its week overlaps the window
            Utc(2026, 7, 3, 0, 0),     // first window instant: included
            Utc(2026, 9, 30, 23, 59),  // last day of the window: included
            Utc(2026, 10, 1, 0, 0),    // asOf + 1 day 00:00Z: excluded (the end is exclusive)
        };

        var weeks = InsightsStatsCalculator.WeeklyApplications(applied, InsightsWindow.Last90Days, AsOf);

        Assert.Equal(2, weeks.Sum(w => w.Count));
        Assert.Equal(1, weeks.Single(w => w.WeekStart == new DateOnly(2026, 6, 29)).Count);
        Assert.Equal(1, weeks.Single(w => w.WeekStart == new DateOnly(2026, 9, 28)).Count);
    }

    [Fact]
    public void WeeklyApplications_AllTime_RunsFromTheEarliestApplicationsWeekToTheAsOfWeek_ZeroFilled()
    {
        var applied = new[] { Utc(2026, 8, 12), Utc(2026, 9, 30) }; // Wednesday of 2026-08-10, Wednesday of 2026-09-28

        var weeks = InsightsStatsCalculator.WeeklyApplications(applied, InsightsWindow.AllTime, AsOf);

        Assert.Equal(new DateOnly(2026, 8, 10), weeks[0].WeekStart);
        Assert.Equal(new DateOnly(2026, 9, 28), weeks[^1].WeekStart);
        Assert.Equal(8, weeks.Count);
        Assert.Equal(1, weeks[0].Count);
        Assert.Equal(1, weeks[^1].Count);
        Assert.Equal(6, weeks.Count(w => w.Count == 0));
    }

    [Fact]
    public void WeeklyApplications_AllTime_WithNoApplications_IsEmpty() =>
        Assert.Empty(InsightsStatsCalculator.WeeklyApplications([], InsightsWindow.AllTime, AsOf));

    [Fact]
    public void WeeklyApplications_AllTime_IgnoresApplicationsAfterTheUpperBound() =>
        Assert.Empty(InsightsStatsCalculator.WeeklyApplications([Utc(2026, 10, 5)], InsightsWindow.AllTime, AsOf));

    // ---- T064: search-run yield ----------------------------------------------------------------------------------

    [Fact]
    public void RunYield_IsOldestFirst_AndInWindowOnly()
    {
        var runs = new (DateTimeOffset RunDate, int NewPostings)[]
        {
            (Utc(2026, 9, 20), 5),
            (Utc(2026, 9, 10), 9),
            (Utc(2026, 6, 1), 40),    // outside the 90 day window
            (Utc(2026, 9, 15), 0),
            (Utc(2026, 10, 2), 7),    // after asOf + 1 day
        };

        var yield = InsightsStatsCalculator.RunYield(runs, InsightsWindow.Last90Days, AsOf);

        Assert.False(yield.Truncated);
        Assert.Equal([9, 0, 5], yield.Runs.Select(r => r.NewPostings).ToArray());
        Assert.Equal(
            [Utc(2026, 9, 10), Utc(2026, 9, 15), Utc(2026, 9, 20)],
            yield.Runs.Select(r => r.StartedAt).ToArray());
    }

    [Fact]
    public void RunYield_Exactly120Runs_IsNotTruncated()
    {
        var yield = InsightsStatsCalculator.RunYield(Runs(120), InsightsWindow.AllTime, AsOf);

        Assert.Equal(120, yield.Runs.Count);
        Assert.False(yield.Truncated);
    }

    [Fact]
    public void RunYield_MoreThan120Runs_KeepsTheLatest120_OldestFirst_AndSetsTruncated()
    {
        var yield = InsightsStatsCalculator.RunYield(Runs(125), InsightsWindow.AllTime, AsOf);

        Assert.True(yield.Truncated);
        Assert.Equal(120, yield.Runs.Count);
        // Runs are numbered 1..125 by NewPostings, one per day; the oldest five are dropped.
        Assert.Equal(6, yield.Runs[0].NewPostings);
        Assert.Equal(125, yield.Runs[^1].NewPostings);
        Assert.Equal(yield.Runs.Select(r => r.StartedAt).OrderBy(d => d).ToList(), yield.Runs.Select(r => r.StartedAt).ToList());
    }

    [Fact]
    public void RunYield_NoRuns_IsEmptyAndNotTruncated()
    {
        var yield = InsightsStatsCalculator.RunYield([], InsightsWindow.Last90Days, AsOf);

        Assert.Empty(yield.Runs);
        Assert.False(yield.Truncated);
    }

    private static (DateTimeOffset RunDate, int NewPostings)[] Runs(int count) =>
        Enumerable.Range(1, count)
            .Select(i => (Utc(2026, 5, 1).AddDays(i - 1), i))
            .ToArray();
}
