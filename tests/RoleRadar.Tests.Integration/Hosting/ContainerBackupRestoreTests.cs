using System.Security.Cryptography;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Application.Abstractions;
using RoleRadar.Cli.Commands;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Tracking;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Tests.Integration.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Integration.Hosting;

[Collection("ContainerIsolatedCollection")]
public sealed class ContainerBackupRestoreTests : IClassFixture<IsolatedContainerMongoFixture>
{
    private readonly IsolatedContainerMongoFixture _mongo;

    public ContainerBackupRestoreTests(IsolatedContainerMongoFixture mongo)
    {
        _mongo = mongo;
    }

    [Fact]
    public async Task Container_ExecutesBackupRestoreAndWipe_CoveringAll12Collections()
    {
        await ContainerTestHarness.EnsureImagesBuiltAsync();

        var client = new MongoClient(_mongo.HostMongoUri);
        var uniqueDb = $"rrtest_backup_{Guid.NewGuid():N}";
        var db = client.GetDatabase(uniqueDb);

        var tempBackupDir = ContainerTestHarness.CreateSharedTempDirectory("rr_container_bak");
        var hostBackupFile = Path.Combine(tempBackupDir, "test.rrbak");
        var containerBackupFile = "/data/backup/test.rrbak";

        try
        {
            // Seed all 12 collections
            var ownerId = "container-test-owner";
            var noteId = Guid.NewGuid();
            var noteVersion = Guid.NewGuid();
            var revisionId = Guid.NewGuid();
            var auditId = Guid.NewGuid();
            var companyId = Guid.NewGuid();
            var careerSourceId = Guid.NewGuid();
            var postingId = Guid.NewGuid();
            var signalId = Guid.NewGuid();
            var criterionId = Guid.NewGuid();
            var applicationId = Guid.NewGuid();
            var appEventId = Guid.NewGuid();
            var contactId = Guid.NewGuid();
            var searchRunId = Guid.NewGuid();

            await db.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.NotesCollectionName).InsertOneAsync(new BsonDocument
            {
                ["_id"] = new BsonBinaryData(noteId, GuidRepresentation.Standard),
                ["owner_id"] = ownerId,
                ["title"] = "Container Note",
                ["body"] = "Body text",
                ["status"] = "active",
                ["version_token"] = new BsonBinaryData(noteVersion, GuidRepresentation.Standard),
                ["revision_count"] = 0,
                ["created_at"] = new BsonDateTime(DateTime.UtcNow),
                ["updated_at"] = new BsonDateTime(DateTime.UtcNow)
            });

            await db.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.NoteRevisionsCollectionName).InsertOneAsync(new BsonDocument
            {
                ["_id"] = new BsonBinaryData(revisionId, GuidRepresentation.Standard),
                ["note_id"] = new BsonBinaryData(noteId, GuidRepresentation.Standard),
                ["owner_id"] = ownerId,
                ["title"] = "Rev 0",
                ["body"] = "Body text",
                ["version_token"] = new BsonBinaryData(noteVersion, GuidRepresentation.Standard),
                ["revision_number"] = 0,
                ["created_at"] = new BsonDateTime(DateTime.UtcNow)
            });

            await db.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.AuditLogCollectionName).InsertOneAsync(new BsonDocument
            {
                ["_id"] = new BsonBinaryData(auditId, GuidRepresentation.Standard),
                ["owner_id"] = ownerId,
                ["actor"] = "container-test",
                ["operation"] = "note_create",
                ["entity_type"] = "note",
                ["entity_id"] = new BsonBinaryData(noteId, GuidRepresentation.Standard),
                ["target_note_id"] = new BsonBinaryData(noteId, GuidRepresentation.Standard),
                ["resulting_version_token"] = new BsonBinaryData(noteVersion, GuidRepresentation.Standard),
                ["occurred_at"] = new BsonDateTime(DateTime.UtcNow)
            });

            await db.GetCollection<BsonDocument>(TrackingMongoOptions.CompaniesCollectionName).InsertOneAsync(new BsonDocument
            {
                ["_id"] = new BsonBinaryData(companyId, GuidRepresentation.Standard),
                ["owner_id"] = ownerId,
                ["name"] = "Container Test Corp",
                ["normalized_name"] = "container test corp",
                ["version_token"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
                ["created_at"] = new BsonDateTime(DateTime.UtcNow),
                ["updated_at"] = new BsonDateTime(DateTime.UtcNow)
            });

            await db.GetCollection<BsonDocument>(TrackingMongoOptions.CareerSourcesCollectionName).InsertOneAsync(new BsonDocument
            {
                ["_id"] = new BsonBinaryData(careerSourceId, GuidRepresentation.Standard),
                ["owner_id"] = ownerId,
                ["company_id"] = new BsonBinaryData(companyId, GuidRepresentation.Standard),
                ["url"] = "https://example.com/careers",
                ["source_type"] = "ats",
                ["is_active"] = true,
                ["created_at"] = new BsonDateTime(DateTime.UtcNow),
                ["updated_at"] = new BsonDateTime(DateTime.UtcNow)
            });

            await db.GetCollection<BsonDocument>(TrackingMongoOptions.PostingsCollectionName).InsertOneAsync(new BsonDocument
            {
                ["_id"] = new BsonBinaryData(postingId, GuidRepresentation.Standard),
                ["owner_id"] = ownerId,
                ["company_id"] = new BsonBinaryData(companyId, GuidRepresentation.Standard),
                ["role_title"] = "Container Engineer",
                ["url"] = "https://example.com/job/1",
                ["status"] = "lead",
                ["first_seen_at"] = new BsonDateTime(DateTime.UtcNow),
                ["last_verified_at"] = new BsonDateTime(DateTime.UtcNow),
                ["created_at"] = new BsonDateTime(DateTime.UtcNow),
                ["updated_at"] = new BsonDateTime(DateTime.UtcNow)
            });

            await db.GetCollection<BsonDocument>(TrackingMongoOptions.PostingSignalsCollectionName).InsertOneAsync(new BsonDocument
            {
                ["_id"] = new BsonBinaryData(signalId, GuidRepresentation.Standard),
                ["owner_id"] = ownerId,
                ["posting_id"] = new BsonBinaryData(postingId, GuidRepresentation.Standard),
                ["signal_type"] = "compensation",
                ["observed_at"] = new BsonDateTime(DateTime.UtcNow),
                ["value"] = "$150,000"
            });

            await db.GetCollection<BsonDocument>(TrackingMongoOptions.CriteriaCollectionName).InsertOneAsync(new BsonDocument
            {
                ["_id"] = new BsonBinaryData(criterionId, GuidRepresentation.Standard),
                ["owner_id"] = ownerId,
                ["key"] = "min_salary",
                ["description"] = "Minimum base pay",
                ["effective_from"] = new BsonDateTime(DateTime.UtcNow),
                ["created_at"] = new BsonDateTime(DateTime.UtcNow)
            });

            await db.GetCollection<BsonDocument>(TrackingMongoOptions.ApplicationsCollectionName).InsertOneAsync(new BsonDocument
            {
                ["_id"] = new BsonBinaryData(applicationId, GuidRepresentation.Standard),
                ["owner_id"] = ownerId,
                ["posting_id"] = new BsonBinaryData(postingId, GuidRepresentation.Standard),
                ["outcome"] = "applied",
                ["created_at"] = new BsonDateTime(DateTime.UtcNow),
                ["updated_at"] = new BsonDateTime(DateTime.UtcNow)
            });

            await db.GetCollection<BsonDocument>(TrackingMongoOptions.ApplicationEventsCollectionName).InsertOneAsync(new BsonDocument
            {
                ["_id"] = new BsonBinaryData(appEventId, GuidRepresentation.Standard),
                ["owner_id"] = ownerId,
                ["application_id"] = new BsonBinaryData(applicationId, GuidRepresentation.Standard),
                ["event_type"] = "interview_scheduled",
                ["created_at"] = new BsonDateTime(DateTime.UtcNow)
            });

            await db.GetCollection<BsonDocument>(TrackingMongoOptions.ContactsCollectionName).InsertOneAsync(new BsonDocument
            {
                ["_id"] = new BsonBinaryData(contactId, GuidRepresentation.Standard),
                ["owner_id"] = ownerId,
                ["name"] = "Alice Smith",
                ["relationship"] = "recruiter",
                ["is_redacted"] = false,
                ["version_token"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
                ["created_at"] = new BsonDateTime(DateTime.UtcNow),
                ["updated_at"] = new BsonDateTime(DateTime.UtcNow)
            });

            await db.GetCollection<BsonDocument>(TrackingMongoOptions.SearchRunsCollectionName).InsertOneAsync(new BsonDocument
            {
                ["_id"] = new BsonBinaryData(searchRunId, GuidRepresentation.Standard),
                ["owner_id"] = ownerId,
                ["started_at"] = new BsonDateTime(DateTime.UtcNow),
                ["source"] = "manual",
                ["created_at"] = new BsonDateTime(DateTime.UtcNow)
            });

            var all12 = new[]
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

            var env = new Dictionary<string, string>
            {
                ["ROLERADAR_MONGO_CONNECTION_STRING"] = _mongo.ContainerMongoUri,
                ["ROLERADAR_MONGO_DATABASE"] = uniqueDb,
                ["BACKUP_PW"] = "SuperSecretContainerPW123!"
            };

            var volumes = new[] { $"{tempBackupDir}:/data/backup:rw" };

            // 1. Run roleradar backup inside container
            var backupResult = await ContainerTestHarness.RunCliAsync(
                ["backup", containerBackupFile, "--passphrase-env", "BACKUP_PW"],
                environment: env,
                volumes: volumes,
                network: _mongo.NetworkName);

            Assert.Equal(0, backupResult.ExitCode);
            Assert.True(File.Exists(hostBackupFile), $"Expected backup file at {hostBackupFile}");

            // 2. Run roleradar wipe inside container with --yes
            var wipeResult = await ContainerTestHarness.RunCliAsync(
                ["wipe", "--yes"],
                environment: env,
                network: _mongo.NetworkName);

            Assert.Equal(0, wipeResult.ExitCode);
            foreach (var col in all12)
            {
                var count = await db.GetCollection<BsonDocument>(col).CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);
                Assert.Equal(0, count);
            }

            // 3. Run roleradar restore inside container
            var restoreResult = await ContainerTestHarness.RunCliAsync(
                ["restore", containerBackupFile, "--passphrase-env", "BACKUP_PW"],
                environment: env,
                volumes: volumes,
                network: _mongo.NetworkName);

            Assert.Equal(0, restoreResult.ExitCode);

            // Assert all 12 collections restored with exactly 1 document
            foreach (var col in all12)
            {
                var count = await db.GetCollection<BsonDocument>(col).CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);
                Assert.Equal(1, count);
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempBackupDir))
                {
                    Directory.Delete(tempBackupDir, recursive: true);
                }
            }
            catch
            {
                // Ignore cleanup error
            }

            ContainerTestHarness.ValidateDatabaseNameForDrop(uniqueDb);
            await client.DropDatabaseAsync(uniqueDb);
        }
    }

    [Fact]
    public async Task Container_BackupIntoRunnerOwnedDirectory_SucceedsWithHostUserMapping()
    {
        var client = new MongoClient(_mongo.HostMongoUri);
        var uniqueDb = $"rrtest_backup_usr_{Guid.NewGuid():N}";
        var db = client.GetDatabase(uniqueDb);

        var tempBackupDir = Path.Combine(Path.GetTempPath(), $"rr_user_bak_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempBackupDir);
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            // Set directory to 0700 (drwx------), owned by host runner process.
            // Default container image user (UID 1654) has zero permissions unless
            // the harness passes `--user "$(id -u):$(id -g)"`.
            File.SetUnixFileMode(tempBackupDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var hostBackupFile = Path.Combine(tempBackupDir, "user_test.rrbak");
        var containerBackupFile = "/data/backup/user_test.rrbak";

        try
        {
            // Seed a note in the database
            await db.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.NotesCollectionName).InsertOneAsync(new BsonDocument
            {
                ["_id"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
                ["owner_id"] = "test-owner",
                ["title"] = "Host User Perms Note",
                ["body"] = "Verifying host user mapping into 0700 runner directory",
                ["created_at"] = new BsonDateTime(DateTime.UtcNow),
                ["updated_at"] = new BsonDateTime(DateTime.UtcNow)
            });

            var env = new Dictionary<string, string>
            {
                ["ROLERADAR_MONGO_CONNECTION_STRING"] = _mongo.ContainerMongoUri,
                ["ROLERADAR_MONGO_DATABASE"] = uniqueDb,
                ["BACKUP_PW"] = "HostUserPw123!"
            };

            var volumes = new[] { $"{tempBackupDir}:/data/backup:rw" };

            // Run backup inside container
            var backupResult = await ContainerTestHarness.RunCliAsync(
                ["backup", containerBackupFile, "--passphrase-env", "BACKUP_PW"],
                environment: env,
                volumes: volumes,
                network: _mongo.NetworkName);

            Assert.Equal(0, backupResult.ExitCode);
            Assert.True(File.Exists(hostBackupFile), $"Expected backup file at {hostBackupFile}");
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempBackupDir))
                {
                    Directory.Delete(tempBackupDir, recursive: true);
                }
            }
            catch
            {
                // Ignore cleanup error
            }

            ContainerTestHarness.ValidateDatabaseNameForDrop(uniqueDb);
            await client.DropDatabaseAsync(uniqueDb);
        }
    }
}
