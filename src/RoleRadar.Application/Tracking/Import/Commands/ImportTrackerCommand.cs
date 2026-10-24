using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Import.Pipeline;
using RoleRadar.Domain.Common;

namespace RoleRadar.Application.Tracking.Import.Commands;

public sealed record ImportTrackerCommand(
    string BundleDirectory,
    DateOnly? DefaultDate = null,
    bool Apply = false,
    string? ReportPath = null,
    string? OwnerId = null);

public interface ITrackerImportCoordinator
{
    Task<Result<TrackerImportSummary>> ExecuteAsync(ImportTrackerCommand command, CancellationToken cancellationToken = default);
}

public sealed class TrackerImportCoordinator : ITrackerImportCoordinator
{
    private readonly ITrackerScratchManager _scratchManager;
    private readonly ITrackingContextFactory _contextFactory;
    private readonly ICsvParser _csvParser;
    private readonly ITrackerImportPipelineFactory _pipelineFactory;

    public TrackerImportCoordinator(
        ITrackerScratchManager scratchManager,
        ITrackingContextFactory contextFactory,
        ICsvParser csvParser,
        ITrackerImportPipelineFactory pipelineFactory)
    {
        _scratchManager = scratchManager ?? throw new ArgumentNullException(nameof(scratchManager));
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _csvParser = csvParser ?? throw new ArgumentNullException(nameof(csvParser));
        _pipelineFactory = pipelineFactory ?? throw new ArgumentNullException(nameof(pipelineFactory));
    }

    public async Task<Result<TrackerImportSummary>> ExecuteAsync(
        ImportTrackerCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.BundleDirectory) || !Directory.Exists(command.BundleDirectory))
        {
            return Result<TrackerImportSummary>.Failure(
                ErrorCode.ValidationError,
                $"Bundle directory not found: '{command.BundleDirectory}'.");
        }

        // Validate --report path does not reside in a git work tree
        if (!string.IsNullOrWhiteSpace(command.ReportPath))
        {
            if (IsInsideGitWorkTree(command.ReportPath))
            {
                return Result<TrackerImportSummary>.Failure(
                    ErrorCode.ValidationError,
                    $"Refusing to write report to '{command.ReportPath}': report path must not reside inside a git working tree.");
            }
        }

        // Pre-scan for missing dates when --default-date is omitted
        if (command.DefaultDate is null)
        {
            var dateCheckResult = CheckForMissingOrPartialDates(command.BundleDirectory);
            if (dateCheckResult is not null)
            {
                return Result<TrackerImportSummary>.Failure(dateCheckResult.Value);
            }
        }

        var ownerId = !string.IsNullOrWhiteSpace(command.OwnerId)
            ? command.OwnerId.Trim()
            : "default-owner";

        var targetDbName = _contextFactory.TargetDatabaseName;

        if (!command.Apply)
        {
            // Dry-run mode: ephemerally isolated inside scratch database
            await _scratchManager.CleanupOrphanedScratchDatabasesAsync(targetDbName, cancellationToken: cancellationToken);

            await using var scratchScope = await _scratchManager.CreateScopeAsync(targetDbName, cancellationToken);
            return await RunPipelineAsync(scratchScope.ScratchDatabaseName, ownerId, command, isDryRun: true, cancellationToken);
        }
        else
        {
            // Apply mode: commits persistent writes to target database
            return await RunPipelineAsync(targetDbName, ownerId, command, isDryRun: false, cancellationToken);
        }
    }

    private async Task<Result<TrackerImportSummary>> RunPipelineAsync(
        string databaseName,
        string ownerId,
        ImportTrackerCommand command,
        bool isDryRun,
        CancellationToken cancellationToken)
    {
        var context = new TrackerImportContext
        {
            OwnerId = ownerId,
            BundleDirectory = command.BundleDirectory,
            DefaultDate = command.DefaultDate,
            Apply = command.Apply,
            Summary = { IsDryRun = isDryRun }
        };

        var trackingContext = _contextFactory.CreateContext(databaseName);
        var pipeline = _pipelineFactory.CreatePipeline(trackingContext);

        return await pipeline.ExecuteAsync(context, cancellationToken);
    }

    private static bool IsInsideGitWorkTree(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var dir = new DirectoryInfo(Path.GetDirectoryName(fullPath) ?? fullPath);

        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) ||
                File.Exists(Path.Combine(dir.FullName, ".git")))
            {
                return true;
            }

            dir = dir.Parent;
        }

        return false;
    }

    private ResultError? CheckForMissingOrPartialDates(string bundleDirectory)
    {
        var filesToCheck = new[] { "appendix-rows.csv", "applications.csv" };

        foreach (var file in filesToCheck)
        {
            var fullPath = Path.Combine(bundleDirectory, file);
            if (!File.Exists(fullPath))
            {
                continue;
            }

            using var reader = new StreamReader(fullPath);
            var partialDateCount = 0;

            foreach (var row in _csvParser.Read(reader, hasHeader: true))
            {
                var addedMd = row["added_md"] ?? row["applied_date_md"] ?? row["rejected_date_md"];
                if (string.IsNullOrWhiteSpace(addedMd) ||
                    (!addedMd.Trim().StartsWith("20", StringComparison.Ordinal) && !addedMd.Trim().StartsWith("19", StringComparison.Ordinal)))
                {
                    partialDateCount++;
                }
            }

            if (partialDateCount > 0)
            {
                return new ResultError(
                    ErrorCode.ValidationError,
                    $"Missing required --default-date: {file} has {partialDateCount} rows with undated or partial date fields.");
            }
        }

        return null;
    }
}
