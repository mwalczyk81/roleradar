namespace RoleRadar.Tests.Integration;

using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Infrastructure.Mongo;
using Xunit;

/// <summary>
/// One container and API host holding fictional data for all six OData entity sets and two owners
/// (plus a third that owns nothing). Shared by the Phase 6 guardrail, envelope and isolation tests.
/// </summary>
public sealed class AllEntitiesFixture : IAsyncLifetime
{
    public const string OwnerA = RemainingEntitiesSeed.OwnerA;
    public const string OwnerB = RemainingEntitiesSeed.OwnerB;
    public const string OwnerC = RemainingEntitiesSeed.OwnerC;

    internal ODataTestHost Host { get; } = new("odata_all_entities_test_db", OwnerA, OwnerB, OwnerC);

    public async ValueTask InitializeAsync()
    {
        await Host.StartDatabaseAsync();
        await RemainingEntitiesSeed.SeedAsync(Host.Database, legacy: false);
        await SeedPostingsAsync(Host.Database);
        await SeedApplicationsAsync(Host.Database);
        await Host.StartApiAsync();
    }

    public async ValueTask DisposeAsync() => await Host.DisposeAsync();

    private static BsonBinaryData Bin(int n) => RemainingEntitiesSeed.Bin(RemainingEntitiesSeed.G(n));

    private static async Task SeedPostingsAsync(IMongoDatabase db)
    {
        var now = RemainingEntitiesSeed.Now;
        BsonDocument Posting(int n, string owner, string company, string title, string disposition, int rank, int? score) => new()
        {
            ["_id"] = Bin(400 + n),
            ["owner_id"] = owner,
            ["company_id"] = Bin(500 + n),
            ["company_name"] = company,
            ["title"] = title,
            ["disposition"] = disposition,
            ["disposition_rank"] = rank,
            ["score"] = score is null ? BsonNull.Value : score.Value,
            ["has_score"] = score is not null,
            ["created_at"] = new BsonDateTime(now.AddDays(-n)),
            ["updated_at"] = new BsonDateTime(now.AddDays(-n))
        };

        await db.GetCollection<BsonDocument>(TrackingMongoOptions.PostingsCollectionName).InsertManyAsync(
        [
            Posting(1, AllEntitiesFixture.OwnerA, "Acme Robotics", "Platform Engineer", "lead", 1, 80),
            Posting(2, AllEntitiesFixture.OwnerA, "beta Systems", "Staff Engineer", "applied", 3, null),
            Posting(3, AllEntitiesFixture.OwnerA, "zeta labs", "Data Engineer", "lead", 1, 60),
            Posting(4, AllEntitiesFixture.OwnerB, "Omega Secret Co", "Owner B Posting", "lead", 1, 99)
        ]);
    }

    private static async Task SeedApplicationsAsync(IMongoDatabase db)
    {
        var now = RemainingEntitiesSeed.Now;
        BsonDocument App(int n, string owner, string company, string title, DateTime? firstResponse) => new()
        {
            ["_id"] = Bin(600 + n),
            ["owner_id"] = owner,
            ["posting_id"] = Bin(400 + n),
            ["company_id"] = Bin(500 + n),
            ["posting_title"] = title,
            ["company_name"] = company,
            ["status"] = "applied",
            ["applied_at"] = new BsonDateTime(now.AddDays(-10 - n)),
            ["has_interview"] = false,
            ["first_response_at"] = firstResponse is null ? BsonNull.Value : new BsonDateTime(firstResponse.Value),
            ["last_event_at"] = new BsonDateTime(now.AddDays(-n)),
            ["has_first_response"] = firstResponse is not null,
            ["has_last_event"] = true
        };

        await db.GetCollection<BsonDocument>(TrackingMongoOptions.ApplicationsCollectionName).InsertManyAsync(
        [
            App(1, AllEntitiesFixture.OwnerA, "Acme Robotics", "Platform Engineer", now.AddDays(-5)),
            App(2, AllEntitiesFixture.OwnerA, "beta Systems", "Staff Engineer", null),
            App(3, AllEntitiesFixture.OwnerA, "zeta labs", "Data Engineer", now.AddDays(-2)),
            App(4, AllEntitiesFixture.OwnerB, "Omega Secret Co", "Owner B Application", null)
        ]);
    }
}
