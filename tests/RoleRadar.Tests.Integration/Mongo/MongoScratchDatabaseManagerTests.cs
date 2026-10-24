using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Tests.Integration.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Integration.Mongo;

[Collection("ContainerIsolatedCollection")]
public sealed class MongoScratchDatabaseManagerTests : IClassFixture<IsolatedContainerMongoFixture>
{
    private readonly IsolatedContainerMongoFixture _fixture;

    public MongoScratchDatabaseManagerTests(IsolatedContainerMongoFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ScratchLifecycle_ClonesWithMerge_InitializesIndexes_AndDropsInFinally()
    {
        var client = new MongoClient(_fixture.HostMongoUri);
        var targetDbName = $"rrtest_target_{Guid.NewGuid():N}";
        var targetDb = client.GetDatabase(targetDbName);

        // 1. Seed sample data in target across notes and companies
        var notesCol = targetDb.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.NotesCollectionName);
        var testNoteId = Guid.NewGuid();
        await notesCol.InsertOneAsync(new BsonDocument
        {
            { "_id", new BsonBinaryData(testNoteId, GuidRepresentation.Standard) },
            { "owner_id", "test-owner" },
            { "title", "Architecture Decision Note" },
            { "current_version", 1 }
        });

        var companiesCol = targetDb.GetCollection<BsonDocument>(TrackingMongoOptions.CompaniesCollectionName);
        var testCompanyId = Guid.NewGuid();
        await companiesCol.InsertOneAsync(new BsonDocument
        {
            { "_id", new BsonBinaryData(testCompanyId, GuidRepresentation.Standard) },
            { "owner_id", "test-owner" },
            { "canonical_name", "Acme Industries" },
            { "normalized_name", "acme industries" },
            { "status", "target" }
        });

        // Seed contacts and note_revisions in target to verify they are excluded from cloning
        var contactsCol = targetDb.GetCollection<BsonDocument>(TrackingMongoOptions.ContactsCollectionName);
        await contactsCol.InsertOneAsync(new BsonDocument
        {
            { "_id", new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard) },
            { "owner_id", "test-owner" },
            { "name", "John Doe" }
        });

        var revisionsCol = targetDb.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.NoteRevisionsCollectionName);
        await revisionsCol.InsertOneAsync(new BsonDocument
        {
            { "_id", new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard) },
            { "owner_id", "test-owner" },
            { "note_id", new BsonBinaryData(testNoteId, GuidRepresentation.Standard) }
        });

        var manager = new MongoScratchDatabaseManager(client);
        string scratchDbName;

        // 2. Create scratch database scope and verify cloning & indexes
        await using (var scope = await manager.CreateScopeAsync(targetDbName))
        {
            scratchDbName = scope.ScratchDatabaseName;

            Assert.True(MongoScratchDatabaseGuard.IsValidScratchDatabaseName(scratchDbName));
            Assert.NotEqual(targetDbName, scratchDbName);

            var scratchDb = client.GetDatabase(scratchDbName);

            // Verify documents cloned via $merge
            var clonedNote = await scratchDb.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.NotesCollectionName)
                .Find(Builders<BsonDocument>.Filter.Eq("_id", testNoteId))
                .FirstOrDefaultAsync();
            Assert.NotNull(clonedNote);
            Assert.Equal("Architecture Decision Note", clonedNote["title"].AsString);

            var clonedCompany = await scratchDb.GetCollection<BsonDocument>(TrackingMongoOptions.CompaniesCollectionName)
                .Find(Builders<BsonDocument>.Filter.Eq("_id", testCompanyId))
                .FirstOrDefaultAsync();
            Assert.NotNull(clonedCompany);
            Assert.Equal("Acme Industries", clonedCompany["canonical_name"].AsString);

            // Verify excluded collections (contacts and note_revisions) were NOT cloned
            var scratchContactsCount = await scratchDb.GetCollection<BsonDocument>(TrackingMongoOptions.ContactsCollectionName)
                .CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);
            Assert.Equal(0, scratchContactsCount);

            var scratchRevisionsCount = await scratchDb.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.NoteRevisionsCollectionName)
                .CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);
            Assert.Equal(0, scratchRevisionsCount);

            // Verify indexes were initialized on scratch database
            var scratchCompanyIndexes = await (await scratchDb.GetCollection<BsonDocument>(TrackingMongoOptions.CompaniesCollectionName)
                .Indexes.ListAsync()).ToListAsync();
            var indexNames = scratchCompanyIndexes.Select(idx => idx["name"].AsString).ToList();
            Assert.Contains(indexNames, name => name.Contains("normalized_name", StringComparison.OrdinalIgnoreCase));
        }

        // 3. Verify finally drop: scratch database is gone, target database remains untouched
        var databaseNames = await (await client.ListDatabaseNamesAsync()).ToListAsync();
        Assert.DoesNotContain(scratchDbName, databaseNames);

        var targetNoteCount = await notesCol.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);
        Assert.Equal(1, targetNoteCount);
        var targetCompanyCount = await companiesCol.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);
        Assert.Equal(1, targetCompanyCount);
    }

    [Fact]
    public async Task FailedOrAbortedDryRun_LeavesTargetCollectionCountsAndAuditLogUnchanged()
    {
        var client = new MongoClient(_fixture.HostMongoUri);
        var targetDbName = $"rrtest_target_{Guid.NewGuid():N}";
        var targetDb = client.GetDatabase(targetDbName);

        // 1. Seed baseline documents and audit entries in target
        var auditCol = targetDb.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.AuditLogCollectionName);
        await auditCol.InsertOneAsync(new BsonDocument
        {
            { "_id", new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard) },
            { "owner_id", "test-owner" },
            { "operation", "company_create" },
            { "occurred_at", DateTime.UtcNow }
        });

        var postingsCol = targetDb.GetCollection<BsonDocument>(TrackingMongoOptions.PostingsCollectionName);
        await postingsCol.InsertOneAsync(new BsonDocument
        {
            { "_id", new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard) },
            { "owner_id", "test-owner" },
            { "title", "Existing Lead" }
        });

        var baselineAuditCount = await auditCol.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);
        var baselinePostingsCount = await postingsCol.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);

        var manager = new MongoScratchDatabaseManager(client);

        // 2. Simulate failed dry run that writes to scratch and throws/aborts
        string scratchDbName = string.Empty;
        var caughtException = false;

        try
        {
            await using var scope = await manager.CreateScopeAsync(targetDbName);
            scratchDbName = scope.ScratchDatabaseName;
            var scratchDb = client.GetDatabase(scratchDbName);

            // Write 5 new postings and 5 audit logs into the scratch database
            var scratchPostings = scratchDb.GetCollection<BsonDocument>(TrackingMongoOptions.PostingsCollectionName);
            var scratchAudit = scratchDb.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.AuditLogCollectionName);

            for (var i = 0; i < 5; i++)
            {
                await scratchPostings.InsertOneAsync(new BsonDocument
                {
                    { "_id", new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard) },
                    { "owner_id", "test-owner" },
                    { "title", $"Scratch Lead {i}" }
                });

                await scratchAudit.InsertOneAsync(new BsonDocument
                {
                    { "_id", new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard) },
                    { "owner_id", "test-owner" },
                    { "operation", "import_posting_create" },
                    { "occurred_at", DateTime.UtcNow }
                });
            }

            // Simulate unhandled failure during dry-run pipeline
            throw new InvalidOperationException("Simulated unexpected batch failure during dry-run!");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Simulated unexpected batch failure"))
        {
            caughtException = true;
        }

        Assert.True(caughtException);

        // 3. Assert target database collections and audit_log are 100% untouched
        var finalAuditCount = await auditCol.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);
        var finalPostingsCount = await postingsCol.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);

        Assert.Equal(baselineAuditCount, finalAuditCount);
        Assert.Equal(baselinePostingsCount, finalPostingsCount);

        // And verify scratch was cleanly dropped in the finally block
        var databases = await (await client.ListDatabaseNamesAsync()).ToListAsync();
        Assert.DoesNotContain(scratchDbName, databases);
    }

    [Fact]
    public async Task OrphanCleanup_CleansStaleScratchDatabases_AndNeverDropsYoungOrTargetDatabases()
    {
        var client = new MongoClient(_fixture.HostMongoUri);
        var targetDbName = $"rrtest_target_{Guid.NewGuid():N}";
        var targetDb = client.GetDatabase(targetDbName);
        await targetDb.CreateCollectionAsync("canary");

        // Create an "old" scratch database with a timestamp 2 hours ago
        var oldTime = DateTimeOffset.UtcNow.AddHours(-2);
        var oldGuid = CreateUuidV7WithTimestamp(oldTime);
        var oldScratchDbName = $"roleradar_scratch_{oldGuid:N}";
        var oldDb = client.GetDatabase(oldScratchDbName);
        await oldDb.CreateCollectionAsync("dummy");

        // Create a "young" scratch database with a timestamp 5 minutes ago
        var youngTime = DateTimeOffset.UtcNow.AddMinutes(-5);
        var youngGuid = CreateUuidV7WithTimestamp(youngTime);
        var youngScratchDbName = $"roleradar_scratch_{youngGuid:N}";
        var youngDb = client.GetDatabase(youngScratchDbName);
        await youngDb.CreateCollectionAsync("dummy");

        var manager = new MongoScratchDatabaseManager(client);

        // Run orphan cleanup with default 1-hour threshold
        var droppedCount = await manager.CleanupOrphanedScratchDatabasesAsync(targetDbName, minimumAge: TimeSpan.FromHours(1));

        Assert.True(droppedCount >= 1);

        var remainingDbs = await (await client.ListDatabaseNamesAsync()).ToListAsync();

        // Old scratch must be dropped
        Assert.DoesNotContain(oldScratchDbName, remainingDbs);

        // Young scratch (< 1 hour) must be preserved
        Assert.Contains(youngScratchDbName, remainingDbs);

        // Target database must never be dropped
        Assert.Contains(targetDbName, remainingDbs);

        // Clean up young scratch manually
        await client.DropDatabaseAsync(youngScratchDbName);
    }

    [Fact]
    public void AllManagedCollections_ExcludesContactsAndNoteRevisions()
    {
        Assert.DoesNotContain(TrackingMongoOptions.ContactsCollectionName, MongoScratchDatabaseManager.AllManagedCollections);
        Assert.DoesNotContain(MongoBootstrapAndIndexes.NoteRevisionsCollectionName, MongoScratchDatabaseManager.AllManagedCollections);
    }

    [Fact]
    public async Task CreateScratchDatabaseAsync_WhenCancelledDuringCloning_DropsScratchDatabaseBeforeRethrowing()
    {
        var client = new MongoClient(_fixture.HostMongoUri);
        var targetDbName = $"rrtest_target_{Guid.NewGuid():N}";
        var targetDb = client.GetDatabase(targetDbName);

        // Seed target data in notes collection so cloning has data to process
        var notesCol = targetDb.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.NotesCollectionName);
        for (var i = 0; i < 10; i++)
        {
            await notesCol.InsertOneAsync(new BsonDocument
            {
                { "_id", new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard) },
                { "owner_id", "test-owner" },
                { "title", $"Test Note {i}" }
            });
        }

        var manager = new MongoScratchDatabaseManager(client);

        // Use a cancellation token source that cancels promptly to simulate mid-pipeline cancellation/failure
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await manager.CreateScratchDatabaseAsync(targetDbName, cts.Token);
        });

        // Verify that no orphaned roleradar_scratch_* database remains
        var databases = await (await client.ListDatabaseNamesAsync()).ToListAsync();
        Assert.DoesNotContain(databases, db => db.StartsWith("roleradar_scratch_", StringComparison.Ordinal));
    }

    private static Guid CreateUuidV7WithTimestamp(DateTimeOffset timestamp)
    {
        var unixMs = timestamp.ToUnixTimeMilliseconds();
        Span<byte> bytes = stackalloc byte[16];
        Random.Shared.NextBytes(bytes);

        // Write 48-bit timestamp in big-endian
        bytes[0] = (byte)((unixMs >> 40) & 0xFF);
        bytes[1] = (byte)((unixMs >> 32) & 0xFF);
        bytes[2] = (byte)((unixMs >> 24) & 0xFF);
        bytes[3] = (byte)((unixMs >> 16) & 0xFF);
        bytes[4] = (byte)((unixMs >> 8) & 0xFF);
        bytes[5] = (byte)(unixMs & 0xFF);

        // Set version 7 (0b0111) in byte 6
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x70);
        // Set variant RFC 4122 (0b10) in byte 8
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        return new Guid(bytes, bigEndian: true);
    }
}
