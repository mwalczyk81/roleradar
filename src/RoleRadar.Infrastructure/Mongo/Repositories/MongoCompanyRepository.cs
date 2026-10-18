using MongoDB.Driver;
using RoleRadar.Application.Abstractions;
using RoleRadar.Domain.Tracking;

namespace RoleRadar.Infrastructure.Mongo.Repositories;

public sealed class MongoCompanyRepository : ICompanyRepository
{
    private readonly IMongoClient _client;
    private readonly IMongoDatabase _database;

    public MongoCompanyRepository(IMongoClient client, IMongoDatabase database)
    {
        _client = client;
        _database = database;
    }

    public Task<Result<Company?>> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result<Company?>.Success(null));
    }

    public Task<Result<Company?>> GetByNormalizedNameAsync(string ownerId, string normalizedName, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result<Company?>.Success(null));
    }

    public Task<Result<IReadOnlyList<Company>>> ListAsync(string ownerId, CompanyStatus? status = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result<IReadOnlyList<Company>>.Success([]));
    }

    public Task<Result<IReadOnlyList<CareerSource>>> GetCareerSourcesAsync(string ownerId, Guid companyId, bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result<IReadOnlyList<CareerSource>>.Success([]));
    }

    public Task<Result<CareerSource?>> GetCareerSourceByCompositeKeyAsync(string ownerId, Guid companyId, string atsType, string? tenant, string? slug, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result<CareerSource?>.Success(null));
    }

    public Task<Result<bool>> UpsertCompanyAsync(Company company, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result<bool>.Success(false));
    }

    public Task<Result<bool>> UpsertCareerSourceAsync(CareerSource careerSource, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result<bool>.Success(false));
    }

    public Task<Result<TResponse>> ExecuteInTransactionAsync<TResponse>(
        Func<ITrackingTransactionContext, CancellationToken, Task<Result<TResponse>>> work,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result<TResponse>.Failure(ErrorCode.ValidationError, "Stub repository"));
    }
}
