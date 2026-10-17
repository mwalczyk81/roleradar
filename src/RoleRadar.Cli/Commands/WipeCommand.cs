using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using RoleRadar.Application.Abstractions;

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

        wipeCommand.Add(confirmOption);

        wipeCommand.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var confirm = parseResult.GetValue(confirmOption);
            var service = services.GetRequiredService<IBackupArchiveService>();
            var result = await service.WipeDatabaseAsync(confirm ?? string.Empty, ct);
            return result.IsSuccess ? 0 : 1;
        });

        return wipeCommand;
    }
}
