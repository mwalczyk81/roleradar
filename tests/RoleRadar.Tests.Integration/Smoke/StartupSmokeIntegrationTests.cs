using System.Diagnostics;
using RoleRadar.Tests.Integration.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Integration.Smoke;

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

        var (exitCode, stdout, stderr) = await RoleRadar.Tests.Integration.Hosting.LaunchedCliRunner.RunCliAsync(
            "status",
            env);

        Assert.DoesNotContain("Unhandled exception", stderr, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, exitCode);
        Assert.Contains("RoleRadar Store: Ready", stdout, StringComparison.OrdinalIgnoreCase);

        // Also smoke test contact command
        var (contactAddCode, contactAddOut, contactAddErr) = await RoleRadar.Tests.Integration.Hosting.LaunchedCliRunner.RunCliAsync(
            "contact add --name \"Smoke Contact\" --role \"Smoke Tester\" --json",
            env);
        Assert.Equal(0, contactAddCode);
        Assert.DoesNotContain("Unhandled exception", contactAddErr, StringComparison.OrdinalIgnoreCase);
        using var doc = System.Text.Json.JsonDocument.Parse(contactAddOut);
        var contactId = doc.RootElement.GetProperty("data").GetProperty("id").GetString()!;

        var (contactGetCode, contactGetOut, contactGetErr) = await RoleRadar.Tests.Integration.Hosting.LaunchedCliRunner.RunCliAsync(
            $"contact get --id {contactId} --json",
            env);
        Assert.Equal(0, contactGetCode);
        Assert.DoesNotContain("Unhandled exception", contactGetErr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Smoke Contact", contactGetOut);

        var (contactRedactCode, contactRedactOut, contactRedactErr) = await RoleRadar.Tests.Integration.Hosting.LaunchedCliRunner.RunCliAsync(
            $"contact redact --id {contactId} --force --json",
            env);
        Assert.Equal(0, contactRedactCode);
        Assert.DoesNotContain("Unhandled exception", contactRedactErr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("redacted", contactRedactOut);

        // Also smoke test verify command
        var (verifyCode, verifyOut, verifyErr) = await RoleRadar.Tests.Integration.Hosting.LaunchedCliRunner.RunCliAsync(
            "verify --json",
            env);
        Assert.Equal(0, verifyCode);
        Assert.DoesNotContain("Unhandled exception", verifyErr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"anomalyCount\": 0", verifyOut);
    }

    [Fact]
    public async Task McpServer_StartupSmoke_RunsBuiltDll_InitializesAndListsToolsWithoutUnhandledErrors()
    {
        var mcpDllPath = typeof(Mcp.Stdio.Program).Assembly.Location;
        if (string.IsNullOrWhiteSpace(mcpDllPath) || !File.Exists(mcpDllPath))
        {
            mcpDllPath = Path.Combine(AppContext.BaseDirectory, "RoleRadar.Mcp.Stdio.dll");
        }

        Assert.True(File.Exists(mcpDllPath), $"Expected built MCP server assembly at '{mcpDllPath}'.");

        var uniqueDb = _sharedMongo.CreateDatabaseName("roleradar_smoke_mcp");
        var env = _sharedMongo.CreateProcessEnvironment(uniqueDb);

        await using var client = await RoleRadar.Tests.Integration.Tracking.McpProcessClient.StartAsync(mcpDllPath, env);
        var toolsElement = await client.ListToolsAsync();
        var stderr = client.GetStderr();

        Assert.DoesNotContain("Unhandled exception", stderr, StringComparison.OrdinalIgnoreCase);
        var toolsArray = toolsElement.GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()!).ToList();
        Assert.Contains("note_create", toolsArray);
        Assert.Contains("note_archive", toolsArray);
        Assert.Contains("company_record", toolsArray);
        Assert.Contains("posting_record", toolsArray);
        Assert.Contains("posting_transition", toolsArray);
        Assert.Contains("tracker_query", toolsArray);
        Assert.Contains("tracker_log", toolsArray);
        Assert.Contains("application_record", toolsArray);
    }
}
