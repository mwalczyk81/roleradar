using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Infrastructure.Mongo;
using Testcontainers.MongoDb;
using Xunit;

[assembly: AssemblyFixture(typeof(RoleRadar.Tests.Contract.Fixtures.SharedMongoFixture))]

namespace RoleRadar.Tests.Contract.Fixtures;

public sealed class SharedMongoFixture : IAsyncLifetime
{
    private readonly MongoDbContainer _mongoContainer = new MongoDbBuilder("mongo:8.0.34")
        .WithReplicaSet("rs0")
        .WithTmpfsMount("/data/db")
        .WithTmpfsMount("/data/configdb")
        .Build();

    private bool _isExternalMode;
    private MongoClient? _mongoClient;

    public string ConnectionString { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        var optIn = Environment.GetEnvironmentVariable("ROLERADAR_EXTERNAL_TEST_OPT_IN");
        _isExternalMode = optIn == "1";

        var externalConn = Environment.GetEnvironmentVariable("ROLERADAR_EXTERNAL_MONGO_CONNECTION_STRING");
        if (_isExternalMode && !string.IsNullOrWhiteSpace(externalConn))
        {
            ExternalMongoSafetyGuard.ValidateConnectionString(externalConn, isOptInEnabled: true);
            ConnectionString = externalConn;
            _mongoClient = new MongoClient(ConnectionString);

            try
            {
                using var cursor = await _mongoClient.ListDatabaseNamesAsync();
                var dbs = await cursor.ToListAsync();
                ExternalMongoSafetyGuard.ValidateExistingDatabases(dbs);
            }
            catch (Exception ex) when (ex is not ForeignDatabaseDetectedException)
            {
                ExternalMongoSafetyGuard.HandleListDatabasesException(ex);
            }
        }
        else
        {
            await _mongoContainer.StartAsync();
            ConnectionString = _mongoContainer.GetConnectionString();
            _mongoClient = new MongoClient(ConnectionString);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isExternalMode && _mongoClient != null)
        {
            try
            {
                var testResultsDir = Path.Combine(Directory.GetCurrentDirectory(), "TestResults");
                Directory.CreateDirectory(testResultsDir);
                var probeDb = _mongoClient.GetDatabase("admin");
                await DatabaseCapabilityMatrixReporter.Instance.GenerateReportsAsync(probeDb, testResultsDir);
            }
            catch
            {
                // Best effort report generation on teardown
            }

            await ExternalMongoSafetyGuard.DropAllTrackedTestDatabasesAsync(_mongoClient);
        }

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ROLERADAR_EXTERNAL_MONGO_CONNECTION_STRING")))
        {
            await _mongoContainer.DisposeAsync();
        }
    }

    public string CreateDatabaseName(string prefix = "rrtest_contract")
    {
        if (_isExternalMode)
        {
            return ExternalMongoSafetyGuard.GenerateAndRegisterTestDatabase();
        }

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

    public IReadOnlyDictionary<string, string> CreateProcessEnvironment(string? databaseName = null) =>
        new Dictionary<string, string>
        {
            ["ROLERADAR_MONGO_CONNECTION_STRING"] = ConnectionString,
            ["ROLERADAR_MONGO_DATABASE"] = databaseName ?? CreateDatabaseName()
        };

    public async Task<IMongoDatabase> CreateDatabaseAsync(string prefix = "roleradar_contract")
    {
        var dbName = CreateDatabaseName(prefix);
        var client = new MongoClient(ConnectionString);
        await Task.CompletedTask;
        return client.GetDatabase(dbName);
    }

    public async Task DropDatabaseAsync(string databaseName)
    {
        if (string.IsNullOrWhiteSpace(databaseName) || _mongoClient == null)
        {
            return;
        }

        ExternalMongoSafetyGuard.ValidateDatabaseNameForDrop(databaseName);
        await _mongoClient.DropDatabaseAsync(databaseName);
    }
}
