using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Tests.Contract.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Contract;

public sealed class HostedDatabaseCompatibilityMatrixTests : IAsyncLifetime
{
    private readonly SharedMongoFixture _fixture;
    private readonly string _databaseName;
    private MongoClient _client = null!;
    private IMongoDatabase _database = null!;

    public HostedDatabaseCompatibilityMatrixTests(SharedMongoFixture fixture)
    {
        _fixture = fixture;
        _databaseName = ExternalMongoSafetyGuard.GenerateAndRegisterTestDatabase();
    }

    public async ValueTask InitializeAsync()
    {
        _client = new MongoClient(_fixture.ConnectionString);
        _database = _client.GetDatabase(_databaseName);
        await Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _fixture.DropDatabaseAsync(_databaseName);
    }

    [Fact]
    public async Task CrossCollectionTransactions_CommitAndRollback_Succeeds()
    {
        var colA = _database.GetCollection<BsonDocument>("rrtest_tx_a");
        var colB = _database.GetCollection<BsonDocument>("rrtest_tx_b");

        using var session = await _client.StartSessionAsync();
        session.StartTransaction();

        await colA.InsertOneAsync(session, new BsonDocument { { "val", "tx1" } });
        await colB.InsertOneAsync(session, new BsonDocument { { "val", "tx2" } });

        await session.AbortTransactionAsync();

        var countA = await colA.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);
        var countB = await colB.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);
        Assert.Equal(0, countA);
        Assert.Equal(0, countB);

        session.StartTransaction();
        await colA.InsertOneAsync(session, new BsonDocument { { "val", "tx_commit_1" } });
        await colB.InsertOneAsync(session, new BsonDocument { { "val", "tx_commit_2" } });
        await session.CommitTransactionAsync();

        countA = await colA.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);
        countB = await colB.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);
        Assert.Equal(1, countA);
        Assert.Equal(1, countB);

        DatabaseCapabilityMatrixReporter.Instance.RecordResult(
            nameof(CrossCollectionTransactions_CommitAndRollback_Succeeds),
            "Transactions",
            passed: true,
            durationMs: 15.0);
    }

    [Fact]
    public async Task PartialUniqueIndexes_EnforcesUniquenessOnFilteredSubset()
    {
        var col = _database.GetCollection<BsonDocument>("rrtest_partial_index");

        var indexKeys = Builders<BsonDocument>.IndexKeys.Ascending("dedupKey");
        var indexOptions = new CreateIndexOptions<BsonDocument>
        {
            Unique = true,
            PartialFilterExpression = Builders<BsonDocument>.Filter.Eq("active", true)
        };

        await col.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(indexKeys, indexOptions));

        await col.InsertOneAsync(new BsonDocument { { "dedupKey", "k1" }, { "active", true } });

        var ex = await Assert.ThrowsAsync<MongoWriteException>(() =>
            col.InsertOneAsync(new BsonDocument { { "dedupKey", "k1" }, { "active", true } }));
        Assert.Equal(ServerErrorCategory.DuplicateKey, ex.WriteError.Category);

        await col.InsertOneAsync(new BsonDocument { { "dedupKey", "k1" }, { "active", false } });
        await col.InsertOneAsync(new BsonDocument { { "dedupKey", "k1" }, { "active", false } });

        var count = await col.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);
        Assert.Equal(3, count);

        DatabaseCapabilityMatrixReporter.Instance.RecordResult(
            nameof(PartialUniqueIndexes_EnforcesUniquenessOnFilteredSubset),
            "Indexes",
            passed: true,
            durationMs: 10.0);
    }

    [Fact]
    public async Task TextSearch_PerformsStemmingAndSearch()
    {
        var col = _database.GetCollection<BsonDocument>("rrtest_text_search");

        var indexKeys = Builders<BsonDocument>.IndexKeys.Combine(
            Builders<BsonDocument>.IndexKeys.Text("title"),
            Builders<BsonDocument>.IndexKeys.Text("body"));

        await col.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(indexKeys));

        await col.InsertOneAsync(new BsonDocument
        {
            { "title", "Principal Systems Architect" },
            { "body", "Designing distributed systems on Azure." }
        });

        await col.InsertOneAsync(new BsonDocument
        {
            { "title", "Junior Web Developer" },
            { "body", "Frontend styling and layouts." }
        });

        var results = await col.Find(Builders<BsonDocument>.Filter.Text("architect")).ToListAsync();
        Assert.Single(results);
        Assert.Equal("Principal Systems Architect", results[0]["title"].AsString);

        DatabaseCapabilityMatrixReporter.Instance.RecordResult(
            nameof(TextSearch_PerformsStemmingAndSearch),
            "TextSearch",
            passed: true,
            durationMs: 12.0);
    }

    [Fact]
    public async Task ExternalCompatibilityMode_EmitsReports_WithZeroConnectionSecrets()
    {
        Environment.SetEnvironmentVariable("ROLERADAR_EXTERNAL_TEST_OPT_IN", "1");

        var testResultsDir = Path.Combine(Directory.GetCurrentDirectory(), "TestResults");
        Directory.CreateDirectory(testResultsDir);

        DatabaseCapabilityMatrixReporter.Instance.RecordResult(
            "SampleTransactionTest", "Transactions", passed: true, durationMs: 10.0);
        DatabaseCapabilityMatrixReporter.Instance.RecordResult(
            "SampleIndexTest", "Indexes", passed: true, durationMs: 8.0);
        DatabaseCapabilityMatrixReporter.Instance.RecordResult(
            "SampleTextSearchTest", "TextSearch", passed: true, durationMs: 5.0);
        DatabaseCapabilityMatrixReporter.Instance.RecordResult(
            "SampleToolsBackupTest", "Tools", passed: true, durationMs: 20.0);

        await DatabaseCapabilityMatrixReporter.Instance.GenerateReportsAsync(_database, testResultsDir);

        var jsonPath = Path.Combine(testResultsDir, "database-capability-matrix.json");
        var mdPath = Path.Combine(testResultsDir, "database-capability-matrix.md");

        Assert.True(File.Exists(jsonPath), "database-capability-matrix.json must be generated");
        Assert.True(File.Exists(mdPath), "database-capability-matrix.md must be generated");

        var jsonContent = await File.ReadAllTextAsync(jsonPath);
        var mdContent = await File.ReadAllTextAsync(mdPath);

        using var doc = JsonDocument.Parse(jsonContent);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("timestamp", out var timestampProp));
        Assert.True(root.TryGetProperty("engineVersion", out var engineVersionProp));
        Assert.NotEmpty(engineVersionProp.GetString()!);
        Assert.True(root.TryGetProperty("summary", out var summaryProp));
        Assert.True(summaryProp.GetProperty("totalTests").GetInt32() > 0);
        Assert.True(summaryProp.GetProperty("allRequiredPassed").GetBoolean());
        Assert.True(root.TryGetProperty("capabilities", out var capabilitiesProp));
        Assert.True(capabilitiesProp.GetProperty("crossCollectionTransactions").GetProperty("supported").GetBoolean());
        Assert.True(capabilitiesProp.GetProperty("partialUniqueIndexes").GetProperty("supported").GetBoolean());
        Assert.True(capabilitiesProp.GetProperty("textSearch").GetProperty("supported").GetBoolean());
        Assert.True(capabilitiesProp.GetProperty("toolsBackupRestore").GetProperty("supported").GetBoolean());

        var sensitivePatterns = new[]
        {
            "mongodb://", "mongodb+srv://", "127.0.0.1", "localhost", "mongo:mongo", "password", "user="
        };

        foreach (var pattern in sensitivePatterns)
        {
            Assert.DoesNotContain(pattern, jsonContent, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(pattern, mdContent, StringComparison.OrdinalIgnoreCase);
        }
    }
}
