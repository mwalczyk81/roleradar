namespace RoleRadar.Tests.Integration;

using System.Diagnostics;
using DotNet.Testcontainers.Containers;
using Xunit;

/// <summary>
/// xUnit runs test classes in parallel, and many of them start their own MongoDB replica set. Dozens of
/// simultaneous container starts (plus the docker image builds the container tests run) starve the Docker
/// daemon, which made first requests and <c>docker run</c> calls blow through their timeouts. Every Mongo
/// container start goes through this gate so only a few start at once; a started container is not limited.
/// </summary>
internal static class MongoContainerStartGate
{
    private const int MaxConcurrentStarts = 3;

    private static readonly SemaphoreSlim Gate = new(MaxConcurrentStarts, MaxConcurrentStarts);

    private static int _startsInFlight;

    public static async Task StartAsync(IContainer container)
    {
        ArgumentNullException.ThrowIfNull(container);

        await Gate.WaitAsync();
        Interlocked.Increment(ref _startsInFlight);
        try
        {
            await StartWithOneRestartAsync(container);
        }
        finally
        {
            Interlocked.Decrement(ref _startsInFlight);
            Gate.Release();
        }
    }

    private static Task StartWithOneRestartAsync(IContainer container) =>
        StartWithOneRestartAsync(
            () => container.StartAsync(),
            failure => DescribeFailureAsync(container, failure));

    /// <summary>
    /// The retry policy, separated from Docker so it can be tested without a real container: one restart after an
    /// "is not running" failure, none after anything else.
    /// </summary>
    internal static async Task StartWithOneRestartAsync(
        Func<Task> start,
        Func<Exception, Task<string>> describeFailure)
    {
        try
        {
            await start();
            return;
        }
        catch (Exception first) when (first.Message.Contains("is not running", StringComparison.OrdinalIgnoreCase))
        {
            // mongod occasionally dies while the replica set is being set up (the container then reports
            // "is not running"). Capture what Docker says about why, make the restart visible, and start the same
            // container once more. There are no further retries.
            var diagnostics = await describeFailure(first);
            ReportRestart(diagnostics);
            var firstLogs = diagnostics;
            try
            {
                await start();
            }
            catch (Exception second)
            {
                // The restart failed too: explain both attempts (docker State and log tail each), not only the first.
                var secondDiagnostics = await describeFailure(second);
                throw new InvalidOperationException(
                    $"MongoDB container failed to start twice.{Environment.NewLine}First attempt: {first.Message}{Environment.NewLine}{firstLogs}{Environment.NewLine}Second attempt: {second.Message}{Environment.NewLine}{secondDiagnostics}",
                    second);
            }
        }
        catch (Exception other)
        {
            // Attach docker State, memory and what the container logged, so the cause is visible in the test failure.
            throw new InvalidOperationException($"MongoDB container failed to start: {other.Message}{Environment.NewLine}{await describeFailure(other)}", other);
        }
    }

    /// <summary>
    /// Visible in the test output (a diagnostic message and stderr). When ROLERADAR_TEST_RESTART_LOG names a file the
    /// warning is also appended there, so repeated runs can be counted.
    /// </summary>
    private static void ReportRestart(string diagnostics)
    {
        var warning = "WARNING: MongoDB test container died during start; restarting it once." + Environment.NewLine + diagnostics;
        Console.Error.WriteLine(warning);
        try
        {
            TestContext.Current.SendDiagnosticMessage(warning);
        }
        catch (Exception)
        {
            // No active test context (fixture start outside a test); stderr already has it.
        }

        var file = Environment.GetEnvironmentVariable("ROLERADAR_TEST_RESTART_LOG");
        if (!string.IsNullOrEmpty(file))
        {
            File.AppendAllText(file, warning + Environment.NewLine + "=====" + Environment.NewLine);
        }
    }

    internal static async Task<string> DescribeFailureAsync(IContainer container, Exception failure)
    {
        var lines = new List<string>
        {
            $"start error: {failure.Message}",
            $"concurrent container starts at this moment: {Volatile.Read(ref _startsInFlight)} (gate allows {MaxConcurrentStarts})",
            $"docker State: {await StateAsync(container)}",
            $"docker memory (bytes): {await RunDockerAsync("info", "--format", "{{.MemTotal}}")}"
        };
        lines.Add(await TailLogsAsync(container));
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>The container's docker State; a container that was never created (for example an image that cannot be pulled) has no id, and that must not make the diagnostics themselves throw.</summary>
    private static async Task<string> StateAsync(IContainer container)
    {
        string id;
        try
        {
            id = container.Id;
        }
        catch (Exception ex)
        {
            return $"(container was never created: {ex.Message})";
        }

        return await RunDockerAsync("inspect", "--format", "{{json .State}}", id);
    }

    private static async Task<string> RunDockerAsync(params string[] args)
    {
        try
        {
            var info = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in args)
            {
                info.ArgumentList.Add(arg);
            }

            using var process = Process.Start(info) ?? throw new InvalidOperationException("docker did not start");
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return (output + error).Trim();
        }
        catch (Exception ex)
        {
            return $"(docker unavailable: {ex.Message})";
        }
    }

    private static async Task<string> TailLogsAsync(IContainer container)
    {
        try
        {
            var (stdout, stderr) = await container.GetLogsAsync();
            var lines = (stdout + Environment.NewLine + stderr).Split(Environment.NewLine).TakeLast(40);
            return "--- last container log lines ---" + Environment.NewLine + string.Join(Environment.NewLine, lines);
        }
        catch (Exception logEx)
        {
            return $"(container logs unavailable: {logEx.Message})";
        }
    }
}
