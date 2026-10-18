using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Integrity;

namespace RoleRadar.Cli.Commands;

public static class VerifyCommand
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static Command Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var verifyCommand = new Command("verify", "Verify referential integrity across RoleRadar tracking collections (read-only)");

        var jsonOption = new Option<bool>("--json")
        {
            Description = "Emit structured JSON verification output."
        };

        var ownerIdOption = new Option<string?>("--owner-id")
        {
            Description = "Optional owner identifier filter."
        };

        verifyCommand.Add(jsonOption);
        verifyCommand.Add(ownerIdOption);

        verifyCommand.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var json = parseResult.GetValue(jsonOption);
            var ownerId = parseResult.GetValue(ownerIdOption);

            using var scope = services.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<IQueryHandler<VerifyIntegrityQuery, VerifyIntegrityResultDto>>();

            var query = new VerifyIntegrityQuery(ownerId);
            var result = await handler.HandleAsync(query, ct);

            if (!result.IsSuccess || result.Value is null)
            {
                var error = result.Error ?? new ResultError(ErrorCode.ValidationError, "Integrity verification failed.");
                if (json)
                {
                    Console.WriteLine(JsonSerializer.Serialize(new { ok = false, error = error.Message }, JsonOpts));
                }
                else
                {
                    Console.Error.WriteLine(error.Message);
                }
                return 1;
            }

            var report = result.Value;

            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { ok = true, data = report }, JsonOpts));
            }
            else
            {
                if (report.AnomalyCount == 0)
                {
                    Console.WriteLine("RoleRadar Referential Integrity: OK (0 anomalies detected)");
                }
                else
                {
                    Console.WriteLine($"RoleRadar Referential Integrity: FAILED ({report.AnomalyCount} anomalies detected)");
                    foreach (var a in report.Anomalies)
                    {
                        var targetInfo = a.TargetId.HasValue ? $", TargetId: {a.TargetId.Value}" : string.Empty;
                        Console.WriteLine($"  - [{a.Kind}] SourceId: {a.SourceId}{targetInfo} - {a.Description}");
                    }
                }
            }

            return report.AnomalyCount == 0 ? 0 : 1;
        });

        return verifyCommand;
    }
}
