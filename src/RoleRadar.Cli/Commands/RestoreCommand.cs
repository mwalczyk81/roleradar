using System.CommandLine;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using RoleRadar.Application.Abstractions;
using RoleRadar.Infrastructure.Mongo;

namespace RoleRadar.Cli.Commands;

public static class RestoreCommand
{
    public static Command Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var restoreCommand = new Command("restore", "Restore RoleRadar data from an encrypted backup archive");

        var fileArgument = new Argument<string>("file")
        {
            Description = "Path to the .rrbak backup archive file."
        };

        var forceOverwriteOption = new Option<bool>("--force-overwrite")
        {
            Description = "Allow overwriting non-empty datastore (drops existing collections)."
        };

        var passphraseEnvOption = new Option<string?>("--passphrase-env")
        {
            Description = "Read passphrase from the specified environment variable."
        };

        var passphraseStdinOption = new Option<bool>("--passphrase-stdin")
        {
            Description = "Read passphrase from standard input."
        };

        var forbiddenPassphraseOption = new Option<string?>("--passphrase")
        {
            Description = "Forbidden: Passphrase must never be passed via CLI argument.",
            Arity = ArgumentArity.ZeroOrMore
        };
        forbiddenPassphraseOption.Validators.Add(result =>
        {
            result.AddError("Supplying passphrase via CLI argument is forbidden for security. Use interactive prompt, --passphrase-env, or --passphrase-stdin.");
        });

        restoreCommand.Add(fileArgument);
        restoreCommand.Add(forceOverwriteOption);
        restoreCommand.Add(passphraseEnvOption);
        restoreCommand.Add(passphraseStdinOption);
        restoreCommand.Add(forbiddenPassphraseOption);

        restoreCommand.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var file = parseResult.GetRequiredValue(fileArgument);
            var force = parseResult.GetValue(forceOverwriteOption);
            var envVar = parseResult.GetValue(passphraseEnvOption);
            var fromStdin = parseResult.GetValue(passphraseStdinOption);

            string? passphrase = null;

            if (!string.IsNullOrWhiteSpace(envVar))
            {
                passphrase = Environment.GetEnvironmentVariable(envVar);
                if (string.IsNullOrEmpty(passphrase))
                {
                    Console.Error.WriteLine($"Environment variable '{envVar}' is not set or empty.");
                    return 1;
                }
            }
            else if (fromStdin)
            {
                passphrase = Console.In.ReadLine();
                if (string.IsNullOrEmpty(passphrase))
                {
                    Console.Error.WriteLine("Passphrase read from stdin was empty.");
                    return 1;
                }
            }
            else
            {
                if (Console.IsInputRedirected)
                {
                    Console.Error.WriteLine("Standard input is redirected. Provide passphrase via --passphrase-env or --passphrase-stdin.");
                    return 1;
                }

                Console.Write("Enter backup passphrase: ");
                passphrase = ReadPasswordNoEcho();
            }

            var service = services.GetRequiredService<IBackupArchiveService>();
            var outcome = await service.RestoreBackupAsync(file, passphrase, force, ct);

            if (!outcome.IsSuccess || outcome.Value is null)
            {
                Console.Error.WriteLine($"Restore failed: {outcome.Error?.Message}");
                return 1;
            }

            var summary = outcome.Value;
            var notes = summary.RestoredCounts.GetValueOrDefault(MongoBootstrapAndIndexes.NotesCollectionName, 0);
            var revisions = summary.RestoredCounts.GetValueOrDefault(MongoBootstrapAndIndexes.NoteRevisionsCollectionName, 0);
            var audits = summary.RestoredCounts.GetValueOrDefault(MongoBootstrapAndIndexes.AuditLogCollectionName, 0);
            var companies = summary.RestoredCounts.GetValueOrDefault(TrackingMongoOptions.CompaniesCollectionName, 0);
            var sources = summary.RestoredCounts.GetValueOrDefault(TrackingMongoOptions.CareerSourcesCollectionName, 0);
            var postings = summary.RestoredCounts.GetValueOrDefault(TrackingMongoOptions.PostingsCollectionName, 0);
            var signals = summary.RestoredCounts.GetValueOrDefault(TrackingMongoOptions.PostingSignalsCollectionName, 0);
            var criteria = summary.RestoredCounts.GetValueOrDefault(TrackingMongoOptions.CriteriaCollectionName, 0);
            var applications = summary.RestoredCounts.GetValueOrDefault(TrackingMongoOptions.ApplicationsCollectionName, 0);
            var appEvents = summary.RestoredCounts.GetValueOrDefault(TrackingMongoOptions.ApplicationEventsCollectionName, 0);
            var contacts = summary.RestoredCounts.GetValueOrDefault(TrackingMongoOptions.ContactsCollectionName, 0);
            var searchRuns = summary.RestoredCounts.GetValueOrDefault(TrackingMongoOptions.SearchRunsCollectionName, 0);

            var totalRestored = summary.RestoredCounts.Values.Sum();
            if (totalRestored == 0)
            {
                Console.Error.WriteLine("Restore failed: Zero documents were restored.");
                Console.Error.WriteLine($"Restored counts: {notes} notes, {revisions} revisions, {audits} audit entries, {companies} companies, {sources} career sources, {postings} postings, {signals} signals, {criteria} criteria, {applications} applications, {appEvents} application events, {contacts} contacts, {searchRuns} search runs.");
                return 1;
            }

            Console.WriteLine($"Restore completed: {summary.ArchivePath}");
            Console.WriteLine($"Restored counts: {notes} notes, {revisions} revisions, {audits} audit entries, {companies} companies, {sources} career sources, {postings} postings, {signals} signals, {criteria} criteria, {applications} applications, {appEvents} application events, {contacts} contacts, {searchRuns} search runs.");
            return 0;
        });

        return restoreCommand;
    }

    private static string ReadPasswordNoEcho()
    {
        var sb = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                break;
            }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (sb.Length > 0)
                {
                    sb.Length--;
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                sb.Append(key.KeyChar);
            }
        }
        return sb.ToString();
    }
}
