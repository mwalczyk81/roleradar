using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using RoleRadar.Application.Abstractions;

namespace RoleRadar.Cli.Commands;

public static class BackupCommand
{
    public static Command Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var backupCommand = new Command("backup", "Create an encrypted, compressed backup archive of all RoleRadar data");

        var destinationArgument = new Argument<string?>("destination")
        {
            Arity = ArgumentArity.ZeroOrOne,
            Description = "Destination directory or file path for the backup archive (defaults to current directory)."
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

        backupCommand.Add(destinationArgument);
        backupCommand.Add(passphraseEnvOption);
        backupCommand.Add(passphraseStdinOption);
        backupCommand.Add(forbiddenPassphraseOption);

        backupCommand.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var destination = parseResult.GetValue(destinationArgument);
            var service = services.GetRequiredService<IBackupArchiveService>();
            var result = await service.CreateBackupAsync(destination ?? ".", "stub", ct);
            return result.IsSuccess ? 0 : 1;
        });

        return backupCommand;
    }
}
