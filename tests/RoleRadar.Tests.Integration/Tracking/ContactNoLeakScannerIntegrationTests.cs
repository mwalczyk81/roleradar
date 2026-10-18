using System.Diagnostics;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Tests.Integration.Fixtures;
using RoleRadar.Tests.Integration.Tracking;
using Xunit;

namespace RoleRadar.Tests.Integration.Tracking;

public sealed class ContactNoLeakScannerIntegrationTests : IAsyncLifetime
{
    private readonly SharedMongoFixture _fixture;
    private readonly string _databaseName;
    private MongoClient _client = null!;
    private IMongoDatabase _database = null!;

    public ContactNoLeakScannerIntegrationTests(SharedMongoFixture fixture)
    {
        _fixture = fixture;
        _databaseName = _fixture.CreateDatabaseName("roleradar_contact_noleak");
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
    public async Task Contact_NoLeakAcrossCliStatusMcpToolsAuditAndBackups()
    {
        var cliDllPath = GetCliDllPath();
        var mcpDllPath = McpHostLocator.GetMcpDllPath();
        var env = _fixture.CreateProcessEnvironment(_databaseName);

        const string sensitiveName = "Bartholomew Confidential Contact";
        const string sensitiveNote = "DistinctiveTopSecretMeetingNotes12345";
        const string sensitiveRole = "Lead Confidential Recruiter";
        const string sensitiveCompany = "HyperSecretTalentLLC";

        // 1. Create contact via launched CLI
        var (addCode, addStdout, addStderr) = await RunCliAsync(
            cliDllPath,
            $"contact add --name \"{sensitiveName}\" --role \"{sensitiveRole}\" --company \"{sensitiveCompany}\" --notes \"{sensitiveNote}\" --json",
            env);

        Assert.Equal(0, addCode);
        using var addDoc = JsonDocument.Parse(addStdout);
        var contactIdStr = addDoc.RootElement.GetProperty("data").GetProperty("id").GetString()!;
        var contactId = Guid.Parse(contactIdStr);

        // Verify the ONLY allowed place to see sensitive info is 'contact get <id>'
        var (getCode, getStdout, _) = await RunCliAsync(
            cliDllPath,
            $"contact get --id {contactIdStr} --json",
            env);
        Assert.Equal(0, getCode);
        Assert.Contains(sensitiveName, getStdout);
        Assert.Contains(sensitiveNote, getStdout);

        // 2. Scan CLI status and status --json
        var (_, statusOut, statusErr) = await RunCliAsync(cliDllPath, "status", env);
        AssertNoLeak(statusOut + "\n" + statusErr, sensitiveName, sensitiveNote, sensitiveRole, sensitiveCompany, "CLI status");

        var (_, statusJsonOut, statusJsonErr) = await RunCliAsync(cliDllPath, "status --json", env);
        AssertNoLeak(statusJsonOut + "\n" + statusJsonErr, sensitiveName, sensitiveNote, sensitiveRole, sensitiveCompany, "CLI status --json");

        // 3. Scan MCP tools/list and all MCP tool calls
        await using var mcpClient = await McpProcessClient.StartAsync(mcpDllPath, env);

        var toolsList = await mcpClient.ListToolsAsync();
        AssertNoLeak(toolsList.ToString(), sensitiveName, sensitiveNote, sensitiveRole, sensitiveCompany, "MCP tools/list");

        // note_search
        var searchResp = await mcpClient.CallToolAsync("note_search", new { query = "Secret" });
        AssertNoLeak(searchResp.ToString(), sensitiveName, sensitiveNote, sensitiveRole, sensitiveCompany, "MCP note_search");

        // tracker_query (postings, decided_postings, companies, career_sources)
        var qPostings = await mcpClient.CallToolAsync("tracker_query", new { entity = "postings" });
        AssertNoLeak(qPostings.ToString(), sensitiveName, sensitiveNote, sensitiveRole, sensitiveCompany, "MCP tracker_query postings");

        var qDecided = await mcpClient.CallToolAsync("tracker_query", new { entity = "decided_postings" });
        AssertNoLeak(qDecided.ToString(), sensitiveName, sensitiveNote, sensitiveRole, sensitiveCompany, "MCP tracker_query decided_postings");

        var qCompanies = await mcpClient.CallToolAsync("tracker_query", new { entity = "companies" });
        AssertNoLeak(qCompanies.ToString(), sensitiveName, sensitiveNote, sensitiveRole, sensitiveCompany, "MCP tracker_query companies");

        var qSources = await mcpClient.CallToolAsync("tracker_query", new { entity = "career_sources" });
        AssertNoLeak(qSources.ToString(), sensitiveName, sensitiveNote, sensitiveRole, sensitiveCompany, "MCP tracker_query career_sources");

        // Attempt tracker_query entity = contacts -> must fail with ValidationError
        var qContacts = await mcpClient.CallToolAsync("tracker_query", new { entity = "contacts" });
        Assert.False(qContacts.GetProperty("ok").GetBoolean());
        Assert.Equal("ValidationError", qContacts.GetProperty("error").GetProperty("code").GetString());
        AssertNoLeak(qContacts.ToString(), sensitiveName, sensitiveNote, sensitiveRole, sensitiveCompany, "MCP tracker_query contacts");

        // 4. Scan audit_log collection
        var auditCol = _database.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.AuditLogCollectionName);
        var allAudits = await auditCol.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
        foreach (var audit in allAudits)
        {
            var auditJson = audit.ToJson();
            AssertNoLeak(auditJson, sensitiveName, sensitiveNote, sensitiveRole, sensitiveCompany, "audit_log collection");
        }

        // 5. Backup round-trip: .rrbak must NOT leak plaintext of the name or notes
        var backupFile = Path.Combine(Path.GetTempPath(), $"contact_leak_test_{Guid.NewGuid():N}.rrbak");
        try
        {
            const string passphrase = "CorrectHorseBatteryStaple123!";
            var backupEnv = new Dictionary<string, string>(env)
            {
                ["ROLERADAR_BACKUP_PASSPHRASE"] = passphrase
            };

            var (backupCode, backupOut, backupErr) = await RunCliAsync(
                cliDllPath,
                $"backup --output \"{backupFile}\" --passphrase \"{passphrase}\"",
                backupEnv);
            Assert.Equal(0, backupCode);
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

    private static string GetCliDllPath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "RoleRadar.Cli.dll");
        if (!File.Exists(path))
        {
            path = typeof(RoleRadar.Cli.Program).Assembly.Location;
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
