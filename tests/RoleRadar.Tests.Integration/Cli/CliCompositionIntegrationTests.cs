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
                    opt.ConnectionString = _sharedMongo.ConnectionString;
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

        var archiveHandler = provider.GetService<ICommandHandler<ArchiveNoteCommand, NoteArchivedDto>>();
        Assert.NotNull(archiveHandler);

        // 3. System.CommandLine command trees must be constructable without throwing
        var statusCmd = StatusCommand.Create(provider);
        Assert.NotNull(statusCmd);

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

            // 0. status check with running container
            var (exitCodeStatus, stdoutStatus, stderrStatus) = await RunCliAsync(cliDllPath, "status", env);
            Assert.DoesNotContain("Unhandled exception", stderrStatus, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("info:", stderrStatus, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, exitCodeStatus);
            Assert.Contains("RoleRadar Store: Ready", stdoutStatus);

            var (exitCodeStatusJson, stdoutStatusJson, stderrStatusJson) = await RunCliAsync(cliDllPath, "status --json", env);
            Assert.Equal(0, exitCodeStatusJson);
            Assert.DoesNotContain("info:", stderrStatusJson, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"isReady\": true", stdoutStatusJson);

            // 1. import --dry-run on a valid empty/minimal markdown file
            var (exitCodeImport, _, stderrImport) = await RunCliAsync(cliDllPath, $"import \"{tempFile}\" --dry-run", env);
            Assert.DoesNotContain("Unhandled exception", stderrImport, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("info:", stderrImport, StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public async Task McpServer_SmokeTest_RunsBuiltDll_InitializesAndListsToolsWithoutUnhandledErrors()
    {
        var mcpDllPath = typeof(Mcp.Stdio.Program).Assembly.Location;
        if (string.IsNullOrWhiteSpace(mcpDllPath) || !File.Exists(mcpDllPath))
        {
            mcpDllPath = Path.Combine(AppContext.BaseDirectory, "RoleRadar.Mcp.Stdio.dll");
        }

        Assert.True(File.Exists(mcpDllPath), $"Expected built MCP server assembly at '{mcpDllPath}'.");

        var uniqueDb = $"roleradar_mcp_smoke_{Guid.CreateVersion7():N}";
        var env = new Dictionary<string, string>
        {
            ["ROLERADAR_MONGO_CONNECTION_STRING"] = _sharedMongo.ConnectionString,
            ["ROLERADAR_MONGO_DATABASE"] = uniqueDb
        };

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{mcpDllPath}\"",
            RedirectStandardInput = true,
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

        // Send initialize
        await proc.StandardInput.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2024-11-05\",\"capabilities\":{},\"clientInfo\":{\"name\":\"test-client\",\"version\":\"1.0.0\"}}}");
        await proc.StandardInput.FlushAsync();

        // Read response to initialize
        var initResponse = await proc.StandardOutput.ReadLineAsync();

        // Send notifications/initialized
        await proc.StandardInput.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
        await proc.StandardInput.FlushAsync();

        // Send tools/list
        await proc.StandardInput.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}");
        await proc.StandardInput.FlushAsync();

        // Read response to tools/list
        var toolsResponse = await proc.StandardOutput.ReadLineAsync();

        proc.StandardInput.Close();
        await proc.WaitForExitAsync();

        var stderr = await proc.StandardError.ReadToEndAsync();

        Assert.DoesNotContain("Unhandled exception", stderr, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(initResponse);
        Assert.Contains("\"result\":", initResponse);
        Assert.NotNull(toolsResponse);
        Assert.Contains("note_create", toolsResponse);
        Assert.Contains("note_archive", toolsResponse);
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
