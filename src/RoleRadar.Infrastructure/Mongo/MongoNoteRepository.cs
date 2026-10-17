using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Application.Abstractions;
using RoleRadar.Domain.Audit;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Notes;

namespace RoleRadar.Infrastructure.Mongo;

public sealed class MongoNoteRepository : INoteRepository, IDisposable
{
    private static readonly AsyncLocal<AmbientTransactionScope?> CurrentTransactionScope = new();

    private readonly IMongoClient _client;
    private readonly IMongoDatabase _database;
    private readonly MongoReadinessProbe _readinessProbe;
    private readonly ILogger<MongoNoteRepository> _logger;
    private readonly Func<string, CancellationToken, Task>? _faultInjectionHook;
    private readonly AppendOnlyMongoCollection _revisionsCollection;
    private readonly AppendOnlyMongoCollection _auditsCollection;
    private readonly SemaphoreSlim _indexInitLock = new(1, 1);
    private bool _indexesInitialized;

    public MongoNoteRepository(
        IMongoClient client,
        IOptions<MongoOptions> options,
        MongoReadinessProbe readinessProbe,
        ILogger<MongoNoteRepository> logger,
        Func<string, CancellationToken, Task>? faultInjectionHook = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(readinessProbe);
        ArgumentNullException.ThrowIfNull(logger);

        MongoBootstrapAndIndexes.RegisterSerializers();
        _client = client;
        _database = client.GetDatabase(options.Value.DatabaseName);
        _readinessProbe = readinessProbe;
        _logger = logger;
        _faultInjectionHook = faultInjectionHook;
        _revisionsCollection = new AppendOnlyMongoCollection(_database, MongoBootstrapAndIndexes.NoteRevisionsCollectionName);
        _auditsCollection = new AppendOnlyMongoCollection(_database, MongoBootstrapAndIndexes.AuditLogCollectionName);
    }

    public bool HasFaultInjectionHook => _faultInjectionHook is not null;

    private IMongoCollection<BsonDocument> NotesCollection =>
        _database.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.NotesCollectionName);

    private AppendOnlyMongoCollection RevisionsCollection => _revisionsCollection;

    private AppendOnlyMongoCollection AuditsCollection => _auditsCollection;

    public Task<Result<StoreReadinessStatus>> CheckReadinessAsync(CancellationToken cancellationToken = default) =>
        _readinessProbe.CheckAsync(cancellationToken);

    public async Task<Result<TResponse>> ExecuteInTransactionAsync<TResponse>(
        Func<INoteTransactionContext, CancellationToken, Task<Result<TResponse>>> work,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        await EnsureIndexesOnceAsync(cancellationToken);

        // Support ambient transaction re-entrancy when TransactionAndAuditCommandDecorator wraps a handler
        // that itself calls ExecuteInTransactionAsync.
        var existingScope = CurrentTransactionScope.Value;
        if (existingScope is not null)
        {
            return await work(existingScope.Context, cancellationToken);
        }

        using var session = await _client.StartSessionAsync(cancellationToken: cancellationToken);
        var txOptions = new TransactionOptions(
            readConcern: ReadConcern.Snapshot,
            writeConcern: WriteConcern.WMajority,
            readPreference: ReadPreference.Primary);

        Result<TResponse> finalResult = default;

        try
        {
            await session.WithTransactionAsync(
                async (sessionHandle, ct) =>
                {
                    var context = new MongoNoteTransactionContext(this, sessionHandle);
                    var scope = new AmbientTransactionScope(context);
                    CurrentTransactionScope.Value = scope;
                    try
                    {
                        var outcome = await work(context, ct);
                        finalResult = outcome;
                        if (!outcome.IsSuccess)
                        {
                            throw new RollbackOnResultFailureException();
                        }

                        return true;
                    }
                    finally
                    {
                        CurrentTransactionScope.Value = null;
                    }
                },
                txOptions,
                cancellationToken);

            return finalResult;
        }
        catch (RollbackOnResultFailureException)
        {
            return finalResult;
        }
    }

    public async Task<Result<Note>> GetByIdAsync(
        string ownerId,
        Guid noteId,
        CancellationToken cancellationToken = default)
    {
        await EnsureIndexesOnceAsync(cancellationToken);

        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("owner_id", ownerId),
            Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(noteId, GuidRepresentation.Standard)));

        var activeSession = CurrentTransactionScope.Value?.Context.Session;
        var doc = activeSession is not null
            ? await NotesCollection.Find(activeSession, filter).FirstOrDefaultAsync(cancellationToken)
            : await NotesCollection.Find(filter).FirstOrDefaultAsync(cancellationToken);

        if (doc is null)
        {
            return Result<Note>.Failure(
                ErrorCode.NotFound,
                $"Note '{noteId}' was not found for owner '{ownerId}'.");
        }

        return Result<Note>.Success(MapDocumentToNote(doc));
    }

    public async Task<Result<IReadOnlyList<NoteSearchHit>>> SearchAsync(
        NoteSearchCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        await EnsureIndexesOnceAsync(cancellationToken);

        var filters = new List<FilterDefinition<BsonDocument>>
        {
            Builders<BsonDocument>.Filter.Eq("owner_id", criteria.OwnerId)
        };

        if (!criteria.IncludeArchived)
        {
            filters.Add(Builders<BsonDocument>.Filter.Eq("status", NoteStatus.Active.ToWireString()));
        }

        if (!string.IsNullOrWhiteSpace(criteria.Area))
        {
            filters.Add(Builders<BsonDocument>.Filter.Eq("area", Note.NormalizeArea(criteria.Area)));
        }

        if (criteria.Tags is { Count: > 0 })
        {
            var normalizedTags = Note.NormalizeTags(criteria.Tags);
            if (normalizedTags.Count > 0)
            {
                filters.Add(Builders<BsonDocument>.Filter.All("tags", normalizedTags));
            }
        }

        var hasTextQuery = !string.IsNullOrWhiteSpace(criteria.Query);
        if (hasTextQuery)
        {
            filters.Add(Builders<BsonDocument>.Filter.Text(
                criteria.Query!.Trim(),
                new TextSearchOptions { CaseSensitive = false, DiacriticSensitive = false }));
        }

        var combinedFilter = Builders<BsonDocument>.Filter.And(filters);
        var limit = Math.Clamp(criteria.Limit <= 0 ? 20 : criteria.Limit, 1, 100);

        List<BsonDocument> docs;
        if (hasTextQuery)
        {
            var projection = Builders<BsonDocument>.Projection.MetaTextScore("text_score");
            var sort = Builders<BsonDocument>.Sort
                .MetaTextScore("text_score")
                .Descending("updated_at")
                .Ascending("_id");

            docs = await NotesCollection
                .Find(combinedFilter)
                .Project(projection)
                .Sort(sort)
                .Limit(limit)
                .ToListAsync(cancellationToken);
        }
        else
        {
            var sort = Builders<BsonDocument>.Sort
                .Descending("updated_at")
                .Ascending("_id");

            docs = await NotesCollection
                .Find(combinedFilter)
                .Sort(sort)
                .Limit(limit)
                .ToListAsync(cancellationToken);
        }

        var hits = new List<NoteSearchHit>(docs.Count);
        foreach (var doc in docs)
        {
            var note = MapDocumentToNote(doc);
            var score = doc.TryGetValue("text_score", out var scoreVal) && scoreVal.IsNumeric
                ? scoreVal.ToDouble()
                : 1.0;
            var snippet = BuildSnippet(note.Body, criteria.Query);
            hits.Add(new NoteSearchHit(note, score, snippet));
        }

        return Result<IReadOnlyList<NoteSearchHit>>.Success(hits);
    }

    public async Task<Result<IReadOnlyList<Note>>> GetImportedBySourceFileAsync(
        string ownerId,
        string sourceFileId,
        CancellationToken cancellationToken = default)
    {
        await EnsureIndexesOnceAsync(cancellationToken);

        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("owner_id", ownerId),
            Builders<BsonDocument>.Filter.Eq("import_metadata.source_file_id", sourceFileId.Trim()));

        var sort = Builders<BsonDocument>.Sort.Ascending("import_metadata.section_order_index");
        var activeSession = CurrentTransactionScope.Value?.Context.Session;

        var docs = activeSession is not null
            ? await NotesCollection.Find(activeSession, filter).Sort(sort).ToListAsync(cancellationToken)
            : await NotesCollection.Find(filter).Sort(sort).ToListAsync(cancellationToken);

        var notes = docs.Select(MapDocumentToNote).ToArray();
        return Result<IReadOnlyList<Note>>.Success(notes);
    }

    public async Task<Result<IReadOnlyList<NoteRevision>>> GetRevisionsAsync(
        string ownerId,
        Guid noteId,
        CancellationToken cancellationToken = default)
    {
        await EnsureIndexesOnceAsync(cancellationToken);

        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("owner_id", ownerId),
            Builders<BsonDocument>.Filter.Eq("note_id", new BsonBinaryData(noteId, GuidRepresentation.Standard)));

        var sort = Builders<BsonDocument>.Sort.Ascending("revision_number");
        var activeSession = CurrentTransactionScope.Value?.Context.Session;
        var docs = await RevisionsCollection.FindSortedAsync(filter, sort, activeSession, cancellationToken);
        var revisions = docs.Select(MapDocumentToRevision).ToArray();
        return Result<IReadOnlyList<NoteRevision>>.Success(revisions);
    }

    public async Task<Result<IReadOnlyList<AuditLogEntry>>> GetAuditEntriesAsync(
        string ownerId,
        Guid? noteId = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureIndexesOnceAsync(cancellationToken);

        var filters = new List<FilterDefinition<BsonDocument>>
        {
            Builders<BsonDocument>.Filter.Eq("owner_id", ownerId)
        };

        if (noteId is not null)
        {
            filters.Add(Builders<BsonDocument>.Filter.Eq(
                "target_note_id",
                new BsonBinaryData(noteId.Value, GuidRepresentation.Standard)));
        }

        var sort = Builders<BsonDocument>.Sort
            .Ascending("occurred_at")
            .Ascending("_id");

        var activeSession = CurrentTransactionScope.Value?.Context.Session;
        var docs = await AuditsCollection.FindSortedAsync(
            Builders<BsonDocument>.Filter.And(filters),
            sort,
            activeSession,
            cancellationToken);

        var entries = docs.Select(MapDocumentToAudit).ToArray();
        return Result<IReadOnlyList<AuditLogEntry>>.Success(entries);
    }

    private async Task EnsureIndexesOnceAsync(CancellationToken cancellationToken)
    {
        if (_indexesInitialized)
        {
            return;
        }

        await _indexInitLock.WaitAsync(cancellationToken);
        try
        {
            if (!_indexesInitialized)
            {
                await MongoBootstrapAndIndexes.EnsureIndexesAsync(_database, cancellationToken);
                _indexesInitialized = true;
            }
        }
        finally
        {
            _indexInitLock.Release();
        }
    }

    private Task InvokeFaultHookIfPresentAsync(string stage, CancellationToken cancellationToken) =>
        _faultInjectionHook is not null
            ? _faultInjectionHook(stage, cancellationToken)
            : Task.CompletedTask;

    private static string BuildSnippet(string body, string? query)
    {
        if (string.IsNullOrEmpty(body))
        {
            return string.Empty;
        }

        const int maxLen = 200;
        if (body.Length <= maxLen)
        {
            return body;
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim().Trim('"');
            var idx = body.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var start = Math.Max(0, idx - 40);
                var len = Math.Min(maxLen, body.Length - start);
                return body.Substring(start, len);
            }
        }

        return body[..maxLen];
    }

    private static BsonDocument MapNoteToDocument(Note note)
    {
        var doc = new BsonDocument
        {
            { "_id", new BsonBinaryData(note.Id, GuidRepresentation.Standard) },
            { "owner_id", note.OwnerId },
            { "title", note.Title },
            { "body", note.Body },
            { "current_body_hash", note.CurrentBodyHash },
            { "tags", new BsonArray(note.Tags) },
            { "area", note.Area },
            {
                "provenance",
                new BsonDocument
                {
                    { "kind", note.Provenance.Kind.ToWireString() },
                    { "date", note.Provenance.Date },
                    { "qualifier", note.Provenance.Qualifier is not null ? new BsonString(note.Provenance.Qualifier) : BsonNull.Value }
                }
            },
            {
                "import_metadata",
                note.ImportMetadata is not null
                    ? new BsonDocument
                    {
                        { "import_key", note.ImportMetadata.ImportKey },
                        { "source_file_id", note.ImportMetadata.SourceFileId },
                        { "heading_path", note.ImportMetadata.HeadingPath },
                        { "occurrence_index", note.ImportMetadata.OccurrenceIndex },
                        { "section_order_index", note.ImportMetadata.SectionOrderIndex },
                        { "last_imported_body_hash", note.ImportMetadata.LastImportedBodyHash }
                    }
                    : BsonNull.Value
            },
            { "status", note.Status.ToWireString() },
            { "version_token", new BsonBinaryData(note.VersionToken, GuidRepresentation.Standard) },
            { "revision_count", note.RevisionCount },
            { "created_at", new BsonDateTime(note.CreatedAtUtc) },
            { "updated_at", new BsonDateTime(note.UpdatedAtUtc) }
        };

        return doc;
    }

    private static Note MapDocumentToNote(BsonDocument doc)
    {
        var provDoc = doc["provenance"].AsBsonDocument;
        var provenance = new ProvenanceMarker(
            ProvenanceKindExtensions.FromWireString(provDoc["kind"].AsString),
            provDoc["date"].AsString,
            provDoc.TryGetValue("qualifier", out var qVal) && !qVal.IsBsonNull ? qVal.AsString : null);

        NoteImportMetadata? importMeta = null;
        if (doc.TryGetValue("import_metadata", out var importVal) && !importVal.IsBsonNull)
        {
            var metaDoc = importVal.AsBsonDocument;
            importMeta = NoteImportMetadata.Rehydrate(
                metaDoc["import_key"].AsString,
                metaDoc["source_file_id"].AsString,
                metaDoc["heading_path"].AsString,
                metaDoc["occurrence_index"].AsInt32,
                metaDoc["section_order_index"].AsInt32,
                metaDoc["last_imported_body_hash"].AsString);
        }

        var tags = doc["tags"].AsBsonArray.Select(t => t.AsString).ToArray();

        return Note.Rehydrate(
            id: doc["_id"].AsBsonBinaryData.ToGuid(GuidRepresentation.Standard),
            ownerId: doc["owner_id"].AsString,
            title: doc["title"].AsString,
            body: doc["body"].AsString,
            currentBodyHash: doc["current_body_hash"].AsString,
            tags: tags,
            area: doc["area"].AsString,
            provenance: provenance,
            importMetadata: importMeta,
            status: NoteStatusExtensions.FromWireString(doc["status"].AsString),
            versionToken: doc["version_token"].AsBsonBinaryData.ToGuid(GuidRepresentation.Standard),
            revisionCount: doc["revision_count"].AsInt32,
            createdAtUtc: doc["created_at"].ToUniversalTime(),
            updatedAtUtc: doc["updated_at"].ToUniversalTime());
    }

    private static BsonDocument MapRevisionToDocument(NoteRevision revision) =>
        new()
        {
            { "_id", new BsonBinaryData(revision.Id, GuidRepresentation.Standard) },
            { "owner_id", revision.OwnerId },
            { "note_id", new BsonBinaryData(revision.NoteId, GuidRepresentation.Standard) },
            { "revision_number", revision.RevisionNumber },
            { "reason", revision.Reason },
            { "prior_body", revision.PriorBody },
            { "prior_body_hash", revision.PriorBodyHash },
            { "prior_version_token", new BsonBinaryData(revision.PriorVersionToken, GuidRepresentation.Standard) },
            { "created_at", new BsonDateTime(revision.CreatedAtUtc) }
        };

    private static NoteRevision MapDocumentToRevision(BsonDocument doc) =>
        NoteRevision.Rehydrate(
            id: doc["_id"].AsBsonBinaryData.ToGuid(GuidRepresentation.Standard),
            ownerId: doc["owner_id"].AsString,
            noteId: doc["note_id"].AsBsonBinaryData.ToGuid(GuidRepresentation.Standard),
            revisionNumber: doc["revision_number"].AsInt32,
            reason: doc["reason"].AsString,
            priorBody: doc["prior_body"].AsString,
            priorBodyHash: doc["prior_body_hash"].AsString,
            priorVersionToken: doc["prior_version_token"].AsBsonBinaryData.ToGuid(GuidRepresentation.Standard),
            createdAtUtc: doc["created_at"].ToUniversalTime());

    private static BsonDocument MapAuditToDocument(AuditLogEntry entry) =>
        new()
        {
            { "_id", new BsonBinaryData(entry.Id, GuidRepresentation.Standard) },
            { "owner_id", entry.OwnerId },
            { "actor", entry.Actor },
            { "operation", entry.Operation },
            { "target_note_id", new BsonBinaryData(entry.TargetNoteId, GuidRepresentation.Standard) },
            {
                "note_revision_id",
                entry.NoteRevisionId is not null
                    ? new BsonBinaryData(entry.NoteRevisionId.Value, GuidRepresentation.Standard)
                    : BsonNull.Value
            },
            { "appended_text", entry.AppendedText is not null ? new BsonString(entry.AppendedText) : BsonNull.Value },
            { "resulting_version_token", new BsonBinaryData(entry.ResultingVersionToken, GuidRepresentation.Standard) },
            { "resolved_default_date", entry.ResolvedDefaultDate is not null ? new BsonString(entry.ResolvedDefaultDate) : BsonNull.Value },
            { "occurred_at", new BsonDateTime(entry.OccurredAtUtc) },
            {
                "before_summary",
                entry.BeforeSummary is not null
                    ? MapSummaryToDocument(entry.BeforeSummary)
                    : BsonNull.Value
            },
            { "after_summary", MapSummaryToDocument(entry.AfterSummary) }
        };

    private static BsonDocument MapSummaryToDocument(AuditContentSummary summary) =>
        new()
        {
            { "body_hash", summary.BodyHash },
            { "byte_length", summary.ByteLength },
            { "status", summary.Status },
            { "excerpt", summary.Excerpt }
        };

    private static AuditContentSummary MapDocumentToSummary(BsonDocument doc) =>
        new(
            doc["body_hash"].AsString,
            doc["byte_length"].AsInt32,
            doc["status"].AsString,
            doc["excerpt"].AsString);

    private static AuditLogEntry MapDocumentToAudit(BsonDocument doc) =>
        AuditLogEntry.Rehydrate(
            id: doc["_id"].AsBsonBinaryData.ToGuid(GuidRepresentation.Standard),
            ownerId: doc["owner_id"].AsString,
            actor: doc["actor"].AsString,
            operation: doc["operation"].AsString,
            targetNoteId: doc["target_note_id"].AsBsonBinaryData.ToGuid(GuidRepresentation.Standard),
            noteRevisionId: doc.TryGetValue("note_revision_id", out var revVal) && !revVal.IsBsonNull
                ? revVal.AsBsonBinaryData.ToGuid(GuidRepresentation.Standard)
                : null,
            appendedText: doc.TryGetValue("appended_text", out var appVal) && !appVal.IsBsonNull
                ? appVal.AsString
                : null,
            resultingVersionToken: doc["resulting_version_token"].AsBsonBinaryData.ToGuid(GuidRepresentation.Standard),
            resolvedDefaultDate: doc.TryGetValue("resolved_default_date", out var defDateVal) && !defDateVal.IsBsonNull
                ? defDateVal.AsString
                : null,
            occurredAtUtc: doc["occurred_at"].ToUniversalTime(),
            beforeSummary: doc.TryGetValue("before_summary", out var beforeVal) && !beforeVal.IsBsonNull
                ? MapDocumentToSummary(beforeVal.AsBsonDocument)
                : null,
            afterSummary: MapDocumentToSummary(doc["after_summary"].AsBsonDocument));

    private sealed class AmbientTransactionScope(MongoNoteTransactionContext context)
    {
        public MongoNoteTransactionContext Context { get; } = context;
    }

    private sealed class RollbackOnResultFailureException : Exception;

    private sealed class MongoNoteTransactionContext(
        MongoNoteRepository parent,
        IClientSessionHandle session) : INoteTransactionContext
    {
        public IClientSessionHandle Session { get; } = session;

        public async Task<Result<NoteWriteOutcome>> CreateAsync(
            Note note,
            AuditLogEntry auditEntry,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(note);
            ArgumentNullException.ThrowIfNull(auditEntry);

            await parent.NotesCollection.InsertOneAsync(
                Session,
                MapNoteToDocument(note),
                cancellationToken: cancellationToken);

            await parent.InvokeFaultHookIfPresentAsync("before_audit_insert", cancellationToken);

            await parent.AuditsCollection.InsertOneAsync(
                Session,
                MapAuditToDocument(auditEntry),
                cancellationToken: cancellationToken);

            return Result<NoteWriteOutcome>.Success(new NoteWriteOutcome(note, auditEntry));
        }

        public async Task<Result<NoteWriteOutcome>> AppendAsync(
            string ownerId,
            Guid noteId,
            string textToAppend,
            string actor,
            DateTime occurredAtUtc,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(textToAppend))
            {
                return Result<NoteWriteOutcome>.Failure(
                    ErrorCode.ValidationError,
                    "Text to append cannot be empty.");
            }

            var filter = Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("owner_id", ownerId),
                Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(noteId, GuidRepresentation.Standard)));

            var existingDoc = await parent.NotesCollection
                .Find(Session, filter)
                .FirstOrDefaultAsync(cancellationToken);

            if (existingDoc is null)
            {
                return Result<NoteWriteOutcome>.Failure(
                    ErrorCode.NotFound,
                    $"Note '{noteId}' was not found for owner '{ownerId}'.");
            }

            var note = MapDocumentToNote(existingDoc);
            var beforeSummary = AuditContentSummary.FromNote(note);

            // Atomic inside a snapshot transaction (read, append, hash, update) with transient-error retry via WithTransactionAsync;
            // no caller version token required and no NoteRevision snapshot created.
            note.AppendBody(textToAppend, occurredAtUtc);
            var afterSummary = AuditContentSummary.FromNote(note);

            var updateDef = Builders<BsonDocument>.Update
                .Set("body", note.Body)
                .Set("current_body_hash", note.CurrentBodyHash)
                .Set("version_token", new BsonBinaryData(note.VersionToken, GuidRepresentation.Standard))
                .Set("updated_at", new BsonDateTime(note.UpdatedAtUtc));

            await parent.NotesCollection.UpdateOneAsync(
                Session,
                filter,
                updateDef,
                cancellationToken: cancellationToken);

            await parent.InvokeFaultHookIfPresentAsync("before_audit_insert", cancellationToken);

            var auditEntry = AuditLogEntry.CreateForAppend(
                ownerId: ownerId,
                actor: actor,
                targetNoteId: note.Id,
                appendedText: textToAppend,
                resultingVersionToken: note.VersionToken,
                occurredAtUtc: note.UpdatedAtUtc,
                beforeSummary: beforeSummary,
                afterSummary: afterSummary);

            await parent.AuditsCollection.InsertOneAsync(
                Session,
                MapAuditToDocument(auditEntry),
                cancellationToken: cancellationToken);

            return Result<NoteWriteOutcome>.Success(new NoteWriteOutcome(note, auditEntry));
        }

        public async Task<Result<NoteWriteOutcome>> ReplaceSectionAsync(
            Note updatedNote,
            Guid expectedVersionToken,
            NoteRevision priorBodyRevision,
            AuditLogEntry auditEntry,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(updatedNote);
            ArgumentNullException.ThrowIfNull(priorBodyRevision);
            ArgumentNullException.ThrowIfNull(auditEntry);

            var idFilter = Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("owner_id", updatedNote.OwnerId),
                Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(updatedNote.Id, GuidRepresentation.Standard)));

            var versionedFilter = Builders<BsonDocument>.Filter.And(
                idFilter,
                Builders<BsonDocument>.Filter.Eq("version_token", new BsonBinaryData(expectedVersionToken, GuidRepresentation.Standard)));

            var updateDef = Builders<BsonDocument>.Update
                .Set("title", updatedNote.Title)
                .Set("body", updatedNote.Body)
                .Set("current_body_hash", updatedNote.CurrentBodyHash)
                .Set("revision_count", updatedNote.RevisionCount)
                .Set("version_token", new BsonBinaryData(updatedNote.VersionToken, GuidRepresentation.Standard))
                .Set("updated_at", new BsonDateTime(updatedNote.UpdatedAtUtc));

            var updateResult = await parent.NotesCollection.UpdateOneAsync(
                Session,
                versionedFilter,
                updateDef,
                cancellationToken: cancellationToken);

            if (updateResult.MatchedCount == 0)
            {
                var currentDoc = await parent.NotesCollection.Find(Session, idFilter).FirstOrDefaultAsync(cancellationToken);
                if (currentDoc is null)
                {
                    return Result<NoteWriteOutcome>.Failure(
                        ErrorCode.NotFound,
                        $"Note '{updatedNote.Id}' was not found.");
                }

                var currentToken = currentDoc["version_token"].AsBsonBinaryData.ToGuid(GuidRepresentation.Standard);
                return Result<NoteWriteOutcome>.Failure(
                    ErrorCode.StaleVersionToken,
                    $"Note '{updatedNote.Id}' was modified concurrently. Expected version token '{expectedVersionToken}', but current version token is '{currentToken}'.",
                    new Dictionary<string, object?>
                    {
                        ["currentVersionToken"] = currentToken.ToString()
                    });
            }

            await parent.RevisionsCollection.InsertOneAsync(
                Session,
                MapRevisionToDocument(priorBodyRevision),
                cancellationToken: cancellationToken);

            await parent.InvokeFaultHookIfPresentAsync("before_audit_insert", cancellationToken);

            await parent.AuditsCollection.InsertOneAsync(
                Session,
                MapAuditToDocument(auditEntry),
                cancellationToken: cancellationToken);

            return Result<NoteWriteOutcome>.Success(new NoteWriteOutcome(updatedNote, auditEntry, priorBodyRevision));
        }

        public async Task<Result<NoteWriteOutcome>> ArchiveAsync(
            Note archivedNote,
            Guid expectedVersionToken,
            AuditLogEntry auditEntry,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(archivedNote);
            ArgumentNullException.ThrowIfNull(auditEntry);

            var versionedFilter = Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("owner_id", archivedNote.OwnerId),
                Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(archivedNote.Id, GuidRepresentation.Standard)),
                Builders<BsonDocument>.Filter.Eq("version_token", new BsonBinaryData(expectedVersionToken, GuidRepresentation.Standard)));

            var updateDef = Builders<BsonDocument>.Update
                .Set("status", archivedNote.Status.ToWireString())
                .Set("version_token", new BsonBinaryData(archivedNote.VersionToken, GuidRepresentation.Standard))
                .Set("updated_at", new BsonDateTime(archivedNote.UpdatedAtUtc));

            var updateResult = await parent.NotesCollection.UpdateOneAsync(
                Session,
                versionedFilter,
                updateDef,
                cancellationToken: cancellationToken);

            if (updateResult.MatchedCount == 0)
            {
                return Result<NoteWriteOutcome>.Failure(
                    ErrorCode.StaleVersionToken,
                    $"Note '{archivedNote.Id}' was modified concurrently while archiving.");
            }

            await parent.InvokeFaultHookIfPresentAsync("before_audit_insert", cancellationToken);

            await parent.AuditsCollection.InsertOneAsync(
                Session,
                MapAuditToDocument(auditEntry),
                cancellationToken: cancellationToken);

            return Result<NoteWriteOutcome>.Success(new NoteWriteOutcome(archivedNote, auditEntry));
        }

        public async Task<Result<NoteWriteOutcome>> UpsertImportedSectionAsync(
            UpsertImportedSectionRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            var bodyHash = Sha256Hasher.ComputeLowerHex(request.Body);
            var importMeta = NoteImportMetadata.Create(
                request.SourceFileId,
                request.HeadingPath,
                request.OccurrenceIndex,
                request.SectionOrderIndex,
                bodyHash);

            var keyFilter = Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("owner_id", request.OwnerId),
                Builders<BsonDocument>.Filter.Eq("import_metadata.import_key", importMeta.ImportKey));

            var existingDoc = await parent.NotesCollection
                .Find(Session, keyFilter)
                .FirstOrDefaultAsync(cancellationToken);

            if (existingDoc is null)
            {
                var createdNote = Note.CreateImported(
                    ownerId: request.OwnerId,
                    title: request.Title,
                    body: request.Body,
                    tags: request.Tags,
                    area: request.Area,
                    provenance: request.Provenance,
                    importMetadata: importMeta,
                    createdAtUtc: request.OccurredAtUtc);

                var createAudit = AuditLogEntry.CreateForImportCreate(
                    ownerId: request.OwnerId,
                    actor: request.Actor,
                    targetNoteId: createdNote.Id,
                    resultingVersionToken: createdNote.VersionToken,
                    resolvedDefaultDate: request.ResolvedDefaultDate,
                    occurredAtUtc: createdNote.CreatedAtUtc,
                    afterSummary: AuditContentSummary.FromNote(createdNote));

                await parent.NotesCollection.InsertOneAsync(
                    Session,
                    MapNoteToDocument(createdNote),
                    cancellationToken: cancellationToken);

                await parent.InvokeFaultHookIfPresentAsync("before_audit_insert", cancellationToken);

                await parent.AuditsCollection.InsertOneAsync(
                    Session,
                    MapAuditToDocument(createAudit),
                    cancellationToken: cancellationToken);

                return Result<NoteWriteOutcome>.Success(new NoteWriteOutcome(createdNote, createAudit));
            }

            var storedNote = MapDocumentToNote(existingDoc);
            if (storedNote.HasPostImportEdits)
            {
                return Result<NoteWriteOutcome>.Failure(
                    ErrorCode.ImportConflict,
                    $"Note '{storedNote.Id}' (importKey '{importMeta.ImportKey}') was modified after its last import and cannot be overwritten.",
                    new Dictionary<string, object?>
                    {
                        ["noteId"] = storedNote.Id.ToString(),
                        ["importKey"] = importMeta.ImportKey,
                        ["title"] = storedNote.Title
                    });
            }

            if (string.Equals(storedNote.CurrentBodyHash, bodyHash, StringComparison.Ordinal))
            {
                // Unchanged body: return existing note without creating a duplicate or redundant revision
                var noOpSummary = AuditContentSummary.FromNote(storedNote);
                var unchangedAudit = AuditLogEntry.CreateForImportCreate(
                    ownerId: request.OwnerId,
                    actor: request.Actor,
                    targetNoteId: storedNote.Id,
                    resultingVersionToken: storedNote.VersionToken,
                    resolvedDefaultDate: request.ResolvedDefaultDate,
                    occurredAtUtc: request.OccurredAtUtc,
                    afterSummary: noOpSummary);
                return Result<NoteWriteOutcome>.Success(new NoteWriteOutcome(storedNote, unchangedAudit));
            }

            var beforeSummary = AuditContentSummary.FromNote(storedNote);
            var priorRevision = storedNote.ReplaceBodyWithRevision(
                newBody: request.Body,
                reason: NoteRevision.ReasonImportOverwrite,
                occurredAtUtc: request.OccurredAtUtc,
                updatedTitle: request.Title,
                updatedProvenance: request.Provenance,
                updatedSectionOrderIndex: request.SectionOrderIndex,
                updateLastImportedHash: true);

            var afterSummary = AuditContentSummary.FromNote(storedNote);
            var overwriteAudit = AuditLogEntry.CreateForImportOverwrite(
                ownerId: request.OwnerId,
                actor: request.Actor,
                targetNoteId: storedNote.Id,
                noteRevisionId: priorRevision.Id,
                resultingVersionToken: storedNote.VersionToken,
                resolvedDefaultDate: request.ResolvedDefaultDate,
                occurredAtUtc: storedNote.UpdatedAtUtc,
                beforeSummary: beforeSummary,
                afterSummary: afterSummary);

            BsonValue qualifierValue = storedNote.Provenance.Qualifier is not null
                ? new BsonString(storedNote.Provenance.Qualifier)
                : BsonNull.Value;

            var updateDef = Builders<BsonDocument>.Update
                .Set("title", storedNote.Title)
                .Set("body", storedNote.Body)
                .Set("current_body_hash", storedNote.CurrentBodyHash)
                .Set("provenance.kind", storedNote.Provenance.Kind.ToWireString())
                .Set("provenance.date", storedNote.Provenance.Date)
                .Set("provenance.qualifier", qualifierValue)
                .Set("import_metadata.section_order_index", storedNote.ImportMetadata!.SectionOrderIndex)
                .Set("import_metadata.last_imported_body_hash", storedNote.ImportMetadata.LastImportedBodyHash)
                .Set("revision_count", storedNote.RevisionCount)
                .Set("version_token", new BsonBinaryData(storedNote.VersionToken, GuidRepresentation.Standard))
                .Set("updated_at", new BsonDateTime(storedNote.UpdatedAtUtc));

            await parent.NotesCollection.UpdateOneAsync(
                Session,
                keyFilter,
                updateDef,
                cancellationToken: cancellationToken);

            await parent.RevisionsCollection.InsertOneAsync(
                Session,
                MapRevisionToDocument(priorRevision),
                cancellationToken: cancellationToken);

            await parent.InvokeFaultHookIfPresentAsync("before_audit_insert", cancellationToken);

            await parent.AuditsCollection.InsertOneAsync(
                Session,
                MapAuditToDocument(overwriteAudit),
                cancellationToken: cancellationToken);

            return Result<NoteWriteOutcome>.Success(new NoteWriteOutcome(storedNote, overwriteAudit, priorRevision));
        }
    }

    public void Dispose() => _indexInitLock.Dispose();
}
