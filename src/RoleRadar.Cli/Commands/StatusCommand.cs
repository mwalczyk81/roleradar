using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoleRadar.Application.Abstractions;

namespace RoleRadar.Cli.Commands;

public static class StatusCommand
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static Command Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var statusCommand = new Command("status", "Check RoleRadar backing data store readiness and status");

        var jsonOption = new Option<bool>("--json")
        {
            Description = "Emit structured JSON status output."
        };

        statusCommand.Add(jsonOption);

        statusCommand.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var json = parseResult.GetValue(jsonOption);

            using var scope = services.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<INoteRepository>();

            var readinessResult = await repository.CheckReadinessAsync(ct);

            if (readinessResult.IsSuccess && readinessResult.Value is not null)
            {
                var status = readinessResult.Value;
                if (json)
                {
                    var jsonStr = JsonSerializer.Serialize(status, JsonOpts);
                    Console.WriteLine(jsonStr);
                    return 0;
                }

                Console.WriteLine("RoleRadar Store: Ready");
                Console.WriteLine($"Endpoint:        {status.Endpoint}");
                Console.WriteLine($"Replica Set:     {status.ReplicaSet}");
                Console.WriteLine($"Latency:         {status.LatencyMs:F2} ms");
                return 0;
            }

            var error = readinessResult.Error!.Value;
            if (json)
            {
                var errorObj = new
                {
                    isReady = false,
                    code = error.Code.ToString(),
                    message = error.Message,
                    metadata = error.Metadata
                };
                Console.WriteLine(JsonSerializer.Serialize(errorObj, JsonOpts));
                return 1;
            }

            Console.Error.WriteLine(error.Message);
            return 1;
        });

        return statusCommand;
    }
}
