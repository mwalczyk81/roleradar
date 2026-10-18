using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Commands;
using RoleRadar.Domain.Audit;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Tracking;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Infrastructure.Mongo.Repositories;
using RoleRadar.Tests.Contract.Fixtures;
using Xunit;
using ApplicationEntity = RoleRadar.Domain.Tracking.Application;

namespace RoleRadar.Tests.Contract;

public sealed class ApplicationRecordContractTests : IAsyncLifetime
{
    private readonly SharedMongoFixture _fixture;
    private readonly string _databaseName;
    private MongoClient _client = null!;
    private IMongoDatabase _database = null!;
    private MongoCompanyRepository _companyRepo = null!;
    private MongoPostingRepository _postingRepo = null!;
    private MongoCriterionRepository _criterionRepo = null!;
    private MongoApplicationRepository _applicationRepo = null!;
    private TrackingTransactionContextReal _context = null!;

    public ApplicationRecordContractTests(SharedMongoFixture fixture)
    {
        _fixture = fixture;
        _databaseName = _fixture.CreateDatabaseName("roleradar_app_contract");
    }

    public async ValueTask InitializeAsync()
    {
        _client = new MongoClient(_fixture.ConnectionString);
        _database = _client.GetDatabase(_databaseName);
        await TrackingIndexInitializer.EnsureIndexesAsync(_database, CancellationToken.None);
        _companyRepo = new MongoCompanyRepository(_client, _database);
        _postingRepo = new MongoPostingRepository(_client, _database);
        _criterionRepo = new MongoCriterionRepository(_client, _database);
        _applicationRepo = new MongoApplicationRepository(_client, _database);
        _context = new TrackingTransactionContextReal(_companyRepo, _postingRepo, _criterionRepo, _applicationRepo, _database);
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

    [Theory]
    [InlineData(PostingDisposition.Lead)]
    [InlineData(PostingDisposition.Unscored)]
    [InlineData(PostingDisposition.Scored)]
    public async Task ApplicationRecord_Create_FromLeadUnscoredScored_AdvancesPostingToApplied(PostingDisposition initialDisp)
    {
        var ownerId = "owner-1";
        var company = Company.Create(ownerId, "Acme Applied Corp", ownershipType: OwnershipType.Private, status: CompanyStatus.Target);
        await _companyRepo.UpsertCompanyAsync(company);

        var posting = Posting.Create(
            ownerId: ownerId,
            companyId: company.Id,
            title: $"Software Engineer ({initialDisp})",
            disposition: initialDisp);
        await _postingRepo.UpsertPostingAsync(posting);

        var handler = new RecordApplicationCommandHandler(_context);
        var command = new RecordApplicationCommand(
            PostingId: posting.Id,
            Action: "create",
            SubmittedDate: "2026-10-18",
            DesiredSalaryMin: 185000m,
            DesiredSalaryMax: 205000m,
            DesiredSalaryCurrency: "USD",
            ResumeVariant: "tailored",
            ResumePointer: "local://resumes/swe-2026.pdf",
            ConfirmationCode: "CONF-987",
            OwnerId: ownerId);

        var result = await handler.HandleAsync(command);

        Assert.True(result.IsSuccess, result.Error?.Message);
        var dto = result.Value!;
        Assert.Equal(posting.Id, dto.PostingId);
        Assert.Equal("applied", dto.CurrentOutcome);
        Assert.False(dto.OverrideApplied);
        Assert.Equal(1, dto.EventCount);

        // Verify posting disposition moved to applied in DB
        var updatedPostingRes = await _postingRepo.GetByIdAsync(ownerId, posting.Id);
        Assert.True(updatedPostingRes.IsSuccess);
        Assert.NotNull(updatedPostingRes.Value);
        Assert.Equal(PostingDisposition.Applied, updatedPostingRes.Value!.Disposition);

        // Verify audit entry exists
        var auditCol = _database.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.AuditLogCollectionName);
        var auditEntries = await auditCol.Find(Builders<BsonDocument>.Filter.Eq("target_note_id", new BsonBinaryData(posting.Id, GuidRepresentation.Standard))).ToListAsync();
        Assert.NotEmpty(auditEntries);
    }

    [Theory]
    [InlineData(PostingDisposition.ScreenedOut)]
    [InlineData(PostingDisposition.Closed)]
    public async Task ApplicationRecord_Create_OnScreenedOutOrClosed_WithoutOverride_FailsWithInvalidTransition(PostingDisposition initialDisp)
    {
        var ownerId = "owner-1";
        var company = Company.Create(ownerId, "Decided Corp", ownershipType: OwnershipType.Private, status: CompanyStatus.Neutral);
        await _companyRepo.UpsertCompanyAsync(company);

        var posting = Posting.Create(
            ownerId: ownerId,
            companyId: company.Id,
            title: $"Role ({initialDisp})",
            disposition: initialDisp,
            dispositionReason: "Initial screen");
        await _postingRepo.UpsertPostingAsync(posting);

        var handler = new RecordApplicationCommandHandler(_context);
        var command = new RecordApplicationCommand(
            PostingId: posting.Id,
            Action: "create",
            SubmittedDate: "2026-10-18",
            ResumeVariant: "as_is",
            OverrideDecided: false,
            OwnerId: ownerId);

        var result = await handler.HandleAsync(command);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.InvalidTransition, result.Error!.Value.Code);

        // Verify posting disposition remains untouched
        var currentPostingRes = await _postingRepo.GetByIdAsync(ownerId, posting.Id);
        Assert.True(currentPostingRes.IsSuccess);
        Assert.Equal(initialDisp, currentPostingRes.Value!.Disposition);
    }

    [Theory]
    [InlineData(PostingDisposition.ScreenedOut)]
    [InlineData(PostingDisposition.Closed)]
    public async Task ApplicationRecord_Create_OnScreenedOutOrClosed_WithOverride_SucceedsInOneAuditedTransaction(PostingDisposition initialDisp)
    {
        var ownerId = "owner-1";
        var company = Company.Create(ownerId, "Override Corp", ownershipType: OwnershipType.Private, status: CompanyStatus.Neutral);
        await _companyRepo.UpsertCompanyAsync(company);

        var posting = Posting.Create(
            ownerId: ownerId,
            companyId: company.Id,
            title: $"Overridden Role ({initialDisp})",
            disposition: initialDisp,
            dispositionReason: "Under comp floor",
            resurfacePolicy: ResurfacePolicy.Never);
        await _postingRepo.UpsertPostingAsync(posting);

        var handler = new RecordApplicationCommandHandler(_context);
        var command = new RecordApplicationCommand(
            PostingId: posting.Id,
            Action: "create",
            SubmittedDate: "2026-10-18",
            ResumeVariant: "ic_framed",
            OverrideDecided: true,
            OverrideReason: "Reopened by hiring manager outreach directly on LinkedIn",
            OwnerId: ownerId);

        var result = await handler.HandleAsync(command);

        Assert.True(result.IsSuccess, result.Error?.Message);
        var dto = result.Value!;
        Assert.Equal("applied", dto.CurrentOutcome);
        Assert.True(dto.OverrideApplied);

        // Posting in DB is now applied
        var updatedPostingRes = await _postingRepo.GetByIdAsync(ownerId, posting.Id);
        Assert.True(updatedPostingRes.IsSuccess);
        Assert.Equal(PostingDisposition.Applied, updatedPostingRes.Value!.Disposition);

        // Verify audit entry captures previous disposition and override justification
        var auditCol = _database.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.AuditLogCollectionName);
        var auditDoc = await auditCol.Find(Builders<BsonDocument>.Filter.Eq("target_note_id", new BsonBinaryData(posting.Id, GuidRepresentation.Standard))).FirstOrDefaultAsync();
        Assert.NotNull(auditDoc);
    }

    [Fact]
    public async Task ApplicationRecord_Create_OnAlreadyAppliedPosting_FailsWithInvalidTransition()
    {
        var ownerId = "owner-1";
        var company = Company.Create(ownerId, "Already Applied Corp", ownershipType: OwnershipType.Private, status: CompanyStatus.Target);
        await _companyRepo.UpsertCompanyAsync(company);

        var posting = Posting.Create(
            ownerId: ownerId,
            companyId: company.Id,
            title: "Already Applied Staff SWE",
            disposition: PostingDisposition.Applied);
        await _postingRepo.UpsertPostingAsync(posting);

        var handler = new RecordApplicationCommandHandler(_context);
        var command = new RecordApplicationCommand(
            PostingId: posting.Id,
            Action: "create",
            SubmittedDate: "2026-10-18",
            ResumeVariant: "tailored",
            OwnerId: ownerId);

        var result = await handler.HandleAsync(command);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.InvalidTransition, result.Error!.Value.Code);
    }

    [Fact]
    public async Task ApplicationRecord_AppendEvent_Offer_LeavesPostingInApplied()
    {
        var ownerId = "owner-1";
        var company = Company.Create(ownerId, "Offer Tech", ownershipType: OwnershipType.Private, status: CompanyStatus.Target);
        await _companyRepo.UpsertCompanyAsync(company);

        var posting = Posting.Create(ownerId, company.Id, "Principal SWE", disposition: PostingDisposition.Applied);
        await _postingRepo.UpsertPostingAsync(posting);

        var app = ApplicationEntity.Create(ownerId, posting.Id, "2026-10-10", ResumeVariant.Tailored);
        await _applicationRepo.UpsertApplicationAsync(app);

        var handler = new RecordApplicationCommandHandler(_context);
        var command = new RecordApplicationCommand(
            PostingId: posting.Id,
            Action: "append_event",
            Event: new ApplicationEventInputDto(
                EventType: "offer",
                EventDate: "2026-10-18",
                EventSource: "email",
                Notes: "Received initial offer letter with $220k base"),
            OwnerId: ownerId);

        var result = await handler.HandleAsync(command);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal("applied", result.Value!.CurrentOutcome);

        // Assert posting remains applied
        var postingRes = await _postingRepo.GetByIdAsync(ownerId, posting.Id);
        Assert.True(postingRes.IsSuccess);
        Assert.Equal(PostingDisposition.Applied, postingRes.Value!.Disposition);
    }

    [Fact]
    public async Task ApplicationRecord_AppendEvent_Rejected_SynchronizesPostingToRejected()
    {
        var ownerId = "owner-1";
        var company = Company.Create(ownerId, "Rejecting Corp", ownershipType: OwnershipType.Private, status: CompanyStatus.Target);
        await _companyRepo.UpsertCompanyAsync(company);

        var posting = Posting.Create(ownerId, company.Id, "Staff SWE", disposition: PostingDisposition.Applied);
        await _postingRepo.UpsertPostingAsync(posting);

        var app = ApplicationEntity.Create(ownerId, posting.Id, "2026-10-10", ResumeVariant.Tailored);
        await _applicationRepo.UpsertApplicationAsync(app);

        var handler = new RecordApplicationCommandHandler(_context);
        var command = new RecordApplicationCommand(
            PostingId: posting.Id,
            Action: "append_event",
            Event: new ApplicationEventInputDto(
                EventType: "rejected",
                EventDate: "2026-10-18",
                EventSource: "portal",
                Notes: "Team moved forward with internal candidate"),
            OwnerId: ownerId);

        var result = await handler.HandleAsync(command);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal("rejected", result.Value!.CurrentOutcome);

        // Posting in DB synchronized to rejected
        var postingRes = await _postingRepo.GetByIdAsync(ownerId, posting.Id);
        Assert.True(postingRes.IsSuccess);
        Assert.Equal(PostingDisposition.Rejected, postingRes.Value!.Disposition);
        Assert.Equal("Team moved forward with internal candidate", postingRes.Value!.DispositionReason);
        Assert.NotNull(postingRes.Value!.DecidedAt);
    }

    [Theory]
    [InlineData("offer_accepted")]
    [InlineData("offer_declined")]
    [InlineData("withdrawn")]
    public async Task ApplicationRecord_AppendEvent_ClosingEvents_SynchronizePostingToClosed(string eventType)
    {
        var ownerId = "owner-1";
        var company = Company.Create(ownerId, "Closing Corp", ownershipType: OwnershipType.Private, status: CompanyStatus.Target);
        await _companyRepo.UpsertCompanyAsync(company);

        var posting = Posting.Create(ownerId, company.Id, $"Position for {eventType}", disposition: PostingDisposition.Applied);
        await _postingRepo.UpsertPostingAsync(posting);

        var app = ApplicationEntity.Create(ownerId, posting.Id, "2026-10-10", ResumeVariant.Tailored);
        await _applicationRepo.UpsertApplicationAsync(app);

        var handler = new RecordApplicationCommandHandler(_context);
        var command = new RecordApplicationCommand(
            PostingId: posting.Id,
            Action: "append_event",
            Event: new ApplicationEventInputDto(
                EventType: eventType,
                EventDate: "2026-10-18",
                EventSource: "recruiter_call",
                Notes: $"Candidate completed {eventType}"),
            OwnerId: ownerId);

        var result = await handler.HandleAsync(command);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal("closed", result.Value!.CurrentOutcome);

        // Posting in DB synchronized to closed
        var postingRes = await _postingRepo.GetByIdAsync(ownerId, posting.Id);
        Assert.True(postingRes.IsSuccess);
        Assert.Equal(PostingDisposition.Closed, postingRes.Value!.Disposition);
        Assert.Equal($"Candidate completed {eventType}", postingRes.Value!.DispositionReason);
        Assert.NotNull(postingRes.Value!.DecidedAt);
    }

    [Theory]
    [InlineData(PostingDisposition.Rejected)]
    [InlineData(PostingDisposition.Closed)]
    public async Task ApplicationRecord_AppendEvent_OnAlreadyDecidedOrTerminalPosting_AppendsEventWithoutMutatingPosting(PostingDisposition terminalDisp)
    {
        var ownerId = "owner-1";
        var company = Company.Create(ownerId, "Terminal Postings Corp", ownershipType: OwnershipType.Private, status: CompanyStatus.Neutral);
        await _companyRepo.UpsertCompanyAsync(company);

        var originalDecidedAt = DateTimeOffset.UtcNow.AddDays(-2);
        var posting = Posting.Create(
            ownerId: ownerId,
            companyId: company.Id,
            title: "Terminal SWE",
            disposition: terminalDisp,
            dispositionReason: "Original terminal rationale",
            decidedAt: originalDecidedAt);
        await _postingRepo.UpsertPostingAsync(posting);

        var app = ApplicationEntity.Create(ownerId, posting.Id, "2026-10-01", ResumeVariant.Tailored);
        await _applicationRepo.UpsertApplicationAsync(app);

        var handler = new RecordApplicationCommandHandler(_context);
        var command = new RecordApplicationCommand(
            PostingId: posting.Id,
            Action: "append_event",
            Event: new ApplicationEventInputDto(
                EventType: "follow_up",
                EventDate: "2026-10-18",
                EventSource: "email",
                Notes: "Follow-up email received post-closure"),
            OwnerId: ownerId);

        var result = await handler.HandleAsync(command);

        Assert.True(result.IsSuccess, result.Error?.Message);

        // Posting remains in its terminal disposition without mutation
        var postingRes = await _postingRepo.GetByIdAsync(ownerId, posting.Id);
        Assert.True(postingRes.IsSuccess);
        Assert.Equal(terminalDisp, postingRes.Value!.Disposition);
        Assert.Equal("Original terminal rationale", postingRes.Value!.DispositionReason);
    }

    private sealed class TrackingTransactionContextReal : ITrackingTransactionContext
    {
        private readonly IMongoCollection<BsonDocument> _auditCol;

        public TrackingTransactionContextReal(
            ICompanyRepository companyRepo,
            IPostingRepository postingRepo,
            ICriterionRepository criterionRepo,
            IApplicationRepository applicationRepo,
            IMongoDatabase database)
        {
            Companies = companyRepo;
            Postings = postingRepo;
            Criteria = criterionRepo;
            Applications = applicationRepo;
            _auditCol = database.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.AuditLogCollectionName);
        }

        public ICompanyRepository Companies { get; }
        public IPostingRepository Postings { get; }
        public ICriterionRepository Criteria { get; }
        public IApplicationRepository Applications { get; }
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
                ["occurred_at"] = new BsonDateTime(entry.OccurredAtUtc)
            };
            return _auditCol.InsertOneAsync(doc, cancellationToken: cancellationToken);
        }
    }
}
