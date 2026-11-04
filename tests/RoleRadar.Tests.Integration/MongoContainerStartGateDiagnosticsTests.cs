namespace RoleRadar.Tests.Integration;

using Testcontainers.MongoDb;
using Xunit;

/// <summary>
/// The restart in <see cref="MongoContainerStartGate"/> only fires on a rare mongod death, so this proves the
/// diagnostics it prints (docker State, log tail, concurrent starts) are really produced for a live container.
/// </summary>
public sealed class MongoContainerStartGateDiagnosticsTests
{
    [Fact]
    public async Task FailureDiagnostics_IncludeDockerState_LogTail_AndConcurrentStarts()
    {
        await using var container = new MongoDbBuilder("mongo:8.0.34")
            .WithEnvironment("GLIBC_TUNABLES", "glibc.pthread.rseq=1") // same kernel workaround as ODataTestHost; mongod 8.0 exits on Linux 6.19+ without it
            .Build();
        await MongoContainerStartGate.StartAsync(container);

        var text = await MongoContainerStartGate.DescribeFailureAsync(container, new InvalidOperationException("simulated"));

        Assert.Contains("start error: simulated", text);
        Assert.Contains("concurrent container starts at this moment:", text);
        foreach (var field in new[] { "\"Status\"", "\"ExitCode\"", "\"OOMKilled\"", "\"Error\"", "\"FinishedAt\"" })
        {
            Assert.Contains(field, text);
        }

        Assert.Contains("docker memory (bytes):", text);
        Assert.Contains("--- last container log lines ---", text);
    }
}
