using System.Diagnostics;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Tests.Contract.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Contract;

public sealed class ContactCliContractTests : IAsyncLifetime
{
    private readonly SharedMongoFixture _fixture;
    private readonly string _databaseName;
    private MongoClient _client = null!;
    private IMongoDatabase _database = null!;

    public ContactCliContractTests(SharedMongoFixture fixture)
    {
        _fixture = fixture;
        _databaseName = _fixture.CreateDatabaseName("roleradar_contact_contract");
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
    public async Task Contact_Add_CreatesContactWithAllFields_AndAuditsFieldNamesOnly()
    {
        var cliDllPath = GetCliDllPath();
        var env = _fixture.CreateProcessEnvironment(_databaseName);

        // 1. Run roleradar contact add --json
        var contactName = "Jane Recruiter Doe";
        var role = "Senior Talent Acquisition Partner";
        var company = "Acme Global Systems";
        var relationship = "recruiter";
        var notes = "Met at Seattle Tech Hiring Summit 2026.";

        var (exitCode, stdout, stderr) = await RunCliAsync(
            cliDllPath,
            $"contact add --name \"{contactName}\" --role \"{role}\" --company \"{company}\" --relationship \"{relationship}\" --notes \"{notes}\" --json",
            env);

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("Unhandled exception", stderr, StringComparison.OrdinalIgnoreCase);

        using var jsonDoc = JsonDocument.Parse(stdout);
        var root = jsonDoc.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        var data = root.GetProperty("data");
        var contactIdStr = data.GetProperty("id").GetString();
        Assert.NotNull(contactIdStr);
        var contactId = Guid.Parse(contactIdStr);
        var versionTokenStr = data.GetProperty("versionToken").GetString();
        Assert.NotNull(versionTokenStr);

        // 2. Query MongoDB contacts collection directly to verify storage
        var contactsCol = _database.GetCollection<BsonDocument>(TrackingMongoOptions.ContactsCollectionName);
        var stored = await contactsCol.Find(Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(contactId, GuidRepresentation.Standard))).FirstOrDefaultAsync();
        Assert.NotNull(stored);
        Assert.Equal(contactName, stored["name"].AsString);
        Assert.Equal(role, stored["role"].AsString);
        Assert.Equal(company, stored["company_affiliation"].AsString);
        Assert.Equal(relationship, stored["relationship"].AsString);
        Assert.Equal(notes, stored["notes"].AsString);
        Assert.False(stored["is_redacted"].AsBoolean);
        Assert.Equal(Guid.Parse(versionTokenStr), stored["version_token"].AsGuid);

        // 3. Inspect audit_log collection: must have operation contact_add, summary with field names only, zero names or notes
        var auditCol = _database.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.AuditLogCollectionName);
        var audit = await auditCol.Find(Builders<BsonDocument>.Filter.Eq("target_note_id", new BsonBinaryData(contactId, GuidRepresentation.Standard))).FirstOrDefaultAsync();
        Assert.NotNull(audit);
        Assert.Equal("contact_add", audit["operation"].AsString);
        var afterSummary = audit["after_summary"].AsBsonDocument;
        var auditRaw = audit.ToJson();
        Assert.DoesNotContain("Jane", auditRaw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Acme", auditRaw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Seattle", auditRaw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Talent", auditRaw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Contact_Get_ById_ReturnsContactDetails()
    {
        var cliDllPath = GetCliDllPath();
        var env = _fixture.CreateProcessEnvironment(_databaseName);

        // 1. Add contact
        var (addCode, addStdout, _) = await RunCliAsync(
            cliDllPath,
            "contact add --name \"Alex Referral Peer\" --role \"Staff SRE\" --relationship \"referral\" --json",
            env);
        Assert.Equal(0, addCode);
        using var addDoc = JsonDocument.Parse(addStdout);
        var contactId = addDoc.RootElement.GetProperty("data").GetProperty("id").GetString()!;

        // 2. Get contact by ID
        var (getCode, getStdout, getStderr) = await RunCliAsync(
            cliDllPath,
            $"contact get --id {contactId} --json",
            env);

        Assert.Equal(0, getCode);
        Assert.DoesNotContain("Unhandled exception", getStderr, StringComparison.OrdinalIgnoreCase);

        using var getDoc = JsonDocument.Parse(getStdout);
        var getData = getDoc.RootElement.GetProperty("data");
        Assert.Equal(contactId, getData.GetProperty("id").GetString());
        Assert.Equal("Alex Referral Peer", getData.GetProperty("name").GetString());
        Assert.Equal("Staff SRE", getData.GetProperty("role").GetString());
        Assert.Equal("referral", getData.GetProperty("relationship").GetString());
    }

    [Fact]
    public async Task Contact_Redact_BlanksPersonalData_SetsIsRedacted_AndIsIdempotent()
    {
        var cliDllPath = GetCliDllPath();
        var env = _fixture.CreateProcessEnvironment(_databaseName);

        // 1. Add contact
        var (addCode, addStdout, _) = await RunCliAsync(
            cliDllPath,
            "contact add --name \"Sensitive Executive Contact\" --role \"VP Engineering\" --company \"Stealth Corp\" --notes \"Private salary expectations discussed.\" --json",
            env);
        Assert.Equal(0, addCode);
        using var addDoc = JsonDocument.Parse(addStdout);
        var contactId = addDoc.RootElement.GetProperty("data").GetProperty("id").GetString()!;
        var versionToken = addDoc.RootElement.GetProperty("data").GetProperty("versionToken").GetString()!;

        // 2. Redact with stale version token -> fails with StaleVersionToken
        var (staleCode, staleStdout, _) = await RunCliAsync(
            cliDllPath,
            $"contact redact --id {contactId} --version-token {Guid.NewGuid()} --force --json",
            env);
        Assert.NotEqual(0, staleCode);
        using var staleDoc = JsonDocument.Parse(staleStdout);
        Assert.Equal("StaleVersionToken", staleDoc.RootElement.GetProperty("error").GetProperty("code").GetString());

        // 3. Redact successfully
        var (redactCode, redactStdout, redactStderr) = await RunCliAsync(
            cliDllPath,
            $"contact redact --id {contactId} --version-token {versionToken} --force --json",
            env);
        Assert.Equal(0, redactCode);
        Assert.DoesNotContain("Unhandled exception", redactStderr, StringComparison.OrdinalIgnoreCase);

        using var redactDoc = JsonDocument.Parse(redactStdout);
        Assert.True(redactDoc.RootElement.GetProperty("ok").GetBoolean());
        var newVersionToken = redactDoc.RootElement.GetProperty("data").GetProperty("versionToken").GetString()!;
        Assert.NotEqual(versionToken, newVersionToken);

        // 4. Assert MongoDB record has personal fields blanked
        var contactsCol = _database.GetCollection<BsonDocument>(TrackingMongoOptions.ContactsCollectionName);
        var stored = await contactsCol.Find(Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(Guid.Parse(contactId), GuidRepresentation.Standard))).FirstOrDefaultAsync();
        Assert.NotNull(stored);
        Assert.Equal("[REDACTED]", stored["name"].AsString);
        Assert.True(stored["role"].IsBsonNull);
        Assert.True(stored["company_affiliation"].IsBsonNull);
        Assert.True(stored["notes"].IsBsonNull);
        Assert.Equal("redacted", stored["relationship"].AsString);
        Assert.True(stored["is_redacted"].AsBoolean);

        // 5. Assert audit entry contains zero PII
        var auditCol = _database.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.AuditLogCollectionName);
        var redactAudit = await auditCol.Find(Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("target_note_id", new BsonBinaryData(Guid.Parse(contactId), GuidRepresentation.Standard)),
            Builders<BsonDocument>.Filter.Eq("operation", "contact_redact"))).FirstOrDefaultAsync();
        Assert.NotNull(redactAudit);
        var redactAuditRaw = redactAudit.ToJson();
        Assert.DoesNotContain("Sensitive", redactAuditRaw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Stealth", redactAuditRaw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("salary", redactAuditRaw, StringComparison.OrdinalIgnoreCase);

        // 6. Redacting an already-redacted contact is idempotent (writes nothing, writes no audit entry)
        var auditCountBefore = await auditCol.CountDocumentsAsync(Builders<BsonDocument>.Filter.Eq("target_note_id", new BsonBinaryData(Guid.Parse(contactId), GuidRepresentation.Standard)));
        var (idemCode, idemStdout, _) = await RunCliAsync(
            cliDllPath,
            $"contact redact --id {contactId} --force --json",
            env);
        Assert.Equal(0, idemCode);
        using var idemDoc = JsonDocument.Parse(idemStdout);
        Assert.True(idemDoc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("already_redacted", idemDoc.RootElement.GetProperty("data").GetProperty("status").GetString());

        var auditCountAfter = await auditCol.CountDocumentsAsync(Builders<BsonDocument>.Filter.Eq("target_note_id", new BsonBinaryData(Guid.Parse(contactId), GuidRepresentation.Standard)));
        Assert.Equal(auditCountBefore, auditCountAfter);
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
