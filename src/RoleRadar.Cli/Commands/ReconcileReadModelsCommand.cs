namespace RoleRadar.Cli.Commands;

using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using RoleRadar.Infrastructure.Mongo.Reconcile;

public static class ReconcileReadModelsCommand
{
    public const string ConfirmationToken = "RECONCILE-READ-MODELS";

    public static Command Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var command = new Command("reconcile-read-models", "Reconcile derived stored fields for read models (postings, applications)");

        var confirmOption = new Option<string?>("--confirm")
        {
            Description = $"Explicit confirmation token required to apply changes ('{ConfirmationToken}'). Without this flag, runs in dry-run mode."
        };
        var yesOption = new Option<bool>("--yes")
        {
            Description = $"Confirm reconcile directly ('{ConfirmationToken}')."
        };

        command.Add(confirmOption);
        command.Add(yesOption);

        command.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var confirm = parseResult.GetValue(confirmOption);
            var yes = parseResult.GetValue(yesOption);
            if (yes)
            {
                confirm = ConfirmationToken;
            }

            var isConfirmed = string.Equals(confirm, ConfirmationToken, StringComparison.Ordinal);
            var isDryRun = !isConfirmed;

            if (isDryRun)
            {
                Console.WriteLine("Running in DRY-RUN mode. No documents will be modified.");
                Console.WriteLine($"To execute changes, re-run with: --confirm {ConfirmationToken}");
            }
            else
            {
                Console.WriteLine("Running in CONFIRMED mode. Applying updates to database...");
            }

            var service = services.GetRequiredService<ReadModelReconciliationService>();
            var report = await service.ReconcilePostingsAsync(isDryRun, ct);

            Console.WriteLine("--- Reconciliation Summary ---");
            Console.WriteLine($"Mode: {(report.IsDryRun ? "DRY RUN" : "COMMITTED")}");
            Console.WriteLine($"Postings Examined: {report.PostingsExamined}");
            Console.WriteLine($"Postings Updated: {report.PostingsUpdated}");
            return 0;
        });

        return command;
    }
}
