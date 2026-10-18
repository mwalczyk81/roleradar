using MongoDB.Driver;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Queries;
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
        var handler = new TrackerQueryHandler(
            new MongoTrackingTransactionContextMock(_companyRepo),
            new SystemUtcClock());

        var query = new TrackerQueryRequest(
            Entity: "postings",
            Page: 1,
            PageSize: 50,
            OwnerId: "owner-1");

        var result = await handler.HandleAsync(query);

        // Expected to fail before implementation
        Assert.True(result.IsSuccess);
    }

    private sealed class MongoTrackingTransactionContextMock : ITrackingTransactionContext
    {
        public MongoTrackingTransactionContextMock(ICompanyRepository companyRepo)
        {
            Companies = companyRepo;
        }

        public ICompanyRepository Companies { get; }
        public IPostingRepository Postings => throw new NotImplementedException();
        public ICriterionRepository Criteria => throw new NotImplementedException();
        public IApplicationRepository Applications => throw new NotImplementedException();
        public IContactRepository Contacts => throw new NotImplementedException();
        public ISearchRunRepository SearchRuns => throw new NotImplementedException();
        public Task AppendAuditEntryAsync(RoleRadar.Domain.Audit.AuditLogEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
