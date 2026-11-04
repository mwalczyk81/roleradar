namespace RoleRadar.Tests.Integration.Insights;

using MongoDB.Driver;
using Xunit;

/// <summary>
/// One isolated Testcontainers MongoDB replica set, seeded with the fictional <see cref="InsightsSeed"/> through the
/// real repositories, with the real API host started over it. The seed guard runs before the first write.
/// Nothing here reads configuration, user secrets or environment connection strings.
/// </summary>
public sealed class InsightsFixture : IAsyncLifetime
{
    private ODataTestHost? _host;

    internal ODataTestHost Host => _host ?? throw new InvalidOperationException("Fixture not initialized.");

    public IMongoDatabase Database => Host.Database;

    public DateOnly AsOf { get; } = InsightsSeed.FixedAsOf;

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
    }
}
