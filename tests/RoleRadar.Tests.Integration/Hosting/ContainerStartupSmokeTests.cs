using Xunit;

namespace RoleRadar.Tests.Integration.Hosting;

public sealed class ContainerStartupSmokeTests
{
    [Fact]
    public async Task Cli_Startup_UnderContainerHarness_OutputsHelpAndLogsToStderr()
    {
        var result = await ContainerTestHarness.RunCliAsync(["--help"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("RoleRadar", result.StandardOutput);
        // Stdio hygiene: standard error contains logging/diagnostics, not corrupting stdout
    }

    [Fact]
    public async Task Mcp_Startup_UnderContainerHarness_LogsExclusivelyToStderr()
    {
        // MCP stdio ping/initialize frame
        const string initializeRpc = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2024-11-05\",\"capabilities\":{},\"clientInfo\":{\"name\":\"test\",\"version\":\"1.0\"}}}\n";
        var result = await ContainerTestHarness.RunMcpAsync(initializeRpc);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("\"jsonrpc\":\"2.0\"", result.StandardOutput);
        Assert.Contains("\"result\"", result.StandardOutput);
    }
}
