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
public sealed class ApplicationsImportIntegrationTests : IClassFixture<IsolatedContainerMongoFixture>
{
    private readonly IsolatedContainerMongoFixture _fixture;

    public ApplicationsImportIntegrationTests(IsolatedContainerMongoFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ApplicationsImport_TransitionsPosting_CreatesNotesInJobSearch_AndDoesNotAutoCreateContacts()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"rr_app_integ_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var dbName = $"rrtest_app_{Guid.NewGuid():N}";

        try
        {
            File.WriteAllText(Path.Combine(tempDir, "company-aliases.csv"), "alias,canonical\r\n");
            File.WriteAllText(Path.Combine(tempDir, "watchlist-companies.csv"), "company,tier,source,notes,decision,corrected_notes\r\nAcme Corp,target,recruiter,Note,,\r\n");

            // postings.json: 2 postings with status "applied"
            // Role 1 (Claimed Role) will be claimed by applications.csv and reach Applied
            // Role 2 (Unclaimed Role) will NOT be claimed and must stay Scored
            var postingsJson = """
                [
                    {
                        "title": "Claimed Role",
                        "company": "Acme Corp",
                        "status": "applied",
                        "url": "https://example.com/job/501"
                    },
                    {
                        "title": "Unclaimed Role",
                        "company": "Acme Corp",
                        "status": "applied",
                        "url": "https://example.com/job/502"
                    }
                ]
                """;
            File.WriteAllText(Path.Combine(tempDir, "postings.json"), postingsJson);
            File.WriteAllText(Path.Combine(tempDir, "appendix-rows.csv"), "req_id,company,title,remote_mentioned,details_raw,added_md,reason_primary,reason_all,proposed_resurface,company_also_in_db,url,decision,corrected_notes\r\n");

            // applications.csv: 1 application for "Claimed Role" (rejected=true) + 1 standalone_note + people_detected
            var appCsv = """
                kind,company,title,req,applied_on,rejected,rejected_date_md,desired_salary,referral,fit,resume_variant,people_detected,review_flags,raw,decision,corrected_notes
                application,Acme Corp,Claimed Role,,2026-03-10,true,2026-04-01,220000,true,strong_fit,tailored,John Smith (Recruiter),contains_people,Application notes,,
                standalone_note,Acme Corp,Engineering Strategy Memo,,2026-03-12,,,,,,,,,Architecture notes for Q2,,
                """;
            File.WriteAllText(Path.Combine(tempDir, "applications.csv"), appCsv);

            var provider = BuildServiceProvider(dbName);
            var coordinator = provider.GetRequiredService<ITrackerImportCoordinator>();

            var command = new ImportTrackerCommand(
                BundleDirectory: tempDir,
                DefaultDate: new DateOnly(2026, 10, 24),
                Apply: true);

            var result = await coordinator.ExecuteAsync(command);

            Assert.True(result.IsSuccess);
            var summary = result.Value!;
            Assert.Equal(1, summary.ApplicationsCreated);
            Assert.Equal(1, summary.StandaloneNotesCreated);
            Assert.Equal(1, summary.UnclaimedAppliedPostings); // Exactly 1 remains unclaimed
            Assert.Contains("Unclaimed Role", summary.UnclaimedAppliedPostingTitles);
            Assert.Single(summary.PeopleDetected);

            // Verify in MongoDB
            var client = new MongoClient(_fixture.HostMongoUri);
            var db = client.GetDatabase(dbName);

            // 1. Check Postings: Claimed Role -> Rejected (via application rejected event), Unclaimed Role -> Scored
            var postingsCol = db.GetCollection<MongoDB.Bson.BsonDocument>(TrackingMongoOptions.PostingsCollectionName);
            var claimedPosting = await postingsCol.Find(Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("title", "Claimed Role")).FirstOrDefaultAsync();
            var unclaimedPosting = await postingsCol.Find(Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("title", "Unclaimed Role")).FirstOrDefaultAsync();

            Assert.NotNull(claimedPosting);
            Assert.Equal("rejected", claimedPosting["disposition"].AsString);

            Assert.NotNull(unclaimedPosting);
            Assert.Equal("scored", unclaimedPosting["disposition"].AsString); // Stays scored!

            // 2. Check Applications and Events
            var appsCol = db.GetCollection<MongoDB.Bson.BsonDocument>(TrackingMongoOptions.ApplicationsCollectionName);
            var appDoc = await appsCol.Find(Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("posting_id", claimedPosting["_id"])).FirstOrDefaultAsync();
            Assert.NotNull(appDoc);
            Assert.Equal("rejected", appDoc["outcome"].AsString);

            var eventsCol = db.GetCollection<MongoDB.Bson.BsonDocument>(TrackingMongoOptions.ApplicationEventsCollectionName);
            var eventDocs = await eventsCol.Find(Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("application_id", appDoc["_id"])).ToListAsync();
            Assert.Equal(2, eventDocs.Count);

            // 3. Check Notes: standalone note must be in area "job-search"
            var notesCol = db.GetCollection<MongoDB.Bson.BsonDocument>(MongoBootstrapAndIndexes.NotesCollectionName);
            var noteDoc = await notesCol.Find(Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("area", "job-search")).FirstOrDefaultAsync();
            Assert.NotNull(noteDoc);
            Assert.Contains("Architecture notes for Q2", noteDoc["body"].AsString);

            // 4. Check Contacts: ZERO contacts created automatically
            var contactsCol = db.GetCollection<MongoDB.Bson.BsonDocument>(TrackingMongoOptions.ContactsCollectionName);
            var contactCount = await contactsCol.CountDocumentsAsync(Builders<MongoDB.Bson.BsonDocument>.Filter.Empty);
            Assert.Equal(0, contactCount); // STRICT PRIVACY PRINCIPLE: 0 auto-created contacts

            // 5. Check Audit Log:
            // Standalone note reuses import_create with actor cli-import-tracker
            var auditCol = db.GetCollection<MongoDB.Bson.BsonDocument>(MongoBootstrapAndIndexes.AuditLogCollectionName);
            var noteAudit = await auditCol.Find(Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("entity_type", "note")).FirstOrDefaultAsync();
            Assert.NotNull(noteAudit);
            Assert.Equal("cli-import-tracker", noteAudit["actor"].AsString);
            Assert.Equal(AuditOperation.ImportCreate, noteAudit["operation"].AsString);

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
