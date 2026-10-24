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
public sealed class PostingImportIntegrationTests : IClassFixture<IsolatedContainerMongoFixture>
{
    private readonly IsolatedContainerMongoFixture _fixture;

    public PostingImportIntegrationTests(IsolatedContainerMongoFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PostingImport_StagesAppliedAsScored_FlagsOutOfRangeScore_AndResolvesDuplicates()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"rr_post_integ_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var dbName = $"rrtest_post_{Guid.NewGuid():N}";

        try
        {
            File.WriteAllText(Path.Combine(tempDir, "company-aliases.csv"), "alias,canonical\r\n");
            File.WriteAllText(Path.Combine(tempDir, "watchlist-companies.csv"), "company,tier,source,notes,decision,corrected_notes\r\nAcme Corp,target,recruiter,Leading firm,,\r\n");

            // postings.json with status "applied", compHigh=true, out-of-range score=15.0
            var postingsJson = """
                [
                    {
                        "title": "Principal Architect",
                        "company": "Acme Corp",
                        "tier": "A",
                        "comp": "240k",
                        "compHigh": true,
                        "cultureLevel": "stable",
                        "score": 15.0,
                        "status": "applied",
                        "url": "https://example.com/job/401"
                    }
                ]
                """;
            File.WriteAllText(Path.Combine(tempDir, "postings.json"), postingsJson);

            // appendix-rows.csv with duplicate appearing before non-duplicate
            var appendixCsv = """
                req_id,company,title,remote_mentioned,details_raw,added_md,reason_primary,reason_all,proposed_resurface,company_also_in_db,url,decision,corrected_notes
                REQ-2,Acme Corp,Staff Engineer,true,Details,2026-06-01,duplicate,duplicate,never,false,https://example.com/job/402,,
                REQ-1,Acme Corp,Staff Engineer,true,Details,2026-06-01,no-remote,no-remote,never,false,https://example.com/job/403,,
                """;
            File.WriteAllText(Path.Combine(tempDir, "appendix-rows.csv"), appendixCsv);
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
            Assert.Equal(3, summary.PostingsCreated); // 1 from JSON, 2 from Appendix
            Assert.Equal(1, summary.UnclaimedAppliedPostings); // 1 unclaimed applied role remaining scored
            Assert.True(summary.DatesFlagged > 0); // Out of range score flagged

            // Verify postings in MongoDB
            var client = new MongoClient(_fixture.HostMongoUri);
            var db = client.GetDatabase(dbName);
            var postingsCol = db.GetCollection<MongoDB.Bson.BsonDocument>(TrackingMongoOptions.PostingsCollectionName);

            // 1. Postings from JSON: staged as scored, compHighPriority = true, score = BsonNull (out-of-range not clamped)
            var p1Doc = await postingsCol.Find(Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("original_url", "https://example.com/job/401")).FirstOrDefaultAsync();
            Assert.NotNull(p1Doc);
            Assert.Equal("scored", p1Doc["disposition"].AsString);
            Assert.True(p1Doc["comp_high_priority"].AsBoolean);
            Assert.True(p1Doc["score"].IsBsonNull);

            // 2. Postings from Appendix: REQ-1 (target) and REQ-2 (duplicate)
            var origDoc = await postingsCol.Find(Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("requisition_id", "REQ-1")).FirstOrDefaultAsync();
            var dupDoc = await postingsCol.Find(Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("requisition_id", "REQ-2")).FirstOrDefaultAsync();
            Assert.NotNull(origDoc);
            Assert.NotNull(dupDoc);
            Assert.Equal(origDoc["_id"], dupDoc["duplicate_of_id"]);

            // 3. Verify audit entries: all carry import_* operations and actor cli-import-tracker
            var auditCol = db.GetCollection<MongoDB.Bson.BsonDocument>(MongoBootstrapAndIndexes.AuditLogCollectionName);
            var postingAudits = await auditCol.Find(Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("entity_type", "posting")).ToListAsync();
            Assert.Equal(3, postingAudits.Count);
            Assert.All(postingAudits, a =>
            {
                Assert.Equal("cli-import-tracker", a["actor"].AsString);
                Assert.Equal(AuditOperation.ImportPostingCreate, a["operation"].AsString);
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
