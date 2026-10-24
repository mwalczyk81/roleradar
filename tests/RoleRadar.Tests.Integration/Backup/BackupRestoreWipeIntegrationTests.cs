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

[Collection("BackupRestoreWipeCollection")]
public sealed class BackupRestoreWipeIntegrationTests : IClassFixture<BackupIntegrationMongoFixture>
{
    private readonly BackupIntegrationMongoFixture _fixture;
    private readonly string _connectionString;
    private string _databaseName = string.Empty;

    public BackupRestoreWipeIntegrationTests(BackupIntegrationMongoFixture fixture)
    {
        _fixture = fixture;
        _connectionString = fixture.ConnectionString;
    }

    private async Task ResetDatabasesAsync()
    {
        await _fixture.DropNonSystemDatabasesAsync();
        _databaseName = $"roleradar_backup_test_{Guid.CreateVersion7():N}";
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
    public async Task HostToCompose_Connectivity_EvaluatesReplicaSetAndDirectConnection()
    {
        await ResetDatabasesAsync();

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
        await ResetDatabasesAsync();

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

    [Theory]
    [InlineData("scripts/backup.ps1", "--passphrase hunter2")]
    [InlineData("scripts/backup.ps1", "-Passphrase hunter2")]
    [InlineData("scripts/backup.ps1", "--passphrase=hunter2")]
    [InlineData("scripts/restore.ps1", "--passphrase hunter2")]
    [InlineData("scripts/restore.ps1", "-Passphrase hunter2")]
    [InlineData("scripts/restore.ps1", "--passphrase=hunter2")]
    public async Task OperationalScripts_RejectPassphraseValueArguments(string scriptRelativePath, string args)
    {
        var repoRoot = FindRepoRoot();
        var scriptPath = Path.Combine(repoRoot, scriptRelativePath);
        Assert.True(File.Exists(scriptPath), $"Script not found at '{scriptPath}'.");

        var psi = new ProcessStartInfo
        {
            FileName = "pwsh",
            Arguments = $"-NoProfile -NonInteractive -File \"{scriptPath}\" {args}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi)!;
        Assert.NotNull(proc);

        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();

        await proc.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        var combined = stdout + "\n" + stderr;

        Assert.Equal(1, proc.ExitCode);
        Assert.DoesNotContain("hunter2", combined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Supplying passphrase via CLI argument is forbidden for security. Use interactive prompt, --passphrase-env, or --passphrase-stdin.",
            combined);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RoleRadar.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir.FullName;
    }

    [Fact]
    public async Task FileWatcher_TempAndTargetLocations_ZeroPlaintextFilesWritten()
    {
        await ResetDatabasesAsync();

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
        await ResetDatabasesAsync();

        await using var provider = BuildServiceProvider(opts =>
        {
            opts.DumpCommand = $"docker exec -i {_fixture.ContainerId} mongodump --archive --gzip --oplog";
            opts.RestoreCommand = $"docker exec -i {_fixture.ContainerId} mongorestore --archive --gzip --nsInclude \"{_databaseName}.*\"";
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
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task Restore_IntoNonEmptyDatastore_RefusesWithoutForceOverwrite()
    {
        await ResetDatabasesAsync();

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
        await ResetDatabasesAsync();

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
        await ResetDatabasesAsync();

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
        await ResetDatabasesAsync();

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
            if (File.Exists(backupFile))
            {
                File.Delete(backupFile);
            }
        }
    }

    [Fact]
    public async Task CreateBackup_WhenDumpProcessFails_DeletesPartialFileAndLeavesNoArtifacts()
    {
        await ResetDatabasesAsync();

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
            if (Directory.Exists(targetDir))
            {
                Directory.Delete(targetDir, true);
            }
        }
    }

    [Fact]
    public async Task CreateBackup_WhenCancelled_DeletesPartialFileAndLeavesNoArtifacts()
    {
        await ResetDatabasesAsync();

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
            if (Directory.Exists(targetDir))
            {
                Directory.Delete(targetDir, true);
            }
        }
    }

    [Fact]
    public async Task CreateBackup_WhenStreamIsEmptyOrInvalidMagic_DeletesPartialFileAndFails()
    {
        await ResetDatabasesAsync();

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
            if (Directory.Exists(targetDir))
            {
                Directory.Delete(targetDir, true);
            }
        }
    }

    [Fact]
    public async Task Backup_WhenCancelled_TerminatesEntireProcessTree()
    {
        await ResetDatabasesAsync();

        var targetDir = Path.Combine(Path.GetTempPath(), $"rb_tree_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(targetDir);
        var targetFile = Path.Combine(targetDir, "tree_backup.rrbak");
        var pidFile = Path.Combine(targetDir, "child_pid.txt");

        // Spawn python or powershell script that writes child PID and sleeps
        var script = $"import os, time, sys; sys.stdout.write('x'); sys.stdout.flush(); f = open(r'{pidFile}', 'w'); f.write(str(os.getpid())); f.close(); time.sleep(60)";

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
            if (Directory.Exists(targetDir))
            {
                Directory.Delete(targetDir, true);
            }
        }
    }

    [Fact]
    public async Task Restore_WithForceOverwrite_DoesNotDropOrAlterSecondDatabase()
    {
        await ResetDatabasesAsync();

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

        var backupFile = Path.Combine(Path.GetTempPath(), $"scoped_test_{Guid.NewGuid():N}.rrbak");

        try
        {
            var backupService = provider.GetRequiredService<IBackupArchiveService>();
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
            if (File.Exists(backupFile))
            {
                File.Delete(backupFile);
            }

            Hosting.ContainerTestHarness.ValidateDatabaseNameForDrop(dbBName);
            await mongoClient.DropDatabaseAsync(dbBName);
        }
    }
}
