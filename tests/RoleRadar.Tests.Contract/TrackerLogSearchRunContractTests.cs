using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Commands;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Tracking;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Infrastructure.Mongo.Repositories;
using RoleRadar.Tests.Contract.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Contract;

public sealed class TrackerLogSearchRunContractTests : IAsyncLifetime
{
    private readonly SharedMongoFixture _fixture;
    private readonly string _databaseName;
    private MongoClient _client = null!;
    private IMongoDatabase _database = null!;

    public TrackerLogSearchRunContractTests(SharedMongoFixture fixture)
    {
        _fixture = fixture;
        _databaseName = _fixture.CreateDatabaseName("roleradar_search_run_contract");
    }

    public async ValueTask InitializeAsync()
    {
        _client = new MongoClient(_fixture.ConnectionString);
        _database = _client.GetDatabase(_databaseName);
        await TrackingIndexInitializer.EnsureIndexesAsync(_database, CancellationToken.None);
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
    public async Task TrackerLog_SearchRun_LogsNewRun_AndAuditsEntry()
    {
        var companyRepo = new MongoCompanyRepository(_client, _database);
        var postingRepo = new MongoPostingRepository(_client, _database);
        var criterionRepo = new MongoCriterionRepository(_client, _database);
        var applicationRepo = new MongoApplicationRepository(_client, _database);
        var contactRepo = new MongoContactRepository(_client, _database);
        var searchRunRepo = new MongoSearchRunRepository(_client, _database);
        var auditCol = _database.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.AuditLogCollectionName);

        var context = new MongoTrackingTransactionContext(
            companyRepo,
            postingRepo,
            criterionRepo,
            applicationRepo,
            contactRepo,
            searchRunRepo,
            auditCol);

        var handler = new TrackerLogCommandHandler(context, SystemUtcClock.Instance);

        var reportNoteId = Guid.NewGuid();
        var startedAt = "2026-10-18T10:00:00Z";
        var cmd = new TrackerLogCommand(
            Action: "search_run",
            StartedAt: startedAt,
            NewPostingsCount: 5,
            ReportNoteId: reportNoteId,
            Notes: "Weekly target sweep covering 12 target companies.",
            OwnerId: OwnerId.Default.Value);

        var result = await handler.HandleAsync(cmd, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Handler failed: {result.Error?.Message}");
        Assert.NotNull(result.Value);
        Assert.Equal("search_run", result.Value.Action);
        Assert.Equal("success", result.Value.Status);
        Assert.NotEqual(Guid.Empty, result.Value.RecordId);

        // Verify record in search_runs collection
        var searchRunsCol = _database.GetCollection<BsonDocument>(TrackingMongoOptions.SearchRunsCollectionName);
        var doc = await searchRunsCol.Find(Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(result.Value.RecordId, GuidRepresentation.Standard))).FirstOrDefaultAsync();
        Assert.NotNull(doc);
        Assert.Equal(OwnerId.Default.Value, doc["owner_id"].AsString);
        Assert.Equal(5, doc["new_postings_count"].AsInt32);
        Assert.Equal(reportNoteId, doc["report_note_id"].AsGuid);
        Assert.Equal("Weekly target sweep covering 12 target companies.", doc["notes"].AsString);

        // Verify audit log entry
        var auditDoc = await auditCol.Find(Builders<BsonDocument>.Filter.Eq("entity_id", new BsonBinaryData(result.Value.RecordId, GuidRepresentation.Standard))).FirstOrDefaultAsync();
        Assert.NotNull(auditDoc);
        Assert.Equal("search_run", auditDoc["entity_type"].AsString);
        Assert.Equal("search_run_log", auditDoc["operation"].AsString);
    }

    [Fact]
    public async Task TrackerLog_SearchRun_DuplicateStartedAt_ReturnsAlreadyLogged_WithoutWriteOrAudit()
    {
        var companyRepo = new MongoCompanyRepository(_client, _database);
        var postingRepo = new MongoPostingRepository(_client, _database);
        var criterionRepo = new MongoCriterionRepository(_client, _database);
        var applicationRepo = new MongoApplicationRepository(_client, _database);
        var contactRepo = new MongoContactRepository(_client, _database);
        var searchRunRepo = new MongoSearchRunRepository(_client, _database);
        var auditCol = _database.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.AuditLogCollectionName);

        var context = new MongoTrackingTransactionContext(
            companyRepo,
            postingRepo,
            criterionRepo,
            applicationRepo,
            contactRepo,
            searchRunRepo,
            auditCol);

        var handler = new TrackerLogCommandHandler(context, SystemUtcClock.Instance);

        var startedAt = "2026-10-18T14:30:00Z";
        var firstCmd = new TrackerLogCommand(
            Action: "search_run",
            StartedAt: startedAt,
            NewPostingsCount: 3,
            Notes: "First run",
            OwnerId: OwnerId.Default.Value);

        var firstResult = await handler.HandleAsync(firstCmd, CancellationToken.None);
        Assert.True(firstResult.IsSuccess);
        Assert.Equal("success", firstResult.Value!.Status);

        var searchRunsCol = _database.GetCollection<BsonDocument>(TrackingMongoOptions.SearchRunsCollectionName);
        var runsCountBefore = await searchRunsCol.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);
        var auditCountBefore = await auditCol.CountDocumentsAsync(Builders<BsonDocument>.Filter.Eq("target_note_id", new BsonBinaryData(firstResult.Value.RecordId, GuidRepresentation.Standard)));

        // Resubmit with identical startedAt
        var secondCmd = new TrackerLogCommand(
            Action: "search_run",
            StartedAt: startedAt,
            NewPostingsCount: 10,
            Notes: "Second duplicate attempt",
            OwnerId: OwnerId.Default.Value);

        var secondResult = await handler.HandleAsync(secondCmd, CancellationToken.None);
        Assert.True(secondResult.IsSuccess);
        Assert.NotNull(secondResult.Value);
        Assert.Equal("search_run", secondResult.Value.Action);
        Assert.Equal("already_logged", secondResult.Value.Status);
        Assert.Equal(firstResult.Value.RecordId, secondResult.Value.RecordId);

        var runsCountAfter = await searchRunsCol.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);
        var auditCountAfter = await auditCol.CountDocumentsAsync(Builders<BsonDocument>.Filter.Eq("target_note_id", new BsonBinaryData(firstResult.Value.RecordId, GuidRepresentation.Standard)));

        Assert.Equal(runsCountBefore, runsCountAfter);
        Assert.Equal(auditCountBefore, auditCountAfter);
    }

    [Fact]
    public async Task TrackerLog_SearchRun_InvalidInputs_ReturnsValidationError()
    {
        var companyRepo = new MongoCompanyRepository(_client, _database);
        var postingRepo = new MongoPostingRepository(_client, _database);
        var criterionRepo = new MongoCriterionRepository(_client, _database);
        var applicationRepo = new MongoApplicationRepository(_client, _database);
        var contactRepo = new MongoContactRepository(_client, _database);
        var searchRunRepo = new MongoSearchRunRepository(_client, _database);
        var auditCol = _database.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.AuditLogCollectionName);

        var context = new MongoTrackingTransactionContext(
            companyRepo,
            postingRepo,
            criterionRepo,
            applicationRepo,
            contactRepo,
            searchRunRepo,
            auditCol);

        var handler = new TrackerLogCommandHandler(context, SystemUtcClock.Instance);

        // Missing startedAt
        var noStartedAt = new TrackerLogCommand(Action: "search_run", NewPostingsCount: 2);
        var res1 = await handler.HandleAsync(noStartedAt, CancellationToken.None);
        Assert.False(res1.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, res1.Error!.Value.Code);

        // Negative count
        var negativeCount = new TrackerLogCommand(Action: "search_run", StartedAt: "2026-10-18T10:00:00Z", NewPostingsCount: -1);
        var res2 = await handler.HandleAsync(negativeCount, CancellationToken.None);
        Assert.False(res2.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, res2.Error!.Value.Code);
    }
}
