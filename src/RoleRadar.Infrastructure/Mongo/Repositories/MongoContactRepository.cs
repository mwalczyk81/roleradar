using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Application.Abstractions;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Tracking;

namespace RoleRadar.Infrastructure.Mongo.Repositories;

public sealed class MongoContactRepository : IContactRepository
{
    private readonly IMongoClient _client;
    private readonly IMongoDatabase _database;
    private readonly IMongoCollection<BsonDocument> _collection;

    public MongoContactRepository(IMongoClient client, IMongoDatabase database)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _collection = database.GetCollection<BsonDocument>(TrackingMongoOptions.ContactsCollectionName);
    }

    public async Task<Result<Contact?>> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ownerId) || id == Guid.Empty)
        {
            return Result<Contact?>.Success(null);
        }

        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("owner_id", ownerId),
            Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(id, GuidRepresentation.Standard)));

        var session = MongoNoteRepository.CurrentSession;
        var doc = session is not null
            ? await (await _collection.FindAsync(session, filter, cancellationToken: cancellationToken)).FirstOrDefaultAsync(cancellationToken)
            : await (await _collection.FindAsync(filter, cancellationToken: cancellationToken)).FirstOrDefaultAsync(cancellationToken);

        if (doc is null)
        {
            return Result<Contact?>.Success(null);
        }

        return Result<Contact?>.Success(MapDocumentToContact(doc));
    }

    public async Task<Result<bool>> UpsertContactAsync(Contact contact, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contact);

        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("owner_id", contact.OwnerId),
            Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(contact.Id, GuidRepresentation.Standard)));

        var doc = MapContactToDocument(contact);
        var session = MongoNoteRepository.CurrentSession;

        if (session is not null)
        {
            await _collection.ReplaceOneAsync(session, filter, doc, new ReplaceOptions { IsUpsert = true }, cancellationToken);
        }
        else
        {
            await _collection.ReplaceOneAsync(filter, doc, new ReplaceOptions { IsUpsert = true }, cancellationToken);
        }

        return Result<bool>.Success(true);
    }

    public async Task<Result<int>> CountContactsAsync(string ownerId, bool? isRedacted = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            return Result<int>.Success(0);
        }

        var filters = new List<FilterDefinition<BsonDocument>>
        {
            Builders<BsonDocument>.Filter.Eq("owner_id", ownerId)
        };

        if (isRedacted.HasValue)
        {
            filters.Add(Builders<BsonDocument>.Filter.Eq("is_redacted", isRedacted.Value));
        }

        var combined = Builders<BsonDocument>.Filter.And(filters);
        var session = MongoNoteRepository.CurrentSession;
        var count = session is not null
            ? await _collection.CountDocumentsAsync(session, combined, cancellationToken: cancellationToken)
            : await _collection.CountDocumentsAsync(combined, cancellationToken: cancellationToken);

        return Result<int>.Success((int)count);
    }

    private static BsonDocument MapContactToDocument(Contact contact)
    {
        return new BsonDocument
        {
            ["_id"] = new BsonBinaryData(contact.Id, GuidRepresentation.Standard),
            ["owner_id"] = contact.OwnerId,
            ["name"] = contact.Name,
            ["role"] = contact.Role is not null ? new BsonString(contact.Role) : BsonNull.Value,
            ["company_affiliation"] = contact.CompanyAffiliation is not null ? new BsonString(contact.CompanyAffiliation) : BsonNull.Value,
            ["company_id"] = contact.CompanyId.HasValue ? new BsonBinaryData(contact.CompanyId.Value, GuidRepresentation.Standard) : BsonNull.Value,
            ["relationship"] = contact.Relationship.ToSnakeCase(),
            ["notes"] = contact.Notes is not null ? new BsonString(contact.Notes) : BsonNull.Value,
            ["is_redacted"] = contact.IsRedacted,
            ["version_token"] = new BsonBinaryData(contact.VersionToken, GuidRepresentation.Standard),
            ["created_at"] = new BsonDateTime(contact.CreatedAt),
            ["updated_at"] = new BsonDateTime(contact.UpdatedAt)
        };
    }

    private static Contact MapDocumentToContact(BsonDocument doc)
    {
        var id = doc["_id"].AsGuid;
        var ownerId = doc["owner_id"].AsString;
        var name = doc["name"].AsString;
        var role = doc.Contains("role") && !doc["role"].IsBsonNull ? doc["role"].AsString : null;
        var companyAffiliation = doc.Contains("company_affiliation") && !doc["company_affiliation"].IsBsonNull ? doc["company_affiliation"].AsString : null;
        var companyId = doc.Contains("company_id") && !doc["company_id"].IsBsonNull ? doc["company_id"].AsGuid : (Guid?)null;

        var relationshipStr = doc["relationship"].AsString;
        var relationship = relationshipStr.ToLowerInvariant() switch
        {
            "recruiter" => ContactRelationship.Recruiter,
            "referral" => ContactRelationship.Referral,
            "hiring_manager" or "hiringmanager" => ContactRelationship.HiringManager,
            "peer" => ContactRelationship.Peer,
            "redacted" => ContactRelationship.Redacted,
            _ => ContactRelationship.Peer
        };

        var notes = doc.Contains("notes") && !doc["notes"].IsBsonNull ? doc["notes"].AsString : null;
        var isRedacted = doc.Contains("is_redacted") && doc["is_redacted"].AsBoolean;
        var versionToken = doc["version_token"].AsGuid;
        var createdAt = doc["created_at"].ToUniversalTime();
        var updatedAt = doc["updated_at"].ToUniversalTime();

        return Contact.Rehydrate(
            id,
            ownerId,
            name,
            role,
            companyAffiliation,
            companyId,
            relationship,
            notes,
            isRedacted,
            versionToken,
            createdAt,
            updatedAt);
    }
}
