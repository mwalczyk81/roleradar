using System.Diagnostics;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Tests.Integration.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Integration.Tracking;

[Collection(SharedMongoCollectionDefinition.Name)]
public sealed class ShippedHostTrackingIntegrationTests
{
    private readonly SharedMongoFixture _sharedMongo;

    public ShippedHostTrackingIntegrationTests(SharedMongoFixture sharedMongo)
    {
        ArgumentNullException.ThrowIfNull(sharedMongo);
        _sharedMongo = sharedMongo;
    }

    [Fact]
    public async Task ShippedMcpHost_FirstTrackingWrite_InitializesUniquePartialIndexesOnDatabase()
    {
        var mcpDllPath = GetMcpDllPath();
        var uniqueDb = _sharedMongo.CreateDatabaseName("roleradar_smoke_track");
        var env = _sharedMongo.CreateProcessEnvironment(uniqueDb);

        await using var client = await McpProcessClient.StartAsync(mcpDllPath, env);

        // Perform first tracking write (e.g. company_record)
        var response = await client.CallToolAsync("company_record", new
        {
            name = "Smoke Initializer Corp",
            ownershipType = "public",
            status = "target"
        });

        Assert.True(response.TryGetProperty("ok", out var okProp) && okProp.GetBoolean(),
            $"Expected successful MCP response, got: {response.GetRawText()}\nHost Stderr:\n{client.GetStderr()}");

        // Inspect indexes on postings, companies, career_sources, criteria, and search_runs
        var mongoClient = new MongoClient(_sharedMongo.ConnectionString);
        var db = mongoClient.GetDatabase(uniqueDb);

        // 1. Postings
        var postingIndexes = await (await db.GetCollection<BsonDocument>(TrackingMongoOptions.PostingsCollectionName).Indexes.ListAsync()).ToListAsync();
        var postingIndexNames = postingIndexes.Select(i => i["name"].AsString).ToArray();
        Assert.Contains("ux_postings_owner_dedup_key_a", postingIndexNames);
        Assert.Contains("ux_postings_owner_dedup_key_b", postingIndexNames);
        Assert.Contains("ux_postings_owner_dedup_key_c", postingIndexNames);
        var keyA = postingIndexes.First(i => i["name"].AsString == "ux_postings_owner_dedup_key_a");
        Assert.True(keyA["unique"].AsBoolean);
        Assert.True(keyA.Contains("partialFilterExpression"));

        // 2. Companies
        var companyIndexes = await (await db.GetCollection<BsonDocument>(TrackingMongoOptions.CompaniesCollectionName).Indexes.ListAsync()).ToListAsync();
        var companyIndexNames = companyIndexes.Select(i => i["name"].AsString).ToArray();
        Assert.Contains("ux_companies_owner_normalized_name", companyIndexNames);
        Assert.True(companyIndexes.First(i => i["name"].AsString == "ux_companies_owner_normalized_name")["unique"].AsBoolean);

        // 3. Career Sources
        var sourceIndexes = await (await db.GetCollection<BsonDocument>(TrackingMongoOptions.CareerSourcesCollectionName).Indexes.ListAsync()).ToListAsync();
        var sourceIndexNames = sourceIndexes.Select(i => i["name"].AsString).ToArray();
        Assert.Contains("ux_career_sources_composite", sourceIndexNames);
        Assert.True(sourceIndexes.First(i => i["name"].AsString == "ux_career_sources_composite")["unique"].AsBoolean);

        // 4. Criteria
        var criteriaIndexes = await (await db.GetCollection<BsonDocument>(TrackingMongoOptions.CriteriaCollectionName).Indexes.ListAsync()).ToListAsync();
        var criteriaIndexNames = criteriaIndexes.Select(i => i["name"].AsString).ToArray();
        Assert.Contains("ux_criteria_owner_key_active", criteriaIndexNames);
        var criteriaIdx = criteriaIndexes.First(i => i["name"].AsString == "ux_criteria_owner_key_active");
        Assert.True(criteriaIdx["unique"].AsBoolean);
        Assert.True(criteriaIdx.Contains("partialFilterExpression"));

        // 5. Search Runs
        var searchRunIndexes = await (await db.GetCollection<BsonDocument>(TrackingMongoOptions.SearchRunsCollectionName).Indexes.ListAsync()).ToListAsync();
        var searchRunIndexNames = searchRunIndexes.Select(i => i["name"].AsString).ToArray();
        Assert.Contains("ux_search_runs_owner_started", searchRunIndexNames);
        Assert.True(searchRunIndexes.First(i => i["name"].AsString == "ux_search_runs_owner_started")["unique"].AsBoolean);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task ShippedMcpHost_ConcurrentCompanyRecord_RacesN8Processes_ExactlyOneCreatedAndSevenAlreadyExists()
    {
        var mcpDllPath = GetMcpDllPath();
        var uniqueDb = _sharedMongo.CreateDatabaseName("roleradar_race_company");
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
            var createAudits = await auditCol.Find(Builders<BsonDocument>.Filter.Eq("after_summary.status", "company_create")).ToListAsync();
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
        var mcpDllPath = GetMcpDllPath();
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
                    Builders<BsonDocument>.Filter.Eq("after_summary.status", "posting_create"),
                    Builders<BsonDocument>.Filter.Eq("target_note_id", tierAPostings[0]["_id"]))).ToListAsync();
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
                    Builders<BsonDocument>.Filter.Eq("after_summary.status", "posting_create"),
                    Builders<BsonDocument>.Filter.Eq("target_note_id", tierBPostings[0]["_id"]))).ToListAsync();
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
                    Builders<BsonDocument>.Filter.Eq("after_summary.status", "posting_create"),
                    Builders<BsonDocument>.Filter.Eq("target_note_id", tierCPostings[0]["_id"]))).ToListAsync();
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
    public async Task ShippedMcpHost_PreExistingDuplicateDataViolatesIndex_ReturnsValidationErrorAcrossTools()
    {
        var mcpDllPath = GetMcpDllPath();
        var uniqueDb = _sharedMongo.CreateDatabaseName("roleradar_dup_idx");
        var mongoClient = new MongoClient(_sharedMongo.ConnectionString);
        var db = mongoClient.GetDatabase(uniqueDb);

        try
        {
            // Seed duplicate dedup_key_a documents before first use
            var postingsCol = db.GetCollection<BsonDocument>(TrackingMongoOptions.PostingsCollectionName);
            await postingsCol.InsertManyAsync(
            [
                new BsonDocument { ["owner_id"] = "owner-1", ["dedup_key_a"] = "tenant|req1" },
                new BsonDocument { ["owner_id"] = "owner-1", ["dedup_key_a"] = "tenant|req1" }
            ]);

            var env = _sharedMongo.CreateProcessEnvironment(uniqueDb);
            await using var client = await McpProcessClient.StartAsync(mcpDllPath, env);

            // 1. company_record must fail with ValidationError naming the index
            var companyResp = await client.CallToolAsync("company_record", new
            {
                name = "Test Index Failure Corp"
            });
            Assert.False(companyResp.GetProperty("ok").GetBoolean());
            var compErr = companyResp.GetProperty("error");
            Assert.Equal("ValidationError", compErr.GetProperty("code").GetString());
            Assert.Contains("ux_postings_owner_dedup_key_a", compErr.GetProperty("message").GetString());

            // 2. posting_record must fail with ValidationError naming the index
            var postingResp = await client.CallToolAsync("posting_record", new
            {
                companyId = Guid.NewGuid(),
                title = "Backend Engineer"
            });
            Assert.False(postingResp.GetProperty("ok").GetBoolean());
            var postErr = postingResp.GetProperty("error");
            Assert.Equal("ValidationError", postErr.GetProperty("code").GetString());
            Assert.Contains("ux_postings_owner_dedup_key_a", postErr.GetProperty("message").GetString());

            // 3. tracker_query must fail with ValidationError naming the index
            var queryResp = await client.CallToolAsync("tracker_query", new
            {
                entity = "postings"
            });
            Assert.False(queryResp.GetProperty("ok").GetBoolean());
            var queryErr = queryResp.GetProperty("error");
            Assert.Equal("ValidationError", queryErr.GetProperty("code").GetString());
            Assert.Contains("ux_postings_owner_dedup_key_a", queryErr.GetProperty("message").GetString());
        }
        finally
        {
            await mongoClient.DropDatabaseAsync(uniqueDb);
        }
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task ShippedMcpHost_ConcurrentCriterionRevisionRace_ExactlyOneSucceedsAndOthersGetStaleVersionToken()
    {
        var mcpDllPath = GetMcpDllPath();
        var uniqueDb = _sharedMongo.CreateDatabaseName("roleradar_criterion_race");
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

            await mongoClient.DropDatabaseAsync(uniqueDb);
        }
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task ShippedMcpHost_ConcurrentApplicationCreateRace_ExactlyOneSucceedsAndOthersFailWithInvalidTransition()
    {
        var mcpDllPath = GetMcpDllPath();
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

            await mongoClient.DropDatabaseAsync(uniqueDb);
        }
    }

    [Fact]
    public async Task ShippedMcpHost_PostingRecord_Signals_ValidatesPersistsAndAffectsGhostLikelihood()
    {
        var mcpDllPath = GetMcpDllPath();
        var uniqueDb = _sharedMongo.CreateDatabaseName("roleradar_shipped_signals");
        var env = _sharedMongo.CreateProcessEnvironment(uniqueDb);
        var mongoClient = new MongoClient(_sharedMongo.ConnectionString);

        await using var client = await McpProcessClient.StartAsync(mcpDllPath, env);

        try
        {
            // 1. Create company
            var compRes = await client.CallToolAsync("company_record", new
            {
                name = "Signals Shipped Host Corp"
            });
            Assert.True(compRes.GetProperty("ok").GetBoolean());
            var companyId = compRes.GetProperty("data").GetProperty("company").GetProperty("id").GetString();

            // 2. Reject invalid signalType with ValidationError and writes nothing
            var invalidSignalRes = await client.CallToolAsync("posting_record", new
            {
                companyId = companyId,
                title = "Invalid Signal Title",
                sourceKind = "ats",
                signals = new[]
                {
                    new
                    {
                        signalType = "bogus_signal_type",
                        evidence = "Bogus evidence"
                    }
                }
            });
            Assert.False(invalidSignalRes.GetProperty("ok").GetBoolean());
            Assert.Equal("ValidationError", invalidSignalRes.GetProperty("error").GetProperty("code").GetString());

            var db = mongoClient.GetDatabase(uniqueDb);
            var signalsCol = db.GetCollection<BsonDocument>(TrackingMongoOptions.PostingSignalsCollectionName);
            var initialSignalCount = await signalsCol.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);
            Assert.Equal(0, initialSignalCount);

            // 3. Create posting with valid signals (gone + phantom + passed deadline => high or critical ghostLikelihood)
            var createRes = await client.CallToolAsync("posting_record", new
            {
                companyId = companyId,
                title = "Senior Platform Lead",
                sourceKind = "ats",
                atsTenant = "signalscorp",
                requisitionId = "req-ghost-1",
                signals = new object[]
                {
                    new
                    {
                        signalType = "gone",
                        evidence = "404 Not Found on career site"
                    },
                    new
                    {
                        signalType = "phantom",
                        evidence = "Headcount cancelled on quarterly earnings call"
                    },
                    new
                    {
                        signalType = "deadline",
                        evidence = "Application window expired",
                        deadlineDate = "2026-01-01"
                    }
                }
            });
            Assert.True(createRes.GetProperty("ok").GetBoolean());
            var postingId = createRes.GetProperty("data").GetProperty("posting").GetProperty("id").GetString();

            // Assert posting_signals collection has the documents
            var signalsInDb = await signalsCol.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
            Assert.Equal(3, signalsInDb.Count);

            // Assert ghostLikelihood in tracker_query reflects them (high or critical)
            var queryRes = await client.CallToolAsync("tracker_query", new
            {
                entity = "postings"
            });
            Assert.True(queryRes.GetProperty("ok").GetBoolean());
            var postingsList = queryRes.GetProperty("data").GetProperty("items").EnumerateArray().ToList();
            var matchedPosting = postingsList.FirstOrDefault(p => p.GetProperty("id").GetString() == postingId);
            Assert.True(matchedPosting.ValueKind != JsonValueKind.Undefined);
            var ghost = matchedPosting.GetProperty("ghostLikelihood").GetString();
            Assert.True(ghost == "high" || ghost == "critical", $"Expected ghostLikelihood to be high or critical, got: {ghost}");

            // 4. Dedup match appends signals and audits
            var dedupRes = await client.CallToolAsync("posting_record", new
            {
                companyId = companyId,
                title = "Senior Platform Lead",
                atsTenant = "signalscorp",
                requisitionId = "req-ghost-1",
                signals = new[]
                {
                    new
                    {
                        signalType = "repost",
                        evidence = "Re-opened after internal review"
                    }
                }
            });
            Assert.True(dedupRes.GetProperty("ok").GetBoolean());
            Assert.Equal("already_tracked", dedupRes.GetProperty("data").GetProperty("status").GetString());

            var signalsAfterDedup = await signalsCol.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
            Assert.Equal(4, signalsAfterDedup.Count);

            // 5. Identical resubmission same day appends nothing
            var resubmitRes = await client.CallToolAsync("posting_record", new
            {
                companyId = companyId,
                title = "Senior Platform Lead",
                atsTenant = "signalscorp",
                requisitionId = "req-ghost-1",
                signals = new[]
                {
                    new
                    {
                        signalType = "repost",
                        evidence = "Re-opened after internal review"
                    }
                }
            });
            Assert.True(resubmitRes.GetProperty("ok").GetBoolean());
            Assert.Equal("already_tracked", resubmitRes.GetProperty("data").GetProperty("status").GetString());

            var signalsAfterResubmit = await signalsCol.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
            Assert.Equal(4, signalsAfterResubmit.Count);
        }
        finally
        {
            await mongoClient.DropDatabaseAsync(uniqueDb);
        }
    }

    [Fact]
    public async Task ShippedMcpHost_ToolsList_InputSchemaParameterNamesEqualContractDoc()
    {
        var mcpDllPath = GetMcpDllPath();
        var uniqueDb = _sharedMongo.CreateDatabaseName("roleradar_schema_test");
        var env = _sharedMongo.CreateProcessEnvironment(uniqueDb);

        await using var client = await McpProcessClient.StartAsync(mcpDllPath, env);

        var listResult = await client.ListToolsAsync();
        Assert.True(listResult.TryGetProperty("tools", out var toolsArray));

        // Parse contracts/mcp-tools.md for 002 tools and 001 tools
        var repoRoot = FindRepoRoot();
        var contract002Path = Path.Combine(repoRoot, "specs", "002-job-search-tracking", "contracts", "mcp-tools.md");
        var contract001Path = Path.Combine(repoRoot, "specs", "001-memory-replacement", "contracts", "mcp-tools.md");

        Assert.True(File.Exists(contract002Path), $"Missing contract at {contract002Path}");
        Assert.True(File.Exists(contract001Path), $"Missing contract at {contract001Path}");

        var expectedParametersByTool = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        ParseToolContractParameters(contract001Path, expectedParametersByTool);
        ParseToolContractParameters(contract002Path, expectedParametersByTool);

        Assert.Equal(12, expectedParametersByTool.Count);

        var actualTools = toolsArray.EnumerateArray().ToList();
        Assert.Equal(12, actualTools.Count);

        var discrepancies = new List<string>();

        foreach (var toolElem in actualTools)
        {
            var toolName = toolElem.GetProperty("name").GetString()!;
            if (!expectedParametersByTool.TryGetValue(toolName, out var expectedParams))
            {
                discrepancies.Add($"Tool '{toolName}' returned by MCP server was not found in contract documents.");
                continue;
            }

            var actualParams = new HashSet<string>(StringComparer.Ordinal);
            if (toolElem.TryGetProperty("inputSchema", out var inputSchema) &&
                inputSchema.TryGetProperty("properties", out var properties))
            {
                foreach (var prop in properties.EnumerateObject())
                {
                    actualParams.Add(prop.Name);
                }
            }

            // Compare sets
            var missingInMcp = expectedParams.Except(actualParams).ToList();
            var extraInMcp = actualParams.Except(expectedParams).ToList();

            if (missingInMcp.Count > 0 || extraInMcp.Count > 0)
            {
                discrepancies.Add($"Tool '{toolName}' parameter mismatch. Missing in MCP schema: [{string.Join(", ", missingInMcp)}], Extra in MCP schema: [{string.Join(", ", extraInMcp)}]");
            }
        }

        Assert.True(discrepancies.Count == 0, $"Schema contract discrepancies found:\n{string.Join("\n", discrepancies)}");
    }

    [Fact]
    public async Task ShippedMcpHost_ToolInvocation_WithUnknownArguments_ReturnsValidationError()
    {
        var mcpDllPath = GetMcpDllPath();
        var uniqueDb = _sharedMongo.CreateDatabaseName("roleradar_unknown_args_test");
        var env = _sharedMongo.CreateProcessEnvironment(uniqueDb);

        await using var client = await McpProcessClient.StartAsync(mcpDllPath, env);

        var allTools = new[]
        {
            "note_create", "note_get", "note_append", "note_replace_section", "note_search", "note_archive",
            "company_record", "posting_record", "posting_transition", "tracker_query", "tracker_log", "application_record"
        };

        foreach (var tool in allTools)
        {
            using var rawResponse = await client.CallToolRawAsync(tool, new
            {
                bogusParam = "secretValue123"
            });

            var root = rawResponse.RootElement;
            Assert.True(root.TryGetProperty("result", out var result), $"Expected result property for tool {tool}");
            var text = result.GetProperty("content")[0].GetProperty("text").GetString()!;
            using var envelope = JsonDocument.Parse(text);
            Assert.False(envelope.RootElement.GetProperty("ok").GetBoolean(), $"Tool {tool} should return ok=false");
            var err = envelope.RootElement.GetProperty("error");
            Assert.Equal("ValidationError", err.GetProperty("code").GetString());
            var msg = err.GetProperty("message").GetString()!;
            Assert.Contains("bogusParam", msg, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("secretValue123", msg, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string FindRepoRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(Path.Combine(current, ".git")))
            {
                return current;
            }
            current = Directory.GetParent(current)?.FullName;
        }
        throw new InvalidOperationException("Could not find repository root containing .git");
    }

    private static void ParseToolContractParameters(string filePath, Dictionary<string, HashSet<string>> result)
    {
        var lines = File.ReadAllLines(filePath);
        string? currentTool = null;
        var inInputParameters = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            // Match ## Tool X: `tool_name`
            if (line.StartsWith("## Tool", StringComparison.OrdinalIgnoreCase))
            {
                inInputParameters = false;
                var backtickStart = line.IndexOf('`');
                if (backtickStart >= 0)
                {
                    var backtickEnd = line.IndexOf('`', backtickStart + 1);
                    if (backtickEnd > backtickStart)
                    {
                        currentTool = line.Substring(backtickStart + 1, backtickEnd - backtickStart - 1);
                        if (!result.ContainsKey(currentTool))
                        {
                            result[currentTool] = new HashSet<string>(StringComparer.Ordinal);
                        }
                    }
                }
                continue;
            }

            if (currentTool is null)
            {
                continue;
            }

            if (line.StartsWith("- **Input Parameters**:", StringComparison.OrdinalIgnoreCase))
            {
                inInputParameters = true;
                continue;
            }

            if (inInputParameters)
            {
                // If line starts another top-level section (e.g. - **Behavior** or - **Output) or ##, stop
                if (line.StartsWith("- **Behavior", StringComparison.Ordinal) ||
                    line.StartsWith("- **Output", StringComparison.Ordinal) ||
                    line.StartsWith("##", StringComparison.Ordinal) ||
                    line.StartsWith("---", StringComparison.Ordinal))
                {
                    inInputParameters = false;
                    continue;
                }

                // Match top-level parameter: "  - `paramName`" or parameters grouped under sections: "    - `paramName`"
                // But skip sub-parameters of object/array properties (e.g., careerSources, signals, event items)
                var trimmed = rawLine.TrimStart();
                if (trimmed.StartsWith("- `", StringComparison.Ordinal))
                {
                    // Check if it is a subfield of an object property (e.g., careerSources or signals or event)
                    // In mcp-tools.md:
                    //   careerSources:
                    //     - `id`
                    //     - `atsType`
                    //   signals:
                    //     - `signalType`
                    //   event:
                    //     - `eventType`
                    // Whereas tracker_log criterion/search parameters have:
                    //   - **Criterion Parameters** (...):
                    //     - `key`
                    //     - `category`
                    // Let's check indentation: if rawLine starts with 4 spaces ("    - `"), verify whether the preceding section was a bold parameter category or an object property.
                    var pStart = trimmed.IndexOf('`');
                    var pEnd = trimmed.IndexOf('`', pStart + 1);
                    if (pEnd > pStart)
                    {
                        var paramName = trimmed.Substring(pStart + 1, pEnd - pStart - 1);
                        var isObjectSubfield = paramName is "signalType" or "evidence" or "observedAt" or "deadlineDate"
                            or "atsType" or "tenant" or "slug" or "fetchMethod" or "notes" or "isActive"
                            or "eventType" or "eventDate" or "eventSource";

                        // If it's a known object subfield (and not a tool parameter like notes on tracker_log or search_run)
                        if (currentTool == "company_record" && (paramName is "atsType" or "tenant" or "slug" or "fetchMethod" or "notes" or "isActive"))
                        {
                            continue;
                        }
                        if (currentTool == "posting_record" && (paramName is "signalType" or "evidence" or "observedAt" or "deadlineDate"))
                        {
                            continue;
                        }
                        if (currentTool == "application_record" && (paramName is "eventType" or "eventDate" or "eventSource" or "notes"))
                        {
                            continue;
                        }

                        result[currentTool].Add(paramName);
                    }
                }
            }
        }
    }

    private static string GetMcpDllPath()
    {
        var mcpDllPath = typeof(Mcp.Stdio.Program).Assembly.Location;
        if (string.IsNullOrWhiteSpace(mcpDllPath) || !File.Exists(mcpDllPath))
        {
            mcpDllPath = Path.Combine(AppContext.BaseDirectory, "RoleRadar.Mcp.Stdio.dll");
        }

        Assert.True(File.Exists(mcpDllPath), $"Expected built MCP server assembly at '{mcpDllPath}'.");
        return mcpDllPath;
    }

    private sealed class McpProcessClient : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly System.Text.StringBuilder _stderr = new();
        private int _nextId = 1;

        private McpProcessClient(Process process)
        {
            _process = process;
        }

        public string GetStderr()
        {
            lock (_stderr)
            {
                return _stderr.ToString();
            }
        }

        public static async Task<McpProcessClient> StartAsync(string mcpDllPath, IReadOnlyDictionary<string, string> env)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{mcpDllPath}\"",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var (k, v) in env)
            {
                psi.Environment[k] = v;
            }

            var proc = Process.Start(psi)!;
            Assert.NotNull(proc);

            var client = new McpProcessClient(proc);
            proc.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    lock (client._stderr)
                    {
                        client._stderr.AppendLine(e.Data);
                    }
                }
            };
            proc.BeginErrorReadLine();

            await client.InitializeAsync();
            return client;
        }

        private async Task InitializeAsync()
        {
            var initId = _nextId++;
            var initJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = initId,
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new { },
                    clientInfo = new { name = "race-test", version = "1.0.0" }
                }
            });

            await _process.StandardInput.WriteLineAsync(initJson);
            await _process.StandardInput.FlushAsync();
            var initResponse = await _process.StandardOutput.ReadLineAsync();
            Assert.NotNull(initResponse);

            var notifJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                method = "notifications/initialized"
            });
            await _process.StandardInput.WriteLineAsync(notifJson);
            await _process.StandardInput.FlushAsync();
        }

        public async Task<JsonElement> ListToolsAsync()
        {
            var id = Interlocked.Increment(ref _nextId);
            var request = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = id,
                method = "tools/list"
            });

            await _process.StandardInput.WriteLineAsync(request);
            await _process.StandardInput.FlushAsync();

            var responseLine = await _process.StandardOutput.ReadLineAsync();
            Assert.NotNull(responseLine);

            using var doc = JsonDocument.Parse(responseLine);
            if (!doc.RootElement.TryGetProperty("result", out var resultElement))
            {
                throw new InvalidOperationException($"MCP error response: {responseLine}");
            }

            return resultElement.Clone();
        }

        public async Task<JsonDocument> CallToolRawAsync(string name, object arguments)
        {
            var id = Interlocked.Increment(ref _nextId);
            var request = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = id,
                method = "tools/call",
                @params = new
                {
                    name = name,
                    arguments = arguments
                }
            });

            await _process.StandardInput.WriteLineAsync(request);
            await _process.StandardInput.FlushAsync();

            var responseLine = await _process.StandardOutput.ReadLineAsync();
            Assert.NotNull(responseLine);

            return JsonDocument.Parse(responseLine);
        }

        public async Task<JsonElement> CallToolAsync(string name, object arguments)
        {
            var id = Interlocked.Increment(ref _nextId);
            var request = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = id,
                method = "tools/call",
                @params = new
                {
                    name = name,
                    arguments = arguments
                }
            });

            await _process.StandardInput.WriteLineAsync(request);
            await _process.StandardInput.FlushAsync();

            var responseLine = await _process.StandardOutput.ReadLineAsync();
            Assert.NotNull(responseLine);

            using var doc = JsonDocument.Parse(responseLine);
            if (!doc.RootElement.TryGetProperty("result", out var resultElement))
            {
                throw new InvalidOperationException($"MCP error response: {responseLine}");
            }

            var content = resultElement.GetProperty("content");
            var text = content[0].GetProperty("text").GetString();
            Assert.NotNull(text);

            try
            {
                return JsonDocument.Parse(text).RootElement.Clone();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to parse tool text as JSON. Text was: '{text}'. Host stderr: {GetStderr()}", ex);
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                _process.StandardInput.Close();
                await _process.WaitForExitAsync();
            }
            finally
            {
                _process.Dispose();
            }
        }
    }
}
