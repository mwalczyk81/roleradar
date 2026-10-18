using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Application.Abstractions;
using RoleRadar.Domain.Tracking;

namespace RoleRadar.Infrastructure.Mongo.Repositories;

public sealed class MongoPostingRepository : IPostingRepository
{
    private readonly IMongoClient _client;
    private readonly IMongoDatabase _database;
    private readonly IMongoCollection<BsonDocument> _postings;
    private readonly IMongoCollection<BsonDocument> _signals;

    public MongoPostingRepository(IMongoClient client, IMongoDatabase database)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _postings = database.GetCollection<BsonDocument>(TrackingMongoOptions.PostingsCollectionName);
        _signals = database.GetCollection<BsonDocument>(TrackingMongoOptions.PostingSignalsCollectionName);
    }

    public async Task<Result<Posting?>> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await TrackingIndexInitializer.EnsureIndexesOnceAsync(_database, cancellationToken);
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("owner_id", ownerId),
            Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(id, GuidRepresentation.Standard)));

        var session = MongoNoteRepository.CurrentSession;
        var doc = session is not null
            ? await _postings.Find(session, filter).FirstOrDefaultAsync(cancellationToken)
            : await _postings.Find(filter).FirstOrDefaultAsync(cancellationToken);
        return Result<Posting?>.Success(doc is null ? null : MapPosting(doc));
    }

    public async Task<Result<Posting?>> GetByDedupKeyAAsync(string ownerId, string dedupKeyA, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(dedupKeyA);
        await TrackingIndexInitializer.EnsureIndexesOnceAsync(_database, cancellationToken);

        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("owner_id", ownerId),
            Builders<BsonDocument>.Filter.Eq("dedup_key_a", dedupKeyA));

        var session = MongoNoteRepository.CurrentSession;
        var doc = session is not null
            ? await _postings.Find(session, filter).FirstOrDefaultAsync(cancellationToken)
            : await _postings.Find(filter).FirstOrDefaultAsync(cancellationToken);
        return Result<Posting?>.Success(doc is null ? null : MapPosting(doc));
    }

    public async Task<Result<Posting?>> GetByDedupKeyBAsync(string ownerId, string dedupKeyB, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(dedupKeyB);
        await TrackingIndexInitializer.EnsureIndexesOnceAsync(_database, cancellationToken);

        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("owner_id", ownerId),
            Builders<BsonDocument>.Filter.Eq("dedup_key_b", dedupKeyB));

        var session = MongoNoteRepository.CurrentSession;
        var doc = session is not null
            ? await _postings.Find(session, filter).FirstOrDefaultAsync(cancellationToken)
            : await _postings.Find(filter).FirstOrDefaultAsync(cancellationToken);
        return Result<Posting?>.Success(doc is null ? null : MapPosting(doc));
    }

    public async Task<Result<Posting?>> GetByDedupKeyCAsync(string ownerId, string dedupKeyC, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(dedupKeyC);
        await TrackingIndexInitializer.EnsureIndexesOnceAsync(_database, cancellationToken);

        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("owner_id", ownerId),
            Builders<BsonDocument>.Filter.Eq("dedup_key_c", dedupKeyC));

        var session = MongoNoteRepository.CurrentSession;
        var doc = session is not null
            ? await _postings.Find(session, filter).FirstOrDefaultAsync(cancellationToken)
            : await _postings.Find(filter).FirstOrDefaultAsync(cancellationToken);
        return Result<Posting?>.Success(doc is null ? null : MapPosting(doc));
    }

    public async Task<Result<IReadOnlyList<PostingCandidate>>> FindCandidatesByCompanyAndTitleAsync(
        string ownerId,
        Guid companyId,
        string normalizedTitle,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedTitle);
        await TrackingIndexInitializer.EnsureIndexesOnceAsync(_database, cancellationToken);

        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("owner_id", ownerId),
            Builders<BsonDocument>.Filter.Eq("company_id", new BsonBinaryData(companyId, GuidRepresentation.Standard)),
            Builders<BsonDocument>.Filter.Eq("normalized_title", normalizedTitle));

        var session = MongoNoteRepository.CurrentSession;
        var docs = session is not null
            ? await _postings.Find(session, filter).Limit(20).ToListAsync(cancellationToken)
            : await _postings.Find(filter).Limit(20).ToListAsync(cancellationToken);

        var list = new List<PostingCandidate>(docs.Count);
        foreach (var doc in docs)
        {
            var id = doc["_id"].AsGuid;
            var title = doc["title"].AsString;
            var dispositionStr = doc.GetValue("disposition", "lead").AsString;
            _ = TrackingEnumExtensions.TryParseDisposition(dispositionStr, out var disposition);
            var reason = doc.Contains("disposition_reason") && !doc["disposition_reason"].IsBsonNull
                ? doc["disposition_reason"].AsString
                : null;

            list.Add(new PostingCandidate(id, title, disposition, reason));
        }

        return Result<IReadOnlyList<PostingCandidate>>.Success(list);
    }

    public async Task<Result<IReadOnlyList<Posting>>> ListPostingsAsync(
        string ownerId,
        Guid? companyId = null,
        PostingDisposition? disposition = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await TrackingIndexInitializer.EnsureIndexesOnceAsync(_database, cancellationToken);
        var filter = Builders<BsonDocument>.Filter.Eq("owner_id", ownerId);

        if (companyId.HasValue)
        {
            filter = Builders<BsonDocument>.Filter.And(
                filter,
                Builders<BsonDocument>.Filter.Eq("company_id", new BsonBinaryData(companyId.Value, GuidRepresentation.Standard)));
        }

        if (disposition.HasValue)
        {
            filter = Builders<BsonDocument>.Filter.And(
                filter,
                Builders<BsonDocument>.Filter.Eq("disposition", disposition.Value.ToSnakeCase()));
        }

        var skip = Math.Max(0, (page - 1) * pageSize);
        var session = MongoNoteRepository.CurrentSession;
        var docs = session is not null
            ? await _postings.Find(session, filter)
                .Sort(Builders<BsonDocument>.Sort.Descending("created_at"))
                .Skip(skip)
                .Limit(pageSize)
                .ToListAsync(cancellationToken)
            : await _postings.Find(filter)
                .Sort(Builders<BsonDocument>.Sort.Descending("created_at"))
                .Skip(skip)
                .Limit(pageSize)
                .ToListAsync(cancellationToken);

        var list = new List<Posting>(docs.Count);
        foreach (var doc in docs)
        {
            list.Add(MapPosting(doc));
        }

        return Result<IReadOnlyList<Posting>>.Success(list);
    }

    public async Task<Result<IReadOnlyList<Posting>>> ListDecidedPostingsAsync(
        string ownerId,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await TrackingIndexInitializer.EnsureIndexesOnceAsync(_database, cancellationToken);
        var decidedDispositions = new[] { "screened_out", "closed", "rejected" };
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("owner_id", ownerId),
            Builders<BsonDocument>.Filter.In("disposition", decidedDispositions));

        var skip = Math.Max(0, (page - 1) * pageSize);
        var session = MongoNoteRepository.CurrentSession;
        var docs = session is not null
            ? await _postings.Find(session, filter)
                .Sort(Builders<BsonDocument>.Sort.Descending("decided_at"))
                .Skip(skip)
                .Limit(pageSize)
                .ToListAsync(cancellationToken)
            : await _postings.Find(filter)
                .Sort(Builders<BsonDocument>.Sort.Descending("decided_at"))
                .Skip(skip)
                .Limit(pageSize)
                .ToListAsync(cancellationToken);

        var list = new List<Posting>(docs.Count);
        foreach (var doc in docs)
        {
            list.Add(MapPosting(doc));
        }

        return Result<IReadOnlyList<Posting>>.Success(list);
    }

    public async Task<Result<int>> CountPostingsAsync(
        string ownerId,
        Guid? companyId = null,
        PostingDisposition? disposition = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await TrackingIndexInitializer.EnsureIndexesOnceAsync(_database, cancellationToken);
        var filter = Builders<BsonDocument>.Filter.Eq("owner_id", ownerId);

        if (companyId.HasValue)
        {
            filter = Builders<BsonDocument>.Filter.And(
                filter,
                Builders<BsonDocument>.Filter.Eq("company_id", new BsonBinaryData(companyId.Value, GuidRepresentation.Standard)));
        }

        if (disposition.HasValue)
        {
            filter = Builders<BsonDocument>.Filter.And(
                filter,
                Builders<BsonDocument>.Filter.Eq("disposition", disposition.Value.ToSnakeCase()));
        }

        var session = MongoNoteRepository.CurrentSession;
        var count = session is not null
            ? await _postings.CountDocumentsAsync(session, filter, cancellationToken: cancellationToken)
            : await _postings.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
        return Result<int>.Success((int)count);
    }

    public async Task<Result<int>> CountDecidedPostingsAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await TrackingIndexInitializer.EnsureIndexesOnceAsync(_database, cancellationToken);
        var decidedDispositions = new[] { "screened_out", "closed", "rejected" };
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("owner_id", ownerId),
            Builders<BsonDocument>.Filter.In("disposition", decidedDispositions));

        var session = MongoNoteRepository.CurrentSession;
        var count = session is not null
            ? await _postings.CountDocumentsAsync(session, filter, cancellationToken: cancellationToken)
            : await _postings.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
        return Result<int>.Success((int)count);
    }

    public async Task<Result<bool>> UpsertPostingAsync(Posting posting, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(posting);
        await TrackingIndexInitializer.EnsureIndexesOnceAsync(_database, cancellationToken);
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("owner_id", posting.OwnerId),
            Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(posting.Id, GuidRepresentation.Standard)));

        var doc = SerializePosting(posting);
        var session = MongoNoteRepository.CurrentSession;
        var result = session is not null
            ? await _postings.ReplaceOneAsync(session, filter, doc, new ReplaceOptions { IsUpsert = true }, cancellationToken)
            : await _postings.ReplaceOneAsync(filter, doc, new ReplaceOptions { IsUpsert = true }, cancellationToken);
        return Result<bool>.Success(result.IsAcknowledged);
    }

    public async Task<Result<bool>> AppendSignalAsync(PostingSignal signal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(signal);
        await TrackingIndexInitializer.EnsureIndexesOnceAsync(_database, cancellationToken);
        var doc = new BsonDocument
        {
            ["_id"] = new BsonBinaryData(signal.Id, GuidRepresentation.Standard),
            ["owner_id"] = signal.OwnerId,
            ["posting_id"] = new BsonBinaryData(signal.PostingId, GuidRepresentation.Standard),
            ["signal_type"] = signal.SignalType.ToSnakeCase(),
            ["evidence"] = signal.Evidence,
            ["deadline_date"] = signal.DeadlineDate is not null ? new BsonString(signal.DeadlineDate) : BsonNull.Value,
            ["observed_at"] = new BsonDateTime(signal.ObservedAt.UtcDateTime)
        };

        var session = MongoNoteRepository.CurrentSession;
        if (session is not null)
        {
            await _signals.InsertOneAsync(session, doc, cancellationToken: cancellationToken);
        }
        else
        {
            await _signals.InsertOneAsync(doc, cancellationToken: cancellationToken);
        }

        return Result<bool>.Success(true);
    }

    public async Task<Result<IReadOnlyList<PostingSignal>>> GetSignalsAsync(string ownerId, Guid postingId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await TrackingIndexInitializer.EnsureIndexesOnceAsync(_database, cancellationToken);
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("owner_id", ownerId),
            Builders<BsonDocument>.Filter.Eq("posting_id", new BsonBinaryData(postingId, GuidRepresentation.Standard)));

        var session = MongoNoteRepository.CurrentSession;
        var docs = session is not null
            ? await _signals.Find(session, filter).Sort(Builders<BsonDocument>.Sort.Descending("observed_at")).ToListAsync(cancellationToken)
            : await _signals.Find(filter).Sort(Builders<BsonDocument>.Sort.Descending("observed_at")).ToListAsync(cancellationToken);

        var list = new List<PostingSignal>(docs.Count);
        foreach (var doc in docs)
        {
            var id = doc["_id"].AsGuid;
            var sigTypeStr = doc["signal_type"].AsString;
            _ = TrackingEnumExtensions.TryParseSignalType(sigTypeStr, out var sigType);
            var evidence = doc["evidence"].AsString;
            var observedAt = DateTime.SpecifyKind(doc["observed_at"].ToUniversalTime(), DateTimeKind.Utc);
            var deadline = doc.Contains("deadline_date") && !doc["deadline_date"].IsBsonNull
                ? doc["deadline_date"].AsString
                : null;

            list.Add(new PostingSignal(id, ownerId, postingId, sigType, evidence, observedAt, deadline));
        }

        return Result<IReadOnlyList<PostingSignal>>.Success(list);
    }

    private static BsonDocument SerializePosting(Posting p)
    {
        var comp = new BsonDocument
        {
            ["min"] = p.Compensation.Min.HasValue ? new BsonDecimal128(p.Compensation.Min.Value) : BsonNull.Value,
            ["max"] = p.Compensation.Max.HasValue ? new BsonDecimal128(p.Compensation.Max.Value) : BsonNull.Value,
            ["currency"] = p.Compensation.Currency,
            ["text"] = p.Compensation.Text is not null ? new BsonString(p.Compensation.Text) : BsonNull.Value
        };

        return new BsonDocument
        {
            ["_id"] = new BsonBinaryData(p.Id, GuidRepresentation.Standard),
            ["owner_id"] = p.OwnerId,
            ["company_id"] = new BsonBinaryData(p.CompanyId, GuidRepresentation.Standard),
            ["title"] = p.Title,
            ["normalized_title"] = p.NormalizedTitle,
            ["original_url"] = p.OriginalUrl is not null ? new BsonString(p.OriginalUrl) : BsonNull.Value,
            ["normalized_url"] = p.NormalizedUrl is not null ? new BsonString(p.NormalizedUrl) : BsonNull.Value,
            ["source_kind"] = p.SourceKind.ToSnakeCase(),
            ["ats_tenant"] = p.AtsTenant is not null ? new BsonString(p.AtsTenant) : BsonNull.Value,
            ["requisition_id"] = p.RequisitionId is not null ? new BsonString(p.RequisitionId) : BsonNull.Value,
            ["dedup_key_a"] = p.DedupKeyA is not null ? new BsonString(p.DedupKeyA) : BsonNull.Value,
            ["dedup_key_b"] = p.DedupKeyB is not null ? new BsonString(p.DedupKeyB) : BsonNull.Value,
            ["dedup_key_c"] = p.DedupKeyC is not null ? new BsonString(p.DedupKeyC) : BsonNull.Value,
            ["location"] = p.Location is not null ? new BsonString(p.Location) : BsonNull.Value,
            ["remote_type"] = p.RemoteType.ToSnakeCase(),
            ["compensation"] = comp,
            ["posted_date"] = p.PostedDate is not null ? new BsonString(p.PostedDate) : BsonNull.Value,
            ["first_seen_at"] = new BsonDateTime(p.FirstSeenAt.UtcDateTime),
            ["last_verified_at"] = new BsonDateTime(p.LastVerifiedAt.UtcDateTime),
            ["closed_date"] = p.ClosedDate is not null ? new BsonString(p.ClosedDate) : BsonNull.Value,
            ["score"] = p.Score.HasValue ? new BsonDecimal128(p.Score.Value) : BsonNull.Value,
            ["culture_level"] = p.CultureLevel.ToSnakeCase(),
            ["culture_summary"] = p.CultureSummary is not null ? new BsonString(p.CultureSummary) : BsonNull.Value,
            ["disposition"] = p.Disposition.ToSnakeCase(),
            ["disposition_reason"] = p.DispositionReason is not null ? new BsonString(p.DispositionReason) : BsonNull.Value,
            ["disposition_criterion_id"] = p.DispositionCriterionId.HasValue
                ? new BsonBinaryData(p.DispositionCriterionId.Value, GuidRepresentation.Standard)
                : BsonNull.Value,
            ["resurface_policy"] = p.ResurfacePolicy.ToSnakeCase(),
            ["decided_at"] = p.DecidedAt.HasValue ? new BsonDateTime(p.DecidedAt.Value.UtcDateTime) : BsonNull.Value,
            ["duplicate_of_id"] = p.DuplicateOfId.HasValue
                ? new BsonBinaryData(p.DuplicateOfId.Value, GuidRepresentation.Standard)
                : BsonNull.Value,
            ["screening_note_id"] = p.ScreeningNoteId.HasValue
                ? new BsonBinaryData(p.ScreeningNoteId.Value, GuidRepresentation.Standard)
                : BsonNull.Value,
            ["version_token"] = new BsonBinaryData(p.VersionToken, GuidRepresentation.Standard),
            ["created_at"] = new BsonDateTime(p.CreatedAt.UtcDateTime),
            ["updated_at"] = new BsonDateTime(p.UpdatedAt.UtcDateTime)
        };
    }

    private static Posting MapPosting(BsonDocument doc)
    {
        var id = doc["_id"].AsGuid;
        var ownerId = doc["owner_id"].AsString;
        var companyId = doc["company_id"].AsGuid;
        var title = doc["title"].AsString;
        var normalizedTitle = doc.Contains("normalized_title") && !doc["normalized_title"].IsBsonNull
            ? doc["normalized_title"].AsString
            : TitleNormalizer.Normalize(title);

        var originalUrl = doc.Contains("original_url") && !doc["original_url"].IsBsonNull ? doc["original_url"].AsString : null;
        var normalizedUrl = doc.Contains("normalized_url") && !doc["normalized_url"].IsBsonNull ? doc["normalized_url"].AsString : null;

        var sourceKindStr = doc.GetValue("source_kind", "ats").AsString;
        _ = TrackingEnumExtensions.TryParseSourceKind(sourceKindStr, out var sourceKind);

        var atsTenant = doc.Contains("ats_tenant") && !doc["ats_tenant"].IsBsonNull ? doc["ats_tenant"].AsString : null;
        var requisitionId = doc.Contains("requisition_id") && !doc["requisition_id"].IsBsonNull ? doc["requisition_id"].AsString : null;
        var dedupKeyA = doc.Contains("dedup_key_a") && !doc["dedup_key_a"].IsBsonNull ? doc["dedup_key_a"].AsString : null;
        var dedupKeyB = doc.Contains("dedup_key_b") && !doc["dedup_key_b"].IsBsonNull ? doc["dedup_key_b"].AsString : null;
        var dedupKeyC = doc.Contains("dedup_key_c") && !doc["dedup_key_c"].IsBsonNull ? doc["dedup_key_c"].AsString : null;

        var location = doc.Contains("location") && !doc["location"].IsBsonNull ? doc["location"].AsString : null;

        var remoteTypeStr = doc.GetValue("remote_type", "unknown").AsString;
        _ = TrackingEnumExtensions.TryParseRemoteType(remoteTypeStr, out var remoteType);

        Compensation comp = new();
        if (doc.Contains("compensation") && doc["compensation"].IsBsonDocument)
        {
            var cDoc = doc["compensation"].AsBsonDocument;
            decimal? min = cDoc.Contains("min") && !cDoc["min"].IsBsonNull ? Convert.ToDecimal(cDoc["min"].AsDecimal128, System.Globalization.CultureInfo.InvariantCulture) : null;
            decimal? max = cDoc.Contains("max") && !cDoc["max"].IsBsonNull ? Convert.ToDecimal(cDoc["max"].AsDecimal128, System.Globalization.CultureInfo.InvariantCulture) : null;
            var curr = cDoc.GetValue("currency", "USD").AsString;
            var text = cDoc.Contains("text") && !cDoc["text"].IsBsonNull ? cDoc["text"].AsString : null;
            comp = new Compensation(min, max, curr, text);
        }

        var postedDate = doc.Contains("posted_date") && !doc["posted_date"].IsBsonNull ? doc["posted_date"].AsString : null;
        decimal? score = doc.Contains("score") && !doc["score"].IsBsonNull ? Convert.ToDecimal(doc["score"].AsDecimal128, System.Globalization.CultureInfo.InvariantCulture) : null;

        var cultureLevelStr = doc.GetValue("culture_level", "unknown").AsString;
        _ = TrackingEnumExtensions.TryParseCultureLevel(cultureLevelStr, out var cultureLevel);

        var cultureSummary = doc.Contains("culture_summary") && !doc["culture_summary"].IsBsonNull ? doc["culture_summary"].AsString : null;

        var dispositionStr = doc.GetValue("disposition", "lead").AsString;
        _ = TrackingEnumExtensions.TryParseDisposition(dispositionStr, out var disposition);

        var dispositionReason = doc.Contains("disposition_reason") && !doc["disposition_reason"].IsBsonNull ? doc["disposition_reason"].AsString : null;
        Guid? criterionId = doc.Contains("disposition_criterion_id") && !doc["disposition_criterion_id"].IsBsonNull ? doc["disposition_criterion_id"].AsGuid : null;

        var resurfaceStr = doc.GetValue("resurface_policy", "never").AsString;
        _ = TrackingEnumExtensions.TryParseResurfacePolicy(resurfaceStr, out var resurface);

        DateTimeOffset? decidedAt = doc.Contains("decided_at") && !doc["decided_at"].IsBsonNull
            ? DateTime.SpecifyKind(doc["decided_at"].ToUniversalTime(), DateTimeKind.Utc)
            : null;

        Guid? duplicateOfId = doc.Contains("duplicate_of_id") && !doc["duplicate_of_id"].IsBsonNull ? doc["duplicate_of_id"].AsGuid : null;
        Guid? screeningNoteId = doc.Contains("screening_note_id") && !doc["screening_note_id"].IsBsonNull ? doc["screening_note_id"].AsGuid : null;

        var firstSeen = DateTime.SpecifyKind(doc.GetValue("first_seen_at", doc["created_at"]).ToUniversalTime(), DateTimeKind.Utc);
        var lastVerified = doc.Contains("last_verified_at")
            ? DateTime.SpecifyKind(doc["last_verified_at"].ToUniversalTime(), DateTimeKind.Utc)
            : firstSeen;
        var closedDate = doc.Contains("closed_date") && !doc["closed_date"].IsBsonNull ? doc["closed_date"].AsString : null;
        var versionToken = doc.Contains("version_token") ? doc["version_token"].AsGuid : Guid.Empty;
        var createdAt = DateTime.SpecifyKind(doc.GetValue("created_at", firstSeen).ToUniversalTime(), DateTimeKind.Utc);
        var updatedAt = DateTime.SpecifyKind(doc.GetValue("updated_at", lastVerified).ToUniversalTime(), DateTimeKind.Utc);

        return Posting.Rehydrate(
            id: id,
            ownerId: ownerId,
            companyId: companyId,
            title: title,
            normalizedTitle: normalizedTitle,
            originalUrl: originalUrl,
            normalizedUrl: normalizedUrl,
            sourceKind: sourceKind,
            atsTenant: atsTenant,
            requisitionId: requisitionId,
            dedupKeyA: dedupKeyA,
            dedupKeyB: dedupKeyB,
            dedupKeyC: dedupKeyC,
            location: location,
            remoteType: remoteType,
            compensation: comp,
            postedDate: postedDate,
            firstSeenAt: firstSeen,
            lastVerifiedAt: lastVerified,
            closedDate: closedDate,
            score: score,
            cultureLevel: cultureLevel,
            cultureSummary: cultureSummary,
            disposition: disposition,
            dispositionReason: dispositionReason,
            dispositionCriterionId: criterionId,
            resurfacePolicy: resurface,
            decidedAt: decidedAt,
            duplicateOfId: duplicateOfId,
            screeningNoteId: screeningNoteId,
            versionToken: versionToken,
            createdAt: createdAt,
            updatedAt: updatedAt);
    }
}
