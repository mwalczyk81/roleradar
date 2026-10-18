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

public sealed class PostingSignalContractTests : IClassFixture<SharedMongoFixture>, IAsyncLifetime
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
        public Task AppendAuditEntryAsync(RoleRadar.Domain.Audit.AuditLogEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
