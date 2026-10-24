using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using RoleRadar.Application;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Import.Pipeline;
using RoleRadar.Domain.Audit;
using RoleRadar.Domain.Tracking;
using RoleRadar.Infrastructure;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Tests.Integration.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Integration.Import;

[Collection("ContainerIsolatedCollection")]
public sealed class CriteriaImportIntegrationTests : IClassFixture<IsolatedContainerMongoFixture>
{
    private readonly IsolatedContainerMongoFixture _fixture;

    public CriteriaImportIntegrationTests(IsolatedContainerMongoFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SeedPredefinedCriteriaAsync_PersistsToMongoAndAppendsAuditEntries()
    {
        var dbName = $"rrtest_criteria_{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        RoleRadar.Infrastructure.DependencyInjection.AddRoleRadarInfrastructure(services, opts =>
        {
            opts.ConnectionString = _fixture.HostMongoUri;
            opts.DatabaseName = dbName;
        });
        services.AddRoleRadarApplication();

        await using var provider = services.BuildServiceProvider();
        var client = new MongoClient(_fixture.HostMongoUri);
        var db = client.GetDatabase(dbName);

        var repo = provider.GetRequiredService<ICriterionRepository>();
        var trackingContext = provider.GetRequiredService<ITrackingTransactionContext>();
        var step = new CriteriaSeedingStep(trackingContext);

        var ownerId = "owner-criteria-test";
        var defaultDate = new DateOnly(2026, 10, 24);

        // 1. Initial run
        var result = await step.SeedPredefinedCriteriaAsync(ownerId, defaultDate);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(2, result.Value!.CreatedCount);

        // Verify criteria in MongoDB
        var floor = await repo.GetActiveByKeyAsync(ownerId, "comp_preferred_floor");
        Assert.NotNull(floor.Value);
        Assert.Equal("200000", floor.Value!.Value);
        Assert.Equal(ProvenanceKind.Stated, floor.Value!.Provenance);

        var bar = await repo.GetActiveByKeyAsync(ownerId, "comp_high_priority_bar");
        Assert.NotNull(bar.Value);
        Assert.Equal("220000", bar.Value!.Value);
        Assert.Equal(ProvenanceKind.Stated, bar.Value!.Provenance);

        // Verify audit log entries in MongoDB
        var auditCol = db.GetCollection<MongoDB.Bson.BsonDocument>(RoleRadar.Infrastructure.Mongo.MongoBootstrapAndIndexes.AuditLogCollectionName);
        var auditEntries = await (await auditCol.FindAsync(MongoDB.Driver.Builders<MongoDB.Bson.BsonDocument>.Filter.Empty)).ToListAsync();
        Assert.Equal(2, auditEntries.Count);
        Assert.All(auditEntries, doc =>
        {
            Assert.Equal("cli-import-tracker", doc["actor"].AsString);
            Assert.Equal(AuditOperation.ImportCriterionRevision, doc["operation"].AsString);
        });

        // 2. Second run is idempotent
        var secondRun = await step.SeedPredefinedCriteriaAsync(ownerId, defaultDate);
        Assert.True(secondRun.IsSuccess);
        Assert.NotNull(secondRun.Value);
        Assert.Equal(0, secondRun.Value!.CreatedCount);

        var finalAuditCount = await auditCol.CountDocumentsAsync(MongoDB.Driver.Builders<MongoDB.Bson.BsonDocument>.Filter.Empty);
        Assert.Equal(2, finalAuditCount);

        // 3. Register reason_primary on first use
        var reasonResult = await step.GetOrCreateReasonCriterionAsync(
            ownerId,
            "Culture mismatch",
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.True(reasonResult.IsSuccess);
        Assert.NotNull(reasonResult.Value);
        Assert.Equal("Culture mismatch", reasonResult.Value!.Key);
        Assert.Equal(ProvenanceKind.Inferred, reasonResult.Value!.Provenance);

        var thirdAuditCount = await auditCol.CountDocumentsAsync(MongoDB.Driver.Builders<MongoDB.Bson.BsonDocument>.Filter.Empty);
        Assert.Equal(3, thirdAuditCount);

        // Clean up test database
        await client.DropDatabaseAsync(dbName);
    }

    [Fact]
    public async Task TrackerImport_BundleWithFullDates_AndNoDefaultDate_SeedsBothCriteria_IdempotentlyOnRerun()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"rr_crit_nodef_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var dbName = $"rrtest_crit_nodef_{Guid.NewGuid():N}";

        try
        {
            File.WriteAllText(Path.Combine(tempDir, "company-aliases.csv"), "alias,canonical\r\n");
            File.WriteAllText(Path.Combine(tempDir, "watchlist-companies.csv"), "company,tier,source,notes,decision,corrected_notes\r\nAcme Corp,target,recruiter,Note,,\r\n");

            // postings.json with explicit full date
            var postingsJson = """
                [
                    { "title": "Senior Engineer", "company": "Acme Corp", "status": "applied", "added": "2026-05-15", "url": "https://example.com/job/1" }
                ]
                """;
            File.WriteAllText(Path.Combine(tempDir, "postings.json"), postingsJson);
            File.WriteAllText(Path.Combine(tempDir, "appendix-rows.csv"), "req_id,company,title,remote_mentioned,details_raw,added_md,reason_primary,reason_all,proposed_resurface,company_also_in_db,url,decision,corrected_notes\r\n");

            // applications.csv with explicit full date
            var appCsv = """
                kind,company,title,req,applied_on,rejected,rejected_date_md,desired_salary,referral,fit,resume_variant,people_detected,review_flags,raw,decision,corrected_notes
                application,Acme Corp,Senior Engineer,,2026-05-15,false,,200000,false,fit,tailored,,,Notes,,
                """;
            File.WriteAllText(Path.Combine(tempDir, "applications.csv"), appCsv);

            var services = new ServiceCollection();
            services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

            RoleRadar.Infrastructure.DependencyInjection.AddRoleRadarInfrastructure(services, opts =>
            {
                opts.ConnectionString = _fixture.HostMongoUri;
                opts.DatabaseName = dbName;
            });
            services.AddRoleRadarApplication();

            await using var provider = services.BuildServiceProvider();
            var coordinator = provider.GetRequiredService<RoleRadar.Application.Tracking.Import.Commands.ITrackerImportCoordinator>();

            // First run: DefaultDate is NULL!
            var command = new RoleRadar.Application.Tracking.Import.Commands.ImportTrackerCommand(
                BundleDirectory: tempDir,
                DefaultDate: null,
                Apply: true);

            var result = await coordinator.ExecuteAsync(command);

            Assert.True(result.IsSuccess);
            Assert.Equal(2, result.Value!.CriteriaCreated); // Both seeded!

            var client = new MongoClient(_fixture.HostMongoUri);
            var db = client.GetDatabase(dbName);
            var criteriaCol = db.GetCollection<MongoDB.Bson.BsonDocument>(TrackingMongoOptions.CriteriaCollectionName);

            var floor = await criteriaCol.Find(Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("key", "comp_preferred_floor")).FirstOrDefaultAsync();
            var bar = await criteriaCol.Find(Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("key", "comp_high_priority_bar")).FirstOrDefaultAsync();

            Assert.NotNull(floor);
            Assert.Equal("200000", floor["value"].AsString);
            Assert.Equal("stated", floor["provenance"].AsString);

            Assert.NotNull(bar);
            Assert.Equal("220000", bar["value"].AsString);
            Assert.Equal("stated", bar["provenance"].AsString);

            // Second run: also DefaultDate is NULL, idempotent no-op!
            var secondResult = await coordinator.ExecuteAsync(command);
            Assert.True(secondResult.IsSuccess);
            Assert.Equal(0, secondResult.Value!.CriteriaCreated);

            var criteriaCount = await criteriaCol.CountDocumentsAsync(Builders<MongoDB.Bson.BsonDocument>.Filter.Empty);
            Assert.Equal(2, criteriaCount);

            await client.DropDatabaseAsync(dbName);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
