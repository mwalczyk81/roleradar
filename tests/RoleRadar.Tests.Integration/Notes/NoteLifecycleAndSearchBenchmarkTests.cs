using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Application;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Notes.Commands;
using RoleRadar.Application.Notes.Queries;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Notes;
using RoleRadar.Infrastructure;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Mcp.Stdio.Tools;
using Testcontainers.MongoDb;
using Xunit;

namespace RoleRadar.Tests.Integration.Notes;

public sealed class NoteLifecycleAndSearchBenchmarkTests : IAsyncLifetime
{
    private readonly MongoDbContainer _mongoContainer = new MongoDbBuilder("mongo:8.0.34")
        .WithReplicaSet("rs0")
        .Build();

    private string _connectionString = string.Empty;
    private string _databaseName = string.Empty;

    public async ValueTask InitializeAsync()
    {
        await _mongoContainer.StartAsync();
        _connectionString = _mongoContainer.GetConnectionString();
        _databaseName = $"roleradar_us1_{Guid.CreateVersion7():N}";
    }

    public async ValueTask DisposeAsync() => await _mongoContainer.DisposeAsync();

    private ServiceProvider BuildServiceProvider(string? dbNameOverride = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddRoleRadarApplication();
        services.AddRoleRadarInfrastructure(opts =>
        {
            opts.ConnectionString = _connectionString;
            opts.DatabaseName = dbNameOverride ?? _databaseName;
            opts.ReadinessTimeout = TimeSpan.FromSeconds(5);
        });
        services.AddTransient<NoteMcpTools>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task LargeNoteLifecycle_Over100Kb_ConcurrentAppends_SectionReplacement_AndPersistenceAcrossRestart()
    {
        await using var provider1 = BuildServiceProvider();
        var tools1 = provider1.GetRequiredService<NoteMcpTools>();
        var repo1 = provider1.GetRequiredService<INoteRepository>();

        // Build a >120 KB fictional markdown body with # and ## sections
        var sb = new StringBuilder(135_000);
        sb.Append("# Fictional Staff Platform Architecture Dossier\r\n");
        sb.Append("## Executive Summary\r\n");
        sb.Append("Initial executive summary for fictional evaluation.\r\n");
        sb.Append("## Deep Technical Appendix\r\n");
        var fillerParagraph = "Fictional distributed systems evaluation paragraph covering event streaming, idempotency, and consensus under synthetic load. ";
        while (Encoding.UTF8.GetByteCount(sb.ToString()) < 120_000)
        {
            sb.Append(fillerParagraph).Append("\r\n");
        }

        var largeBody = sb.ToString();
        Assert.True(Encoding.UTF8.GetByteCount(largeBody) > 100_000);

        // 1. Create via NoteMcpTools
        var createJson = await tools1.NoteCreateAsync(
            title: "Large Fictional Dossier",
            body: largeBody,
            area: "criteria",
            tags: ["distributed", "dotnet"],
            provenanceKind: "stated",
            provenanceDate: "2026-10-17",
            provenanceQualifier: "synthetic test");

        using var createDoc = JsonDocument.Parse(createJson);
        Assert.True(createDoc.RootElement.GetProperty("ok").GetBoolean());
        var createdData = createDoc.RootElement.GetProperty("data");
        var noteId = createdData.GetProperty("id").GetGuid();

        // 2. Concurrent appends from two independent sessions
        await using var provider2 = BuildServiceProvider();
        var appendHandler1 = provider1.GetRequiredService<ICommandHandler<AppendToNoteCommand, NoteUpdatedDto>>();
        var appendHandler2 = provider2.GetRequiredService<ICommandHandler<AppendToNoteCommand, NoteUpdatedDto>>();

        var appendTask1 = appendHandler1.HandleAsync(new AppendToNoteCommand(noteId, "\r\n- Session 1 concurrent append line.\r\n", "mcp-session-1"));
        var appendTask2 = appendHandler2.HandleAsync(new AppendToNoteCommand(noteId, "\r\n- Session 2 concurrent append line.\r\n", "mcp-session-2"));

        var appendResults = await Task.WhenAll(appendTask1, appendTask2);
        Assert.All(appendResults, r => Assert.True(r.IsSuccess, r.Error?.Message));

        // Verify no NoteRevision was created by appends
        var revisionsAfterAppend = await repo1.GetRevisionsAsync(OwnerId.Default.Value, noteId);
        Assert.Empty(revisionsAfterAppend.Value!);

        // 3. Read current version token via GetNote
        var getHandler = provider1.GetRequiredService<IQueryHandler<GetNoteQuery, NoteDetailDto>>();
        var afterAppends = await getHandler.HandleAsync(new GetNoteQuery(noteId));
        Assert.True(afterAppends.IsSuccess);
        Assert.Contains("Session 1 concurrent append line.", afterAppends.Value!.Body);
        Assert.Contains("Session 2 concurrent append line.", afterAppends.Value.Body);

        // 4. Version-checked section replacement via NoteMcpTools
        var replaceJson = await tools1.NoteReplaceSectionAsync(
            id: noteId,
            heading: "Executive Summary",
            newSectionBody: "## Executive Summary\r\nUpdated executive summary with verified compensation and scope.\r\n",
            versionToken: afterAppends.Value.VersionToken);

        using var replaceDoc = JsonDocument.Parse(replaceJson);
        Assert.True(replaceDoc.RootElement.GetProperty("ok").GetBoolean());

        // Verify stale version token returns structured error envelope
        var staleReplaceJson = await tools1.NoteReplaceSectionAsync(
            id: noteId,
            heading: "Executive Summary",
            newSectionBody: "## Executive Summary\r\nShould fail with StaleVersionToken.\r\n",
            versionToken: afterAppends.Value.VersionToken);

        using var staleDoc = JsonDocument.Parse(staleReplaceJson);
        Assert.False(staleDoc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("StaleVersionToken", staleDoc.RootElement.GetProperty("error").GetProperty("code").GetString());

        // 5. Simulate session restart with a fresh DI container and verify full integrity + note_get pagination metadata
        await using var providerAfterRestart = BuildServiceProvider();
        var toolsAfterRestart = providerAfterRestart.GetRequiredService<NoteMcpTools>();
        var getJson = await toolsAfterRestart.NoteGetAsync(noteId);
        using var getDoc = JsonDocument.Parse(getJson);
        Assert.True(getDoc.RootElement.GetProperty("ok").GetBoolean());
        var getData = getDoc.RootElement.GetProperty("data");
        var fetchedBody = getData.GetProperty("body").GetString()!;
        Assert.Contains("Updated executive summary with verified compensation and scope.", fetchedBody);
        Assert.Contains("Session 1 concurrent append line.", fetchedBody);
        Assert.Contains("Session 2 concurrent append line.", fetchedBody);
        Assert.Equal(fetchedBody.Length, getData.GetProperty("totalChars").GetInt32());
        Assert.False(getData.GetProperty("truncated").GetBoolean());
        Assert.Equal(JsonValueKind.Null, getData.GetProperty("nextOffset").ValueKind);
        Assert.Equal(1, getData.GetProperty("revisionCount").GetInt32());

        // 6. Append additional text so totalChars exceeds 200,000 and verify note_get pagination via MCP tool
        var extraCharsNeeded = (230_000 - fetchedBody.Length) + 10;
        var extraAppendJson = await toolsAfterRestart.NoteAppendAsync(
            id: noteId,
            textToAppend: new string('P', extraCharsNeeded));
        using var extraAppendDoc = JsonDocument.Parse(extraAppendJson);
        Assert.True(extraAppendDoc.RootElement.GetProperty("ok").GetBoolean());

        var page1Json = await toolsAfterRestart.NoteGetAsync(noteId);
        using var page1Doc = JsonDocument.Parse(page1Json);
        var page1Data = page1Doc.RootElement.GetProperty("data");
        var page1Body = page1Data.GetProperty("body").GetString()!;
        var totalChars = page1Data.GetProperty("totalChars").GetInt32();
        Assert.Equal(200_000, page1Body.Length);
        Assert.True(totalChars > 200_000);
        Assert.True(page1Data.GetProperty("truncated").GetBoolean());
        var nextOffset = page1Data.GetProperty("nextOffset").GetInt32();
        Assert.Equal(200_000, nextOffset);

        var page2Json = await toolsAfterRestart.NoteGetAsync(noteId, offset: nextOffset, maxChars: 200_000);
        using var page2Doc = JsonDocument.Parse(page2Json);
        var page2Data = page2Doc.RootElement.GetProperty("data");
        var page2Body = page2Data.GetProperty("body").GetString()!;
        Assert.Equal(totalChars - 200_000, page2Body.Length);
        Assert.Equal(totalChars, page2Data.GetProperty("totalChars").GetInt32());
        Assert.False(page2Data.GetProperty("truncated").GetBoolean());
        Assert.Equal(JsonValueKind.Null, page2Data.GetProperty("nextOffset").ValueKind);
        Assert.Equal(
            page1Data.GetProperty("bodyHash").GetString(),
            Sha256Hasher.ComputeLowerHex(page1Body + page2Body));
    }

    [Fact]
    public async Task SearchAcross5000FictionalNotes_SupportsStemmingQuotedPhrasesAndFilters_WithP95Under200Ms()
    {
        var benchmarkDbName = $"roleradar_bench_{Guid.CreateVersion7():N}";
        await using var provider = BuildServiceProvider(benchmarkDbName);
        var client = provider.GetRequiredService<IMongoClient>();
        var database = client.GetDatabase(benchmarkDbName);
        await MongoBootstrapAndIndexes.EnsureIndexesAsync(database, CancellationToken.None);

        var notesCollection = database.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.NotesCollectionName);
        var baseTime = new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
        var owner = OwnerId.Default.Value;

        // Seed 5,000 fictional notes in batches for fast setup
        var batch = new List<BsonDocument>(5_000);
        for (var i = 1; i <= 5_000; i++)
        {
            var isTargetMatch = i % 250 == 0; // 20 matching notes out of 5,000
            var title = isTargetMatch
                ? $"Fictional Architecture Blueprint #{i} for Distributed Scaling"
                : $"Synthetic Role Note #{i} General Engineering";

            var body = isTargetMatch
                ? $"# Overview\nWe arearchitecting resilient microservices and \"staff platform engineer\" workflows for fictional org #{i}.\n"
                : $"# Summary\nStandard fictional engineering note #{i} covering general backend development and testing practices.\n";

            var area = isTargetMatch ? "criteria" : (i % 2 == 0 ? "runbook" : "general");
            var tags = isTargetMatch
                ? new BsonArray { "dotnet", "kafka", "remote" }
                : new BsonArray { "general", $"tag-{i % 20}" };

            var noteId = Guid.CreateVersion7();
            var versionToken = Guid.CreateVersion7();
            var updatedAt = baseTime.AddSeconds(i);

            batch.Add(new BsonDocument
            {
                ["_id"] = new BsonBinaryData(noteId, GuidRepresentation.Standard),
                ["owner_id"] = owner,
                ["title"] = title,
                ["body"] = body,
                ["current_body_hash"] = Sha256Hasher.ComputeLowerHex(body),
                ["tags"] = tags,
                ["area"] = area,
                ["provenance"] = new BsonDocument
                {
                    ["kind"] = "stated",
                    ["date"] = "2026-10-17",
                    ["qualifier"] = BsonNull.Value
                },
                ["import_metadata"] = BsonNull.Value,
                ["status"] = "active",
                ["version_token"] = new BsonBinaryData(versionToken, GuidRepresentation.Standard),
                ["revision_count"] = 0,
                ["created_at"] = new BsonDateTime(updatedAt),
                ["updated_at"] = new BsonDateTime(updatedAt)
            });
        }

        await notesCollection.InsertManyAsync(batch);

        var searchHandler = provider.GetRequiredService<IQueryHandler<SearchNotesQuery, NoteSearchResponseDto>>();

        // Verify quoted phrase + stemmed word ("architects" stems to "architect" matching "Architecture") + ANDed tags & area
        var query = new SearchNotesQuery(
            Query: "\"staff platform engineer\" architects",
            Tags: ["dotnet", "kafka"],
            Area: "criteria",
            IncludeArchived: false,
            Limit: 25);

        // Warm up once
        var warmup = await searchHandler.HandleAsync(query);
        Assert.True(warmup.IsSuccess);
        Assert.Equal(20, warmup.Value!.Results.Count);

        // Measure 40 iterations and compute p95 latency
        var latenciesMs = new List<double>(40);
        for (var i = 0; i < 40; i++)
        {
            var sw = Stopwatch.StartNew();
            var res = await searchHandler.HandleAsync(query);
            sw.Stop();
            Assert.True(res.IsSuccess);
            Assert.Equal(20, res.Value!.Results.Count);
            latenciesMs.Add(sw.Elapsed.TotalMilliseconds);
        }

        latenciesMs.Sort();
        var p95Index = (int)Math.Ceiling(latenciesMs.Count * 0.95) - 1;
        var p95Ms = latenciesMs[Math.Clamp(p95Index, 0, latenciesMs.Count - 1)];

        Assert.True(p95Ms < 200.0, $"Expected search p95 < 200 ms across 5,000 notes, but measured p95 = {p95Ms:F2} ms.");
    }

    [Fact]
    public async Task McpStdioHost_StdoutCarriesExclusivelyValidJsonRpcFrames_AndRoutesLogsToStderr()
    {
        var hostAssemblyPath = typeof(NoteMcpTools).Assembly.Location;
        if (string.IsNullOrWhiteSpace(hostAssemblyPath) || !File.Exists(hostAssemblyPath))
        {
            hostAssemblyPath = Path.Combine(AppContext.BaseDirectory, "RoleRadar.Mcp.Stdio.dll");
        }

        Assert.True(File.Exists(hostAssemblyPath), $"Expected built MCP stdio host assembly at '{hostAssemblyPath}'.");

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{hostAssemblyPath}\"",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.Environment["ROLERADAR_MONGO_CONNECTION_STRING"] = _connectionString;
        psi.Environment["ROLERADAR_MONGO_DATABASE"] = _databaseName;

        using var proc = Process.Start(psi)!;
        Assert.NotNull(proc);

        var stderrBuffer = new StringBuilder();
        var stderrLock = new object();
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (stderrLock)
                {
                    stderrBuffer.AppendLine(e.Data);
                }
            }
        };
        proc.BeginErrorReadLine();

        string FormatDiagnosticMessage(string message)
        {
            string stderr;
            lock (stderrLock)
            {
                stderr = stderrBuffer.ToString();
            }

            return $"{message}\nHost assembly: {hostAssemblyPath}\nHost stderr:\n{(string.IsNullOrWhiteSpace(stderr) ? "<empty>" : stderr)}";
        }

        try
        {
            // Send JSON-RPC 2.0 initialize request followed by tools/list request
            const string initRequest = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2024-11-05\",\"capabilities\":{},\"clientInfo\":{\"name\":\"hygiene-test\",\"version\":\"1.0.0\"}}}";
            const string initializedNotification = "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}";
            const string listToolsRequest = "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\",\"params\":{}}";

            await proc.StandardInput.WriteLineAsync(initRequest);
            await proc.StandardInput.FlushAsync();

            var firstStdoutLine = await ReadLineWithTimeoutAsync(proc.StandardOutput, TimeSpan.FromSeconds(15));
            Assert.False(
                string.IsNullOrWhiteSpace(firstStdoutLine),
                FormatDiagnosticMessage("Expected initialize JSON-RPC response on stdout."));

            await proc.StandardInput.WriteLineAsync(initializedNotification);
            await proc.StandardInput.WriteLineAsync(listToolsRequest);
            await proc.StandardInput.FlushAsync();

            var secondStdoutLine = await ReadLineWithTimeoutAsync(proc.StandardOutput, TimeSpan.FromSeconds(15));
            Assert.False(
                string.IsNullOrWhiteSpace(secondStdoutLine),
                FormatDiagnosticMessage("Expected tools/list JSON-RPC response on stdout."));

            proc.StandardInput.Close();

            // Every line emitted to stdout MUST be a valid JSON-RPC 2.0 object with "jsonrpc": "2.0"
            foreach (var line in new[] { firstStdoutLine!, secondStdoutLine! })
            {
                using var json = JsonDocument.Parse(line);
                Assert.True(
                    json.RootElement.TryGetProperty("jsonrpc", out var versionProp) && versionProp.GetString() == "2.0",
                    FormatDiagnosticMessage($"Expected JSON-RPC 2.0 frame on stdout, but received: {line}"));
            }

            // Verify tools/list exposes the US1 tools (and <= 8 tools total)
            using var toolsJson = JsonDocument.Parse(secondStdoutLine!);
            var toolsArray = toolsJson.RootElement.GetProperty("result").GetProperty("tools");
            var toolNames = toolsArray.EnumerateArray()
                .Select(t => t.GetProperty("name").GetString()!)
                .ToArray();

            Assert.True(
                toolNames.Length <= 8,
                FormatDiagnosticMessage($"Expected at most 8 MCP tools, found {toolNames.Length}."));
            Assert.True(toolNames.Contains("note_create"), FormatDiagnosticMessage("Missing 'note_create' tool."));
            Assert.True(toolNames.Contains("note_get"), FormatDiagnosticMessage("Missing 'note_get' tool."));
            Assert.True(toolNames.Contains("note_append"), FormatDiagnosticMessage("Missing 'note_append' tool."));
            Assert.True(toolNames.Contains("note_replace_section"), FormatDiagnosticMessage("Missing 'note_replace_section' tool."));
            Assert.True(toolNames.Contains("note_search"), FormatDiagnosticMessage("Missing 'note_search' tool."));
        }
        finally
        {
            if (!proc.HasExited)
            {
                proc.Kill(entireProcessTree: true);
            }
        }
    }

    private static async Task<string?> ReadLineWithTimeoutAsync(StreamReader reader, TimeSpan timeout)
    {
        try
        {
            using var cts = new CancellationTokenSource(timeout);
            return await reader.ReadLineAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }
}
