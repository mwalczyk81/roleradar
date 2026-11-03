namespace RoleRadar.Infrastructure.Mongo.Reading;

using Microsoft.Extensions.Options;
using MongoDB.Driver;

/// <summary>
/// Named seam that makes sure the read-path indexes (nulls-last compounds, collation sort indexes) exist before the
/// first OData read. It delegates to <see cref="TrackingIndexInitializer.EnsureIndexesOnceAsync"/>, which is idempotent
/// and gated once per database per process. Registered with the infrastructure services but only invoked by the API
/// host's startup service, so the CLI and MCP hosts never run it.
/// </summary>
public sealed class ReadIndexInitializer
{
    private readonly IMongoClient _client;
    private readonly IOptions<MongoOptions> _options;

    public ReadIndexInitializer(IMongoClient client, IOptions<MongoOptions> options)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public Task EnsureAsync(CancellationToken cancellationToken)
    {
        var database = _client.GetDatabase(_options.Value.DatabaseName);
        return TrackingIndexInitializer.EnsureIndexesOnceAsync(database, cancellationToken);
    }
}
