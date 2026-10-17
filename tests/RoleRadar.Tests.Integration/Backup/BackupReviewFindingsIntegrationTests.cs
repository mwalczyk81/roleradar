using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Application;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Notes.Commands;
using RoleRadar.Cli.Commands;
using RoleRadar.Infrastructure;
using RoleRadar.Infrastructure.Backup;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Tests.Integration.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Integration.Backup;

public sealed class BackupReviewFindingsIntegrationTests : IClassFixture<BackupIntegrationMongoFixture>
{
    private readonly BackupIntegrationMongoFixture _fixture;
    private readonly string _connectionString;
    private readonly string _databaseName;

    public BackupReviewFindingsIntegrationTests(BackupIntegrationMongoFixture fixture)
    {
        _fixture = fixture;
        _connectionString = fixture.ConnectionString;
        _databaseName = $"roleradar_findings_test_{Guid.CreateVersion7():N}";
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

    [Fact]
    public async Task CreateBackup_WhenDumpProcessFails_DeletesPartialFileAndLeavesNoArtifacts()
    {
        await _fixture.DropNonSystemDatabasesAsync();

        var targetDir = Path.Combine(Path.GetTempPath(), $"rb_fail_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(targetDir);
        var targetFile = Path.Combine(targetDir, "failing_backup.rrbak");

        await using var provider = BuildServiceProvider(opts =>
        {
            // Command outputs some bytes then exits with code 1
            opts.DumpCommand = "cmd.exe /c \"echo corrupt payload && exit 1\"";
        });

        try
        {
            var backupService = provider.GetRequiredService<IBackupArchiveService>();
            var result = await backupService.CreateBackupAsync(targetFile, "Passphrase123!");

            Assert.False(result.IsSuccess);
            Assert.Empty(Directory.GetFiles(targetDir, "*.partial"));
            Assert.Empty(Directory.GetFiles(targetDir, "*.rrbak"));
        }
        finally
        {
            if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true);
        }
    }

    [Fact]
    public async Task CreateBackup_WhenCancelled_DeletesPartialFileAndLeavesNoArtifacts()
    {
        await _fixture.DropNonSystemDatabasesAsync();

        var targetDir = Path.Combine(Path.GetTempPath(), $"rb_cancel_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(targetDir);
        var targetFile = Path.Combine(targetDir, "cancelled_backup.rrbak");

        await using var provider = BuildServiceProvider(opts =>
        {
            // Long running command
            opts.DumpCommand = "powershell -NoProfile -Command Start-Sleep -Seconds 60";
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        try
        {
            var backupService = provider.GetRequiredService<IBackupArchiveService>();
            try
            {
                var result = await backupService.CreateBackupAsync(targetFile, "Passphrase123!", cancellationToken: cts.Token);
                Assert.False(result.IsSuccess);
            }
            catch (OperationCanceledException)
            {
                // Expected when canceled
            }

            Assert.Empty(Directory.GetFiles(targetDir, "*.partial"));
            Assert.Empty(Directory.GetFiles(targetDir, "*.rrbak"));
        }
        finally
        {
            if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true);
        }
    }

    [Fact]
    public async Task CreateBackup_WhenStreamIsEmptyOrInvalidMagic_DeletesPartialFileAndFails()
    {
        await _fixture.DropNonSystemDatabasesAsync();

        var targetDir = Path.Combine(Path.GetTempPath(), $"rb_bad_magic_{Guid.NewGuid():N}");
        Directory.CreateDirectory(targetDir);
        var targetFile = Path.Combine(targetDir, "bad_magic_backup.rrbak");

        await using var provider = BuildServiceProvider(opts =>
        {
            // Outputs text string that is neither GZIP nor 0x6de29945 magic bytes
            opts.DumpCommand = "cmd.exe /c \"echo not a valid archive\"";
        });

        try
        {
            var backupService = provider.GetRequiredService<IBackupArchiveService>();
            var result = await backupService.CreateBackupAsync(targetFile, "Passphrase123!");

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCode.ValidationError, result.Error?.Code);
            Assert.Empty(Directory.GetFiles(targetDir, "*.partial"));
            Assert.Empty(Directory.GetFiles(targetDir, "*.rrbak"));
        }
        finally
        {
            if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true);
        }
    }

    [Fact]
    public async Task Backup_WhenCancelled_TerminatesEntireProcessTree()
    {
        await _fixture.DropNonSystemDatabasesAsync();

        var targetDir = Path.Combine(Path.GetTempPath(), $"rb_tree_kill_{Guid.NewGuid():N}");
        Directory.CreateDirectory(targetDir);
        var targetFile = Path.Combine(targetDir, "tree_kill_backup.rrbak");
        var pidFile = Path.Combine(targetDir, "child.pid");

        // Use python to spawn a long-sleeping grandchild process and record its PID
        var script = $"import subprocess, sys, time; p = subprocess.Popen([sys.executable, '-c', 'import time; time.sleep(60)']); open(r'{pidFile.Replace('\\', '/')}', 'w').write(str(p.pid)); p.wait()";
        await using var provider = BuildServiceProvider(opts =>
        {
            opts.DumpCommand = $"python -c \"{script}\"";
        });

        using var cts = new CancellationTokenSource();

        try
        {
            var backupService = provider.GetRequiredService<IBackupArchiveService>();
            var backupTask = backupService.CreateBackupAsync(targetFile, "Passphrase123!", cancellationToken: cts.Token);

            // Wait until child PID file is written
            var sw = Stopwatch.StartNew();
            while (!File.Exists(pidFile) && sw.ElapsedMilliseconds < 5000)
            {
                await Task.Delay(50);
            }

            Assert.True(File.Exists(pidFile), "Expected child PID file to be created by test process.");
            var childPid = int.Parse(await File.ReadAllTextAsync(pidFile), CultureInfo.InvariantCulture);

            // Verify child process is currently alive
            using var childProc = Process.GetProcessById(childPid);
            Assert.False(childProc.HasExited);

            // Cancel the backup operation
            cts.Cancel();

            try
            {
                await backupTask;
            }
            catch (OperationCanceledException)
            {
            }

            // Verify grandchild process has exited (process tree killed)
            var exited = childProc.WaitForExit(3000);
            Assert.True(exited, "Expected grandchild process to be terminated with entireProcessTree: true.");
            Assert.True(childProc.HasExited);
        }
        finally
        {
            if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true);
        }
    }

    [Fact]
    public async Task Restore_WithForceOverwrite_DoesNotDropOrAlterSecondDatabase()
    {
        await _fixture.DropNonSystemDatabasesAsync();

        var dbAName = _databaseName;
        var dbBName = $"{_databaseName}_db_b";

        var mongoClient = new MongoClient(_connectionString);
        var dbA = mongoClient.GetDatabase(dbAName);
        var dbB = mongoClient.GetDatabase(dbBName);

        var colA = dbA.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.NotesCollectionName);
        var colB = dbB.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.NotesCollectionName);

        // Insert initial notes into both DBs
        await colA.InsertOneAsync(new BsonDocument { { "title", "Note in DB A" }, { "body", "Body A" } });
        await colB.InsertOneAsync(new BsonDocument { { "title", "Untouched Note in DB B" }, { "body", "Secret Body B that must survive" } });

        await using var provider = BuildServiceProvider(opts =>
        {
            var mongoUrl = new MongoUrl(_connectionString);
            var authPart = (!string.IsNullOrEmpty(mongoUrl.Username) && !string.IsNullOrEmpty(mongoUrl.Password))
                ? $"{mongoUrl.Username}:{mongoUrl.Password}@"
                : string.Empty;
            var internalUri = $"mongodb://{authPart}localhost:27017/?replicaSet=rs0&authSource=admin";

            opts.DumpCommand = $"docker exec -i {_fixture.ContainerId} mongodump --uri=\"{internalUri}\" --archive --gzip --oplog";
            opts.RestoreCommand = $"docker exec -i {_fixture.ContainerId} mongorestore --uri=\"{internalUri}\" --archive --gzip --nsInclude \"{dbAName}.*\"";
        });

        var backupService = provider.GetRequiredService<IBackupArchiveService>();
        var backupFile = Path.Combine(Path.GetTempPath(), $"scoped_test_{Guid.NewGuid():N}.rrbak");

        try
        {
            // 1. Backup DB A
            var backupRes = await backupService.CreateBackupAsync(backupFile, "CorrectPassphrase123!");
            Assert.True(backupRes.IsSuccess);

            // 2. Mutate DB A
            await colA.InsertOneAsync(new BsonDocument { { "title", "Extra unbacked note in DB A" } });

            // 3. Restore DB A with forceOverwrite: true
            var restoreRes = await backupService.RestoreBackupAsync(backupFile, "CorrectPassphrase123!", forceOverwrite: true);
            Assert.True(restoreRes.IsSuccess, $"Restore failed: {restoreRes.Error?.Message}");

            // 4. Verify DB B was completely untouched
            var countB = await colB.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);
            Assert.Equal(1, countB);

            var docB = await colB.Find(FilterDefinition<BsonDocument>.Empty).FirstOrDefaultAsync();
            Assert.NotNull(docB);
            Assert.Equal("Untouched Note in DB B", docB["title"].AsString);
            Assert.Equal("Secret Body B that must survive", docB["body"].AsString);
        }
        finally
        {
            if (File.Exists(backupFile)) File.Delete(backupFile);
            await mongoClient.DropDatabaseAsync(dbBName);
        }
    }

    [Fact]
    public async Task BackupRestoreWipe_WithDefaultComposeCommands_ReturnsIdenticalCounts()
    {
        // Must run against real compose service with NO command overrides
        var composeDbName = $"roleradar_defaults_test_{Guid.CreateVersion7():N}";
        const string composeConnString = "mongodb://127.0.0.1:27017/?replicaSet=rs0&directConnection=true";

        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddRoleRadarApplication();
        services.AddRoleRadarInfrastructure(opts =>
        {
            opts.ConnectionString = composeConnString;
            opts.DatabaseName = composeDbName;
            opts.ReadinessTimeout = TimeSpan.FromSeconds(5);
        });

        // DO NOT set opts.DumpCommand or opts.RestoreCommand - use defaults!
        await using var provider = services.BuildServiceProvider();

        var createHandler = provider.GetRequiredService<ICommandHandler<CreateNoteCommand, NoteCreatedDto>>();
        var createResult = await createHandler.HandleAsync(new CreateNoteCommand(
            Title: "Defaults Roundtrip Note",
            Body: "# Default Note Body\nVerifying default compose commands roundtrip.",
            Area: "primary",
            Tags: null,
            ProvenanceKind: "stated",
            ProvenanceDate: "2026-10-17",
            ProvenanceQualifier: null,
            Actor: "test-user",
            OwnerId: "user-default"));
        Assert.True(createResult.IsSuccess);

        var backupService = provider.GetRequiredService<IBackupArchiveService>();
        var backupFile = Path.Combine(Path.GetTempPath(), $"defaults_test_{Guid.NewGuid():N}.rrbak");

        try
        {
            const string passphrase = "CorrectHorseBatteryStaple123!";
            var backupResult = await backupService.CreateBackupAsync(backupFile, passphrase);
            Assert.True(backupResult.IsSuccess, $"Backup with defaults failed: {backupResult.Error?.Message}");

            var wipeResult = await backupService.WipeDatabaseAsync(WipeCommand.ConfirmationToken);
            Assert.True(wipeResult.IsSuccess, $"Wipe failed: {wipeResult.Error?.Message}");

            var restoreResult = await backupService.RestoreBackupAsync(backupFile, passphrase);
            Assert.True(restoreResult.IsSuccess, $"Restore with defaults failed: {restoreResult.Error?.Message}");

            var mongoClient = provider.GetRequiredService<IMongoClient>();
            var db = mongoClient.GetDatabase(composeDbName);
            var notesCol = db.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.NotesCollectionName);
            var countAfter = await notesCol.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);
            Assert.Equal(1, countAfter);

            var note = await notesCol.Find(Builders<BsonDocument>.Filter.Eq("title", "Defaults Roundtrip Note")).FirstOrDefaultAsync();
            Assert.NotNull(note);
            Assert.Contains("Default Note Body", note["body"].AsString);
        }
        finally
        {
            if (File.Exists(backupFile)) File.Delete(backupFile);
            var client = new MongoClient(composeConnString);
            await client.DropDatabaseAsync(composeDbName);
        }
    }
}
