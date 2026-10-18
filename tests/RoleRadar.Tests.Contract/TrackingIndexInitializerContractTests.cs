using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Tests.Contract.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Contract;

public sealed class TrackingIndexInitializerContractTests : IAsyncLifetime
{
    private readonly SharedMongoFixture _fixture;
    private readonly string _databaseName;
    private MongoClient _client = null!;
    private IMongoDatabase _database = null!;

    public TrackingIndexInitializerContractTests(SharedMongoFixture fixture)
    {
        _fixture = fixture;
        _databaseName = _fixture.CreateDatabaseName("roleradar_tracking_indexes");
    }

    public ValueTask InitializeAsync()
    {
        _client = new MongoClient(_fixture.ConnectionString);
        _database = _client.GetDatabase(_databaseName);
        return ValueTask.CompletedTask;
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
    public async Task EnsureIndexesAsync_CreatesMandatoryPartialAndUniqueIndexes_Idempotently()
    {
        // 1. Initial invocation creates indexes
        await TrackingIndexInitializer.EnsureIndexesAsync(_database, CancellationToken.None);

        // 2. Second invocation must be safe and idempotent
        await TrackingIndexInitializer.EnsureIndexesAsync(_database, CancellationToken.None);

        // 3. Inspect postings collection indexes
        var postingsIndexes = await (await _database.GetCollection<BsonDocument>(TrackingMongoOptions.PostingsCollectionName)
            .Indexes.ListAsync()).ToListAsync();
        var postingIndexNames = postingsIndexes.Select(i => i["name"].AsString).ToArray();

        Assert.Contains("ux_postings_owner_dedup_key_a", postingIndexNames);
        Assert.Contains("ux_postings_owner_dedup_key_b", postingIndexNames);
        Assert.Contains("ux_postings_owner_dedup_key_c", postingIndexNames);

        // Verify partial unique filter on dedup_key_a
        var keyAIndex = postingsIndexes.First(i => i["name"].AsString == "ux_postings_owner_dedup_key_a");
        Assert.True(keyAIndex["unique"].AsBoolean);
        Assert.True(keyAIndex.Contains("partialFilterExpression"));

        // 4. Inspect criteria collection indexes
        var criteriaIndexes = await (await _database.GetCollection<BsonDocument>(TrackingMongoOptions.CriteriaCollectionName)
            .Indexes.ListAsync()).ToListAsync();
        var criteriaIndexNames = criteriaIndexes.Select(i => i["name"].AsString).ToArray();

        Assert.Contains("ux_criteria_owner_key_active", criteriaIndexNames);
        var activeCriteriaIndex = criteriaIndexes.First(i => i["name"].AsString == "ux_criteria_owner_key_active");
        Assert.True(activeCriteriaIndex["unique"].AsBoolean);
        Assert.True(activeCriteriaIndex.Contains("partialFilterExpression"));

        // 5. Inspect career_sources collection indexes
        var careerSourcesIndexes = await (await _database.GetCollection<BsonDocument>(TrackingMongoOptions.CareerSourcesCollectionName)
            .Indexes.ListAsync()).ToListAsync();
        var careerSourceNames = careerSourcesIndexes.Select(i => i["name"].AsString).ToArray();
        Assert.Contains("ux_career_sources_composite", careerSourceNames);
        var compositeCareerSourceIndex = careerSourcesIndexes.First(i => i["name"].AsString == "ux_career_sources_composite");
        Assert.True(compositeCareerSourceIndex["unique"].AsBoolean);

        // 6. Inspect search_runs collection indexes
        var searchRunsIndexes = await (await _database.GetCollection<BsonDocument>(TrackingMongoOptions.SearchRunsCollectionName)
            .Indexes.ListAsync()).ToListAsync();
        var searchRunsIndexNames = searchRunsIndexes.Select(i => i["name"].AsString).ToArray();
        Assert.Contains("ux_search_runs_owner_started", searchRunsIndexNames);
        var searchRunsIndex = searchRunsIndexes.First(i => i["name"].AsString == "ux_search_runs_owner_started");
        Assert.True(searchRunsIndex["unique"].AsBoolean);
    }

    [Fact]
    public async Task EnsureIndexesAsync_WhenDataViolatesUniqueIndex_ThrowsTrackingIndexValidationExceptionNamingIndexAndRedactingConnectionStrings()
    {
        var dupDbName = _fixture.CreateDatabaseName("roleradar_tracking_indexes_dup");
        var dupDb = _client.GetDatabase(dupDbName);
        try
        {
            var postingsCol = dupDb.GetCollection<BsonDocument>(TrackingMongoOptions.PostingsCollectionName);
            await postingsCol.InsertManyAsync(
            [
                new BsonDocument { ["owner_id"] = "owner-1", ["dedup_key_a"] = "tenant|req1" },
                new BsonDocument { ["owner_id"] = "owner-1", ["dedup_key_a"] = "tenant|req1" }
            ]);

            var ex = await Assert.ThrowsAsync<TrackingIndexValidationException>(
                () => TrackingIndexInitializer.EnsureIndexesAsync(dupDb, CancellationToken.None));

            Assert.Contains("ux_postings_owner_dedup_key_a", ex.IndexName);
            Assert.Contains("ux_postings_owner_dedup_key_a", ex.Message);
            Assert.DoesNotContain("mongodb://", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await _client.DropDatabaseAsync(dupDbName);
        }
    }
}
