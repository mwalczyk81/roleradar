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

    /// <summary>
    /// Feature 010: when the single restart also fails, the exception must explain the second attempt too (its docker
    /// State and log tail), not only the first. The retry policy is driven with delegates, so the "is not running" path
    /// can be exercised on demand instead of waiting for a rare mongod death.
    /// </summary>
    [Fact]
    public async Task SecondStartFailure_ReportsDiagnosticsOfBothAttempts()
    {
        var describeCalls = 0;
        var startCalls = 0;

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => MongoContainerStartGate.StartWithOneRestartAsync(
            () =>
            {
                startCalls++;
                throw new InvalidOperationException($"Container abc123 is not running (start {startCalls})");
            },
            _ => Task.FromResult($"DIAGNOSTICS-{++describeCalls}")));

        Assert.Equal(2, startCalls);
        Assert.Contains("failed to start twice", failure.Message);
        Assert.Contains("First attempt", failure.Message);
        Assert.Contains("DIAGNOSTICS-1", failure.Message);
        Assert.Contains("Second attempt", failure.Message);
        Assert.Contains("DIAGNOSTICS-2", failure.Message);
        Assert.Contains("(start 2)", failure.InnerException!.Message);
    }

    [Fact]
    public async Task RestartThatSucceeds_ThrowsNothing_AndDescribesOnlyTheFirstFailure()
    {
        var startCalls = 0;
        var describeCalls = 0;

        await MongoContainerStartGate.StartWithOneRestartAsync(
            () => ++startCalls == 1 ? throw new InvalidOperationException("Container abc123 is not running") : Task.CompletedTask,
            _ => Task.FromResult($"DIAGNOSTICS-{++describeCalls}"));

        Assert.Equal(2, startCalls);
        Assert.Equal(1, describeCalls);
    }

    [Fact]
    public async Task OtherFailure_IsNotRetried_AndReportsTheFullDiagnostics()
    {
        var startCalls = 0;

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => MongoContainerStartGate.StartWithOneRestartAsync(
            () =>
            {
                startCalls++;
                throw new InvalidOperationException("Container abc123 exited with code 2.");
            },
            _ => Task.FromResult("DIAGNOSTICS-WITH-STATE-AND-LOGS")));

        Assert.Equal(1, startCalls);
        Assert.Contains("MongoDB container failed to start", failure.Message);
        Assert.Contains("DIAGNOSTICS-WITH-STATE-AND-LOGS", failure.Message);
    }

    /// <summary>
    /// A mongod that is given a flag it rejects exits at once with "exited with code 2" (not "is not running"), so it
    /// takes the generic path. The message must carry the docker State and the log that explains the exit.
    /// </summary>
    [Fact]
    public async Task ExitedMongod_ReportsDockerStateAndTheLogLine_NotOnlyALogTail()
    {
        await using var container = new MongoDbBuilder("mongo:8.0.34")
            .WithCommand("--definitely-not-a-mongod-flag")
            .Build();

        var failure = await Assert.ThrowsAnyAsync<Exception>(() => MongoContainerStartGate.StartAsync(container));

        Assert.True(failure.Message.Contains("docker State:", StringComparison.Ordinal), "message was: " + failure.Message);
        Assert.Contains("\"ExitCode\":2", failure.Message);
        Assert.Contains("unrecognised option '--definitely-not-a-mongod-flag'", failure.Message);
        Assert.Contains("--- last container log lines ---", failure.Message);
    }

    /// <summary>The diagnostics must not themselves throw when no container was ever created (an image that cannot be pulled).</summary>
    [Fact]
    public async Task ContainerThatWasNeverCreated_StillGetsAnExplainingMessage()
    {
        await using var container = new MongoDbBuilder("mongo:0.0.0-this-tag-does-not-exist").Build();

        var failure = await Assert.ThrowsAnyAsync<Exception>(() => MongoContainerStartGate.StartAsync(container));

        Assert.True(failure.Message.Contains("MongoDB container failed to start", StringComparison.Ordinal), "message was: " + failure.Message);
        Assert.Contains("docker State:", failure.Message);
    }
}
