using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Application;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Integrity;
using RoleRadar.Application.Tracking.Import;
using RoleRadar.Application.Tracking.Import.Commands;
using RoleRadar.Cli.Commands;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Tracking;
using RoleRadar.Infrastructure;
using RoleRadar.Infrastructure.Backup;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Tests.Integration.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Integration.Backup;

[Collection("ContainerIsolatedCollection")]
public sealed class TrackerImportBackupRestoreTests : IClassFixture<IsolatedContainerMongoFixture>
{
    private readonly IsolatedContainerMongoFixture _fixture;

    public TrackerImportBackupRestoreTests(IsolatedContainerMongoFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task EndToEnd_FictionalBundle_ExercisesAllMappings_Parity_Idempotency_Verify_And_BackupRestore()
    {
        var tempBundleDir = Path.Combine(Path.GetTempPath(), $"rr_e2e_bundle_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempBundleDir);

        var tempBackupFile = Path.Combine(Path.GetTempPath(), $"rr_e2e_backup_{Guid.NewGuid():N}.rrbak");
        const string passphrase = "SuperSecureFictionalBackupPassphrase123!";

        try
        {
            // 1. watchlist-companies.csv (watch and avoid statuses)
            File.WriteAllText(Path.Combine(tempBundleDir, "watchlist-companies.csv"),
                "company,tier,source,notes,decision,corrected_notes\r\n" +
                "Initech Corp,target,recruiter,Core target watchlist company,,\r\n" +
                "Badco Industries,avoid,recruiter,Known poor engineering culture,,\r\n");

            // 2. company-aliases.csv (alias mappings)
            File.WriteAllText(Path.Combine(tempBundleDir, "company-aliases.csv"),
                "alias,canonical\r\n" +
                "Initech,Initech Corp\r\n" +
                "Cyberdyne,Cyberdyne Systems\r\n");

            // 3. postings.json (oversized culture > 500 chars, out-of-range score > 10.0, applied unclaimed, alias resolution)
            var oversizedCulture = new string('A', 525);
            File.WriteAllText(Path.Combine(tempBundleDir, "postings.json"),
                $$"""
                [
                  {
                    "company": "Initech",
                    "title": "Lead Systems Architect",
                    "status": "applied",
                    "req_id": "REQ-101",
                    "url": "https://example.com/job/101",
                    "score": 8.5,
                    "culture": "{{oversizedCulture}}",
                    "comp": "$210,000",
                    "compHigh": true
                  },
                  {
                    "company": "Cyberdyne",
                    "title": "Autonomous Systems Engineer",
                    "status": "applied",
                    "req_id": "REQ-102",
                    "url": "https://example.com/job/102",
                    "score": 14.5,
                    "culture": "Stable robotics research culture"
                  },
                  {
                    "company": "Umbrella Corp",
                    "title": "Bioinformatics Engineer",
                    "status": "lead",
                    "req_id": "REQ-103",
                    "url": "https://example.com/job/103",
                    "score": 7.0
                  }
                ]
                """);

            // 4. appendix-rows.csv (screened_out, date rollback, ~ date prefix, duplicate linking)
            File.WriteAllText(Path.Combine(tempBundleDir, "appendix-rows.csv"),
                "req_id,company,title,remote_mentioned,details_raw,added_md,reason_primary,reason_all,proposed_resurface,company_also_in_db,url,decision,corrected_notes\r\n" +
                "REQ-201,Initech Corp,Platform Engineer,true,Details,10-15,comp-low,comp-low,never,false,https://example.com/job/201,,\r\n" +
                "REQ-202,Initech Corp,Platform Engineer Duplicate,true,duplicate of REQ-201,~10/12,comp-low,comp-low,never,false,https://example.com/job/202,,\r\n");

            // 5. applications.csv (transitions posting to applied, standalone_note, contains_people, parenthetical date)
            File.WriteAllText(Path.Combine(tempBundleDir, "applications.csv"),
                "kind,company,title,req,applied_on,rejected,rejected_date_md,desired_salary,referral,fit,resume_variant,people_detected,review_flags,raw,decision,corrected_notes\r\n" +
                "application,Initech Corp,Lead Systems Architect,REQ-101,2026-10-18,false,,,,,,Alice Recruiter,contains_people,Submitted application,,\r\n" +
                "standalone_note,Initech Corp,General Strategy,,(10/15),,,,,,,,,Strategic platform discussion with team,,\r\n");

            var dbName = $"rrtest_e2e_{Guid.NewGuid():N}";
            var services = BuildServiceProvider(dbName);
            var coordinator = services.GetRequiredService<ITrackerImportCoordinator>();
            var defaultDate = new DateOnly(2026, 10, 24);

            // Phase 4/5/6/7 Validation:
            // Step A: Dry-Run execution
            var dryRunCmd = new ImportTrackerCommand(
                BundleDirectory: tempBundleDir,
                DefaultDate: defaultDate,
                Apply: false);

            var dryRunResult = await coordinator.ExecuteAsync(dryRunCmd);
            Assert.True(dryRunResult.IsSuccess);
            var drySummary = dryRunResult.Value!;
            Assert.True(drySummary.IsDryRun);
            Assert.Equal(1, drySummary.CultureTruncations);
            Assert.Equal(1, drySummary.UnclaimedAppliedPostings);

            // Target DB must be untouched after dry-run
            var client = new MongoClient(_fixture.HostMongoUri);
            var targetDb = client.GetDatabase(dbName);
            var initialPostingCount = await targetDb.GetCollection<BsonDocument>(TrackingMongoOptions.PostingsCollectionName)
                .CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);
            Assert.Equal(0, initialPostingCount);

            // Step B: First --apply execution
            var applyCmd = new ImportTrackerCommand(
                BundleDirectory: tempBundleDir,
                DefaultDate: defaultDate,
                Apply: true);

            var applyResult = await coordinator.ExecuteAsync(applyCmd);
            Assert.True(applyResult.IsSuccess);
            var applySummary = applyResult.Value!;
            Assert.False(applySummary.IsDryRun);

            // Assert dry-run outcome counts equal first apply outcome counts
            Assert.Equal(drySummary.CompaniesCreated, applySummary.CompaniesCreated);
            Assert.Equal(drySummary.PostingsCreated, applySummary.PostingsCreated);
            Assert.Equal(drySummary.ApplicationsCreated, applySummary.ApplicationsCreated);
            Assert.Equal(drySummary.StandaloneNotesCreated, applySummary.StandaloneNotesCreated);
            Assert.Equal(drySummary.CriteriaCreated, applySummary.CriteriaCreated);
            Assert.Equal(drySummary.DatesInferred, applySummary.DatesInferred);
            Assert.Equal(drySummary.CultureTruncations, applySummary.CultureTruncations);
            Assert.Equal(drySummary.UnclaimedAppliedPostings, applySummary.UnclaimedAppliedPostings);

            // Verify all dispositions are represented in MongoDB
            var postingsCol = targetDb.GetCollection<BsonDocument>(TrackingMongoOptions.PostingsCollectionName);
            var postings = await postingsCol.Find(Builders<BsonDocument>.Filter.Empty).ToListAsync();

            Assert.Contains(postings, p => p["disposition"].AsString.Equals("scored", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(postings, p => p["disposition"].AsString.Equals("applied", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(postings, p => p["disposition"].AsString.Equals("screened_out", StringComparison.OrdinalIgnoreCase));

            // Verify out-of-range score stored as null / empty
            var scoreOutOfRangePosting = postings.First(p => p["title"].AsString == "Autonomous Systems Engineer");
            Assert.True(!scoreOutOfRangePosting.Contains("score") || scoreOutOfRangePosting["score"].IsBsonNull);

            Assert.True(applySummary.StandaloneNotesCreated > 0,
                $"StandaloneNotesCreated is {applySummary.StandaloneNotesCreated}. Errors: {string.Join("; ", applySummary.Errors.Select(e => $"{e.SourceFile}:{e.LineNumber} {e.ErrorCode}: {e.Message}"))}");

            // Verify standalone note stored in job-search area
            var notesCol = targetDb.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.NotesCollectionName);
            var notes = await notesCol.Find(Builders<BsonDocument>.Filter.Empty).ToListAsync();
            Assert.Contains(notes, n => n["area"].AsString == "job-search");

            // Step C: Second --apply execution reports zero created/updated (idempotent no-op)
            var secondApplyResult = await coordinator.ExecuteAsync(applyCmd);
            Assert.True(secondApplyResult.IsSuccess);
            var secondSummary = secondApplyResult.Value!;
            Assert.True(secondSummary.IsIdempotentNoOp);
            Assert.Equal(0, secondSummary.CompaniesCreated);
            Assert.Equal(0, secondSummary.PostingsCreated);
            Assert.Equal(0, secondSummary.ApplicationsCreated);
            Assert.Equal(0, secondSummary.StandaloneNotesCreated);
            Assert.Equal(0, secondSummary.CriteriaCreated);

            // Step D: roleradar verify exits 0 (zero integrity anomalies)
            var verifyHandler = services.GetRequiredService<IQueryHandler<VerifyIntegrityQuery, VerifyIntegrityResultDto>>();
            var verifyResult = await verifyHandler.HandleAsync(new VerifyIntegrityQuery("default-owner"));
            Assert.True(verifyResult.IsSuccess);
            Assert.Equal(0, verifyResult.Value!.AnomalyCount);

            // Step E: Backup followed by restore round-trips 100% of imported data
            var snapshotColCounts = new Dictionary<string, long>();
            var allCollections = new[]
            {
                MongoBootstrapAndIndexes.NotesCollectionName,
                MongoBootstrapAndIndexes.NoteRevisionsCollectionName,
                MongoBootstrapAndIndexes.AuditLogCollectionName,
                TrackingMongoOptions.CompaniesCollectionName,
                TrackingMongoOptions.CareerSourcesCollectionName,
                TrackingMongoOptions.PostingsCollectionName,
                TrackingMongoOptions.PostingSignalsCollectionName,
                TrackingMongoOptions.CriteriaCollectionName,
                TrackingMongoOptions.ApplicationsCollectionName,
                TrackingMongoOptions.ApplicationEventsCollectionName,
                TrackingMongoOptions.ContactsCollectionName,
                TrackingMongoOptions.SearchRunsCollectionName
            };

            foreach (var colName in allCollections)
            {
                var count = await targetDb.GetCollection<BsonDocument>(colName).CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);
                snapshotColCounts[colName] = count;
            }

            var backupService = services.GetRequiredService<IBackupArchiveService>();

            // Create encrypted backup archive
            var backupRes = await backupService.CreateBackupAsync(tempBackupFile, passphrase);
            Assert.True(backupRes.IsSuccess);
            Assert.True(File.Exists(tempBackupFile));

            // Restore from backup archive with forceOverwrite
            var restoreRes = await backupService.RestoreBackupAsync(tempBackupFile, passphrase, forceOverwrite: true);
            Assert.True(restoreRes.IsSuccess);

            // Assert 100% document counts match exactly after restore across every collection
            foreach (var colName in allCollections)
            {
                var postRestoreCount = await targetDb.GetCollection<BsonDocument>(colName).CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);
                Assert.Equal(snapshotColCounts[colName], postRestoreCount);
            }

            await client.DropDatabaseAsync(dbName);
        }
        finally
        {
            try { Directory.Delete(tempBundleDir, true); } catch { }
            if (File.Exists(tempBackupFile)) { File.Delete(tempBackupFile); }
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

        services.Configure<BackupOptions>(opts =>
        {
            opts.DumpCommand = $"docker exec -i {_fixture.ContainerName} mongodump --uri=\"mongodb://127.0.0.1:27018/?replicaSet=rs0\" --archive --gzip";
            opts.RestoreCommand = $"docker exec -i {_fixture.ContainerName} mongorestore --uri=\"mongodb://127.0.0.1:27018/?replicaSet=rs0\" --archive --gzip --drop";
        });

        services.AddRoleRadarApplication();

        return services.BuildServiceProvider();
    }
}
