using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Tests.Contract.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Contract;

public sealed class TrackerQueryMultiEntityContractTests : IAsyncLifetime
{
    private readonly SharedMongoFixture _fixture;
    private readonly string _databaseName;
    private MongoClient _client = null!;
    private IMongoDatabase _database = null!;

    private static readonly string[] CloudSecurityTags = ["cloud", "security"];
    private static readonly string[] SecurityTags = ["security"];
    private static readonly string[] ConsultingTags = ["consulting"];

    public TrackerQueryMultiEntityContractTests(SharedMongoFixture fixture)
    {
        _fixture = fixture;
        _databaseName = _fixture.CreateDatabaseName("roleradar_multi_query");
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
    public async Task ShippedHost_TrackerQuery_ContactsDiscriminator_ReturnsValidationError()
    {
        var mcpDllPath = McpHostLocator.GetMcpDllPath();
        var env = _fixture.CreateProcessEnvironment(_databaseName);

        await using var mcpClient = await McpProcessClient.StartAsync(mcpDllPath, env);

        var queryRes = await mcpClient.CallToolAsync("tracker_query", new
        {
            entity = "contacts"
        });

        Assert.False(queryRes.GetProperty("ok").GetBoolean());
        var error = queryRes.GetProperty("error");
        Assert.Equal("ValidationError", error.GetProperty("code").GetString());
        Assert.Contains("Contacts are accessible exclusively via CLI", error.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShippedHost_TrackerQuery_Applications_ReturnsActiveApplicationsWithEventsTimelineAndSupportsStatusFilter()
    {
        var mcpDllPath = McpHostLocator.GetMcpDllPath();
        var env = _fixture.CreateProcessEnvironment(_databaseName);

        await using var mcpClient = await McpProcessClient.StartAsync(mcpDllPath, env);

        // 1. Create company and posting
        var compRes = await mcpClient.CallToolAsync("company_record", new
        {
            name = "App Corp",
            status = "target"
        });
        Assert.True(compRes.GetProperty("ok").GetBoolean());
        var companyId = compRes.GetProperty("data").GetProperty("company").GetProperty("id").GetString()!;

        var postRes = await mcpClient.CallToolAsync("posting_record", new
        {
            companyId = companyId,
            title = "Principal Engineer",
            url = "https://appcorp.com/jobs/101"
        });
        Assert.True(postRes.GetProperty("ok").GetBoolean());
        var postingId = postRes.GetProperty("data").GetProperty("posting").GetProperty("id").GetString()!;

        // 2. Create application
        var appRes = await mcpClient.CallToolAsync("application_record", new
        {
            postingId = postingId,
            action = "create",
            submittedDate = "2026-10-18",
            desiredSalaryMin = 180000,
            desiredSalaryMax = 210000,
            resumeVariant = "tailored"
        });
        Assert.True(appRes.GetProperty("ok").GetBoolean());

        // 3. Append event
        var eventRes = await mcpClient.CallToolAsync("application_record", new
        {
            postingId = postingId,
            action = "append_event",
            @event = new
            {
                eventType = "interview",
                eventDate = "2026-10-19",
                notes = "Screening interview scheduled"
            }
        });
        Assert.True(eventRes.GetProperty("ok").GetBoolean());

        // 4. Query entity == "applications"
        var queryAllRes = await mcpClient.CallToolAsync("tracker_query", new
        {
            entity = "applications"
        });
        Assert.True(queryAllRes.GetProperty("ok").GetBoolean(), queryAllRes.ToString());
        var data = queryAllRes.GetProperty("data");
        Assert.Equal("applications", data.GetProperty("entity").GetString());
        Assert.Equal(1, data.GetProperty("totalCount").GetInt32());
        var items = data.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());

        var appItem = items[0];
        Assert.Equal(postingId, appItem.GetProperty("postingId").GetString());
        Assert.Equal("applied", appItem.GetProperty("outcome").GetString());
        Assert.Equal("2026-10-18", appItem.GetProperty("submittedDate").GetString());
        Assert.Equal(180000, appItem.GetProperty("desiredSalaryMin").GetDecimal());
        Assert.Equal(210000, appItem.GetProperty("desiredSalaryMax").GetDecimal());

        var events = appItem.GetProperty("events");
        Assert.Equal(2, events.GetArrayLength());
        var eventTypes = events.EnumerateArray().Select(e => e.GetProperty("eventType").GetString()!).ToList();
        Assert.Contains("applied", eventTypes);
        Assert.Contains("interview", eventTypes);

        // 5. Query with status filter
        var queryApplied = await mcpClient.CallToolAsync("tracker_query", new
        {
            entity = "applications",
            status = "applied"
        });
        Assert.True(queryApplied.GetProperty("ok").GetBoolean());
        Assert.Equal(1, queryApplied.GetProperty("data").GetProperty("totalCount").GetInt32());

        var queryRejected = await mcpClient.CallToolAsync("tracker_query", new
        {
            entity = "applications",
            status = "rejected"
        });
        Assert.True(queryRejected.GetProperty("ok").GetBoolean());
        Assert.Equal(0, queryRejected.GetProperty("data").GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task ShippedHost_TrackerQuery_Companies_FiltersByStatusTagSubstringAndStaleFlagWithRetiredLifecycle()
    {
        var mcpDllPath = McpHostLocator.GetMcpDllPath();
        var env = _fixture.CreateProcessEnvironment(_databaseName);

        await using var mcpClient = await McpProcessClient.StartAsync(mcpDllPath, env);

        // 1. Company 1: Alpha Security Solutions (target, cloud, security) with active and retired career source
        var c1Res = await mcpClient.CallToolAsync("company_record", new
        {
            name = "Alpha Security Solutions",
            status = "target",
            ownershipType = "public",
            categoryTags = CloudSecurityTags,
            careerSources = new[]
            {
                new { atsType = "greenhouse", tenant = "alphacloud", isActive = true },
                new { atsType = "lever", tenant = "alpharetired", isActive = false }
            }
        });
        Assert.True(c1Res.GetProperty("ok").GetBoolean());
        var c1Id = Guid.Parse(c1Res.GetProperty("data").GetProperty("company").GetProperty("id").GetString()!);

        // Manually mark Company 1 last_checked_at to 10 days ago to test includeStale
        var companyCol = _database.GetCollection<BsonDocument>(TrackingMongoOptions.CompaniesCollectionName);
        var tenDaysAgo = DateTimeOffset.UtcNow.AddDays(-10);
        await companyCol.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(c1Id, GuidRepresentation.Standard)),
            Builders<BsonDocument>.Update.Set("last_checked_at", BsonDateTime.Create(tenDaysAgo.UtcDateTime)));

        // 2. Company 2: Beta Security Labs (watch, security)
        var c2Res = await mcpClient.CallToolAsync("company_record", new
        {
            name = "Beta Security Labs",
            status = "watch",
            ownershipType = "private",
            categoryTags = SecurityTags
        });
        Assert.True(c2Res.GetProperty("ok").GetBoolean());
        var c2Id = Guid.Parse(c2Res.GetProperty("data").GetProperty("company").GetProperty("id").GetString()!);
        var twoDaysAgo = DateTimeOffset.UtcNow.AddDays(-2);
        await companyCol.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(c2Id, GuidRepresentation.Standard)),
            Builders<BsonDocument>.Update.Set("last_checked_at", BsonDateTime.Create(twoDaysAgo.UtcDateTime)));

        // 3. Company 3: Gamma Advisory (avoid, consulting)
        var c3Res = await mcpClient.CallToolAsync("company_record", new
        {
            name = "Gamma Advisory",
            status = "avoid",
            ownershipType = "private",
            categoryTags = ConsultingTags
        });
        Assert.True(c3Res.GetProperty("ok").GetBoolean());

        // Test Filter: status == "target"
        var statusQuery = await mcpClient.CallToolAsync("tracker_query", new
        {
            entity = "companies",
            status = "target"
        });
        Assert.True(statusQuery.GetProperty("ok").GetBoolean());
        Assert.Equal(1, statusQuery.GetProperty("data").GetProperty("totalCount").GetInt32());
        Assert.Equal("Alpha Security Solutions", statusQuery.GetProperty("data").GetProperty("items")[0].GetProperty("name").GetString());

        // Test Filter: tag == "cloud"
        var tagQuery = await mcpClient.CallToolAsync("tracker_query", new
        {
            entity = "companies",
            tag = "cloud"
        });
        Assert.True(tagQuery.GetProperty("ok").GetBoolean());
        Assert.Equal(1, tagQuery.GetProperty("data").GetProperty("totalCount").GetInt32());
        Assert.Equal("Alpha Security Solutions", tagQuery.GetProperty("data").GetProperty("items")[0].GetProperty("name").GetString());

        // Test Filter: name substring == "security" (matches Alpha and Beta)
        var nameQuery = await mcpClient.CallToolAsync("tracker_query", new
        {
            entity = "companies",
            name = "security"
        });
        Assert.True(nameQuery.GetProperty("ok").GetBoolean());
        Assert.Equal(2, nameQuery.GetProperty("data").GetProperty("totalCount").GetInt32());

        // Test includeRetired == false (default) vs true
        var retiredFalseQuery = await mcpClient.CallToolAsync("tracker_query", new
        {
            entity = "companies",
            name = "Alpha",
            includeRetired = false
        });
        Assert.True(retiredFalseQuery.GetProperty("ok").GetBoolean());
        var c1ItemDefault = retiredFalseQuery.GetProperty("data").GetProperty("items")[0];
        Assert.Equal(1, c1ItemDefault.GetProperty("careerSources").GetArrayLength());

        var retiredTrueQuery = await mcpClient.CallToolAsync("tracker_query", new
        {
            entity = "companies",
            name = "Alpha",
            includeRetired = true
        });
        Assert.True(retiredTrueQuery.GetProperty("ok").GetBoolean());
        var c1ItemRetired = retiredTrueQuery.GetProperty("data").GetProperty("items")[0];
        Assert.Equal(2, c1ItemRetired.GetProperty("careerSources").GetArrayLength());

        // Test includeStale == true with staleAfterDays == 7 (Company 1 was 10 days ago, Company 2 was 2 days ago)
        var staleQuery = await mcpClient.CallToolAsync("tracker_query", new
        {
            entity = "companies",
            includeStale = true,
            staleAfterDays = 7
        });
        Assert.True(staleQuery.GetProperty("ok").GetBoolean());
        var staleItems = staleQuery.GetProperty("data").GetProperty("items");
        var staleNames = staleItems.EnumerateArray().Select(i => i.GetProperty("name").GetString()!).ToList();
        Assert.Contains("Alpha Security Solutions", staleNames);
        Assert.DoesNotContain("Beta Security Labs", staleNames);

        // Test combined filters: status == "target" and tag == "security" and name == "Alpha"
        var combinedQuery = await mcpClient.CallToolAsync("tracker_query", new
        {
            entity = "companies",
            status = "target",
            tag = "security",
            name = "Alpha"
        });
        Assert.True(combinedQuery.GetProperty("ok").GetBoolean());
        Assert.Equal(1, combinedQuery.GetProperty("data").GetProperty("totalCount").GetInt32());
        Assert.Equal("Alpha Security Solutions", combinedQuery.GetProperty("data").GetProperty("items")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task ShippedHost_TrackerQuery_Criteria_PointInTimeEvaluationAndValidation()
    {
        var mcpDllPath = McpHostLocator.GetMcpDllPath();
        var env = _fixture.CreateProcessEnvironment(_databaseName);

        await using var mcpClient = await McpProcessClient.StartAsync(mcpDllPath, env);

        // 1. Revision 1: effective from 2026-01-01
        var rev1 = await mcpClient.CallToolAsync("tracker_log", new
        {
            action = "criterion_revision",
            key = "pit_tech_stack",
            category = "hard_skip",
            value = "Must have C# and cloud architecture experience",
            provenance = "stated",
            effectiveFrom = "2026-01-01T00:00:00Z"
        });
        Assert.True(rev1.GetProperty("ok").GetBoolean(), rev1.ToString());
        var v1Token = rev1.GetProperty("data").GetProperty("versionToken").GetString()!;

        // 2. Revision 2: effective from 2026-06-01 (supersedes Revision 1)
        var rev2 = await mcpClient.CallToolAsync("tracker_log", new
        {
            action = "criterion_revision",
            key = "pit_tech_stack",
            category = "hard_skip",
            value = "Must have C#, .NET 10, and cloud architecture experience",
            provenance = "stated",
            effectiveFrom = "2026-06-01T00:00:00Z",
            versionToken = v1Token
        });
        Assert.True(rev2.GetProperty("ok").GetBoolean(), rev2.ToString());

        // 3. Query without effectiveDate (active version)
        var activeQuery = await mcpClient.CallToolAsync("tracker_query", new
        {
            entity = "criteria"
        });
        Assert.True(activeQuery.GetProperty("ok").GetBoolean(), activeQuery.ToString());
        var activeItems = activeQuery.GetProperty("data").GetProperty("items");
        var activeTech = activeItems.EnumerateArray().FirstOrDefault(c => c.GetProperty("key").GetString() == "pit_tech_stack");
        Assert.True(activeTech.ValueKind != JsonValueKind.Undefined);
        Assert.Equal("Must have C#, .NET 10, and cloud architecture experience", activeTech.GetProperty("value").GetString());

        // 4. Query with effectiveDate = "2026-03-01" (historical point-in-time)
        var historicalQuery = await mcpClient.CallToolAsync("tracker_query", new
        {
            entity = "criteria",
            effectiveDate = "2026-03-01"
        });
        Assert.True(historicalQuery.GetProperty("ok").GetBoolean());
        var histItems = historicalQuery.GetProperty("data").GetProperty("items");
        var histTech = histItems.EnumerateArray().FirstOrDefault(c => c.GetProperty("key").GetString() == "pit_tech_stack");
        Assert.True(histTech.ValueKind != JsonValueKind.Undefined);
        Assert.Equal("Must have C# and cloud architecture experience", histTech.GetProperty("value").GetString());

        // 5. Query with effectiveDate = "2026-07-01" (after revision 2)
        var futureQuery = await mcpClient.CallToolAsync("tracker_query", new
        {
            entity = "criteria",
            effectiveDate = "2026-07-01"
        });
        Assert.True(futureQuery.GetProperty("ok").GetBoolean());
        var futureItems = futureQuery.GetProperty("data").GetProperty("items");
        var futureTech = futureItems.EnumerateArray().FirstOrDefault(c => c.GetProperty("key").GetString() == "pit_tech_stack");
        Assert.True(futureTech.ValueKind != JsonValueKind.Undefined);
        Assert.Equal("Must have C#, .NET 10, and cloud architecture experience", futureTech.GetProperty("value").GetString());

        // 6. Query with invalid effectiveDate
        var badDateQuery = await mcpClient.CallToolAsync("tracker_query", new
        {
            entity = "criteria",
            effectiveDate = "not-a-valid-date"
        });
        Assert.False(badDateQuery.GetProperty("ok").GetBoolean());
        Assert.Equal("ValidationError", badDateQuery.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task ShippedHost_TrackerQuery_UniformBoundedPaging_ClampsPageAndPageSizeConsistently()
    {
        var mcpDllPath = McpHostLocator.GetMcpDllPath();
        var env = _fixture.CreateProcessEnvironment(_databaseName);

        await using var mcpClient = await McpProcessClient.StartAsync(mcpDllPath, env);

        // Page < 1 clamps to 1
        var p0Query = await mcpClient.CallToolAsync("tracker_query", new
        {
            entity = "companies",
            page = -2
        });
        Assert.True(p0Query.GetProperty("ok").GetBoolean());
        Assert.Equal(1, p0Query.GetProperty("data").GetProperty("page").GetInt32());

        // PageSize <= 0 defaults to 50
        var ps0Query = await mcpClient.CallToolAsync("tracker_query", new
        {
            entity = "companies",
            pageSize = -10
        });
        Assert.True(ps0Query.GetProperty("ok").GetBoolean());
        Assert.Equal(50, ps0Query.GetProperty("data").GetProperty("pageSize").GetInt32());

        // PageSize > 200 clamps to 200
        var psMaxQuery = await mcpClient.CallToolAsync("tracker_query", new
        {
            entity = "companies",
            pageSize = 999
        });
        Assert.True(psMaxQuery.GetProperty("ok").GetBoolean());
        Assert.Equal(200, psMaxQuery.GetProperty("data").GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task ShippedHost_TrackerQuery_SeededPostings_250PlusItems_EnforcesPageBoundariesTotalCountAndStableOrdering()
    {
        var mcpDllPath = McpHostLocator.GetMcpDllPath();
        var env = _fixture.CreateProcessEnvironment(_databaseName);

        var postingsCol = _database.GetCollection<BsonDocument>(TrackingMongoOptions.PostingsCollectionName);
        var companiesCol = _database.GetCollection<BsonDocument>(TrackingMongoOptions.CompaniesCollectionName);

        // 1. Seed company
        var companyId = Guid.NewGuid();
        var companyDoc = new BsonDocument
        {
            ["_id"] = new BsonBinaryData(companyId, GuidRepresentation.Standard),
            ["owner_id"] = RoleRadar.Domain.Common.OwnerId.DefaultValue,
            ["name"] = "Scale Corp",
            ["normalized_name"] = "scale corp",
            ["status"] = "target",
            ["ownership_type"] = "public",
            ["category_tags"] = new BsonArray(),
            ["version_token"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
            ["created_at"] = BsonDateTime.Create(DateTime.UtcNow),
            ["updated_at"] = BsonDateTime.Create(DateTime.UtcNow)
        };
        await companiesCol.InsertOneAsync(companyDoc);

        // 2. Seed 260 postings with unique titles and descending timestamps
        const int totalPostings = 260;
        var baseTime = DateTime.UtcNow;
        var postingDocs = new List<BsonDocument>(totalPostings);
        var seededIds = new List<Guid>(totalPostings);

        for (var i = 0; i < totalPostings; i++)
        {
            var pId = Guid.CreateVersion7();
            seededIds.Add(pId);
            var timestamp = baseTime.AddMinutes(-i);

            var doc = new BsonDocument
            {
                ["_id"] = new BsonBinaryData(pId, GuidRepresentation.Standard),
                ["owner_id"] = RoleRadar.Domain.Common.OwnerId.DefaultValue,
                ["company_id"] = new BsonBinaryData(companyId, GuidRepresentation.Standard),
                ["title"] = $"Staff Engineer #{i:D4}",
                ["normalized_title"] = $"staff engineer #{i:D4}",
                ["url"] = $"https://scalecorp.com/jobs/{i}",
                ["normalized_url"] = $"https://scalecorp.com/jobs/{i}",
                ["disposition"] = "lead",
                ["resurface_policy"] = "never",
                ["version_token"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
                ["created_at"] = BsonDateTime.Create(timestamp),
                ["updated_at"] = BsonDateTime.Create(timestamp),
                ["first_seen_at"] = BsonDateTime.Create(timestamp),
                ["last_verified_at"] = BsonDateTime.Create(timestamp)
            };
            postingDocs.Add(doc);
        }

        await postingsCol.InsertManyAsync(postingDocs);

        // 3. Query pages 1 through 6 using shipped MCP host
        await using var mcpClient = await McpProcessClient.StartAsync(mcpDllPath, env);

        var retrievedIds = new List<Guid>();
        const int pageSize = 50;
        const int expectedPages = 6;

        for (var page = 1; page <= expectedPages; page++)
        {
            var pageRes = await mcpClient.CallToolAsync("tracker_query", new
            {
                entity = "postings",
                companyId = companyId.ToString(),
                page = page,
                pageSize = pageSize
            });

            Assert.True(pageRes.GetProperty("ok").GetBoolean(), pageRes.ToString());
            var pageData = pageRes.GetProperty("data");
            Assert.Equal("postings", pageData.GetProperty("entity").GetString());
            Assert.Equal(page, pageData.GetProperty("page").GetInt32());
            Assert.Equal(pageSize, pageData.GetProperty("pageSize").GetInt32());
            Assert.Equal(totalPostings, pageData.GetProperty("totalCount").GetInt32());

            var items = pageData.GetProperty("items").EnumerateArray().ToList();
            var expectedItemCount = page == 6 ? 10 : 50;
            Assert.Equal(expectedItemCount, items.Count);

            foreach (var item in items)
            {
                var id = Guid.Parse(item.GetProperty("id").GetString()!);
                retrievedIds.Add(id);
            }
        }

        // 4. Assert exact coverage, zero duplicates, and identical deterministic sequence
        Assert.Equal(totalPostings, retrievedIds.Count);
        Assert.Equal(totalPostings, retrievedIds.Distinct().Count());
        Assert.Equal(seededIds, retrievedIds);
    }
}
