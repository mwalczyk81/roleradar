using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using RoleRadar.Application;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Import;
using RoleRadar.Application.Tracking.Import.Commands;
using RoleRadar.Domain.Audit;
using RoleRadar.Domain.Tracking;
using RoleRadar.Infrastructure;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Tests.Integration.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Integration.Import;

[Collection("ContainerIsolatedCollection")]
public sealed class CompanyImportIntegrationTests : IClassFixture<IsolatedContainerMongoFixture>
{
    private readonly IsolatedContainerMongoFixture _fixture;

    public CompanyImportIntegrationTests(IsolatedContainerMongoFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CompanyImport_AppliesAliases_WatchlistStatuses_AndAutoCreatesNeutralOnly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"rr_comp_integ_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var dbName = $"rrtest_comp_{Guid.NewGuid():N}";

        try
        {
            // 1. Aliases: Acme -> Acme Corporation
            File.WriteAllText(Path.Combine(tempDir, "company-aliases.csv"), "alias,canonical\r\nAcme,Acme Corporation");

            // 2. Watchlist: Acme Corporation (watch), Tyrell (avoid), Initech (skip)
            var watchlistCsv = """
                company,tier,source,notes,decision,corrected_notes
                Acme Corporation,watch,referral,Leading widget manufacturer,,
                Tyrell Corp,avoid,news,Cybernetics turbulence,,
                Initech,watch,direct,Skip this,skip,
                """;
            File.WriteAllText(Path.Combine(tempDir, "watchlist-companies.csv"), watchlistCsv);

            // 3. Postings referencing unlisted company 'Soylent Foods' (must auto-create as Neutral, never Watch)
            var postingsJson = """
                {
                    "soylent-principal-reliability-eng": {
                        "title": "Principal Reliability Engineer",
                        "company": "Soylent Foods",
                        "status": "applied",
                        "href": "https://example.com/job/201"
                    }
                }
                """;
            File.WriteAllText(Path.Combine(tempDir, "postings.json"), postingsJson);
            File.WriteAllText(Path.Combine(tempDir, "appendix-rows.csv"), "req_id,company,title,remote_mentioned,details_raw,added_md,reason_primary,reason_all,proposed_resurface,company_also_in_db,url,decision,corrected_notes\r\n");
            File.WriteAllText(Path.Combine(tempDir, "applications.csv"), "kind,company,title,req,applied_on,rejected,rejected_date_md,desired_salary,referral,fit,resume_variant,people_detected,review_flags,raw,decision,corrected_notes\r\n");

            var provider = BuildServiceProvider(dbName);
            var coordinator = provider.GetRequiredService<ITrackerImportCoordinator>();

            var command = new ImportTrackerCommand(
                BundleDirectory: tempDir,
                DefaultDate: new DateOnly(2026, 10, 24),
                Apply: true);

            var result = await coordinator.ExecuteAsync(command);

            Assert.True(result.IsSuccess);
            var summary = result.Value!;
            Assert.False(summary.IsDryRun);
            Assert.Equal(3, summary.CompaniesCreated); // Acme Corporation, Tyrell Corp, Soylent Foods
            Assert.Equal(1, summary.CompaniesUnchanged); // Initech skipped
            Assert.Contains("Soylent Foods", summary.AutoCreatedCompanies);

            // Verify in MongoDB
            var client = new MongoClient(_fixture.HostMongoUri);
            var db = client.GetDatabase(dbName);
            var companiesCol = db.GetCollection<MongoDB.Bson.BsonDocument>(TrackingMongoOptions.CompaniesCollectionName);

            // Acme Corporation -> Watch
            var acmeDoc = await companiesCol.Find(Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("name", "Acme Corporation")).FirstOrDefaultAsync();
            Assert.NotNull(acmeDoc);
            Assert.Equal("watch", acmeDoc["status"].AsString);

            // Tyrell Corp -> Avoid
            var tyrellDoc = await companiesCol.Find(Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("name", "Tyrell Corp")).FirstOrDefaultAsync();
            Assert.NotNull(tyrellDoc);
            Assert.Equal("avoid", tyrellDoc["status"].AsString);

            // Soylent Foods -> Neutral (never Watch)
            var soylentDoc = await companiesCol.Find(Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("name", "Soylent Foods")).FirstOrDefaultAsync();
            Assert.NotNull(soylentDoc);
            Assert.Equal("neutral", soylentDoc["status"].AsString);

            // Initech -> Not created
            var initechDoc = await companiesCol.Find(Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("name", "Initech")).FirstOrDefaultAsync();
            Assert.Null(initechDoc);

            // Verify Audit entries
            var auditCol = db.GetCollection<MongoDB.Bson.BsonDocument>(MongoBootstrapAndIndexes.AuditLogCollectionName);
            var companyAuditEntries = await auditCol.Find(Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("entity_type", "company")).ToListAsync();
            Assert.Equal(3, companyAuditEntries.Count);
            Assert.All(companyAuditEntries, a =>
            {
                Assert.Equal("cli-import-tracker", a["actor"].AsString);
                Assert.Equal(AuditOperation.ImportCompanyCreate, a["operation"].AsString);
            });

            await client.DropDatabaseAsync(dbName);
        }
        finally
        {
            Directory.Delete(tempDir, true);
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
