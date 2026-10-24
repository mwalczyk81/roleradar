using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Application;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Commands;
using RoleRadar.Domain.Tracking;
using RoleRadar.Infrastructure;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Tests.Integration.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Integration.Import;

[Collection("ContainerIsolatedCollection")]
public sealed class EntityHandlerParityTests : IClassFixture<IsolatedContainerMongoFixture>
{
    private readonly IsolatedContainerMongoFixture _fixture;

    public EntityHandlerParityTests(IsolatedContainerMongoFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Company_ImportDirectPath_ProducesDocumentEqual_ToCommandHandler()
    {
        var dbName = $"rrtest_parity_comp_{Guid.NewGuid():N}";
        await using var provider = BuildServiceProvider(dbName);
        var client = new MongoClient(_fixture.HostMongoUri);
        var db = client.GetDatabase(dbName);

        try
        {
            var ownerId = "owner-parity-test";
            var companyRepo = provider.GetRequiredService<ICompanyRepository>();
            var companyHandler = provider.GetRequiredService<ICommandHandler<RecordCompanyCommand, CompanyRecordResultDto>>();

            // 1. Create company via interactive command handler
            var handlerCmd = new RecordCompanyCommand(
                Name: "Initech Systems",
                Status: "Watch",
                Description: "Enterprise software provider",
                OwnerId: ownerId,
                Actor: "mcp-stdio");
            var handlerResult = await companyHandler.HandleAsync(handlerCmd);
            Assert.True(handlerResult.IsSuccess);

            // 2. Create company via domain entity + repository upsert (importer path)
            var importCompany = Company.Create(
                ownerId: ownerId,
                name: "Initech Systems Import",
                status: CompanyStatus.Watch,
                description: "Enterprise software provider");
            var importResult = await companyRepo.UpsertCompanyAsync(importCompany);
            Assert.True(importResult.IsSuccess);

            // 3. Compare BsonDocuments
            var compCol = db.GetCollection<BsonDocument>(TrackingMongoOptions.CompaniesCollectionName);
            var handlerDoc = await compCol.Find(Builders<BsonDocument>.Filter.Eq("name", "Initech Systems")).FirstOrDefaultAsync();
            var importDoc = await compCol.Find(Builders<BsonDocument>.Filter.Eq("name", "Initech Systems Import")).FirstOrDefaultAsync();

            Assert.NotNull(handlerDoc);
            Assert.NotNull(importDoc);

            AssertDocumentParity(handlerDoc, importDoc,
                ignoredFields: ["_id", "name", "normalized_name", "version_token", "created_at", "updated_at"]);
        }
        finally
        {
            await client.DropDatabaseAsync(dbName);
        }
    }

    [Fact]
    public async Task Posting_WithReqIdAndUrl_ImportDirectPath_ProducesDocumentEqual_ToCommandHandler()
    {
        var dbNameHandler = $"rrtest_parity_ph_{Guid.NewGuid():N}";
        var dbNameImport = $"rrtest_parity_pi_{Guid.NewGuid():N}";
        await using var providerHandler = BuildServiceProvider(dbNameHandler);
        await using var providerImport = BuildServiceProvider(dbNameImport);
        var client = new MongoClient(_fixture.HostMongoUri);
        var dbHandler = client.GetDatabase(dbNameHandler);
        var dbImport = client.GetDatabase(dbNameImport);

        try
        {
            var ownerId = "owner-parity-test";
            var companyRepoH = providerHandler.GetRequiredService<ICompanyRepository>();
            var postingHandler = providerHandler.GetRequiredService<ICommandHandler<RecordPostingCommand, PostingRecordResultDto>>();

            var companyRepoI = providerImport.GetRequiredService<ICompanyRepository>();
            var postingRepoI = providerImport.GetRequiredService<IPostingRepository>();

            var compH = Company.Create(ownerId, "Soylent Corp", status: CompanyStatus.Neutral);
            await companyRepoH.UpsertCompanyAsync(compH);

            var compI = Company.Create(ownerId, "Soylent Corp", status: CompanyStatus.Neutral, id: compH.Id);
            await companyRepoI.UpsertCompanyAsync(compI);

            var now = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

            // 1. Create via Command Handler
            var handlerCmd = new RecordPostingCommand(
                CompanyId: compH.Id,
                Title: "Principal Cloud Engineer",
                Url: "https://example.com/jobs/901",
                RequisitionId: "REQ-901",
                SourceKind: "Aggregator",
                RemoteType: "Unknown",
                Score: 9.0m,
                CultureLevel: "Moderate",
                CultureSummary: "Fast-paced team environment",
                CompHighPriority: true,
                CompensationText: "$190k - $220k",
                OwnerId: ownerId,
                Actor: "mcp-stdio");
            var handlerResult = await postingHandler.HandleAsync(handlerCmd);
            Assert.True(handlerResult.IsSuccess);

            // 2. Create via Domain Entity (importer path) with identical fields
            var importPosting = Posting.Create(
                ownerId: ownerId,
                companyId: compI.Id,
                title: "Principal Cloud Engineer",
                sourceKind: SourceKind.Aggregator,
                originalUrl: "https://example.com/jobs/901",
                requisitionId: "REQ-901",
                remoteType: RemoteType.Unknown,
                compensation: new Compensation(Text: "$190k - $220k"),
                score: 9.0m,
                cultureLevel: CultureLevel.Moderate,
                cultureSummary: "Fast-paced team environment",
                disposition: PostingDisposition.Lead,
                now: now,
                compHighPriority: true);
            var importResult = await postingRepoI.UpsertPostingAsync(importPosting);
            Assert.True(importResult.IsSuccess);

            // 3. Compare BsonDocuments
            var postColH = dbHandler.GetCollection<BsonDocument>(TrackingMongoOptions.PostingsCollectionName);
            var postColI = dbImport.GetCollection<BsonDocument>(TrackingMongoOptions.PostingsCollectionName);
            var handlerDoc = await postColH.Find(Builders<BsonDocument>.Filter.Eq("title", "Principal Cloud Engineer")).FirstOrDefaultAsync();
            var importDoc = await postColI.Find(Builders<BsonDocument>.Filter.Eq("title", "Principal Cloud Engineer")).FirstOrDefaultAsync();

            Assert.NotNull(handlerDoc);
            Assert.NotNull(importDoc);

            AssertDocumentParity(handlerDoc, importDoc,
                ignoredFields: ["_id", "version_token", "created_at", "updated_at", "first_seen_at", "last_verified_at"]);
        }
        finally
        {
            await client.DropDatabaseAsync(dbNameHandler);
            await client.DropDatabaseAsync(dbNameImport);
        }
    }

    [Fact]
    public async Task Posting_WithoutReqIdAndUrl_ImportDirectPath_ProducesDocumentEqual_ToCommandHandler()
    {
        var dbNameHandler = $"rrtest_parity_pnh_{Guid.NewGuid():N}";
        var dbNameImport = $"rrtest_parity_pni_{Guid.NewGuid():N}";
        await using var providerHandler = BuildServiceProvider(dbNameHandler);
        await using var providerImport = BuildServiceProvider(dbNameImport);
        var client = new MongoClient(_fixture.HostMongoUri);
        var dbHandler = client.GetDatabase(dbNameHandler);
        var dbImport = client.GetDatabase(dbNameImport);

        try
        {
            var ownerId = "owner-parity-test";
            var companyRepoH = providerHandler.GetRequiredService<ICompanyRepository>();
            var postingHandler = providerHandler.GetRequiredService<ICommandHandler<RecordPostingCommand, PostingRecordResultDto>>();

            var companyRepoI = providerImport.GetRequiredService<ICompanyRepository>();
            var postingRepoI = providerImport.GetRequiredService<IPostingRepository>();

            var compH = Company.Create(ownerId, "Massive Dynamic", status: CompanyStatus.Neutral);
            await companyRepoH.UpsertCompanyAsync(compH);

            var compI = Company.Create(ownerId, "Massive Dynamic", status: CompanyStatus.Neutral, id: compH.Id);
            await companyRepoI.UpsertCompanyAsync(compI);

            var now = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

            // 1. Create via Command Handler (no reqId, no url)
            var handlerCmd = new RecordPostingCommand(
                CompanyId: compH.Id,
                Title: "Lead Systems Architect",
                SourceKind: "Aggregator",
                RemoteType: "Unknown",
                Score: 8.0m,
                CultureLevel: "Stable",
                CultureSummary: "Established research division",
                CompHighPriority: false,
                OwnerId: ownerId,
                Actor: "mcp-stdio");
            var handlerResult = await postingHandler.HandleAsync(handlerCmd);
            Assert.True(handlerResult.IsSuccess);

            // 2. Create via Domain Entity (importer path)
            var importPosting = Posting.Create(
                ownerId: ownerId,
                companyId: compI.Id,
                title: "Lead Systems Architect",
                sourceKind: SourceKind.Aggregator,
                originalUrl: null,
                requisitionId: null,
                remoteType: RemoteType.Unknown,
                score: 8.0m,
                cultureLevel: CultureLevel.Stable,
                cultureSummary: "Established research division",
                disposition: PostingDisposition.Lead,
                now: now,
                compHighPriority: false);
            var importResult = await postingRepoI.UpsertPostingAsync(importPosting);
            Assert.True(importResult.IsSuccess);

            // 3. Compare BsonDocuments
            var postColH = dbHandler.GetCollection<BsonDocument>(TrackingMongoOptions.PostingsCollectionName);
            var postColI = dbImport.GetCollection<BsonDocument>(TrackingMongoOptions.PostingsCollectionName);
            var handlerDoc = await postColH.Find(Builders<BsonDocument>.Filter.Eq("title", "Lead Systems Architect")).FirstOrDefaultAsync();
            var importDoc = await postColI.Find(Builders<BsonDocument>.Filter.Eq("title", "Lead Systems Architect")).FirstOrDefaultAsync();

            Assert.NotNull(handlerDoc);
            Assert.NotNull(importDoc);

            AssertDocumentParity(handlerDoc, importDoc,
                ignoredFields: ["_id", "version_token", "created_at", "updated_at", "first_seen_at", "last_verified_at"]);
        }
        finally
        {
            await client.DropDatabaseAsync(dbNameHandler);
            await client.DropDatabaseAsync(dbNameImport);
        }
    }

    [Fact]
    public async Task Application_And_Event_ImportDirectPath_ProducesDocumentEqual_ToCommandHandler()
    {
        var dbNameHandler = $"rrtest_parity_ah_{Guid.NewGuid():N}";
        var dbNameImport = $"rrtest_parity_ai_{Guid.NewGuid():N}";
        await using var providerHandler = BuildServiceProvider(dbNameHandler);
        await using var providerImport = BuildServiceProvider(dbNameImport);
        var client = new MongoClient(_fixture.HostMongoUri);
        var dbHandler = client.GetDatabase(dbNameHandler);
        var dbImport = client.GetDatabase(dbNameImport);

        try
        {
            var ownerId = "owner-parity-test";
            var companyRepoH = providerHandler.GetRequiredService<ICompanyRepository>();
            var postingRepoH = providerHandler.GetRequiredService<IPostingRepository>();
            var appHandler = providerHandler.GetRequiredService<ICommandHandler<RecordApplicationCommand, ApplicationRecordResultDto>>();

            var companyRepoI = providerImport.GetRequiredService<ICompanyRepository>();
            var postingRepoI = providerImport.GetRequiredService<IPostingRepository>();
            var appRepoI = providerImport.GetRequiredService<IApplicationRepository>();

            var compH = Company.Create(ownerId, "Hooli", status: CompanyStatus.Neutral);
            await companyRepoH.UpsertCompanyAsync(compH);
            var compI = Company.Create(ownerId, "Hooli", status: CompanyStatus.Neutral, id: compH.Id);
            await companyRepoI.UpsertCompanyAsync(compI);

            var postH = Posting.Create(ownerId, compH.Id, "Platform Engineer", SourceKind.Aggregator);
            await postingRepoH.UpsertPostingAsync(postH);
            var postI = Posting.Create(ownerId, compI.Id, "Platform Engineer", SourceKind.Aggregator, id: postH.Id);
            await postingRepoI.UpsertPostingAsync(postI);

            var now = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

            // 1. Create Application and Event via Command Handler
            var handlerCmd = new RecordApplicationCommand(
                PostingId: postH.Id,
                Action: "create",
                SubmittedDate: "2026-06-01",
                ResumeVariant: "tailored",
                DesiredSalaryMin: 215000m,
                DesiredSalaryMax: 215000m,
                DesiredSalaryCurrency: "USD",
                Event: new ApplicationEventInputDto("applied", "2026-06-01", Notes: "Submitted directly through company portal"),
                OwnerId: ownerId,
                Actor: "mcp-stdio");
            var handlerResult = await appHandler.HandleAsync(handlerCmd);
            Assert.True(handlerResult.IsSuccess);

            // 2. Create Application and Event via Domain Entities (importer path)
            var importApp = RoleRadar.Domain.Tracking.Application.Create(
                ownerId: ownerId,
                postingId: postI.Id,
                submittedDate: "2026-06-01",
                resumeVariant: ResumeVariant.Tailored,
                desiredSalary: new DesiredSalary(Min: 215000m, Max: 215000m, Currency: "USD"),
                outcome: ApplicationOutcome.Applied,
                outcomeReason: null,
                now: now);
            await appRepoI.UpsertApplicationAsync(importApp);

            var importEvent = ApplicationEvent.Create(
                ownerId: ownerId,
                applicationId: importApp.Id,
                eventDate: "2026-06-01",
                eventType: ApplicationEventType.Applied,
                notes: "Application submitted",
                now: now);
            await appRepoI.AppendEventAsync(importEvent);

            // 3. Compare Application BsonDocuments
            var appsColH = dbHandler.GetCollection<BsonDocument>(TrackingMongoOptions.ApplicationsCollectionName);
            var appsColI = dbImport.GetCollection<BsonDocument>(TrackingMongoOptions.ApplicationsCollectionName);
            var handlerAppDoc = await appsColH.Find(Builders<BsonDocument>.Filter.Eq("posting_id", postH.Id)).FirstOrDefaultAsync();
            var importAppDoc = await appsColI.Find(Builders<BsonDocument>.Filter.Eq("posting_id", postI.Id)).FirstOrDefaultAsync();

            Assert.NotNull(handlerAppDoc);
            Assert.NotNull(importAppDoc);

            AssertDocumentParity(handlerAppDoc, importAppDoc,
                ignoredFields: ["_id", "version_token", "created_at", "updated_at"]);

            // 4. Compare ApplicationEvent BsonDocuments
            var eventsColH = dbHandler.GetCollection<BsonDocument>(TrackingMongoOptions.ApplicationEventsCollectionName);
            var eventsColI = dbImport.GetCollection<BsonDocument>(TrackingMongoOptions.ApplicationEventsCollectionName);
            var handlerEvtDoc = await eventsColH.Find(Builders<BsonDocument>.Filter.Eq("application_id", handlerAppDoc["_id"])).FirstOrDefaultAsync();
            var importEvtDoc = await eventsColI.Find(Builders<BsonDocument>.Filter.Eq("application_id", importAppDoc["_id"])).FirstOrDefaultAsync();

            Assert.NotNull(handlerEvtDoc);
            Assert.NotNull(importEvtDoc);

            AssertDocumentParity(handlerEvtDoc, importEvtDoc,
                ignoredFields: ["_id", "application_id", "created_at", "event_source"]);
        }
        finally
        {
            await client.DropDatabaseAsync(dbNameHandler);
            await client.DropDatabaseAsync(dbNameImport);
        }
    }

    private static void AssertDocumentParity(BsonDocument expected, BsonDocument actual, HashSet<string> ignoredFields)
    {
        var expectedNames = expected.Names.Where(n => !ignoredFields.Contains(n)).ToHashSet();
        var actualNames = actual.Names.Where(n => !ignoredFields.Contains(n)).ToHashSet();

        Assert.Equal(expectedNames, actualNames);

        foreach (var name in expectedNames)
        {
            var expectedVal = expected[name];
            var actualVal = actual[name];

            Assert.True(expectedVal.Equals(actualVal),
                $"Field '{name}' mismatch: expected '{expectedVal}' ({expectedVal.BsonType}), got '{actualVal}' ({actualVal.BsonType}).");
        }
    }

    private ServiceProvider BuildServiceProvider(string dbName)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        RoleRadar.Infrastructure.DependencyInjection.AddRoleRadarInfrastructure(services, opts =>
        {
            opts.ConnectionString = _fixture.HostMongoUri;
            opts.DatabaseName = dbName;
        });
        services.AddRoleRadarApplication();

        return services.BuildServiceProvider();
    }
}
