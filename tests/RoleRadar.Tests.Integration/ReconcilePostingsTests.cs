namespace RoleRadar.Tests.Integration;

using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Infrastructure.Mongo.Reconcile;
using Testcontainers.MongoDb;
using Xunit;

public sealed class ReconcilePostingsTests : IAsyncLifetime
{
    private readonly MongoDbContainer _container = new MongoDbBuilder(MongoTestImage.Tag)
        .WithEnvironment("GLIBC_TUNABLES", "glibc.pthread.rseq=1")
        .WithReplicaSet("rs0")
        .Build();

    private IMongoDatabase _database = null!;
    private readonly Guid _companyId = Guid.NewGuid();

    public async ValueTask InitializeAsync()
    {
        await MongoContainerStartGate.StartAsync(_container);
        var client = new MongoClient(_container.GetConnectionString());
        _database = client.GetDatabase("reconcile_postings_test_db");
        await SeedUnreconciledPostingsAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    private async Task SeedUnreconciledPostingsAsync()
    {
        var companies = _database.GetCollection<BsonDocument>(TrackingMongoOptions.CompaniesCollectionName);
        var postings = _database.GetCollection<BsonDocument>(TrackingMongoOptions.PostingsCollectionName);

        await companies.InsertOneAsync(new BsonDocument
        {
            ["_id"] = new BsonBinaryData(_companyId, GuidRepresentation.Standard),
            ["owner_id"] = "owner-reconcile",
            ["name"] = "Stark Industries"
        });

        // Insert postings intentionally missing disposition_rank, company_name, and has_score
        var docs = new[]
        {
            new BsonDocument
            {
                ["_id"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
                ["owner_id"] = "owner-reconcile",
                ["company_id"] = new BsonBinaryData(_companyId, GuidRepresentation.Standard),
                ["title"] = "Iron Engineer",
                ["disposition"] = "applied",
                ["score"] = 90
            },
            new BsonDocument
            {
                ["_id"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
                ["owner_id"] = "owner-reconcile",
                ["company_id"] = new BsonBinaryData(_companyId, GuidRepresentation.Standard),
                ["title"] = "Weapons Analyst",
                ["disposition"] = "closed",
                ["score"] = BsonNull.Value
            },
            new BsonDocument
            {
                ["_id"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
                ["owner_id"] = "owner-reconcile",
                ["company_id"] = new BsonBinaryData(_companyId, GuidRepresentation.Standard),
                ["title"] = "Security Officer",
                ["disposition"] = "lead",
                ["score"] = 50
            }
        };

        await postings.InsertManyAsync(docs);
    }

    [Fact]
    public async Task ReconcilePostings_DryRun_Reports_Without_Modifying_And_Confirm_Is_Idempotent()
    {
        var service = new ReadModelReconciliationService(_database);
        var postings = _database.GetCollection<BsonDocument>(TrackingMongoOptions.PostingsCollectionName);

        // 1. Dry Run: Must report 3 examined, 3 updated, but not touch the collection
        var dryRunResult = await service.ReconcilePostingsAsync(dryRun: true);
        Assert.True(dryRunResult.IsDryRun);
        Assert.Equal(3, dryRunResult.PostingsExamined);
        Assert.Equal(3, dryRunResult.PostingsUpdated);

        var docsAfterDryRun = await postings.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
        Assert.All(docsAfterDryRun, doc =>
        {
            Assert.False(doc.Contains("disposition_rank"), "Dry run must NOT add disposition_rank");
            Assert.False(doc.Contains("has_score"), "Dry run must NOT add has_score");
            Assert.False(doc.Contains("company_name"), "Dry run must NOT add company_name");
        });

        // 2. Real Run (confirm): Must update all 3 documents
        var confirmResult = await service.ReconcilePostingsAsync(dryRun: false);
        Assert.False(confirmResult.IsDryRun);
        Assert.Equal(3, confirmResult.PostingsExamined);
        Assert.Equal(3, confirmResult.PostingsUpdated);

        var docsAfterConfirm = await postings.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
        Assert.All(docsAfterConfirm, doc =>
        {
            Assert.True(doc.Contains("disposition_rank"), "Confirm must populate disposition_rank");
            Assert.True(doc.Contains("has_score"), "Confirm must populate has_score");
            Assert.Equal("Stark Industries", doc["company_name"].AsString);
        });

        // 3. Idempotent Second Run: Must report 3 examined, 0 updated
        var secondRunResult = await service.ReconcilePostingsAsync(dryRun: false);
        Assert.Equal(3, secondRunResult.PostingsExamined);
        Assert.Equal(0, secondRunResult.PostingsUpdated);
    }
}
