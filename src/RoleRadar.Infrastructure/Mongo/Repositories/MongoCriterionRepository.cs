using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Application.Abstractions;
using RoleRadar.Domain.Tracking;

namespace RoleRadar.Infrastructure.Mongo.Repositories;

public sealed class MongoCriterionRepository : ICriterionRepository
{
    private readonly IMongoClient _client;
    private readonly IMongoDatabase _database;
    private readonly IMongoCollection<BsonDocument> _criteria;

    public MongoCriterionRepository(IMongoClient client, IMongoDatabase database)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _criteria = database.GetCollection<BsonDocument>(TrackingMongoOptions.CriteriaCollectionName);
    }

    public async Task<Result<Criterion?>> GetActiveByKeyAsync(string ownerId, string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        await TrackingIndexInitializer.EnsureIndexesOnceAsync(_database, cancellationToken);

        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("owner_id", ownerId),
            Builders<BsonDocument>.Filter.Eq("key", key.Trim()),
            Builders<BsonDocument>.Filter.Eq("is_active", true));

        var session = MongoNoteRepository.CurrentSession;
        var doc = session is not null
            ? await _criteria.Find(session, filter).FirstOrDefaultAsync(cancellationToken)
            : await _criteria.Find(filter).FirstOrDefaultAsync(cancellationToken);

        return Result<Criterion?>.Success(doc is null ? null : MapCriterion(doc));
    }

    public async Task<Result<Criterion?>> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await TrackingIndexInitializer.EnsureIndexesOnceAsync(_database, cancellationToken);

        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("owner_id", ownerId),
            Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(id, GuidRepresentation.Standard)));

        var session = MongoNoteRepository.CurrentSession;
        var doc = session is not null
            ? await _criteria.Find(session, filter).FirstOrDefaultAsync(cancellationToken)
            : await _criteria.Find(filter).FirstOrDefaultAsync(cancellationToken);

        return Result<Criterion?>.Success(doc is null ? null : MapCriterion(doc));
    }

    public async Task<Result<IReadOnlyList<Criterion>>> GetEffectiveAtAsync(string ownerId, DateTimeOffset effectiveDate, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await TrackingIndexInitializer.EnsureIndexesOnceAsync(_database, cancellationToken);

        var targetUtc = BsonDateTime.Create(effectiveDate.UtcDateTime);

        // effective_from <= effectiveDate AND (effective_to == null OR effective_to > effectiveDate)
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("owner_id", ownerId),
            Builders<BsonDocument>.Filter.Lte("effective_from", targetUtc),
            Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Eq("effective_to", BsonNull.Value),
                Builders<BsonDocument>.Filter.Gt("effective_to", targetUtc)));

        var session = MongoNoteRepository.CurrentSession;
        var docs = session is not null
            ? await _criteria.Find(session, filter).ToListAsync(cancellationToken)
            : await _criteria.Find(filter).ToListAsync(cancellationToken);

        var list = docs.Select(MapCriterion).ToArray();
        return Result<IReadOnlyList<Criterion>>.Success(list);
    }

    public async Task<Result<IReadOnlyList<Criterion>>> ListActiveAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await TrackingIndexInitializer.EnsureIndexesOnceAsync(_database, cancellationToken);

        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("owner_id", ownerId),
            Builders<BsonDocument>.Filter.Eq("is_active", true));

        var session = MongoNoteRepository.CurrentSession;
        var docs = session is not null
            ? await _criteria.Find(session, filter).ToListAsync(cancellationToken)
            : await _criteria.Find(filter).ToListAsync(cancellationToken);

        var list = docs.Select(MapCriterion).ToArray();
        return Result<IReadOnlyList<Criterion>>.Success(list);
    }

    public async Task<Result<bool>> UpsertCriterionAsync(Criterion criterion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criterion);
        await TrackingIndexInitializer.EnsureIndexesOnceAsync(_database, cancellationToken);

        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("owner_id", criterion.OwnerId),
            Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(criterion.Id, GuidRepresentation.Standard)));

        var doc = SerializeCriterion(criterion);
        var session = MongoNoteRepository.CurrentSession;
        var res = session is not null
            ? await _criteria.ReplaceOneAsync(session, filter, doc, new ReplaceOptions { IsUpsert = true }, cancellationToken)
            : await _criteria.ReplaceOneAsync(filter, doc, new ReplaceOptions { IsUpsert = true }, cancellationToken);

        return Result<bool>.Success(res.IsAcknowledged);
    }

    private static BsonDocument SerializeCriterion(Criterion c)
    {
        return new BsonDocument
        {
            ["_id"] = new BsonBinaryData(c.Id, GuidRepresentation.Standard),
            ["owner_id"] = c.OwnerId,
            ["key"] = c.Key,
            ["category"] = c.Category.ToSnakeCase(),
            ["value"] = c.Value,
            ["effective_from"] = BsonDateTime.Create(c.EffectiveFrom.UtcDateTime),
            ["effective_to"] = c.EffectiveTo.HasValue ? BsonDateTime.Create(c.EffectiveTo.Value.UtcDateTime) : BsonNull.Value,
            ["provenance"] = c.Provenance.ToSnakeCase(),
            ["is_active"] = c.IsActive,
            ["version_token"] = new BsonBinaryData(c.VersionToken, GuidRepresentation.Standard),
            ["created_at"] = BsonDateTime.Create(c.CreatedAt.UtcDateTime)
        };
    }

    private static Criterion MapCriterion(BsonDocument doc)
    {
        var id = doc["_id"].AsGuid;
        var ownerId = doc["owner_id"].AsString;
        var key = doc["key"].AsString;

        var category = CriterionCategory.HardSkip;
        if (doc.Contains("category") && !doc["category"].IsBsonNull)
        {
            _ = TrackingEnumExtensions.TryParseCriterionCategory(doc["category"].AsString, out category);
        }

        var value = doc["value"].AsString;
        var effectiveFrom = new DateTimeOffset(doc["effective_from"].ToUniversalTime(), TimeSpan.Zero);
        var effectiveTo = doc.Contains("effective_to") && !doc["effective_to"].IsBsonNull
            ? (DateTimeOffset?)new DateTimeOffset(doc["effective_to"].ToUniversalTime(), TimeSpan.Zero)
            : null;

        var provenance = ProvenanceKind.Stated;
        if (doc.Contains("provenance") && !doc["provenance"].IsBsonNull)
        {
            _ = TrackingEnumExtensions.TryParseProvenanceKind(doc["provenance"].AsString, out provenance);
        }

        var isActive = doc.Contains("is_active") && doc["is_active"].AsBoolean;
        var versionToken = doc.Contains("version_token") ? doc["version_token"].AsGuid : Guid.Empty;
        var createdAt = doc.Contains("created_at")
            ? new DateTimeOffset(doc["created_at"].ToUniversalTime(), TimeSpan.Zero)
            : effectiveFrom;

        return Criterion.Rehydrate(
            id,
            ownerId,
            key,
            category,
            value,
            effectiveFrom,
            effectiveTo,
            provenance,
            isActive,
            versionToken,
            createdAt);
    }
}
