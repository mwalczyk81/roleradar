using System.CommandLine;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Import;
using RoleRadar.Application.Tracking.Import.Commands;

namespace RoleRadar.Cli.Commands;

public static class TrackerImportCommand
{
    public static Command Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var command = new Command(
            "tracker-import",
            "Import job search tracker bundle into RoleRadar (previews by default; use --apply to write changes, unlike import which writes by default)");

        var bundleDirArgument = new Argument<string>("bundle-directory")
        {
            Description = "Path to directory containing tracker files (postings.json, watchlist-companies.csv, applications.csv, appendix-rows.csv, company-aliases.csv)."
        };

        var defaultDateOption = new Option<string?>("--default-date")
        {
            Description = "Explicit default date for inferring partial dates or missing dates (format YYYY-MM-DD). Required when rows lack a usable date."
        };

        var applyOption = new Option<bool>("--apply")
        {
            Description = "Apply and commit imported tracker data persistently to the database (dry-run preview is the default)."
        };

        var reportOption = new Option<string?>("--report")
        {
            Description = "Path to write detailed UTF-8 outcome report (refuses paths inside a git working tree)."
        };

        command.Add(bundleDirArgument);
        command.Add(defaultDateOption);
        command.Add(applyOption);
        command.Add(reportOption);

        command.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            try
            {
                var bundleDir = parseResult.GetRequiredValue(bundleDirArgument);
                var defaultDateStr = parseResult.GetValue(defaultDateOption);
                var apply = parseResult.GetValue(applyOption);
                var reportPath = parseResult.GetValue(reportOption);

                DateOnly? defaultDate = null;
                if (!string.IsNullOrWhiteSpace(defaultDateStr))
                {
                    if (!DateOnly.TryParseExact(defaultDateStr.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                    {
                        await Console.Error.WriteLineAsync($"Invalid --default-date '{defaultDateStr}'. Expected ISO format 'YYYY-MM-DD'.");
                        return 1;
                    }
                    defaultDate = parsed;
                }

                using var scope = services.CreateScope();
                var coordinator = scope.ServiceProvider.GetRequiredService<ITrackerImportCoordinator>();

                var importCommand = new ImportTrackerCommand(
                    BundleDirectory: bundleDir,
                    DefaultDate: defaultDate,
                    Apply: apply,
                    ReportPath: reportPath);

                var result = await coordinator.ExecuteAsync(importCommand, ct);
                if (!result.IsSuccess)
                {
                    var error = result.Error!.Value;
                    await Console.Error.WriteLineAsync($"Tracker import failed: {error.Code}");
                    return 1;
                }

                var summary = result.Value!;
                Console.WriteLine(summary.IsDryRun
                    ? "=== RoleRadar Tracker Import (Dry Run) ==="
                    : "=== RoleRadar Tracker Import ===");

                Console.WriteLine($"Companies Created:       {summary.CompaniesCreated}");
                Console.WriteLine($"Companies Updated:       {summary.CompaniesUpdated}");
                Console.WriteLine($"Companies Unchanged:     {summary.CompaniesUnchanged}");
                Console.WriteLine($"Postings Created:        {summary.PostingsCreated}");
                Console.WriteLine($"Postings Updated:        {summary.PostingsUpdated}");
                Console.WriteLine($"Postings Unchanged:      {summary.PostingsUnchanged}");
                Console.WriteLine($"Applications Created:    {summary.ApplicationsCreated}");
                Console.WriteLine($"Standalone Notes:        {summary.StandaloneNotesCreated}");
                Console.WriteLine($"Criteria Created:        {summary.CriteriaCreated}");
                Console.WriteLine($"Signals Created:         {summary.SignalsCreated}");
                Console.WriteLine($"Mapped to Event Notes:   {summary.MappedToEventNotes}");
                Console.WriteLine($"Resume Variants Defaulted: {summary.ResumeVariantsDefaulted}");
                Console.WriteLine($"Dates Inferred:          {summary.DatesInferred}");
                Console.WriteLine($"Dates Defaulted:         {summary.DatesDefaulted}");
                Console.WriteLine($"Dates Flagged:           {summary.DatesFlagged}");
                Console.WriteLine($"Unclaimed Applied Roles: {summary.UnclaimedAppliedPostings}");
                Console.WriteLine($"Errors Encountered:      {summary.Errors.Count}");

                if (summary.FileReconciliations.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("File Reconciliation:");
                    Console.WriteLine($"{"File",-25} {"Read",8} {"Created",8} {"Updated",8} {"Unchanged",10} {"Skipped",8} {"Errored",8}");
                    Console.WriteLine(new string('-', 81));
                    foreach (var file in summary.FileReconciliations.Values.OrderBy(f => f.FileName, StringComparer.OrdinalIgnoreCase))
                    {
                        if (file.IsAbsent)
                        {
                            Console.WriteLine($"{file.FileName,-25} {"absent",8} {"-",8} {"-",8} {"-",10} {"-",8} {"-",8}");
                        }
                        else
                        {
                            Console.WriteLine($"{file.FileName,-25} {file.Read,8} {file.Created,8} {file.Updated,8} {file.Unchanged,10} {file.TotalSkipped,8} {file.Errored,8}");
                        }
                    }
                }

                if (summary.UnmappedValues.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("Unmapped Values:");
                    foreach (var (field, count) in summary.UnmappedValues.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"  {field}: {count}");
                    }
                }

                if (summary.FieldTruncations.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("Field Truncations:");
                    foreach (var (field, count) in summary.FieldTruncations.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"  {field}: {count}");
                    }
                }

                if (summary.DispositionHistogram.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("Post-Run Disposition Histogram:");
                    foreach (var (disposition, count) in summary.DispositionHistogram.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"  {disposition}: {count}");
                    }
                }

                if (summary.Errors.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("Encountered Row Errors:");
                    foreach (var err in summary.Errors)
                    {
                        Console.WriteLine($"  [{err.SourceFile}:{err.LineNumber}] {err.ErrorCode}");
                    }

                    return 1;
                }

                return 0;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var correlationHint = Guid.NewGuid().ToString("N")[..8];
                await Console.Error.WriteLineAsync($"Tracker import failed unexpectedly with {ex.GetType().Name} (ref: {correlationHint}). Inspect logs or rerun with diagnostic logging.");
                return 1;
            }
        });

        return command;
    }
}
