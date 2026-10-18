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

public sealed class PostingRecordExplicitUpdateTests : IClassFixture<SharedMongoFixture>, IAsyncLifetime
{
    private readonly SharedMongoFixture _fixture;
    private readonly string _databaseName;
    private MongoClient _client = null!;
    private IMongoDatabase _database = null!;
    private MongoCompanyRepository _companyRepo = null!;
    private MongoPostingRepository _postingRepo = null!;
    private TrackingTransactionContextReal _context = null!;

    public PostingRecordExplicitUpdateTests(SharedMongoFixture fixture)
    {
        _fixture = fixture;
        _databaseName = _fixture.CreateDatabaseName("roleradar_posting_update");
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
    public async Task PostingRecord_ExplicitUpdate_MutatesSuppliedFieldsAndRotatesVersionToken()
    {
        var company = Company.Create("owner-1", "Update Target Co");
        await _companyRepo.UpsertCompanyAsync(company);

        var posting = Posting.Create(
            "owner-1", company.Id, "Original Title",
            location: "Dallas, TX",
            remoteType: RemoteType.Onsite,
            score: 7.0m);
        await _postingRepo.UpsertPostingAsync(posting);

        var originalToken = posting.VersionToken;
        var clock = new SystemUtcClock();
        var handler = new RecordPostingCommandHandler(_context, clock);

        var command = new RecordPostingCommand(
            PostingId: posting.Id,
            VersionToken: originalToken,
            Location: "Austin, TX (Remote)",
            RemoteType: "remote",
            Score: 9.2m,
            OwnerId: "owner-1");

        var result = await handler.HandleAsync(command);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("updated", result.Value.Status);

        var updated = await _postingRepo.GetByIdAsync("owner-1", posting.Id);
        Assert.True(updated.IsSuccess);
        Assert.NotNull(updated.Value);
        Assert.Equal("Austin, TX (Remote)", updated.Value.Location);
        Assert.Equal(RemoteType.Remote, updated.Value.RemoteType);
        Assert.Equal(9.2m, updated.Value.Score);
        Assert.NotEqual(originalToken, updated.Value.VersionToken);
    }

    [Fact]
    public async Task PostingRecord_ExplicitUpdate_StaleVersionToken_ReturnsStaleVersionTokenError()
    {
        var company = Company.Create("owner-1", "Concurrency Co");
        await _companyRepo.UpsertCompanyAsync(company);

        var posting = Posting.Create("owner-1", company.Id, "Original Title");
        await _postingRepo.UpsertPostingAsync(posting);

        var staleToken = Guid.CreateVersion7(); // Different from stored
        var clock = new SystemUtcClock();
        var handler = new RecordPostingCommandHandler(_context, clock);

        var command = new RecordPostingCommand(
            PostingId: posting.Id,
            VersionToken: staleToken,
            Location: "New Location",
            OwnerId: "owner-1");

        var result = await handler.HandleAsync(command);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.StaleVersionToken, result.Error!.Value.Code);
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
