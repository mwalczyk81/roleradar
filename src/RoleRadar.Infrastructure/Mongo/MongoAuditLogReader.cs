using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Audit.Queries;

namespace RoleRadar.Infrastructure.Mongo;

/// <summary>
/// Read-only audit listing over <c>audit_log</c> (feature 013, FR-020, ADR 0041). Every call filters on the query's
/// owner, sorts by (occurred_at desc, _id desc), pages by keyset on that pair, and projects an allowlist of fields, so
/// appended text, before and after values and content summaries never leave the store. It writes nothing.
/// </summary>
public sealed class MongoAuditLogReader : IAuditLogReader
{
    private const string ChangedFieldsPath = TrackingBsonFieldNames.AuditLog.Details + ".changed_fields";

    private readonly IMongoDatabase _database;
    private readonly MongoReadinessProbe _readinessProbe;

    public MongoAuditLogReader(IMongoClient client, IOptions<MongoOptions> options, MongoReadinessProbe readinessProbe)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(readinessProbe);

        _database = client.GetDatabase(options.Value.DatabaseName);
        _readinessProbe = readinessProbe;
    }

    public async Task<Result<AuditListPage>> ListAsync(ListAuditEntriesQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var invalid = ListAuditEntriesQueryValidator.Validate(query);
        if (invalid is not null)
        {
            return Result<AuditListPage>.Failure(invalid.Value);
        }

        var readiness = await _readinessProbe.CheckAsync(cancellationToken);
        if (!readiness.IsSuccess)
        {
            return Result<AuditListPage>.Failure(readiness.Error!.Value);
        }

        try
        {
            var audit = _database.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.AuditLogCollectionName);
            var documents = await audit
                .Find(BuildFilter(query))
                .Sort(Builders<BsonDocument>.Sort
                    .Descending(TrackingBsonFieldNames.AuditLog.OccurredAt)
                    .Descending(TrackingBsonFieldNames.AuditLog.Id))
                .Project(BuildProjection(query.IncludeChangedFields))
                .Limit(query.Limit + 1)
                .ToListAsync(cancellationToken);

            var pageDocuments = documents.Take(query.Limit).ToList();
            var items = pageDocuments.Select(Map).ToList();
            var nextCursor = documents.Count > query.Limit ? CursorOf(pageDocuments[^1]) : null;
            return Result<AuditListPage>.Success(new AuditListPage(items, nextCursor));
        }
        catch (Exception ex) when (ex is MongoException or TimeoutException)
        {
            // The message names no endpoint and carries no connection string, so no credential can reach it.
            return Result<AuditListPage>.Failure(
                ErrorCode.DatastoreOffline,
                "The data store became unreachable while reading the audit log.");
        }
    }

    private static FilterDefinition<BsonDocument> BuildFilter(ListAuditEntriesQuery query)
    {
        var f = Builders<BsonDocument>.Filter;
        var filters = new List<FilterDefinition<BsonDocument>>
        {
            // The owner filter is unconditional: it is always the first clause, whatever else is asked for.
            f.Eq(TrackingBsonFieldNames.AuditLog.OwnerId, query.OwnerId.Value),
        };

        if (query.EntityType is not null)
        {
            filters.Add(f.Eq(TrackingBsonFieldNames.AuditLog.EntityType, query.EntityType));
        }

        if (query.EntityId is not null)
        {
            // Entries store a GUID entity id as binary; a legacy or foreign entry may hold the id as text.
            var accepted = new List<BsonValue> { query.EntityId };
            if (Guid.TryParse(query.EntityId, out var entityGuid))
            {
                accepted.Add(new BsonBinaryData(entityGuid, GuidRepresentation.Standard));
            }

            filters.Add(f.In(TrackingBsonFieldNames.AuditLog.EntityId, accepted));
        }

        if (query.Actor is not null)
        {
            filters.Add(f.Eq(TrackingBsonFieldNames.AuditLog.Actor, query.Actor));
        }

        if (query.FromUtc is { } from)
        {
            filters.Add(f.Gte(TrackingBsonFieldNames.AuditLog.OccurredAt, new BsonDateTime(from.UtcDateTime)));
        }

        if (query.ToUtc is { } to)
        {
            filters.Add(f.Lt(TrackingBsonFieldNames.AuditLog.OccurredAt, new BsonDateTime(to.UtcDateTime)));
        }

        if (query.Cursor is not null && AuditCursorCodec.TryDecode(query.Cursor, out var at, out var id))
        {
            var atValue = new BsonDateTime(at);
            filters.Add(f.Or(
                f.Lt(TrackingBsonFieldNames.AuditLog.OccurredAt, atValue),
                f.And(
                    f.Eq(TrackingBsonFieldNames.AuditLog.OccurredAt, atValue),
                    f.Lt(TrackingBsonFieldNames.AuditLog.Id, new BsonBinaryData(id, GuidRepresentation.Standard)))));
        }

        return f.And(filters);
    }

    /// <summary>An allowlist: only these fields are fetched. Nothing else in the document is ever read.</summary>
    private static ProjectionDefinition<BsonDocument> BuildProjection(bool includeChangedFields)
    {
        var p = Builders<BsonDocument>.Projection;
        var projection = p.Combine(
            p.Include(TrackingBsonFieldNames.AuditLog.Id),
            p.Include(TrackingBsonFieldNames.AuditLog.OccurredAt),
            p.Include(TrackingBsonFieldNames.AuditLog.EntityType),
            p.Include(TrackingBsonFieldNames.AuditLog.EntityId),
            p.Include(TrackingBsonFieldNames.AuditLog.Operation),
            p.Include(TrackingBsonFieldNames.AuditLog.Actor),
            p.Include(TrackingBsonFieldNames.AuditLog.RequestCorrelationId));

        // Field names only: this path holds the names, never the before and after values next to it.
        return includeChangedFields ? p.Combine(projection, p.Include(ChangedFieldsPath)) : projection;
    }

    private static string? CursorOf(BsonDocument document) =>
        document.TryGetValue(TrackingBsonFieldNames.AuditLog.Id, out var id) && id is BsonBinaryData { SubType: BsonBinarySubType.UuidStandard } binary
            ? AuditCursorCodec.Encode(document[TrackingBsonFieldNames.AuditLog.OccurredAt].ToUniversalTime(), binary.ToGuid(GuidRepresentation.Standard))
            : null;

    private static AuditListItem Map(BsonDocument document) =>
        new(
            OccurredAtUtc: document[TrackingBsonFieldNames.AuditLog.OccurredAt].ToUniversalTime(),
            EntityType: TextOf(document, TrackingBsonFieldNames.AuditLog.EntityType),
            EntityId: EntityIdOf(document),
            Operation: TextOf(document, TrackingBsonFieldNames.AuditLog.Operation),
            Actor: TextOf(document, TrackingBsonFieldNames.AuditLog.Actor),
            RequestCorrelationId: document.TryGetValue(TrackingBsonFieldNames.AuditLog.RequestCorrelationId, out var correlation) && correlation.IsString
                ? correlation.AsString
                : null,
            ChangedFields: ChangedFieldsOf(document));

    private static string TextOf(BsonDocument document, string field) =>
        document.TryGetValue(field, out var value) && value.IsString ? value.AsString : string.Empty;

    private static string EntityIdOf(BsonDocument document)
    {
        if (!document.TryGetValue(TrackingBsonFieldNames.AuditLog.EntityId, out var value))
        {
            return string.Empty;
        }

        return value switch
        {
            BsonBinaryData { SubType: BsonBinarySubType.UuidStandard } binary => binary.ToGuid(GuidRepresentation.Standard).ToString("D"),
            BsonString text => text.Value,
            _ => string.Empty,
        };
    }

    private static List<string>? ChangedFieldsOf(BsonDocument document) =>
        document.TryGetValue(TrackingBsonFieldNames.AuditLog.Details, out var details)
        && details is BsonDocument detailsDocument
        && detailsDocument.TryGetValue("changed_fields", out var fields)
        && fields is BsonArray array
            ? array.Where(v => v.IsString).Select(v => v.AsString).ToList()
            : null;
}
