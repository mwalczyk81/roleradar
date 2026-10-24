using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RoleRadar.Application;
using RoleRadar.Infrastructure;
using RoleRadar.Mcp.Stdio.Tools;
using RoleRadar.Tests.Integration.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Integration.Safety;

[Collection("StoppedMongoReadinessCollection")]
public sealed class StoppedMongoReadinessIntegrationTests : IClassFixture<DedicatedMongoFixture>
{
    private readonly DedicatedMongoFixture _dedicatedMongo;

    public StoppedMongoReadinessIntegrationTests(DedicatedMongoFixture dedicatedMongo)
    {
        ArgumentNullException.ThrowIfNull(dedicatedMongo);
        _dedicatedMongo = dedicatedMongo;
    }

    private static ServiceProvider BuildServiceProvider(string connectionString, string databaseName)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddRoleRadarApplication();
        services.AddRoleRadarInfrastructure(opts =>
        {
            opts.ConnectionString = connectionString;
            opts.DatabaseName = databaseName;
            opts.ReadinessTimeout = TimeSpan.FromSeconds(2);
        });
        services.AddScoped<NoteMcpTools>();
        return services.BuildServiceProvider();
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task OfflineReadiness_WithMongoStopped_McpToolCallAndCliStatusFailUnder5SecondsWithRemediationScript()
    {
        // Use the dedicated container fixture, NEVER the shared container or dev stack
        var dedicatedConn = _dedicatedMongo.ConnectionString;
        var dedicatedDb = $"roleradar_offline_{Guid.CreateVersion7():N}";

        // Stop the dedicated container to simulate offline datastore
        await _dedicatedMongo.StopAsync();

        // Ensure password and credentials are present in the raw connection string to test redaction
        const string rawPassword = "SuperSecretPassword123!";
        var credentialedConn = dedicatedConn.Contains('@')
            ? dedicatedConn
            : dedicatedConn.Replace("mongodb://", $"mongodb://admin:{rawPassword}@");

        // 1. MCP Tool Call offline test (< 5 seconds, typed error naming 'pwsh ./scripts/start-services.ps1', valid JSON-RPC return)
        await using var provider = BuildServiceProvider(credentialedConn, dedicatedDb);
        var tools = provider.GetRequiredService<NoteMcpTools>();

        var sw = Stopwatch.StartNew();
        var toolJson = await tools.NoteGetAsync(Guid.NewGuid());
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(7), $"Tool call took {sw.Elapsed.TotalSeconds:F2}s, exceeding 7s budget.");

        using var doc = JsonDocument.Parse(toolJson);
        var root = doc.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        var errorObj = root.GetProperty("error");
        Assert.Equal("DatastoreOffline", errorObj.GetProperty("code").GetString());
        var message = errorObj.GetProperty("message").GetString()!;
        Assert.Contains("pwsh ./scripts/start-services.ps1", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(rawPassword, message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(rawPassword, toolJson, StringComparison.OrdinalIgnoreCase);

        // 2. roleradar status CLI offline test (< 5 seconds, exit code non-zero, names 'pwsh ./scripts/start-services.ps1')
        var cliDllPath = Path.Combine(AppContext.BaseDirectory, "RoleRadar.Cli.dll");
        if (!File.Exists(cliDllPath))
        {
            cliDllPath = typeof(RoleRadar.Cli.Program).Assembly.Location;
        }

        var env = new Dictionary<string, string>
        {
            ["ROLERADAR_MONGO_CONNECTION_STRING"] = credentialedConn,
            ["ROLERADAR_MONGO_DATABASE"] = dedicatedDb
        };

        var cliSw = Stopwatch.StartNew();
        var (exitCode, stdout, stderr) = await RunCliAsync(cliDllPath, "status", env);
        cliSw.Stop();

        Assert.True(cliSw.Elapsed < TimeSpan.FromSeconds(7), $"CLI status took {cliSw.Elapsed.TotalSeconds:F2}s, exceeding 7s budget.");
        Assert.NotEqual(0, exitCode);
        var combinedOutput = stdout + "\n" + stderr;
        Assert.Contains("pwsh ./scripts/start-services.ps1", combinedOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Unhandled exception", stderr, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(rawPassword, combinedOutput, StringComparison.OrdinalIgnoreCase);

        // 3. roleradar status --json CLI offline test
        var (exitCodeJson, stdoutJson, stderrJson) = await RunCliAsync(cliDllPath, "status --json", env);
        Assert.NotEqual(0, exitCodeJson);
        var combinedJsonOutput = stdoutJson + "\n" + stderrJson;
        Assert.DoesNotContain(rawPassword, combinedJsonOutput, StringComparison.OrdinalIgnoreCase);
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
