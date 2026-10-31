using Testcontainers.MongoDb;
using Xunit;

namespace RoleRadar.Tests.Integration.Fixtures;

public sealed class DedicatedMongoFixture : IAsyncLifetime
{
    // Work around MongoDB SERVER-121912 rseq/TCMalloc startup abort on Linux kernels 6.19+ and 7.0.0-7.0.13.
    private readonly MongoDbContainer _container = new MongoDbBuilder("mongo:8.0.34")
        .WithEnvironment("GLIBC_TUNABLES", "glibc.pthread.rseq=1")
        .WithReplicaSet("rs0")
        .WithTmpfsMount("/data/db")
        .WithTmpfsMount("/data/configdb")
        .Build();

    public string ConnectionString => _container.GetConnectionString();
    public string ContainerId => _container.Id;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
    }

    public async Task StopAsync()
    {
        await _container.StopAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}
