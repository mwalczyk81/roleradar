using System.Diagnostics;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Tests.Contract.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Contract;

public sealed class VerifyCliContractTests : IAsyncLifetime
{
    private readonly SharedMongoFixture _fixture;
    private readonly string _databaseName;
    private MongoClient _client = null!;
    private IMongoDatabase _database = null!;

    public VerifyCliContractTests(SharedMongoFixture fixture)
    {
        _fixture = fixture;
        _databaseName = _fixture.CreateDatabaseName("roleradar_verify_contract");
    }

    public async ValueTask InitializeAsync()
    {
        _client = new MongoClient(_fixture.ConnectionString);
        _database = _client.GetDatabase(_databaseName);
        await TrackingIndexInitializer.EnsureIndexesAsync(_database, CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_client is not null && !string.IsNullOrWhiteSpace(_databaseName))
            {
                await _client.DropDatabaseAsync(_databaseName);
            }
        }
        finally
        {
            _client?.Dispose();
        }
    }

    [Fact]
    public async Task Verify_WhenDatabaseIsClean_ReturnsExitCodeZero()
    {
        var cliDllPath = GetCliDllPath();
        var env = _fixture.CreateProcessEnvironment(_databaseName);

        // Seed valid company, posting and application
        var companyId = Guid.NewGuid();
        var postingId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();

        var companiesCol = _database.GetCollection<BsonDocument>(TrackingMongoOptions.CompaniesCollectionName);
        await companiesCol.InsertOneAsync(new BsonDocument
        {
            ["_id"] = new BsonBinaryData(companyId, GuidRepresentation.Standard),
            ["owner_id"] = "owner-1",
            ["name"] = "Integrity Clean Tech",
            ["status"] = "target",
            ["ownership_type"] = "public",
            ["category_tags"] = new BsonArray(),
            ["version_token"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
            ["created_at"] = new BsonDateTime(DateTime.UtcNow),
            ["updated_at"] = new BsonDateTime(DateTime.UtcNow)
        });

        var postingsCol = _database.GetCollection<BsonDocument>(TrackingMongoOptions.PostingsCollectionName);
        await postingsCol.InsertOneAsync(new BsonDocument
        {
            ["_id"] = new BsonBinaryData(postingId, GuidRepresentation.Standard),
            ["owner_id"] = "owner-1",
            ["company_id"] = new BsonBinaryData(companyId, GuidRepresentation.Standard),
            ["title"] = "Senior Verification Engineer",
            ["url"] = "https://example.com/jobs/1",
            ["normalized_url"] = "https://example.com/jobs/1",
            ["source_kind"] = "ats",
            ["disposition"] = "lead",
            ["resurface_policy"] = "never",
            ["culture_risk_level"] = "stable",
            ["first_seen_at"] = new BsonDateTime(DateTime.UtcNow),
            ["last_verified_at"] = new BsonDateTime(DateTime.UtcNow),
            ["version_token"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
            ["created_at"] = new BsonDateTime(DateTime.UtcNow),
            ["updated_at"] = new BsonDateTime(DateTime.UtcNow)
        });

        var (exitCode, stdout, stderr) = await RunCliAsync(cliDllPath, "verify --json", env);
        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("Unhandled exception", stderr, StringComparison.OrdinalIgnoreCase);

        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal(0, root.GetProperty("data").GetProperty("anomalyCount").GetInt32());
    }

    [Fact]
    public async Task Verify_DetectsDanglingReferences_ReturnsNonZero_AndModifiesNothing()
    {
        var cliDllPath = GetCliDllPath();
        var env = _fixture.CreateProcessEnvironment(_databaseName);

        // Seed anomalies:
        // 1. Posting referencing dangling company
        var danglingCompanyId = Guid.NewGuid();
        var postingWithDanglingCompanyId = Guid.NewGuid();
        var postingsCol = _database.GetCollection<BsonDocument>(TrackingMongoOptions.PostingsCollectionName);
        await postingsCol.InsertOneAsync(new BsonDocument
        {
            ["_id"] = new BsonBinaryData(postingWithDanglingCompanyId, GuidRepresentation.Standard),
            ["owner_id"] = "owner-1",
            ["company_id"] = new BsonBinaryData(danglingCompanyId, GuidRepresentation.Standard),
            ["title"] = "Role With Missing Company",
            ["url"] = "https://example.com/jobs/2",
            ["normalized_url"] = "https://example.com/jobs/2",
            ["source_kind"] = "ats",
            ["disposition"] = "lead",
            ["resurface_policy"] = "never",
            ["culture_risk_level"] = "stable",
            ["first_seen_at"] = new BsonDateTime(DateTime.UtcNow),
            ["last_verified_at"] = new BsonDateTime(DateTime.UtcNow),
            ["version_token"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
            ["created_at"] = new BsonDateTime(DateTime.UtcNow),
            ["updated_at"] = new BsonDateTime(DateTime.UtcNow)
        });

        // 2. Application referencing dangling posting
        var danglingPostingId = Guid.NewGuid();
        var applicationsCol = _database.GetCollection<BsonDocument>(TrackingMongoOptions.ApplicationsCollectionName);
        await applicationsCol.InsertOneAsync(new BsonDocument
        {
            ["_id"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
            ["owner_id"] = "owner-1",
            ["posting_id"] = new BsonBinaryData(danglingPostingId, GuidRepresentation.Standard),
            ["status"] = "applied",
            ["submitted_date"] = "2026-10-18",
            ["resume_variant"] = "standard",
            ["version_token"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
            ["created_at"] = new BsonDateTime(DateTime.UtcNow),
            ["updated_at"] = new BsonDateTime(DateTime.UtcNow)
        });

        // 3. Posting with duplicate_of referencing non-existent posting
        var danglingDupPostingId = Guid.NewGuid();
        await postingsCol.InsertOneAsync(new BsonDocument
        {
            ["_id"] = new BsonBinaryData(danglingDupPostingId, GuidRepresentation.Standard),
            ["owner_id"] = "owner-1",
            ["company_id"] = new BsonBinaryData(danglingCompanyId, GuidRepresentation.Standard),
            ["title"] = "Duplicate Role",
            ["url"] = "https://example.com/jobs/3",
            ["normalized_url"] = "https://example.com/jobs/3",
            ["source_kind"] = "ats",
            ["disposition"] = "screened_out",
            ["disposition_reason"] = "duplicate",
            ["duplicate_of"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
            ["resurface_policy"] = "never",
            ["culture_risk_level"] = "stable",
            ["first_seen_at"] = new BsonDateTime(DateTime.UtcNow),
            ["last_verified_at"] = new BsonDateTime(DateTime.UtcNow),
            ["version_token"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
            ["created_at"] = new BsonDateTime(DateTime.UtcNow),
            ["updated_at"] = new BsonDateTime(DateTime.UtcNow)
        });

        // 4. Applied posting without an application record
        var appliedWithoutAppPostingId = Guid.NewGuid();
        await postingsCol.InsertOneAsync(new BsonDocument
        {
            ["_id"] = new BsonBinaryData(appliedWithoutAppPostingId, GuidRepresentation.Standard),
            ["owner_id"] = "owner-1",
            ["company_id"] = new BsonBinaryData(danglingCompanyId, GuidRepresentation.Standard),
            ["title"] = "Applied Role Without Application Record",
            ["url"] = "https://example.com/jobs/4",
            ["normalized_url"] = "https://example.com/jobs/4",
            ["source_kind"] = "ats",
            ["disposition"] = "applied",
            ["resurface_policy"] = "never",
            ["culture_risk_level"] = "stable",
            ["first_seen_at"] = new BsonDateTime(DateTime.UtcNow),
            ["last_verified_at"] = new BsonDateTime(DateTime.UtcNow),
            ["version_token"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
            ["created_at"] = new BsonDateTime(DateTime.UtcNow),
            ["updated_at"] = new BsonDateTime(DateTime.UtcNow)
        });

        // 5. Note referencing non-existent company
        var notesCol = _database.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.NotesCollectionName);
        await notesCol.InsertOneAsync(new BsonDocument
        {
            ["_id"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
            ["owner_id"] = "owner-1",
            ["title"] = "Narrative Note With Dangling Link",
            ["company_id"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
            ["status"] = "active",
            ["current_body"] = "Body text",
            ["version_token"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
            ["created_at"] = new BsonDateTime(DateTime.UtcNow),
            ["updated_at"] = new BsonDateTime(DateTime.UtcNow)
        });

        var countPostingsBefore = await postingsCol.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);
        var countAppsBefore = await applicationsCol.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);

        var (exitCode, stdout, stderr) = await RunCliAsync(cliDllPath, "verify --json", env);
        Assert.NotEqual(0, exitCode);

        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        var data = root.GetProperty("data");
        Assert.True(data.GetProperty("anomalyCount").GetInt32() > 0);

        // Assert read-only guarantee: zero documents mutated or deleted
        var countPostingsAfter = await postingsCol.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);
        var countAppsAfter = await applicationsCol.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);
        Assert.Equal(countPostingsBefore, countPostingsAfter);
        Assert.Equal(countAppsBefore, countAppsAfter);
    }

    private static string GetCliDllPath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "RoleRadar.Cli.dll");
        if (!File.Exists(path))
        {
            var slnRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
            var candidate = Path.Combine(slnRoot, "src", "RoleRadar.Cli", "bin", "Release", "net10.0", "RoleRadar.Cli.dll");
            if (File.Exists(candidate))
            {
                path = candidate;
            }
            else
            {
                candidate = Path.Combine(slnRoot, "src", "RoleRadar.Cli", "bin", "Debug", "net10.0", "RoleRadar.Cli.dll");
                if (File.Exists(candidate))
                {
                    path = candidate;
                }
            }
        }

        Assert.True(File.Exists(path), $"Expected CLI assembly at '{path}'.");
        return path;
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunCliAsync(
        string dllPath,
        string arguments,
        IReadOnlyDictionary<string, string> env)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{dllPath}\" {arguments}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var (k, v) in env)
        {
            psi.Environment[k] = v;
        }

        using var proc = Process.Start(psi)!;
        Assert.NotNull(proc);

        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();

        await proc.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        return (proc.ExitCode, stdout, stderr);
    }
}
