using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Commands;
using RoleRadar.Application.Tracking.Queries;
using RoleRadar.Domain.Audit;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Tracking;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Infrastructure.Mongo.Repositories;
using RoleRadar.Tests.Contract.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Contract;

public sealed class CompanyRecordContractTests : IClassFixture<SharedMongoFixture>, IAsyncLifetime
{
    private readonly SharedMongoFixture _fixture;
    private readonly string _databaseName;
    private MongoClient _client = null!;
    private IMongoDatabase _database = null!;
    private MongoCompanyRepository _companyRepo = null!;
    private MongoPostingRepository _postingRepo = null!;
    private TrackingTransactionContextReal _context = null!;

    public CompanyRecordContractTests(SharedMongoFixture fixture)
    {
        _fixture = fixture;
        _databaseName = _fixture.CreateDatabaseName("roleradar_company_record");
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
    public async Task CompanyRecord_CreateCompany_WithBoundedDescriptionAndCareerSources_Succeeds()
    {
        var clock = new FixedClock(new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc));
        var handler = new RecordCompanyCommandHandler(_context, clock);

        var command = new RecordCompanyCommand(
            Name: "Acme Software Corp",
            Description: "Enterprise cloud compliance and infrastructure software; VC-backed Series B; HQ in Austin.",
            ParentCompany: "Acme Holdings LLC",
            OwnershipType: "vc_backed",
            Headquarters: "Austin, TX",
            Status: "target",
            CategoryTags: ["cloud", "infrastructure", "security"],
            CareerSources:
            [
                new CareerSourceInputDto(
                    AtsType: "Greenhouse",
                    Tenant: "acmesoftware",
                    Slug: "acme-careers",
                    FetchMethod: "api",
                    Notes: "Greenhouse public API boards.greenhouse.io/v1/boards/acmesoftware/jobs")
            ],
            OwnerId: "owner-1");

        var result = await handler.HandleAsync(command);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("Acme Software Corp", result.Value.Company.Name);
        Assert.Equal("vc_backed", result.Value.Company.OwnershipType);
        Assert.Equal("target", result.Value.Company.Status);
        Assert.Equal(1, result.Value.Company.CareerSourceCount);
        Assert.NotEqual(Guid.Empty, result.Value.Company.Id);
        Assert.NotEqual(Guid.Empty, result.Value.Company.VersionToken);

        // Verify datastore
        var stored = await _companyRepo.GetByIdAsync("owner-1", result.Value.Company.Id);
        Assert.True(stored.IsSuccess);
        Assert.NotNull(stored.Value);
        Assert.Equal("Acme Software Corp", stored.Value.Name);
        Assert.Equal(OwnershipType.VcBacked, stored.Value.OwnershipType);
        Assert.Equal(CompanyStatus.Target, stored.Value.Status);
        Assert.Equal(3, stored.Value.CategoryTags.Count);

        var sources = await _companyRepo.GetCareerSourcesAsync("owner-1", result.Value.Company.Id, includeInactive: true);
        Assert.True(sources.IsSuccess);
        Assert.NotNull(sources.Value);
        Assert.Single(sources.Value);
        Assert.Equal("Greenhouse", sources.Value[0].AtsType);
        Assert.Equal("acmesoftware", sources.Value[0].Tenant);
        Assert.True(sources.Value[0].IsActive);
    }

    [Fact]
    public async Task CompanyRecord_DescriptionExceeding300Chars_ReturnsValidationError()
    {
        var clock = new FixedClock(new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc));
        var handler = new RecordCompanyCommandHandler(_context, clock);

        var command = new RecordCompanyCommand(
            Name: "Oversized Corp",
            Description: new string('x', 301),
            OwnershipType: "private",
            Status: "neutral",
            OwnerId: "owner-1");

        var result = await handler.HandleAsync(command);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, result.Error!.Value.Code);
        Assert.Contains("description", result.Error!.Value.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompanyRecord_UpsertCareerSources_ByIdAndCompositeKey_Succeeds()
    {
        var clock = new FixedClock(new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc));
        var handler = new RecordCompanyCommandHandler(_context, clock);

        // 1. Create company with source 1 (Greenhouse)
        var createCmd = new RecordCompanyCommand(
            Name: "MultiSource Inc",
            OwnershipType: "public",
            Status: "target",
            CareerSources:
            [
                new CareerSourceInputDto(
                    AtsType: "Greenhouse",
                    Tenant: "multisource",
                    Slug: "careers",
                    FetchMethod: "api")
            ],
            OwnerId: "owner-1");

        var createRes = await handler.HandleAsync(createCmd);
        Assert.True(createRes.IsSuccess);
        var companyId = createRes.Value!.Company.Id;
        var versionToken = createRes.Value.Company.VersionToken;

        // 2. Upsert second source by composite key (Workday)
        var upsertCmd = new RecordCompanyCommand(
            Id: companyId,
            Name: "MultiSource Inc",
            OwnershipType: "public",
            Status: "target",
            VersionToken: versionToken,
            CareerSources:
            [
                new CareerSourceInputDto(
                    AtsType: "Workday",
                    Tenant: "multisource_wd",
                    Slug: "external",
                    FetchMethod: "browser",
                    Notes: "Requires JS evaluation")
            ],
            OwnerId: "owner-1");

        var upsertRes = await handler.HandleAsync(upsertCmd);
        Assert.True(upsertRes.IsSuccess);
        Assert.Equal(2, upsertRes.Value!.Company.CareerSourceCount);

        var sources = await _companyRepo.GetCareerSourcesAsync("owner-1", companyId, includeInactive: true);
        Assert.True(sources.IsSuccess);
        Assert.Equal(2, sources.Value!.Count);
        Assert.Contains(sources.Value, s => s.AtsType == "Greenhouse" && s.Tenant == "multisource");
        Assert.Contains(sources.Value, s => s.AtsType == "Workday" && s.Tenant == "multisource_wd");
    }

    [Fact]
    public async Task CompanyRecord_OmissionOfCareerSources_LeavesExistingSourcesUntouched()
    {
        var clock = new FixedClock(new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc));
        var handler = new RecordCompanyCommandHandler(_context, clock);

        // Create company with 2 career sources
        var createCmd = new RecordCompanyCommand(
            Name: "Persistent Sources Corp",
            OwnershipType: "private",
            Status: "target",
            CareerSources:
            [
                new CareerSourceInputDto(AtsType: "Lever", Tenant: "persists", Slug: "jobs"),
                new CareerSourceInputDto(AtsType: "Ashby", Tenant: "persists", Slug: "careers")
            ],
            OwnerId: "owner-1");

        var createRes = await handler.HandleAsync(createCmd);
        Assert.True(createRes.IsSuccess);
        var companyId = createRes.Value!.Company.Id;
        var versionToken = createRes.Value.Company.VersionToken;

        // Update company name with null careerSources (omission)
        var updateCmd = new RecordCompanyCommand(
            Id: companyId,
            Name: "Persistent Sources Corp Renamed",
            OwnershipType: "private",
            Status: "target",
            VersionToken: versionToken,
            CareerSources: null,
            OwnerId: "owner-1");

        var updateRes = await handler.HandleAsync(updateCmd);
        Assert.True(updateRes.IsSuccess);
        Assert.Equal("Persistent Sources Corp Renamed", updateRes.Value!.Company.Name);
        Assert.Equal(2, updateRes.Value.Company.CareerSourceCount);

        // Sources must be intact in the database
        var sources = await _companyRepo.GetCareerSourcesAsync("owner-1", companyId, includeInactive: true);
        Assert.True(sources.IsSuccess);
        Assert.Equal(2, sources.Value!.Count);
    }

    [Fact]
    public async Task CompanyRecord_RetireAndReactivateCareerSource_WithoutHardDelete()
    {
        var clock = new FixedClock(new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc));
        var handler = new RecordCompanyCommandHandler(_context, clock);
        var queryHandler = new TrackerQueryHandler(_context, clock);

        // 1. Create company with 1 career source
        var createCmd = new RecordCompanyCommand(
            Name: "Retireable Sources Corp",
            OwnershipType: "subsidiary",
            Status: "watch",
            CareerSources:
            [
                new CareerSourceInputDto(
                    AtsType: "Custom",
                    Tenant: "retireable",
                    Slug: "portal",
                    FetchMethod: "browser")
            ],
            OwnerId: "owner-1");

        var createRes = await handler.HandleAsync(createCmd);
        Assert.True(createRes.IsSuccess);
        var companyId = createRes.Value!.Company.Id;
        var compVersion = createRes.Value.Company.VersionToken;

        var sourcesBefore = await _companyRepo.GetCareerSourcesAsync("owner-1", companyId, includeInactive: true);
        var sourceId = sourcesBefore.Value![0].Id;
        var sourceVersion = sourcesBefore.Value[0].VersionToken;

        // 2. Retire career source by updating isActive: false
        var retireCmd = new RecordCompanyCommand(
            Id: companyId,
            Name: "Retireable Sources Corp",
            OwnershipType: "subsidiary",
            Status: "watch",
            VersionToken: compVersion,
            CareerSources:
            [
                new CareerSourceInputDto(
                    Id: sourceId,
                    AtsType: "Custom",
                    Tenant: "retireable",
                    Slug: "portal",
                    FetchMethod: "blocked",
                    IsActive: false,
                    VersionToken: sourceVersion)
            ],
            OwnerId: "owner-1");

        var retireRes = await handler.HandleAsync(retireCmd);
        Assert.True(retireRes.IsSuccess);

        // 3. Verify in repository: source still exists (never hard deleted), but is_active == false
        var allSources = await _companyRepo.GetCareerSourcesAsync("owner-1", companyId, includeInactive: true);
        Assert.Single(allSources.Value!);
        Assert.False(allSources.Value![0].IsActive);

        var activeSources = await _companyRepo.GetCareerSourcesAsync("owner-1", companyId, includeInactive: false);
        Assert.Empty(activeSources.Value!);

        // 4. Query via tracker_query
        var queryActive = await queryHandler.HandleAsync(new TrackerQueryRequest(
            Entity: "companies",
            Name: "Retireable",
            IncludeRetired: false,
            OwnerId: "owner-1"));
        Assert.True(queryActive.IsSuccess);
        var compItem = Assert.Single(queryActive.Value!.Items);
        // Retired sources omitted
        var csProperty = compItem.GetType().GetProperty("CareerSources")?.GetValue(compItem) as System.Collections.IEnumerable;
        Assert.NotNull(csProperty);
        Assert.Empty(csProperty);

        var queryWithRetired = await queryHandler.HandleAsync(new TrackerQueryRequest(
            Entity: "companies",
            Name: "Retireable",
            IncludeRetired: true,
            OwnerId: "owner-1"));
        Assert.True(queryWithRetired.IsSuccess);
        var compItemRetired = Assert.Single(queryWithRetired.Value!.Items);
        var csPropertyRetired = compItemRetired.GetType().GetProperty("CareerSources")?.GetValue(compItemRetired) as System.Collections.IEnumerable;
        Assert.NotNull(csPropertyRetired);
        Assert.Single(csPropertyRetired);

        // 5. Reactivate career source
        var reactivateCmd = new RecordCompanyCommand(
            Id: companyId,
            Name: "Retireable Sources Corp",
            OwnershipType: "subsidiary",
            Status: "watch",
            VersionToken: retireRes.Value!.Company.VersionToken,
            CareerSources:
            [
                new CareerSourceInputDto(
                    Id: sourceId,
                    AtsType: "Custom",
                    Tenant: "retireable",
                    Slug: "portal",
                    FetchMethod: "api",
                    IsActive: true)
            ],
            OwnerId: "owner-1");

        var reactivateRes = await handler.HandleAsync(reactivateCmd);
        Assert.True(reactivateRes.IsSuccess);

        var reActiveSources = await _companyRepo.GetCareerSourcesAsync("owner-1", companyId, includeInactive: false);
        Assert.Single(reActiveSources.Value!);
        Assert.True(reActiveSources.Value![0].IsActive);
    }

    [Fact]
    public async Task CompanyRecord_UpdateExistingCompany_StaleVersionToken_ReturnsStaleVersionToken()
    {
        var clock = new FixedClock(new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc));
        var handler = new RecordCompanyCommandHandler(_context, clock);

        var createCmd = new RecordCompanyCommand(
            Name: "Stale Token Corp",
            OwnershipType: "private",
            Status: "neutral",
            OwnerId: "owner-1");

        var createRes = await handler.HandleAsync(createCmd);
        Assert.True(createRes.IsSuccess);
        var companyId = createRes.Value!.Company.Id;

        // Try updating with random stale token
        var updateCmd = new RecordCompanyCommand(
            Id: companyId,
            Name: "Stale Token Corp Updated",
            OwnershipType: "private",
            Status: "target",
            VersionToken: Guid.CreateVersion7(),
            OwnerId: "owner-1");

        var updateRes = await handler.HandleAsync(updateCmd);
        Assert.False(updateRes.IsSuccess);
        Assert.Equal(ErrorCode.StaleVersionToken, updateRes.Error!.Value.Code);
    }

    [Fact]
    public async Task CompanyRecord_UpdateExistingCompany_ValidVersionToken_SucceedsAndRotatesVersionToken()
    {
        var clock = new FixedClock(new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc));
        var handler = new RecordCompanyCommandHandler(_context, clock);

        var createCmd = new RecordCompanyCommand(
            Name: "Rotating Token Corp",
            OwnershipType: "private",
            Status: "neutral",
            CategoryTags: ["initial"],
            OwnerId: "owner-1");

        var createRes = await handler.HandleAsync(createCmd);
        Assert.True(createRes.IsSuccess);
        var companyId = createRes.Value!.Company.Id;
        var initialToken = createRes.Value.Company.VersionToken;

        var updateCmd = new RecordCompanyCommand(
            Id: companyId,
            Name: "Rotating Token Corp",
            Description: "Updated description",
            OwnershipType: "pe_backed",
            Status: "target",
            CategoryTags: ["initial", "updated"],
            VersionToken: initialToken,
            OwnerId: "owner-1");

        var updateRes = await handler.HandleAsync(updateCmd);
        Assert.True(updateRes.IsSuccess);
        Assert.Equal("pe_backed", updateRes.Value!.Company.OwnershipType);
        Assert.Equal("target", updateRes.Value.Company.Status);
        Assert.NotEqual(initialToken, updateRes.Value.Company.VersionToken);

        var stored = await _companyRepo.GetByIdAsync("owner-1", companyId);
        Assert.True(stored.IsSuccess);
        Assert.Equal("Updated description", stored.Value!.Description);
        Assert.Equal(OwnershipType.PeBacked, stored.Value.OwnershipType);
        Assert.Equal(updateRes.Value.Company.VersionToken, stored.Value.VersionToken);
    }

    [Fact]
    public async Task CompanyRecord_ExistingNameWithoutIdOrVersionToken_ReturnsAlreadyExistsWithoutWritesOrAudit()
    {
        var clock = new FixedClock(new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc));
        var handler = new RecordCompanyCommandHandler(_context, clock);

        // 1. Create company
        var createCommand = new RecordCompanyCommand(
            Name: "Stripe, Inc.",
            OwnershipType: "private",
            OwnerId: "owner-1");
        var createResult = await handler.HandleAsync(createCommand);
        Assert.True(createResult.IsSuccess);
        Assert.NotNull(createResult.Value);
        Assert.Equal("created", createResult.Value.Status);

        var initialCompanyCount = await _database.GetCollection<BsonDocument>(TrackingMongoOptions.CompaniesCollectionName)
            .CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);

        // 2. Call with same name (case-insensitive) and NO id or versionToken
        var reencounterCommand = new RecordCompanyCommand(
            Name: "  stripe, inc.  ",
            OwnershipType: "private",
            OwnerId: "owner-1");
        var reencounterResult = await handler.HandleAsync(reencounterCommand);

        Assert.True(reencounterResult.IsSuccess);
        Assert.NotNull(reencounterResult.Value);
        Assert.Equal("already_exists", reencounterResult.Value.Status);
        Assert.Equal(createResult.Value.Company.Id, reencounterResult.Value.Company.Id);

        // 3. Assert zero additional writes
        var finalCompanyCount = await _database.GetCollection<BsonDocument>(TrackingMongoOptions.CompaniesCollectionName)
            .CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);
        Assert.Equal(initialCompanyCount, finalCompanyCount);

        // 4. Calling with explicit MISMATCHED versionToken continues to return StaleVersionToken
        var wrongTokenCommand = new RecordCompanyCommand(
            Id: createResult.Value.Company.Id,
            Name: "Stripe, Inc.",
            OwnershipType: "private",
            VersionToken: Guid.NewGuid(),
            OwnerId: "owner-1");
        var wrongTokenResult = await handler.HandleAsync(wrongTokenCommand);
        Assert.False(wrongTokenResult.IsSuccess);
        Assert.Equal(ErrorCode.StaleVersionToken, wrongTokenResult.Error!.Value.Code);
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
