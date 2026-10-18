using MongoDB.Driver;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Queries;
using RoleRadar.Domain.Audit;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Tracking;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Infrastructure.Mongo.Repositories;
using RoleRadar.Tests.Contract.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Contract;

public sealed class TrackerQueryPostingsContractTests : IClassFixture<SharedMongoFixture>, IAsyncLifetime
{
    private readonly SharedMongoFixture _fixture;
    private readonly string _databaseName;
    private MongoClient _client = null!;
    private IMongoDatabase _database = null!;
    private MongoCompanyRepository _companyRepo = null!;
    private MongoPostingRepository _postingRepo = null!;
    private TrackingTransactionContextReal _context = null!;

    public TrackerQueryPostingsContractTests(SharedMongoFixture fixture)
    {
        _fixture = fixture;
        _databaseName = _fixture.CreateDatabaseName("roleradar_tracker_query");
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
    public async Task TrackerQuery_Postings_ReturnsBoundedPagingAndCalculatesGhostLikelihood()
    {
        var company = Company.Create("owner-1", "Query Target Co");
        await _companyRepo.UpsertCompanyAsync(company);

        var posting = Posting.Create("owner-1", company.Id, "Senior Go Developer", now: DateTimeOffset.UtcNow.AddDays(-14));
        await _postingRepo.UpsertPostingAsync(posting);

        // Append a 'gone' signal to boost ghost likelihood
        var signal = new PostingSignal(
            Id: Guid.CreateVersion7(),
            OwnerId: "owner-1",
            PostingId: posting.Id,
            SignalType: SignalType.Gone,
            Evidence: "URL returned 404",
            ObservedAt: DateTimeOffset.UtcNow);
        await _postingRepo.AppendSignalAsync(signal);

        var clock = new SystemUtcClock();
        var handler = new TrackerQueryHandler(_context, clock);

        var query = new TrackerQueryRequest(
            Entity: "postings",
            PostingId: posting.Id,
            OwnerId: "owner-1");

        var result = await handler.HandleAsync(query);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(1, result.Value.TotalCount);
        Assert.Single(result.Value.Items);

        var item = Assert.IsType<PostingSummaryItemDto>(result.Value.Items[0]);
        Assert.Equal(posting.Id, item.Id);
        Assert.Equal("high", item.GhostLikelihood);
    }

    [Fact]
    public async Task TrackerQuery_DecidedPostings_ReturnsCompactListOrderedByDecidedAtDesc()
    {
        var company = Company.Create("owner-1", "Decided Query Co");
        await _companyRepo.UpsertCompanyAsync(company);

        var p1 = Posting.Create(
            "owner-1", company.Id, "Old Closed Role",
            disposition: PostingDisposition.Closed,
            dispositionReason: "Role cancelled",
            now: DateTimeOffset.UtcNow.AddDays(-5));
        await _postingRepo.UpsertPostingAsync(p1);

        var p2 = Posting.Create(
            "owner-1", company.Id, "Recent Screened Role",
            disposition: PostingDisposition.ScreenedOut,
            dispositionReason: "Bad fit",
            now: DateTimeOffset.UtcNow.AddDays(-1));
        await _postingRepo.UpsertPostingAsync(p2);

        var clock = new SystemUtcClock();
        var handler = new TrackerQueryHandler(_context, clock);

        var query = new TrackerQueryRequest(
            Entity: "decided_postings",
            OwnerId: "owner-1");

        var result = await handler.HandleAsync(query);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(2, result.Value.TotalCount);
        var firstItem = Assert.IsType<DecidedPostingSummaryItemDto>(result.Value.Items[0]);
        Assert.Equal("Recent Screened Role", firstItem.Title);
    }

    [Fact]
    public async Task TrackerQuery_PostingDedupLookup_ReturnsReadOnlySingleMatchWithoutMutation()
    {
        var company = Company.Create("owner-1", "Dedup Lookup Co");
        await _companyRepo.UpsertCompanyAsync(company);

        var posting = Posting.Create(
            "owner-1", company.Id, "Staff Site Reliability Engineer",
            atsTenant: "dedupco",
            requisitionId: "SRE-404");
        await _postingRepo.UpsertPostingAsync(posting);

        var clock = new SystemUtcClock();
        var handler = new TrackerQueryHandler(_context, clock);

        var query = new TrackerQueryRequest(
            Entity: "posting_dedup_lookup",
            DedupKey: "dedupco|SRE-404",
            OwnerId: "owner-1");

        var result = await handler.HandleAsync(query);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(1, result.Value.TotalCount);
        var item = Assert.IsType<PostingSummaryItemDto>(result.Value.Items[0]);
        Assert.Equal(posting.Id, item.Id);
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
}
