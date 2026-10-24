using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Application.Abstractions;
using RoleRadar.Cli.Commands;
using RoleRadar.Domain.Common;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Tests.Integration.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Integration.Hosting;

[Collection("ContainerIsolatedCollection")]
public sealed class ContainerLegacyBackupCompatibilityTests : IClassFixture<IsolatedContainerMongoFixture>
{
    private readonly IsolatedContainerMongoFixture _mongo;

    public ContainerLegacyBackupCompatibilityTests(IsolatedContainerMongoFixture mongo)
    {
        _mongo = mongo;
    }

    [Fact]
    public async Task Container_Restores001LegacyBackup_WithNoteEntityTypeAndTrackingIndexes()
    {
        await ContainerTestHarness.EnsureImagesBuiltAsync();

        var client = new MongoClient(_mongo.HostMongoUri);
        var uniqueDb = $"rrtest_legacy001_{Guid.NewGuid():N}";
        var db = client.GetDatabase(uniqueDb);

        var tempBackupDir = Path.Combine(Path.GetTempPath(), $"rr_leg001_bak_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempBackupDir);
        var hostBackupFile = Path.Combine(tempBackupDir, "legacy001.rrbak");
        var containerBackupFile = "/data/backup/legacy001.rrbak";

        try
        {
            // Seed a 001-shaped database: 1 note and 1 audit log entry WITHOUT entity_type
            var ownerId = "legacy-001-owner";
            var noteId = Guid.NewGuid();
            var noteVersion = Guid.NewGuid();
            var auditId = Guid.NewGuid();

            await db.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.NotesCollectionName).InsertOneAsync(new BsonDocument
            {
                ["_id"] = new BsonBinaryData(noteId, GuidRepresentation.Standard),
                ["owner_id"] = ownerId,
                ["title"] = "Legacy 001 Note",
                ["body"] = "Body text from 001 format",
                ["source_file"] = new BsonDocument
                {
                    ["source_file_id"] = "legacy.md",
                    ["heading_path"] = "## Heading",
                    ["occurrence_index"] = 0,
                    ["section_order_index"] = 1,
                    ["last_imported_body_hash"] = "abc123hash",
                    ["import_key"] = "legacy.md### Heading#0"
                },
                ["status"] = "active",
                ["version_token"] = new BsonBinaryData(noteVersion, GuidRepresentation.Standard),
                ["revision_count"] = 0,
                ["created_at"] = new BsonDateTime(DateTime.UtcNow),
                ["updated_at"] = new BsonDateTime(DateTime.UtcNow)
            });

            // 001 audit entry without entity_type
            await db.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.AuditLogCollectionName).InsertOneAsync(new BsonDocument
            {
                ["_id"] = new BsonBinaryData(auditId, GuidRepresentation.Standard),
                ["owner_id"] = ownerId,
                ["actor"] = "legacy-agent",
                ["operation"] = "note_create",
                ["target_note_id"] = new BsonBinaryData(noteId, GuidRepresentation.Standard),
                ["resulting_version_token"] = new BsonBinaryData(noteVersion, GuidRepresentation.Standard),
                ["occurred_at"] = new BsonDateTime(DateTime.UtcNow)
            });

            var env = new Dictionary<string, string>
            {
                ["ROLERADAR_MONGO_CONNECTION_STRING"] = _mongo.ContainerMongoUri,
                ["ROLERADAR_MONGO_DATABASE"] = uniqueDb,
                ["BACKUP_PW"] = "LegacyPass123!"
            };

            var volumes = new[] { $"{tempBackupDir}:/data/backup:rw" };

            // 1. Create backup archive inside container
            var backupResult = await ContainerTestHarness.RunCliAsync(
                ["backup", containerBackupFile, "--passphrase-env", "BACKUP_PW"],
                environment: env,
                volumes: volumes,
                network: _mongo.NetworkName);
            Assert.Equal(0, backupResult.ExitCode);

            // 2. Wipe database inside container
            var wipeResult = await ContainerTestHarness.RunCliAsync(
                ["wipe", "--yes"],
                environment: env,
                network: _mongo.NetworkName);
            Assert.Equal(0, wipeResult.ExitCode);

            // 3. Restore 001 archive inside container
            var restoreResult = await ContainerTestHarness.RunCliAsync(
                ["restore", containerBackupFile, "--passphrase-env", "BACKUP_PW"],
                environment: env,
                volumes: volumes,
                network: _mongo.NetworkName);
            Assert.Equal(0, restoreResult.ExitCode);

            // 4. Verify tracking indexes exist on restored empty collections
            var companyIndexes = await (await db.GetCollection<BsonDocument>(TrackingMongoOptions.CompaniesCollectionName).Indexes.ListAsync()).ToListAsync();
            Assert.Contains(companyIndexes, idx => idx["name"].AsString == "ux_companies_owner_normalized_name");

            // 5. Verify audit log entry without entity_type is intact and readable
            var restoredAudit = await db.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.AuditLogCollectionName).Find(Builders<BsonDocument>.Filter.Empty).FirstOrDefaultAsync();
            Assert.NotNull(restoredAudit);
            Assert.Equal(new BsonBinaryData(noteId, GuidRepresentation.Standard), restoredAudit["target_note_id"]);
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
