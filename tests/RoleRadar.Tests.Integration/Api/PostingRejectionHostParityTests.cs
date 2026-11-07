using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Tests.Integration.Fixtures;
using RoleRadar.Tests.Integration.Tracking;
using Xunit;

namespace RoleRadar.Tests.Integration.Api;

/// <summary>
/// Feature 008 phase 2: the same rejection through the shipped MCP stdio server and through the API produces
/// the same posting, application, event and audit records, apart from actor and timestamps. Two isolated
/// databases on the Testcontainers replica set, equivalent fictional data.
/// </summary>
[Trait("Lane", "images")]
public sealed class PostingRejectionHostParityTests(SharedMongoFixture sharedMongo)
{
    private static readonly string[] AuditOperations = ["posting_transition", "application_event_append"];

    private static readonly string[] ApplicationFields =
        ["outcome", "outcome_reason", "has_first_response", "has_rejection", "rejected_before_interview", "has_interview", "has_offer", "has_last_event"];

    private readonly SharedMongoFixture _sharedMongo = sharedMongo;

    [Fact]
    public async Task ShippedMcpHost_ListsTwelveTools_AndPostingTransitionDeclaresEventDate()
    {
        var env = _sharedMongo.CreateProcessEnvironment(_sharedMongo.CreateDatabaseName("rrtest_reject_parity_tools"));
        await using var mcp = await McpProcessClient.StartAsync(McpHostLocator.GetMcpDllPath(), env);

        var tools = (await mcp.ListToolsAsync()).GetProperty("tools").EnumerateArray().ToList();
        Assert.Equal(12, tools.Count);

        var transition = Assert.Single(tools, t => t.GetProperty("name").GetString() == "posting_transition");
        Assert.Contains("also records a rejected event on its application", transition.GetProperty("description").GetString(), StringComparison.Ordinal);
        var eventDate = transition.GetProperty("inputSchema").GetProperty("properties").GetProperty("eventDate");
        Assert.Equal("string", eventDate.GetProperty("type").GetString());
        Assert.False(string.IsNullOrWhiteSpace(eventDate.GetProperty("description").GetString()));
        var required = transition.GetProperty("inputSchema").TryGetProperty("required", out var req)
            ? req.EnumerateArray().Select(r => r.GetString()).ToList()
            : [];
        Assert.DoesNotContain("eventDate", required);
    }

    [Fact]
    public async Task SameRejection_ThroughMcpAndThroughTheApi_ProducesTheSameRecords()
    {
        // ---- MCP side: shipped stdio host, its own isolated database ----
        var mcpDb = _sharedMongo.CreateDatabaseName("rrtest_reject_parity_mcp");
        await using var mcp = await McpProcessClient.StartAsync(McpHostLocator.GetMcpDllPath(), _sharedMongo.CreateProcessEnvironment(mcpDb));

        var company = await mcp.CallToolAsync("company_record", new { name = "Harbor Analytics", ownershipType = "private", status = "neutral" });
        var companyId = company.GetProperty("data").GetProperty("company").GetProperty("id").GetGuid();
        var posting = await mcp.CallToolAsync("posting_record", new
        {
            companyId = companyId.ToString(),
            title = "Staff Platform Engineer",
            requisitionId = "HAR-7001",
            atsTenant = "harbor"
        });
        var mcpPostingId = posting.GetProperty("data").GetProperty("posting").GetProperty("id").GetGuid();
        var application = await mcp.CallToolAsync("application_record", new
        {
            postingId = mcpPostingId.ToString(),
            action = "create",
            submittedDate = "2026-09-01",
            resumeVariant = "tailored"
        });
        Assert.True(application.GetProperty("ok").GetBoolean(), application.GetRawText());
        var mcpApplicationId = application.GetProperty("data").GetProperty("applicationId").GetGuid();
        var versionToken = (await mcp.CallToolAsync("tracker_query", new { entity = "postings", postingId = mcpPostingId.ToString() }))
            .GetProperty("data").GetProperty("items")[0].GetProperty("versionToken").GetGuid();

        var mcpResult = await mcp.CallToolAsync("posting_transition", new
        {
            postingId = mcpPostingId.ToString(),
            targetDisposition = "rejected",
            versionToken = versionToken.ToString(),
            reason = "Rejected by email",
            eventDate = "2026-09-15"
        });
        Assert.True(mcpResult.GetProperty("ok").GetBoolean(), mcpResult.GetRawText());
        Assert.Equal(0, mcpResult.GetProperty("data").GetProperty("warnings").GetArrayLength());

        // ---- API side: in-process API, its own isolated database ----
        await using var api = await PostingRejectionSyncHost.StartAsync(_sharedMongo, "rrtest_reject_parity_api");
        var seeded = await api.SeedAppliedPostingAsync("Staff Platform Engineer");
        var response = await api.Client.PostAsJsonAsync($"/api/v1/postings/{seeded.PostingId}/transition", new
        {
            toDisposition = "rejected",
            reason = "Rejected by email",
            versionToken = seeded.VersionToken.ToString(),
            eventDate = "2026-09-15"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, detail.GetProperty("warnings").GetArrayLength());

        // ---- Compare the stored records ----
        var mcpClientDb = new MongoClient(_sharedMongo.ConnectionString).GetDatabase(mcpDb);
        var mcpSnapshot = await SnapshotAsync(mcpClientDb, mcpPostingId, mcpApplicationId);
        var apiSnapshot = await SnapshotAsync(api.Database, seeded.PostingId, seeded.ApplicationId);

        Assert.Equal(apiSnapshot.Posting, mcpSnapshot.Posting);
        Assert.Equal(apiSnapshot.Application, mcpSnapshot.Application);
        Assert.Equal(apiSnapshot.Events, mcpSnapshot.Events);
        Assert.Equal(apiSnapshot.Audit, mcpSnapshot.Audit);
        Assert.Contains("posting_transition", mcpSnapshot.Events.Single(e => e.StartsWith("rejected|", StringComparison.Ordinal)));
    }

    /// <summary>Stable, host-independent view of what the transition wrote. Ids, timestamps and actors are left out.</summary>
    private static async Task<(string Posting, string Application, List<string> Events, List<string> Audit)> SnapshotAsync(
        IMongoDatabase db, Guid postingId, Guid applicationId)
    {
        var posting = await db.GetCollection<BsonDocument>(TrackingMongoOptions.PostingsCollectionName)
            .Find(Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(postingId, GuidRepresentation.Standard))).SingleAsync();
        var application = await db.GetCollection<BsonDocument>(TrackingMongoOptions.ApplicationsCollectionName)
            .Find(Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(applicationId, GuidRepresentation.Standard))).SingleAsync();
        var events = await db.GetCollection<BsonDocument>(TrackingMongoOptions.ApplicationEventsCollectionName)
            .Find(Builders<BsonDocument>.Filter.Eq("application_id", new BsonBinaryData(applicationId, GuidRepresentation.Standard))).ToListAsync();
        var audits = await db.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.AuditLogCollectionName)
            .Find(Builders<BsonDocument>.Filter.In("operation", AuditOperations)).ToListAsync();

        string Flag(BsonDocument d, string f) => d.Contains(f) && !d[f].IsBsonNull ? d[f].ToString()! : "-";

        var postingView = $"{posting["disposition"]}|{Flag(posting, "disposition_reason")}|{Flag(posting, "has_application")}";
        var applicationView = string.Join(
            "|",
            ApplicationFields.Select(f => $"{f}={Flag(application, f)}"));
        var eventViews = events
            .OrderBy(e => e["event_date"].AsString, StringComparer.Ordinal)
            .ThenBy(e => e["event_type"].AsString, StringComparer.Ordinal)
            .Select(e => $"{e["event_type"].AsString}|{e["event_date"].AsString}|{Flag(e, "event_source")}|{Flag(e, "notes")}")
            .ToList();
        var auditViews = audits
            .Select(a =>
            {
                var after = a["details"]["after"].AsBsonDocument;
                var keys = string.Join(",", after.Names.OrderBy(n => n, StringComparer.Ordinal));
                var correlated = after.Contains("correlation_id") && !string.IsNullOrWhiteSpace(after["correlation_id"].AsString);
                return $"{a["operation"].AsString}|{a["entity_type"].AsString}|{keys}|correlated={correlated}";
            })
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();

        return (postingView, applicationView, eventViews, auditViews);
    }
}
