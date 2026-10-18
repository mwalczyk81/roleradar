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
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("owner_id", ownerId),
            Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(id, GuidRepresentation.Standard)));

        var doc = await _companies.Find(filter).FirstOrDefaultAsync(cancellationToken);
        return Result<Company?>.Success(doc is null ? null : MapCompany(doc));
    }

    public async Task<Result<Company?>> GetByNormalizedNameAsync(string ownerId, string normalizedName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedName);

        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("owner_id", ownerId),
            Builders<BsonDocument>.Filter.Eq("normalized_name", normalizedName.Trim().ToLowerInvariant()));

        var doc = await _companies.Find(filter).FirstOrDefaultAsync(cancellationToken);
        return Result<Company?>.Success(doc is null ? null : MapCompany(doc));
    }

    public async Task<Result<IReadOnlyList<Company>>> ListAsync(string ownerId, CompanyStatus? status = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        var filter = Builders<BsonDocument>.Filter.Eq("owner_id", ownerId);
        if (status.HasValue)
        {
            filter = Builders<BsonDocument>.Filter.And(
                filter,
                Builders<BsonDocument>.Filter.Eq("status", status.Value.ToString().ToLowerInvariant()));
        }

        var docs = await _companies.Find(filter).ToListAsync(cancellationToken);
        var list = docs.Select(MapCompany).ToArray();
        return Result<IReadOnlyList<Company>>.Success(list);
    }

    public async Task<Result<IReadOnlyList<CareerSource>>> GetCareerSourcesAsync(string ownerId, Guid companyId, bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("owner_id", ownerId),
            Builders<BsonDocument>.Filter.Eq("company_id", new BsonBinaryData(companyId, GuidRepresentation.Standard)));

        if (!includeInactive)
        {
            filter = Builders<BsonDocument>.Filter.And(
                filter,
                Builders<BsonDocument>.Filter.Eq("is_active", true));
        }

        var docs = await _careerSources.Find(filter).ToListAsync(cancellationToken);
        var list = docs.Select(MapCareerSource).ToArray();
        return Result<IReadOnlyList<CareerSource>>.Success(list);
    }

    public async Task<Result<CareerSource?>> GetCareerSourceByCompositeKeyAsync(string ownerId, Guid companyId, string atsType, string? tenant, string? slug, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(atsType);

        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("owner_id", ownerId),
            Builders<BsonDocument>.Filter.Eq("company_id", new BsonBinaryData(companyId, GuidRepresentation.Standard)),
            Builders<BsonDocument>.Filter.Eq("ats_type", atsType.Trim()),
            Builders<BsonDocument>.Filter.Eq("tenant", tenant?.Trim()),
            Builders<BsonDocument>.Filter.Eq("slug", slug?.Trim()));

        var doc = await _careerSources.Find(filter).FirstOrDefaultAsync(cancellationToken);
        return Result<CareerSource?>.Success(doc is null ? null : MapCareerSource(doc));
    }

    public async Task<Result<bool>> UpsertCompanyAsync(Company company, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(company);

        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("owner_id", company.OwnerId),
            Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(company.Id, GuidRepresentation.Standard)));

        var doc = SerializeCompany(company);
        var result = await _companies.ReplaceOneAsync(filter, doc, new ReplaceOptions { IsUpsert = true }, cancellationToken);
        return Result<bool>.Success(result.IsAcknowledged);
    }

    public async Task<Result<bool>> UpsertCareerSourceAsync(CareerSource careerSource, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(careerSource);

        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("owner_id", careerSource.OwnerId),
            Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(careerSource.Id, GuidRepresentation.Standard)));

        var doc = SerializeCareerSource(careerSource);
        var result = await _careerSources.ReplaceOneAsync(filter, doc, new ReplaceOptions { IsUpsert = true }, cancellationToken);
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
            var context = new TrackingTransactionContext(this, _auditLog, session);
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
            ["ownership_type"] = company.OwnershipType.ToString().ToLowerInvariant(),
            ["headquarters"] = company.Headquarters is null ? BsonNull.Value : company.Headquarters,
            ["category_tags"] = new BsonArray(company.CategoryTags),
            ["status"] = company.Status.ToString().ToLowerInvariant(),
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
        var description = doc.Contains("description") && !doc["description"].IsBsonNull ? doc["description"].AsString : null;
        var parentCompany = doc.Contains("parent_company") && !doc["parent_company"].IsBsonNull ? doc["parent_company"].AsString : null;

        var ownershipType = OwnershipType.Unknown;
        if (doc.Contains("ownership_type") && !doc["ownership_type"].IsBsonNull)
        {
            Enum.TryParse<OwnershipType>(doc["ownership_type"].AsString, ignoreCase: true, out ownershipType);
        }

        var headquarters = doc.Contains("headquarters") && !doc["headquarters"].IsBsonNull ? doc["headquarters"].AsString : null;

        var categoryTags = doc.Contains("category_tags") && doc["category_tags"].IsBsonArray
            ? doc["category_tags"].AsBsonArray.Select(t => t.AsString).ToArray()
            : [];

        var status = CompanyStatus.Neutral;
        if (doc.Contains("status") && !doc["status"].IsBsonNull)
        {
            Enum.TryParse<CompanyStatus>(doc["status"].AsString, ignoreCase: true, out status);
        }

        var lastCheckedAt = doc.Contains("last_checked_at") && !doc["last_checked_at"].IsBsonNull
            ? (DateTimeOffset?)new DateTimeOffset(doc["last_checked_at"].ToUniversalTime(), TimeSpan.Zero)
            : null;

        var lastCheckResult = doc.Contains("last_check_result") && !doc["last_check_result"].IsBsonNull
            ? doc["last_check_result"].AsString
            : null;

        var createdAt = doc.Contains("created_at")
            ? new DateTimeOffset(doc["created_at"].ToUniversalTime(), TimeSpan.Zero)
            : DateTimeOffset.UtcNow;

        return Company.Create(
            ownerId,
            name,
            description,
            parentCompany,
            ownershipType,
            headquarters,
            categoryTags,
            status,
            id,
            createdAt);
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
            ["fetch_method"] = source.FetchMethod.ToString().ToLowerInvariant(),
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
            Enum.TryParse<FetchMethod>(doc["fetch_method"].AsString, ignoreCase: true, out fetchMethod);
        }

        var notes = doc.Contains("notes") && !doc["notes"].IsBsonNull ? doc["notes"].AsString : null;
        var isActive = doc.Contains("is_active") && doc["is_active"].AsBoolean;

        var createdAt = doc.Contains("created_at")
            ? new DateTimeOffset(doc["created_at"].ToUniversalTime(), TimeSpan.Zero)
            : DateTimeOffset.UtcNow;

        return CareerSource.Create(
            ownerId,
            companyId,
            atsType,
            tenant,
            slug,
            fetchMethod,
            notes,
            isActive,
            id,
            createdAt);
    }

    private sealed class TrackingTransactionContext : ITrackingTransactionContext
    {
        private readonly ICompanyRepository _companies;
        private readonly IMongoCollection<BsonDocument> _auditCollection;
        private readonly IClientSessionHandle _session;

        public TrackingTransactionContext(
            ICompanyRepository companies,
            IMongoCollection<BsonDocument> auditCollection,
            IClientSessionHandle session)
        {
            _companies = companies;
            _auditCollection = auditCollection;
            _session = session;
        }

        public ICompanyRepository Companies => _companies;
        public IPostingRepository Postings => throw new NotImplementedException("Postings available in US1");
        public ICriterionRepository Criteria => throw new NotImplementedException("Criteria available in US3");
        public IApplicationRepository Applications => throw new NotImplementedException("Applications available in US4");
        public IContactRepository Contacts => throw new NotImplementedException("Contacts available in US6");
        public ISearchRunRepository SearchRuns => throw new NotImplementedException("SearchRuns available in US7");

        public async Task AppendAuditEntryAsync(AuditLogEntry entry, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(entry);

            var doc = new BsonDocument
            {
                ["_id"] = new BsonBinaryData(entry.Id, GuidRepresentation.Standard),
                ["owner_id"] = entry.OwnerId,
                ["actor"] = entry.Actor,
                ["operation"] = entry.Operation,
                ["target_note_id"] = new BsonBinaryData(entry.TargetNoteId, GuidRepresentation.Standard),
                ["note_revision_id"] = entry.NoteRevisionId.HasValue
                    ? new BsonBinaryData(entry.NoteRevisionId.Value, GuidRepresentation.Standard)
                    : BsonNull.Value,
                ["appended_text"] = entry.AppendedText is not null ? new BsonString(entry.AppendedText) : BsonNull.Value,
                ["resulting_version_token"] = new BsonBinaryData(entry.ResultingVersionToken, GuidRepresentation.Standard),
                ["resolved_default_date"] = entry.ResolvedDefaultDate is not null ? new BsonString(entry.ResolvedDefaultDate) : BsonNull.Value,
                ["occurred_at"] = new BsonDateTime(entry.OccurredAtUtc),
                ["before_summary"] = entry.BeforeSummary is not null ? MapSummary(entry.BeforeSummary) : BsonNull.Value,
                ["after_summary"] = MapSummary(entry.AfterSummary)
            };

            await _auditCollection.InsertOneAsync(_session, doc, cancellationToken: cancellationToken);
        }

        private static BsonDocument MapSummary(AuditContentSummary summary) =>
            new()
            {
                ["body_hash"] = summary.BodyHash,
                ["byte_length"] = summary.ByteLength,
                ["status"] = summary.Status,
                ["excerpt"] = summary.Excerpt
            };
    }
}
