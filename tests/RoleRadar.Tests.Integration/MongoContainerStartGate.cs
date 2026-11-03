namespace RoleRadar.Tests.Integration;

using DotNet.Testcontainers.Containers;

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

    public static async Task StartAsync(IContainer container)
    {
        ArgumentNullException.ThrowIfNull(container);

        await Gate.WaitAsync();
        try
        {
            await StartWithOneRestartAsync(container);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task StartWithOneRestartAsync(IContainer container)
    {
        try
        {
            await container.StartAsync();
            return;
        }
        catch (Exception first) when (first.Message.Contains("is not running", StringComparison.OrdinalIgnoreCase))
        {
            // mongod occasionally dies while the replica set is being set up (the container then reports
            // "is not running"). The cause is not established; start the same container once more and keep
            // the first attempt's output so a repeat is visible in the failure.
            var firstLogs = await TailLogsAsync(container);
            try
            {
                await container.StartAsync();
            }
            catch (Exception second)
            {
                throw new InvalidOperationException(
                    $"MongoDB container failed to start twice. First: {first.Message}{Environment.NewLine}{firstLogs}{Environment.NewLine}Second: {second.Message}",
                    second);
            }
        }
        catch (Exception other)
        {
            // Attach what the container itself logged so the cause is visible in the test failure.
            throw new InvalidOperationException($"MongoDB container failed to start: {other.Message}{Environment.NewLine}{await TailLogsAsync(container)}", other);
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
