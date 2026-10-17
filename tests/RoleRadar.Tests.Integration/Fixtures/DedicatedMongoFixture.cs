using Testcontainers.MongoDb;
using Xunit;

namespace RoleRadar.Tests.Integration.Fixtures;

public sealed class DedicatedMongoFixture : IAsyncLifetime
{
    private readonly MongoDbContainer _container = new MongoDbBuilder("mongo:8.0.34")
        .WithReplicaSet("rs0")
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
