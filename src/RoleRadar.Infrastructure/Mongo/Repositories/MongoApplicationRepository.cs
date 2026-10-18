using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Application.Abstractions;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Tracking;
using ApplicationEntity = RoleRadar.Domain.Tracking.Application;

namespace RoleRadar.Infrastructure.Mongo.Repositories;

public sealed class MongoApplicationRepository : IApplicationRepository
{
    private readonly IMongoClient _client;
    private readonly IMongoDatabase _database;
    private readonly IMongoCollection<BsonDocument> _applications;
    private readonly IMongoCollection<BsonDocument> _applicationEvents;

    public MongoApplicationRepository(IMongoClient client, IMongoDatabase database)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _applications = _database.GetCollection<BsonDocument>(TrackingMongoOptions.ApplicationsCollectionName);
        _applicationEvents = _database.GetCollection<BsonDocument>(TrackingMongoOptions.ApplicationEventsCollectionName);
    }

    public Task<Result<ApplicationEntity?>> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result<ApplicationEntity?>.Success(null));
    }

    public Task<Result<ApplicationEntity?>> GetByPostingIdAsync(string ownerId, Guid postingId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result<ApplicationEntity?>.Success(null));
    }

    public Task<Result<bool>> UpsertApplicationAsync(ApplicationEntity application, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result<bool>.Success(true));
    }

    public Task<Result<bool>> AppendEventAsync(ApplicationEvent appEvent, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result<bool>.Success(true));
    }

    public Task<Result<IReadOnlyList<ApplicationEvent>>> GetEventsAsync(string ownerId, Guid applicationId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result<IReadOnlyList<ApplicationEvent>>.Success([]));
    }

    public Task<Result<IReadOnlyList<ApplicationEntity>>> ListApplicationsAsync(string ownerId, string? outcome = null, int page = 1, int pageSize = 50, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result<IReadOnlyList<ApplicationEntity>>.Success([]));
    }

    public Task<Result<int>> CountApplicationsAsync(string ownerId, string? outcome = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result<int>.Success(0));
    }
}
