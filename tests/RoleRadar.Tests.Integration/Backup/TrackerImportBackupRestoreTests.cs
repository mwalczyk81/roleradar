using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Application;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Integrity;
using RoleRadar.Application.Notes.Queries;
using RoleRadar.Application.Tracking.Import;
using RoleRadar.Application.Tracking.Import.Commands;
using RoleRadar.Application.Tracking.Queries;
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
                "company,proposed_status,text,decision,corrected_notes\r\n" +
                "Initech Corp,target,Core target watchlist company,,\r\n" +
                "Badco Industries,avoid,Known poor engineering culture,,\r\n");

            // 2. company-aliases.csv (alias mappings)
            File.WriteAllText(Path.Combine(tempBundleDir, "company-aliases.csv"),
                "alias,canonical\r\n" +
                "Initech,Initech Corp\r\n" +
                "Cyberdyne,Cyberdyne Systems\r\n");

            // 3. postings.json (object keyed by slug, full field set: compHigh true/false/missing, ghost tokens/bool, status applied/scored)
            var oversizedCulture = new string('A', 525);
            File.WriteAllText(Path.Combine(tempBundleDir, "postings.json"),
                $$"""
                {
                  "initech-lead-systems-arch": {
                    "added": "2026-03-01",
                    "comp": "$210,000",
                    "compHigh": true,
                    "company": "Initech",
                    "culture": "{{oversizedCulture}}",
                    "cultureLevel": "stable",
                    "first": "2026-02-28",
                    "ghost": "repost nosalary",
                    "href": "https://example.com/job/101",
                    "loc": "Remote",
                    "note": "Screening note",
                    "order": 1,
                    "parent": "Initech Corp Parent",
                    "posted": "2026-02-25",
                    "score": 8.5,
                    "status": "applied",
                    "tier": "tier-1",
                    "title": "Lead Systems Architect"
                  },
                  "cyberdyne-autonomous-systems-eng": {
                    "added": "2026-03-05",
                    "comp": "$230,000",
                    "compHigh": false,
                    "company": "Cyberdyne",
                    "culture": "Stable robotics research culture",
                    "cultureLevel": "moderate",
                    "first": "2026-03-04",
                    "ghost": "parallel deadline:2026-05-25",
                    "href": "https://example.com/job/102",
                    "loc": "Austin, TX",
                    "note": "AI research note",
                    "order": 2,
                    "parent": "Cyberdyne Systems",
                    "posted": "2026-03-04",
                    "score": 14.5,
                    "status": "applied",
                    "tier": "tier-2",
                    "title": "Autonomous Systems Engineer"
                  },
                  "umbrella-bioinformatics-eng": {
                    "added": "2026-03-08",
                    "company": "Umbrella Corp",
                    "culture": "Advanced biological research",
                    "cultureLevel": "stable",
                    "first": "2026-03-07",
                    "ghost": true,
                    "href": "https://example.com/job/103",
                    "loc": "Raccoon City",
                    "note": "Genetics note",
                    "order": 3,
                    "parent": "Umbrella Pharmaceuticals",
                    "posted": "2026-03-06",
                    "score": 7.0,
                    "status": "scored",
                    "tier": "tier-3",
                    "title": "Bioinformatics Engineer"
                  }
                }
                """);

            // 4. appendix-rows.csv (exercises every proposed_disposition: screened_out, duplicate, closed, unscored, dismissed; blank & non-blank resurface)
            File.WriteAllText(Path.Combine(tempBundleDir, "appendix-rows.csv"),
                "source,proposed_disposition,company,title,req,comp_detected,comp_is_estimate,remote_mentioned,details_raw,added_md,reason_primary,reason_all,proposed_resurface,company_also_in_db,review_flags,reason_text,decision,corrected_notes\r\n" +
                "aggregator,screened_out,Initech Corp,Platform Engineer,REQ-201,,,yes,Details,10-15,comp-low,comp-low,never,false,,,,\r\n" +
                "aggregator,duplicate,Initech Corp,Platform Engineer Duplicate,REQ-202,,,yes,duplicate of REQ-201,~10/12,comp-low,comp-low,,false,,,,\r\n" +
                "aggregator,closed,Initech Corp,Closed Platform Engineer,REQ-203,,,yes,Details,10-16,comp-low,comp-low,if_reopened,false,,,,\r\n" +
                "aggregator,unscored,Initech Corp,Unscored Platform Engineer,REQ-204,,,yes,Details,10-17,comp-low,comp-low,,false,,,,\r\n" +
                "aggregator,dismissed,Initech Corp,Dismissed Platform Engineer,REQ-205,,,yes,Details,10-18,comp-low,comp-low,never,false,,,,\r\n");

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
            Assert.Contains("Autonomous Systems Engineer", drySummary.UnclaimedAppliedPostingTitles);

            var postingsRecon = drySummary.FileReconciliations["postings.json"];
            Assert.Equal(3, postingsRecon.Read);
            Assert.Equal(postingsRecon.Read, postingsRecon.Created + postingsRecon.Updated + postingsRecon.Unchanged + postingsRecon.TotalSkipped + postingsRecon.Errored);

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
            Assert.Equal(drySummary.SignalsCreated, applySummary.SignalsCreated);
            Assert.Equal(5, applySummary.SignalsCreated); // Repost, NoSalary, ParallelPosting, Deadline, Phantom
            Assert.Equal(drySummary.DatesInferred, applySummary.DatesInferred);
            Assert.Equal(drySummary.CultureTruncations, applySummary.CultureTruncations);
            Assert.Equal(drySummary.UnclaimedAppliedPostings, applySummary.UnclaimedAppliedPostings);

            // Verify all 5 dispositions are represented in MongoDB
            var postingsCol = targetDb.GetCollection<BsonDocument>(TrackingMongoOptions.PostingsCollectionName);
            var postings = await postingsCol.Find(Builders<BsonDocument>.Filter.Empty).ToListAsync();

            Assert.Contains(postings, p => p["disposition"].AsString.Equals("scored", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(postings, p => p["disposition"].AsString.Equals("applied", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(postings, p => p["disposition"].AsString.Equals("screened_out", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(postings, p => p["disposition"].AsString.Equals("closed", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(postings, p => p["disposition"].AsString.Equals("unscored", StringComparison.OrdinalIgnoreCase));

            // Verify closed date populated for closed posting and null for others
            var closedPosting = postings.First(p => p.Contains("requisition_id") && p["requisition_id"].IsString && p["requisition_id"].AsString == "REQ-203");
            Assert.True(closedPosting.Contains("closed_date") && !closedPosting["closed_date"].IsBsonNull);
            Assert.Equal("if_reopened", closedPosting["resurface_policy"].AsString);

            var unscoredPosting = postings.First(p => p.Contains("requisition_id") && p["requisition_id"].IsString && p["requisition_id"].AsString == "REQ-204");
            Assert.True(!unscoredPosting.Contains("closed_date") || unscoredPosting["closed_date"].IsBsonNull);
            Assert.Equal("never", unscoredPosting["resurface_policy"].AsString);

            // Verify out-of-range score stored as null / empty
            var scoreOutOfRangePosting = postings.First(p => p["title"].AsString == "Autonomous Systems Engineer");
            Assert.True(!scoreOutOfRangePosting.Contains("score") || scoreOutOfRangePosting["score"].IsBsonNull);

            // Verify signals were appended
            var signalsCol = targetDb.GetCollection<BsonDocument>(TrackingMongoOptions.PostingSignalsCollectionName);
            var signals = await signalsCol.Find(Builders<BsonDocument>.Filter.Empty).ToListAsync();
            Assert.Equal(5, signals.Count);
            Assert.Contains(signals, s => s["signal_type"].AsString.Equals("repost", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(signals, s => s["signal_type"].AsString.Equals("no_salary", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(signals, s => s["signal_type"].AsString.Equals("parallel_posting", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(signals, s => s["signal_type"].AsString.Equals("deadline", StringComparison.OrdinalIgnoreCase) && s["deadline_date"].AsString == "2026-05-25");
            Assert.Contains(signals, s => s["signal_type"].AsString.Equals("phantom", StringComparison.OrdinalIgnoreCase));

            // Verify disposition histogram
            Assert.True(applySummary.DispositionHistogram.ContainsKey("screened_out"));
            Assert.True(applySummary.DispositionHistogram.ContainsKey("closed"));
            Assert.True(applySummary.DispositionHistogram.ContainsKey("unscored"));

            // Requirement E.11: Disposition histogram total equals stored postings
            var histogramTotal = applySummary.DispositionHistogram.Values.Sum();
            Assert.Equal(postings.Count, histogramTotal);

            // Requirement E.11: Application row maps to 1 stored application with matching company & title
            var appsCol = targetDb.GetCollection<BsonDocument>(TrackingMongoOptions.ApplicationsCollectionName);
            var storedApps = await appsCol.Find(Builders<BsonDocument>.Filter.Empty).ToListAsync();
            var appDoc = Assert.Single(storedApps);
            var appPostingDoc = postings.First(p => p["_id"].AsGuid == appDoc["posting_id"].AsGuid);
            var appCompanyDoc = await targetDb.GetCollection<BsonDocument>(TrackingMongoOptions.CompaniesCollectionName)
                .Find(Builders<BsonDocument>.Filter.Eq("_id", appPostingDoc["company_id"].AsGuid)).FirstOrDefaultAsync();
            Assert.NotNull(appCompanyDoc);
            Assert.Equal("Initech Corp", appCompanyDoc["name"].AsString);
            Assert.Equal("Lead Systems Architect", appPostingDoc["title"].AsString);

            // Requirement E.11: Unclaimed applied roles equals applied postings with no application row
            // postings.json has 2 entries with status: "applied". 1 was claimed by applications.csv ("Lead Systems Architect").
            // 1 remains unclaimed ("Autonomous Systems Engineer").
            var expectedUnclaimedTitles = new[] { "Autonomous Systems Engineer" };
            Assert.Equal(expectedUnclaimedTitles.Length, applySummary.UnclaimedAppliedPostings);
            Assert.Equal(expectedUnclaimedTitles, applySummary.UnclaimedAppliedPostingTitles);

            Assert.True(applySummary.StandaloneNotesCreated > 0,
                $"StandaloneNotesCreated is {applySummary.StandaloneNotesCreated}. Errors: {string.Join("; ", applySummary.Errors.Select(e => $"{e.SourceFile}:{e.LineNumber} {e.ErrorCode}: {e.Message}"))}");

            // Verify standalone note and postings screening notes stored in job-search area
            var notesCol = targetDb.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.NotesCollectionName);
            var notes = await notesCol.Find(Builders<BsonDocument>.Filter.Empty).ToListAsync();
            Assert.Equal(4, notes.Count); // 3 screening notes + 1 standalone note
            Assert.All(notes, n => Assert.Equal("job-search", n["area"].AsString));
            Assert.Contains(notes, n => n["title"].AsString == "Initech Corp - General Strategy");
            Assert.Contains(notes, n => n["title"].AsString == "Assessment: Initech Corp - Lead Systems Architect");

            // Read data back through MCP query handler and note_search using MCP host's default owner
            var trackerQueryHandler = services.GetRequiredService<IQueryHandler<TrackerQueryRequest, TrackerQueryResultDto>>();
            var searchNotesHandler = services.GetRequiredService<IQueryHandler<SearchNotesQuery, NoteSearchResponseDto>>();

            // 1. Postings visible through MCP tracker_query
            var postingsQueryRes = await trackerQueryHandler.HandleAsync(new TrackerQueryRequest(Entity: "postings", PageSize: 100));
            Assert.True(postingsQueryRes.IsSuccess);
            Assert.Equal(8, postingsQueryRes.Value!.TotalCount);

            // 2. Companies visible through MCP tracker_query
            var companiesQueryRes = await trackerQueryHandler.HandleAsync(new TrackerQueryRequest(Entity: "companies", PageSize: 100));
            Assert.True(companiesQueryRes.IsSuccess);
            Assert.Equal(4, companiesQueryRes.Value!.TotalCount);

            // 3. Applications visible through MCP tracker_query
            var appsQueryRes = await trackerQueryHandler.HandleAsync(new TrackerQueryRequest(Entity: "applications", PageSize: 100));
            Assert.True(appsQueryRes.IsSuccess);
            Assert.Equal(1, appsQueryRes.Value!.TotalCount);

            // 4. Assessment notes visible through MCP note_search
            var noteSearchRes = await searchNotesHandler.HandleAsync(new SearchNotesQuery(Query: "Assessment", Area: "job-search", Limit: 50));
            Assert.True(noteSearchRes.IsSuccess);
            Assert.Equal(3, noteSearchRes.Value!.Results.Count);

            // Step C: Second --apply execution reports zero created/updated (idempotent no-op)
            var secondApplyResult = await coordinator.ExecuteAsync(applyCmd);
            Assert.True(secondApplyResult.IsSuccess);
            var secondSummary = secondApplyResult.Value!;
            Assert.True(secondSummary.IsIdempotentNoOp);
            Assert.Equal(0, secondSummary.CompaniesCreated);
            Assert.Equal(0, secondSummary.PostingsCreated);
            Assert.Equal(0, secondSummary.ApplicationsCreated);
            Assert.Equal(0, secondSummary.StandaloneNotesCreated);
            Assert.Equal(0, secondSummary.ScreeningNotesCreated);
            Assert.Equal(0, secondSummary.CriteriaCreated);
            Assert.Equal(0, secondSummary.SignalsCreated);

            // Step D: roleradar verify exits 0 (zero integrity anomalies)
            var verifyHandler = services.GetRequiredService<IQueryHandler<VerifyIntegrityQuery, VerifyIntegrityResultDto>>();
            var verifyResult = await verifyHandler.HandleAsync(new VerifyIntegrityQuery());
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
