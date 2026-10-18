using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Application.Abstractions;
using RoleRadar.Domain.Tracking;

namespace RoleRadar.Infrastructure.Mongo.Repositories;

public sealed class MongoSearchRunRepository : ISearchRunRepository
{
    private readonly IMongoClient _client;
    private readonly IMongoDatabase _database;

    public MongoSearchRunRepository(IMongoClient client, IMongoDatabase database)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public Task<Result<SearchRun?>> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<Result<SearchRun?>> GetByStartedAtAsync(string ownerId, DateTimeOffset startedAt, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<Result<bool>> UpsertSearchRunAsync(SearchRun searchRun, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<Result<IReadOnlyList<SearchRun>>> ListSearchRunsAsync(string ownerId, int page = 1, int pageSize = 50, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<Result<int>> CountSearchRunsAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
