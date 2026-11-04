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

        var command = new Command("reconcile-read-models", "Reconcile derived stored fields for read models (postings, applications, criteria)");

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

            var service = services.GetRequiredService<IReadModelReconciliationService>();
            var postReport = await service.ReconcilePostingsAsync(isDryRun, ct);
            var appReport = await service.ReconcileApplicationsAsync(isDryRun, ct);
            var criteriaReport = await service.ReconcileCriteriaAsync(isDryRun, ct);

            Console.WriteLine("--- Reconciliation Summary ---");
            Console.WriteLine($"Mode: {(postReport.IsDryRun ? "DRY RUN" : "COMMITTED")}");
            Console.WriteLine($"Postings Examined: {postReport.PostingsExamined}");
            Console.WriteLine($"Postings Updated: {postReport.PostingsUpdated}");
            Console.WriteLine($"Applications Examined: {appReport.ApplicationsExamined}");
            Console.WriteLine($"Applications Updated: {appReport.ApplicationsUpdated}");
            Console.WriteLine($"Criteria Examined: {criteriaReport.CriteriaExamined}");
            Console.WriteLine($"Criteria Updated: {criteriaReport.CriteriaUpdated}");
            return 0;
        });

        return command;
    }
}
