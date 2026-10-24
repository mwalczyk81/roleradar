using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Tests.Integration.Fixtures;
using RoleRadar.Tests.Integration.Tracking;
using Xunit;

namespace RoleRadar.Tests.Integration.Hosting;

public sealed class ContainerContactPrivacyNoLeakTests : IAsyncLifetime
{
    private readonly SharedMongoFixture _fixture;
    private readonly string _databaseName;
    private MongoClient _client = null!;
    private IMongoDatabase _database = null!;

    public ContainerContactPrivacyNoLeakTests(SharedMongoFixture fixture)
    {
        _fixture = fixture;
        _databaseName = _fixture.CreateDatabaseName("rrtest_cont_noleak");
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
                await _fixture.DropDatabaseAsync(_databaseName);
            }
        }
        finally
        {
            _client?.Dispose();
        }
    }

    [Fact]
    public async Task Container_ContactPrivacy_NoLeakAcrossCliStatusMcpToolsAuditAndBackups()
    {
        var mcpDllPath = McpHostLocator.GetMcpDllPath();
        var env = _fixture.CreateProcessEnvironment(_databaseName);

        const string sensitiveName = "Cassandra Container Contact";
        const string sensitiveNote = "ContainerSecretMeetingNotes98765";
        const string sensitiveRole = "Lead Container Recruiter";
        const string sensitiveCompany = "HyperSecretContainerLLC";

        // 1. Create contact via launched CLI container
        var (addCode, addStdout, addStderr) = await LaunchedCliRunner.RunCliAsync(
            $"contact add --name \"{sensitiveName}\" --role \"{sensitiveRole}\" --company \"{sensitiveCompany}\" --notes \"{sensitiveNote}\" --json",
            env,
            forceContainer: true);

        Assert.Equal(0, addCode);
        using var addDoc = JsonDocument.Parse(addStdout);
        var contactIdStr = addDoc.RootElement.GetProperty("data").GetProperty("id").GetString()!;

        // Verify the ONLY allowed place to see sensitive info is 'contact get <id>'
        var (getCode, getStdout, _) = await LaunchedCliRunner.RunCliAsync(
            $"contact get --id {contactIdStr} --json",
            env,
            forceContainer: true);
        Assert.Equal(0, getCode);
        Assert.Contains(sensitiveName, getStdout);
        Assert.Contains(sensitiveNote, getStdout);

        // 2. Scan CLI container status and status --json
        var (_, statusOut, statusErr) = await LaunchedCliRunner.RunCliAsync("status", env, forceContainer: true);
        AssertNoLeak(statusOut + "\n" + statusErr, sensitiveName, sensitiveNote, sensitiveRole, sensitiveCompany, "CLI status");

        var (_, statusJsonOut, statusJsonErr) = await LaunchedCliRunner.RunCliAsync("status --json", env, forceContainer: true);
        AssertNoLeak(statusJsonOut + "\n" + statusJsonErr, sensitiveName, sensitiveNote, sensitiveRole, sensitiveCompany, "CLI status --json");

        // 3. Scan MCP container tools/list and all MCP tool calls
        await using var mcpClient = await McpProcessClient.StartAsync(mcpDllPath, env, forceContainer: true);

        var toolsList = await mcpClient.ListToolsAsync();
        AssertNoLeak(toolsList.ToString(), sensitiveName, sensitiveNote, sensitiveRole, sensitiveCompany, "MCP tools/list");

        // note_search
        var searchResp = await mcpClient.CallToolAsync("note_search", new { query = "Secret" });
        AssertNoLeak(searchResp.ToString(), sensitiveName, sensitiveNote, sensitiveRole, sensitiveCompany, "MCP note_search");

        // tracker_query
        var qPostings = await mcpClient.CallToolAsync("tracker_query", new { entity = "postings" });
        AssertNoLeak(qPostings.ToString(), sensitiveName, sensitiveNote, sensitiveRole, sensitiveCompany, "MCP tracker_query postings");

        var qCompanies = await mcpClient.CallToolAsync("tracker_query", new { entity = "companies" });
        AssertNoLeak(qCompanies.ToString(), sensitiveName, sensitiveNote, sensitiveRole, sensitiveCompany, "MCP tracker_query companies");

        var qSources = await mcpClient.CallToolAsync("tracker_query", new { entity = "career_sources" });
        AssertNoLeak(qSources.ToString(), sensitiveName, sensitiveNote, sensitiveRole, sensitiveCompany, "MCP tracker_query career_sources");

        var qContacts = await mcpClient.CallToolAsync("tracker_query", new { entity = "contacts" });
        Assert.False(qContacts.GetProperty("ok").GetBoolean());
        AssertNoLeak(qContacts.ToString(), sensitiveName, sensitiveNote, sensitiveRole, sensitiveCompany, "MCP tracker_query contacts");

        // 4. Scan audit_log collection
        var auditCol = _database.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.AuditLogCollectionName);
        var allAudits = await auditCol.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
        foreach (var audit in allAudits)
        {
            var auditJson = audit.ToJson();
            AssertNoLeak(auditJson, sensitiveName, sensitiveNote, sensitiveRole, sensitiveCompany, "audit_log collection");
        }

        // 5. Backup round-trip via CLI container: .rrbak must NOT leak plaintext of the name or notes
        var backupFile = Path.Combine(Path.GetTempPath(), $"container_contact_leak_{Guid.NewGuid():N}.rrbak");
        try
        {
            const string passphrase = "CorrectHorseBatteryStaple123!";
            var backupEnv = new Dictionary<string, string>(env)
            {
                ["ROLERADAR_BACKUP_PASSPHRASE"] = passphrase
            };

            var (backupCode, backupOut, backupErr) = await LaunchedCliRunner.RunCliAsync(
                $"backup \"{backupFile}\" --passphrase-env ROLERADAR_BACKUP_PASSPHRASE",
                backupEnv,
                forceContainer: true);
            Assert.True(backupCode == 0, $"Backup failed with code {backupCode}. Stderr: '{backupErr}'. Stdout: '{backupOut}'");
            Assert.True(File.Exists(backupFile));

            var backupBytes = await File.ReadAllBytesAsync(backupFile);
            var backupText = System.Text.Encoding.UTF8.GetString(backupBytes);
            AssertNoLeak(backupText, sensitiveName, sensitiveNote, sensitiveRole, sensitiveCompany, ".rrbak archive bytes");
        }
        finally
        {
            if (File.Exists(backupFile))
            {
                File.Delete(backupFile);
            }
        }
    }

    private static void AssertNoLeak(string haystack, string name, string note, string role, string company, string sourceContext)
    {
        Assert.DoesNotContain(name, haystack, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(note, haystack, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(role, haystack, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(company, haystack, StringComparison.OrdinalIgnoreCase);
    }
}
