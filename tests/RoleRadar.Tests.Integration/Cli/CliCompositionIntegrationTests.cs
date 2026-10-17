using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Import.Commands;
using RoleRadar.Application.Notes.Commands;
using RoleRadar.Cli;
using RoleRadar.Cli.Commands;
using RoleRadar.Infrastructure.Backup;
using Xunit;

namespace RoleRadar.Tests.Integration.Cli;

[Collection(Fixtures.SharedMongoCollectionDefinition.Name)]
public sealed class CliCompositionIntegrationTests
{
    private readonly Fixtures.SharedMongoFixture _sharedMongo;

    public CliCompositionIntegrationTests(Fixtures.SharedMongoFixture sharedMongo)
    {
        ArgumentNullException.ThrowIfNull(sharedMongo);
        _sharedMongo = sharedMongo;
    }

    [Fact]
    public void Program_BuildServiceProvider_ResolvesAllCliCommandsAndLogging()
    {
        using var provider = Program.BuildServiceProvider(services =>
        {
            // Inject dummy connection string if none is in environment so infrastructure starts
            services.PostConfigure<Infrastructure.Mongo.MongoOptions>(opt =>
            {
                if (string.IsNullOrWhiteSpace(opt.ConnectionString))
                {
                    opt.ConnectionString = "mongodb://127.0.0.1:27017";
                }
            });
        });

        // 1. Logging provider must be registered
        var loggerFactory = provider.GetService<ILoggerFactory>();
        Assert.NotNull(loggerFactory);

        var logger = provider.GetService<ILogger<ImportMarkdownCommandHandler>>();
        Assert.NotNull(logger);

        // 2. Services and handlers must resolve cleanly
        var backupService = provider.GetService<IBackupArchiveService>();
        Assert.NotNull(backupService);

        var noteRepo = provider.GetService<INoteRepository>();
        Assert.NotNull(noteRepo);

        var importHandler = provider.GetService<ICommandHandler<ImportMarkdownCommand, ImportMarkdownSummaryDto>>();
        Assert.NotNull(importHandler);

        // 3. System.CommandLine command trees must be constructable without throwing
        var importCmd = ImportCommand.Create(provider);
        Assert.NotNull(importCmd);

        var backupCmd = BackupCommand.Create(provider);
        Assert.NotNull(backupCmd);

        var restoreCmd = RestoreCommand.Create(provider);
        Assert.NotNull(restoreCmd);

        var wipeCmd = WipeCommand.Create(provider);
        Assert.NotNull(wipeCmd);
    }

    [Fact]
    public void Program_EnvironmentVariables_OverrideBackupOptions()
    {
        const string customDump = "custom-mongodump --flag";
        const string customRestore = "custom-mongorestore --flag";

        Environment.SetEnvironmentVariable("ROLERADAR_BACKUP_DUMP_COMMAND", customDump);
        Environment.SetEnvironmentVariable("ROLERADAR_BACKUP_RESTORE_COMMAND", customRestore);

        try
        {
            using var provider = Program.BuildServiceProvider();
            var backupOptions = provider.GetRequiredService<IOptions<BackupOptions>>().Value;

            Assert.Equal(customDump, backupOptions.DumpCommand);
            Assert.Equal(customRestore, backupOptions.RestoreCommand);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ROLERADAR_BACKUP_DUMP_COMMAND", null);
            Environment.SetEnvironmentVariable("ROLERADAR_BACKUP_RESTORE_COMMAND", null);
        }
    }

    [Fact]
    public async Task Cli_SmokeTest_RunsBuiltDll_AssertsExitCodesAndNoUnhandledExceptions()
    {
        var cliDllPath = typeof(Program).Assembly.Location;
        if (string.IsNullOrWhiteSpace(cliDllPath) || !File.Exists(cliDllPath))
        {
            cliDllPath = Path.Combine(AppContext.BaseDirectory, "RoleRadar.Cli.dll");
        }

        Assert.True(File.Exists(cliDllPath), $"Expected built CLI assembly at '{cliDllPath}'.");

        // 1. import --dry-run on a valid empty/minimal markdown file
        var tempFile = Path.GetTempFileName();
        var uniqueDb = $"roleradar_cli_smoke_{Guid.CreateVersion7():N}";
        var env = new Dictionary<string, string>
        {
            ["ROLERADAR_MONGO_CONNECTION_STRING"] = _sharedMongo.ConnectionString,
            ["ROLERADAR_MONGO_DATABASE"] = uniqueDb
        };

        try
        {
            await File.WriteAllTextAsync(tempFile, "# Test Heading\nTest body content.");

            var (exitCodeImport, _, stderrImport) = await RunCliAsync(cliDllPath, $"import \"{tempFile}\" --dry-run", env);
            Assert.DoesNotContain("Unhandled exception", stderrImport, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, exitCodeImport);

            // 2. backup missing passphrase or arguments
            var (exitCodeBackup, _, stderrBackup) = await RunCliAsync(cliDllPath, "backup", env);
            Assert.DoesNotContain("Unhandled exception", stderrBackup, StringComparison.OrdinalIgnoreCase);
            Assert.NotEqual(0, exitCodeBackup);

            // 3. wipe without --confirm
            var (exitCodeWipe, _, stderrWipe) = await RunCliAsync(cliDllPath, "wipe", env);
            Assert.DoesNotContain("Unhandled exception", stderrWipe, StringComparison.OrdinalIgnoreCase);
            Assert.NotEqual(0, exitCodeWipe);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunCliAsync(
        string dllPath,
        string arguments,
        IReadOnlyDictionary<string, string>? environmentVariables = null)
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

        if (environmentVariables is not null)
        {
            foreach (var (k, v) in environmentVariables)
            {
                psi.Environment[k] = v;
            }
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
