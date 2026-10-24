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

        const int runCount = 8;
        var samples = new List<double>(runCount);

        // Pre-warm Docker daemon runtime caches
        await ContainerTestHarness.RunMcpAsync(initializeRpc, timeoutSeconds: 10);

        for (int i = 0; i < runCount; i++)
        {
            var result = await ContainerTestHarness.RunMcpAsync(initializeRpc, timeoutSeconds: 10);
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("\"jsonrpc\":\"2.0\"", result.StandardOutput);
            samples.Add(result.TimeToFirstResponse.TotalSeconds);
        }

        samples.Sort();
        // Median for 8 elements is average of 4th and 5th (indices 3 and 4)
        double median = (samples[3] + samples[4]) / 2.0;
        // P90 (nearest rank: 0.9 * 8 = 7.2 -> index 7)
        double p90 = samples[(int)Math.Ceiling(0.9 * runCount) - 1];
        double max = samples[^1];

        // Report metrics
        Console.WriteLine($"[Benchmark] Cold start over {runCount} runs: Median={median:F3}s, P90={p90:F3}s, Max={max:F3}s, Min={samples[0]:F3}s");

        // Assert threshold applied to median (< 3.0s per SC-003) and hard cap on max (< 10.0s for VM scheduling jitter)
        Assert.True(
            median < 3.0,
            $"Cold start median {median:F3}s exceeded the strict 3.0s ceiling (SC-003). Samples: [{string.Join(", ", samples.Select(s => $"{s:F3}s"))}]");

        Assert.True(
            max < 10.0,
            $"Cold start max {max:F3}s exceeded hard cap of 10.0s. Samples: [{string.Join(", ", samples.Select(s => $"{s:F3}s"))}]");
    }
}
