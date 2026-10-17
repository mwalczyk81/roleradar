using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Import.Commands;

namespace RoleRadar.Cli.Commands;

public static class ImportCommand
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static Command Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var importCommand = new Command("import", "Import UTF-8 markdown files into RoleRadar notes");

        var filesArgument = new Argument<string[]>("files")
        {
            Arity = ArgumentArity.OneOrMore,
            Description = "Paths to UTF-8 markdown files to import. Source file identity defaults to the file name so moving a file between folders is safe, but renaming it orphans its sections."
        };

        var sourceIdOption = new Option<string?>("--source-id")
        {
            Description = "Override source file identity (only valid when importing a single file). Defaults to the file name so moving a file between folders is safe, but renaming it orphans its sections."
        };

        var areaOption = new Option<string>("--area")
        {
            DefaultValueFactory = _ => "imported",
            Description = "Area label assigned to imported notes (default 'imported')."
        };

        var tagsOption = new Option<string[]>("--tags")
        {
            AllowMultipleArgumentsPerToken = true,
            Description = "Tags applied to imported notes."
        };

        var defaultDateOption = new Option<string?>("--default-date")
        {
            Description = "Explicit default date for undated provenance tags (format YYYY-MM-DD)."
        };

        var dryRunOption = new Option<bool>("--dry-run")
        {
            Description = "Run full parse and reconciliation with zero database writes and zero audit entries."
        };

        var verboseOption = new Option<bool>("--verbose")
        {
            Description = "Display note titles in summary output."
        };

        var jsonOption = new Option<bool>("--json")
        {
            Description = "Emit structured JSON summary output."
        };

        importCommand.Add(filesArgument);
        importCommand.Add(sourceIdOption);
        importCommand.Add(areaOption);
        importCommand.Add(tagsOption);
        importCommand.Add(defaultDateOption);
        importCommand.Add(dryRunOption);
        importCommand.Add(verboseOption);
        importCommand.Add(jsonOption);

        importCommand.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var files = parseResult.GetRequiredValue(filesArgument);
            var sourceId = parseResult.GetValue(sourceIdOption);
            var area = parseResult.GetValue(areaOption) ?? "imported";
            var tags = parseResult.GetValue(tagsOption);
            var defaultDate = parseResult.GetValue(defaultDateOption);
            var dryRun = parseResult.GetValue(dryRunOption);
            var verbose = parseResult.GetValue(verboseOption);
            var json = parseResult.GetValue(jsonOption);

            using var scope = services.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<ImportMarkdownCommand, ImportMarkdownSummaryDto>>();

            var command = new ImportMarkdownCommand(
                FilePaths: files,
                Area: area,
                Tags: tags,
                DefaultDate: defaultDate,
                DryRun: dryRun,
                SourceFileId: sourceId,
                Actor: "cli-import");

            var outcome = await handler.HandleAsync(command, ct);

            if (!outcome.IsSuccess)
            {
                var error = outcome.Error!.Value;
                await Console.Error.WriteLineAsync($"Import failed ({error.Code}): {error.Message}");
                return error.Code == ErrorCode.InvalidUtf8 ? 2 : 1;
            }

            var summary = outcome.Value!;
            if (json)
            {
                var jsonStr = JsonSerializer.Serialize(summary, JsonOpts);
                Console.WriteLine(jsonStr);
                return 0;
            }

            Console.WriteLine(dryRun ? "=== RoleRadar Import (Dry Run) ===" : "=== RoleRadar Import ===");
            Console.WriteLine($"Files Processed: {summary.FilesProcessed}");
            Console.WriteLine($"Created:         {summary.CreatedCount}");
            Console.WriteLine($"Updated:         {summary.UpdatedCount}");
            Console.WriteLine($"Unchanged:       {summary.UnchangedCount}");
            Console.WriteLine($"Conflicts:       {summary.ConflictCount}");
            Console.WriteLine($"Orphans:         {summary.OrphanCount}");

            if (verbose)
            {
                if (summary.Conflicts.Count > 0)
                {
                    Console.WriteLine("\nConflicts:");
                    foreach (var c in summary.Conflicts)
                    {
                        Console.WriteLine($"  - {c.Title} ({c.ImportKey}): {c.Reason}");
                    }
                }

                if (summary.Orphans.Count > 0)
                {
                    Console.WriteLine("\nOrphans:");
                    foreach (var o in summary.Orphans)
                    {
                        Console.WriteLine($"  - {o.Title} ({o.ImportKey})");
                    }
                }
            }

            return 0;
        });

        return importCommand;
    }
}
