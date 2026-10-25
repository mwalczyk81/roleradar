using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Integrity;
using RoleRadar.Domain.Common;

namespace RoleRadar.Infrastructure.Mongo.Repositories;

public sealed class MongoIntegrityRepository : IIntegrityRepository
{
    private readonly IMongoDatabase _database;

    public MongoIntegrityRepository(IMongoClient client, IMongoDatabase database)
    {
        ArgumentNullException.ThrowIfNull(client);
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public async Task<Result<VerifyIntegrityResultDto>> VerifyIntegrityAsync(string? ownerId = null, CancellationToken cancellationToken = default)
    {
        await TrackingIndexInitializer.EnsureIndexesOnceAsync(_database, cancellationToken);

        var configuredOwner = string.IsNullOrWhiteSpace(ownerId)
            ? OwnerId.Default.Value
            : new OwnerId(ownerId).Value;

        var filter = Builders<BsonDocument>.Filter.Eq(TrackingBsonFieldNames.Common.OwnerId, configuredOwner);

        var companiesCol = _database.GetCollection<BsonDocument>(TrackingMongoOptions.CompaniesCollectionName);
        var postingsCol = _database.GetCollection<BsonDocument>(TrackingMongoOptions.PostingsCollectionName);
        var applicationsCol = _database.GetCollection<BsonDocument>(TrackingMongoOptions.ApplicationsCollectionName);
        var signalsCol = _database.GetCollection<BsonDocument>(TrackingMongoOptions.PostingSignalsCollectionName);
        var notesCol = _database.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.NotesCollectionName);
        var searchRunsCol = _database.GetCollection<BsonDocument>(TrackingMongoOptions.SearchRunsCollectionName);

        // 1. Fetch Companies
        var companiesDocs = await companiesCol.Find(filter)
            .Project(Builders<BsonDocument>.Projection.Include(TrackingBsonFieldNames.Companies.Id))
            .ToListAsync(cancellationToken);
        var companyIds = new HashSet<Guid>();
        foreach (var doc in companiesDocs)
        {
            var id = ExtractGuid(doc[TrackingBsonFieldNames.Companies.Id]);
            if (id.HasValue) companyIds.Add(id.Value);
        }

        // 2. Fetch Postings
        var postingsDocs = await postingsCol.Find(filter)
            .Project(Builders<BsonDocument>.Projection
                .Include(TrackingBsonFieldNames.Postings.Id)
                .Include(TrackingBsonFieldNames.Postings.CompanyId)
                .Include(TrackingBsonFieldNames.Postings.Disposition)
                .Include(TrackingBsonFieldNames.Postings.DuplicateOfId))
            .ToListAsync(cancellationToken);

        var postingIds = new HashSet<Guid>();
        var appliedPostingIds = new HashSet<Guid>();
        foreach (var doc in postingsDocs)
        {
            var id = ExtractGuid(doc[TrackingBsonFieldNames.Postings.Id]);
            if (id.HasValue)
            {
                postingIds.Add(id.Value);
                if (doc.TryGetValue(TrackingBsonFieldNames.Postings.Disposition, out var dispVal) && !dispVal.IsBsonNull &&
                    string.Equals(dispVal.AsString, "applied", StringComparison.OrdinalIgnoreCase))
                {
                    appliedPostingIds.Add(id.Value);
                }
            }
        }

        // 3. Fetch Applications
        var appsDocs = await applicationsCol.Find(filter)
            .Project(Builders<BsonDocument>.Projection
                .Include(TrackingBsonFieldNames.Applications.Id)
                .Include(TrackingBsonFieldNames.Applications.PostingId))
            .ToListAsync(cancellationToken);

        var applicationIds = new HashSet<Guid>();
        var appPostingIds = new HashSet<Guid>();
        foreach (var doc in appsDocs)
        {
            var id = ExtractGuid(doc[TrackingBsonFieldNames.Applications.Id]);
            if (id.HasValue) applicationIds.Add(id.Value);

            var pId = ExtractGuid(doc.TryGetValue(TrackingBsonFieldNames.Applications.PostingId, out var pv) ? pv : null);
            if (pId.HasValue) appPostingIds.Add(pId.Value);
        }

        // 4. Fetch Notes
        var notesDocs = await notesCol.Find(filter)
            .Project(Builders<BsonDocument>.Projection
                .Include(TrackingBsonFieldNames.Notes.Id)
                .Include(TrackingBsonFieldNames.Notes.CompanyId)
                .Include(TrackingBsonFieldNames.Notes.PostingId)
                .Include(TrackingBsonFieldNames.Notes.ApplicationId))
            .ToListAsync(cancellationToken);

        var noteIds = new HashSet<Guid>();
        foreach (var doc in notesDocs)
        {
            var id = ExtractGuid(doc[TrackingBsonFieldNames.Notes.Id]);
            if (id.HasValue) noteIds.Add(id.Value);
        }

        // 5. Fetch Signals
        var signalsDocs = await signalsCol.Find(filter)
            .Project(Builders<BsonDocument>.Projection
                .Include(TrackingBsonFieldNames.Signals.Id)
                .Include(TrackingBsonFieldNames.Signals.PostingId))
            .ToListAsync(cancellationToken);

        // 6. Fetch SearchRuns
        var searchRunsDocs = await searchRunsCol.Find(filter)
            .Project(Builders<BsonDocument>.Projection
                .Include(TrackingBsonFieldNames.SearchRuns.Id)
                .Include(TrackingBsonFieldNames.SearchRuns.ReportNoteId))
            .ToListAsync(cancellationToken);

        var anomalies = new List<IntegrityAnomalyDto>();

        // Anomaly Check 1: Postings with dangling company_id
        // Anomaly Check 4: Postings with dangling duplicate_of_id
        foreach (var doc in postingsDocs)
        {
            var postingId = ExtractGuid(doc[TrackingBsonFieldNames.Postings.Id]);
            if (!postingId.HasValue) continue;

            var companyId = ExtractGuid(doc.TryGetValue(TrackingBsonFieldNames.Postings.CompanyId, out var cv) ? cv : null);
            if (companyId.HasValue && !companyIds.Contains(companyId.Value))
            {
                anomalies.Add(new IntegrityAnomalyDto(
                    "dangling_company_on_posting",
                    "Posting references non-existent company.",
                    postingId.Value,
                    companyId.Value));
            }

            var duplicateOf = ExtractGuid(doc.TryGetValue(TrackingBsonFieldNames.Postings.DuplicateOfId, out var dv) ? dv : null);
            if (duplicateOf.HasValue && !postingIds.Contains(duplicateOf.Value))
            {
                anomalies.Add(new IntegrityAnomalyDto(
                    "dangling_duplicate_of_on_posting",
                    "Posting duplicate_of references non-existent posting.",
                    postingId.Value,
                    duplicateOf.Value));
            }
        }

        // Anomaly Check 2: Applications with dangling posting_id
        foreach (var doc in appsDocs)
        {
            var appId = ExtractGuid(doc[TrackingBsonFieldNames.Applications.Id]);
            if (!appId.HasValue) continue;

            var postingId = ExtractGuid(doc.TryGetValue(TrackingBsonFieldNames.Applications.PostingId, out var pv) ? pv : null);
            if (postingId.HasValue && !postingIds.Contains(postingId.Value))
            {
                anomalies.Add(new IntegrityAnomalyDto(
                    "dangling_posting_on_application",
                    "Application references non-existent posting.",
                    appId.Value,
                    postingId.Value));
            }
        }

        // Anomaly Check 3: Posting signals with dangling posting_id
        foreach (var doc in signalsDocs)
        {
            var signalId = ExtractGuid(doc[TrackingBsonFieldNames.Signals.Id]);
            if (!signalId.HasValue) continue;

            var postingId = ExtractGuid(doc.TryGetValue(TrackingBsonFieldNames.Signals.PostingId, out var pv) ? pv : null);
            if (postingId.HasValue && !postingIds.Contains(postingId.Value))
            {
                anomalies.Add(new IntegrityAnomalyDto(
                    "dangling_posting_on_signal",
                    "Posting signal references non-existent posting.",
                    signalId.Value,
                    postingId.Value));
            }
        }

        // Anomaly Check 5: Notes with dangling company_id, posting_id, or application_id
        foreach (var doc in notesDocs)
        {
            var noteId = ExtractGuid(doc[TrackingBsonFieldNames.Notes.Id]);
            if (!noteId.HasValue) continue;

            var cId = ExtractGuid(doc.TryGetValue(TrackingBsonFieldNames.Notes.CompanyId, out var cv) ? cv : null);
            if (cId.HasValue && !companyIds.Contains(cId.Value))
            {
                anomalies.Add(new IntegrityAnomalyDto(
                    "dangling_company_on_note",
                    "Note references non-existent company.",
                    noteId.Value,
                    cId.Value));
            }

            var pId = ExtractGuid(doc.TryGetValue(TrackingBsonFieldNames.Notes.PostingId, out var pv) ? pv : null);
            if (pId.HasValue && !postingIds.Contains(pId.Value))
            {
                anomalies.Add(new IntegrityAnomalyDto(
                    "dangling_posting_on_note",
                    "Note references non-existent posting.",
                    noteId.Value,
                    pId.Value));
            }

            var aId = ExtractGuid(doc.TryGetValue(TrackingBsonFieldNames.Notes.ApplicationId, out var av) ? av : null);
            if (aId.HasValue && !applicationIds.Contains(aId.Value))
            {
                anomalies.Add(new IntegrityAnomalyDto(
                    "dangling_application_on_note",
                    "Note references non-existent application.",
                    noteId.Value,
                    aId.Value));
            }
        }

        // Anomaly Check 6: Search runs with dangling report_note_id
        foreach (var doc in searchRunsDocs)
        {
            var runId = ExtractGuid(doc[TrackingBsonFieldNames.SearchRuns.Id]);
            if (!runId.HasValue) continue;

            var reportNoteId = ExtractGuid(doc.TryGetValue(TrackingBsonFieldNames.SearchRuns.ReportNoteId, out var rv) ? rv : null);
            if (reportNoteId.HasValue && !noteIds.Contains(reportNoteId.Value))
            {
                anomalies.Add(new IntegrityAnomalyDto(
                    "dangling_report_note_on_search_run",
                    "Search run references non-existent report note.",
                    runId.Value,
                    reportNoteId.Value));
            }
        }

        // Anomaly Check 7: Applied postings without matching application record
        foreach (var appliedPostingId in appliedPostingIds)
        {
            if (!appPostingIds.Contains(appliedPostingId))
            {
                anomalies.Add(new IntegrityAnomalyDto(
                    "applied_posting_without_application",
                    "Posting has applied disposition but no matching application record exists.",
                    appliedPostingId,
                    null));
            }
        }

        // Anomaly Check 8: Records in any collection whose owner_id differs from configured owner (count only)
        var allCollections = new[]
        {
            TrackingMongoOptions.CompaniesCollectionName,
            TrackingMongoOptions.CareerSourcesCollectionName,
            TrackingMongoOptions.PostingsCollectionName,
            TrackingMongoOptions.PostingSignalsCollectionName,
            TrackingMongoOptions.CriteriaCollectionName,
            TrackingMongoOptions.ApplicationsCollectionName,
            TrackingMongoOptions.ApplicationEventsCollectionName,
            TrackingMongoOptions.ContactsCollectionName,
            TrackingMongoOptions.SearchRunsCollectionName,
            MongoBootstrapAndIndexes.NotesCollectionName,
            MongoBootstrapAndIndexes.NoteRevisionsCollectionName,
            MongoBootstrapAndIndexes.AuditLogCollectionName
        };

        foreach (var colName in allCollections)
        {
            var col = _database.GetCollection<BsonDocument>(colName);
            var mismatchFilter = Builders<BsonDocument>.Filter.Ne(TrackingBsonFieldNames.Common.OwnerId, configuredOwner);
            var mismatchCount = await col.CountDocumentsAsync(mismatchFilter, cancellationToken: cancellationToken);
            if (mismatchCount > 0)
            {
                anomalies.Add(new IntegrityAnomalyDto(
                    "owner_mismatch",
                    $"{mismatchCount} record(s) in collection '{colName}' have owner_id differing from configured owner '{configuredOwner}'.",
                    Guid.Empty));
            }
        }

        return Result<VerifyIntegrityResultDto>.Success(new VerifyIntegrityResultDto(anomalies.Count, anomalies));
    }

    private static Guid? ExtractGuid(BsonValue? val)
    {
        if (val is null || val.IsBsonNull) return null;
        if (val.IsBsonBinaryData) return val.AsBsonBinaryData.ToGuid(GuidRepresentation.Standard);
        if (val.IsGuid) return val.AsGuid;
        if (val.IsString && Guid.TryParse(val.AsString, out var g)) return g;
        return null;
    }
}
