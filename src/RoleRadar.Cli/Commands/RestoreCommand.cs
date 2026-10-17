using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using RoleRadar.Application.Abstractions;

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
            var service = services.GetRequiredService<IBackupArchiveService>();
            var result = await service.RestoreBackupAsync(file, "stub", force, ct);
            return result.IsSuccess ? 0 : 1;
        });

        return restoreCommand;
    }
}
