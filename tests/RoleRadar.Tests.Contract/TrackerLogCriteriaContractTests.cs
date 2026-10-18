using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Commands;
using RoleRadar.Application.Tracking.Queries;
using RoleRadar.Domain.Audit;
using RoleRadar.Domain.Common;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Infrastructure.Mongo.Repositories;
using RoleRadar.Tests.Contract.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Contract;

public sealed class TrackerLogCriteriaContractTests : IAsyncLifetime
{
    private readonly SharedMongoFixture _fixture;
    private readonly string _databaseName;
    private MongoClient _client = null!;
    private IMongoDatabase _database = null!;
    private MongoCompanyRepository _companyRepo = null!;
    private MongoPostingRepository _postingRepo = null!;
    private MongoCriterionRepository _criterionRepo = null!;
    private TrackingTransactionContextReal _context = null!;

    public TrackerLogCriteriaContractTests(SharedMongoFixture fixture)
    {
        _fixture = fixture;
        _databaseName = _fixture.CreateDatabaseName("roleradar_criteria_contract");
    }

    public async ValueTask InitializeAsync()
    {
        _client = new MongoClient(_fixture.ConnectionString);
        _database = _client.GetDatabase(_databaseName);
        await TrackingIndexInitializer.EnsureIndexesAsync(_database, CancellationToken.None);
        _companyRepo = new MongoCompanyRepository(_client, _database);
        _postingRepo = new MongoPostingRepository(_client, _database);
        _criterionRepo = new MongoCriterionRepository(_client, _database);
        _context = new TrackingTransactionContextReal(_companyRepo, _postingRepo, _criterionRepo, _database);
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
    public async Task TrackerLog_CriterionRevision_StrictSequencingAndTokenHandling()
    {
        var handler = new TrackerLogCommandHandler(_context, SystemUtcClock.Instance);

        // 1. Initial version
        var initialCmd = new TrackerLogCommand(
            Action: "criterion_revision",
            Key: "min_base_salary",
            Category: "hard_skip",
            Value: "180000",
            Provenance: "stated",
            EffectiveFrom: "2026-01-01T00:00:00Z",
            OwnerId: "owner-1");

        var v1Result = await handler.HandleAsync(initialCmd);
        Assert.True(v1Result.IsSuccess);
        Assert.Equal("criterion_revision", v1Result.Value!.Action);
        Assert.NotNull(v1Result.Value!.VersionToken);
        Assert.NotEqual(Guid.Empty, v1Result.Value!.VersionToken);

        var activeCriterionRes = await _criterionRepo.GetActiveByKeyAsync("owner-1", "min_base_salary");
        Assert.True(activeCriterionRes.IsSuccess);
        Assert.NotNull(activeCriterionRes.Value);
        var activeCriterion = activeCriterionRes.Value!;
        Assert.Equal("180000", activeCriterion.Value);
        Assert.Equal(activeCriterion.VersionToken, v1Result.Value!.VersionToken);

        // 2. Attempt revision without version token -> StaleVersionToken
        var noTokenCmd = new TrackerLogCommand(
            Action: "criterion_revision",
            Key: "min_base_salary",
            Category: "hard_skip",
            Value: "200000",
            Provenance: "stated",
            EffectiveFrom: "2026-07-01T00:00:00Z",
            OwnerId: "owner-1");

        var noTokenRes = await handler.HandleAsync(noTokenCmd);
        Assert.False(noTokenRes.IsSuccess);
        Assert.Equal(ErrorCode.StaleVersionToken, noTokenRes.Error!.Value.Code);

        // 3. Attempt revision with mismatched version token -> StaleVersionToken
        var staleTokenCmd = new TrackerLogCommand(
            Action: "criterion_revision",
            Key: "min_base_salary",
            Category: "hard_skip",
            Value: "200000",
            Provenance: "stated",
            EffectiveFrom: "2026-07-01T00:00:00Z",
            VersionToken: Guid.NewGuid(),
            OwnerId: "owner-1");

        var staleTokenRes = await handler.HandleAsync(staleTokenCmd);
        Assert.False(staleTokenRes.IsSuccess);
        Assert.Equal(ErrorCode.StaleVersionToken, staleTokenRes.Error!.Value.Code);

        // 4. Attempt revision with non-sequenced effectiveFrom (earlier than v1) -> ValidationError
        var nonSeqCmd = new TrackerLogCommand(
            Action: "criterion_revision",
            Key: "min_base_salary",
            Category: "hard_skip",
            Value: "200000",
            Provenance: "stated",
            EffectiveFrom: "2025-12-01T00:00:00Z",
            VersionToken: activeCriterion.VersionToken,
            OwnerId: "owner-1");

        var nonSeqRes = await handler.HandleAsync(nonSeqCmd);
        Assert.False(nonSeqRes.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, nonSeqRes.Error!.Value.Code);

        // 5. Successful revision
        var v2Cmd = new TrackerLogCommand(
            Action: "criterion_revision",
            Key: "min_base_salary",
            Category: "hard_skip",
            Value: "200000",
            Provenance: "stated",
            EffectiveFrom: "2026-07-01T00:00:00Z",
            VersionToken: activeCriterion.VersionToken,
            OwnerId: "owner-1");

        var v2Result = await handler.HandleAsync(v2Cmd);
        Assert.True(v2Result.IsSuccess);
        Assert.NotNull(v2Result.Value!.VersionToken);
        Assert.NotEqual(Guid.Empty, v2Result.Value!.VersionToken);

        // Check DB state: exactly 1 active version, previous version closed
        var v2ActiveRes = await _criterionRepo.GetActiveByKeyAsync("owner-1", "min_base_salary");
        Assert.True(v2ActiveRes.IsSuccess);
        Assert.NotNull(v2ActiveRes.Value);
        var v2Active = v2ActiveRes.Value!;
        Assert.Equal("200000", v2Active.Value);
        Assert.Equal(v2Active.VersionToken, v2Result.Value!.VersionToken);
        Assert.Equal(DateTimeOffset.Parse("2026-07-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture), v2Active.EffectiveFrom);
        Assert.Null(v2Active.EffectiveTo);

        var v1OldRes = await _criterionRepo.GetByIdAsync("owner-1", activeCriterion.Id);
        Assert.True(v1OldRes.IsSuccess);
        Assert.NotNull(v1OldRes.Value);
        var v1Old = v1OldRes.Value!;
        Assert.False(v1Old.IsActive);
        Assert.Equal(DateTimeOffset.Parse("2026-07-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture), v1Old.EffectiveTo);

        // Assert audit entries for both initial create and revision
        var v1Audit = await _database.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.AuditLogCollectionName)
            .Find(Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("target_note_id", new BsonBinaryData(activeCriterion.Id, GuidRepresentation.Standard)),
                Builders<BsonDocument>.Filter.Eq("operation", "note_create")))
            .FirstOrDefaultAsync();
        Assert.NotNull(v1Audit);
        Assert.Equal(activeCriterion.VersionToken, v1Audit["resulting_version_token"].AsGuid);

        var v2Audit = await _database.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.AuditLogCollectionName)
            .Find(Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("target_note_id", new BsonBinaryData(v2Active.Id, GuidRepresentation.Standard)),
                Builders<BsonDocument>.Filter.Eq("operation", "note_append")))
            .FirstOrDefaultAsync();
        Assert.NotNull(v2Audit);
        Assert.Equal(v2Active.VersionToken, v2Audit["resulting_version_token"].AsGuid);
    }

    [Fact]
    public async Task TrackerLog_CriterionDeactivate_ClosesActiveCriterionWithoutReplacement()
    {
        var handler = new TrackerLogCommandHandler(_context, SystemUtcClock.Instance);

        var initialCmd = new TrackerLogCommand(
            Action: "criterion_revision",
            Key: "commute_time",
            Category: "preference",
            Value: "45m",
            Provenance: "stated",
            EffectiveFrom: "2026-01-01T00:00:00Z",
            OwnerId: "owner-1");

        var v1Result = await handler.HandleAsync(initialCmd);
        Assert.True(v1Result.IsSuccess);

        var activeRes = await _criterionRepo.GetActiveByKeyAsync("owner-1", "commute_time");
        Assert.NotNull(activeRes.Value);
        var active = activeRes.Value!;

        // Missing version token -> StaleVersionToken
        var deactNoToken = new TrackerLogCommand(
            Action: "criterion_deactivate",
            Key: "commute_time",
            OwnerId: "owner-1");
        var deactNoTokenRes = await handler.HandleAsync(deactNoToken);
        Assert.False(deactNoTokenRes.IsSuccess);
        Assert.Equal(ErrorCode.StaleVersionToken, deactNoTokenRes.Error!.Value.Code);

        // Successful deactivation
        var deactCmd = new TrackerLogCommand(
            Action: "criterion_deactivate",
            Key: "commute_time",
            VersionToken: active.VersionToken,
            OwnerId: "owner-1");

        var deactRes = await handler.HandleAsync(deactCmd);
        Assert.True(deactRes.IsSuccess);
        Assert.Equal("criterion_deactivate", deactRes.Value!.Action);
        Assert.NotNull(deactRes.Value!.VersionToken);
        Assert.NotEqual(Guid.Empty, deactRes.Value!.VersionToken);

        var activeAfterRes = await _criterionRepo.GetActiveByKeyAsync("owner-1", "commute_time");
        Assert.True(activeAfterRes.IsSuccess);
        Assert.Null(activeAfterRes.Value);

        // Assert audit entry for deactivation
        var deactAudit = await _database.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.AuditLogCollectionName)
            .Find(Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("target_note_id", new BsonBinaryData(active.Id, GuidRepresentation.Standard)),
                Builders<BsonDocument>.Filter.Eq("operation", "note_append")))
            .FirstOrDefaultAsync();
        Assert.NotNull(deactAudit);
        var deactStored = (await _criterionRepo.GetByIdAsync("owner-1", active.Id)).Value!;
        Assert.Equal(deactStored.VersionToken, deactAudit["resulting_version_token"].AsGuid);
        Assert.Equal(deactStored.VersionToken, deactRes.Value!.VersionToken);
    }

    [Fact]
    public async Task TrackerQuery_Criteria_PointInTimeEvaluation()
    {
        var handler = new TrackerLogCommandHandler(_context, SystemUtcClock.Instance);

        // v1: 2026-01-01
        var v1Cmd = new TrackerLogCommand(
            Action: "criterion_revision",
            Key: "remote_flexibility",
            Category: "hard_skip",
            Value: "fully_remote",
            Provenance: "stated",
            EffectiveFrom: "2026-01-01T00:00:00Z",
            OwnerId: "owner-1");
        await handler.HandleAsync(v1Cmd);

        var v1Active = (await _criterionRepo.GetActiveByKeyAsync("owner-1", "remote_flexibility")).Value!;

        // v2: 2026-07-01
        var v2Cmd = new TrackerLogCommand(
            Action: "criterion_revision",
            Key: "remote_flexibility",
            Category: "preference",
            Value: "hybrid_acceptable",
            Provenance: "stated",
            EffectiveFrom: "2026-07-01T00:00:00Z",
            VersionToken: v1Active.VersionToken,
            OwnerId: "owner-1");
        await handler.HandleAsync(v2Cmd);

        var v2Active = (await _criterionRepo.GetActiveByKeyAsync("owner-1", "remote_flexibility")).Value!;

        // Deactivate: 2026-09-01
        var deactCmd = new TrackerLogCommand(
            Action: "criterion_deactivate",
            Key: "remote_flexibility",
            EffectiveFrom: "2026-09-01T00:00:00Z",
            VersionToken: v2Active.VersionToken,
            OwnerId: "owner-1");
        await handler.HandleAsync(deactCmd);

        var queryHandler = new TrackerQueryHandler(_context, new SystemUtcClock());

        // 1. Before v1 (2025-12-15) -> 0 matching criteria
        var qBefore = await queryHandler.HandleAsync(new TrackerQueryRequest(
            Entity: "criteria",
            EffectiveDate: "2025-12-15",
            OwnerId: "owner-1"));
        Assert.True(qBefore.IsSuccess);
        Assert.Empty(qBefore.Value!.Items);

        // 2. Mid v1 (2026-03-15) -> v1 ($fully_remote)
        var qMidV1 = await queryHandler.HandleAsync(new TrackerQueryRequest(
            Entity: "criteria",
            EffectiveDate: "2026-03-15",
            OwnerId: "owner-1"));
        Assert.True(qMidV1.IsSuccess);
        Assert.Single(qMidV1.Value!.Items);

        // 3. Mid v2 (2026-07-15) -> v2 ($hybrid_acceptable)
        var qMidV2 = await queryHandler.HandleAsync(new TrackerQueryRequest(
            Entity: "criteria",
            EffectiveDate: "2026-07-15",
            OwnerId: "owner-1"));
        Assert.True(qMidV2.IsSuccess);
        Assert.Single(qMidV2.Value!.Items);

        // 4. After deactivation (2026-10-01) -> 0 matching criteria
        var qAfter = await queryHandler.HandleAsync(new TrackerQueryRequest(
            Entity: "criteria",
            EffectiveDate: "2026-10-01",
            OwnerId: "owner-1"));
        Assert.True(qAfter.IsSuccess);
        Assert.Empty(qAfter.Value!.Items);
    }

    private sealed class TrackingTransactionContextReal : ITrackingTransactionContext
    {
        private readonly IMongoCollection<BsonDocument> _auditCol;

        public TrackingTransactionContextReal(
            ICompanyRepository companyRepo,
            IPostingRepository postingRepo,
            ICriterionRepository criterionRepo,
            IMongoDatabase database)
        {
            Companies = companyRepo;
            Postings = postingRepo;
            Criteria = criterionRepo;
            _auditCol = database.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.AuditLogCollectionName);
        }

        public ICompanyRepository Companies { get; }
        public IPostingRepository Postings { get; }
        public ICriterionRepository Criteria { get; }
        public IApplicationRepository Applications => throw new NotImplementedException();
        public IContactRepository Contacts => throw new NotImplementedException();
        public ISearchRunRepository SearchRuns => throw new NotImplementedException();

        public Task AppendAuditEntryAsync(AuditLogEntry entry, CancellationToken cancellationToken = default)
        {
            var doc = new BsonDocument
            {
                ["_id"] = new BsonBinaryData(entry.Id, GuidRepresentation.Standard),
                ["owner_id"] = entry.OwnerId,
                ["actor"] = entry.Actor,
                ["operation"] = entry.Operation,
                ["target_note_id"] = new BsonBinaryData(entry.TargetNoteId, GuidRepresentation.Standard),
                ["resulting_version_token"] = new BsonBinaryData(entry.ResultingVersionToken, GuidRepresentation.Standard),
                ["appended_text"] = entry.AppendedText is not null ? new BsonString(entry.AppendedText) : BsonNull.Value,
                ["occurred_at"] = new BsonDateTime(entry.OccurredAtUtc)
            };
            return _auditCol.InsertOneAsync(doc, cancellationToken: cancellationToken);
        }
    }
}
