using System.Diagnostics;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Tests.Contract.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Contract;

public sealed class StatusCliContractTests : IAsyncLifetime
{
    private readonly SharedMongoFixture _fixture;
    private readonly string _databaseName;
    private MongoClient _client = null!;
    private IMongoDatabase _database = null!;

    public StatusCliContractTests(SharedMongoFixture fixture)
    {
        _fixture = fixture;
        _databaseName = _fixture.CreateDatabaseName("roleradar_status_contract");
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
    public async Task Status_ReturnsExtendedEntityCounts_WithZeroNamesOrText()
    {
        var cliDllPath = GetCliDllPath();
        var env = _fixture.CreateProcessEnvironment(_databaseName);

        // Seed 1 of each entity: company, posting, criterion, application, signal, search_run, contact
        var companyId = Guid.NewGuid();
        var postingId = Guid.NewGuid();
        var sensitiveName = "StatusConfidentialAgent";

        var companiesCol = _database.GetCollection<BsonDocument>(TrackingMongoOptions.CompaniesCollectionName);
        await companiesCol.InsertOneAsync(new BsonDocument
        {
            ["_id"] = new BsonBinaryData(companyId, GuidRepresentation.Standard),
            ["owner_id"] = "owner-1",
            ["name"] = "SensitiveCompanyStatus",
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
            ["title"] = "ConfidentialPositionTitle",
            ["url"] = "https://example.com/status-jobs/1",
            ["normalized_url"] = "https://example.com/status-jobs/1",
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

        var contactsCol = _database.GetCollection<BsonDocument>(TrackingMongoOptions.ContactsCollectionName);
        await contactsCol.InsertOneAsync(new BsonDocument
        {
            ["_id"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
            ["owner_id"] = "owner-1",
            ["name"] = sensitiveName,
            ["relationship"] = "recruiter",
            ["is_redacted"] = false,
            ["version_token"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
            ["created_at"] = new BsonDateTime(DateTime.UtcNow),
            ["updated_at"] = new BsonDateTime(DateTime.UtcNow)
        });

        var searchRunsCol = _database.GetCollection<BsonDocument>(TrackingMongoOptions.SearchRunsCollectionName);
        await searchRunsCol.InsertOneAsync(new BsonDocument
        {
            ["_id"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
            ["owner_id"] = "owner-1",
            ["started_at"] = new BsonDateTime(DateTime.UtcNow),
            ["new_postings_count"] = 1,
            ["created_at"] = new BsonDateTime(DateTime.UtcNow)
        });

        // 1. Run roleradar status --json
        var (jsonCode, jsonStdout, jsonStderr) = await RunCliAsync(cliDllPath, "status --json", env);
        Assert.Equal(0, jsonCode);
        Assert.DoesNotContain("Unhandled exception", jsonStderr, StringComparison.OrdinalIgnoreCase);

        // Assert zero confidential names or strings in output
        Assert.DoesNotContain(sensitiveName, jsonStdout, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SensitiveCompanyStatus", jsonStdout, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ConfidentialPositionTitle", jsonStdout, StringComparison.OrdinalIgnoreCase);

        using var doc = JsonDocument.Parse(jsonStdout);
        var data = doc.RootElement.GetProperty("data");
        Assert.True(data.TryGetProperty("companiesCount", out var cCount) && cCount.GetInt32() >= 1);
        Assert.True(data.TryGetProperty("postingsCount", out var pCount) && pCount.GetInt32() >= 1);
        Assert.True(data.TryGetProperty("contactsCount", out var ctCount) && ctCount.GetInt32() >= 1);
        Assert.True(data.TryGetProperty("searchRunsCount", out var sCount) && sCount.GetInt32() >= 1);

        // 2. Run roleradar status (text mode)
        var (textCode, textStdout, textStderr) = await RunCliAsync(cliDllPath, "status", env);
        Assert.Equal(0, textCode);
        Assert.DoesNotContain(sensitiveName, textStdout, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SensitiveCompanyStatus", textStdout, StringComparison.OrdinalIgnoreCase);
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
