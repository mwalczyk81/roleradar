using System.Diagnostics;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Domain.Tracking;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Tests.Integration.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Integration.Tracking;

[Collection("ShippedHostRaceCollection")]
public sealed class ShippedHostRaceIntegrationTests
{
    private readonly SharedMongoFixture _sharedMongo;

    public ShippedHostRaceIntegrationTests(SharedMongoFixture sharedMongo)
    {
        _sharedMongo = sharedMongo ?? throw new ArgumentNullException(nameof(sharedMongo));
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task ShippedMcpHost_ConcurrentCompanyRecord_RacesN8Processes_ExactlyOneCreatedAndSevenAlreadyExists()
    {
        var mcpDllPath = McpHostLocator.GetMcpDllPath();
        var uniqueDb = _sharedMongo.CreateDatabaseName("rrtest_race_company");
        var env = _sharedMongo.CreateProcessEnvironment(uniqueDb);

        const int processCount = 8;
        var clients = new McpProcessClient[processCount];

        try
        {
            for (var i = 0; i < processCount; i++)
            {
                clients[i] = await McpProcessClient.StartAsync(mcpDllPath, env);
            }

            // Release all 8 processes simultaneously to record company with same name (varied casing)
            var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var tasks = clients.Select((client, idx) => Task.Run(async () =>
            {
                await barrier.Task;
                var variedName = (idx % 2 == 0) ? "Concurrent Race Tech LLC" : "  concurrent race tech llc  ";
                return await client.CallToolAsync("company_record", new
                {
                    name = variedName,
                    ownershipType = "private",
                    status = "target"
                });
            })).ToArray();

            barrier.SetResult();
            var results = await Task.WhenAll(tasks);

            var mongoClient = new MongoClient(_sharedMongo.ConnectionString);
            var db = mongoClient.GetDatabase(uniqueDb);

            var companiesCol = db.GetCollection<BsonDocument>(TrackingMongoOptions.CompaniesCollectionName);
            var auditCol = db.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.AuditLogCollectionName);

            // Assert exactly one company in database
            var storedCompanies = await companiesCol.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
            Assert.Single(storedCompanies);
            Assert.Equal("concurrent race tech llc", storedCompanies[0]["normalized_name"].AsString);

            // Assert exactly one audit entry for company creation
            var createAudits = await auditCol.Find(Builders<BsonDocument>.Filter.Eq("operation", "company_create")).ToListAsync();
            Assert.Single(createAudits);

            // Assert responses: exactly 1 "created", N-1 "already_exists"
            var createdResponses = results.Where(r =>
                r.TryGetProperty("ok", out var ok) && ok.GetBoolean() &&
                r.GetProperty("data").TryGetProperty("status", out var st) && st.GetString() == "created").ToList();

            var alreadyExistsResponses = results.Where(r =>
                r.TryGetProperty("ok", out var ok) && ok.GetBoolean() &&
                r.GetProperty("data").TryGetProperty("status", out var st) && st.GetString() == "already_exists").ToList();

            Assert.Single(createdResponses);
            Assert.Equal(processCount - 1, alreadyExistsResponses.Count);
        }
        finally
        {
            foreach (var client in clients)
            {
                if (client is not null)
                {
                    await client.DisposeAsync();
                }
            }
        }
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task ShippedMcpHost_ConcurrentPostingRecord_RacesN8Processes_TierA_TierB_TierC()
    {
        var mcpDllPath = McpHostLocator.GetMcpDllPath();
        var uniqueDb = _sharedMongo.CreateDatabaseName("roleradar_race_postings");
        var env = _sharedMongo.CreateProcessEnvironment(uniqueDb);

        const int processCount = 8;
        var clients = new McpProcessClient[processCount];

        try
        {
            for (var i = 0; i < processCount; i++)
            {
                clients[i] = await McpProcessClient.StartAsync(mcpDllPath, env);
            }

            // Create target company first
            var setupClient = clients[0];
            var companyRes = await setupClient.CallToolAsync("company_record", new
            {
                name = "Race Postings Corp",
                ownershipType = "public",
                status = "target"
            });
            Assert.True(companyRes.GetProperty("ok").GetBoolean());
            var companyIdStr = companyRes.GetProperty("data").GetProperty("company").GetProperty("id").GetString();
            Assert.NotNull(companyIdStr);

            var mongoClient = new MongoClient(_sharedMongo.ConnectionString);
            var db = mongoClient.GetDatabase(uniqueDb);
            var postingsCol = db.GetCollection<BsonDocument>(TrackingMongoOptions.PostingsCollectionName);
            var auditCol = db.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.AuditLogCollectionName);

            // --- Tier A Race (atsTenant + requisitionId) ---
            {
                var barrierA = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var tasksA = clients.Select(c => Task.Run(async () =>
                {
                    await barrierA.Task;
                    return await c.CallToolAsync("posting_record", new
                    {
                        companyId = companyIdStr,
                        title = "Tier A Staff Engineer",
                        atsTenant = "racecorp",
                        requisitionId = "REQ-TIER-A-RACE-01"
                    });
                })).ToArray();

                barrierA.SetResult();
                var resultsA = await Task.WhenAll(tasksA);

                var tierAPostings = await postingsCol.Find(Builders<BsonDocument>.Filter.Eq("dedup_key_a", "racecorp|REQ-TIER-A-RACE-01")).ToListAsync();
                Assert.Single(tierAPostings);

                var createdA = resultsA.Where(r =>
                    r.TryGetProperty("ok", out var ok) && ok.GetBoolean() &&
                    r.GetProperty("data").TryGetProperty("status", out var st) && st.GetString() == "created").ToList();

                var alreadyTrackedA = resultsA.Where(r =>
                    r.TryGetProperty("ok", out var ok) && ok.GetBoolean() &&
                    r.GetProperty("data").TryGetProperty("status", out var st) && st.GetString() == "already_tracked").ToList();

                Assert.Single(createdA);
                Assert.Equal(processCount - 1, alreadyTrackedA.Count);

                var auditsA = await auditCol.Find(Builders<BsonDocument>.Filter.And(
                    Builders<BsonDocument>.Filter.Eq("operation", "posting_create"),
                    Builders<BsonDocument>.Filter.Eq("entity_id", tierAPostings[0]["_id"]))).ToListAsync();
                Assert.Single(auditsA);
            }

            // --- Tier B Race (normalized_url) ---
            {
                var barrierB = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var tasksB = clients.Select(c => Task.Run(async () =>
                {
                    await barrierB.Task;
                    return await c.CallToolAsync("posting_record", new
                    {
                        companyId = companyIdStr,
                        title = "Tier B Backend Lead",
                        url = "https://jobs.racecorp.com/careers/lead-backend?ref=linkedin"
                    });
                })).ToArray();

                barrierB.SetResult();
                var resultsB = await Task.WhenAll(tasksB);

                var tierBPostings = await postingsCol.Find(Builders<BsonDocument>.Filter.Eq("normalized_url", "https://jobs.racecorp.com/careers/lead-backend")).ToListAsync();
                Assert.Single(tierBPostings);

                var createdB = resultsB.Where(r =>
                    r.TryGetProperty("ok", out var ok) && ok.GetBoolean() &&
                    r.GetProperty("data").TryGetProperty("status", out var st) && st.GetString() == "created").ToList();

                var alreadyTrackedB = resultsB.Where(r =>
                    r.TryGetProperty("ok", out var ok) && ok.GetBoolean() &&
                    r.GetProperty("data").TryGetProperty("status", out var st) && st.GetString() == "already_tracked").ToList();

                Assert.Single(createdB);
                Assert.Equal(processCount - 1, alreadyTrackedB.Count);

                var auditsB = await auditCol.Find(Builders<BsonDocument>.Filter.And(
                    Builders<BsonDocument>.Filter.Eq("operation", "posting_create"),
                    Builders<BsonDocument>.Filter.Eq("entity_id", tierBPostings[0]["_id"]))).ToListAsync();
                Assert.Single(auditsB);
            }

            // --- Tier C Race (companyId + requisitionId, no tenant) ---
            {
                var barrierC = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var tasksC = clients.Select(c => Task.Run(async () =>
                {
                    await barrierC.Task;
                    return await c.CallToolAsync("posting_record", new
                    {
                        companyId = companyIdStr,
                        title = "Tier C Security Architect",
                        requisitionId = "REQ-TIER-C-99"
                    });
                })).ToArray();

                barrierC.SetResult();
                var resultsC = await Task.WhenAll(tasksC);

                var expectedDedupKeyC = $"{Guid.Parse(companyIdStr):D}|REQ-TIER-C-99";
                var tierCPostings = await postingsCol.Find(Builders<BsonDocument>.Filter.Eq("dedup_key_c", expectedDedupKeyC)).ToListAsync();
                Assert.Single(tierCPostings);

                var createdC = resultsC.Where(r =>
                    r.TryGetProperty("ok", out var ok) && ok.GetBoolean() &&
                    r.GetProperty("data").TryGetProperty("status", out var st) && st.GetString() == "created").ToList();

                var alreadyTrackedC = resultsC.Where(r =>
                    r.TryGetProperty("ok", out var ok) && ok.GetBoolean() &&
                    r.GetProperty("data").TryGetProperty("status", out var st) && st.GetString() == "already_tracked").ToList();

                var statuses = resultsC.Select(r => r.TryGetProperty("data", out var d) && d.TryGetProperty("status", out var s) ? s.GetString() : r.ToString()).ToArray();
                Assert.True(alreadyTrackedC.Count == processCount - 1, $"Statuses: {string.Join(", ", statuses)}");

                var auditsC = await auditCol.Find(Builders<BsonDocument>.Filter.And(
                    Builders<BsonDocument>.Filter.Eq("operation", "posting_create"),
                    Builders<BsonDocument>.Filter.Eq("entity_id", tierCPostings[0]["_id"]))).ToListAsync();
                Assert.Single(auditsC);
            }
        }
        finally
        {
            foreach (var client in clients)
            {
                if (client is not null)
                {
                    await client.DisposeAsync();
                }
            }
        }
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task ShippedMcpHost_ConcurrentCriterionRevisionRace_ExactlyOneSucceedsAndOthersGetStaleVersionToken()
    {
        var mcpDllPath = McpHostLocator.GetMcpDllPath();
        var uniqueDb = _sharedMongo.CreateDatabaseName("rrtest_criterion_race");
        var mongoClient = new MongoClient(_sharedMongo.ConnectionString);
        var db = mongoClient.GetDatabase(uniqueDb);

        const int processCount = 8;
        var clients = new List<McpProcessClient>();

        try
        {
            var env = _sharedMongo.CreateProcessEnvironment(uniqueDb);
            for (var i = 0; i < processCount; i++)
            {
                clients.Add(await McpProcessClient.StartAsync(mcpDllPath, env));
            }

            // 1. Initial criterion creation
            var initialResp = await clients[0].CallToolAsync("tracker_log", new
            {
                action = "criterion_revision",
                key = "tech_stack_csharp",
                category = "hard_skip",
                value = "Must use .NET 10",
                provenance = "stated",
                effectiveFrom = "2026-01-01T00:00:00Z"
            });

            Assert.True(initialResp.GetProperty("ok").GetBoolean());
            Assert.True(initialResp.GetProperty("data").TryGetProperty("versionToken", out var initialVtProp));
            Assert.NotEqual(Guid.Empty, initialVtProp.GetGuid());

            // Get active criterion to retrieve version token
            var criteriaCol = db.GetCollection<BsonDocument>(TrackingMongoOptions.CriteriaCollectionName);
            var activeDoc = await criteriaCol.Find(Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("key", "tech_stack_csharp"),
                Builders<BsonDocument>.Filter.Eq("is_active", true))).FirstOrDefaultAsync();

            Assert.NotNull(activeDoc);
            var initialToken = activeDoc["version_token"].AsGuid;
            Assert.Equal(initialToken, initialVtProp.GetGuid());

            // 2. Launch 8 concurrent MCP processes revising the same key with the same expected versionToken
            var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var tasks = clients.Select((c, idx) => Task.Run(async () =>
            {
                await barrier.Task;
                return await c.CallToolAsync("tracker_log", new
                {
                    action = "criterion_revision",
                    key = "tech_stack_csharp",
                    category = "hard_skip",
                    value = $"Revised by worker {idx}",
                    provenance = "stated",
                    effectiveFrom = "2026-06-01T00:00:00Z",
                    versionToken = initialToken
                });
            })).ToArray();

            barrier.SetResult();
            var results = await Task.WhenAll(tasks);

            var succeeded = results.Where(r =>
                r.TryGetProperty("ok", out var ok) && ok.GetBoolean()).ToList();

            var staleErrors = results.Where(r =>
                r.TryGetProperty("ok", out var ok) && !ok.GetBoolean() &&
                r.GetProperty("error").GetProperty("code").GetString() == "StaleVersionToken").ToList();

            Assert.Single(succeeded);
            Assert.True(succeeded[0].GetProperty("data").TryGetProperty("versionToken", out var revVtProp));
            Assert.NotEqual(Guid.Empty, revVtProp.GetGuid());
            Assert.Equal(processCount - 1, staleErrors.Count);

            // Assert exactly one active version in MongoDB afterwards
            var activeVersions = await criteriaCol.Find(Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("key", "tech_stack_csharp"),
                Builders<BsonDocument>.Filter.Eq("is_active", true))).ToListAsync();

            Assert.Single(activeVersions);
        }
        finally
        {
            foreach (var client in clients)
            {
                await client.DisposeAsync();
            }

            await _sharedMongo.DropDatabaseAsync(uniqueDb);
        }
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task ShippedMcpHost_ConcurrentApplicationCreateRace_ExactlyOneSucceedsAndOthersFailWithInvalidTransition()
    {
        var mcpDllPath = McpHostLocator.GetMcpDllPath();
        var uniqueDb = _sharedMongo.CreateDatabaseName("roleradar_app_race");
        var mongoClient = new MongoClient(_sharedMongo.ConnectionString);
        var db = mongoClient.GetDatabase(uniqueDb);

        const int processCount = 8;
        var clients = new List<McpProcessClient>();

        try
        {
            var env = _sharedMongo.CreateProcessEnvironment(uniqueDb);
            for (var i = 0; i < processCount; i++)
            {
                clients.Add(await McpProcessClient.StartAsync(mcpDllPath, env));
            }

            // 1. Create company and posting
            var companyResp = await clients[0].CallToolAsync("company_record", new
            {
                name = "App Race Corp",
                ownershipType = "public",
                status = "target"
            });
            Assert.True(companyResp.GetProperty("ok").GetBoolean(), $"company_record failed: {companyResp}");
            var companyIdStr = companyResp.GetProperty("data").GetProperty("company").GetProperty("id").GetString();
            Assert.NotNull(companyIdStr);

            var postingResp = await clients[0].CallToolAsync("posting_record", new
            {
                companyId = companyIdStr,
                title = "Concurrent Application Role",
                url = "https://jobs.apprace.internal/positions/123"
            });
            Assert.True(postingResp.GetProperty("ok").GetBoolean(), $"posting_record failed: {postingResp}");
            var postingIdStr = postingResp.GetProperty("data").GetProperty("posting").GetProperty("id").GetString();
            Assert.NotNull(postingIdStr);
            var postingId = Guid.Parse(postingIdStr);

            // 2. Launch 8 concurrent MCP processes submitting application for the same posting
            var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var tasks = clients.Select((c, idx) => Task.Run(async () =>
            {
                await barrier.Task;
                return await c.CallToolAsync("application_record", new
                {
                    postingId = postingId,
                    action = "create",
                    submittedDate = "2026-10-18",
                    resumeVariant = "tailored",
                    desiredSalaryMin = 180000m,
                    desiredSalaryMax = 210000m
                });
            })).ToArray();

            barrier.SetResult();
            var results = await Task.WhenAll(tasks);

            var succeeded = results.Where(r =>
                r.TryGetProperty("ok", out var ok) && ok.GetBoolean()).ToList();

            var invalidTransitionErrors = results.Where(r =>
                r.TryGetProperty("ok", out var ok) && !ok.GetBoolean() &&
                r.GetProperty("error").GetProperty("code").GetString() == "InvalidTransition").ToList();

            if (succeeded.Count != 1)
            {
                var summary = string.Join("\n", results.Select((r, idx) => $"[{idx}] {r}"));
                Assert.Fail($"Expected 1 succeeded, got {succeeded.Count}. Process results:\n{summary}");
            }
            Assert.Single(succeeded);
            Assert.Equal(processCount - 1, invalidTransitionErrors.Count);

            // Assert exactly one document in applications
            var appCol = db.GetCollection<BsonDocument>(TrackingMongoOptions.ApplicationsCollectionName);
            var apps = await appCol.Find(Builders<BsonDocument>.Filter.Eq("posting_id", new BsonBinaryData(postingId, GuidRepresentation.Standard))).ToListAsync();
            Assert.Single(apps);

            // Assert posting disposition is applied
            var postingCol = db.GetCollection<BsonDocument>(TrackingMongoOptions.PostingsCollectionName);
            var postDoc = await postingCol.Find(Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(postingId, GuidRepresentation.Standard))).FirstOrDefaultAsync();
            Assert.NotNull(postDoc);
            Assert.Equal("applied", postDoc["disposition"].AsString);
        }
        finally
        {
            foreach (var client in clients)
            {
                await client.DisposeAsync();
            }

            await _sharedMongo.DropDatabaseAsync(uniqueDb);
        }
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task ShippedMcpHost_ConcurrentSearchRunRace_ExactlyOneCreatedSevenAlreadyLoggedAndOneAuditEntry()
    {
        var mcpDllPath = McpHostLocator.GetMcpDllPath();
        var uniqueDb = _sharedMongo.CreateDatabaseName("rrtest_searchrun_race");
        var mongoClient = new MongoClient(_sharedMongo.ConnectionString);
        var db = mongoClient.GetDatabase(uniqueDb);

        const int processCount = 8;
        var clients = new List<McpProcessClient>();

        try
        {
            var env = _sharedMongo.CreateProcessEnvironment(uniqueDb);
            for (var i = 0; i < processCount; i++)
            {
                clients.Add(await McpProcessClient.StartAsync(mcpDllPath, env));
            }

            var startedAt = "2026-10-18T16:00:00Z";
            var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var tasks = clients.Select((c, idx) => Task.Run(async () =>
            {
                await barrier.Task;
                return await c.CallToolAsync("tracker_log", new
                {
                    action = "search_run",
                    startedAt = startedAt,
                    newPostingsCount = 10,
                    notes = $"Race worker {idx}"
                });
            })).ToArray();

            barrier.SetResult();
            var results = await Task.WhenAll(tasks);

            var created = results.Where(r =>
                r.TryGetProperty("ok", out var ok) && ok.GetBoolean() &&
                r.GetProperty("data").GetProperty("status").GetString() == "success").ToList();

            var alreadyLogged = results.Where(r =>
                r.TryGetProperty("ok", out var ok) && ok.GetBoolean() &&
                r.GetProperty("data").GetProperty("status").GetString() == "already_logged").ToList();

            Assert.Single(created);
            Assert.Equal(processCount - 1, alreadyLogged.Count);

            // Assert exactly one record in search_runs collection
            var searchRunsCol = db.GetCollection<BsonDocument>(TrackingMongoOptions.SearchRunsCollectionName);
            var count = await searchRunsCol.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);
            Assert.Equal(1, count);

            // Assert exactly one audit log entry for this search run
            var runIdStr = created[0].GetProperty("data").GetProperty("recordId").GetString();
            Assert.NotNull(runIdStr);
            var runId = Guid.Parse(runIdStr);

            var auditCol = db.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.AuditLogCollectionName);
            var auditCount = await auditCol.CountDocumentsAsync(Builders<BsonDocument>.Filter.Eq("entity_id", new BsonBinaryData(runId, GuidRepresentation.Standard)));
            Assert.Equal(1, auditCount);
        }
        finally
        {
            foreach (var client in clients)
            {
                await client.DisposeAsync();
            }

            await _sharedMongo.DropDatabaseAsync(uniqueDb);
        }
    }
}
