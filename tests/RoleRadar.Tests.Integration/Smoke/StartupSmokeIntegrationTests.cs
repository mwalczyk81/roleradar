using System.Diagnostics;
using RoleRadar.Tests.Integration.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Integration.Smoke;

[Collection(SharedMongoCollectionDefinition.Name)]
public sealed class StartupSmokeIntegrationTests
{
    private readonly SharedMongoFixture _sharedMongo;

    public StartupSmokeIntegrationTests(SharedMongoFixture sharedMongo)
    {
        ArgumentNullException.ThrowIfNull(sharedMongo);
        _sharedMongo = sharedMongo;
    }

    [Fact]
    public async Task Cli_StartupSmoke_RunsBuiltDll_StatusReturnsHealthyWithoutUnhandledErrors()
    {
        var cliDllPath = typeof(RoleRadar.Cli.Program).Assembly.Location;
        if (string.IsNullOrWhiteSpace(cliDllPath) || !File.Exists(cliDllPath))
        {
            cliDllPath = Path.Combine(AppContext.BaseDirectory, "RoleRadar.Cli.dll");
        }

        Assert.True(File.Exists(cliDllPath), $"Expected built CLI assembly at '{cliDllPath}'.");

        var uniqueDb = _sharedMongo.CreateDatabaseName("roleradar_smoke_cli");
        var env = _sharedMongo.CreateProcessEnvironment(uniqueDb);

        var (exitCode, stdout, stderr) = await RunProcessAsync(
            "dotnet",
            $"\"{cliDllPath}\" status",
            env);

        Assert.DoesNotContain("Unhandled exception", stderr, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, exitCode);
        Assert.Contains("RoleRadar Store: Ready", stdout, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task McpServer_StartupSmoke_RunsBuiltDll_InitializesAndListsToolsWithoutUnhandledErrors()
    {
        var mcpDllPath = typeof(RoleRadar.Mcp.Stdio.Program).Assembly.Location;
        if (string.IsNullOrWhiteSpace(mcpDllPath) || !File.Exists(mcpDllPath))
        {
            mcpDllPath = Path.Combine(AppContext.BaseDirectory, "RoleRadar.Mcp.Stdio.dll");
        }

        Assert.True(File.Exists(mcpDllPath), $"Expected built MCP server assembly at '{mcpDllPath}'.");

        var uniqueDb = _sharedMongo.CreateDatabaseName("roleradar_smoke_mcp");
        var env = _sharedMongo.CreateProcessEnvironment(uniqueDb);

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

        // 1. Send initialize
        await proc.StandardInput.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2024-11-05\",\"capabilities\":{},\"clientInfo\":{\"name\":\"smoke-test\",\"version\":\"1.0.0\"}}}");
        await proc.StandardInput.FlushAsync();

        var initResponse = await proc.StandardOutput.ReadLineAsync();

        // 2. Send notifications/initialized
        await proc.StandardInput.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
        await proc.StandardInput.FlushAsync();

        // 3. Send tools/list
        await proc.StandardInput.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}");
        await proc.StandardInput.FlushAsync();

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
        Assert.Contains("company_record", toolsResponse);
        Assert.Contains("posting_record", toolsResponse);
        Assert.Contains("posting_transition", toolsResponse);
        Assert.Contains("tracker_query", toolsResponse);
        Assert.Contains("tracker_log", toolsResponse);
        Assert.Contains("application_record", toolsResponse);
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunProcessAsync(
        string fileName,
        string arguments,
        IReadOnlyDictionary<string, string>? environmentVariables = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
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
