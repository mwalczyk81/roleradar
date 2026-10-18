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
            var createAudits = await auditCol.Find(Builders<BsonDocument>.Filter.Eq("action", "company_create")).ToListAsync();
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
                    Builders<BsonDocument>.Filter.Eq("action", "posting_create"),
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
                    Builders<BsonDocument>.Filter.Eq("action", "posting_create"),
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

                Assert.Single(createdC);
                Assert.Equal(processCount - 1, alreadyTrackedC.Count);

                var auditsC = await auditCol.Find(Builders<BsonDocument>.Filter.And(
                    Builders<BsonDocument>.Filter.Eq("action", "posting_create"),
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

    private static string GetMcpDllPath()
    {
        var mcpDllPath = typeof(RoleRadar.Mcp.Stdio.Program).Assembly.Location;
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

            return JsonDocument.Parse(text).RootElement.Clone();
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
