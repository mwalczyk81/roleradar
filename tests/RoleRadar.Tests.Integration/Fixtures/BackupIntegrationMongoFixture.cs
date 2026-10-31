using MongoDB.Driver;
using Testcontainers.MongoDb;
using Xunit;

namespace RoleRadar.Tests.Integration.Fixtures;

public sealed class BackupIntegrationMongoFixture : IAsyncLifetime
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

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    public async Task DropNonSystemDatabasesAsync()
    {
        var client = new MongoClient(ConnectionString);
        using var cursor = await client.ListDatabaseNamesAsync();
        var databaseNames = await cursor.ToListAsync();

        foreach (var dbName in databaseNames)
        {
            if (dbName != "admin" && dbName != "config" && dbName != "local")
            {
                Hosting.ContainerTestHarness.ValidateDatabaseNameForDrop(dbName);
                await client.DropDatabaseAsync(dbName);
            }
        }
    }

    public IReadOnlyDictionary<string, string> CreateProcessEnvironment(string databaseName)
    {
        var mongoUrl = new MongoUrl(ConnectionString);
        var authPart = (!string.IsNullOrEmpty(mongoUrl.Username) && !string.IsNullOrEmpty(mongoUrl.Password))
            ? $"{mongoUrl.Username}:{mongoUrl.Password}@"
            : string.Empty;
        var internalUri = $"mongodb://{authPart}localhost:27017/?replicaSet=rs0&authSource=admin";

        return new Dictionary<string, string>
        {
            ["ROLERADAR_MONGO_CONNECTION_STRING"] = ConnectionString,
            ["ROLERADAR_MONGO_DATABASE"] = databaseName,
            ["ROLERADAR_BACKUP_DUMP_COMMAND"] = $"docker exec -i {_container.Id} mongodump --uri=\"{internalUri}\" --archive --gzip --oplog",
            ["ROLERADAR_BACKUP_RESTORE_COMMAND"] = $"docker exec -i {_container.Id} mongorestore --uri=\"{internalUri}\" --archive --gzip --nsInclude \"<db>.*\""
        };
    }
}
