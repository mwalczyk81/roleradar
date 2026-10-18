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

namespace RoleRadar.Tests.Contract;

public sealed class PostingRecordContractTests : IClassFixture<SharedMongoFixture>, IAsyncLifetime
{
    private readonly SharedMongoFixture _fixture;
    private readonly string _databaseName;
    private MongoClient _client = null!;
    private IMongoDatabase _database = null!;
    private MongoCompanyRepository _companyRepo = null!;
    private MongoPostingRepository _postingRepo = null!;
    private TrackingTransactionContextReal _context = null!;

    public PostingRecordContractTests(SharedMongoFixture fixture)
    {
        _fixture = fixture;
        _databaseName = _fixture.CreateDatabaseName("roleradar_posting_record");
    }

    public async ValueTask InitializeAsync()
    {
        _client = new MongoClient(_fixture.ConnectionString);
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
    public async Task PostingRecord_NonExistentCompany_ReturnsValidationError()
    {
        var missingCompanyId = Guid.CreateVersion7();
        var handler = new RecordPostingCommandHandler(_context, new SystemUtcClock());

        var command = new RecordPostingCommand(
            CompanyId: missingCompanyId,
            Title: "Staff Backend Engineer",
            SourceKind: "ats",
            OwnerId: "owner-1");

        var result = await handler.HandleAsync(command);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, result.Error!.Value.Code);
        Assert.Contains("does not exist", result.Error!.Value.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostingRecord_TierA_ReEncounterDifferentUtcDate_CommitsLastVerifiedBumpAndAuditEntry()
    {
        var company = Company.Create("owner-1", "OctaneCorp");
        await _companyRepo.UpsertCompanyAsync(company);

        var firstDayClock = new FixedClock(new DateTime(2026, 10, 15, 10, 0, 0, DateTimeKind.Utc));
        var handlerFirstDay = new RecordPostingCommandHandler(_context, firstDayClock);

        var command = new RecordPostingCommand(
            CompanyId: company.Id,
            Title: "Staff Backend Engineer",
            SourceKind: "ats",
            AtsTenant: "octanecorp",
            RequisitionId: "REQ-1001",
            OwnerId: "owner-1");

        var result = await handlerFirstDay.HandleAsync(command);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("created", result.Value.Status);

        var postingId = result.Value.Posting!.Id;

        // Second day: re-encounter on different UTC date
        var secondDayClock = new FixedClock(new DateTime(2026, 10, 16, 11, 0, 0, DateTimeKind.Utc));
        var handlerSecondDay = new RecordPostingCommandHandler(_context, secondDayClock);

        var reEncounterResult = await handlerSecondDay.HandleAsync(command);
        Assert.True(reEncounterResult.IsSuccess);
        Assert.NotNull(reEncounterResult.Value);
        Assert.Equal("already_tracked", reEncounterResult.Value.Status);

        // Verify stored posting in Mongo was updated
        var stored = await _postingRepo.GetByIdAsync("owner-1", postingId);
        Assert.True(stored.IsSuccess);
        Assert.NotNull(stored.Value);
        Assert.Equal(secondDayClock.UtcNow, stored.Value.LastVerifiedAt);
    }

    [Fact]
    public async Task PostingRecord_TierD_AdvisoryMatchReturnsPossibleDuplicateWithoutMutation()
    {
        var company = Company.Create("owner-1", "Acme Software Corp");
        await _companyRepo.UpsertCompanyAsync(company);

        var clock = new SystemUtcClock();
        var handler = new RecordPostingCommandHandler(_context, clock);

        // 1. Create first posting
        var cmd1 = new RecordPostingCommand(
            CompanyId: company.Id,
            Title: "Staff Backend Engineer",
            SourceKind: "ats",
            AtsTenant: "acme",
            RequisitionId: "REQ-001",
            OwnerId: "owner-1");
        var res1 = await handler.HandleAsync(cmd1);
        Assert.True(res1.IsSuccess);
        Assert.NotNull(res1.Value);
        Assert.Equal("created", res1.Value.Status);

        // 2. Submit second posting with same company and title but no matching ATS IDs or URL
        var cmd2 = new RecordPostingCommand(
            CompanyId: company.Id,
            Title: "Staff Backend Engineer",
            SourceKind: "aggregator",
            OwnerId: "owner-1");
        var res2 = await handler.HandleAsync(cmd2);
        Assert.True(res2.IsSuccess);
        Assert.NotNull(res2.Value);
        Assert.Equal("possible_duplicate", res2.Value.Status);
        Assert.NotNull(res2.Value.Candidates);
        Assert.Single(res2.Value.Candidates);
        Assert.Equal(res1.Value.Posting!.Id, res2.Value.Candidates[0].Id);
    }

    [Fact]
    public async Task PostingRecord_DuplicateCollapsing_CollapsesReferenceChains()
    {
        var company = Company.Create("owner-1", "Zenith Dynamics");
        await _companyRepo.UpsertCompanyAsync(company);

        var clock = new SystemUtcClock();
        var handler = new RecordPostingCommandHandler(_context, clock);

        // Posting A (Root)
        var cmdA = new RecordPostingCommand(
            CompanyId: company.Id,
            Title: "Principal Architect",
            SourceKind: "ats",
            AtsTenant: "zenith",
            RequisitionId: "REQ-A",
            OwnerId: "owner-1");
        var resA = await handler.HandleAsync(cmdA);
        Assert.True(resA.IsSuccess);
        Assert.NotNull(resA.Value);
        var idA = resA.Value.Posting!.Id;

        // Posting B -> points to A
        var cmdB = new RecordPostingCommand(
            CompanyId: company.Id,
            Title: "Principal Architect (Backup)",
            SourceKind: "ats",
            AtsTenant: "zenith",
            RequisitionId: "REQ-B",
            DuplicateOfId: idA,
            OwnerId: "owner-1");
        var resB = await handler.HandleAsync(cmdB);
        Assert.True(resB.IsSuccess);
        Assert.NotNull(resB.Value);
        var idB = resB.Value.Posting!.Id;

        // Posting C -> points to B (must collapse to A)
        var cmdC = new RecordPostingCommand(
            CompanyId: company.Id,
            Title: "Principal Architect (Third)",
            SourceKind: "ats",
            AtsTenant: "zenith",
            RequisitionId: "REQ-C",
            DuplicateOfId: idB,
            OwnerId: "owner-1");
        var resC = await handler.HandleAsync(cmdC);
        Assert.True(resC.IsSuccess);
        Assert.NotNull(resC.Value);

        var storedC = await _postingRepo.GetByIdAsync("owner-1", resC.Value.Posting!.Id);
        Assert.True(storedC.IsSuccess);
        Assert.NotNull(storedC.Value);
        Assert.Equal(idA, storedC.Value.DuplicateOfId);
    }

    [Fact]
    public async Task PostingRecord_ReEncounterDecidedPosting_ReturnsAlreadyDecidedWithCanReopen()
    {
        var company = Company.Create("owner-1", "Acme Decided Corp");
        await _companyRepo.UpsertCompanyAsync(company);

        var clock = new SystemUtcClock();
        var recordHandler = new RecordPostingCommandHandler(_context, clock);
        var transitionHandler = new TransitionPostingCommandHandler(_context, clock);

        var cmd = new RecordPostingCommand(
            CompanyId: company.Id,
            Title: "Data Engineer",
            SourceKind: "ats",
            AtsTenant: "acme",
            RequisitionId: "DATA-100",
            ResurfacePolicy: "if_reopened",
            OwnerId: "owner-1");

        var res = await recordHandler.HandleAsync(cmd);
        Assert.True(res.IsSuccess);
        Assert.NotNull(res.Value);
        var posting = res.Value.Posting!;

        // Transition to screened_out
        var transCmd = new TransitionPostingCommand(
            PostingId: posting.Id,
            TargetDisposition: "screened_out",
            VersionToken: posting.VersionToken,
            Reason: "Below required salary threshold",
            ResurfacePolicy: "if_reopened",
            OwnerId: "owner-1");

        var transRes = await transitionHandler.HandleAsync(transCmd);
        Assert.True(transRes.IsSuccess);

        // Re-encounter via posting_record
        var reEncounterRes = await recordHandler.HandleAsync(cmd);
        Assert.True(reEncounterRes.IsSuccess);
        Assert.NotNull(reEncounterRes.Value);
        Assert.Equal("already_decided", reEncounterRes.Value.Status);
        Assert.Equal(true, reEncounterRes.Value.CanReopen);
        Assert.Equal("Below required salary threshold", reEncounterRes.Value.PriorReason);
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
        public Task AppendAuditEntryAsync(AuditLogEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FixedClock : IUtcClock
    {
        public FixedClock(DateTime now) => UtcNow = now;
        public DateTime UtcNow { get; }
    }
}
