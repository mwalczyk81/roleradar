using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using RoleRadar.Application;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Import;
using RoleRadar.Application.Tracking.Import.Commands;
using RoleRadar.Infrastructure;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Tests.Integration.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Integration.Import;

[Collection("ContainerIsolatedCollection")]
public sealed class TrackerImportCliTests : IClassFixture<IsolatedContainerMongoFixture>
{
    private readonly IsolatedContainerMongoFixture _fixture;

    public TrackerImportCliTests(IsolatedContainerMongoFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DryRun_DefaultExecution_LeavesTargetDatabaseUntouched()
    {
        var tempBundleDir = CreateFictionalBundleDirectory();
        try
        {
            var dbName = $"rrtest_dryrun_{Guid.NewGuid():N}";
            var provider = BuildServiceProvider(dbName);
            var coordinator = provider.GetRequiredService<ITrackerImportCoordinator>();

            var command = new ImportTrackerCommand(
                BundleDirectory: tempBundleDir,
                DefaultDate: new DateOnly(2026, 10, 24),
                Apply: false);

            var result = await coordinator.ExecuteAsync(command);

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.True(result.Value!.IsDryRun);

            // Assert target database collections and audit_log are completely untouched (0 documents)
            var client = new MongoClient(_fixture.HostMongoUri);
            var db = client.GetDatabase(dbName);
            var auditCount = await db.GetCollection<MongoDB.Bson.BsonDocument>(RoleRadar.Infrastructure.Mongo.MongoBootstrapAndIndexes.AuditLogCollectionName)
                .CountDocumentsAsync(MongoDB.Driver.Builders<MongoDB.Bson.BsonDocument>.Filter.Empty);
            Assert.Equal(0, auditCount);

            var companiesCount = await db.GetCollection<MongoDB.Bson.BsonDocument>(TrackingMongoOptions.CompaniesCollectionName)
                .CountDocumentsAsync(MongoDB.Driver.Builders<MongoDB.Bson.BsonDocument>.Filter.Empty);
            Assert.Equal(0, companiesCount);

            await client.DropDatabaseAsync(dbName);
        }
        finally
        {
            Directory.Delete(tempBundleDir, true);
        }
    }

    [Fact]
    public async Task Apply_FollowedBySecondApply_ReportsIdempotentNoOp()
    {
        var tempBundleDir = CreateFictionalBundleDirectory();
        try
        {
            var dbName = $"rrtest_apply_{Guid.NewGuid():N}";
            var provider = BuildServiceProvider(dbName);
            var coordinator = provider.GetRequiredService<ITrackerImportCoordinator>();

            var command = new ImportTrackerCommand(
                BundleDirectory: tempBundleDir,
                DefaultDate: new DateOnly(2026, 10, 24),
                Apply: true);

            // 1. First apply
            var firstResult = await coordinator.ExecuteAsync(command);
            Assert.True(firstResult.IsSuccess);
            Assert.NotNull(firstResult.Value);
            Assert.False(firstResult.Value!.IsDryRun);
            Assert.True(firstResult.Value!.CriteriaCreated >= 2); // Seeded criteria

            // 2. Second apply against same bundle
            var secondResult = await coordinator.ExecuteAsync(command);
            Assert.True(secondResult.IsSuccess);
            Assert.NotNull(secondResult.Value);
            Assert.True(secondResult.Value!.IsIdempotentNoOp);
            Assert.Equal(0, secondResult.Value!.CriteriaCreated);
            Assert.Equal(0, secondResult.Value!.CompaniesCreated);
            Assert.Equal(0, secondResult.Value!.PostingsCreated);

            var client = new MongoClient(_fixture.HostMongoUri);
            await client.DropDatabaseAsync(dbName);
        }
        finally
        {
            Directory.Delete(tempBundleDir, true);
        }
    }

    [Fact]
    public async Task MissingDefaultDate_WhenPartialDatesPresent_ReturnsValidationError()
    {
        var tempBundleDir = CreateFictionalBundleDirectory(includePartialDate: true);
        try
        {
            var dbName = $"rrtest_dateerr_{Guid.NewGuid():N}";
            var provider = BuildServiceProvider(dbName);
            var coordinator = provider.GetRequiredService<ITrackerImportCoordinator>();

            var command = new ImportTrackerCommand(
                BundleDirectory: tempBundleDir,
                DefaultDate: null, // Omitted
                Apply: false);

            var result = await coordinator.ExecuteAsync(command);

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCode.ValidationError, result.Error!.Value.Code);
            Assert.Contains("appendix-rows.csv", result.Error!.Value.Message, StringComparison.OrdinalIgnoreCase);

            var client = new MongoClient(_fixture.HostMongoUri);
            await client.DropDatabaseAsync(dbName);
        }
        finally
        {
            Directory.Delete(tempBundleDir, true);
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

    private static string CreateFictionalBundleDirectory(bool includePartialDate = false)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"rr_bundle_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);

        var dateField = includePartialDate ? "10-15" : "2026-10-15";

        File.WriteAllText(Path.Combine(dir, "watchlist-companies.csv"), "company,tier,source,notes,decision,corrected_notes\r\nAcme Corp,target,recruiter,Fictional note,,");
        File.WriteAllText(Path.Combine(dir, "company-aliases.csv"), "alias,canonical\r\nAcme,Acme Corp");
        File.WriteAllText(Path.Combine(dir, "postings.json"), "[]");
        File.WriteAllText(Path.Combine(dir, "appendix-rows.csv"), $"req_id,company,title,remote_mentioned,details_raw,added_md,reason_primary,reason_all,proposed_resurface,company_also_in_db,url,decision,corrected_notes\r\n101,Acme Corp,Software Architect,true,Details,{dateField},no-remote,no-remote,never,false,https://example.com/job/101,,");
        File.WriteAllText(Path.Combine(dir, "applications.csv"), "req_id,company,role,applied_date_md,fit,interview_lead,notes,raw,outcome,status,rejected_date_md,reason_rejected,rejected_stage,contains_people,decision,corrected_notes\r\n");

        return dir;
    }
}
