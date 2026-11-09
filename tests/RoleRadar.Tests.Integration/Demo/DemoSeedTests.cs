namespace RoleRadar.Tests.Integration.Demo;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Application.Abstractions;
using RoleRadar.Cli.Demo;
using RoleRadar.Domain.Common;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Infrastructure.Mongo.Reconcile;
using Xunit;

/// <summary>
/// The demo seed against a throwaway Testcontainers replica set (never a developer's Mongo). The explicit-host guard
/// overload is used only here and in the guard tests; production callers pass the fixed demo host.
/// </summary>
public sealed class DemoSeedTests : IAsyncLifetime
{
    private ODataTestHost _host = null!;
    private ServiceProvider _services = null!;

    public async ValueTask InitializeAsync()
    {
        // The guard demands the demo database name; inside this throwaway container that is safe.
        _host = new ODataTestHost(DemoModeGuard.DemoDatabaseName, "local-owner");
        await _host.StartDatabaseAsync();

        var connection = _host.ContainerConnectionString;
        _services = RoleRadar.Cli.Program.BuildServiceProvider(s => s.Configure<MongoOptions>(o =>
        {
            o.ConnectionString = connection;
            o.DatabaseName = DemoModeGuard.DemoDatabaseName;
        }));
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task Seed_CreatesTheDocumentedDataset_WithVersion7Ids()
    {
        var report = await new DemoSeeder(_services).SeedAsync(new DateOnly(2026, 9, 30), TestContext.Current.CancellationToken);

        Assert.False(report.AlreadySeeded);
        Assert.Equal(DemoDataset.Companies.Count, report.Companies);
        Assert.Equal(DemoDataset.Postings.Count, report.Postings);
        Assert.Equal(20, report.Applications);
        Assert.Equal(DemoDataset.Notes.Count, report.Notes);
        Assert.Equal(DemoDataset.SearchRuns.Count, report.SearchRuns);

        var db = _host.Database;
        Assert.Equal(14, await Count(db, TrackingMongoOptions.CompaniesCollectionName));
        Assert.Equal(48, await Count(db, TrackingMongoOptions.PostingsCollectionName));
        Assert.Equal(20, await Count(db, TrackingMongoOptions.ApplicationsCollectionName));
        Assert.Equal(8, await Count(db, TrackingMongoOptions.SearchRunsCollectionName));
        Assert.Equal(6, await Count(db, MongoBootstrapAndIndexes.NotesCollectionName));
        Assert.True(await Count(db, TrackingMongoOptions.ApplicationEventsCollectionName) >= 40);

        Assert.Equal(DemoDataset.Postings.Sum(p => p.Signals.Count), report.PostingSignals);
        Assert.Equal(1, report.CareerSources);
        Assert.Equal(report.PostingSignals, await Count(db, TrackingMongoOptions.PostingSignalsCollectionName));
        Assert.Equal(1, await Count(db, TrackingMongoOptions.CareerSourcesCollectionName));

        foreach (var collection in new[]
                 {
                     TrackingMongoOptions.CompaniesCollectionName, TrackingMongoOptions.PostingsCollectionName,
                     TrackingMongoOptions.ApplicationsCollectionName, TrackingMongoOptions.SearchRunsCollectionName,
                     TrackingMongoOptions.ApplicationEventsCollectionName, MongoBootstrapAndIndexes.NotesCollectionName,
                     TrackingMongoOptions.PostingSignalsCollectionName, TrackingMongoOptions.CareerSourcesCollectionName
                 })
        {
            await AssertAllIdsAreVersion7(db, collection);
        }
    }

    [Fact]
    public async Task Seed_EnrichesTheHeroRecords_ThroughTheRealRepositories()
    {
        await new DemoSeeder(_services).SeedAsync(new DateOnly(2026, 9, 30), TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var owner = OwnerId.Default.Value;
        var companies = _services.GetRequiredService<ICompanyRepository>();
        var postings = _services.GetRequiredService<IPostingRepository>();
        var notes = _services.GetRequiredService<INoteRepository>();

        var company = Assert.Single((await companies.ListAsync(owner, cancellationToken: ct)).Value!, c => c.Name == DemoDataset.HeroCompany);
        var expectedCompany = DemoDataset.Companies.Single(c => c.Name == DemoDataset.HeroCompany);
        Assert.Equal(expectedCompany.CategoryTags, company.CategoryTags);
        var source = Assert.Single((await companies.GetCareerSourcesAsync(owner, company.Id, cancellationToken: ct)).Value!);
        Assert.Equal(expectedCompany.CareerSourceUrl, source.Slug);

        var expected = DemoDataset.Postings.Single(p => p.Slug == DemoDataset.HeroPostingSlug);
        var posting = Assert.Single(
            (await postings.ListPostingsAsync(owner, company.Id, pageSize: 100, cancellationToken: ct)).Value!,
            p => p.Title == expected.Title);
        Assert.Equal(expected.Compensation, posting.Compensation);

        var noteId = Assert.IsType<Guid>(posting.ScreeningNoteId);
        var note = (await notes.GetByIdAsync(owner, noteId, ct)).Value;
        Assert.NotNull(note);
        Assert.Equal(expected.AssessmentNoteTitle, note.Title);

        var signals = (await postings.GetSignalsAsync(owner, posting.Id, ct)).Value!;
        Assert.Equal(expected.Signals.Select(s => s.Type).Order(), signals.Select(s => s.SignalType).Order());
    }

    [Fact]
    public async Task SecondRun_ReportsAlreadySeeded_AndChangesNothing_EvenWithAnotherAsOfDate()
    {
        var seeder = new DemoSeeder(_services);
        await seeder.SeedAsync(new DateOnly(2026, 9, 30), TestContext.Current.CancellationToken);
        var before = await Snapshot(_host.Database);

        var again = await seeder.SeedAsync(new DateOnly(2026, 9, 30), TestContext.Current.CancellationToken);
        var shifted = await seeder.SeedAsync(new DateOnly(2026, 10, 15), TestContext.Current.CancellationToken);

        Assert.True(again.AlreadySeeded);
        Assert.True(shifted.AlreadySeeded);
        Assert.Equal(0, again.Companies + again.Postings + again.Applications + again.Notes + again.SearchRuns + again.PostingSignals + again.CareerSources);
        Assert.Equal(0, shifted.PostingSignals + shifted.CareerSources);
        Assert.Equal(before, await Snapshot(_host.Database));
    }

    [Fact]
    public async Task Seed_WritesDerivedFieldsAtTheChokePoint_SoReconcileFindsNothingToChange()
    {
        await new DemoSeeder(_services).SeedAsync(new DateOnly(2026, 9, 30), TestContext.Current.CancellationToken);

        var service = new ReadModelReconciliationService(_host.Database);
        var postings = await service.ReconcilePostingsAsync(dryRun: true, TestContext.Current.CancellationToken);
        var applications = await service.ReconcileApplicationsAsync(dryRun: true, TestContext.Current.CancellationToken);

        Assert.Equal(48, postings.PostingsExamined);
        Assert.Equal(0, postings.PostingsUpdated);
        Assert.Equal(20, applications.ApplicationsExamined);
        Assert.Equal(0, applications.ApplicationsUpdated);
    }

    [Fact]
    public async Task SeedCommand_WithTheProductionGuard_RefusesAThrowawayEndpointBeforeAnyWrite()
    {
        // The production guard supplies the fixed demo host, so a container endpoint is refused: nothing is written.
        var output = new StringWriter();
        var error = new StringWriter();
        var host = new DemoCommandHost(
            name => name == DemoModeGuard.DemoModeVariable ? "1" : null,
            () => true,
            output,
            error);

        var exit = await DemoCommand.Create(_services, host).Parse("seed").InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, exit);
        Assert.Equal(0, await Count(_host.Database, TrackingMongoOptions.PostingsCollectionName));
        Assert.DoesNotContain("mongodb://", error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SeedCommand_WithTheExplicitHostGuard_SeedsAndReportsCountsOnly()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var options = _services.GetRequiredService<IOptions<MongoOptions>>().Value;
        var containerHost = new MongoUrl(options.ConnectionString).Servers.Single().Host;
        var host = new DemoCommandHost(
            name => name == DemoModeGuard.DemoModeVariable ? "1" : null,
            () => true,
            output,
            error,
            Guard: (mode, database, connection) => DemoModeGuard.Check(mode, database, connection, containerHost));

        var exit = await DemoCommand.Create(_services, host).Parse("seed").InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, exit);
        Assert.Contains("48", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(48, await Count(_host.Database, TrackingMongoOptions.PostingsCollectionName));
        Assert.DoesNotContain("password", output.ToString() + error, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<long> Count(IMongoDatabase db, string collection) =>
        await db.GetCollection<BsonDocument>(collection).CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty, cancellationToken: TestContext.Current.CancellationToken);

    private static async Task AssertAllIdsAreVersion7(IMongoDatabase db, string collection)
    {
        var docs = await db.GetCollection<BsonDocument>(collection).Find(FilterDefinition<BsonDocument>.Empty).ToListAsync(TestContext.Current.CancellationToken);
        Assert.NotEmpty(docs);
        foreach (var doc in docs)
        {
            var id = doc["_id"];
            var guid = id.IsGuid ? id.AsGuid : Guid.Parse(id.AsString);
            Assert.Equal(7, guid.Version);
        }
    }

    private static async Task<string> Snapshot(IMongoDatabase db)
    {
        var parts = new List<string>();
        foreach (var name in await (await db.ListCollectionNamesAsync(cancellationToken: TestContext.Current.CancellationToken)).ToListAsync(TestContext.Current.CancellationToken))
        {
            if (name.StartsWith("system.", StringComparison.Ordinal))
            {
                continue;
            }

            var docs = await db.GetCollection<BsonDocument>(name).Find(FilterDefinition<BsonDocument>.Empty).ToListAsync(TestContext.Current.CancellationToken);
            parts.Add($"{name}:{docs.Count}:{string.Join(',', docs.Select(d => d["_id"].ToString()).OrderBy(x => x, StringComparer.Ordinal))}");
        }

        return string.Join('|', parts.OrderBy(x => x, StringComparer.Ordinal));
    }
}
