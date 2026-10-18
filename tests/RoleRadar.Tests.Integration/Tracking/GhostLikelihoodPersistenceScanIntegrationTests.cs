using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Queries;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Tracking;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Infrastructure.Mongo.Repositories;
using RoleRadar.Tests.Integration.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Integration.Tracking;

public sealed class GhostLikelihoodPersistenceScanIntegrationTests : IAsyncLifetime
{
    private readonly SharedMongoFixture _sharedMongo;
    private readonly string _databaseName;
    private MongoClient _client = null!;
    private IMongoDatabase _database = null!;
    private MongoCompanyRepository _companyRepo = null!;
    private MongoPostingRepository _postingRepo = null!;
    private TrackingTransactionContextReal _context = null!;

    public GhostLikelihoodPersistenceScanIntegrationTests(SharedMongoFixture sharedMongo)
    {
        _sharedMongo = sharedMongo;
        _databaseName = _sharedMongo.CreateDatabaseName("roleradar_ghost_scan");
    }

    public async ValueTask InitializeAsync()
    {
        _client = new MongoClient(_sharedMongo.ConnectionString);
        _database = _client.GetDatabase(_databaseName);
        await TrackingIndexInitializer.EnsureIndexesAsync(_database, CancellationToken.None);
        _companyRepo = new MongoCompanyRepository(_client, _database);
        _postingRepo = new MongoPostingRepository(_client, _database);
        _context = new TrackingTransactionContextReal(_companyRepo, _postingRepo);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_client is not null && !string.IsNullOrWhiteSpace(_databaseName))
            {
                await _client.DropDatabaseAsync(_databaseName);
            }
        }
        finally
        {
            _client?.Dispose();
        }
    }

    [Fact]
    public async Task GhostLikelihood_IsDynamicallyComputedOnRead_AndNeverPersistedInDatabaseOrBsonDocuments()
    {
        var ownerId = "owner-1";
        var company = Company.Create(ownerId, "Ghost Persistence Co");
        await _companyRepo.UpsertCompanyAsync(company);

        var posting = Posting.Create(
            ownerId: ownerId,
            companyId: company.Id,
            title: "Senior Distributed Systems Engineer",
            now: DateTimeOffset.UtcNow.AddDays(-70));
        posting.UpdateLastVerified(DateTimeOffset.UtcNow.AddDays(-15));
        await _postingRepo.UpsertPostingAsync(posting);

        var signal = new PostingSignal(
            id: Guid.CreateVersion7(),
            ownerId: ownerId,
            postingId: posting.Id,
            signalType: SignalType.Gone,
            evidence: "Posting removed from company board",
            observedAt: DateTimeOffset.UtcNow);
        await _postingRepo.AppendSignalAsync(signal);

        // 1. Read via TrackerQueryHandler and verify dynamic calculation
        var clock = new SystemUtcClock();
        var queryHandler = new TrackerQueryHandler(_context, clock);
        var queryResult = await queryHandler.HandleAsync(new TrackerQueryRequest(Entity: "postings", PostingId: posting.Id, OwnerId: ownerId));

        Assert.True(queryResult.IsSuccess, queryResult.Error?.Message);
        Assert.NotNull(queryResult.Value);
        Assert.Single(queryResult.Value.Items);
        var item = Assert.IsType<PostingSummaryItemDto>(queryResult.Value.Items[0]);

        // age 70d (+20) + gone (+50) = 70 => high
        Assert.Equal("high", item.GhostLikelihood);

        // 2. Direct raw BSON scan of all documents in the database
        // Verify zero occurrences of "ghost" or "ghost_likelihood" keys in ANY collection
        using var cursor = await _database.ListCollectionNamesAsync();
        var collectionNames = await cursor.ToListAsync();

        foreach (var colName in collectionNames)
        {
            var collection = _database.GetCollection<BsonDocument>(colName);
            var docs = await collection.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();

            foreach (var doc in docs)
            {
                AssertNoGhostKeysRecursive(doc, colName);
            }
        }
    }

    private static void AssertNoGhostKeysRecursive(BsonDocument doc, string collectionName)
    {
        foreach (var element in doc.Elements)
        {
            var key = element.Name.ToLowerInvariant();
            Assert.False(
                key == "ghost" || key == "ghost_likelihood" || key.Contains("ghost"),
                $"Collection '{collectionName}' document '{doc.GetValue("_id", BsonNull.Value)}' contains forbidden persisted ghost field '{element.Name}'. Ghost likelihood must strictly be computed dynamically on read!");

            if (element.Value.IsBsonDocument)
            {
                AssertNoGhostKeysRecursive(element.Value.AsBsonDocument, collectionName);
            }
            else if (element.Value.IsBsonArray)
            {
                foreach (var item in element.Value.AsBsonArray)
                {
                    if (item.IsBsonDocument)
                    {
                        AssertNoGhostKeysRecursive(item.AsBsonDocument, collectionName);
                    }
                }
            }
        }
    }

    private sealed class TrackingTransactionContextReal : ITrackingTransactionContext
    {
        public TrackingTransactionContextReal(ICompanyRepository companyRepo, IPostingRepository postingRepo)
        {
            Companies = companyRepo;
            Postings = postingRepo;
        }

        public ICompanyRepository Companies { get; }
        public IPostingRepository Postings { get; }
        public ICriterionRepository Criteria => throw new NotImplementedException();
        public IApplicationRepository Applications => throw new NotImplementedException();
        public IContactRepository Contacts => throw new NotImplementedException();
        public ISearchRunRepository SearchRuns => throw new NotImplementedException();
        public Task AppendAuditEntryAsync(Domain.Audit.AuditLogEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
