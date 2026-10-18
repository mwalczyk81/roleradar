using System.Diagnostics;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Tests.Integration.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Integration.Tracking;

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
        var mcpDllPath = McpHostLocator.GetMcpDllPath();
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
    public async Task ShippedMcpHost_PreExistingDuplicateDataViolatesIndex_ReturnsValidationErrorAcrossTools()
    {
        var mcpDllPath = McpHostLocator.GetMcpDllPath();
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
    public async Task ShippedMcpHost_PostingRecord_Signals_ValidatesPersistsAndAffectsGhostLikelihood()
    {
        var mcpDllPath = McpHostLocator.GetMcpDllPath();
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
        var mcpDllPath = McpHostLocator.GetMcpDllPath();
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
        var mcpDllPath = McpHostLocator.GetMcpDllPath();
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


}
