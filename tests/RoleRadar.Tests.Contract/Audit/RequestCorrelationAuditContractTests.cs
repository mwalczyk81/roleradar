using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Application.Abstractions;
using RoleRadar.Domain.Audit;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Notes;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Infrastructure.Mongo.Repositories;
using RoleRadar.Tests.Contract.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Contract.Audit;

/// <summary>
/// Feature 013 (FR-017, FR-008), restated for feature 014: both audit choke points stamp the trace id (32 lower-case hex
/// characters) of the current request as <c>request_correlation_id</c>, leave the field out when there is no request context, and leave the existing
/// <c>details.after.correlation_id</c> alone.
/// </summary>
public sealed class RequestCorrelationAuditContractTests : IAsyncLifetime
{
    private const string CorrelationId = "0af7651916cd43dd8448eb211c80319c";

    private readonly SharedMongoFixture _sharedMongo;
    private MongoClient _client = null!;
    private IMongoDatabase _database = null!;
    private MongoOptions _options = null!;

    public RequestCorrelationAuditContractTests(SharedMongoFixture sharedMongo)
    {
        _sharedMongo = sharedMongo;
    }

    public async ValueTask InitializeAsync()
    {
        _options = new MongoOptions
        {
            ConnectionString = _sharedMongo.ConnectionString,
            DatabaseName = _sharedMongo.CreateDatabaseName("rrtest_request_corr"),
            ReadinessTimeout = TimeSpan.FromSeconds(5)
        };

        MongoBootstrapAndIndexes.RegisterSerializers();
        _client = new MongoClient(_options.ConnectionString);
        _database = _client.GetDatabase(_options.DatabaseName);
        await MongoBootstrapAndIndexes.EnsureIndexesAsync(_database, CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _client.DropDatabaseAsync(_options.DatabaseName);
        }
        finally
        {
            _client.Dispose();
        }
    }

    private sealed class TestRequestContext(string? correlationId) : IRequestContext
    {
        public string? CorrelationId { get; } = correlationId;
    }

    private IMongoCollection<BsonDocument> AuditCollection =>
        _database.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.AuditLogCollectionName);

    private MongoTrackingTransactionContext CreateTrackingContext(IRequestContext? requestContext) =>
        new(
            client: null,
            companies: new MongoCompanyRepository(_client, _database),
            postings: new MongoPostingRepository(_client, _database),
            criteria: new MongoCriterionRepository(_client, _database),
            applications: new MongoApplicationRepository(_client, _database),
            contacts: null,
            searchRuns: null,
            auditCollection: AuditCollection,
            requestContext: requestContext);

    private MongoNoteRepository CreateNoteRepository(IRequestContext? requestContext)
    {
        var probe = new MongoReadinessProbe(_client, Options.Create(_options), NullLogger<MongoReadinessProbe>.Instance);
        return new MongoNoteRepository(
            _client,
            Options.Create(_options),
            probe,
            NullLogger<MongoNoteRepository>.Instance,
            faultInjectionHook: null,
            requestContext: requestContext);
    }

    private static AuditLogEntry TrackingEntry(IReadOnlyDictionary<string, object?>? after = null) =>
        AuditLogEntry.CreateForTracking(
            OwnerId.Default.Value,
            "contract-test",
            AuditOperation.CompanyUpdate,
            "company",
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            new DateTime(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc),
            AuditDetails.ForEntity(["status"], after: after));

    private static async Task<Guid> CreateNoteAsync(MongoNoteRepository repo)
    {
        var now = new DateTime(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);
        var note = Note.Create(
            ownerId: OwnerId.Default.Value,
            title: "Fictional Correlation Note",
            body: "# Overview\nFictional body.\n",
            tags: ["fictional"],
            area: "criteria",
            provenance: new ProvenanceMarker(ProvenanceKind.Stated, "2026-11-09"),
            createdAtUtc: now);
        var audit = AuditLogEntry.CreateForCreate(
            OwnerId.Default.Value,
            "contract-test",
            note.Id,
            note.VersionToken,
            now,
            AuditContentSummary.FromNote(note));

        var result = await repo.ExecuteInTransactionAsync((tx, ct) => tx.CreateAsync(note, audit, ct));
        Assert.True(result.IsSuccess);
        return note.Id;
    }

    [Fact]
    public async Task TrackingChokePoint_StampsTheRequestCorrelationId()
    {
        var context = CreateTrackingContext(new TestRequestContext(CorrelationId));

        await context.AppendAuditEntryAsync(TrackingEntry());

        var stored = await AuditCollection.Find(Builders<BsonDocument>.Filter.Empty).SingleAsync();
        Assert.True(stored.TryGetValue("request_correlation_id", out var value), "no request_correlation_id on the stored entry");
        Assert.Equal(CorrelationId, value!.AsString);
    }

    [Fact]
    public async Task TrackingChokePoint_LeavesTheFieldOut_WhenThereIsNoRequestContext()
    {
        var context = CreateTrackingContext(requestContext: null);

        await context.AppendAuditEntryAsync(TrackingEntry());

        var stored = await AuditCollection.Find(Builders<BsonDocument>.Filter.Empty).SingleAsync();
        Assert.False(stored.Contains("request_correlation_id"));
    }

    [Fact]
    public async Task TrackingChokePoint_LeavesTheFieldOut_WhenTheContextHasNoId()
    {
        var context = CreateTrackingContext(new TestRequestContext(null));

        await context.AppendAuditEntryAsync(TrackingEntry());

        var stored = await AuditCollection.Find(Builders<BsonDocument>.Filter.Empty).SingleAsync();
        Assert.False(stored.Contains("request_correlation_id"));
    }

    [Fact]
    public async Task TrackingChokePoint_KeepsTheExistingDetailsAfterCorrelationId()
    {
        var context = CreateTrackingContext(new TestRequestContext(CorrelationId));
        var entry = TrackingEntry(after: new Dictionary<string, object?> { ["correlation_id"] = "domain-corr-0001" });

        await context.AppendAuditEntryAsync(entry);

        var stored = await AuditCollection.Find(Builders<BsonDocument>.Filter.Empty).SingleAsync();
        Assert.Equal("domain-corr-0001", stored["details"]["after"]["correlation_id"].AsString);
        Assert.True(stored.TryGetValue("request_correlation_id", out var value), "no request_correlation_id on the stored entry");
        Assert.Equal(CorrelationId, value!.AsString);
    }

    [Fact]
    public async Task NoteChokePoint_StampsTheRequestCorrelationId_AndReadsItBack()
    {
        using var repo = CreateNoteRepository(new TestRequestContext(CorrelationId));

        var noteId = await CreateNoteAsync(repo);

        var stored = await AuditCollection.Find(Builders<BsonDocument>.Filter.Empty).SingleAsync();
        Assert.True(stored.TryGetValue("request_correlation_id", out var value), "no request_correlation_id on the stored entry");
        Assert.Equal(CorrelationId, value!.AsString);

        var entries = await repo.GetAuditEntriesAsync(OwnerId.Default.Value, noteId);
        Assert.True(entries.IsSuccess);
        Assert.Equal(CorrelationId, Assert.Single(entries.Value!).RequestCorrelationId);
    }

    [Fact]
    public async Task NoteChokePoint_LeavesTheFieldOut_WhenThereIsNoRequestContext()
    {
        using var repo = CreateNoteRepository(requestContext: null);

        var noteId = await CreateNoteAsync(repo);

        var stored = await AuditCollection.Find(Builders<BsonDocument>.Filter.Empty).SingleAsync();
        Assert.False(stored.Contains("request_correlation_id"));
        var entries = await repo.GetAuditEntriesAsync(OwnerId.Default.Value, noteId);
        Assert.Null(Assert.Single(entries.Value!).RequestCorrelationId);
    }

    [Fact]
    public void Rehydrate_ReadsTheRequestCorrelationIdBack()
    {
        var entry = AuditLogEntry.Rehydrate(
            Guid.CreateVersion7(),
            OwnerId.Default.Value,
            "contract-test",
            AuditOperation.CompanyUpdate,
            Guid.Empty,
            null,
            null,
            Guid.CreateVersion7(),
            null,
            new DateTime(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc),
            null,
            null,
            entityType: "company",
            entityId: Guid.CreateVersion7(),
            details: AuditDetails.ForEntity(["status"]),
            requestCorrelationId: CorrelationId);

        Assert.Equal(CorrelationId, entry.RequestCorrelationId);
    }
}
