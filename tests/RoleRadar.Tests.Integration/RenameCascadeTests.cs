namespace RoleRadar.Tests.Integration;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Application;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Commands;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Tracking;
using RoleRadar.Infrastructure;
using RoleRadar.Infrastructure.Mongo;
using Testcontainers.MongoDb;
using Xunit;

public sealed class RenameCascadeTests : IAsyncLifetime
{
    private readonly MongoDbContainer _container = new MongoDbBuilder("mongo:8.0.34")
        .WithEnvironment("GLIBC_TUNABLES", "glibc.pthread.rseq=1")
        .WithReplicaSet("rs0")
        .Build();

    private ServiceProvider _provider = null!;
    private IMongoDatabase _database = null!;
    private const string TestOwnerId = "owner-cascade-test";

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddRoleRadarApplication();
        services.AddRoleRadarInfrastructure(opts =>
        {
            opts.ConnectionString = _container.GetConnectionString();
            opts.DatabaseName = "rename_cascade_test_db";
        });

        _provider = services.BuildServiceProvider();
        var client = new MongoClient(_container.GetConnectionString());
        _database = client.GetDatabase("rename_cascade_test_db");
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task CompanyRename_CascadesToPostings_And_Applications_InSameTransaction()
    {
        using var scope = _provider.CreateScope();
        var companyHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RecordCompanyCommand, CompanyRecordResultDto>>();
        var postingHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RecordPostingCommand, PostingRecordResultDto>>();
        var appHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RecordApplicationCommand, ApplicationRecordResultDto>>();

        // 1. Create company
        var compRes = await companyHandler.HandleAsync(new RecordCompanyCommand(
            Name: "Old Company Name",
            OwnerId: TestOwnerId));
        Assert.True(compRes.IsSuccess, compRes.Error?.Message);
        Assert.NotNull(compRes.Value?.Company);
        var companyId = compRes.Value.Company.Id;
        var versionToken = compRes.Value.Company.VersionToken;

        // 2. Create posting under company
        var postRes = await postingHandler.HandleAsync(new RecordPostingCommand(
            CompanyId: companyId,
            Title: "Backend Engineer",
            OwnerId: TestOwnerId));
        Assert.True(postRes.IsSuccess, postRes.Error?.Message);
        Assert.NotNull(postRes.Value?.Posting);
        var postingId = postRes.Value.Posting.Id;

        // 3. Create application under posting
        var appRes = await appHandler.HandleAsync(new RecordApplicationCommand(
            PostingId: postingId,
            Action: "create",
            SubmittedDate: "2026-11-01",
            ResumeVariant: "tailored",
            OwnerId: TestOwnerId));
        Assert.True(appRes.IsSuccess, appRes.Error?.Message);
        var applicationId = appRes.Value!.ApplicationId;

        // 4. Rename Company
        var renameRes = await companyHandler.HandleAsync(new RecordCompanyCommand(
            Id: companyId,
            VersionToken: versionToken,
            Name: "New Company Name",
            OwnerId: TestOwnerId));
        Assert.True(renameRes.IsSuccess, renameRes.Error?.Message);

        // 5. Verify denormalized company_name updated on postings collection
        var postingsCollection = _database.GetCollection<BsonDocument>(TrackingMongoOptions.PostingsCollectionName);
        var postDoc = await postingsCollection.Find(Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(postingId, GuidRepresentation.Standard))).FirstOrDefaultAsync();
        Assert.NotNull(postDoc);
        Assert.True(postDoc.Contains("company_name"));
        Assert.Equal("New Company Name", postDoc["company_name"].AsString);

        // 6. Verify denormalized company_name updated on applications collection
        var appsCollection = _database.GetCollection<BsonDocument>(TrackingMongoOptions.ApplicationsCollectionName);
        var appDoc = await appsCollection.Find(Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(applicationId, GuidRepresentation.Standard))).FirstOrDefaultAsync();
        Assert.NotNull(appDoc);
        Assert.True(appDoc.Contains("company_name"));
        Assert.Equal("New Company Name", appDoc["company_name"].AsString);
    }

    [Fact]
    public async Task PostingTitleChange_CascadesToApplications_InSameTransaction()
    {
        using var scope = _provider.CreateScope();
        var companyHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RecordCompanyCommand, CompanyRecordResultDto>>();
        var postingHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RecordPostingCommand, PostingRecordResultDto>>();
        var appHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RecordApplicationCommand, ApplicationRecordResultDto>>();

        var compRes = await companyHandler.HandleAsync(new RecordCompanyCommand(
            Name: "Apex Systems",
            OwnerId: TestOwnerId));
        Assert.True(compRes.IsSuccess);
        var companyId = compRes.Value!.Company.Id;

        var postRes = await postingHandler.HandleAsync(new RecordPostingCommand(
            CompanyId: companyId,
            Title: "Initial Job Title",
            OwnerId: TestOwnerId));
        Assert.True(postRes.IsSuccess);
        Assert.NotNull(postRes.Value?.Posting);
        var postingId = postRes.Value.Posting.Id;
        var postingVersionToken = postRes.Value.Posting.VersionToken;

        var appRes = await appHandler.HandleAsync(new RecordApplicationCommand(
            PostingId: postingId,
            Action: "create",
            SubmittedDate: "2026-11-01",
            ResumeVariant: "tailored",
            OwnerId: TestOwnerId));
        Assert.True(appRes.IsSuccess);
        var applicationId = appRes.Value!.ApplicationId;

        var postingRepo = scope.ServiceProvider.GetRequiredService<IPostingRepository>();
        var currentPosting = await postingRepo.GetByIdAsync(TestOwnerId, postingId);
        Assert.True(currentPosting.IsSuccess && currentPosting.Value is not null);
        var currentVersionToken = currentPosting.Value.VersionToken;

        // Update posting title
        var updatePostRes = await postingHandler.HandleAsync(new RecordPostingCommand(
            PostingId: postingId,
            VersionToken: currentVersionToken,
            Title: "Promoted Staff Engineer",
            OwnerId: TestOwnerId));
        Assert.True(updatePostRes.IsSuccess, updatePostRes.Error?.Message);

        // Verify application posting_title updated
        var appsCollection = _database.GetCollection<BsonDocument>(TrackingMongoOptions.ApplicationsCollectionName);
        var appDoc = await appsCollection.Find(Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(applicationId, GuidRepresentation.Standard))).FirstOrDefaultAsync();
        Assert.NotNull(appDoc);
        Assert.True(appDoc.Contains("posting_title"));
        Assert.Equal("Promoted Staff Engineer", appDoc["posting_title"].AsString);
    }

    /// <summary>
    /// Seeds company -> posting -> application through the real handlers so every denormalized field is
    /// populated exactly as production writes it.
    /// </summary>
    private async Task<(Guid CompanyId, Guid PostingId, Guid ApplicationId, Guid CompanyVersionToken)> SeedChainAsync(
        IServiceScope scope, string companyName, string postingTitle)
    {
        var companyHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RecordCompanyCommand, CompanyRecordResultDto>>();
        var postingHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RecordPostingCommand, PostingRecordResultDto>>();
        var appHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RecordApplicationCommand, ApplicationRecordResultDto>>();

        var compRes = await companyHandler.HandleAsync(new RecordCompanyCommand(Name: companyName, OwnerId: TestOwnerId));
        Assert.True(compRes.IsSuccess, compRes.Error?.Message);
        Assert.NotNull(compRes.Value?.Company);

        var postRes = await postingHandler.HandleAsync(new RecordPostingCommand(
            CompanyId: compRes.Value.Company.Id,
            Title: postingTitle,
            OwnerId: TestOwnerId));
        Assert.True(postRes.IsSuccess, postRes.Error?.Message);
        Assert.NotNull(postRes.Value?.Posting);

        var appRes = await appHandler.HandleAsync(new RecordApplicationCommand(
            PostingId: postRes.Value.Posting.Id,
            Action: "create",
            SubmittedDate: "2026-11-01",
            ResumeVariant: "tailored",
            OwnerId: TestOwnerId));
        Assert.True(appRes.IsSuccess, appRes.Error?.Message);

        return (compRes.Value.Company.Id, postRes.Value.Posting.Id, appRes.Value!.ApplicationId, compRes.Value.Company.VersionToken);
    }

    private static FilterDefinition<BsonDocument> ById(Guid id) =>
        Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(id, GuidRepresentation.Standard));

    private async Task<BsonDocument> LoadAsync(string collection, Guid id) =>
        await _database.GetCollection<BsonDocument>(collection).Find(ById(id)).FirstAsync();

    [Fact]
    public async Task RenameCascade_RollsBackAllChanges_IfSubsequentUpdateFails()
    {
        using var scope = _provider.CreateScope();
        var (companyId, postingId, applicationId, _) = await SeedChainAsync(scope, "Rollback Original Co", "Engineer");

        // Sanity check the starting point so "unchanged" below is meaningful.
        Assert.Equal("Rollback Original Co", (await LoadAsync(TrackingMongoOptions.PostingsCollectionName, postingId))["company_name"].AsString);
        var appBefore = await LoadAsync(TrackingMongoOptions.ApplicationsCollectionName, applicationId);
        Assert.Equal("Rollback Original Co", appBefore["company_name"].AsString);
        Assert.Equal("Engineer", appBefore["posting_title"].AsString);

        // Within a transactional scope, if an operation fails, previous updates in that transaction are rolled back.
        // We test this against ITrackingTransactionContext directly by simulating a failure AFTER the rename
        // (and therefore after the cascade UpdateMany calls on postings and applications) has been issued.
        var txContext = scope.ServiceProvider.GetRequiredService<ITrackingTransactionContext>();

        var failureRes = await txContext.ExecuteInTransactionAsync<bool>(async (ctx, ct) =>
        {
            var comp = (await ctx.Companies.GetByIdAsync(TestOwnerId, companyId, ct)).Value!;
            comp.Update(name: "Tentative Co Name", now: DateTimeOffset.UtcNow);
            await ctx.Companies.UpsertCompanyAsync(comp, ct);

            // Simulate failure on subsequent cascade step
            throw new InvalidOperationException("Simulated failure in cascade step");
        });
        Assert.False(failureRes.IsSuccess);

        // The company itself was not renamed...
        Assert.Equal("Rollback Original Co", (await LoadAsync(TrackingMongoOptions.CompaniesCollectionName, companyId))["name"].AsString);

        // ...and neither of the denormalized copies was changed.
        Assert.Equal("Rollback Original Co", (await LoadAsync(TrackingMongoOptions.PostingsCollectionName, postingId))["company_name"].AsString);
        var appAfter = await LoadAsync(TrackingMongoOptions.ApplicationsCollectionName, applicationId);
        Assert.Equal("Rollback Original Co", appAfter["company_name"].AsString);
        Assert.Equal("Engineer", appAfter["posting_title"].AsString);
    }

    [Fact]
    public async Task RenameCascade_SameTransactionWithoutFailure_Commits_BothCollections()
    {
        // Positive control for the rollback test above: the identical transactional rename, without the
        // simulated failure, must commit the new name to postings AND applications.
        using var scope = _provider.CreateScope();
        var (companyId, postingId, applicationId, _) = await SeedChainAsync(scope, "Commit Original Co", "Platform Engineer");

        var txContext = scope.ServiceProvider.GetRequiredService<ITrackingTransactionContext>();
        var res = await txContext.ExecuteInTransactionAsync<bool>(async (ctx, ct) =>
        {
            var comp = (await ctx.Companies.GetByIdAsync(TestOwnerId, companyId, ct)).Value!;
            comp.Update(name: "Committed Co Name", now: DateTimeOffset.UtcNow);
            await ctx.Companies.UpsertCompanyAsync(comp, ct);
            return Result<bool>.Success(true);
        });
        Assert.True(res.IsSuccess, res.Error?.Message);

        Assert.Equal("Committed Co Name", (await LoadAsync(TrackingMongoOptions.CompaniesCollectionName, companyId))["name"].AsString);
        Assert.Equal("Committed Co Name", (await LoadAsync(TrackingMongoOptions.PostingsCollectionName, postingId))["company_name"].AsString);
        var app = await LoadAsync(TrackingMongoOptions.ApplicationsCollectionName, applicationId);
        Assert.Equal("Committed Co Name", app["company_name"].AsString);
        Assert.Equal("Platform Engineer", app["posting_title"].AsString);
    }

    [Fact]
    public async Task CompanyUpsert_WithUnchangedName_DoesNotRewriteOtherDocumentsCompanyName()
    {
        using var scope = _provider.CreateScope();
        var (companyId, postingId, applicationId, _) = await SeedChainAsync(scope, "Steady Co", "Data Engineer");

        // Plant sentinel values directly in the database. A cascade that ran on every upsert would overwrite
        // them with "Steady Co"; a cascade skipped for an unchanged name leaves them alone.
        const string Sentinel = "Sentinel Not Overwritten";
        var set = Builders<BsonDocument>.Update.Set("company_name", Sentinel);
        await _database.GetCollection<BsonDocument>(TrackingMongoOptions.PostingsCollectionName).UpdateOneAsync(ById(postingId), set);
        await _database.GetCollection<BsonDocument>(TrackingMongoOptions.ApplicationsCollectionName).UpdateOneAsync(ById(applicationId), set);

        // Upsert the company again with the SAME name (e.g. only the description changes).
        var companyRepo = scope.ServiceProvider.GetRequiredService<ICompanyRepository>();
        var company = (await companyRepo.GetByIdAsync(TestOwnerId, companyId)).Value!;
        company.Update(description: "Only the description changed", now: DateTimeOffset.UtcNow);
        var upsert = await companyRepo.UpsertCompanyAsync(company);
        Assert.True(upsert.IsSuccess);

        Assert.Equal(Sentinel, (await LoadAsync(TrackingMongoOptions.PostingsCollectionName, postingId))["company_name"].AsString);
        Assert.Equal(Sentinel, (await LoadAsync(TrackingMongoOptions.ApplicationsCollectionName, applicationId))["company_name"].AsString);
    }
}
