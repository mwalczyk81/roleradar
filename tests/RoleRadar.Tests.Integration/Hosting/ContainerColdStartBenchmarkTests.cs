using Xunit;

namespace RoleRadar.Tests.Integration.Hosting;

public sealed class ContainerColdStartBenchmarkTests
{
    [Fact]
    public async Task McpColdStart_UnderContainerHarness_RespondsUnder3Seconds()
    {
        await ContainerTestHarness.EnsureImagesBuiltAsync();

        // Standard MCP initialize frame
        const string initializeRpc = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2024-11-05\",\"capabilities\":{},\"clientInfo\":{\"name\":\"benchmark-client\",\"version\":\"1.0\"}}}\n";

        var result = await ContainerTestHarness.RunMcpAsync(initializeRpc, timeoutSeconds: 10);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("\"jsonrpc\":\"2.0\"", result.StandardOutput);
        Assert.True(
            result.Duration < TimeSpan.FromSeconds(3.0),
            $"Cold start duration {result.Duration.TotalSeconds:F2}s exceeded the strict 3.0s ceiling (SC-003).");
    }
}
