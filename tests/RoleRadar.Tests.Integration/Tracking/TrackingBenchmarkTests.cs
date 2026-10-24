using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Application;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Queries;
using RoleRadar.Domain.Common;
using RoleRadar.Infrastructure;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Tests.Integration.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Integration.Tracking;

[CollectionDefinition("BenchmarkCollection", DisableParallelization = true)]
public class BenchmarkTestCollectionDefinition : ICollectionFixture<ContainerImagesFixture>
{
}

[Collection("BenchmarkCollection")]
public sealed class TrackingBenchmarkTests : IAsyncLifetime
{
    private readonly SharedMongoFixture _sharedMongo;
    private readonly string _databaseName;
    private MongoClient? _client;

    public TrackingBenchmarkTests(SharedMongoFixture sharedMongo)
    {
        _sharedMongo = sharedMongo;
        _databaseName = _sharedMongo.CreateDatabaseName("rrtest_benchmark");
    }

    public ValueTask InitializeAsync()
    {
        _client = new MongoClient(_sharedMongo.ConnectionString);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_client != null)
            {
                await _sharedMongo.DropDatabaseAsync(_databaseName);
            }
        }
        finally
        {
            _client?.Dispose();
        }
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task Benchmark_5000Postings_AssertsLookupP95Sub100Ms_AndSearchP95Sub200Ms()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddRoleRadarApplication();
        services.AddRoleRadarInfrastructure(opts =>
        {
            opts.ConnectionString = _sharedMongo.ConnectionString;
            opts.DatabaseName = _databaseName;
            opts.ReadinessTimeout = TimeSpan.FromSeconds(5);
        });

        await using var provider = services.BuildServiceProvider();

        var db = _client!.GetDatabase(_databaseName);
        await TrackingIndexInitializer.EnsureIndexesAsync(db, CancellationToken.None);

        var postingsCol = db.GetCollection<BsonDocument>(TrackingMongoOptions.PostingsCollectionName);
        var companyId = Guid.CreateVersion7();
        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        const string owner = "local-owner";

        const int totalPostings = 5000;
        var batch = new List<BsonDocument>(totalPostings);
        var targetPostingIds = new List<Guid>();

        for (var i = 0; i < totalPostings; i++)
        {
            var id = Guid.CreateVersion7();
            if (i % 200 == 0)
            {
                targetPostingIds.Add(id);
            }

            var title = (i % 250 == 0)
                ? $"Staff Platform Engineer Lead #{i}"
                : $"Synthetic Software Engineer #{i}";

            var reqId = $"REQ-{i:D6}";
            var dedupA = $"acme|{reqId}";
            var updatedAt = baseTime.AddSeconds(i);

            batch.Add(new BsonDocument
            {
                ["_id"] = new BsonBinaryData(id, GuidRepresentation.Standard),
                ["owner_id"] = owner,
                ["company_id"] = new BsonBinaryData(companyId, GuidRepresentation.Standard),
                ["title"] = title,
                ["normalized_title"] = title.ToLowerInvariant(),
                ["source_kind"] = "ats",
                ["ats_tenant"] = "acme",
                ["requisition_id"] = reqId,
                ["dedup_key_a"] = dedupA,
                ["disposition"] = "lead",
                ["remote_type"] = "hybrid",
                ["version_token"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
                ["created_at"] = new BsonDateTime(updatedAt),
                ["updated_at"] = new BsonDateTime(updatedAt),
                ["last_verified_at"] = new BsonDateTime(updatedAt)
            });
        }

        await postingsCol.InsertManyAsync(batch);

        var queryHandler = provider.GetRequiredService<IQueryHandler<TrackerQueryRequest, TrackerQueryResultDto>>();

        // 1. Direct Lookup / Dedup lookup benchmark (p95 < 100 ms)
        // Warmup lookup
        var warmupLookup = await queryHandler.HandleAsync(new TrackerQueryRequest(
            Entity: "posting_dedup_lookup",
            DedupKey: "acme|REQ-000000"));
        Assert.True(warmupLookup.IsSuccess);

        var lookupLatencies = new List<double>(50);
        for (var i = 0; i < 50; i++)
        {
            var targetIdx = i * 100;
            var req = $"acme|REQ-{targetIdx:D6}";
            var sw = Stopwatch.StartNew();
            var res = await queryHandler.HandleAsync(new TrackerQueryRequest(
                Entity: "posting_dedup_lookup",
                DedupKey: req));
            sw.Stop();
            Assert.True(res.IsSuccess);
            lookupLatencies.Add(sw.Elapsed.TotalMilliseconds);
        }

        lookupLatencies.Sort();
        var lookupP95Index = (int)Math.Ceiling(lookupLatencies.Count * 0.95) - 1;
        var lookupP95Ms = lookupLatencies[Math.Clamp(lookupP95Index, 0, lookupLatencies.Count - 1)];

        Assert.True(lookupP95Ms < 100.0,
            $"Expected p95 lookup latency < 100 ms across 5,000 postings, but measured {lookupP95Ms:F2} ms.");

        // 2. Search / Filter query benchmark (p95 < 200 ms)
        // Warmup search
        var warmupSearch = await queryHandler.HandleAsync(new TrackerQueryRequest(
            Entity: "postings",
            Disposition: "lead",
            Page: 1,
            PageSize: 50));
        Assert.True(warmupSearch.IsSuccess);

        var searchLatencies = new List<double>(40);
        for (var i = 0; i < 40; i++)
        {
            var page = (i % 5) + 1;
            var sw = Stopwatch.StartNew();
            var res = await queryHandler.HandleAsync(new TrackerQueryRequest(
                Entity: "postings",
                Disposition: "lead",
                Page: page,
                PageSize: 50));
            sw.Stop();
            Assert.True(res.IsSuccess);
            searchLatencies.Add(sw.Elapsed.TotalMilliseconds);
        }

        searchLatencies.Sort();
        var searchP95Index = (int)Math.Ceiling(searchLatencies.Count * 0.95) - 1;
        var searchP95Ms = searchLatencies[Math.Clamp(searchP95Index, 0, searchLatencies.Count - 1)];

        Assert.True(searchP95Ms < 200.0,
            $"Expected p95 search/filter latency < 200 ms across 5,000 postings, but measured {searchP95Ms:F2} ms.");
    }
}
