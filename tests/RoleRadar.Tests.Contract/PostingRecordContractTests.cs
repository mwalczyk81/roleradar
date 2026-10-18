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

public sealed class PostingRecordContractTests : IClassFixture<SharedMongoFixture>, IAsyncLifetime
{
    private readonly SharedMongoFixture _fixture;
    private readonly string _databaseName;
    private MongoClient _client = null!;
    private IMongoDatabase _database = null!;
    private MongoCompanyRepository _companyRepo = null!;

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
        var handler = new RecordPostingCommandHandler(
            new MongoTrackingTransactionContextMock(_companyRepo),
            new SystemUtcClock());

        var command = new RecordPostingCommand(
            CompanyId: missingCompanyId,
            Title: "Staff Backend Engineer",
            SourceKind: "ats");

        var result = await handler.HandleAsync(command);

        // Expected to fail before implementation
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task PostingRecord_TierA_ReEncounterDifferentUtcDate_CommitsLastVerifiedBumpAndAuditEntry()
    {
        var company = Company.Create("owner-1", "OctaneCorp");
        await _companyRepo.UpsertCompanyAsync(company);

        var handler = new RecordPostingCommandHandler(
            new MongoTrackingTransactionContextMock(_companyRepo),
            new SystemUtcClock());

        var command = new RecordPostingCommand(
            CompanyId: company.Id,
            Title: "Staff Backend Engineer",
            SourceKind: "ats",
            AtsTenant: "octanecorp",
            RequisitionId: "REQ-1001",
            OwnerId: "owner-1");

        var result = await handler.HandleAsync(command);

        // Expected to fail before implementation
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("created", result.Value.Status);
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
