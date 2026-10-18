using MongoDB.Driver;
using RoleRadar.Infrastructure.Mongo;
using Testcontainers.MongoDb;
using Xunit;

namespace RoleRadar.Tests.Integration.Fixtures;

public sealed class SharedMongoFixture : IAsyncLifetime
{
    private readonly MongoDbContainer _mongoContainer = new MongoDbBuilder("mongo:8.0.34")
        .WithReplicaSet("rs0")
        .Build();

    public string ConnectionString { get; private set; } = string.Empty;
    public string ContainerId => _mongoContainer.Id;

    public async ValueTask InitializeAsync()
    {
        await _mongoContainer.StartAsync();
        ConnectionString = _mongoContainer.GetConnectionString();
    }

    public async ValueTask DisposeAsync()
    {
        await _mongoContainer.DisposeAsync();
    }

    public string CreateDatabaseName(string prefix = "roleradar_int") =>
        $"{prefix}_{Guid.CreateVersion7():N}";

    public MongoOptions CreateOptions(string? databaseName = null) => new()
    {
        ConnectionString = ConnectionString,
        DatabaseName = databaseName ?? CreateDatabaseName(),
        ReadinessTimeout = TimeSpan.FromSeconds(5)
    };

    public IReadOnlyDictionary<string, string> CreateProcessEnvironment(string? databaseName = null) =>
        new Dictionary<string, string>
        {
            ["ROLERADAR_MONGO_CONNECTION_STRING"] = ConnectionString,
            ["ROLERADAR_MONGO_DATABASE"] = databaseName ?? CreateDatabaseName()
        };

    public async Task<IMongoDatabase> CreateDatabaseAsync(string prefix = "roleradar_int")
    {
        var options = CreateOptions(CreateDatabaseName(prefix));
        var client = new MongoClient(options.ConnectionString);
        return client.GetDatabase(options.DatabaseName);
    }

    public async Task DropDatabaseAsync(string databaseName)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            return;
        }

        var client = new MongoClient(ConnectionString);
        await client.DropDatabaseAsync(databaseName);
    }
}
