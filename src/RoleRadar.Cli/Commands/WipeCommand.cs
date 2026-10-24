using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using RoleRadar.Application.Abstractions;
using RoleRadar.Infrastructure.Mongo;

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
        var yesOption = new Option<bool>("--yes")
        {
            Description = $"Confirm wipe directly ('{ConfirmationToken}')."
        };

        wipeCommand.Add(confirmOption);
        wipeCommand.Add(yesOption);

        wipeCommand.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var confirm = parseResult.GetValue(confirmOption);
            var yes = parseResult.GetValue(yesOption);
            if (yes)
            {
                confirm = ConfirmationToken;
            }

            if (string.IsNullOrWhiteSpace(confirm))
            {
                Console.Error.WriteLine($"Wipe refused: requires explicit confirmation '--confirm {ConfirmationToken}' or '--yes'.");
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
            var notes = summary.DroppedCounts.GetValueOrDefault(MongoBootstrapAndIndexes.NotesCollectionName, 0);
            var revisions = summary.DroppedCounts.GetValueOrDefault(MongoBootstrapAndIndexes.NoteRevisionsCollectionName, 0);
            var audits = summary.DroppedCounts.GetValueOrDefault(MongoBootstrapAndIndexes.AuditLogCollectionName, 0);
            var companies = summary.DroppedCounts.GetValueOrDefault(TrackingMongoOptions.CompaniesCollectionName, 0);
            var sources = summary.DroppedCounts.GetValueOrDefault(TrackingMongoOptions.CareerSourcesCollectionName, 0);
            var postings = summary.DroppedCounts.GetValueOrDefault(TrackingMongoOptions.PostingsCollectionName, 0);
            var signals = summary.DroppedCounts.GetValueOrDefault(TrackingMongoOptions.PostingSignalsCollectionName, 0);
            var criteria = summary.DroppedCounts.GetValueOrDefault(TrackingMongoOptions.CriteriaCollectionName, 0);
            var applications = summary.DroppedCounts.GetValueOrDefault(TrackingMongoOptions.ApplicationsCollectionName, 0);
            var appEvents = summary.DroppedCounts.GetValueOrDefault(TrackingMongoOptions.ApplicationEventsCollectionName, 0);
            var contacts = summary.DroppedCounts.GetValueOrDefault(TrackingMongoOptions.ContactsCollectionName, 0);
            var searchRuns = summary.DroppedCounts.GetValueOrDefault(TrackingMongoOptions.SearchRunsCollectionName, 0);

            Console.WriteLine("Wipe completed successfully.");
            Console.WriteLine($"Dropped records: {notes} notes, {revisions} revisions, {audits} audit entries, {companies} companies, {sources} career sources, {postings} postings, {signals} signals, {criteria} criteria, {applications} applications, {appEvents} application events, {contacts} contacts, {searchRuns} search runs.");
            return 0;
        });

        return wipeCommand;
    }
}
