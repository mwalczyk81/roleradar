using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Application.Abstractions;
using RoleRadar.Domain.Audit;
using RoleRadar.Domain.Tracking;

namespace RoleRadar.Infrastructure.Mongo.Repositories;

public sealed class MongoCompanyRepository : ICompanyRepository
{
    private readonly IMongoClient _client;
    private readonly IMongoDatabase _database;
    private readonly IMongoCollection<BsonDocument> _companies;
    private readonly IMongoCollection<BsonDocument> _careerSources;
    private readonly IMongoCollection<BsonDocument> _auditLog;

    public MongoCompanyRepository(IMongoClient client, IMongoDatabase database)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _companies = database.GetCollection<BsonDocument>(TrackingMongoOptions.CompaniesCollectionName);
        _careerSources = database.GetCollection<BsonDocument>(TrackingMongoOptions.CareerSourcesCollectionName);
        _auditLog = database.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.AuditLogCollectionName);
    }

    public async Task<Result<Company?>> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await TrackingIndexInitializer.EnsureIndexesOnceAsync(_database, cancellationToken);
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("owner_id", ownerId),
            Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(id, GuidRepresentation.Standard)));

        var session = MongoNoteRepository.CurrentSession;
        var doc = session is not null
            ? await _companies.Find(session, filter).FirstOrDefaultAsync(cancellationToken)
            : await _companies.Find(filter).FirstOrDefaultAsync(cancellationToken);
        return Result<Company?>.Success(doc is null ? null : MapCompany(doc));
    }

    public async Task<Result<Company?>> GetByNormalizedNameAsync(string ownerId, string normalizedName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedName);
        await TrackingIndexInitializer.EnsureIndexesOnceAsync(_database, cancellationToken);

        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("owner_id", ownerId),
            Builders<BsonDocument>.Filter.Eq("normalized_name", normalizedName.Trim().ToLowerInvariant()));

        var session = MongoNoteRepository.CurrentSession;
        var doc = session is not null
            ? await _companies.Find(session, filter).FirstOrDefaultAsync(cancellationToken)
            : await _companies.Find(filter).FirstOrDefaultAsync(cancellationToken);
        return Result<Company?>.Success(doc is null ? null : MapCompany(doc));
    }

    public async Task<Result<IReadOnlyList<Company>>> ListAsync(string ownerId, CompanyStatus? status = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await TrackingIndexInitializer.EnsureIndexesOnceAsync(_database, cancellationToken);
        var filter = Builders<BsonDocument>.Filter.Eq("owner_id", ownerId);
        if (status.HasValue)
        {
            filter = Builders<BsonDocument>.Filter.And(
                filter,
                Builders<BsonDocument>.Filter.Eq("status", status.Value.ToString().ToLowerInvariant()));
        }

        var session = MongoNoteRepository.CurrentSession;
        var docs = session is not null
            ? await _companies.Find(session, filter).ToListAsync(cancellationToken)
            : await _companies.Find(filter).ToListAsync(cancellationToken);
        var list = docs.Select(MapCompany).ToArray();
        return Result<IReadOnlyList<Company>>.Success(list);
    }

    public async Task<Result<IReadOnlyList<CareerSource>>> GetCareerSourcesAsync(string ownerId, Guid companyId, bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await TrackingIndexInitializer.EnsureIndexesOnceAsync(_database, cancellationToken);
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("owner_id", ownerId),
            Builders<BsonDocument>.Filter.Eq("company_id", new BsonBinaryData(companyId, GuidRepresentation.Standard)));

        if (!includeInactive)
        {
            filter = Builders<BsonDocument>.Filter.And(
                filter,
                Builders<BsonDocument>.Filter.Eq("is_active", true));
        }

        var session = MongoNoteRepository.CurrentSession;
        var docs = session is not null
            ? await _careerSources.Find(session, filter).ToListAsync(cancellationToken)
            : await _careerSources.Find(filter).ToListAsync(cancellationToken);
        var list = docs.Select(MapCareerSource).ToArray();
        return Result<IReadOnlyList<CareerSource>>.Success(list);
    }

    public async Task<Result<CareerSource?>> GetCareerSourceByCompositeKeyAsync(string ownerId, Guid companyId, string atsType, string? tenant, string? slug, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(atsType);
        await TrackingIndexInitializer.EnsureIndexesOnceAsync(_database, cancellationToken);

        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("owner_id", ownerId),
            Builders<BsonDocument>.Filter.Eq("company_id", new BsonBinaryData(companyId, GuidRepresentation.Standard)),
            Builders<BsonDocument>.Filter.Eq("ats_type", atsType.Trim()),
            Builders<BsonDocument>.Filter.Eq("tenant", tenant?.Trim()),
            Builders<BsonDocument>.Filter.Eq("slug", slug?.Trim()));

        var session = MongoNoteRepository.CurrentSession;
        var doc = session is not null
            ? await _careerSources.Find(session, filter).FirstOrDefaultAsync(cancellationToken)
            : await _careerSources.Find(filter).FirstOrDefaultAsync(cancellationToken);
        return Result<CareerSource?>.Success(doc is null ? null : MapCareerSource(doc));
    }

    public async Task<Result<bool>> UpsertCompanyAsync(Company company, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(company);
        await TrackingIndexInitializer.EnsureIndexesOnceAsync(_database, cancellationToken);

        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("owner_id", company.OwnerId),
            Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(company.Id, GuidRepresentation.Standard)));

        var doc = SerializeCompany(company);
        var session = MongoNoteRepository.CurrentSession;
        var result = session is not null
            ? await _companies.ReplaceOneAsync(session, filter, doc, new ReplaceOptions { IsUpsert = true }, cancellationToken)
            : await _companies.ReplaceOneAsync(filter, doc, new ReplaceOptions { IsUpsert = true }, cancellationToken);
        return Result<bool>.Success(result.IsAcknowledged);
    }

    public async Task<Result<bool>> UpsertCareerSourceAsync(CareerSource careerSource, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(careerSource);
        await TrackingIndexInitializer.EnsureIndexesOnceAsync(_database, cancellationToken);

        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("owner_id", careerSource.OwnerId),
            Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(careerSource.Id, GuidRepresentation.Standard)));

        var doc = SerializeCareerSource(careerSource);
        var session = MongoNoteRepository.CurrentSession;
        var result = session is not null
            ? await _careerSources.ReplaceOneAsync(session, filter, doc, new ReplaceOptions { IsUpsert = true }, cancellationToken)
            : await _careerSources.ReplaceOneAsync(filter, doc, new ReplaceOptions { IsUpsert = true }, cancellationToken);
        return Result<bool>.Success(result.IsAcknowledged);
    }

    public async Task<Result<TResponse>> ExecuteInTransactionAsync<TResponse>(
        Func<ITrackingTransactionContext, CancellationToken, Task<Result<TResponse>>> work,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);

        using var session = await _client.StartSessionAsync(cancellationToken: cancellationToken);
        session.StartTransaction();

        try
        {
            var postingRepo = new MongoPostingRepository(_client, _database);
            var context = new MongoTrackingTransactionContext(this, postingRepo, _auditLog, session);
            var result = await work(context, cancellationToken);

            if (result.IsSuccess)
            {
                await session.CommitTransactionAsync(cancellationToken);
                return result;
            }

            await session.AbortTransactionAsync(cancellationToken);
            return result;
        }
        catch
        {
            if (session.IsInTransaction)
            {
                await session.AbortTransactionAsync(cancellationToken);
            }
            throw;
        }
    }

    private static BsonDocument SerializeCompany(Company company)
    {
        var doc = new BsonDocument
        {
            ["_id"] = new BsonBinaryData(company.Id, GuidRepresentation.Standard),
            ["owner_id"] = company.OwnerId,
            ["name"] = company.Name,
            ["normalized_name"] = company.NormalizedName,
            ["description"] = company.Description is null ? BsonNull.Value : company.Description,
            ["parent_company"] = company.ParentCompany is null ? BsonNull.Value : company.ParentCompany,
            ["ownership_type"] = company.OwnershipType.ToSnakeCase(),
            ["headquarters"] = company.Headquarters is null ? BsonNull.Value : company.Headquarters,
            ["category_tags"] = new BsonArray(company.CategoryTags),
            ["status"] = company.Status.ToSnakeCase(),
            ["last_checked_at"] = company.LastCheckedAt.HasValue ? BsonDateTime.Create(company.LastCheckedAt.Value.UtcDateTime) : BsonNull.Value,
            ["last_check_result"] = company.LastCheckResult is null ? BsonNull.Value : company.LastCheckResult,
            ["version_token"] = new BsonBinaryData(company.VersionToken, GuidRepresentation.Standard),
            ["created_at"] = BsonDateTime.Create(company.CreatedAt.UtcDateTime),
            ["updated_at"] = BsonDateTime.Create(company.UpdatedAt.UtcDateTime)
        };
        return doc;
    }

    private static Company MapCompany(BsonDocument doc)
    {
        var id = doc["_id"].AsGuid;
        var ownerId = doc["owner_id"].AsString;
        var name = doc["name"].AsString;
        var normalizedName = doc.Contains("normalized_name") && !doc["normalized_name"].IsBsonNull
            ? doc["normalized_name"].AsString
            : name.ToLowerInvariant();
        var description = doc.Contains("description") && !doc["description"].IsBsonNull ? doc["description"].AsString : null;
        var parentCompany = doc.Contains("parent_company") && !doc["parent_company"].IsBsonNull ? doc["parent_company"].AsString : null;

        var ownershipType = OwnershipType.Unknown;
        if (doc.Contains("ownership_type") && !doc["ownership_type"].IsBsonNull)
        {
            _ = TrackingEnumExtensions.TryParseOwnershipType(doc["ownership_type"].AsString, out ownershipType);
        }

        var headquarters = doc.Contains("headquarters") && !doc["headquarters"].IsBsonNull ? doc["headquarters"].AsString : null;

        var categoryTags = doc.Contains("category_tags") && doc["category_tags"].IsBsonArray
            ? doc["category_tags"].AsBsonArray.Select(t => t.AsString).ToArray()
            : [];

        var status = CompanyStatus.Neutral;
        if (doc.Contains("status") && !doc["status"].IsBsonNull)
        {
            _ = TrackingEnumExtensions.TryParseCompanyStatus(doc["status"].AsString, out status);
        }

        var lastCheckedAt = doc.Contains("last_checked_at") && !doc["last_checked_at"].IsBsonNull
            ? (DateTimeOffset?)new DateTimeOffset(doc["last_checked_at"].ToUniversalTime(), TimeSpan.Zero)
            : null;

        var lastCheckResult = doc.Contains("last_check_result") && !doc["last_check_result"].IsBsonNull
            ? doc["last_check_result"].AsString
            : null;

        var versionToken = doc.Contains("version_token")
            ? doc["version_token"].AsGuid
            : Guid.Empty;

        var createdAt = doc.Contains("created_at")
            ? new DateTimeOffset(doc["created_at"].ToUniversalTime(), TimeSpan.Zero)
            : DateTimeOffset.UtcNow;

        var updatedAt = doc.Contains("updated_at")
            ? new DateTimeOffset(doc["updated_at"].ToUniversalTime(), TimeSpan.Zero)
            : createdAt;

        return Company.Rehydrate(
            id,
            ownerId,
            name,
            normalizedName,
            description,
            parentCompany,
            ownershipType,
            headquarters,
            categoryTags,
            status,
            lastCheckedAt,
            lastCheckResult,
            versionToken,
            createdAt,
            updatedAt);
    }

    private static BsonDocument SerializeCareerSource(CareerSource source)
    {
        return new BsonDocument
        {
            ["_id"] = new BsonBinaryData(source.Id, GuidRepresentation.Standard),
            ["owner_id"] = source.OwnerId,
            ["company_id"] = new BsonBinaryData(source.CompanyId, GuidRepresentation.Standard),
            ["ats_type"] = source.AtsType,
            ["tenant"] = source.Tenant is null ? BsonNull.Value : source.Tenant,
            ["slug"] = source.Slug is null ? BsonNull.Value : source.Slug,
            ["fetch_method"] = source.FetchMethod.ToSnakeCase(),
            ["notes"] = source.Notes is null ? BsonNull.Value : source.Notes,
            ["is_active"] = source.IsActive,
            ["version_token"] = new BsonBinaryData(source.VersionToken, GuidRepresentation.Standard),
            ["created_at"] = BsonDateTime.Create(source.CreatedAt.UtcDateTime),
            ["updated_at"] = BsonDateTime.Create(source.UpdatedAt.UtcDateTime)
        };
    }

    private static CareerSource MapCareerSource(BsonDocument doc)
    {
        var id = doc["_id"].AsGuid;
        var ownerId = doc["owner_id"].AsString;
        var companyId = doc["company_id"].AsGuid;
        var atsType = doc["ats_type"].AsString;
        var tenant = doc.Contains("tenant") && !doc["tenant"].IsBsonNull ? doc["tenant"].AsString : null;
        var slug = doc.Contains("slug") && !doc["slug"].IsBsonNull ? doc["slug"].AsString : null;

        var fetchMethod = FetchMethod.Api;
        if (doc.Contains("fetch_method") && !doc["fetch_method"].IsBsonNull)
        {
            _ = TrackingEnumExtensions.TryParseFetchMethod(doc["fetch_method"].AsString, out fetchMethod);
        }

        var notes = doc.Contains("notes") && !doc["notes"].IsBsonNull ? doc["notes"].AsString : null;
        var isActive = doc.Contains("is_active") && doc["is_active"].AsBoolean;

        var versionToken = doc.Contains("version_token")
            ? doc["version_token"].AsGuid
            : Guid.Empty;

        var createdAt = doc.Contains("created_at")
            ? new DateTimeOffset(doc["created_at"].ToUniversalTime(), TimeSpan.Zero)
            : DateTimeOffset.UtcNow;

        var updatedAt = doc.Contains("updated_at")
            ? new DateTimeOffset(doc["updated_at"].ToUniversalTime(), TimeSpan.Zero)
            : createdAt;

        return CareerSource.Rehydrate(
            id,
            ownerId,
            companyId,
            atsType,
            tenant,
            slug,
            fetchMethod,
            notes,
            isActive,
            versionToken,
            createdAt,
            updatedAt);
    }
}
