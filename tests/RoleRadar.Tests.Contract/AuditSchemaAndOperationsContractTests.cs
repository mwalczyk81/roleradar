using System.Diagnostics;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Tests.Contract.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Contract;

public sealed class AuditSchemaAndOperationsContractTests : IAsyncLifetime
{
    private readonly SharedMongoFixture _fixture;
    private readonly string _databaseName;
    private MongoClient _client = null!;
    private IMongoDatabase _database = null!;

    public AuditSchemaAndOperationsContractTests(SharedMongoFixture fixture)
    {
        _fixture = fixture;
        _databaseName = _fixture.CreateDatabaseName("roleradar_audit_contract");
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
    public async Task ShippedHosts_All15TrackingOperations_ProduceExactAuditEntriesWithEntityTypeAndId()
    {
        var mcpDllPath = McpHostLocator.GetMcpDllPath();
        var cliDllPath = GetCliDllPath();
        var env = _fixture.CreateProcessEnvironment(_databaseName);

        var auditCol = _database.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.AuditLogCollectionName);

        await using var mcpClient = await McpProcessClient.StartAsync(mcpDllPath, env);

        // 1. company_create
        var compRes = await mcpClient.CallToolAsync("company_record", new
        {
            name = "Audit Test Corp",
            ownershipType = "public",
            status = "target"
        });
        Assert.True(compRes.GetProperty("ok").GetBoolean());
        var companyIdStr = compRes.GetProperty("data").GetProperty("company").GetProperty("id").GetString()!;
        var companyId = Guid.Parse(companyIdStr);
        var companyTokenStr = compRes.GetProperty("data").GetProperty("company").GetProperty("versionToken").GetString()!;
        var companyToken = Guid.Parse(companyTokenStr);

        var compCreateAudit = await auditCol.Find(Builders<BsonDocument>.Filter.Eq("operation", "company_create")).FirstOrDefaultAsync();
        Assert.NotNull(compCreateAudit);
        Assert.Equal("company", compCreateAudit["entity_type"].AsString);
        Assert.Equal(companyId, compCreateAudit["entity_id"].AsGuid);
        Assert.True(compCreateAudit.Contains("details"));

        // 2. company_update
        var compUpRes = await mcpClient.CallToolAsync("company_record", new
        {
            id = companyIdStr,
            name = "Audit Test Corp",
            versionToken = companyTokenStr,
            description = "Updated corp description"
        });
        Assert.True(compUpRes.GetProperty("ok").GetBoolean(), compUpRes.ToString());
        var updatedCompanyToken = compUpRes.GetProperty("data").GetProperty("company").GetProperty("versionToken").GetString()!;

        var compUpdateAudit = await auditCol.Find(Builders<BsonDocument>.Filter.Eq("operation", "company_update")).FirstOrDefaultAsync();
        Assert.NotNull(compUpdateAudit);
        Assert.Equal("company", compUpdateAudit["entity_type"].AsString);
        Assert.Equal(companyId, compUpdateAudit["entity_id"].AsGuid);

        // 3. career_source_upsert
        var csRes = await mcpClient.CallToolAsync("company_record", new
        {
            id = companyIdStr,
            name = "Audit Test Corp",
            versionToken = updatedCompanyToken,
            careerSources = new[]
            {
                new { atsType = "greenhouse", tenant = "auditcorp", isActive = true }
            }
        });
        Assert.True(csRes.GetProperty("ok").GetBoolean(), csRes.ToString());

        var csAudit = await auditCol.Find(Builders<BsonDocument>.Filter.Eq("operation", "career_source_upsert")).FirstOrDefaultAsync();
        Assert.NotNull(csAudit);
        Assert.Equal("company", csAudit["entity_type"].AsString);
        Assert.Equal(companyId, csAudit["entity_id"].AsGuid);

        // 4. posting_create
        var postRes = await mcpClient.CallToolAsync("posting_record", new
        {
            companyId = companyIdStr,
            title = "Principal Audit Engineer",
            sourceKind = "ats",
            atsTenant = "auditcorp",
            requisitionId = "REQ-AUDIT-1"
        });
        Assert.True(postRes.GetProperty("ok").GetBoolean());
        var postingIdStr = postRes.GetProperty("data").GetProperty("posting").GetProperty("id").GetString()!;
        var postingId = Guid.Parse(postingIdStr);
        var postTokenStr = postRes.GetProperty("data").GetProperty("posting").GetProperty("versionToken").GetString()!;

        var postCreateAudit = await auditCol.Find(Builders<BsonDocument>.Filter.Eq("operation", "posting_create")).FirstOrDefaultAsync();
        Assert.NotNull(postCreateAudit);
        Assert.Equal("posting", postCreateAudit["entity_type"].AsString);
        Assert.Equal(postingId, postCreateAudit["entity_id"].AsGuid);

        // 5. posting_update
        var postUpRes = await mcpClient.CallToolAsync("posting_record", new
        {
            postingId = postingIdStr,
            versionToken = postTokenStr,
            location = "Seattle, WA"
        });
        Assert.True(postUpRes.GetProperty("ok").GetBoolean());
        var postUpTokenStr = postUpRes.GetProperty("data").GetProperty("posting").GetProperty("versionToken").GetString()!;

        var postUpdateAudit = await auditCol.Find(Builders<BsonDocument>.Filter.Eq("operation", "posting_update")).FirstOrDefaultAsync();
        Assert.NotNull(postUpdateAudit);
        Assert.Equal("posting", postUpdateAudit["entity_type"].AsString);
        Assert.Equal(postingId, postUpdateAudit["entity_id"].AsGuid);

        // 6. posting_signal_append
        var sigRes = await mcpClient.CallToolAsync("posting_record", new
        {
            companyId = companyIdStr,
            title = "Principal Audit Engineer",
            sourceKind = "ats",
            atsTenant = "auditcorp",
            requisitionId = "REQ-AUDIT-1",
            signals = new[]
            {
                new { signalType = "gone", evidence = "404 Not Found" }
            }
        });
        Assert.True(sigRes.GetProperty("ok").GetBoolean());

        var sigAudit = await auditCol.Find(Builders<BsonDocument>.Filter.Eq("operation", "posting_signal_append")).FirstOrDefaultAsync();
        Assert.NotNull(sigAudit);
        Assert.Equal("posting", sigAudit["entity_type"].AsString);
        Assert.Equal(postingId, sigAudit["entity_id"].AsGuid);

        // 7. posting_verified_bump (mutate last_verified_at to yesterday, then re-encounter)
        var postingsCol = _database.GetCollection<BsonDocument>(TrackingMongoOptions.PostingsCollectionName);
        await postingsCol.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(postingId, GuidRepresentation.Standard)),
            Builders<BsonDocument>.Update.Set("last_verified_at", new BsonDateTime(DateTime.UtcNow.AddDays(-2))));

        var verifyBumpRes = await mcpClient.CallToolAsync("posting_record", new
        {
            companyId = companyIdStr,
            title = "Principal Audit Engineer",
            sourceKind = "ats",
            atsTenant = "auditcorp",
            requisitionId = "REQ-AUDIT-1"
        });
        Assert.True(verifyBumpRes.GetProperty("ok").GetBoolean());

        var verifyBumpAudit = await auditCol.Find(Builders<BsonDocument>.Filter.Eq("operation", "posting_verified_bump")).FirstOrDefaultAsync();
        Assert.NotNull(verifyBumpAudit);
        Assert.Equal("posting", verifyBumpAudit["entity_type"].AsString);
        Assert.Equal(postingId, verifyBumpAudit["entity_id"].AsGuid);

        // Fetch latest version token for posting
        var currentPostDoc = await postingsCol.Find(Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(postingId, GuidRepresentation.Standard))).FirstAsync();
        var latestPostToken = currentPostDoc["version_token"].AsGuid.ToString();

        // 8. posting_transition
        var transRes = await mcpClient.CallToolAsync("posting_transition", new
        {
            postingId = postingIdStr,
            targetDisposition = "screened_out",
            reason = "manual_override",
            versionToken = latestPostToken
        });
        Assert.True(transRes.GetProperty("ok").GetBoolean());

        var transAudit = await auditCol.Find(Builders<BsonDocument>.Filter.Eq("operation", "posting_transition")).FirstOrDefaultAsync();
        Assert.NotNull(transAudit);
        Assert.Equal("posting", transAudit["entity_type"].AsString);
        Assert.Equal(postingId, transAudit["entity_id"].AsGuid);

        // 9. application_create
        // Create another lead posting for application
        var appPostRes = await mcpClient.CallToolAsync("posting_record", new
        {
            companyId = companyIdStr,
            title = "Application Candidate Role",
            sourceKind = "ats",
            atsTenant = "auditcorp",
            requisitionId = "REQ-APP-ROLE"
        });
        var appPostIdStr = appPostRes.GetProperty("data").GetProperty("posting").GetProperty("id").GetString()!;
        var appPostId = Guid.Parse(appPostIdStr);

        var appRes = await mcpClient.CallToolAsync("application_record", new
        {
            postingId = appPostIdStr,
            action = "create",
            submittedDate = "2026-10-18",
            resumeVariant = "tailored"
        });
        Assert.True(appRes.GetProperty("ok").GetBoolean(), appRes.ToString());
        var appIdStr = appRes.GetProperty("data").GetProperty("applicationId").GetString()!;
        var appId = Guid.Parse(appIdStr);

        var appCreateAudit = await auditCol.Find(Builders<BsonDocument>.Filter.Eq("operation", "application_create")).FirstOrDefaultAsync();
        Assert.NotNull(appCreateAudit);
        Assert.Equal("application", appCreateAudit["entity_type"].AsString);
        Assert.Equal(appId, appCreateAudit["entity_id"].AsGuid);

        // 10. application_event_append
        var appEventRes = await mcpClient.CallToolAsync("application_record", new
        {
            postingId = appPostIdStr,
            action = "append_event",
            @event = new
            {
                eventType = "interview",
                eventDate = "2026-10-19",
                notes = "Initial screening call"
            }
        });
        Assert.True(appEventRes.GetProperty("ok").GetBoolean(), appEventRes.ToString());

        var appEventAudit = await auditCol.Find(Builders<BsonDocument>.Filter.Eq("operation", "application_event_append")).FirstOrDefaultAsync();
        Assert.NotNull(appEventAudit);
        Assert.Equal("application", appEventAudit["entity_type"].AsString);
        Assert.Equal(appId, appEventAudit["entity_id"].AsGuid);

        // 11. criterion_revision (initial create & revision)
        var crit1Res = await mcpClient.CallToolAsync("tracker_log", new
        {
            action = "criterion_revision",
            key = "min_salary",
            category = "hard_skip",
            value = "180000",
            provenance = "stated",
            effectiveFrom = "2026-01-01T00:00:00Z"
        });
        Assert.True(crit1Res.GetProperty("ok").GetBoolean());
        var crit1Id = Guid.Parse(crit1Res.GetProperty("data").GetProperty("recordId").GetString()!);
        var crit1Token = crit1Res.GetProperty("data").GetProperty("versionToken").GetString()!;

        var critCreateAudit = await auditCol.Find(Builders<BsonDocument>.Filter.Eq("operation", "criterion_revision")).FirstOrDefaultAsync();
        Assert.NotNull(critCreateAudit);
        Assert.Equal("criterion", critCreateAudit["entity_type"].AsString);
        Assert.Equal(crit1Id, critCreateAudit["entity_id"].AsGuid);

        // 12. criterion_deactivate
        var critDeactRes = await mcpClient.CallToolAsync("tracker_log", new
        {
            action = "criterion_deactivate",
            key = "min_salary",
            versionToken = crit1Token
        });
        Assert.True(critDeactRes.GetProperty("ok").GetBoolean());

        var critDeactAudit = await auditCol.Find(Builders<BsonDocument>.Filter.Eq("operation", "criterion_deactivate")).FirstOrDefaultAsync();
        Assert.NotNull(critDeactAudit);
        Assert.Equal("criterion", critDeactAudit["entity_type"].AsString);

        // 13. search_run_log
        var srRes = await mcpClient.CallToolAsync("tracker_log", new
        {
            action = "search_run",
            startedAt = "2026-10-18T12:00:00Z",
            newPostingsCount = 4,
            notes = "Audit run sweep"
        });
        Assert.True(srRes.GetProperty("ok").GetBoolean());
        var srId = Guid.Parse(srRes.GetProperty("data").GetProperty("recordId").GetString()!);

        var srAudit = await auditCol.Find(Builders<BsonDocument>.Filter.Eq("operation", "search_run_log")).FirstOrDefaultAsync();
        Assert.NotNull(srAudit);
        Assert.Equal("search_run", srAudit["entity_type"].AsString);
        Assert.Equal(srId, srAudit["entity_id"].AsGuid);

        // 14. contact_add (via launched CLI)
        var (cAddCode, cAddStdout, _) = await RunCliAsync(cliDllPath, "contact add --name \"Confidential Jane Recruiter\" --role \"Lead Recruiter\" --json", env);
        Assert.Equal(0, cAddCode);
        using var cAddDoc = JsonDocument.Parse(cAddStdout);
        var contactIdStr = cAddDoc.RootElement.GetProperty("data").GetProperty("id").GetString()!;
        var contactId = Guid.Parse(contactIdStr);
        var contactTokenStr = cAddDoc.RootElement.GetProperty("data").GetProperty("versionToken").GetString()!;

        var contactAddAudit = await auditCol.Find(Builders<BsonDocument>.Filter.Eq("operation", "contact_add")).FirstOrDefaultAsync();
        Assert.NotNull(contactAddAudit);
        Assert.Equal("contact", contactAddAudit["entity_type"].AsString);
        Assert.Equal(contactId, contactAddAudit["entity_id"].AsGuid);
        // Assert details has changed_fields only, no PII
        var contactAddRaw = contactAddAudit.ToJson();
        Assert.DoesNotContain("Confidential Jane", contactAddRaw, StringComparison.OrdinalIgnoreCase);

        // 15. contact_redact (via launched CLI)
        var (cRedactCode, cRedactStdout, _) = await RunCliAsync(cliDllPath, $"contact redact --id {contactIdStr} --version-token {contactTokenStr} --force --json", env);
        Assert.Equal(0, cRedactCode);

        var contactRedactAudit = await auditCol.Find(Builders<BsonDocument>.Filter.Eq("operation", "contact_redact")).FirstOrDefaultAsync();
        Assert.NotNull(contactRedactAudit);
        Assert.Equal("contact", contactRedactAudit["entity_type"].AsString);
        Assert.Equal(contactId, contactRedactAudit["entity_id"].AsGuid);
        var contactRedactRaw = contactRedactAudit.ToJson();
        Assert.DoesNotContain("Confidential Jane", contactRedactRaw, StringComparison.OrdinalIgnoreCase);

        // Total unique operation names check
        var allAudits = await auditCol.Find(Builders<BsonDocument>.Filter.Empty).ToListAsync();
        var recordedOperations = allAudits.Select(a => a["operation"].AsString).Distinct().ToHashSet();

        var expectedOperations = new[]
        {
            "company_create", "company_update", "career_source_upsert",
            "posting_create", "posting_update", "posting_verified_bump", "posting_transition", "posting_signal_append",
            "application_create", "application_event_append",
            "criterion_revision", "criterion_deactivate",
            "search_run_log",
            "contact_add", "contact_redact"
        };

        foreach (var op in expectedOperations)
        {
            Assert.Contains(op, recordedOperations);
        }
    }

    [Fact]
    public async Task ShippedHost_CompanyRecord_ChangingCompanyFieldsWithAndWithoutCareerSources_EmitsCorrectAuditsAndRotatesVersionToken()
    {
        var mcpDllPath = McpHostLocator.GetMcpDllPath();
        var env = _fixture.CreateProcessEnvironment(_databaseName);

        var auditCol = _database.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.AuditLogCollectionName);
        var companyCol = _database.GetCollection<BsonDocument>(TrackingMongoOptions.CompaniesCollectionName);

        await using var mcpClient = await McpProcessClient.StartAsync(mcpDllPath, env);

        // 1. Create company
        var createRes = await mcpClient.CallToolAsync("company_record", new
        {
            name = "Dual Audit Corp",
            ownershipType = "private",
            status = "target",
            description = "Initial description"
        });
        Assert.True(createRes.GetProperty("ok").GetBoolean(), createRes.ToString());
        var companyData = createRes.GetProperty("data").GetProperty("company");
        var companyId = Guid.Parse(companyData.GetProperty("id").GetString()!);
        var initialToken = Guid.Parse(companyData.GetProperty("versionToken").GetString()!);

        var initialDoc = await companyCol.Find(Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(companyId, GuidRepresentation.Standard))).FirstOrDefaultAsync();
        Assert.NotNull(initialDoc);
        var initialUpdatedAt = initialDoc["updated_at"].ToUniversalTime();

        // 2. Change company fields ONLY -> company_update audit, rotates version token, updates updated_at
        var updateCompanyRes = await mcpClient.CallToolAsync("company_record", new
        {
            id = companyId.ToString(),
            name = "Dual Audit Corp",
            versionToken = initialToken.ToString(),
            description = "Updated description only"
        });
        Assert.True(updateCompanyRes.GetProperty("ok").GetBoolean(), updateCompanyRes.ToString());
        var updatedToken1 = Guid.Parse(updateCompanyRes.GetProperty("data").GetProperty("company").GetProperty("versionToken").GetString()!);
        Assert.NotEqual(initialToken, updatedToken1);

        var docAfterUpdate1 = await companyCol.Find(Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(companyId, GuidRepresentation.Standard))).FirstOrDefaultAsync();
        Assert.NotNull(docAfterUpdate1);
        Assert.Equal("Updated description only", docAfterUpdate1["description"].AsString);
        Assert.True(docAfterUpdate1["updated_at"].ToUniversalTime() >= initialUpdatedAt);

        var companyUpdateAudits1 = await auditCol.Find(Builders<BsonDocument>.Filter.Eq("operation", "company_update")).ToListAsync();
        Assert.Single(companyUpdateAudits1);
        var csAudits1 = await auditCol.Find(Builders<BsonDocument>.Filter.Eq("operation", "career_source_upsert")).ToListAsync();
        Assert.Empty(csAudits1);

        // 3. Change BOTH company fields AND career sources -> BOTH company_update AND career_source_upsert audits in same transaction!
        var dualUpdateRes = await mcpClient.CallToolAsync("company_record", new
        {
            id = companyId.ToString(),
            name = "Dual Audit Corp",
            versionToken = updatedToken1.ToString(),
            description = "Updated description with career source",
            careerSources = new[]
            {
                new { atsType = "greenhouse", tenant = "dualaudit", isActive = true }
            }
        });
        Assert.True(dualUpdateRes.GetProperty("ok").GetBoolean(), dualUpdateRes.ToString());
        var updatedToken2 = Guid.Parse(dualUpdateRes.GetProperty("data").GetProperty("company").GetProperty("versionToken").GetString()!);
        Assert.NotEqual(updatedToken1, updatedToken2);

        var docAfterUpdate2 = await companyCol.Find(Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(companyId, GuidRepresentation.Standard))).FirstOrDefaultAsync();
        Assert.NotNull(docAfterUpdate2);
        Assert.Equal("Updated description with career source", docAfterUpdate2["description"].AsString);

        var companyUpdateAudits2 = await auditCol.Find(Builders<BsonDocument>.Filter.Eq("operation", "company_update")).ToListAsync();
        Assert.Equal(2, companyUpdateAudits2.Count);

        var csAudits2 = await auditCol.Find(Builders<BsonDocument>.Filter.Eq("operation", "career_source_upsert")).ToListAsync();
        Assert.Single(csAudits2);
        Assert.Equal("company", csAudits2[0]["entity_type"].AsString);
        Assert.Equal(companyId, csAudits2[0]["entity_id"].AsGuid);

        // 4. Update career sources ONLY (no company fields changed) -> career_source_upsert audit only
        var csOnlyRes = await mcpClient.CallToolAsync("company_record", new
        {
            id = companyId.ToString(),
            name = "Dual Audit Corp",
            versionToken = updatedToken2.ToString(),
            description = "Updated description with career source",
            careerSources = new[]
            {
                new { atsType = "lever", tenant = "dualaudit", isActive = true }
            }
        });
        Assert.True(csOnlyRes.GetProperty("ok").GetBoolean(), csOnlyRes.ToString());

        var companyUpdateAudits3 = await auditCol.Find(Builders<BsonDocument>.Filter.Eq("operation", "company_update")).ToListAsync();
        Assert.Equal(2, companyUpdateAudits3.Count); // unchanged

        var csAudits3 = await auditCol.Find(Builders<BsonDocument>.Filter.Eq("operation", "career_source_upsert")).ToListAsync();
        Assert.Equal(2, csAudits3.Count); // incremented by 1
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
        var psi = new System.Diagnostics.ProcessStartInfo
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

        using var proc = System.Diagnostics.Process.Start(psi)!;
        Assert.NotNull(proc);

        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();

        await proc.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        return (proc.ExitCode, stdout, stderr);
    }
}
