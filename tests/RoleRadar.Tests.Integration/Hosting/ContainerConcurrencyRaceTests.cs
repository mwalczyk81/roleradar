using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Tests.Integration.Fixtures;
using RoleRadar.Tests.Integration.Tracking;
using Xunit;

namespace RoleRadar.Tests.Integration.Hosting;

[Collection("ContainerRaceCollection")]
public sealed class ContainerConcurrencyRaceTests
{
    private readonly SharedMongoFixture _sharedMongo;

    public ContainerConcurrencyRaceTests(SharedMongoFixture sharedMongo)
    {
        _sharedMongo = sharedMongo ?? throw new ArgumentNullException(nameof(sharedMongo));
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task ContainerMcp_ConcurrentSearchRunRace_EnforcesUniqueConstraint_ExactlyOneSuccessAndSevenAlreadyLogged()
    {
        var mcpDllPath = McpHostLocator.GetMcpDllPath();
        var uniqueDb = _sharedMongo.CreateDatabaseName("rrtest_race_searchrun");
        var mongoClient = new MongoClient(_sharedMongo.ConnectionString);
        var db = mongoClient.GetDatabase(uniqueDb);

        const int processCount = 8;
        var clients = new List<McpProcessClient>();

        try
        {
            var env = _sharedMongo.CreateProcessEnvironment(uniqueDb);
            for (var i = 0; i < processCount; i++)
            {
                clients.Add(await McpProcessClient.StartAsync(mcpDllPath, env, forceContainer: true));
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
                    notes = $"Container race worker {idx}"
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

    [Fact]
    [Trait("Category", "Slow")]
    public async Task ContainerMcp_ConcurrentPostingRecord_EnforcesDeduplicationIdempotencyUnderRace()
    {
        var mcpDllPath = McpHostLocator.GetMcpDllPath();
        var uniqueDb = _sharedMongo.CreateDatabaseName("rrtest_race_posting");
        var mongoClient = new MongoClient(_sharedMongo.ConnectionString);
        var db = mongoClient.GetDatabase(uniqueDb);

        const int processCount = 8;
        var clients = new List<McpProcessClient>();

        try
        {
            var env = _sharedMongo.CreateProcessEnvironment(uniqueDb);
            for (var i = 0; i < processCount; i++)
            {
                clients.Add(await McpProcessClient.StartAsync(mcpDllPath, env, forceContainer: true));
            }

            // Create company first through client 0
            var compResp = await clients[0].CallToolAsync("company_record", new
            {
                name = "Container Race Deduplication Corp",
                ownershipType = "private",
                status = "target"
            });
            var compId = compResp.GetProperty("data").GetProperty("company").GetProperty("id").GetString();
            Assert.NotNull(compId);

            // Race 8 container processes recording the exact same posting
            var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var tasks = clients.Select((c, idx) => Task.Run(async () =>
            {
                await barrier.Task;
                return await c.CallToolAsync("posting_record", new
                {
                    companyId = compId,
                    title = "Container Senior Distributed Systems Engineer",
                    atsTenant = "containerats",
                    requisitionId = "REQ-CONTAINER-123"
                });
            })).ToArray();

            barrier.SetResult();
            var results = await Task.WhenAll(tasks);

            var created = results.Where(r =>
                r.TryGetProperty("ok", out var ok) && ok.GetBoolean() &&
                r.GetProperty("data").TryGetProperty("status", out var st) && st.GetString() == "created").ToList();

            var alreadyTracked = results.Where(r =>
                r.TryGetProperty("ok", out var ok) && ok.GetBoolean() &&
                r.GetProperty("data").TryGetProperty("status", out var st) && st.GetString() == "already_tracked").ToList();

            Assert.Single(created);
            Assert.Equal(processCount - 1, alreadyTracked.Count);

            var postingsCol = db.GetCollection<BsonDocument>(TrackingMongoOptions.PostingsCollectionName);
            var count = await postingsCol.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);
            Assert.Equal(1, count);
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
