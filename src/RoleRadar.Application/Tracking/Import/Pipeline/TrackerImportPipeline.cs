using RoleRadar.Application.Abstractions;
using RoleRadar.Domain.Tracking;

namespace RoleRadar.Application.Tracking.Import.Pipeline;

public sealed class TrackerImportContext
{
    public required string OwnerId { get; init; }
    public required string BundleDirectory { get; init; }
    public DateOnly? DefaultDate { get; init; }
    public bool Apply { get; init; }
    public TrackerImportSummary Summary { get; } = new();

    public Dictionary<string, string> CompanyAliases { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, Company> CanonicalCompanies { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, Posting> PostingsByReqOrUrl { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, Posting> PostingsByCompanyAndTitle { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<Guid> ClaimedPostingIds { get; } = new();
}

public interface ITrackerImportStep
{
    Task<Result<bool>> ExecuteAsync(TrackerImportContext context, CancellationToken cancellationToken = default);
}

public interface ITrackerImportPipelineFactory
{
    TrackerImportPipeline CreatePipeline(ITrackingTransactionContext context);
}

public sealed class TrackerImportPipelineFactory : ITrackerImportPipelineFactory
{
    private readonly ICsvParser _csvParser;
    private readonly Func<int, Task>? _applicationsFaultHook;

    public TrackerImportPipelineFactory(ICsvParser csvParser, Func<int, Task>? applicationsFaultHook = null)
    {
        _csvParser = csvParser ?? throw new ArgumentNullException(nameof(csvParser));
        _applicationsFaultHook = applicationsFaultHook;
    }

    public TrackerImportPipeline CreatePipeline(ITrackingTransactionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var criteriaStep = new CriteriaSeedingStep(context);
        var aliasesStep = new CompanyAliasesStep(_csvParser);
        var companiesStep = new WatchlistCompaniesStep(_csvParser, context);
        var postingsStep = new PostingsJsonStep(context, criteriaStep);
        var appendixStep = new AppendixRowsStep(_csvParser, context, criteriaStep);
        var applicationsStep = new ApplicationsStep(_csvParser, context, _applicationsFaultHook);

        var steps = new ITrackerImportStep[]
        {
            new CriteriaSeedingPipelineStep(criteriaStep),
            aliasesStep,
            companiesStep,
            postingsStep,
            appendixStep,
            applicationsStep
        };

        return new TrackerImportPipeline(steps);
    }
}

internal sealed class CriteriaSeedingPipelineStep : ITrackerImportStep
{
    private readonly CriteriaSeedingStep _step;

    public CriteriaSeedingPipelineStep(CriteriaSeedingStep step)
    {
        _step = step;
    }

    public async Task<Result<bool>> ExecuteAsync(TrackerImportContext context, CancellationToken cancellationToken = default)
    {
        // When --default-date is absent, use today's UTC date as the effective date for seeded criteria.
        var effectiveDate = context.DefaultDate ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var seedResult = await _step.SeedPredefinedCriteriaAsync(context.OwnerId, effectiveDate, cancellationToken);
        if (!seedResult.IsSuccess)
        {
            return Result<bool>.Failure(seedResult.Error!.Value);
        }

        if (seedResult.Value is not null)
        {
            context.Summary.CriteriaCreated += seedResult.Value.CreatedCount;
        }

        return Result<bool>.Success(true);
    }
}

public sealed class TrackerImportPipeline
{
    private readonly IReadOnlyList<ITrackerImportStep> _steps;

    public TrackerImportPipeline(IReadOnlyList<ITrackerImportStep> steps)
    {
        _steps = steps ?? throw new ArgumentNullException(nameof(steps));
    }

    public async Task<Result<TrackerImportSummary>> ExecuteAsync(
        TrackerImportContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var knownFiles = new[]
        {
            "company-aliases.csv",
            "watchlist-companies.csv",
            "postings.json",
            "appendix-rows.csv",
            "applications.csv"
        };

        foreach (var file in knownFiles)
        {
            var fullPath = Path.Combine(context.BundleDirectory, file);
            var recon = context.Summary.GetOrCreateFileReconciliation(file);
            if (!File.Exists(fullPath))
            {
                recon.IsAbsent = true;
            }
        }

        foreach (var step in _steps)
        {
            var result = await step.ExecuteAsync(context, cancellationToken);
            if (!result.IsSuccess)
            {
                return Result<TrackerImportSummary>.Failure(result.Error!.Value);
            }
        }

        context.Summary.DispositionHistogram.Clear();
        var distinctPostings = context.PostingsByCompanyAndTitle.Values
            .Concat(context.PostingsByReqOrUrl.Values)
            .DistinctBy(p => p.Id);

        foreach (var p in distinctPostings)
        {
            var dispName = p.Disposition.ToSnakeCase();
            context.Summary.DispositionHistogram.TryGetValue(dispName, out var count);
            context.Summary.DispositionHistogram[dispName] = count + 1;
        }

        return Result<TrackerImportSummary>.Success(context.Summary);
    }
}
