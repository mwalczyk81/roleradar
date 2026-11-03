namespace RoleRadar.Infrastructure.Mongo.Reading;

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

/// <summary>
/// Named seam that makes sure the read-path indexes (nulls-last compounds, collation sort indexes) exist. It delegates
/// to <see cref="TrackingIndexInitializer.EnsureIndexesOnceAsync"/>, which is idempotent and gated once per database
/// per process. The API host's startup service calls <see cref="EnsureAsync"/>; if Mongo was unreachable then, the
/// read seam calls <see cref="TryEnsureOnRead"/> on its query entry until one attempt succeeds, so an API that only
/// serves OData reads still gets its indexes. Registered with the infrastructure services but only used by the API
/// host, so the CLI and MCP hosts never run it.
/// </summary>
public sealed partial class ReadIndexInitializer
{
    /// <summary>Minimum gap between failed read-path attempts, so a Mongo outage does not add a wait to every read.</summary>
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(30);

    private readonly IMongoClient _client;
    private readonly IOptions<MongoOptions> _options;
    private readonly ILogger<ReadIndexInitializer> _logger;
    private volatile bool _ensured;
    private long _nextReadAttemptTimestamp;

    public ReadIndexInitializer(IMongoClient client, IOptions<MongoOptions> options, ILogger<ReadIndexInitializer> logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task EnsureAsync(CancellationToken cancellationToken)
    {
        var database = _client.GetDatabase(_options.Value.DatabaseName);
        await TrackingIndexInitializer.EnsureIndexesOnceAsync(database, cancellationToken);
        _ensured = true;
    }

    /// <summary>
    /// Called on every read-seam query. A no-op once the indexes were ensured. Otherwise makes at most one attempt per
    /// <see cref="RetryInterval"/>; a failure is logged and swallowed, so the read itself is never failed by it.
    /// </summary>
    public void TryEnsureOnRead()
    {
        if (_ensured)
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        var next = Interlocked.Read(ref _nextReadAttemptTimestamp);
        if (now < next ||
            Interlocked.CompareExchange(ref _nextReadAttemptTimestamp, now + (long)(RetryInterval.TotalSeconds * Stopwatch.Frequency), next) != next)
        {
            return;
        }

        try
        {
            EnsureAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            LogReadPathIndexCreationFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not create the read indexes on an OData read; reads continue without them and the next attempt is at least 30 seconds away.")]
    private partial void LogReadPathIndexCreationFailed(Exception exception);
}
