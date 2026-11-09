using System.CommandLine;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RoleRadar.Identity.Data;
using RoleRadar.Infrastructure.Mongo;

namespace RoleRadar.Cli.Demo;

/// <summary>
/// Everything the demo commands touch outside the services: environment, terminal state, output and the guard. The
/// default is the real process; tests inject their own. <see cref="Guard"/> defaults to the production check, which
/// supplies the fixed demo host; only the integration test passes another (ADR 0039).
/// </summary>
public sealed record DemoCommandHost(
    Func<string, string?> GetEnv,
    Func<bool> IsInteractive,
    TextWriter Out,
    TextWriter Error,
    Func<string?, string?, string?, DemoGuardResult>? Guard = null)
{
    public static DemoCommandHost Default { get; } = new(
        Environment.GetEnvironmentVariable,
        () => !Console.IsInputRedirected && !Console.IsOutputRedirected,
        Console.Out,
        Console.Error);
}

/// <summary>
/// <c>roleradar demo seed | credentials | totp</c>. Each subcommand runs the demo guard first and exits with 2 when it
/// refuses. Nothing here writes to a logger; secrets go to the console only, and only to an interactive terminal.
/// </summary>
public static class DemoCommand
{
    public const int RefusedExitCode = 2;

    public static Command Create(IServiceProvider services, DemoCommandHost? host = null)
    {
        var h = host ?? DemoCommandHost.Default;
        var command = new Command(
            "demo",
            "Demo-only commands: fictional data and a throwaway sign-in. They refuse unless demo mode is on and the database is the demo database.");

        command.Add(CreateSeed(services, h));
        command.Add(CreateCredentials(services, h));
        command.Add(CreateTotp(services, h));
        return command;
    }

    private static Command CreateSeed(IServiceProvider services, DemoCommandHost h)
    {
        var seed = new Command("seed", "Load the fictional dataset (idempotent) and make sure the demo account exists. Prints counts only.");
        var asOfOption = new Option<string?>("--as-of")
        {
            Description = "Anchor date, yyyy-MM-dd (default: today, UTC)."
        };
        seed.Add(asOfOption);

        seed.SetAction(async (parseResult, cancellationToken) =>
        {
            if (Refused(services, h) is { } refusal)
            {
                return refusal;
            }

            var asOf = DateOnly.FromDateTime(DateTime.UtcNow);
            var asOfText = parseResult.GetValue(asOfOption);
            if (!string.IsNullOrWhiteSpace(asOfText)
                && !DateOnly.TryParseExact(asOfText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out asOf))
            {
                h.Error.WriteLine("The --as-of value must be a date like 2026-09-30.");
                return 1;
            }

            var report = await new DemoSeeder(services).SeedAsync(asOf, cancellationToken);
            h.Out.WriteLine(report.AlreadySeeded
                ? "Demo seed: already seeded; nothing changed."
                : $"Demo seed: created {report.Companies} companies, {report.Postings} postings, {report.Applications} applications, "
                  + $"{report.ApplicationEvents} application events, {report.Notes} notes, {report.SearchRuns} search runs, "
                  + $"{report.PostingSignals} posting signals, {report.CareerSources} career sources.");

            var identity = services.GetRequiredService<IdentityAppDbContext>();
            await identity.Database.MigrateAsync(cancellationToken);
            var outcome = await new DemoAccountManager(identity).EnsureAccountAsync(cancellationToken);
            switch (outcome)
            {
                case DemoAccountEnsureOutcome.RefusedOtherUserExists:
                    h.Error.WriteLine("Demo account not created: the identity store already has another user.");
                    return RefusedExitCode;
                case DemoAccountEnsureOutcome.Created:
                    h.Out.WriteLine("Demo account: created. No credentials were printed; run `roleradar demo credentials` to get them.");
                    break;
                default:
                    h.Out.WriteLine("Demo account: already exists.");
                    break;
            }

            return 0;
        });

        return seed;
    }

    private static Command CreateCredentials(IServiceProvider services, DemoCommandHost h)
    {
        var credentials = new Command("credentials", "Reset the demo account and print its username, password and TOTP secret once. Interactive terminals only.");

        credentials.SetAction(async (_, cancellationToken) =>
        {
            if (Refused(services, h) is { } refusal)
            {
                return refusal;
            }

            // Check before resetting, so a refused run does not change the account.
            if (!h.IsInteractive())
            {
                h.Error.WriteLine("Refused: the credentials are shown only on an interactive terminal (not redirected to a file or a log).");
                return RefusedExitCode;
            }

            var identity = services.GetRequiredService<IdentityAppDbContext>();
            await identity.Database.MigrateAsync(cancellationToken);
            var manager = new DemoAccountManager(identity);
            if (await manager.EnsureAccountAsync(cancellationToken) == DemoAccountEnsureOutcome.RefusedOtherUserExists)
            {
                h.Error.WriteLine("Refused: the identity store already has another user.");
                return RefusedExitCode;
            }

            var reset = await manager.ResetCredentialsAsync(cancellationToken);
            if (reset is null || !CredentialPrinter.TryPrint(reset, h.Out, h.IsInteractive))
            {
                h.Error.WriteLine("The credentials could not be shown.");
                return 1;
            }

            return 0;
        });

        return credentials;
    }

    private static Command CreateTotp(IServiceProvider services, DemoCommandHost h)
    {
        var totp = new Command("totp", "Print the current six-digit TOTP code of the demo account.");

        totp.SetAction(async (_, cancellationToken) =>
        {
            if (Refused(services, h) is { } refusal)
            {
                return refusal;
            }

            var identity = services.GetRequiredService<IdentityAppDbContext>();
            await identity.Database.MigrateAsync(cancellationToken);
            var current = await new DemoAccountManager(identity).CurrentTotpAsync(cancellationToken);
            if (current is null)
            {
                h.Error.WriteLine("There is no demo account yet. Run `roleradar demo seed` first.");
                return 1;
            }

            h.Out.WriteLine($"{current.Code} (valid for {current.SecondsRemaining} more seconds)");
            return 0;
        });

        return totp;
    }

    /// <summary>Runs the guard. Returns the exit code to stop with, or null when the command may proceed.</summary>
    private static int? Refused(IServiceProvider services, DemoCommandHost h)
    {
        var options = services.GetRequiredService<IOptions<MongoOptions>>().Value;
        var guard = h.Guard ?? DemoModeGuard.CheckForDemoStack;
        var result = guard(h.GetEnv(DemoModeGuard.DemoModeVariable), options.DatabaseName, options.ConnectionString);
        if (result.Allowed)
        {
            return null;
        }

        h.Error.WriteLine("Demo command refused: " + result.Message);
        return RefusedExitCode;
    }
}
