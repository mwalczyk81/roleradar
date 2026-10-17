using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using RoleRadar.Application.Abstractions;

namespace RoleRadar.Cli.Commands;

public static class WipeCommand
{
    public const string ConfirmationToken = "WIPE-ALL-ROLERADAR-DATA";

    public static Command Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var wipeCommand = new Command("wipe", "Wipe all RoleRadar data (requires explicit confirmation token)");

        var confirmOption = new Option<string?>("--confirm")
        {
            Description = $"Explicit confirmation token required to execute wipe ('{ConfirmationToken}')."
        };

        wipeCommand.Add(confirmOption);

        wipeCommand.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var confirm = parseResult.GetValue(confirmOption);
            if (string.IsNullOrWhiteSpace(confirm))
            {
                Console.Error.WriteLine($"Wipe refused: requires explicit confirmation '--confirm {ConfirmationToken}'.");
                return 1;
            }

            var service = services.GetRequiredService<IBackupArchiveService>();
            var outcome = await service.WipeDatabaseAsync(confirm, ct);

            if (!outcome.IsSuccess || outcome.Value is null)
            {
                Console.Error.WriteLine($"Wipe failed: {outcome.Error?.Message}");
                return 1;
            }

            var summary = outcome.Value;
            var notes = summary.DroppedCounts.GetValueOrDefault("notes", 0);
            var revisions = summary.DroppedCounts.GetValueOrDefault("note_revisions", 0);
            var audits = summary.DroppedCounts.GetValueOrDefault("audit_log", 0);

            Console.WriteLine("Wipe completed successfully.");
            Console.WriteLine($"Dropped records: {notes} notes, {revisions} revisions, {audits} audit entries.");
            return 0;
        });

        return wipeCommand;
    }
}
