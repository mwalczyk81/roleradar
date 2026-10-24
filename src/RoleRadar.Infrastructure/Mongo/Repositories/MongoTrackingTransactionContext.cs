using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Application.Abstractions;
using RoleRadar.Domain.Audit;

namespace RoleRadar.Infrastructure.Mongo.Repositories;

public sealed class MongoTrackingTransactionContext : ITrackingTransactionContext
{
    private readonly ICompanyRepository _companies;
    private readonly IPostingRepository _postings;
    private readonly ICriterionRepository _criteria;
    private readonly IApplicationRepository _applications;
    private readonly IContactRepository? _contacts;
    private readonly ISearchRunRepository? _searchRuns;
    private readonly IMongoCollection<BsonDocument> _auditCollection;
    private readonly IMongoCollection<BsonDocument>? _notesCollection;
    private readonly IClientSessionHandle? _session;

    public MongoTrackingTransactionContext(
        ICompanyRepository companies,
        IPostingRepository postings,
        ICriterionRepository criteria,
        IApplicationRepository applications,
        IContactRepository? contacts,
        ISearchRunRepository? searchRuns,
        IMongoCollection<BsonDocument> auditCollection,
        IMongoCollection<BsonDocument>? notesCollection = null,
        IClientSessionHandle? session = null)
    {
        _companies = companies ?? throw new ArgumentNullException(nameof(companies));
        _postings = postings ?? throw new ArgumentNullException(nameof(postings));
        _criteria = criteria ?? throw new ArgumentNullException(nameof(criteria));
        _applications = applications ?? throw new ArgumentNullException(nameof(applications));
        _contacts = contacts;
        _searchRuns = searchRuns;
        _auditCollection = auditCollection ?? throw new ArgumentNullException(nameof(auditCollection));
        _notesCollection = notesCollection;
        _session = session;
    }

    public MongoTrackingTransactionContext(
        ICompanyRepository companies,
        IPostingRepository postings,
        ICriterionRepository criteria,
        IApplicationRepository applications,
        IContactRepository? contacts,
        ISearchRunRepository? searchRuns,
        IMongoCollection<BsonDocument> auditCollection,
        IClientSessionHandle? session)
        : this(companies, postings, criteria, applications, contacts, searchRuns, auditCollection, null, session)
    {
    }

    public MongoTrackingTransactionContext(
        ICompanyRepository companies,
        IPostingRepository postings,
        ICriterionRepository criteria,
        IApplicationRepository applications,
        IContactRepository contacts,
        IMongoCollection<BsonDocument> auditCollection,
        IClientSessionHandle? session = null)
        : this(companies, postings, criteria, applications, contacts, null, auditCollection, session)
    {
    }

    public MongoTrackingTransactionContext(
        ICompanyRepository companies,
        IPostingRepository postings,
        ICriterionRepository criteria,
        IApplicationRepository applications,
        IMongoCollection<BsonDocument> auditCollection,
        IClientSessionHandle? session = null)
        : this(companies, postings, criteria, applications, null, null, auditCollection, session)
    {
    }

    public ICompanyRepository Companies => _companies;
    public IPostingRepository Postings => _postings;
    public ICriterionRepository Criteria => _criteria;
    public IApplicationRepository Applications => _applications;
    public IContactRepository Contacts => _contacts ?? throw new InvalidOperationException("Contact repository not configured in this transaction context.");
    public ISearchRunRepository SearchRuns => _searchRuns ?? throw new InvalidOperationException("SearchRun repository not configured in this transaction context.");

    public async Task AppendAuditEntryAsync(AuditLogEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var doc = new BsonDocument
        {
            [TrackingBsonFieldNames.AuditLog.Id] = new BsonBinaryData(entry.Id, GuidRepresentation.Standard),
            [TrackingBsonFieldNames.AuditLog.OwnerId] = entry.OwnerId,
            [TrackingBsonFieldNames.AuditLog.Actor] = entry.Actor,
            [TrackingBsonFieldNames.AuditLog.Operation] = entry.Operation,
            [TrackingBsonFieldNames.AuditLog.EntityType] = entry.EntityType,
            [TrackingBsonFieldNames.AuditLog.EntityId] = new BsonBinaryData(entry.EntityId, GuidRepresentation.Standard),
            [TrackingBsonFieldNames.AuditLog.ResultingVersionToken] = new BsonBinaryData(entry.ResultingVersionToken, GuidRepresentation.Standard),
            [TrackingBsonFieldNames.AuditLog.OccurredAt] = new BsonDateTime(entry.OccurredAtUtc),
        };

        if (entry.Details is not null)
        {
            doc[TrackingBsonFieldNames.AuditLog.Details] = MapDetailsToBson(entry.Details);
        }

        var session = _session ?? MongoNoteRepository.CurrentSession;
        if (session is not null)
        {
            await _auditCollection.InsertOneAsync(session, doc, cancellationToken: cancellationToken);
        }
        else
        {
            await _auditCollection.InsertOneAsync(doc, cancellationToken: cancellationToken);
        }
    }

    private static BsonDocument MapDetailsToBson(AuditDetails details)
    {
        var doc = new BsonDocument
        {
            ["changed_fields"] = new BsonArray(details.ChangedFields)
        };

        if (details.ContactId.HasValue)
        {
            doc["contact_id"] = new BsonBinaryData(details.ContactId.Value, GuidRepresentation.Standard);
        }

        if (details.Before is not null && details.Before.Count > 0)
        {
            var beforeDoc = new BsonDocument();
            foreach (var (k, v) in details.Before)
            {
                beforeDoc[k] = MapValueToBson(v);
            }
            doc["before"] = beforeDoc;
        }

        if (details.After is not null && details.After.Count > 0)
        {
            var afterDoc = new BsonDocument();
            foreach (var (k, v) in details.After)
            {
                afterDoc[k] = MapValueToBson(v);
            }
            doc["after"] = afterDoc;
        }

        return doc;
    }

    private static BsonValue MapValueToBson(object? value) =>
        value switch
        {
            null => BsonNull.Value,
            string s => new BsonString(s),
            bool b => new BsonBoolean(b),
            int i => new BsonInt32(i),
            long l => new BsonInt64(l),
            decimal d => new BsonDecimal128(d),
            double dbl => new BsonDouble(dbl),
            Guid g => new BsonBinaryData(g, GuidRepresentation.Standard),
            DateTime dt => new BsonDateTime(dt),
            DateTimeOffset dto => new BsonDateTime(dto.UtcDateTime),
            IEnumerable<string> strSeq => new BsonArray(strSeq),
            _ => new BsonString(value.ToString() ?? string.Empty)
        };

    public async Task<Result<bool>> UpsertNoteAsync(RoleRadar.Domain.Notes.Note note, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(note);
        if (_notesCollection is null)
        {
            return Result<bool>.Success(true);
        }

        var filter = Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(note.Id, GuidRepresentation.Standard));
        var doc = MongoNoteRepository.MapNoteToDocument(note);
        var options = new ReplaceOptions { IsUpsert = true };

        if (_session is not null)
        {
            await _notesCollection.ReplaceOneAsync(_session, filter, doc, options, cancellationToken);
        }
        else
        {
            await _notesCollection.ReplaceOneAsync(filter, doc, options, cancellationToken);
        }

        return Result<bool>.Success(true);
    }

    public async Task<Result<bool>> NoteExistsAsync(string ownerId, string title, string area, CancellationToken cancellationToken = default)
    {
        if (_notesCollection is null)
        {
            return Result<bool>.Success(false);
        }

        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("owner_id", ownerId),
            Builders<BsonDocument>.Filter.Eq("title", title),
            Builders<BsonDocument>.Filter.Eq("area", area));

        var count = _session is not null
            ? await _notesCollection.CountDocumentsAsync(_session, filter, cancellationToken: cancellationToken)
            : await _notesCollection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);

        return Result<bool>.Success(count > 0);
    }
}
