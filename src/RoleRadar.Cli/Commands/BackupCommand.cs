using System.CommandLine;
using System.Text;
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
            var destination = parseResult.GetValue(destinationArgument) ?? ".";
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
                var p1 = ReadPasswordNoEcho();
                Console.Write("Confirm backup passphrase: ");
                var p2 = ReadPasswordNoEcho();

                if (p1 != p2)
                {
                    Console.Error.WriteLine("Passphrases do not match.");
                    return 1;
                }

                passphrase = p1;
            }

            if (passphrase.Length < 12)
            {
                Console.Error.WriteLine("Passphrase must be at least 12 characters.");
                return 1;
            }

            var service = services.GetRequiredService<IBackupArchiveService>();
            var outcome = await service.CreateBackupAsync(destination, passphrase, ct);

            if (!outcome.IsSuccess || outcome.Value is null)
            {
                Console.Error.WriteLine($"Backup failed: {outcome.Error?.Message}");
                return 1;
            }

            var summary = outcome.Value;
            Console.WriteLine($"Backup created: {summary.ArchivePath} ({summary.BytesWritten} bytes)");
            return 0;
        });

        return backupCommand;
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
