namespace RoleRadar.Tests.Integration;

using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Infrastructure.Mongo.Reconcile;
using Testcontainers.MongoDb;
using Xunit;

/// <summary>
/// Reconcile backfill for applications. Seeds LEGACY application documents, which only carry
/// <c>outcome</c>, <c>submitted_date</c> and <c>posting_id</c> (plus audit timestamps) and none of the
/// derived read-model fields. All data is fictional.
/// </summary>
public sealed class ReconcileApplicationsTests : IAsyncLifetime
{
    private const string Owner = "owner-reconcile-apps";

    private readonly MongoDbContainer _container = new MongoDbBuilder("mongo:8.0.34")
        .WithEnvironment("GLIBC_TUNABLES", "glibc.pthread.rseq=1")
        .WithReplicaSet("rs0")
        .Build();

    private IMongoDatabase _database = null!;
    private readonly Guid _companyId = Guid.NewGuid();
    private readonly Guid _postingId = Guid.NewGuid();
    private readonly Guid _applicationId1 = Guid.NewGuid();
    private readonly Guid _applicationId2 = Guid.NewGuid();
    private readonly Guid _applicationId3 = Guid.NewGuid();

    // created_at for the legacy app whose submitted_date is unusable (applied_at must fall back to this).
    private static readonly DateTime App3CreatedAt = new(2026, 9, 14, 8, 30, 0, DateTimeKind.Utc);

    public async ValueTask InitializeAsync()
    {
        await MongoContainerStartGate.StartAsync(_container);
        var client = new MongoClient(_container.GetConnectionString());
        _database = client.GetDatabase("reconcile_applications_test_db");
        await SeedLegacyApplicationsAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    private IMongoCollection<BsonDocument> Apps => _database.GetCollection<BsonDocument>(TrackingMongoOptions.ApplicationsCollectionName);

    private static BsonBinaryData Bin(Guid id) => new(id, GuidRepresentation.Standard);

    private static DateTime Utc(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

    private async Task<BsonDocument> LoadAppAsync(Guid id) =>
        await Apps.Find(Builders<BsonDocument>.Filter.Eq("_id", Bin(id))).FirstAsync();

    private async Task SeedLegacyApplicationsAsync()
    {
        var companies = _database.GetCollection<BsonDocument>(TrackingMongoOptions.CompaniesCollectionName);
        var postings = _database.GetCollection<BsonDocument>(TrackingMongoOptions.PostingsCollectionName);
        var appEvents = _database.GetCollection<BsonDocument>(TrackingMongoOptions.ApplicationEventsCollectionName);

        await companies.InsertOneAsync(new BsonDocument
        {
            ["_id"] = Bin(_companyId),
            ["owner_id"] = Owner,
            ["name"] = "Cyberdyne Systems"
        });

        await postings.InsertOneAsync(new BsonDocument
        {
            ["_id"] = Bin(_postingId),
            ["owner_id"] = Owner,
            ["company_id"] = Bin(_companyId),
            ["title"] = "AI Research Lead"
        });

        // LEGACY app 1: outcome=applied, has events. No status, applied_at, company_id or any derived field.
        await Apps.InsertOneAsync(new BsonDocument
        {
            ["_id"] = Bin(_applicationId1),
            ["owner_id"] = Owner,
            ["posting_id"] = Bin(_postingId),
            ["submitted_date"] = "2026-10-01",
            ["outcome"] = "applied",
            ["created_at"] = BsonDateTime.Create(DateTime.UtcNow.AddDays(-30)),
            ["updated_at"] = BsonDateTime.Create(DateTime.UtcNow.AddDays(-30))
        });

        // Events were backfilled "now" (created_at) in reverse order, so only event_date gives the right ordering.
        // follow_up (10-10) must not count as a response; interview (10-20) is the first response;
        // offer (10-25) is the last event.
        var backfillTime = DateTime.UtcNow.AddDays(-1);
        await appEvents.InsertManyAsync(new[]
        {
            new BsonDocument
            {
                ["_id"] = Bin(Guid.NewGuid()),
                ["owner_id"] = Owner,
                ["application_id"] = Bin(_applicationId1),
                ["event_type"] = "offer",
                ["event_date"] = "2026-10-25",
                ["created_at"] = BsonDateTime.Create(backfillTime)
            },
            new BsonDocument
            {
                ["_id"] = Bin(Guid.NewGuid()),
                ["owner_id"] = Owner,
                ["application_id"] = Bin(_applicationId1),
                ["event_type"] = "interview",
                ["event_date"] = "2026-10-20",
                ["created_at"] = BsonDateTime.Create(backfillTime.AddMinutes(1))
            },
            new BsonDocument
            {
                ["_id"] = Bin(Guid.NewGuid()),
                ["owner_id"] = Owner,
                ["application_id"] = Bin(_applicationId1),
                ["event_type"] = "follow_up",
                ["event_date"] = "2026-10-10",
                ["created_at"] = BsonDateTime.Create(backfillTime.AddMinutes(2))
            }
        });

        // LEGACY app 2: outcome=rejected, no events (unresponded).
        await Apps.InsertOneAsync(new BsonDocument
        {
            ["_id"] = Bin(_applicationId2),
            ["owner_id"] = Owner,
            ["posting_id"] = Bin(_postingId),
            ["submitted_date"] = "2026-10-15",
            ["outcome"] = "rejected",
            ["created_at"] = BsonDateTime.Create(DateTime.UtcNow.AddDays(-15)),
            ["updated_at"] = BsonDateTime.Create(DateTime.UtcNow.AddDays(-15))
        });

        // LEGACY app 3: outcome=closed with an unusable submitted_date, so applied_at must fall back to created_at.
        await Apps.InsertOneAsync(new BsonDocument
        {
            ["_id"] = Bin(_applicationId3),
            ["owner_id"] = Owner,
            ["posting_id"] = Bin(_postingId),
            ["submitted_date"] = "not-a-date",
            ["outcome"] = "closed",
            ["created_at"] = BsonDateTime.Create(App3CreatedAt),
            ["updated_at"] = BsonDateTime.Create(App3CreatedAt)
        });
    }

    [Fact]
    public async Task DryRun_WritesNothing_And_ReportsAccurateCount()
    {
        var service = new ReadModelReconciliationService(_database);
        var before = await Apps.Find(FilterDefinition<BsonDocument>.Empty).SortBy(d => d["_id"]).ToListAsync();

        var report = await service.ReconcileApplicationsAsync(dryRun: true);

        Assert.True(report.IsDryRun);
        Assert.Equal(3, report.ApplicationsExamined);
        Assert.Equal(3, report.ApplicationsUpdated);

        // A dry run must leave every document byte-for-byte unchanged.
        var after = await Apps.Find(FilterDefinition<BsonDocument>.Empty).SortBy(d => d["_id"]).ToListAsync();
        Assert.Equal(before.Count, after.Count);
        for (var i = 0; i < before.Count; i++)
        {
            Assert.Equal(before[i], after[i]);
        }

        var doc1 = await LoadAppAsync(_applicationId1);
        foreach (var field in new[] { "status", "applied_at", "company_id", "company_name", "posting_title", "has_interview", "has_first_response", "has_last_event", "first_response_at", "last_event_at" })
        {
            Assert.False(doc1.Contains(field), $"Dry run must not write '{field}'.");
        }
    }

    [Fact]
    public async Task ConfirmedReconcile_BackfillsLegacyDocuments_Accurately()
    {
        var service = new ReadModelReconciliationService(_database);

        var report = await service.ReconcileApplicationsAsync(dryRun: false);

        Assert.False(report.IsDryRun);
        Assert.Equal(3, report.ApplicationsExamined);
        Assert.Equal(3, report.ApplicationsUpdated);

        // App 1: applied, interview + offer, follow_up excluded from first response.
        var doc1 = await LoadAppAsync(_applicationId1);
        Assert.Equal("applied", doc1["status"].AsString);
        Assert.Equal(Utc(2026, 10, 1), doc1["applied_at"].ToUniversalTime());
        Assert.Equal("Cyberdyne Systems", doc1["company_name"].AsString);
        Assert.Equal("AI Research Lead", doc1["posting_title"].AsString);
        Assert.Equal(_companyId, doc1["company_id"].AsGuid);
        Assert.True(doc1["has_interview"].AsBoolean);
        Assert.True(doc1["has_first_response"].AsBoolean);
        Assert.True(doc1["has_last_event"].AsBoolean);
        Assert.Equal(Utc(2026, 10, 20), doc1["first_response_at"].ToUniversalTime());
        Assert.Equal(Utc(2026, 10, 25), doc1["last_event_at"].ToUniversalTime());

        // App 2: rejected, no events.
        var doc2 = await LoadAppAsync(_applicationId2);
        Assert.Equal("rejected", doc2["status"].AsString);
        Assert.Equal(Utc(2026, 10, 15), doc2["applied_at"].ToUniversalTime());
        Assert.Equal("Cyberdyne Systems", doc2["company_name"].AsString);
        Assert.Equal("AI Research Lead", doc2["posting_title"].AsString);
        Assert.Equal(_companyId, doc2["company_id"].AsGuid);
        Assert.False(doc2["has_interview"].AsBoolean);
        Assert.False(doc2["has_first_response"].AsBoolean);
        Assert.False(doc2["has_last_event"].AsBoolean);
        Assert.True(doc2["first_response_at"].IsBsonNull);
        Assert.True(doc2["last_event_at"].IsBsonNull);

        // App 3: closed, submitted_date unusable -> applied_at falls back to created_at.
        var doc3 = await LoadAppAsync(_applicationId3);
        Assert.Equal("closed", doc3["status"].AsString);
        Assert.Equal(App3CreatedAt, doc3["applied_at"].ToUniversalTime());
    }

    [Fact]
    public async Task SecondDryRun_AfterConfirm_ReportsZero()
    {
        var service = new ReadModelReconciliationService(_database);

        var confirmed = await service.ReconcileApplicationsAsync(dryRun: false);
        Assert.Equal(3, confirmed.ApplicationsUpdated);

        var secondDryRun = await service.ReconcileApplicationsAsync(dryRun: true);

        Assert.True(secondDryRun.IsDryRun);
        Assert.Equal(3, secondDryRun.ApplicationsExamined);
        Assert.Equal(0, secondDryRun.ApplicationsUpdated);
    }

    [Fact]
    public async Task ConfirmedReconcile_IsIdempotent_SecondConfirmedRunReportsZeroUpdated()
    {
        var service = new ReadModelReconciliationService(_database);

        var report1 = await service.ReconcileApplicationsAsync(dryRun: false);
        Assert.Equal(3, report1.ApplicationsUpdated);

        var report2 = await service.ReconcileApplicationsAsync(dryRun: false);
        Assert.Equal(3, report2.ApplicationsExamined);
        Assert.Equal(0, report2.ApplicationsUpdated);
    }

    [Theory]
    [InlineData("status")]
    [InlineData("applied_at")]
    [InlineData("company_id")]
    public async Task DocumentMissingOnlyOneDerivedField_IsDetectedAndRepaired(string missingField)
    {
        var service = new ReadModelReconciliationService(_database);
        await service.ReconcileApplicationsAsync(dryRun: false);

        // Remove exactly one derived field from an otherwise fully reconciled document.
        await Apps.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", Bin(_applicationId1)),
            Builders<BsonDocument>.Update.Unset(missingField));

        var dryRun = await service.ReconcileApplicationsAsync(dryRun: true);
        Assert.Equal(1, dryRun.ApplicationsUpdated);

        var confirmed = await service.ReconcileApplicationsAsync(dryRun: false);
        Assert.Equal(1, confirmed.ApplicationsUpdated);

        var repaired = await LoadAppAsync(_applicationId1);
        Assert.True(repaired.Contains(missingField), $"'{missingField}' should have been restored.");

        var final = await service.ReconcileApplicationsAsync(dryRun: true);
        Assert.Equal(0, final.ApplicationsUpdated);
    }
}
