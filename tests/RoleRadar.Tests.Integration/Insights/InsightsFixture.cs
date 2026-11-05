namespace RoleRadar.Tests.Integration.Insights;

using MongoDB.Driver;
using Xunit;

/// <summary>
/// One isolated Testcontainers MongoDB replica set, seeded with the fictional <see cref="InsightsSeed"/> through the
/// real repositories, with the real API host started over it. The seed guard runs before the first write.
/// Nothing here reads configuration, user secrets or environment connection strings.
/// </summary>
public abstract class InsightsFixtureBase : IAsyncLifetime
{
    private ODataTestHost? _host;

    protected InsightsFixtureBase(DateOnly asOf) => AsOf = asOf;

    internal ODataTestHost Host => _host ?? throw new InvalidOperationException("Fixture not initialized.");

    public IMongoDatabase Database => Host.Database;

    public DateOnly AsOf { get; }

    public string DatabaseName { get; } = InsightsSeedGuard.NewDatabaseName();

    public async ValueTask InitializeAsync()
    {
        _host = new ODataTestHost(DatabaseName, InsightsSeed.AllOwners);
        await _host.StartDatabaseAsync();

        InsightsSeedGuard.AssertIsolated(_host.ContainerConnectionString, DatabaseName, _host.ContainerHost, _host.ContainerPort);

        await InsightsSeed.WriteAsync(_host.Database.Client, _host.Database, AsOf);
        await _host.StartApiAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }
}

/// <summary>The seed built relative to the fixed fictional as-of date used by the committed snapshots.</summary>
public sealed class InsightsFixture : InsightsFixtureBase
{
    public InsightsFixture() : base(InsightsSeed.FixedAsOf)
    {
    }
}

/// <summary>
/// The same seed built relative to today (UTC), for tests that compare lens filters with the live <c>ageInDays</c>
/// the API computes from the real clock.
/// </summary>
public sealed class InsightsTodayFixture : InsightsFixtureBase
{
    public InsightsTodayFixture() : base(DateOnly.FromDateTime(DateTime.UtcNow))
    {
    }
}
