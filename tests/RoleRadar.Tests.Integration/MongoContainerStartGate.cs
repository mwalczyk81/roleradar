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
            await container.StartAsync();
        }
        finally
        {
            Gate.Release();
        }
    }
}
