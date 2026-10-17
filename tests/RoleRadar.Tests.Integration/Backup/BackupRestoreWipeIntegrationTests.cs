using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Application;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Notes.Commands;
using RoleRadar.Cli.Commands;
using RoleRadar.Domain.Notes;
using RoleRadar.Infrastructure;
using RoleRadar.Infrastructure.Backup;
using RoleRadar.Infrastructure.Mongo;
using Testcontainers.MongoDb;
using Xunit;

namespace RoleRadar.Tests.Integration.Backup;

public sealed class BackupRestoreWipeIntegrationTests : IAsyncLifetime
{
    private readonly MongoDbContainer _mongoContainer = new MongoDbBuilder("mongo:8.0.34")
        .WithReplicaSet("rs0")
        .Build();

    private string _connectionString = string.Empty;
    private string _databaseName = string.Empty;

    public async ValueTask InitializeAsync()
    {
        await _mongoContainer.StartAsync();
        _connectionString = _mongoContainer.GetConnectionString();
        _databaseName = $"roleradar_backup_test_{Guid.CreateVersion7():N}";
    }

    public async ValueTask DisposeAsync() => await _mongoContainer.DisposeAsync();

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
            opts.DumpCommand = $"docker exec -i {_mongoContainer.Id} mongodump --uri=\"{internalUri}\" --archive --gzip --oplog";
            opts.RestoreCommand = $"docker exec -i {_mongoContainer.Id} mongorestore --uri=\"{internalUri}\" --archive --gzip --oplogReplay";
        });

        if (configureBackup is not null)
        {
            services.Configure(configureBackup);
        }

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task HostToCompose_Connectivity_EvaluatesReplicaSetAndDirectConnection()
    {
        // 1. Test standard replica set topology discovery
        var clientSettingsStandard = MongoClientSettings.FromConnectionString(_connectionString);
        clientSettingsStandard.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
        var standardClient = new MongoClient(clientSettingsStandard);
        var standardAdmin = standardClient.GetDatabase("admin");
        var pingStandard = await standardAdmin.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1));
        Assert.True(pingStandard["ok"].ToBoolean());

        // 2. Test directConnection=true
        var directConnectionString = _connectionString.Contains('?')
            ? $"{_connectionString}&directConnection=true"
            : $"{_connectionString}?directConnection=true";

        var clientSettingsDirect = MongoClientSettings.FromConnectionString(directConnectionString);
        clientSettingsDirect.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
        var directClient = new MongoClient(clientSettingsDirect);
        var directAdmin = directClient.GetDatabase("admin");
        var pingDirect = await directAdmin.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1));
        Assert.True(pingDirect["ok"].ToBoolean());
    }

    [Fact]
    public async Task Cli_RejectsPassphraseValueArgument()
    {
        await using var provider = BuildServiceProvider();
        var backupCmd = BackupCommand.Create(provider);
        var restoreCmd = RestoreCommand.Create(provider);

        // System.CommandLine validation rejection for roleradar backup --passphrase secret
        var backupParse = backupCmd.Parse(["--passphrase", "secretPassphrase123!"]);
        Assert.NotEmpty(backupParse.Errors);
        Assert.Contains(backupParse.Errors, e => e.Message.Contains("forbidden", StringComparison.OrdinalIgnoreCase));

        // System.CommandLine validation rejection for roleradar restore file.rrbak --passphrase secret
        var restoreParse = restoreCmd.Parse(["dummy.rrbak", "--passphrase", "secretPassphrase123!"]);
        Assert.NotEmpty(restoreParse.Errors);
        Assert.Contains(restoreParse.Errors, e => e.Message.Contains("forbidden", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task FileWatcher_TempAndTargetLocations_ZeroPlaintextFilesWritten()
    {
        await using var provider = BuildServiceProvider();

        var backupService = provider.GetRequiredService<IBackupArchiveService>();
        var targetDir = Path.Combine(Path.GetTempPath(), "roleradar_test_backup_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(targetDir);

        var forbiddenPlaintextExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".json", ".bson", ".gzip", ".gz", ".tar", ".dump", ".txt", ".sql"
        };

        var disallowedCreatedFiles = new List<string>();

        using var tempWatcher = new FileSystemWatcher(Path.GetTempPath()) { EnableRaisingEvents = true };
        using var targetWatcher = new FileSystemWatcher(targetDir) { EnableRaisingEvents = true };

        void OnFileCreated(object sender, FileSystemEventArgs e)
        {
            var ext = Path.GetExtension(e.FullPath);
            if (forbiddenPlaintextExtensions.Contains(ext))
            {
                lock (disallowedCreatedFiles)
                {
                    disallowedCreatedFiles.Add(e.FullPath);
                }
            }
        }

        tempWatcher.Created += OnFileCreated;
        targetWatcher.Created += OnFileCreated;

        try
        {
            var backupFile = Path.Combine(targetDir, "test.rrbak");
            var result = await backupService.CreateBackupAsync(backupFile, "CorrectHorseBatteryStaple123!");
            Assert.True(result.IsSuccess, $"Backup failed: {result.Error?.Message}");

            Assert.Empty(disallowedCreatedFiles);
        }
        finally
        {
            tempWatcher.Created -= OnFileCreated;
            targetWatcher.Created -= OnFileCreated;
            if (Directory.Exists(targetDir))
            {
                Directory.Delete(targetDir, true);
            }
        }
    }

    [Fact]
    public async Task CorruptedFinalChunk_FailsInPass1_LeavesMongoUntouched()
    {
        await using var provider = BuildServiceProvider(opts =>
        {
            opts.DumpCommand = $"docker exec -i {_mongoContainer.Id} mongodump --archive --gzip --oplog";
            opts.RestoreCommand = $"docker exec -i {_mongoContainer.Id} mongorestore --archive --gzip --oplogReplay";
        });

        // 1. Insert a document so we can detect if any modification occurs
        var mongoClient = provider.GetRequiredService<IMongoClient>();
        var db = mongoClient.GetDatabase(_databaseName);
        var notesCol = db.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.NotesCollectionName);
        await notesCol.InsertOneAsync(new BsonDocument
        {
            { "_id", ObjectId.GenerateNewId() },
            { "owner_id", "owner-initial" },
            { "title", "Initial Note Before Corrupt Restore" },
            { "body", "Must remain untouched." }
        });

        var countBefore = await notesCol.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);
        Assert.Equal(1, countBefore);

        // 2. Create a corrupted backup file
        var tempFile = Path.Combine(Path.GetTempPath(), $"corrupt_backup_{Guid.NewGuid():N}.rrbak");
        var header = new byte[BackupArchiveConstants.HeaderSize];
        Array.Copy(BackupArchiveConstants.Magic, 0, header, 0, 4);
        header[4] = BackupArchiveConstants.CurrentFormatVersion;
        header[5] = BackupArchiveConstants.ExpectedFlags;
        Array.Copy(BitConverter.GetBytes(BackupArchiveConstants.DefaultArgon2Iterations), 0, header, 6, 4);
        Array.Copy(BitConverter.GetBytes(BackupArchiveConstants.DefaultArgon2MemoryKiB), 0, header, 10, 4);

        // Corrupted chunk
        var corruptedData = new List<byte>(header);
        corruptedData.AddRange(BitConverter.GetBytes(100)); // length
        corruptedData.AddRange(new byte[100]); // garbage chunk
        await File.WriteAllBytesAsync(tempFile, corruptedData.ToArray());

        try
        {
            var backupService = provider.GetRequiredService<IBackupArchiveService>();
            var restoreResult = await backupService.RestoreBackupAsync(tempFile, "AnyPassphrase123!", forceOverwrite: true);

            // Restore must fail in Pass 1
            Assert.False(restoreResult.IsSuccess);
            Assert.Equal(ErrorCode.AuthenticationError, restoreResult.Error?.Code);

            // Verify MongoDB was untouched
            var countAfter = await notesCol.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);
            Assert.Equal(countBefore, countAfter);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Restore_IntoNonEmptyDatastore_RefusesWithoutForceOverwrite()
    {
        await using var provider = BuildServiceProvider();
        var mongoClient = provider.GetRequiredService<IMongoClient>();
        var db = mongoClient.GetDatabase(_databaseName);
        var notesCol = db.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.NotesCollectionName);

        await notesCol.InsertOneAsync(new BsonDocument
        {
            { "_id", ObjectId.GenerateNewId() },
            { "owner_id", "owner-test" },
            { "title", "Existing Note" }
        });

        var backupService = provider.GetRequiredService<IBackupArchiveService>();
        var restoreResult = await backupService.RestoreBackupAsync("some-archive.rrbak", "Passphrase123!", forceOverwrite: false);

        Assert.False(restoreResult.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, restoreResult.Error?.Code);
        Assert.Contains("non-empty", restoreResult.Error?.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Wipe_RefusesWithoutExactConfirmationToken()
    {
        await using var provider = BuildServiceProvider();
        var backupService = provider.GetRequiredService<IBackupArchiveService>();

        var resultNoToken = await backupService.WipeDatabaseAsync(string.Empty);
        Assert.False(resultNoToken.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, resultNoToken.Error?.Code);

        var resultWrongToken = await backupService.WipeDatabaseAsync("INCORRECT-TOKEN");
        Assert.False(resultWrongToken.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, resultWrongToken.Error?.Code);
    }

    [Fact]
    public async Task Wipe_RefusesNonLoopbackMongoHost()
    {
        var nonLoopbackServices = new ServiceCollection();
        nonLoopbackServices.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        nonLoopbackServices.AddRoleRadarApplication();
        nonLoopbackServices.AddRoleRadarInfrastructure(opts =>
        {
            opts.ConnectionString = "mongodb://remote-db.production.internal:27017/?replicaSet=rs0";
            opts.DatabaseName = "roleradar";
        });

        await using var provider = nonLoopbackServices.BuildServiceProvider();
        var backupService = provider.GetRequiredService<IBackupArchiveService>();

        var result = await backupService.WipeDatabaseAsync(WipeCommand.ConfirmationToken);
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, result.Error?.Code);
        Assert.Contains("loopback", result.Error?.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EndToEnd_BackupWipeRestore_FullFidelityRoundTrip()
    {
        await using var provider = BuildServiceProvider();

        var createHandler = provider.GetRequiredService<ICommandHandler<CreateNoteCommand, NoteCreatedDto>>();
        var createResult = await createHandler.HandleAsync(new CreateNoteCommand(
            Title: "Disaster Recovery Test Note",
            Body: "# Section 1\nContent to be restored.",
            Area: "primary",
            Tags: null,
            ProvenanceKind: "stated",
            ProvenanceDate: "2026-10-17",
            ProvenanceQualifier: null,
            Actor: "test-user",
            OwnerId: "user-123"));
        Assert.True(createResult.IsSuccess);

        var backupService = provider.GetRequiredService<IBackupArchiveService>();
        var backupFile = Path.Combine(Path.GetTempPath(), $"dr_test_{Guid.NewGuid():N}.rrbak");

        try
        {
            // 1. Create backup
            const string passphrase = "CorrectHorseBatteryStaple123!";
            var backupResult = await backupService.CreateBackupAsync(backupFile, passphrase);
            Assert.True(backupResult.IsSuccess, $"Backup failed: {backupResult.Error?.Message}");
            Assert.True(File.Exists(backupFile));

            // 2. Wipe database
            var wipeResult = await backupService.WipeDatabaseAsync(WipeCommand.ConfirmationToken);
            Assert.True(wipeResult.IsSuccess, $"Wipe failed: {wipeResult.Error?.Message}");

            // Verify datastore is empty
            var mongoClient = provider.GetRequiredService<IMongoClient>();
            var db = mongoClient.GetDatabase(_databaseName);
            var notesCol = db.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.NotesCollectionName);
            var countAfterWipe = await notesCol.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);
            Assert.Equal(0, countAfterWipe);

            // 3. Restore from backup
            var restoreResult = await backupService.RestoreBackupAsync(backupFile, passphrase);
            Assert.True(restoreResult.IsSuccess, $"Restore failed: {restoreResult.Error?.Message}");

            // 4. Verify 100% document fidelity
            var countAfterRestore = await notesCol.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);
            Assert.Equal(1, countAfterRestore);

            var restoredDoc = await notesCol.Find(Builders<BsonDocument>.Filter.Eq("title", "Disaster Recovery Test Note")).FirstOrDefaultAsync();
            Assert.NotNull(restoredDoc);
            Assert.Contains("Section 1", restoredDoc["body"].AsString);

            // 5. Verify indexes exist
            using var indexCursor = await notesCol.Indexes.ListAsync();
            var indexDocs = await indexCursor.ToListAsync();
            Assert.Contains(indexDocs, idx => idx["name"].AsString == "ux_notes_owner_import_key");
            Assert.Contains(indexDocs, idx => idx["name"].AsString == "ix_notes_fulltext");
        }
        finally
        {
            if (File.Exists(backupFile)) File.Delete(backupFile);
        }
    }
}
