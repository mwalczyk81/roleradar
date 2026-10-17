using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Application.Abstractions;

namespace RoleRadar.Infrastructure.Mongo;

public sealed class MongoReadinessProbe
{
    public const string StartServicesCommand = "pwsh ./scripts/start-services.ps1";
    public const string DockerComposeCommand = "docker compose -f deploy/docker-compose.yml up -d";

    private static readonly Action<ILogger, string, Exception?> LogReadinessWarning =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(3001, "MongoReadinessWarning"),
            "MongoDB replica set readiness probe failed for {Endpoint}");

    private static readonly Action<ILogger, string, Exception?> LogReadinessDebug =
        LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId(3002, "MongoReadinessDebug"),
            "MongoDB replica set readiness probe exception details for {Endpoint}");

    private readonly IMongoClient _client;
    private readonly MongoOptions _options;
    private readonly ILogger<MongoReadinessProbe> _logger;

    public MongoReadinessProbe(
        IMongoClient client,
        IOptions<MongoOptions> options,
        ILogger<MongoReadinessProbe> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public static string FormatOfflineMessage(string connectionString)
    {
        var redactedEndpoint = ConnectionStringRedactor.Redact(connectionString);
        return $"""
            RoleRadar data services are not running or unreachable at {redactedEndpoint}.
            Start them from the repository root with:
              {StartServicesCommand}
            (or: {DockerComposeCommand})
            """;
    }

    public async Task<Result<StoreReadinessStatus>> CheckAsync(CancellationToken cancellationToken = default)
    {
        var timeout = _options.ReadinessTimeout <= TimeSpan.Zero || _options.ReadinessTimeout > TimeSpan.FromSeconds(4.5)
            ? TimeSpan.FromSeconds(3)
            : _options.ReadinessTimeout;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        var redactedEndpoint = ConnectionStringRedactor.Redact(_options.ConnectionString);

        var sw = Stopwatch.StartNew();
        try
        {
            var db = _client.GetDatabase(_options.DatabaseName);
            var helloCmd = new BsonDocument("hello", 1);
            var response = await db.RunCommandAsync<BsonDocument>(helloCmd, cancellationToken: cts.Token);
            sw.Stop();

            var setName = response.TryGetValue("setName", out var setVal) && setVal.IsString
                ? setVal.AsString
                : _options.ReplicaSetName;

            var isWritablePrimary = response.TryGetValue("isWritablePrimary", out var writableVal) &&
                                    writableVal.IsBoolean &&
                                    writableVal.AsBoolean;

            if (!isWritablePrimary)
            {
                var notPrimaryMsg = FormatOfflineMessage(_options.ConnectionString);
                return Result<StoreReadinessStatus>.Failure(
                    ErrorCode.DatastoreOffline,
                    notPrimaryMsg,
                    new Dictionary<string, object?>
                    {
                        ["endpoint"] = redactedEndpoint,
                        ["remediationCommand"] = StartServicesCommand
                    });
            }

            return Result<StoreReadinessStatus>.Success(
                new StoreReadinessStatus(
                    IsReady: true,
                    Endpoint: redactedEndpoint,
                    ReplicaSet: setName,
                    LatencyMs: Math.Round(sw.Elapsed.TotalMilliseconds, 2)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            // Log warning without exception object or stack trace; keep full exception only at Debug level
            LogReadinessWarning(_logger, redactedEndpoint, null);
            LogReadinessDebug(_logger, redactedEndpoint, ex);

            var message = FormatOfflineMessage(_options.ConnectionString);
            return Result<StoreReadinessStatus>.Failure(
                ErrorCode.DatastoreOffline,
                message,
                new Dictionary<string, object?>
                {
                    ["endpoint"] = redactedEndpoint,
                    ["remediationCommand"] = StartServicesCommand
                });
        }
    }
}
