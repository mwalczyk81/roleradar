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
    private readonly IMongoCollection<BsonDocument> _auditCollection;
    private readonly IClientSessionHandle? _session;

    public MongoTrackingTransactionContext(
        ICompanyRepository companies,
        IPostingRepository postings,
        ICriterionRepository criteria,
        IApplicationRepository applications,
        IMongoCollection<BsonDocument> auditCollection,
        IClientSessionHandle? session = null)
    {
        _companies = companies ?? throw new ArgumentNullException(nameof(companies));
        _postings = postings ?? throw new ArgumentNullException(nameof(postings));
        _criteria = criteria ?? throw new ArgumentNullException(nameof(criteria));
        _applications = applications ?? throw new ArgumentNullException(nameof(applications));
        _auditCollection = auditCollection ?? throw new ArgumentNullException(nameof(auditCollection));
        _session = session;
    }

    public ICompanyRepository Companies => _companies;
    public IPostingRepository Postings => _postings;
    public ICriterionRepository Criteria => _criteria;
    public IApplicationRepository Applications => _applications;
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
            ["before_summary"] = entry.BeforeSummary is not null
                ? new BsonDocument
                {
                    ["body_hash"] = entry.BeforeSummary.BodyHash,
                    ["byte_length"] = entry.BeforeSummary.ByteLength,
                    ["status"] = entry.BeforeSummary.Status,
                    ["excerpt"] = entry.BeforeSummary.Excerpt
                }
                : BsonNull.Value,
            ["after_summary"] = new BsonDocument
            {
                ["body_hash"] = entry.AfterSummary.BodyHash,
                ["byte_length"] = entry.AfterSummary.ByteLength,
                ["status"] = entry.AfterSummary.Status,
                ["excerpt"] = entry.AfterSummary.Excerpt
            }
        };

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
}
