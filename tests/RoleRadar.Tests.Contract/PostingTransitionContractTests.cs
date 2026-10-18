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

public sealed class PostingTransitionContractTests : IAsyncLifetime
{
    private readonly SharedMongoFixture _fixture;
    private readonly string _databaseName;
    private MongoClient _client = null!;
    private IMongoDatabase _database = null!;
    private MongoCompanyRepository _companyRepo = null!;
    private MongoPostingRepository _postingRepo = null!;
    private TrackingTransactionContextReal _context = null!;

    public PostingTransitionContractTests(SharedMongoFixture fixture)
    {
        _fixture = fixture;
        _databaseName = _fixture.CreateDatabaseName("roleradar_posting_transition");
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
    public async Task PostingTransition_TargetApplied_DisallowedWithInvalidTransition()
    {
        var company = Company.Create("owner-1", "Transition Target Co");
        await _companyRepo.UpsertCompanyAsync(company);

        var posting = Posting.Create("owner-1", company.Id, "Senior Engineer");
        await _postingRepo.UpsertPostingAsync(posting);

        var handler = new TransitionPostingCommandHandler(_context, new SystemUtcClock());

        var command = new TransitionPostingCommand(
            PostingId: posting.Id,
            TargetDisposition: "applied",
            VersionToken: posting.VersionToken,
            OwnerId: "owner-1");

        var result = await handler.HandleAsync(command);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.InvalidTransition, result.Error!.Value.Code);
        Assert.Contains("applied", result.Error!.Value.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostingTransition_ScreenedOutToLead_EvaluatesStoredResurfacePolicy()
    {
        var company = Company.Create("owner-1", "Resurface Policy Co");
        await _companyRepo.UpsertCompanyAsync(company);

        var clock = new SystemUtcClock();
        var handler = new TransitionPostingCommandHandler(_context, clock);

        // 1. Posting with policy: never
        var postingNever = Posting.Create(
            "owner-1", company.Id, "Never Engineer",
            disposition: PostingDisposition.ScreenedOut,
            resurfacePolicy: ResurfacePolicy.Never,
            dispositionReason: "Never reopen");
        await _postingRepo.UpsertPostingAsync(postingNever);

        var cmdReopenNever = new TransitionPostingCommand(
            PostingId: postingNever.Id,
            TargetDisposition: "lead",
            VersionToken: postingNever.VersionToken,
            OwnerId: "owner-1");
        var resNever = await handler.HandleAsync(cmdReopenNever);

        Assert.False(resNever.IsSuccess);
        Assert.Equal(ErrorCode.InvalidTransition, resNever.Error!.Value.Code);

        // 2. Posting with policy: if_reopened
        var postingIfReopened = Posting.Create(
            "owner-1", company.Id, "Reopenable Engineer",
            disposition: PostingDisposition.ScreenedOut,
            resurfacePolicy: ResurfacePolicy.IfReopened,
            dispositionReason: "Can reopen");
        await _postingRepo.UpsertPostingAsync(postingIfReopened);

        var cmdReopenIf = new TransitionPostingCommand(
            PostingId: postingIfReopened.Id,
            TargetDisposition: "lead",
            VersionToken: postingIfReopened.VersionToken,
            OwnerId: "owner-1");
        var resIf = await handler.HandleAsync(cmdReopenIf);

        Assert.True(resIf.IsSuccess);
        Assert.NotNull(resIf.Value);
        Assert.Equal("lead", resIf.Value.CurrentDisposition);
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
