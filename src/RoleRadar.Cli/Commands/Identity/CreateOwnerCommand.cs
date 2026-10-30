using System.CommandLine;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RoleRadar.Identity.Data;
using RoleRadar.Identity.Services;

namespace RoleRadar.Cli.Commands.Identity;

public static class CreateOwnerCommand
{
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

            Console.WriteLine("\n" + new string('=', 70));
            Console.WriteLine("TOTP TWO-FACTOR AUTHENTICATION SETUP");
            Console.WriteLine(new string('=', 70));
            Console.WriteLine($"Username:       {result.Username}");
            Console.WriteLine($"Base32 Secret:  {result.AuthenticatorKey}");
            Console.WriteLine($"OtpAuth URI:    {result.OtpAuthUri}");
            Console.WriteLine(new string('=', 70));
            Console.WriteLine("Enter the Base32 secret into your authenticator app (e.g. Google Authenticator).");
            Console.WriteLine("This secret is displayed ONCE and is NOT saved in any log or configuration file.\n");

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
