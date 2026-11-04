namespace RoleRadar.Tests.Unit.Insights;

using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Insights;
using RoleRadar.Application.Reading;
using RoleRadar.Application.Reading.Models;
using RoleRadar.Domain.Common;
using Xunit;

/// <summary>
/// T065: the stats handler is an <see cref="IQueryHandler{TQuery,TResponse}"/> that reads only through
/// <see cref="IOwnerReadSeam"/> (applications and search runs), caps each source at 50,000 projected rows with a typed
/// failure instead of an exception, and rejects an as-of date more than one day in the future.
/// </summary>
public sealed class GetInsightsStatsQueryTests
{
    private static readonly OwnerId Owner = new("owner-fictional-stats");
    private static readonly DateOnly AsOf = new(2026, 9, 30);

    private static DateTimeOffset Utc(int year, int month, int day) => new(year, month, day, 0, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock(DateTime utcNow) : IUtcClock
    {
        public DateTime UtcNow { get; } = utcNow;
    }

    private sealed class RecordingSeam(
        IReadOnlyList<ApplicationReadModel>? applications = null,
        IReadOnlyList<SearchRunReadModel>? runs = null) : IOwnerReadSeam
    {
        public List<string> Calls { get; } = [];

        public List<OwnerId> Owners { get; } = [];

        public IQueryable<ApplicationReadModel> QueryApplications(OwnerId ownerId)
        {
            Calls.Add(nameof(QueryApplications));
            Owners.Add(ownerId);
            return (applications ?? []).AsQueryable();
        }

        public IQueryable<SearchRunReadModel> QuerySearchRuns(OwnerId ownerId)
        {
            Calls.Add(nameof(QuerySearchRuns));
            Owners.Add(ownerId);
            return (runs ?? []).AsQueryable();
        }

        public IQueryable<PostingReadModel> QueryPostings(OwnerId ownerId) => throw new InvalidOperationException("The stats read must not read postings.");

        public IQueryable<CompanyReadModel> QueryCompanies(OwnerId ownerId) => throw new InvalidOperationException("The stats read must not read companies.");

        public IQueryable<NoteReadModel> QueryNotes(OwnerId ownerId, bool includeContent = false) => throw new InvalidOperationException("The stats read must not read notes.");

        public IQueryable<CriterionReadModel> QueryCriteria(OwnerId ownerId) => throw new InvalidOperationException("The stats read must not read criteria.");
    }

    private static GetInsightsStatsQueryHandler HandlerFor(RecordingSeam seam, DateTime? now = null) =>
        new(seam, new FixedClock(now ?? new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc)));

    private static GetInsightsStatsQuery QueryFor(InsightsWindow window = InsightsWindow.Last90Days, DateOnly? asOf = null) =>
        new(Owner.Value, window, asOf ?? AsOf);

    private static ApplicationReadModel App(DateTimeOffset appliedAt, DateTimeOffset? firstResponseAt = null) =>
        new() { Id = Guid.NewGuid(), AppliedAt = appliedAt, FirstResponseAt = firstResponseAt };

    private static SearchRunReadModel Run(DateTimeOffset runDate, int newPostings) =>
        new() { Id = Guid.NewGuid(), RunDate = runDate, NewPostings = newPostings };

    [Fact]
    public void Handler_IsAQueryHandler_NotACommandHandler()
    {
        var handlerType = typeof(GetInsightsStatsQueryHandler);

        Assert.Contains(handlerType.GetInterfaces(), i =>
            i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQueryHandler<,>));
        Assert.DoesNotContain(handlerType.GetInterfaces(), i =>
            i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommandHandler<,>));
        Assert.True(typeof(IQuery<InsightsStatsDto>).IsAssignableFrom(typeof(GetInsightsStatsQuery)));
    }

    [Fact]
    public async Task Handler_ReadsOnlyApplicationsAndSearchRuns_ThroughTheSeam_ForTheQueryOwner()
    {
        var seam = new RecordingSeam();

        var result = await HandlerFor(seam).HandleAsync(QueryFor(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(["QueryApplications", "QuerySearchRuns"], seam.Calls.Order().ToArray());
        Assert.All(seam.Owners, o => Assert.Equal(Owner, o));
    }

    [Fact]
    public async Task Handler_ComputesTheStats_ForTheWindow()
    {
        var seam = new RecordingSeam(
            applications:
            [
                App(Utc(2026, 9, 1), Utc(2026, 9, 4)),
                App(Utc(2026, 9, 2), Utc(2026, 9, 12)),
                App(Utc(2026, 9, 28)),
                App(Utc(2026, 6, 1), Utc(2026, 6, 2)),   // outside the 90 day window
                App(Utc(2026, 10, 1), Utc(2026, 10, 2)), // asOf + 1 day: outside
            ],
            runs:
            [
                Run(Utc(2026, 9, 20), 5),
                Run(Utc(2026, 9, 10), 9),
                Run(Utc(2026, 6, 1), 40),
            ]);

        var result = await HandlerFor(seam).HandleAsync(QueryFor(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Message);
        var dto = result.Value!;
        Assert.Equal(AsOf, dto.AsOf);
        Assert.Equal("last90Days", dto.Window);
        Assert.Equal(new DateOnly(2026, 7, 3), dto.WindowStart);
        Assert.Equal(2, dto.ResponseTime.SampleSize);
        Assert.Equal(6.5d, dto.ResponseTime.MedianDays); // 3 and 10 days: mean of the two middle values is 6.5
        Assert.Equal(3, dto.WeeklyApplications.Sum(w => w.Count));
        Assert.Equal([9, 5], dto.SearchRunYield.Runs.Select(r => r.NewPostings).ToArray());
        Assert.False(dto.SearchRunYield.Truncated);
    }

    [Fact]
    public async Task Handler_AllTime_HasNoWindowStart_AndIncludesOldApplications()
    {
        var seam = new RecordingSeam(applications: [App(Utc(2026, 6, 1), Utc(2026, 6, 3)), App(Utc(2026, 9, 1), Utc(2026, 9, 5))]);

        var result = await HandlerFor(seam).HandleAsync(QueryFor(InsightsWindow.AllTime), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal("allTime", result.Value!.Window);
        Assert.Null(result.Value.WindowStart);
        Assert.Equal(2, result.Value.ResponseTime.SampleSize);
    }

    [Fact]
    public async Task Handler_WithNoData_ReturnsAnEmptyButSuccessfulResult()
    {
        var result = await HandlerFor(new RecordingSeam()).HandleAsync(QueryFor(InsightsWindow.AllTime), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.ResponseTime.MedianDays);
        Assert.Equal(0, result.Value.ResponseTime.SampleSize);
        Assert.Empty(result.Value.WeeklyApplications);
        Assert.Empty(result.Value.SearchRunYield.Runs);
    }

    [Fact]
    public async Task Handler_AtExactlyTheRowCap_Succeeds()
    {
        var applications = Enumerable.Range(0, GetInsightsStatsQueryHandler.RowCap).Select(_ => App(Utc(2026, 9, 1))).ToList();

        var result = await HandlerFor(new RecordingSeam(applications: applications))
            .HandleAsync(QueryFor(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Message);
    }

    [Fact]
    public async Task Handler_AboveTheRowCap_FromApplications_ReturnsATypedFailure_AndDoesNotThrow()
    {
        var applications = Enumerable.Range(0, GetInsightsStatsQueryHandler.RowCap + 1).Select(_ => App(Utc(2026, 9, 1))).ToList();

        var result = await HandlerFor(new RecordingSeam(applications: applications))
            .HandleAsync(QueryFor(), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, result.Error!.Value.Code);
        Assert.Contains("50,000", result.Error.Value.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Handler_AboveTheRowCap_FromSearchRuns_ReturnsATypedFailure_AndDoesNotThrow()
    {
        var runs = Enumerable.Range(0, GetInsightsStatsQueryHandler.RowCap + 1).Select(_ => Run(Utc(2026, 9, 1), 1)).ToList();

        var result = await HandlerFor(new RecordingSeam(runs: runs))
            .HandleAsync(QueryFor(), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, result.Error!.Value.Code);
    }

    [Fact]
    public async Task Handler_AsOfOneDayInTheFuture_IsAllowed_AsOfTwoDaysInTheFuture_IsAValidationFailure()
    {
        var now = new DateTime(2026, 11, 4, 23, 59, 0, DateTimeKind.Utc);

        var tomorrow = await HandlerFor(new RecordingSeam(), now)
            .HandleAsync(QueryFor(asOf: new DateOnly(2026, 11, 5)), TestContext.Current.CancellationToken);
        var dayAfter = await HandlerFor(new RecordingSeam(), now)
            .HandleAsync(QueryFor(asOf: new DateOnly(2026, 11, 6)), TestContext.Current.CancellationToken);

        Assert.True(tomorrow.IsSuccess, tomorrow.Error?.Message);
        Assert.False(dayAfter.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, dayAfter.Error!.Value.Code);
        Assert.Contains("asOf", dayAfter.Error.Value.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Handler_AFutureAsOf_DoesNotReadAnything()
    {
        var seam = new RecordingSeam();

        _ = await HandlerFor(seam).HandleAsync(QueryFor(asOf: new DateOnly(2030, 1, 1)), TestContext.Current.CancellationToken);

        Assert.Empty(seam.Calls);
    }
}
