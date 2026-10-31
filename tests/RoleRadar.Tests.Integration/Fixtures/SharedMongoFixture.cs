using MongoDB.Driver;
using RoleRadar.Infrastructure.Mongo;
using Testcontainers.MongoDb;
using Xunit;

[assembly: Trait("Category", "Integration")]
[assembly: AssemblyFixture(typeof(RoleRadar.Tests.Integration.Fixtures.SharedMongoFixture))]

namespace RoleRadar.Tests.Integration.Fixtures;

public sealed class SharedMongoFixture : IAsyncLifetime
{
    // Work around MongoDB SERVER-121912 rseq/TCMalloc startup abort on Linux kernels 6.19+ and 7.0.0-7.0.13.
    private readonly MongoDbContainer _mongoContainer = new MongoDbBuilder("mongo:8.0.34")
        .WithEnvironment("GLIBC_TUNABLES", "glibc.pthread.rseq=1")
        .WithReplicaSet("rs0")
        .WithTmpfsMount("/data/db")
        .WithTmpfsMount("/data/configdb")
        .Build();

    public string ConnectionString { get; private set; } = string.Empty;
    public string ContainerId => _mongoContainer.Id;

    public async ValueTask InitializeAsync()
    {
        await _mongoContainer.StartAsync();
        var rawConn = _mongoContainer.GetConnectionString();
        if (!rawConn.Contains("authSource=", StringComparison.OrdinalIgnoreCase))
        {
            ConnectionString = rawConn.Contains('?') ? $"{rawConn}&authSource=admin" : $"{rawConn}?authSource=admin";
        }
        else
        {
            ConnectionString = rawConn;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _mongoContainer.DisposeAsync();
    }

    public string CreateDatabaseName(string prefix = "rrtest_int")
    {
        var safePrefix = prefix.StartsWith("rrtest_", StringComparison.Ordinal)
            ? prefix
            : $"rrtest_{prefix.Replace("roleradar_", string.Empty)}";
        return $"{safePrefix}_{Guid.CreateVersion7():N}";
    }

    public MongoOptions CreateOptions(string? databaseName = null) => new()
    {
        ConnectionString = ConnectionString,
        DatabaseName = databaseName ?? CreateDatabaseName(),
        ReadinessTimeout = TimeSpan.FromSeconds(5)
    };

    public IReadOnlyDictionary<string, string> CreateProcessEnvironment(string? databaseName = null)
    {
        var db = databaseName ?? CreateDatabaseName();
        var mongoUrl = new MongoUrl(ConnectionString);
        var authPart = (!string.IsNullOrEmpty(mongoUrl.Username) && !string.IsNullOrEmpty(mongoUrl.Password))
            ? $"{mongoUrl.Username}:{mongoUrl.Password}@"
            : string.Empty;
        var internalUri = $"mongodb://{authPart}localhost:27017/?replicaSet=rs0&authSource=admin";

        return new Dictionary<string, string>
        {
            ["ROLERADAR_MONGO_CONNECTION_STRING"] = ConnectionString,
            ["ROLERADAR_MONGO_DATABASE"] = db,
            ["ROLERADAR_BACKUP_DUMP_COMMAND"] = $"docker exec -i {_mongoContainer.Id} mongodump --uri=\"{internalUri}\" --archive --gzip --oplog",
            ["ROLERADAR_BACKUP_RESTORE_COMMAND"] = $"docker exec -i {_mongoContainer.Id} mongorestore --uri=\"{internalUri}\" --archive --gzip --oplogReplay --nsInclude \"<db>.*\""
        };
    }

    public async Task<IMongoDatabase> CreateDatabaseAsync(string prefix = "rrtest_int")
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

        if (string.Equals(databaseName, "roleradar", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to drop protected default database 'roleradar' in test cleanup.");
        }

        var client = new MongoClient(ConnectionString);
        await client.DropDatabaseAsync(databaseName);
    }
}
