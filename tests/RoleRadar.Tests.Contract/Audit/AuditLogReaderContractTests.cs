using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Audit.Queries;
using RoleRadar.Domain.Common;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Tests.Contract.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Contract.Audit;

/// <summary>
/// Feature 013 (Principle II, data-model.md "IAuditLogReader"): the Mongo reader is owner-scoped on every call, orders and
/// pages by (occurred_at desc, _id desc) with a stable keyset, supports each filter, honors the limit, projects only the
/// listed fields, and creates the entity index. Runs against the shared throwaway container, never a developer database.
/// </summary>
public sealed class AuditLogReaderContractTests : IAsyncLifetime
{
    private const string OtherOwner = "other-owner-fictional";
    private const string SentinelAppended = "SENTINEL-APPENDED-TEXT-91ac";
    private const string SentinelBefore = "SENTINEL-BEFORE-VALUE-91ac";
    private const string SentinelAfter = "SENTINEL-AFTER-VALUE-91ac";
    private const string SentinelExcerpt = "SENTINEL-EXCERPT-91ac";

    private static readonly DateTime Origin = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);

    private readonly SharedMongoFixture _sharedMongo;
    private MongoClient _client = null!;
    private IMongoDatabase _database = null!;
    private MongoOptions _options = null!;
    private MongoAuditLogReader _reader = null!;

    public AuditLogReaderContractTests(SharedMongoFixture sharedMongo)
    {
        _sharedMongo = sharedMongo;
    }

    public async ValueTask InitializeAsync()
    {
        _options = new MongoOptions
        {
            ConnectionString = _sharedMongo.ConnectionString,
            DatabaseName = _sharedMongo.CreateDatabaseName("rrtest_audit_reader"),
            ReadinessTimeout = TimeSpan.FromSeconds(5)
        };

        MongoBootstrapAndIndexes.RegisterSerializers();
        _client = new MongoClient(_options.ConnectionString);
        _database = _client.GetDatabase(_options.DatabaseName);
        await MongoBootstrapAndIndexes.EnsureIndexesAsync(_database, CancellationToken.None);

        var probe = new MongoReadinessProbe(_client, Options.Create(_options), NullLogger<MongoReadinessProbe>.Instance);
        _reader = new MongoAuditLogReader(_client, Options.Create(_options), probe);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _client.DropDatabaseAsync(_options.DatabaseName);
        }
        finally
        {
            _client.Dispose();
        }
    }

    private IMongoCollection<BsonDocument> Audit =>
        _database.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.AuditLogCollectionName);

    private static BsonBinaryData Bin(Guid id) => new(id, GuidRepresentation.Standard);

    private static BsonDocument Entry(
        DateTime occurredAtUtc,
        string owner = OwnerId.DefaultValue,
        Guid? id = null,
        string entityType = "posting",
        BsonValue? entityId = null,
        string actor = "cli-user",
        string operation = "posting_transition",
        string? requestCorrelationId = null,
        string[]? changedFields = null)
    {
        var doc = new BsonDocument
        {
            ["_id"] = Bin(id ?? Guid.CreateVersion7()),
            ["owner_id"] = owner,
            ["actor"] = actor,
            ["operation"] = operation,
            ["entity_type"] = entityType,
            ["entity_id"] = entityId ?? Bin(Guid.CreateVersion7()),
            ["resulting_version_token"] = Bin(Guid.CreateVersion7()),
            ["occurred_at"] = new BsonDateTime(occurredAtUtc),
            ["appended_text"] = SentinelAppended,
            ["before_summary"] = new BsonDocument { ["body_hash"] = "h", ["byte_length"] = 1, ["status"] = "s", ["excerpt"] = SentinelExcerpt },
            ["after_summary"] = new BsonDocument { ["body_hash"] = "h", ["byte_length"] = 1, ["status"] = "s", ["excerpt"] = SentinelExcerpt },
            ["details"] = new BsonDocument
            {
                ["changed_fields"] = new BsonArray(changedFields ?? ["disposition", "reason"]),
                ["before"] = new BsonDocument { ["disposition"] = SentinelBefore },
                ["after"] = new BsonDocument { ["disposition"] = SentinelAfter },
            },
        };

        if (requestCorrelationId is not null)
        {
            doc["request_correlation_id"] = requestCorrelationId;
        }

        return doc;
    }

    private async Task SeedAsync(params BsonDocument[] entries) => await Audit.InsertManyAsync(entries);

    private async Task<AuditListPage> ListAsync(ListAuditEntriesQuery query)
    {
        var result = await _reader.ListAsync(query);
        Assert.True(result.IsSuccess, result.Error?.Message);
        return result.Value!;
    }

    private static ListAuditEntriesQuery Query() => new(OwnerId.Default);

    private static Guid IdOf(BsonDocument doc) => doc["_id"].AsBsonBinaryData.ToGuid(GuidRepresentation.Standard);

    [Fact]
    public async Task AnEntryWrittenBeforeFeature014_KeepsItsOldFormatRequestCorrelationId_AndIsListedUnchanged()
    {
        // Spec 014 scenario 9: audit is append-only, so an entry with the 013 style id (a UUID or a header value) is not
        // rewritten. A raw legacy document and a new 32-hex one are both listed with their stored values. Green by design.
        const string legacy = "legacy-corr-0001";
        const string traceId = "0af7651916cd43dd8448eb211c80319c";
        await SeedAsync(
            Entry(Origin, actor: "legacy-1", requestCorrelationId: legacy),
            Entry(Origin.AddMinutes(1), actor: "new-1", requestCorrelationId: traceId));

        var page = await ListAsync(Query());

        Assert.Equal(2, page.Items.Count);
        Assert.Equal(legacy, page.Items.Single(i => i.Actor == "legacy-1").RequestCorrelationId);
        Assert.Equal(traceId, page.Items.Single(i => i.Actor == "new-1").RequestCorrelationId);
    }

    [Fact]
    public async Task ReturnsOnlyTheQueriedOwnersEntries()
    {
        var mine = Entry(Origin, actor: "mine-1");
        var theirs = Entry(Origin.AddMinutes(1), owner: OtherOwner, actor: "theirs-1");
        await SeedAsync(mine, theirs);

        var ownerPage = await ListAsync(Query());
        var otherPage = await ListAsync(new ListAuditEntriesQuery(new OwnerId(OtherOwner)));

        Assert.Equal(["mine-1"], ownerPage.Items.Select(i => i.Actor));
        Assert.Equal(["theirs-1"], otherPage.Items.Select(i => i.Actor));
    }

    [Fact]
    public async Task OwnerIsolationHoldsForEveryFilterAndForPaging()
    {
        var sharedEntity = Guid.CreateVersion7();
        await SeedAsync(
            Entry(Origin, entityType: "company", entityId: Bin(sharedEntity), actor: "same-actor"),
            Entry(Origin.AddMinutes(1), owner: OtherOwner, entityType: "company", entityId: Bin(sharedEntity), actor: "same-actor"));

        var page = await ListAsync(Query() with
        {
            EntityType = "company",
            EntityId = sharedEntity.ToString("D"),
            Actor = "same-actor",
            FromUtc = new DateTimeOffset(Origin.AddHours(-1)),
            ToUtc = new DateTimeOffset(Origin.AddHours(1)),
            Limit = 1,
        });

        Assert.Single(page.Items);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task OrdersByOccurredAtDescending_ThenByIdDescending()
    {
        var sameInstant = Origin.AddMinutes(5);
        var low = Guid.Parse("01970000-0000-7000-8000-000000000001");
        var high = Guid.Parse("01970000-0000-7000-8000-000000000009");
        await SeedAsync(
            Entry(Origin, actor: "oldest"),
            Entry(sameInstant, id: low, actor: "tie-low-id"),
            Entry(sameInstant, id: high, actor: "tie-high-id"),
            Entry(Origin.AddMinutes(10), actor: "newest"));

        var page = await ListAsync(Query());

        Assert.Equal(["newest", "tie-high-id", "tie-low-id", "oldest"], page.Items.Select(i => i.Actor));
    }

    [Fact]
    public async Task KeysetPaging_IsStable_WhenEntriesAreAppendedBetweenPages()
    {
        var seeded = Enumerable.Range(0, 7).Select(i => Entry(Origin.AddMinutes(i), actor: $"seed-{i}")).ToArray();
        await SeedAsync(seeded);
        var expectedOrder = seeded.OrderByDescending(d => d["occurred_at"].ToUniversalTime()).Select(IdOf).ToArray();

        var first = await ListAsync(Query() with { Limit = 3 });
        Assert.Equal(3, first.Items.Count);
        Assert.NotNull(first.NextCursor);

        // New entries arrive between the pages. They are newer, so they must not shift or repeat anything on later pages.
        await SeedAsync(
            Entry(Origin.AddHours(1), actor: "late-1"),
            Entry(Origin.AddHours(2), actor: "late-2"));

        var second = await ListAsync(Query() with { Limit = 3, Cursor = first.NextCursor });
        Assert.Equal(3, second.Items.Count);
        Assert.NotNull(second.NextCursor);

        var third = await ListAsync(Query() with { Limit = 3, Cursor = second.NextCursor });
        Assert.Single(third.Items);
        Assert.Null(third.NextCursor);

        var seen = first.Items.Concat(second.Items).Concat(third.Items).Select(i => i.Actor).ToArray();
        Assert.Equal(seeded.OrderByDescending(d => d["occurred_at"].ToUniversalTime()).Select(d => d["actor"].AsString), seen);
        Assert.Equal(7, seen.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain("late-1", seen);
        Assert.Equal(expectedOrder.Length, seen.Length);
    }

    [Fact]
    public async Task KeysetPaging_BreaksTiesOnId_WithoutSkippingOrRepeating()
    {
        var instant = Origin.AddMinutes(1);
        var tied = Enumerable.Range(1, 5)
            .Select(i => Entry(instant, id: Guid.Parse($"01970000-0000-7000-8000-00000000000{i}"), actor: $"tied-{i}"))
            .ToArray();
        await SeedAsync(tied);

        var seen = new List<string>();
        string? cursor = null;
        do
        {
            var page = await ListAsync(Query() with { Limit = 2, Cursor = cursor });
            seen.AddRange(page.Items.Select(i => i.Actor));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        Assert.Equal(["tied-5", "tied-4", "tied-3", "tied-2", "tied-1"], seen);
    }

    [Fact]
    public async Task NextCursor_IsNull_OnTheLastPage_EvenWhenItIsExactlyFull()
    {
        await SeedAsync(Enumerable.Range(0, 6).Select(i => Entry(Origin.AddMinutes(i))).ToArray());

        var first = await ListAsync(Query() with { Limit = 3 });
        var second = await ListAsync(Query() with { Limit = 3, Cursor = first.NextCursor });

        Assert.NotNull(first.NextCursor);
        Assert.Equal(3, second.Items.Count);
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public async Task Limit_IsHonored()
    {
        await SeedAsync(Enumerable.Range(0, 30).Select(i => Entry(Origin.AddMinutes(i))).ToArray());

        Assert.Equal(25, (await ListAsync(Query())).Items.Count);
        Assert.Single((await ListAsync(Query() with { Limit = 1 })).Items);
        Assert.Equal(30, (await ListAsync(Query() with { Limit = 100 })).Items.Count);
    }

    [Fact]
    public async Task EntityTypeFilter_ReturnsOnlyThatType()
    {
        await SeedAsync(
            Entry(Origin, entityType: "company", actor: "c"),
            Entry(Origin.AddMinutes(1), entityType: "posting", actor: "p"));

        var page = await ListAsync(Query() with { EntityType = "company" });

        Assert.Equal(["c"], page.Items.Select(i => i.Actor));
    }

    [Fact]
    public async Task EntityIdFilter_MatchesAGuidEntityId_AndAStringEntityId()
    {
        var guidId = Guid.CreateVersion7();
        await SeedAsync(
            Entry(Origin, entityId: Bin(guidId), actor: "guid-entity"),
            Entry(Origin.AddMinutes(1), entityId: "legacy-entity-id", actor: "string-entity"),
            Entry(Origin.AddMinutes(2), actor: "other-entity"));

        var byGuid = await ListAsync(Query() with { EntityId = guidId.ToString("D") });
        var byString = await ListAsync(Query() with { EntityId = "legacy-entity-id" });

        Assert.Equal(["guid-entity"], byGuid.Items.Select(i => i.Actor));
        Assert.Equal(guidId.ToString("D"), byGuid.Items.Single().EntityId);
        Assert.Equal(["string-entity"], byString.Items.Select(i => i.Actor));
        Assert.Equal("legacy-entity-id", byString.Items.Single().EntityId);
    }

    [Fact]
    public async Task ActorFilter_IsAnExactMatch()
    {
        await SeedAsync(
            Entry(Origin, actor: "cli-user"),
            Entry(Origin.AddMinutes(1), actor: "cli-user-2"),
            Entry(Origin.AddMinutes(2), actor: "mcp-stdio"));

        var page = await ListAsync(Query() with { Actor = "cli-user" });

        Assert.Equal(["cli-user"], page.Items.Select(i => i.Actor));
    }

    [Fact]
    public async Task FromIsInclusive_AndToIsExclusive()
    {
        await SeedAsync(
            Entry(Origin.AddMinutes(-1), actor: "before"),
            Entry(Origin, actor: "at-from"),
            Entry(Origin.AddMinutes(5), actor: "inside"),
            Entry(Origin.AddMinutes(10), actor: "at-to"),
            Entry(Origin.AddMinutes(11), actor: "after"));

        var page = await ListAsync(Query() with
        {
            FromUtc = new DateTimeOffset(Origin),
            ToUtc = new DateTimeOffset(Origin.AddMinutes(10)),
        });

        Assert.Equal(["inside", "at-from"], page.Items.Select(i => i.Actor));
    }

    [Fact]
    public async Task FromAndTo_AreNormalizedFromAnyOffset()
    {
        await SeedAsync(Entry(Origin, actor: "noon-utc"));

        var page = await ListAsync(Query() with
        {
            FromUtc = new DateTimeOffset(2026, 11, 9, 13, 59, 0, TimeSpan.FromHours(2)),
            ToUtc = new DateTimeOffset(2026, 11, 9, 14, 1, 0, TimeSpan.FromHours(2)),
        });

        Assert.Equal(["noon-utc"], page.Items.Select(i => i.Actor));
    }

    [Fact]
    public async Task ListsTheFieldsOfAnItem_AndTheRequestCorrelationId()
    {
        var entity = Guid.CreateVersion7();
        await SeedAsync(Entry(Origin, entityType: "posting", entityId: Bin(entity), operation: "posting_transition", actor: "subject-1", requestCorrelationId: "req-0001-aaaa"));

        var item = (await ListAsync(Query())).Items.Single();

        Assert.Equal(Origin, item.OccurredAtUtc);
        Assert.Equal(DateTimeKind.Utc, item.OccurredAtUtc.Kind);
        Assert.Equal("posting", item.EntityType);
        Assert.Equal(entity.ToString("D"), item.EntityId);
        Assert.Equal("posting_transition", item.Operation);
        Assert.Equal("subject-1", item.Actor);
        Assert.Equal("req-0001-aaaa", item.RequestCorrelationId);
        Assert.Null(item.ChangedFields);
    }

    [Fact]
    public async Task RequestCorrelationId_IsNull_ForEntriesWrittenBeforeTheFieldExisted()
    {
        await SeedAsync(Entry(Origin));

        var item = (await ListAsync(Query())).Items.Single();

        Assert.Null(item.RequestCorrelationId);
    }

    [Fact]
    public async Task Projection_NeverCarriesAppendedTextBeforeAfterOrSummaries()
    {
        await SeedAsync(Entry(Origin), Entry(Origin.AddMinutes(1), requestCorrelationId: "req-0002-bbbb"));

        var items = (await ListAsync(Query() with { IncludeChangedFields = true })).Items;
        Assert.Equal(2, items.Count);
        var json = JsonSerializer.Serialize(items);

        foreach (var sentinel in new[] { SentinelAppended, SentinelBefore, SentinelAfter, SentinelExcerpt })
        {
            Assert.DoesNotContain(sentinel, json, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task IncludeChangedFields_ReturnsFieldNamesOnly()
    {
        await SeedAsync(Entry(Origin, changedFields: ["disposition", "reason"]));

        var withFields = (await ListAsync(Query() with { IncludeChangedFields = true })).Items.Single();
        var without = (await ListAsync(Query())).Items.Single();

        Assert.Equal(["disposition", "reason"], withFields.ChangedFields);
        Assert.Null(without.ChangedFields);
    }

    [Fact]
    public async Task IncludeChangedFields_IsNull_ForEntriesWithoutDetails()
    {
        var noteEntry = Entry(Origin);
        noteEntry.Remove("details");
        await SeedAsync(noteEntry);

        var item = (await ListAsync(Query() with { IncludeChangedFields = true })).Items.Single();

        Assert.Null(item.ChangedFields);
    }

    [Fact]
    public async Task Bootstrap_CreatesTheEntityIndex()
    {
        var indexes = await (await Audit.Indexes.ListAsync()).ToListAsync();

        var entity = indexes.SingleOrDefault(i => i["name"].AsString == "ix_audit_log_entity");
        Assert.NotNull(entity);
        var keys = entity["key"].AsBsonDocument;
        Assert.Equal(["owner_id", "entity_type", "entity_id", "occurred_at", "_id"], keys.Names);
        Assert.Equal([1, 1, 1, -1, -1], keys.Values.Select(v => v.ToInt32()));
    }

    [Fact]
    public async Task UnreachableStore_ReturnsDatastoreOffline_WithoutThrowing_AndWithoutTheCredential()
    {
        const string secret = "SENTINEL-DB-PASSWORD-91ac";
        var offlineOptions = new MongoOptions
        {
            ConnectionString = $"mongodb://sentinel-db-user:{secret}@127.0.0.1:1/?serverSelectionTimeoutMS=300&connectTimeoutMS=300",
            DatabaseName = "rrtest_audit_offline",
            ReadinessTimeout = TimeSpan.FromSeconds(1)
        };
        using var offlineClient = new MongoClient(offlineOptions.ConnectionString);
        var probe = new MongoReadinessProbe(offlineClient, Options.Create(offlineOptions), NullLogger<MongoReadinessProbe>.Instance);
        var reader = new MongoAuditLogReader(offlineClient, Options.Create(offlineOptions), probe);

        var result = await reader.ListAsync(Query());

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.DatastoreOffline, result.Error!.Value.Code);
        Assert.DoesNotContain(secret, result.Error.Value.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, JsonSerializer.Serialize(result.Error.Value.Metadata ?? new Dictionary<string, object?>()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnOwnerThatWasNeverSet_ReturnsAValidationError_NotAnException()
    {
        var result = await _reader.ListAsync(new ListAuditEntriesQuery(default));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, result.Error!.Value.Code);
    }
}
