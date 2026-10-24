using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
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
using Xunit;

namespace RoleRadar.Tests.Integration.Backup;

[Collection("IsolatedComposeCommandsCollection")]
public sealed class IsolatedComposeDefaultCommandsIntegrationTests
{
    [Fact]
    [Trait("Category", "Slow")]
    public async Task BackupRestoreWipe_WithDefaultComposeCommands_ReturnsIdenticalCounts()
    {
        if (!IsDockerCliAvailable())
        {
            Assert.Skip("Docker CLI is unavailable.");
        }

        var freePort = GetFreePort();
        var testId = Guid.CreateVersion7().ToString("N")[..12];
        var containerName = $"roleradar-mongo-test-{testId}";
        var volumeName = $"roleradar-mongo-data-test-{testId}";
        var projectName = $"roleradar-test-{testId}";

        var originalComposePath = BackupOptions.ResolveDefaultComposePath();
        var composeYaml = await File.ReadAllTextAsync(originalComposePath);

        var customYaml = $"name: {projectName}\n" + composeYaml
            .Replace("container_name: roleradar-mongo", $"container_name: {containerName}")
            .Replace("\"127.0.0.1:27017:27017\"", $"\"127.0.0.1:{freePort}:27017\"")
            .Replace("roleradar-mongo-data:", $"{volumeName}:")
            .Replace("name: roleradar-mongo-data", $"name: {volumeName}");

        var tempDir = Path.Combine(Path.GetTempPath(), $"rr_compose_{testId}");
        Directory.CreateDirectory(tempDir);
        var tempComposePath = Path.Combine(tempDir, "docker-compose.yml");
        await File.WriteAllTextAsync(tempComposePath, customYaml);

        var prevComposeEnv = Environment.GetEnvironmentVariable("ROLERADAR_COMPOSE_FILE");
        Environment.SetEnvironmentVariable("ROLERADAR_COMPOSE_FILE", tempComposePath);

        var composeConnString = $"mongodb://127.0.0.1:{freePort}/?replicaSet=rs0&directConnection=true";
        var composeDbName = $"roleradar_defaults_test_{testId}";

        try
        {
            await RunProcessAsync("docker", $"compose -f \"{tempComposePath}\" up -d --wait", timeoutSeconds: 90);

            var services = new ServiceCollection();
            services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
            services.AddRoleRadarApplication();
            services.AddRoleRadarInfrastructure(opts =>
            {
                opts.ConnectionString = composeConnString;
                opts.DatabaseName = composeDbName;
                opts.ReadinessTimeout = TimeSpan.FromSeconds(30);
            });

            // DO NOT set opts.DumpCommand or opts.RestoreCommand - verify defaults!
            await using var provider = services.BuildServiceProvider();

            // Wait for replica set election to complete and report writable primary
            var probe = provider.GetRequiredService<MongoReadinessProbe>();
            var ready = false;
            for (var attempt = 0; attempt < 30; attempt++)
            {
                var check = await probe.CheckAsync();
                if (check.IsSuccess && check.Value?.IsReady == true)
                {
                    ready = true;
                    break;
                }
                await Task.Delay(500);
            }
            Assert.True(ready, "Replica set did not achieve writable primary status within 15 seconds.");

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
            Assert.True(createResult.IsSuccess, createResult.Error?.Message);

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
                if (File.Exists(backupFile))
                {
                    File.Delete(backupFile);
                }
            }
        }
        finally
        {
            try
            {
                await RunProcessAsync("docker", $"compose -f \"{tempComposePath}\" down -v", timeoutSeconds: 30);
            }
            catch { }

            Environment.SetEnvironmentVariable("ROLERADAR_COMPOSE_FILE", prevComposeEnv);

            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
            catch { }
        }
    }

    private static bool IsDockerCliAvailable()
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (proc is null)
            {
                return false;
            }

            proc.WaitForExit(3000);
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task RunProcessAsync(string fileName, string arguments, int timeoutSeconds)
    {
        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        proc.Start();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            await proc.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"Process '{fileName} {arguments}' timed out after {timeoutSeconds}s.");
        }

        if (proc.ExitCode != 0)
        {
            var stderr = await proc.StandardError.ReadToEndAsync();
            var stdout = await proc.StandardOutput.ReadToEndAsync();
            throw new InvalidOperationException($"Process '{fileName} {arguments}' failed with exit code {proc.ExitCode}.\nStdErr: {stderr}\nStdOut: {stdout}");
        }
    }
}
