using MongoDB.Bson;
using MongoDB.Driver;

namespace RoleRadar.Infrastructure.Mongo;

/// <summary>
/// Scoped lifetime handle for an ephemeral scratch database. Dropping the database
/// is guaranteed when the scope is disposed.
/// </summary>
public sealed class ScratchDatabaseScope : IAsyncDisposable
{
    private readonly MongoScratchDatabaseManager _manager;
    private int _disposed;

    public ScratchDatabaseScope(MongoScratchDatabaseManager manager, string scratchDatabaseName, string targetDatabaseName)
    {
        _manager = manager;
        ScratchDatabaseName = scratchDatabaseName;
        TargetDatabaseName = targetDatabaseName;
    }

    public string ScratchDatabaseName { get; }
    public string TargetDatabaseName { get; }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            await _manager.DropScratchDatabaseAsync(ScratchDatabaseName, TargetDatabaseName);
        }
    }
}

/// <summary>
/// Manages ephemeral scratch databases for tracker-import dry-run execution, supporting
/// per-collection $merge cloning from the target database, host index initialization,
/// strictly guarded drops, and startup orphan cleanup.
/// </summary>
public sealed class MongoScratchDatabaseManager
{
    private readonly IMongoClient _client;

    public static readonly IReadOnlyList<string> AllManagedCollections = new[]
    {
        MongoBootstrapAndIndexes.NotesCollectionName,
        MongoBootstrapAndIndexes.AuditLogCollectionName,
        TrackingMongoOptions.CompaniesCollectionName,
        TrackingMongoOptions.CareerSourcesCollectionName,
        TrackingMongoOptions.PostingsCollectionName,
        TrackingMongoOptions.PostingSignalsCollectionName,
        TrackingMongoOptions.CriteriaCollectionName,
        TrackingMongoOptions.ApplicationsCollectionName,
        TrackingMongoOptions.ApplicationEventsCollectionName,
        TrackingMongoOptions.SearchRunsCollectionName
    };

    public MongoScratchDatabaseManager(IMongoClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <summary>
    /// Creates and initializes a new scratch database cloned from targetDatabaseName.
    /// Clones all existing documents using per-collection $merge pipelines and initializes
    /// all host and domain indexes. If cloning or index initialization throws, drops the scratch database
    /// through the guard before rethrowing.
    /// </summary>
    public async Task<string> CreateScratchDatabaseAsync(string targetDatabaseName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDatabaseName);

        var scratchDbName = MongoScratchDatabaseGuard.GenerateScratchDatabaseName();
        MongoScratchDatabaseGuard.AssertSafeToDrop(scratchDbName, targetDatabaseName);

        var targetDb = _client.GetDatabase(targetDatabaseName);
        var scratchDb = _client.GetDatabase(scratchDbName);

        try
        {
            // 1. Clone existing collections using $merge
            foreach (var colName in AllManagedCollections)
            {
                var targetCol = targetDb.GetCollection<BsonDocument>(colName);
                var docCount = await targetCol.CountDocumentsAsync(
                    Builders<BsonDocument>.Filter.Empty,
                    cancellationToken: cancellationToken);

                if (docCount > 0)
                {
                    var pipeline = new BsonDocument[]
                    {
                        new("$match", new BsonDocument()),
                        new("$merge", new BsonDocument
                        {
                            { "into", new BsonDocument
                                {
                                    { "db", scratchDbName },
                                    { "coll", colName }
                                }
                            },
                            { "whenMatched", "replace" },
                            { "whenNotMatched", "insert" }
                        })
                    };

                    await targetCol.AggregateAsync<BsonDocument>(pipeline, cancellationToken: cancellationToken);
                }
            }

            // 2. Initialize host indexes on scratch database so deduplication and constraints match target
            await MongoBootstrapAndIndexes.EnsureIndexesAsync(scratchDb, cancellationToken);
            await TrackingIndexInitializer.EnsureIndexesOnceAsync(scratchDb, cancellationToken);

            return scratchDbName;
        }
        catch
        {
            try
            {
                await DropScratchDatabaseAsync(scratchDbName, targetDatabaseName, CancellationToken.None);
            }
            catch
            {
                // Best effort drop on initialization failure; original exception is rethrown below
            }

            throw;
        }
    }

    /// <summary>
    /// Creates a disposable scope around a newly cloned scratch database.
    /// Disposing the scope safely drops the scratch database.
    /// </summary>
    public async Task<ScratchDatabaseScope> CreateScopeAsync(string targetDatabaseName, CancellationToken cancellationToken = default)
    {
        var scratchDbName = await CreateScratchDatabaseAsync(targetDatabaseName, cancellationToken);
        return new ScratchDatabaseScope(this, scratchDbName, targetDatabaseName);
    }

    /// <summary>
    /// Drops an ephemeral scratch database with safety verification against the target database and regex pattern.
    /// </summary>
    public async Task DropScratchDatabaseAsync(string scratchDatabaseName, string targetDatabaseName, CancellationToken cancellationToken = default)
    {
        MongoScratchDatabaseGuard.AssertSafeToDrop(scratchDatabaseName, targetDatabaseName);
        await _client.DropDatabaseAsync(scratchDatabaseName, cancellationToken);
    }

    /// <summary>
    /// Scans the cluster for orphaned scratch databases, dropping any matching the scratch pattern
    /// that are older than the specified minimumAge (default: 1 hour). Never drops the target database.
    /// </summary>
    public async Task<int> CleanupOrphanedScratchDatabasesAsync(
        string targetDatabaseName,
        TimeSpan? minimumAge = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDatabaseName);

        var droppedCount = 0;
        var now = DateTimeOffset.UtcNow;

        using var cursor = await _client.ListDatabaseNamesAsync(cancellationToken);
        var allDbNames = await cursor.ToListAsync(cancellationToken);

        foreach (var dbName in allDbNames)
        {
            if (!MongoScratchDatabaseGuard.IsValidScratchDatabaseName(dbName))
            {
                continue;
            }

            if (string.Equals(dbName, targetDatabaseName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!MongoScratchDatabaseGuard.TryExtractCreationTime(dbName, out var creationTime))
            {
                continue;
            }

            if (!MongoScratchDatabaseGuard.IsEligibleForOrphanCleanup(dbName, targetDatabaseName, creationTime, now, minimumAge))
            {
                continue;
            }

            MongoScratchDatabaseGuard.AssertSafeToDrop(dbName, targetDatabaseName);
            await _client.DropDatabaseAsync(dbName, cancellationToken);
            droppedCount++;
        }

        return droppedCount;
    }
}
