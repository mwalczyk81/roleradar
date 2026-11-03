namespace RoleRadar.Tests.Integration;

using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Application.Abstractions;
using RoleRadar.Cli.Commands;
using RoleRadar.Domain.Tracking;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Infrastructure.Mongo.Reconcile;
using Xunit;

/// <summary>
/// Phase 5: criteria gain a stored has_effective_to flag (nullable effectiveTo sorts last in both
/// directions). The real database holds documents written before the flag existed, so these tests seed
/// LEGACY criteria and prove the reconcile backfill, dry-run safety and idempotency. Notes, companies and
/// search runs add no derived fields, so they need no backfill (their class maps read the stored fields).
/// </summary>
public sealed class ReconcileCriteriaTests : IAsyncLifetime
{
    private const string OwnerA = RemainingEntitiesSeed.OwnerA;

    private readonly ODataTestHost _host = new("reconcile_criteria_test_db", RemainingEntitiesSeed.OwnerA, RemainingEntitiesSeed.OwnerB);

    public async ValueTask InitializeAsync()
    {
        await _host.StartDatabaseAsync();
        await RemainingEntitiesSeed.SeedAsync(_host.Database, legacy: true);
        await _host.StartApiAsync();
    }

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    private IMongoCollection<BsonDocument> Criteria =>
        _host.Database.GetCollection<BsonDocument>(TrackingMongoOptions.CriteriaCollectionName);

    private static FilterDefinition<BsonDocument> ById(int n) =>
        Builders<BsonDocument>.Filter.Eq("_id", RemainingEntitiesSeed.Bin(RemainingEntitiesSeed.G(n)));

    [Fact]
    public async Task LegacyCriteria_DryRunWritesNothing_ConfirmBackfills_SecondDryRunReportsZero()
    {
        var reconcile = _host.Services.GetRequiredService<IReadModelReconciliationService>();

        var legacyOpen = await Criteria.Find(ById(201)).FirstAsync();
        Assert.False(legacyOpen.Contains("has_effective_to"));

        var dry = await reconcile.ReconcileCriteriaAsync(dryRun: true);
        Assert.True(dry.IsDryRun);
        Assert.Equal(4, dry.CriteriaExamined);
        Assert.Equal(4, dry.CriteriaUpdated);
        Assert.Equal(0, await Criteria.CountDocumentsAsync(Builders<BsonDocument>.Filter.Exists("has_effective_to")));

        var confirmed = await reconcile.ReconcileCriteriaAsync(dryRun: false);
        Assert.False(confirmed.IsDryRun);
        Assert.Equal(4, confirmed.CriteriaUpdated);

        var open = await Criteria.Find(ById(201)).FirstAsync();
        var bounded = await Criteria.Find(ById(202)).FirstAsync();
        Assert.False(open["has_effective_to"].AsBoolean);
        Assert.True(bounded["has_effective_to"].AsBoolean);

        // Reconcile only writes the derived flag: business fields and timestamps are untouched.
        Assert.Equal("remote only", open["key"].AsString);
        Assert.Equal(legacyOpen["created_at"], open["created_at"]);
        Assert.True(open["effective_to"].IsBsonNull);

        var second = await reconcile.ReconcileCriteriaAsync(dryRun: true);
        Assert.Equal(0, second.CriteriaUpdated);
    }

    [Fact]
    public async Task LegacyCriteria_AfterReconcile_SortByEffectiveTo_PutsOpenEndedLast_InBothDirections()
    {
        await _host.Services.GetRequiredService<IReadModelReconciliationService>().ReconcileCriteriaAsync(dryRun: false);

        Assert.Equal(["Salary floor", "growth stage", "remote only"], await NamesAsync("effectiveTo%20asc"));
        Assert.Equal(["growth stage", "Salary floor", "remote only"], await NamesAsync("effectiveTo%20desc"));
    }

    [Fact]
    public async Task CriterionUpsert_WritesHasEffectiveTo_AtTheRepositoryChokePoint()
    {
        using var scope = _host.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ICriterionRepository>();

        var open = Criterion.Create(OwnerA, "fresh open", CriterionCategory.Preference, "v", ProvenanceKind.Stated, new DateTimeOffset(RemainingEntitiesSeed.Now));
        Assert.True((await repo.UpsertCriterionAsync(open)).IsSuccess);
        var superseded = Criterion.Create(OwnerA, "fresh bounded", CriterionCategory.Weight, "v", ProvenanceKind.Stated, new DateTimeOffset(RemainingEntitiesSeed.Now));
        superseded.Deactivate(new DateTimeOffset(RemainingEntitiesSeed.Now.AddDays(30)));
        Assert.True((await repo.UpsertCriterionAsync(superseded)).IsSuccess);

        var storedOpen = await Criteria.Find(Builders<BsonDocument>.Filter.Eq("_id", RemainingEntitiesSeed.Bin(open.Id))).FirstAsync();
        var storedBounded = await Criteria.Find(Builders<BsonDocument>.Filter.Eq("_id", RemainingEntitiesSeed.Bin(superseded.Id))).FirstAsync();
        Assert.False(storedOpen["has_effective_to"].AsBoolean);
        Assert.True(storedBounded["has_effective_to"].AsBoolean);
    }

    [Fact]
    public async Task CliSummary_ReportsCriteriaCounts_ForDryRunAndConfirm()
    {
        // The real container resolves the service the command asks for (see CliServiceWiringArchitectureTests).
        var command = ReconcileReadModelsCommand.Create(_host.Services);

        async Task<string> RunAsync(params string[] args)
        {
            var original = Console.Out;
            using var captured = new StringWriter();
            Console.SetOut(captured);
            try
            {
                var exit = await command.Parse(args).InvokeAsync();
                Assert.Equal(0, exit);
            }
            finally
            {
                Console.SetOut(original);
            }

            return captured.ToString();
        }

        var dry = await RunAsync();
        Assert.Contains("DRY RUN", dry);
        Assert.Contains("Criteria Examined: 4", dry);
        Assert.Contains("Criteria Updated: 4", dry);
        Assert.Equal(0, await Criteria.CountDocumentsAsync(Builders<BsonDocument>.Filter.Exists("has_effective_to")));

        var confirmed = await RunAsync("--confirm", ReconcileReadModelsCommand.ConfirmationToken);
        Assert.Contains("COMMITTED", confirmed);
        Assert.Contains("Criteria Updated: 4", confirmed);

        var again = await RunAsync();
        Assert.Contains("Criteria Updated: 0", again);
    }

    private async Task<List<string>> NamesAsync(string orderBy)
    {
        var (status, body) = await _host.GetJsonAsync(OwnerA, $"/odata/Criteria?$orderby={orderBy}");
        Assert.Equal(HttpStatusCode.OK, status);
        return body.RootElement.GetProperty("value").EnumerateArray().Select(e => e.GetProperty("name").GetString()!).ToList();
    }
}
