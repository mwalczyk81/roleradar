namespace RoleRadar.Tests.Integration;

using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Infrastructure.Mongo;
using Xunit;

/// <summary>
/// Fictional seed data for the Phase 5 entity sets, written in the shape the repositories really store
/// (no read-model-only fields such as linked_entity_*). With <c>legacy: true</c> criteria also omit the
/// <c>has_effective_to</c> flag, exactly like documents written before Phase 5.
/// </summary>
internal static class RemainingEntitiesSeed
{
    public const string OwnerA = "remaining-owner-aaa";
    public const string OwnerB = "remaining-owner-bbb";
    public const string OwnerC = "remaining-owner-ccc"; // owns nothing

    public static readonly DateTime Now = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    public static Guid G(int n) => Guid.Parse($"{n:D8}-0000-0000-0000-000000000000");

    public static BsonBinaryData Bin(Guid g) => new(g, GuidRepresentation.Standard);

    public static async Task SeedAsync(IMongoDatabase db, bool legacy)
    {
        await db.GetCollection<BsonDocument>(TrackingMongoOptions.CompaniesCollectionName).InsertManyAsync(
        [
            Company(1, OwnerA, "zeta labs", "active", "private", "Austin, TX"),
            Company(2, OwnerA, "Acme Robotics", "target", "public", "Denver, CO"),
            Company(3, OwnerA, "beta Systems", "active", "subsidiary", null),
            Company(4, OwnerB, "Omega Secret Co", "active", "private", "Remote")
        ]);

        await db.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.NotesCollectionName).InsertManyAsync(
        [
            Note(1, OwnerA, "Older interview prep", "area-a", Now.AddDays(-30), Now.AddDays(-20), "secret body one"),
            Note(2, OwnerA, "newest strategy memo", "area-b", Now.AddDays(-5), Now.AddDays(-1), "secret body two"),
            Note(3, OwnerA, "Middle notes", "area-a", Now.AddDays(-10), Now.AddDays(-10), "secret body three"),
            Note(4, OwnerB, "Owner B private note", "area-a", Now.AddDays(-1), Now.AddDays(-1), "owner b body")
        ]);

        await db.GetCollection<BsonDocument>(TrackingMongoOptions.CriteriaCollectionName).InsertManyAsync(
        [
            // Open-ended criterion: effective_to is an explicit null, exactly as SerializeCriterion wrote it.
            Criterion(1, legacy, OwnerA, "remote only", "hard_skip", "Must be fully remote", Now.AddDays(-100), null, true),
            Criterion(2, legacy, OwnerA, "Salary floor", "weight", "At least 180k", Now.AddDays(-200), Now.AddDays(-50), false),
            Criterion(3, legacy, OwnerA, "growth stage", "preference", "Series C or later", Now.AddDays(-20), Now.AddDays(40), true),
            Criterion(4, legacy, OwnerB, "owner b criterion", "weight", "private", Now.AddDays(-5), null, true)
        ]);

        await db.GetCollection<BsonDocument>(TrackingMongoOptions.SearchRunsCollectionName).InsertManyAsync(
        [
            Run(1, OwnerA, Now.AddDays(-7), 3, "weekly sweep"),
            Run(2, OwnerA, Now.AddDays(-1), 0, null),
            Run(3, OwnerA, Now.AddDays(-14), 12, "first big run"),
            Run(4, OwnerB, Now.AddDays(-2), 9, "owner b run")
        ]);
    }

    private static BsonDocument Company(int n, string owner, string name, string status, string ownership, string? hq) => new()
    {
        ["_id"] = Bin(G(n)),
        ["owner_id"] = owner,
        ["name"] = name,
        ["normalized_name"] = name.ToLowerInvariant(),
        ["description"] = BsonNull.Value,
        ["parent_company"] = BsonNull.Value,
        ["ownership_type"] = ownership,
        ["headquarters"] = hq is null ? BsonNull.Value : hq,
        ["category_tags"] = new BsonArray(["fintech"]),
        ["status"] = status,
        ["version_token"] = Bin(Guid.NewGuid()),
        ["created_at"] = new BsonDateTime(Now.AddDays(-60)),
        ["updated_at"] = new BsonDateTime(Now.AddDays(-60))
    };

    private static BsonDocument Note(int n, string owner, string title, string area, DateTime created, DateTime updated, string body) => new()
    {
        ["_id"] = Bin(G(100 + n)),
        ["owner_id"] = owner,
        ["title"] = title,
        ["body"] = body,
        ["current_body_hash"] = "hash",
        ["tags"] = new BsonArray(["t1"]),
        ["area"] = area,
        ["status"] = "active",
        ["version_token"] = Bin(Guid.NewGuid()),
        ["revision_count"] = 0,
        ["created_at"] = new BsonDateTime(created),
        ["updated_at"] = new BsonDateTime(updated),
        ["company_id"] = BsonNull.Value,
        ["posting_id"] = BsonNull.Value,
        ["application_id"] = BsonNull.Value
    };

    private static BsonDocument Criterion(int n, bool legacy, string owner, string key, string category, string value, DateTime from, DateTime? to, bool active)
    {
        var doc = new BsonDocument
        {
            ["_id"] = Bin(G(200 + n)),
            ["owner_id"] = owner,
            ["key"] = key,
            ["category"] = category,
            ["value"] = value,
            ["effective_from"] = new BsonDateTime(from),
            ["effective_to"] = to is null ? BsonNull.Value : new BsonDateTime(to.Value),
            ["provenance"] = "stated",
            ["is_active"] = active,
            ["version_token"] = Bin(Guid.NewGuid()),
            ["created_at"] = new BsonDateTime(from)
        };

        if (!legacy)
        {
            doc["has_effective_to"] = to is not null;
        }

        return doc;
    }

    private static BsonDocument Run(int n, string owner, DateTime started, int newPostings, string? notes) => new()
    {
        ["_id"] = Bin(G(300 + n)),
        ["owner_id"] = owner,
        ["started_at"] = new BsonDateTime(started),
        ["new_postings_count"] = newPostings,
        ["report_note_id"] = BsonNull.Value,
        ["notes"] = notes is null ? BsonNull.Value : notes,
        ["created_at"] = new BsonDateTime(started)
    };
}

/// <summary>One container and API host shared by every read-only query test in <see cref="ODataRemainingEntitiesTests"/>.</summary>
public sealed class RemainingEntitiesFixture : IAsyncLifetime
{
    internal ODataTestHost Host { get; } = new("odata_remaining_test_db", RemainingEntitiesSeed.OwnerA, RemainingEntitiesSeed.OwnerB, RemainingEntitiesSeed.OwnerC);

    public async ValueTask InitializeAsync()
    {
        await Host.StartDatabaseAsync();
        await RemainingEntitiesSeed.SeedAsync(Host.Database, legacy: false);
        await Host.StartApiAsync();
    }

    public async ValueTask DisposeAsync() => await Host.DisposeAsync();
}
