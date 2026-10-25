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
            File.WriteAllText(Path.Combine(tempDir, "watchlist-companies.csv"), "company,proposed_status,text,decision,corrected_notes\r\nAcme Corp,target,Note,,\r\n");

            // postings.json: 2 postings with status "applied"
            // Role 1 (Claimed Role) will be claimed by applications.csv and reach Applied
            // Role 2 (Unclaimed Role) will NOT be claimed and must stay Scored
            var postingsJson = """
                {
                    "acme-claimed-role": {
                        "title": "Claimed Role",
                        "company": "Acme Corp",
                        "status": "applied",
                        "href": "https://example.com/job/501",
                        "ghost": false,
                        "compHigh": true
                    },
                    "acme-unclaimed-role": {
                        "title": "Unclaimed Role",
                        "company": "Acme Corp",
                        "status": "applied",
                        "href": "https://example.com/job/502",
                        "ghost": false,
                        "compHigh": false
                    }
                }
                """;
            File.WriteAllText(Path.Combine(tempDir, "postings.json"), postingsJson);
            File.WriteAllText(Path.Combine(tempDir, "appendix-rows.csv"), "source,proposed_disposition,company,title,req,comp_detected,comp_is_estimate,remote_mentioned,details_raw,added_md,reason_primary,reason_all,proposed_resurface,company_also_in_db,review_flags,reason_text,decision,corrected_notes\r\n");

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

    [Fact]
    public async Task ApplicationsImport_RowFailsMidWay_RollsBackEntityAndAudit_AndNextRowStillImports()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"rr_app_tx_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var dbName = $"rrtest_app_tx_{Guid.NewGuid():N}";

        try
        {
            File.WriteAllText(Path.Combine(tempDir, "company-aliases.csv"), "alias,canonical\r\n");
            File.WriteAllText(Path.Combine(tempDir, "watchlist-companies.csv"), "company,proposed_status,text,decision,corrected_notes\r\nAcme Corp,target,Note,,\r\n");

            // postings.json: 3 postings (object keyed by slug)
            var postingsJson = """
                {
                    "acme-role-1": { "title": "Role 1", "company": "Acme Corp", "status": "applied", "href": "https://example.com/job/1" },
                    "acme-role-2": { "title": "Role 2", "company": "Acme Corp", "status": "applied", "href": "https://example.com/job/2" },
                    "acme-role-3": { "title": "Role 3", "company": "Acme Corp", "status": "applied", "href": "https://example.com/job/3" }
                }
                """;
            File.WriteAllText(Path.Combine(tempDir, "postings.json"), postingsJson);
            File.WriteAllText(Path.Combine(tempDir, "appendix-rows.csv"), "source,proposed_disposition,company,title,req,comp_detected,comp_is_estimate,remote_mentioned,details_raw,added_md,reason_primary,reason_all,proposed_resurface,company_also_in_db,review_flags,reason_text,decision,corrected_notes\r\n");

            // applications.csv: 3 rows (Line 2: Role 1, Line 3: Role 2, Line 4: Role 3)
            var appCsv = """
                kind,company,title,req,applied_on,rejected,rejected_date_md,desired_salary,referral,fit,resume_variant,people_detected,review_flags,raw,decision,corrected_notes
                application,Acme Corp,Role 1,,2026-03-10,false,,200000,false,fit,tailored,,,Applied notes,,
                application,Acme Corp,Role 2,,2026-03-11,false,,210000,false,fit,tailored,,,Applied notes,,
                application,Acme Corp,Role 3,,2026-03-12,false,,220000,false,fit,tailored,,,Applied notes,,
                """;
            File.WriteAllText(Path.Combine(tempDir, "applications.csv"), appCsv);

            // Hook injects mid-way failure after application entity write on Line 3 (Role 2)
            var provider = BuildServiceProvider(dbName, faultHook: line =>
            {
                if (line == 3)
                {
                    throw new InvalidOperationException("Forced mid-way failure on Line 3 after entity write");
                }
                return Task.CompletedTask;
            });

            var coordinator = provider.GetRequiredService<ITrackerImportCoordinator>();
            var command = new ImportTrackerCommand(
                BundleDirectory: tempDir,
                DefaultDate: new DateOnly(2026, 10, 24),
                Apply: true);

            var result = await coordinator.ExecuteAsync(command);

            Assert.True(result.IsSuccess);
            var summary = result.Value!;
            Assert.Equal(2, summary.ApplicationsCreated); // Role 1 and Role 3 created; Role 2 failed and was rolled back
            Assert.Single(summary.Errors); // Line 3 error recorded
            Assert.Equal(3, summary.Errors[0].LineNumber);

            var client = new MongoClient(_fixture.HostMongoUri);
            var db = client.GetDatabase(dbName);
            var appsCol = db.GetCollection<MongoDB.Bson.BsonDocument>(TrackingMongoOptions.ApplicationsCollectionName);
            var postingsCol = db.GetCollection<MongoDB.Bson.BsonDocument>(TrackingMongoOptions.PostingsCollectionName);
            var auditCol = db.GetCollection<MongoDB.Bson.BsonDocument>(MongoBootstrapAndIndexes.AuditLogCollectionName);

            var role1Posting = await postingsCol.Find(Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("title", "Role 1")).FirstOrDefaultAsync();
            var role2Posting = await postingsCol.Find(Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("title", "Role 2")).FirstOrDefaultAsync();
            var role3Posting = await postingsCol.Find(Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("title", "Role 3")).FirstOrDefaultAsync();

            Assert.NotNull(role1Posting);
            Assert.NotNull(role2Posting);
            Assert.NotNull(role3Posting);

            // Role 1 and Role 3 transitioned to applied; Role 2 remained scored because the transition and app were rolled back!
            Assert.Equal("applied", role1Posting["disposition"].AsString);
            Assert.Equal("scored", role2Posting["disposition"].AsString);
            Assert.Equal("applied", role3Posting["disposition"].AsString);

            // Application collection: Role 1 and Role 3 exist; Role 2 DOES NOT exist (rolled back!)
            var app1 = await appsCol.Find(Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("posting_id", role1Posting["_id"])).FirstOrDefaultAsync();
            var app2 = await appsCol.Find(Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("posting_id", role2Posting["_id"])).FirstOrDefaultAsync();
            var app3 = await appsCol.Find(Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("posting_id", role3Posting["_id"])).FirstOrDefaultAsync();

            Assert.NotNull(app1);
            Assert.Null(app2); // ZERO entity remaining for failed row!
            Assert.NotNull(app3);

            // Audit log collection: zero application or event audit entries for Role 2
            var app2Audit = await auditCol.Find(Builders<MongoDB.Bson.BsonDocument>.Filter.And(
                Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("entity_type", "application"),
                Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("entity_id", role2Posting["_id"]))).FirstOrDefaultAsync();
            Assert.Null(app2Audit); // ZERO audit entry remaining for failed row!

            var appAudits = await auditCol.Find(Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("entity_type", "application")).ToListAsync();
            Assert.Equal(2, appAudits.Count);

            await client.DropDatabaseAsync(dbName);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    private ServiceProvider BuildServiceProvider(string dbName, Func<int, Task>? faultHook = null)
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

        if (faultHook is not null)
        {
            services.AddTransient<RoleRadar.Application.Tracking.Import.Pipeline.ITrackerImportPipelineFactory>(sp =>
            {
                var csvParser = sp.GetRequiredService<RoleRadar.Application.Abstractions.ICsvParser>();
                return new RoleRadar.Application.Tracking.Import.Pipeline.TrackerImportPipelineFactory(csvParser, faultHook);
            });
        }

        return services.BuildServiceProvider();
    }
}
