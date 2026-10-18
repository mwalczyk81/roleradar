using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Commands;
using RoleRadar.Application.Tracking.Queries;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Tracking;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Infrastructure.Mongo.Repositories;
using RoleRadar.Tests.Contract.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Contract;

public sealed class PostingCompHighPriorityContractTests : IAsyncLifetime
{
    private readonly SharedMongoFixture _fixture;
    private readonly string _databaseName;
    private MongoClient _client = null!;
    private IMongoDatabase _database = null!;
    private MongoCompanyRepository _companyRepo = null!;
    private MongoPostingRepository _postingRepo = null!;
    private MongoCriterionRepository _criterionRepo = null!;
    private MongoTrackingTransactionContext _context = null!;

    public PostingCompHighPriorityContractTests(SharedMongoFixture fixture)
    {
        _fixture = fixture;
        _databaseName = _fixture.CreateDatabaseName("roleradar_posting_comp_pri");
    }

    public async ValueTask InitializeAsync()
    {
        _client = new MongoClient(_fixture.ConnectionString);
        _database = _client.GetDatabase(_databaseName);
        await TrackingIndexInitializer.EnsureIndexesAsync(_database, CancellationToken.None);
        _companyRepo = new MongoCompanyRepository(_client, _database);
        _postingRepo = new MongoPostingRepository(_client, _database);
        _criterionRepo = new MongoCriterionRepository(_client, _database);
        _context = new MongoTrackingTransactionContext(
            _companyRepo,
            _postingRepo,
            _criterionRepo,
            new MongoApplicationRepository(_client, _database),
            _database.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.AuditLogCollectionName));
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
    public async Task PostingRecord_Create_WithCompHighPriority_PersistsAndReturnsFlag()
    {
        var company = Company.Create("owner-1", "High Priority Co");
        await _companyRepo.UpsertCompanyAsync(company);

        var handler = new RecordPostingCommandHandler(_context, new SystemUtcClock());
        var command = new RecordPostingCommand(
            CompanyId: company.Id,
            Title: "Lead Architect",
            SourceKind: "ats",
            AtsTenant: "hipri-ats",
            RequisitionId: "REQ-HP-1",
            CompHighPriority: true,
            OwnerId: "owner-1");

        var result = await handler.HandleAsync(command);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("created", result.Value.Status);
        Assert.NotNull(result.Value.Posting);
        Assert.True(result.Value.Posting.CompHighPriority);

        // Verify stored in MongoDB document as boolean true
        var rawDoc = await _database.GetCollection<BsonDocument>("postings")
            .Find(Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(result.Value.Posting.Id, GuidRepresentation.Standard)))
            .FirstOrDefaultAsync();

        Assert.NotNull(rawDoc);
        Assert.True(rawDoc.Contains("comp_high_priority"));
        Assert.True(rawDoc["comp_high_priority"].AsBoolean);
    }

    [Fact]
    public async Task PostingRecord_Create_NullDefault_DefaultsToNullNotJudged()
    {
        var company = Company.Create("owner-1", "Null Priority Co");
        await _companyRepo.UpsertCompanyAsync(company);

        var handler = new RecordPostingCommandHandler(_context, new SystemUtcClock());
        var command = new RecordPostingCommand(
            CompanyId: company.Id,
            Title: "Backend Developer",
            SourceKind: "ats",
            AtsTenant: "nullpri-ats",
            RequisitionId: "REQ-NP-1",
            CompHighPriority: null,
            OwnerId: "owner-1");

        var result = await handler.HandleAsync(command);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("created", result.Value.Status);
        Assert.NotNull(result.Value.Posting);
        Assert.Null(result.Value.Posting.CompHighPriority);

        // Verify stored in MongoDB document as BsonNull or null
        var rawDoc = await _database.GetCollection<BsonDocument>("postings")
            .Find(Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(result.Value.Posting.Id, GuidRepresentation.Standard)))
            .FirstOrDefaultAsync();

        Assert.NotNull(rawDoc);
        Assert.True(!rawDoc.Contains("comp_high_priority") || rawDoc["comp_high_priority"].IsBsonNull);
    }

    [Fact]
    public async Task PostingRecord_ExplicitUpdate_WithCompHighPriority_UpdatesFieldRotatesTokenAndAudits()
    {
        var company = Company.Create("owner-1", "Update Priority Co");
        await _companyRepo.UpsertCompanyAsync(company);

        var handler = new RecordPostingCommandHandler(_context, new SystemUtcClock());
        var createCommand = new RecordPostingCommand(
            CompanyId: company.Id,
            Title: "Staff Engineer",
            SourceKind: "ats",
            AtsTenant: "updatepri-ats",
            RequisitionId: "REQ-UP-1",
            CompHighPriority: null,
            OwnerId: "owner-1");

        var createResult = await handler.HandleAsync(createCommand);
        Assert.True(createResult.IsSuccess);
        var originalPosting = createResult.Value!.Posting!;
        var originalToken = originalPosting.VersionToken;

        // Explicit update to true
        var updateCommand = new RecordPostingCommand(
            PostingId: originalPosting.Id,
            VersionToken: originalToken,
            CompHighPriority: true,
            OwnerId: "owner-1");

        var updateResult = await handler.HandleAsync(updateCommand);

        Assert.True(updateResult.IsSuccess);
        Assert.NotNull(updateResult.Value);
        Assert.Equal("updated", updateResult.Value.Status);
        Assert.NotNull(updateResult.Value.Posting);
        Assert.True(updateResult.Value.Posting.CompHighPriority);
        Assert.NotEqual(originalToken, updateResult.Value.Posting.VersionToken);

        // Verify stored in Mongo
        var updatedDoc = await _database.GetCollection<BsonDocument>("postings")
            .Find(Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(originalPosting.Id, GuidRepresentation.Standard)))
            .FirstOrDefaultAsync();
        Assert.NotNull(updatedDoc);
        Assert.True(updatedDoc["comp_high_priority"].AsBoolean);

        // Verify audit log has comp_high_priority in changed field names
        var auditEntry = await _database.GetCollection<BsonDocument>("audit_log")
            .Find(Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("target_note_id", new BsonBinaryData(originalPosting.Id, GuidRepresentation.Standard)),
                Builders<BsonDocument>.Filter.Eq("operation", "note_append")))
            .FirstOrDefaultAsync();

        Assert.NotNull(auditEntry);
        Assert.Contains("comp_high_priority", auditEntry["appended_text"].AsString);
    }

    [Fact]
    public async Task PostingRecord_ExplicitUpdate_StaleVersionToken_ReturnsStaleVersionTokenError()
    {
        var company = Company.Create("owner-1", "Stale Priority Co");
        await _companyRepo.UpsertCompanyAsync(company);

        var handler = new RecordPostingCommandHandler(_context, new SystemUtcClock());
        var createCommand = new RecordPostingCommand(
            CompanyId: company.Id,
            Title: "Engineering Lead",
            SourceKind: "ats",
            AtsTenant: "stalepri-ats",
            RequisitionId: "REQ-SP-1",
            OwnerId: "owner-1");

        var createResult = await handler.HandleAsync(createCommand);
        Assert.True(createResult.IsSuccess);
        var posting = createResult.Value!.Posting!;

        // Update with fake version token
        var updateCommand = new RecordPostingCommand(
            PostingId: posting.Id,
            VersionToken: Guid.CreateVersion7(),
            CompHighPriority: true,
            OwnerId: "owner-1");

        var updateResult = await handler.HandleAsync(updateCommand);

        Assert.False(updateResult.IsSuccess);
        Assert.Equal(ErrorCode.StaleVersionToken, updateResult.Error!.Value.Code);
    }

    [Fact]
    public async Task PostingRecord_DedupMatch_DifferencesLogic_ExcludesIfUnchanged_IncludesIfChanged()
    {
        var company = Company.Create("owner-1", "Dedup Priority Co");
        await _companyRepo.UpsertCompanyAsync(company);

        var handler = new RecordPostingCommandHandler(_context, new SystemUtcClock());
        var createCommand = new RecordPostingCommand(
            CompanyId: company.Id,
            Title: "Senior DevOps Engineer",
            SourceKind: "ats",
            AtsTenant: "deduppri-ats",
            RequisitionId: "REQ-DP-1",
            CompHighPriority: false,
            OwnerId: "owner-1");

        var createResult = await handler.HandleAsync(createCommand);
        Assert.True(createResult.IsSuccess);

        // Re-encounter with identical CompHighPriority (false) -> excluded from differences
        var sameCommand = new RecordPostingCommand(
            CompanyId: company.Id,
            Title: "Senior DevOps Engineer",
            SourceKind: "ats",
            AtsTenant: "deduppri-ats",
            RequisitionId: "REQ-DP-1",
            CompHighPriority: false,
            OwnerId: "owner-1");

        var sameResult = await handler.HandleAsync(sameCommand);
        Assert.True(sameResult.IsSuccess);
        Assert.Equal("already_tracked", sameResult.Value!.Status);
        Assert.True(sameResult.Value.Differences is null ||
                    !sameResult.Value.Differences.Any(d => d.Field == "comp_high_priority"));

        // Re-encounter with different CompHighPriority (true) -> included in differences
        var diffCommand = new RecordPostingCommand(
            CompanyId: company.Id,
            Title: "Senior DevOps Engineer",
            SourceKind: "ats",
            AtsTenant: "deduppri-ats",
            RequisitionId: "REQ-DP-1",
            CompHighPriority: true,
            OwnerId: "owner-1");

        var diffResult = await handler.HandleAsync(diffCommand);
        Assert.True(diffResult.IsSuccess);
        Assert.Equal("already_tracked", diffResult.Value!.Status);
        Assert.NotNull(diffResult.Value.Differences);
        var diffItem = diffResult.Value.Differences.FirstOrDefault(d => d.Field == "comp_high_priority");
        Assert.NotNull(diffItem);
        Assert.Equal(false, diffItem.StoredValue);
        Assert.Equal(true, diffItem.SuppliedValue);
    }

    [Fact]
    public async Task TrackerQuery_PostingsAndDecidedPostings_ReturnsCompHighPriority()
    {
        var company = Company.Create("owner-1", "Query Priority Co");
        await _companyRepo.UpsertCompanyAsync(company);

        var postHandler = new RecordPostingCommandHandler(_context, new SystemUtcClock());
        var transitionHandler = new TransitionPostingCommandHandler(_context, new SystemUtcClock());
        var queryHandler = new TrackerQueryHandler(_context, new SystemUtcClock());

        // 1. Active posting with CompHighPriority = true
        var createActive = await postHandler.HandleAsync(new RecordPostingCommand(
            CompanyId: company.Id,
            Title: "Active Role",
            SourceKind: "ats",
            AtsTenant: "query-ats",
            RequisitionId: "REQ-QA-1",
            CompHighPriority: true,
            OwnerId: "owner-1"));
        Assert.True(createActive.IsSuccess);

        // 2. Decided posting with CompHighPriority = false
        var createDecided = await postHandler.HandleAsync(new RecordPostingCommand(
            CompanyId: company.Id,
            Title: "Decided Role",
            SourceKind: "ats",
            AtsTenant: "query-ats",
            RequisitionId: "REQ-QD-1",
            CompHighPriority: false,
            OwnerId: "owner-1"));
        Assert.True(createDecided.IsSuccess);

        var transResult = await transitionHandler.HandleAsync(new TransitionPostingCommand(
            PostingId: createDecided.Value!.Posting!.Id,
            TargetDisposition: "screened_out",
            VersionToken: createDecided.Value.Posting.VersionToken,
            Reason: "Not enough experience",
            OwnerId: "owner-1"));
        Assert.True(transResult.IsSuccess);

        // Query postings
        var postingsQuery = await queryHandler.HandleAsync(new TrackerQueryRequest("postings", OwnerId: "owner-1"));
        Assert.True(postingsQuery.IsSuccess);
        var activeItem = postingsQuery.Value!.Items.OfType<PostingSummaryItemDto>()
            .FirstOrDefault(p => p.Id == createActive.Value!.Posting!.Id);
        Assert.NotNull(activeItem);
        Assert.True(activeItem.CompHighPriority);

        // Query decided_postings
        var decidedQuery = await queryHandler.HandleAsync(new TrackerQueryRequest("decided_postings", OwnerId: "owner-1"));
        Assert.True(decidedQuery.IsSuccess);
        var decidedItem = decidedQuery.Value!.Items.OfType<DecidedPostingSummaryItemDto>()
            .FirstOrDefault(p => p.Id == createDecided.Value!.Posting!.Id);
        Assert.NotNull(decidedItem);
        Assert.False(decidedItem.CompHighPriority);
    }
}
