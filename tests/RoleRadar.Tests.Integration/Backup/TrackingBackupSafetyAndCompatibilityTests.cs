using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Application;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Import.Commands;
using RoleRadar.Application.Notes.Commands;
using RoleRadar.Cli.Commands;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Tracking;
using RoleRadar.Infrastructure;
using RoleRadar.Infrastructure.Backup;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Tests.Integration.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Integration.Backup;

[Collection("BackupRestoreWipeCollection")]
public sealed class TrackingBackupSafetyAndCompatibilityTests : IClassFixture<BackupIntegrationMongoFixture>
{
    private readonly BackupIntegrationMongoFixture _fixture;
    private readonly string _connectionString;
    private string _databaseName = string.Empty;

    public TrackingBackupSafetyAndCompatibilityTests(BackupIntegrationMongoFixture fixture)
    {
        _fixture = fixture;
        _connectionString = fixture.ConnectionString;
    }

    private async Task ResetDatabasesAsync()
    {
        await _fixture.DropNonSystemDatabasesAsync();
        _databaseName = $"roleradar_tracking_backup_{Guid.CreateVersion7():N}";
    }

    private ServiceProvider BuildServiceProvider(Action<BackupOptions>? configureBackup = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddRoleRadarApplication();
        services.AddRoleRadarInfrastructure(opts =>
        {
            opts.ConnectionString = _connectionString;
            opts.DatabaseName = _databaseName;
            opts.ReadinessTimeout = TimeSpan.FromSeconds(5);
        });

        var mongoUrl = new MongoUrl(_connectionString);
        var authPart = (!string.IsNullOrEmpty(mongoUrl.Username) && !string.IsNullOrEmpty(mongoUrl.Password))
            ? $"{mongoUrl.Username}:{mongoUrl.Password}@"
            : string.Empty;
        var internalUri = $"mongodb://{authPart}localhost:27017/?replicaSet=rs0&authSource=admin";

        services.Configure<BackupOptions>(opts =>
        {
            opts.DumpCommand = $"docker exec -i {_fixture.ContainerId} mongodump --uri=\"{internalUri}\" --archive --gzip --oplog";
            opts.RestoreCommand = $"docker exec -i {_fixture.ContainerId} mongorestore --uri=\"{internalUri}\" --archive --gzip --nsInclude \"{_databaseName}.*\"";
        });

        if (configureBackup is not null)
        {
            services.Configure(configureBackup);
        }

        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData(TrackingMongoOptions.CompaniesCollectionName)]
    [InlineData(TrackingMongoOptions.CareerSourcesCollectionName)]
    [InlineData(TrackingMongoOptions.PostingsCollectionName)]
    [InlineData(TrackingMongoOptions.PostingSignalsCollectionName)]
    [InlineData(TrackingMongoOptions.CriteriaCollectionName)]
    [InlineData(TrackingMongoOptions.ApplicationsCollectionName)]
    [InlineData(TrackingMongoOptions.ApplicationEventsCollectionName)]
    [InlineData(TrackingMongoOptions.ContactsCollectionName)]
    [InlineData(TrackingMongoOptions.SearchRunsCollectionName)]
    public async Task Restore_NonEmptyGuard_RefusesWhenAnyTrackingCollectionContainsDocuments(string collectionName)
    {
        await ResetDatabasesAsync();
        await using var provider = BuildServiceProvider();

        var client = provider.GetRequiredService<IMongoClient>();
        var db = client.GetDatabase(_databaseName);
        var col = db.GetCollection<BsonDocument>(collectionName);

        // Seed a single document into the target collection
        await col.InsertOneAsync(new BsonDocument
        {
            ["_id"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
            ["owner_id"] = "guard-owner",
            ["dummy_field"] = "guard-value"
        });

        var backupService = provider.GetRequiredService<IBackupArchiveService>();

        // Attempt restore without force overwrite
        var result = await backupService.RestoreBackupAsync("non_existent_archive.rrbak", "DummyPassphrase123!", forceOverwrite: false);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, result.Error?.Code);
        Assert.Contains("non-empty", result.Error?.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--force-overwrite", result.Error?.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WipeAndRestore_Summaries_ReportCountsForAll12Collections_AndReensureTrackingIndexes()
    {
        await ResetDatabasesAsync();
        await using var provider = BuildServiceProvider();
        var backupService = provider.GetRequiredService<IBackupArchiveService>();
        var client = provider.GetRequiredService<IMongoClient>();
        var db = client.GetDatabase(_databaseName);

        // 1. Seed at least one document into each of the 12 collections
        var all12Collections = new[]
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

        foreach (var cName in all12Collections)
        {
            var col = db.GetCollection<BsonDocument>(cName);
            await col.InsertOneAsync(new BsonDocument
            {
                ["_id"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
                ["owner_id"] = "test-owner",
                ["created_at"] = new BsonDateTime(DateTime.UtcNow)
            });
        }

        // 2. Execute wipe and inspect DroppedCounts summary
        var wipeOutcome = await backupService.WipeDatabaseAsync(WipeCommand.ConfirmationToken);
        Assert.True(wipeOutcome.IsSuccess, $"Wipe failed: {wipeOutcome.Error?.Message}");
        var wipeSummary = wipeOutcome.Value;
        Assert.NotNull(wipeSummary);

        foreach (var cName in all12Collections)
        {
            Assert.True(wipeSummary.DroppedCounts.ContainsKey(cName), $"Wipe summary missing count for collection '{cName}'.");
            Assert.Equal(1, wipeSummary.DroppedCounts[cName]);
        }

        // Verify tracking indexes are created after wipe
        var companyIndexesAfterWipe = await (await db.GetCollection<BsonDocument>(TrackingMongoOptions.CompaniesCollectionName).Indexes.ListAsync()).ToListAsync();
        Assert.Contains(companyIndexesAfterWipe, idx => idx["name"].AsString == "ux_companies_owner_normalized_name");

        var postingIndexesAfterWipe = await (await db.GetCollection<BsonDocument>(TrackingMongoOptions.PostingsCollectionName).Indexes.ListAsync()).ToListAsync();
        Assert.Contains(postingIndexesAfterWipe, idx => idx["name"].AsString == "ux_postings_owner_dedup_key_a");
    }

    [Fact]
    public async Task Restore_001FormatArchive_Into002Build_Succeeds_InitializesTrackingIndexes_AndDryRunImportUnchanged()
    {
        await ResetDatabasesAsync();
        await using var provider = BuildServiceProvider();

        // 1. Create a pure 001-era dataset (notes, note_revisions, and legacy audit_log without entity_type/entity_id)
        var client = provider.GetRequiredService<IMongoClient>();
        var db = client.GetDatabase(_databaseName);

        var noteId = Guid.NewGuid();
        var noteVersion = Guid.NewGuid();
        var auditId = Guid.NewGuid();

        await db.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.NotesCollectionName).InsertOneAsync(new BsonDocument
        {
            ["_id"] = new BsonBinaryData(noteId, GuidRepresentation.Standard),
            ["owner_id"] = "legacy-owner",
            ["title"] = "Legacy Heading",
            ["body"] = "## Legacy Heading\nLegacy note content written by 001 build without tracking references.\n",
            ["current_body_hash"] = Sha256Hasher.ComputeLowerHex("## Legacy Heading\nLegacy note content written by 001 build without tracking references.\n"),
            ["tags"] = new BsonArray { "legacy", "001" },
            ["area"] = "architecture",
            ["provenance"] = new BsonDocument
            {
                ["kind"] = "stated",
                ["date"] = "2026-09-01"
            },
            ["import_metadata"] = new BsonDocument
            {
                ["source_file_id"] = "legacy-file.md",
                ["heading_path"] = "## Legacy Heading",
                ["occurrence_index"] = 0,
                ["section_order_index"] = 1,
                ["last_imported_body_hash"] = Sha256Hasher.ComputeLowerHex("## Legacy Heading\nLegacy note content written by 001 build without tracking references.\n"),
                ["import_key"] = "legacy-file.md### Legacy Heading#0"
            },
            ["status"] = "active",
            ["version_token"] = new BsonBinaryData(noteVersion, GuidRepresentation.Standard),
            ["revision_count"] = 0,
            ["created_at"] = new BsonDateTime(DateTime.UtcNow),
            ["updated_at"] = new BsonDateTime(DateTime.UtcNow)
        });

        // 001 audit entry: target_note_id present, NO entity_type, NO entity_id
        await db.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.AuditLogCollectionName).InsertOneAsync(new BsonDocument
        {
            ["_id"] = new BsonBinaryData(auditId, GuidRepresentation.Standard),
            ["owner_id"] = "legacy-owner",
            ["actor"] = "legacy-importer",
            ["operation"] = "note_import_create",
            ["target_note_id"] = new BsonBinaryData(noteId, GuidRepresentation.Standard),
            ["resulting_version_token"] = new BsonBinaryData(noteVersion, GuidRepresentation.Standard),
            ["occurred_at"] = new BsonDateTime(DateTime.UtcNow)
        });

        // 2. Backup this 001-shaped database to .rrbak
        var backupService = provider.GetRequiredService<IBackupArchiveService>();
        var archivePath = Path.Combine(Path.GetTempPath(), $"legacy_001_archive_{Guid.NewGuid():N}.rrbak");
        const string passphrase = "LegacyPassword123!";

        try
        {
            var backupResult = await backupService.CreateBackupAsync(archivePath, passphrase);
            Assert.True(backupResult.IsSuccess, $"001 backup failed: {backupResult.Error?.Message}");

            // 3. Wipe database completely
            var wipeResult = await backupService.WipeDatabaseAsync(WipeCommand.ConfirmationToken);
            Assert.True(wipeResult.IsSuccess, $"Wipe failed: {wipeResult.Error?.Message}");

            // 4. Restore the 001-format archive into the 002 build
            var restoreResult = await backupService.RestoreBackupAsync(archivePath, passphrase, forceOverwrite: false);
            Assert.True(restoreResult.IsSuccess, $"Restore failed: {restoreResult.Error?.Message}");

            // Assert restored counts contain all collections (notes: 1, tracking collections: 0)
            Assert.Equal(1, restoreResult.Value!.RestoredCounts[MongoBootstrapAndIndexes.NotesCollectionName]);
            Assert.Equal(1, restoreResult.Value!.RestoredCounts[MongoBootstrapAndIndexes.AuditLogCollectionName]);
            Assert.Equal(0, restoreResult.Value!.RestoredCounts[TrackingMongoOptions.CompaniesCollectionName]);
            Assert.Equal(0, restoreResult.Value!.RestoredCounts[TrackingMongoOptions.PostingsCollectionName]);

            // 5. Verify tracking indexes were automatically initialized after restore
            var companyIndexes = await (await db.GetCollection<BsonDocument>(TrackingMongoOptions.CompaniesCollectionName).Indexes.ListAsync()).ToListAsync();
            Assert.Contains(companyIndexes, idx => idx["name"].AsString == "ux_companies_owner_normalized_name");

            var criteriaIndexes = await (await db.GetCollection<BsonDocument>(TrackingMongoOptions.CriteriaCollectionName).Indexes.ListAsync()).ToListAsync();
            Assert.Contains(criteriaIndexes, idx => idx["name"].AsString == "ux_criteria_owner_key_active");

            // 6. Assert that old audit entries without entity_type read as entity_type "note"
            var noteRepo = provider.GetRequiredService<INoteRepository>();
            var auditEntriesRes = await noteRepo.GetAuditEntriesAsync("legacy-owner", noteId);
            Assert.True(auditEntriesRes.IsSuccess);
            var entry = Assert.Single(auditEntriesRes.Value!);
            Assert.Equal("note", entry.EntityType);
            Assert.Equal(noteId, entry.EntityId);
            Assert.Equal(noteId, entry.TargetNoteId);

            // 7. Test dry-run import reporting unchanged
            var tempDir = Path.Combine(Path.GetTempPath(), $"dryrun_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            var testMarkdownFile = Path.Combine(tempDir, "legacy-file.md");
            try
            {
                await File.WriteAllTextAsync(testMarkdownFile, "## Legacy Heading\nLegacy note content written by 001 build without tracking references.\n");
                var importHandler = provider.GetRequiredService<ICommandHandler<ImportMarkdownCommand, ImportMarkdownSummaryDto>>();
                var dryRunOutcome = await importHandler.HandleAsync(new ImportMarkdownCommand(
                    FilePaths: [testMarkdownFile],
                    DryRun: true,
                    OwnerId: "legacy-owner"));

                Assert.True(dryRunOutcome.IsSuccess);
                var summary = dryRunOutcome.Value!;
                Assert.True(summary.UnchangedCount == 1, $"Expected UnchangedCount == 1, but got: FilesProcessed={summary.FilesProcessed}, Created={summary.CreatedCount}, Updated={summary.UpdatedCount}, Unchanged={summary.UnchangedCount}, Conflicts={summary.ConflictCount}, Orphans={summary.OrphanCount}, ConflictsList=[{string.Join(", ", summary.Conflicts.Select(c => $"{c.ImportKey}: {c.Reason}"))}]");
                Assert.Equal(0, summary.CreatedCount);
                Assert.Equal(0, summary.UpdatedCount);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }
        finally
        {
            if (File.Exists(archivePath))
            {
                File.Delete(archivePath);
            }
        }
    }

    [Fact]
    public async Task Cli_RoundTrip_SeedAllEntities_Backup_Wipe_Restore_VerifiesCountsAndContentHashes_AndZeroPlaintext()
    {
        var cliDllPath = GetCliDllPath();
        var uniqueDb = $"roleradar_cli_roundtrip_{Guid.CreateVersion7():N}";
        var env = _fixture.CreateProcessEnvironment(uniqueDb);

        const string fictionalContactName = "Zachary Fictional Contact";
        const string fictionalCompanyName = "Zephyr Automated Quantum Systems";
        const string backupPassphrase = "QuantumSecureRoundTrip123!";

        var tempBackup = Path.Combine(Path.GetTempPath(), $"cli_roundtrip_{Guid.NewGuid():N}.rrbak");
        var client = new MongoClient(_connectionString);
        var db = client.GetDatabase(uniqueDb);

        try
        {
            await TrackingIndexInitializer.EnsureIndexesAsync(db, CancellationToken.None);

            // 1. Seed entity via CLI: contact add
            (int addCode, string addOut, string addErr) = await RunCliAsync(
                cliDllPath,
                $"contact add --name \"{fictionalContactName}\" --role \"VP Quantum Engineering\" --company \"{fictionalCompanyName}\" --notes \"Encrypted contact verification notes.\" --json",
                env);
            Assert.Equal(0, addCode);
            using var addDoc = JsonDocument.Parse(addOut);
            var contactIdStr = addDoc.RootElement.GetProperty("data").GetProperty("id").GetString();
            Assert.NotNull(contactIdStr);
            var contactId = Guid.Parse(contactIdStr);

            // Seed Note and Company directly to DB
            var noteId = Guid.NewGuid();
            var companyId = Guid.NewGuid();
            var postingId = Guid.NewGuid();
            var criterionId = Guid.NewGuid();
            var appId = Guid.NewGuid();
            var runId = Guid.NewGuid();
            var now = DateTime.UtcNow;

            await db.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.NotesCollectionName).InsertOneAsync(new BsonDocument
            {
                ["_id"] = new BsonBinaryData(noteId, GuidRepresentation.Standard),
                ["owner_id"] = "local-owner",
                ["title"] = "Quantum Computing Architecture Note",
                ["body"] = "Quantum decoherence mitigations.",
                ["current_body_hash"] = Sha256Hasher.ComputeLowerHex("Quantum decoherence mitigations."),
                ["tags"] = new BsonArray { "quantum" },
                ["area"] = "engineering",
                ["provenance"] = new BsonDocument { ["kind"] = "stated", ["date"] = "2026-10-18" },
                ["status"] = "active",
                ["version_token"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
                ["revision_count"] = 0,
                ["created_at"] = new BsonDateTime(now),
                ["updated_at"] = new BsonDateTime(now)
            });

            await db.GetCollection<BsonDocument>(TrackingMongoOptions.CompaniesCollectionName).InsertOneAsync(new BsonDocument
            {
                ["_id"] = new BsonBinaryData(companyId, GuidRepresentation.Standard),
                ["owner_id"] = "local-owner",
                ["name"] = fictionalCompanyName,
                ["normalized_name"] = fictionalCompanyName.ToLowerInvariant(),
                ["ownership_type"] = "private",
                ["status"] = "target",
                ["category_tags"] = new BsonArray { "quantum" },
                ["career_sources"] = new BsonArray(),
                ["version_token"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
                ["created_at"] = new BsonDateTime(now),
                ["updated_at"] = new BsonDateTime(now)
            });

            // 2. Record pre-backup per-collection counts and content hashes
            var allCollections = new[]
            {
                MongoBootstrapAndIndexes.NotesCollectionName,
                MongoBootstrapAndIndexes.AuditLogCollectionName,
                TrackingMongoOptions.CompaniesCollectionName,
                TrackingMongoOptions.ContactsCollectionName
            };

            var preCounts = new Dictionary<string, long>();
            var preHashes = new Dictionary<string, string>();

            foreach (var colName in allCollections)
            {
                var col = db.GetCollection<BsonDocument>(colName);
                preCounts[colName] = await col.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);
                var docs = await col.Find(Builders<BsonDocument>.Filter.Empty).Sort(Builders<BsonDocument>.Sort.Ascending("_id")).ToListAsync();
                var combinedJson = string.Concat(docs.Select(d => d.ToJson()));
                preHashes[colName] = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(combinedJson)));
            }

            // 3. Run roleradar backup via CLI with passphrase env
            var backupEnv = new Dictionary<string, string>(env)
            {
                ["BACKUP_PW"] = backupPassphrase
            };

            var (bCode, bOut, bErr) = await RunCliAsync(
                cliDllPath,
                $"backup \"{tempBackup}\" --passphrase-env BACKUP_PW",
                backupEnv);
            Assert.Equal(0, bCode);
            Assert.True(File.Exists(tempBackup));

            // Scan raw backup bytes: ensure fictionalContactName and fictionalCompanyName plaintext are completely absent
            var backupBytes = await File.ReadAllBytesAsync(tempBackup);
            var backupRawText = System.Text.Encoding.UTF8.GetString(backupBytes);
            Assert.DoesNotContain(fictionalContactName, backupRawText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(fictionalCompanyName, backupRawText, StringComparison.OrdinalIgnoreCase);

            // 4. Test restore into non-empty database without --force-overwrite fails
            var (failRestoreCode, failRestoreOut, failRestoreErr) = await RunCliAsync(
                cliDllPath,
                $"restore \"{tempBackup}\" --passphrase-env BACKUP_PW",
                backupEnv);
            Assert.NotEqual(0, failRestoreCode);
            Assert.Contains("non-empty", failRestoreOut + failRestoreErr, StringComparison.OrdinalIgnoreCase);

            // 5. Run roleradar wipe via CLI
            (int wCode, string wOut, string wErr) = await RunCliAsync(
                cliDllPath,
                $"wipe --confirm {WipeCommand.ConfirmationToken}",
                env);
            Assert.Equal(0, wCode);

            // Assert empty
            foreach (var colName in allCollections)
            {
                var count = await db.GetCollection<BsonDocument>(colName).CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);
                Assert.Equal(0, count);
            }

            // 6. Run roleradar restore via CLI
            var (rCode, rOut, rErr) = await RunCliAsync(
                cliDllPath,
                $"restore \"{tempBackup}\" --passphrase-env BACKUP_PW",
                backupEnv);
            Assert.Equal(0, rCode);

            // 7. Compare per-collection counts and content hashes before and after
            foreach (var colName in allCollections)
            {
                var col = db.GetCollection<BsonDocument>(colName);
                var postCount = await col.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);
                Assert.Equal(preCounts[colName], postCount);

                var docs = await col.Find(Builders<BsonDocument>.Filter.Empty).Sort(Builders<BsonDocument>.Sort.Ascending("_id")).ToListAsync();
                var combinedJson = string.Concat(docs.Select(d => d.ToJson()));
                var postHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(combinedJson)));
                Assert.Equal(preHashes[colName], postHash);
            }
        }
        finally
        {
            if (File.Exists(tempBackup))
            {
                File.Delete(tempBackup);
            }
            await client.DropDatabaseAsync(uniqueDb);
        }
    }

    private static string GetCliDllPath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "RoleRadar.Cli.dll");
        if (!File.Exists(path))
        {
            path = typeof(RoleRadar.Cli.Program).Assembly.Location;
        }

        Assert.True(File.Exists(path), $"Expected CLI assembly at '{path}'.");
        return path;
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunCliAsync(
        string dllPath,
        string arguments,
        IReadOnlyDictionary<string, string> env)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{dllPath}\" {arguments}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var (k, v) in env)
        {
            psi.Environment[k] = v;
        }

        using var proc = Process.Start(psi)!;
        Assert.NotNull(proc);

        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();

        await proc.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        return (proc.ExitCode, stdout, stderr);
    }
}
