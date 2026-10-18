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

public sealed class PostingSignalContractTests : IAsyncLifetime
{
    private readonly SharedMongoFixture _fixture;
    private readonly string _databaseName;
    private MongoClient _client = null!;
    private IMongoDatabase _database = null!;
    private MongoCompanyRepository _companyRepo = null!;
    private MongoPostingRepository _postingRepo = null!;
    private TrackingTransactionContextReal _context = null!;

    public PostingSignalContractTests(SharedMongoFixture fixture)
    {
        _fixture = fixture;
        _databaseName = _fixture.CreateDatabaseName("roleradar_signals_contract");
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
    public async Task PostingRecord_WithSignals_AppendsSignalsIncludingDeadlineDate()
    {
        var ownerId = "owner-1";
        var company = Company.Create(ownerId, "Signals Corp");
        await _companyRepo.UpsertCompanyAsync(company);

        var clock = new SystemUtcClock();
        var handler = new RecordPostingCommandHandler(_context, clock);

        var signals = new List<SignalInputDto>
        {
            new(
                SignalType: "gone",
                Evidence: "Posting URL gives 404 Not Found",
                ObservedAt: "2026-10-18T10:00:00Z"),
            new(
                SignalType: "deadline",
                Evidence: "Deadline indicated on posting page",
                DeadlineDate: "2026-10-31",
                ObservedAt: "2026-10-18T10:05:00Z"),
            new(
                SignalType: "repost",
                Evidence: "Observed re-listing of requisition",
                ObservedAt: "2026-10-18T10:10:00Z")
        };

        var command = new RecordPostingCommand(
            CompanyId: company.Id,
            Title: "Staff Cloud Engineer",
            SourceKind: "ats",
            Url: "https://signals.corp.internal/jobs/cloud-101",
            Signals: signals,
            OwnerId: ownerId);

        var result = await handler.HandleAsync(command);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.NotNull(result.Value);
        Assert.Equal("created", result.Value.Status);

        var postingId = result.Value.Posting!.Id;

        // Retrieve persisted signals from repository
        var retrievedSignalsRes = await _postingRepo.GetSignalsAsync(ownerId, postingId);
        Assert.True(retrievedSignalsRes.IsSuccess);
        Assert.NotNull(retrievedSignalsRes.Value);
        Assert.Equal(3, retrievedSignalsRes.Value.Count);

        var deadlineSignal = Assert.Single(retrievedSignalsRes.Value, s => s.SignalType == SignalType.Deadline);
        Assert.Equal("2026-10-31", deadlineSignal.DeadlineDate);
        Assert.Equal("Deadline indicated on posting page", deadlineSignal.Evidence);

        var goneSignal = Assert.Single(retrievedSignalsRes.Value, s => s.SignalType == SignalType.Gone);
        Assert.Equal("Posting URL gives 404 Not Found", goneSignal.Evidence);
        Assert.Null(goneSignal.DeadlineDate);
    }

    [Fact]
    public async Task PostingRecord_WithUnknownSignalType_ReturnsValidationError()
    {
        var ownerId = "owner-1";
        var company = Company.Create(ownerId, "Signals Validation Corp");
        await _companyRepo.UpsertCompanyAsync(company);

        var clock = new SystemUtcClock();
        var handler = new RecordPostingCommandHandler(_context, clock);

        var command = new RecordPostingCommand(
            CompanyId: company.Id,
            Title: "Invalid Signal Lead",
            Signals: new[]
            {
                new SignalInputDto("unknown_type", "Valid evidence text")
            },
            OwnerId: ownerId);

        var result = await handler.HandleAsync(command);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, result.Error?.Code);
        Assert.Contains("signalType", result.Error?.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostingRecord_WithOverLongEvidence_ReturnsValidationError()
    {
        var ownerId = "owner-1";
        var company = Company.Create(ownerId, "Evidence Length Corp");
        await _companyRepo.UpsertCompanyAsync(company);

        var clock = new SystemUtcClock();
        var handler = new RecordPostingCommandHandler(_context, clock);

        var longEvidence = new string('x', 501);
        var command = new RecordPostingCommand(
            CompanyId: company.Id,
            Title: "Long Evidence Lead",
            Signals: new[]
            {
                new SignalInputDto("repost", longEvidence)
            },
            OwnerId: ownerId);

        var result = await handler.HandleAsync(command);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, result.Error?.Code);
        Assert.Contains("evidence", result.Error?.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostingRecord_DedupMatch_AppendsSignalAndAudits_SameDayResubmissionNoOp()
    {
        var ownerId = "owner-1";
        var company = Company.Create(ownerId, "Dedup Signals Corp");
        await _companyRepo.UpsertCompanyAsync(company);

        var now = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
        var clock = new TestClock(now);
        var handler = new RecordPostingCommandHandler(_context, clock);

        // 1. Initial create with one signal
        var initialCreate = await handler.HandleAsync(new RecordPostingCommand(
            CompanyId: company.Id,
            Title: "Backend Lead",
            AtsTenant: "dedupcorp",
            RequisitionId: "req-999",
            Signals: new[]
            {
                new SignalInputDto("repost", "First repost notice", "2026-10-18T10:00:00Z")
            },
            OwnerId: ownerId));

        Assert.True(initialCreate.IsSuccess);
        Assert.Equal("created", initialCreate.Value!.Status);
        var postingId = initialCreate.Value.Posting!.Id;

        var signalsAfterCreate = await _postingRepo.GetSignalsAsync(ownerId, postingId);
        Assert.Single(signalsAfterCreate.Value!);
        Assert.Single(_context.AuditEntries); // posting_create

        // 2. Dedup match on same UTC day with a NEW signal: appends signal and emits audit
        var dedupMatchWithNewSignal = await handler.HandleAsync(new RecordPostingCommand(
            CompanyId: company.Id,
            Title: "Backend Lead",
            AtsTenant: "dedupcorp",
            RequisitionId: "req-999",
            Signals: new[]
            {
                new SignalInputDto("gone", "Now 404", "2026-10-18T13:00:00Z")
            },
            OwnerId: ownerId));

        Assert.True(dedupMatchWithNewSignal.IsSuccess);
        Assert.Equal("already_tracked", dedupMatchWithNewSignal.Value!.Status);

        var signalsAfterDedup = await _postingRepo.GetSignalsAsync(ownerId, postingId);
        Assert.Equal(2, signalsAfterDedup.Value!.Count);
        Assert.Equal(2, _context.AuditEntries.Count); // posting_create + posting_signal audit

        // 3. Same-day identical resubmission (same signalType and same evidence on same day) -> appends nothing, emits no audit
        var resubmissionSameDay = await handler.HandleAsync(new RecordPostingCommand(
            CompanyId: company.Id,
            Title: "Backend Lead",
            AtsTenant: "dedupcorp",
            RequisitionId: "req-999",
            Signals: new[]
            {
                new SignalInputDto("gone", "Now 404", "2026-10-18T13:00:00Z")
            },
            OwnerId: ownerId));

        Assert.True(resubmissionSameDay.IsSuccess);
        Assert.Equal("already_tracked", resubmissionSameDay.Value!.Status);

        var signalsAfterResubmit = await _postingRepo.GetSignalsAsync(ownerId, postingId);
        Assert.Equal(2, signalsAfterResubmit.Value!.Count); // still 2
        Assert.Equal(2, _context.AuditEntries.Count); // still 2
    }

    private sealed class TestClock : IUtcClock
    {
        public TestClock(DateTime now) => UtcNow = now;
        public DateTime UtcNow { get; set; }
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
        public List<Domain.Audit.AuditLogEntry> AuditEntries { get; } = new();
        public Task AppendAuditEntryAsync(Domain.Audit.AuditLogEntry entry, CancellationToken cancellationToken = default)
        {
            AuditEntries.Add(entry);
            return Task.CompletedTask;
        }
    }
}
