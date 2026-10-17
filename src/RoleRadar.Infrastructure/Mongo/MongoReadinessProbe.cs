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

    private static readonly Action<ILogger, string, Exception?> LogReadinessFailure =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(3001, "MongoReadinessFailed"),
            "MongoDB replica set readiness probe failed for {Endpoint}");

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

    public static string FormatOfflineMessage(string connectionString) =>
        $"""
        RoleRadar data services are not running or unreachable at {connectionString}.
        Start them from the repository root with:
          {StartServicesCommand}
        (or: {DockerComposeCommand})
        """;

    public async Task<Result<StoreReadinessStatus>> CheckAsync(CancellationToken cancellationToken = default)
    {
        var timeout = _options.ReadinessTimeout <= TimeSpan.Zero || _options.ReadinessTimeout > TimeSpan.FromSeconds(4.5)
            ? TimeSpan.FromSeconds(3)
            : _options.ReadinessTimeout;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

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
                        ["endpoint"] = _options.ConnectionString,
                        ["remediationCommand"] = StartServicesCommand
                    });
            }

            return Result<StoreReadinessStatus>.Success(
                new StoreReadinessStatus(
                    IsReady: true,
                    Endpoint: _options.ConnectionString,
                    ReplicaSet: setName,
                    LatencyMs: Math.Round(sw.Elapsed.TotalMilliseconds, 2)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            LogReadinessFailure(_logger, _options.ConnectionString, ex);
            var message = FormatOfflineMessage(_options.ConnectionString);
            return Result<StoreReadinessStatus>.Failure(
                ErrorCode.DatastoreOffline,
                message,
                new Dictionary<string, object?>
                {
                    ["endpoint"] = _options.ConnectionString,
                    ["remediationCommand"] = StartServicesCommand
                });
        }
    }
}
