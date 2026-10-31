using System.CommandLine;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RoleRadar.Identity.Data;
using RoleRadar.Identity.Services;

namespace RoleRadar.Cli.Commands.Identity;

public static class CreateOwnerCommand
{
    /// <summary>
    /// Label printed immediately before the owner's <c>sub</c> value. Public so tests (and docs) can rely on one spelling.
    /// </summary>
    public const string OwnerSubjectLabel = "Owner subject (sub):";

    /// <summary>
    /// Writes the provisioning summary: username, TOTP enrolment details, and the exact <c>sub</c> value
    /// the owner's access tokens will carry (lowercase "D" GUID format, as emitted by Identity).
    /// Extracted from the command action so the printed output can be verified against a real issued token.
    /// </summary>
    public static void WriteOwnerSummary(TextWriter writer, OwnerProvisioningResult result)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(result);

        writer.WriteLine("\n" + new string('=', 70));
        writer.WriteLine("TOTP TWO-FACTOR AUTHENTICATION SETUP");
        writer.WriteLine(new string('=', 70));
        writer.WriteLine($"Username:       {result.Username}");
        writer.WriteLine($"Base32 Secret:  {result.AuthenticatorKey}");
        writer.WriteLine($"OtpAuth URI:    {result.OtpAuthUri}");
        writer.WriteLine(new string('=', 70));
        writer.WriteLine("Enter the Base32 secret into your authenticator app (e.g. Google Authenticator).");
        writer.WriteLine("This secret is displayed ONCE and is NOT saved in any log or configuration file.\n");
        writer.WriteLine($"{OwnerSubjectLabel} {result.Subject}");
        writer.WriteLine("Paste this value verbatim (case-sensitive) into ROLERADAR_OWNER_ALLOWED_SUBJECT so the API accepts this owner's tokens.\n");
    }

    public static Command Create(IServiceProvider services)
    {
        var command = new Command("create-owner", "Interactively provision the single system owner account with password and TOTP 2FA.");

        var usernameOption = new Option<string?>("--username")
        {
            Description = "The owner username (prompted interactively if omitted)."
        };

        command.Add(usernameOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var dbContext = services.GetRequiredService<IdentityAppDbContext>();
            await dbContext.Database.MigrateAsync(cancellationToken);

            var provisioningService = new OwnerProvisioningService(dbContext);

            if (await provisioningService.OwnerExistsAsync(cancellationToken))
            {
                Console.Error.WriteLine("Error: An owner account has already been provisioned.");
                return 1;
            }

            var username = parseResult.GetValue(usernameOption);
            if (string.IsNullOrWhiteSpace(username))
            {
                Console.Write("Enter owner username: ");
                username = Console.ReadLine();
            }

            if (string.IsNullOrWhiteSpace(username))
            {
                Console.Error.WriteLine("Error: Username cannot be empty.");
                return 1;
            }

            var password = ReadMaskedPassword("Enter owner password: ");
            if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            {
                Console.Error.WriteLine("Error: Password must be at least 8 characters.");
                return 1;
            }

            var confirmPassword = ReadMaskedPassword("Confirm owner password: ");
            if (!string.Equals(password, confirmPassword, StringComparison.Ordinal))
            {
                Console.Error.WriteLine("Error: Passwords do not match.");
                return 1;
            }

            Console.WriteLine("\nProvisioning owner account in identity operational store...");
            var result = await provisioningService.ProvisionOwnerAsync(username.Trim(), password, cancellationToken);

            WriteOwnerSummary(Console.Out, result);

            Console.Write("Enter the 6-digit verification code from your authenticator: ");
            var verificationCode = Console.ReadLine()?.Trim();

            if (string.IsNullOrWhiteSpace(verificationCode))
            {
                Console.Error.WriteLine("Error: Verification code required. Owner account not activated.");
                return 1;
            }

            var activated = await provisioningService.VerifyTotpAndActivateAsync(result.UserId, verificationCode, cancellationToken);
            if (!activated)
            {
                Console.Error.WriteLine("Error: Invalid TOTP verification code. Owner account not activated.");
                return 1;
            }

            Console.WriteLine("\nSuccess: Single owner account provisioned and 2FA verified successfully!");
            Console.WriteLine("To let the API accept this owner's tokens, set (value verbatim, case-sensitive):");
            Console.WriteLine($"  ROLERADAR_OWNER_ALLOWED_SUBJECT={result.Subject}");
            return 0;
        });

        return command;
    }

    private static string ReadMaskedPassword(string prompt)
    {
        Console.Write(prompt);
        var password = new StringBuilder();

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
                if (password.Length > 0)
                {
                    password.Remove(password.Length - 1, 1);
                    Console.Write("\b \b");
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                password.Append(key.KeyChar);
                Console.Write('*');
            }
        }

        return password.ToString();
    }
}
