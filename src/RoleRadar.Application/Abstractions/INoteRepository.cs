using RoleRadar.Domain.Audit;
using RoleRadar.Domain.Notes;

namespace RoleRadar.Application.Abstractions;

public sealed record StoreReadinessStatus(
    bool IsReady,
    string Endpoint,
    string ReplicaSet,
    double LatencyMs,
    string? RemediationMessage = null);

public sealed record NoteSearchCriteria(
    string OwnerId,
    string? Query = null,
    IReadOnlyList<string>? Tags = null,
    string? Area = null,
    bool IncludeArchived = false,
    int Limit = 20);

public sealed record NoteSearchHit(
    Note Note,
    double Score,
    string Snippet);

public sealed record NoteWriteOutcome(
    Note Note,
    AuditLogEntry AuditEntry,
    NoteRevision? Revision = null);

public sealed record UpsertImportedSectionRequest(
    string OwnerId,
    string SourceFileId,
    string HeadingPath,
    int OccurrenceIndex,
    int SectionOrderIndex,
    string Title,
    string Body,
    IReadOnlyList<string> Tags,
    string Area,
    ProvenanceMarker Provenance,
    string ResolvedDefaultDate,
    string Actor,
    DateTime OccurredAtUtc);

public interface INoteRepository
{
    /// <summary>
    /// Verifies that the backing MongoDB replica set is reachable and ready for transactions within &lt;=3s (under the 5s FR-019 budget).
    /// Returns ErrorCode.DatastoreOffline with the exact start-services command when unreachable.
    /// </summary>
    Task<Result<StoreReadinessStatus>> CheckReadinessAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes the supplied work within a single MongoDB multi-document ACID transaction across notes, note_revisions, and audit_log,
    /// rolling back completely if the returned Result is a failure or an unexpected fault occurs.
    /// Invoked by TransactionAndAuditCommandDecorator.
    /// </summary>
    Task<Result<TResponse>> ExecuteInTransactionAsync<TResponse>(
        Func<INoteTransactionContext, CancellationToken, Task<Result<TResponse>>> work,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a single Note by owner and ID (including archived notes), or ResultError(ErrorCode.NotFound).
    /// </summary>
    Task<Result<Note>> GetByIdAsync(
        string ownerId,
        Guid noteId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches notes for the given owner using case-insensitive, whole-word stemmed text search (supporting quoted phrases),
    /// ANDed tag and area filters, and archived-inclusion toggle, ordered by relevance descending, then updated_at descending, then _id ascending.
    /// </summary>
    Task<Result<IReadOnlyList<NoteSearchHit>>> SearchAsync(
        NoteSearchCriteria criteria,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all previously imported notes for a given owner and source file ID, ordered by section_order_index ascending.
    /// </summary>
    Task<Result<IReadOnlyList<Note>>> GetImportedBySourceFileAsync(
        string ownerId,
        string sourceFileId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the append-only revision history for a Note, ordered by revision_number ascending.
    /// </summary>
    Task<Result<IReadOnlyList<NoteRevision>>> GetRevisionsAsync(
        string ownerId,
        Guid noteId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the append-only audit log entries for a Note (or all notes for the owner when noteId is null),
    /// ordered chronologically.
    /// </summary>
    Task<Result<IReadOnlyList<AuditLogEntry>>> GetAuditEntriesAsync(
        string ownerId,
        Guid? noteId = null,
        CancellationToken cancellationToken = default);
}

public interface INoteTransactionContext
{
    /// <summary>
    /// Atomically inserts a new Note and its AuditLogEntry ("note_create") inside the active transaction.
    /// </summary>
    Task<Result<NoteWriteOutcome>> CreateAsync(
        Note note,
        AuditLogEntry auditEntry,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically appends markdown text to an existing Note on the server side without requiring a caller version token,
    /// rotates the note's version token, and inserts an AuditLogEntry ("note_append") recording the appended text and
    /// resulting version token. MUST NOT insert a NoteRevision snapshot.
    /// </summary>
    Task<Result<NoteWriteOutcome>> AppendAsync(
        string ownerId,
        Guid noteId,
        string textToAppend,
        string actor,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically verifies the caller's expected VersionToken (returning ErrorCode.StaleVersionToken on mismatch),
    /// inserts a full prior-body NoteRevision ("replace-section"), updates the Note body and rotates its VersionToken,
    /// and inserts an AuditLogEntry ("note_replace_section") referencing the new NoteRevisionId.
    /// </summary>
    Task<Result<NoteWriteOutcome>> ReplaceSectionAsync(
        Note updatedNote,
        Guid expectedVersionToken,
        NoteRevision priorBodyRevision,
        AuditLogEntry auditEntry,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically marks an existing Note as Archived without altering its body or revisions, rotates its VersionToken,
    /// and inserts an AuditLogEntry ("note_archive").
    /// Note: The MCP tool and ArchiveNoteCommand take only the Note ID (no caller VersionToken is required);
    /// ArchiveNoteCommandHandler reads the Note inside the active transaction and passes its current note.VersionToken
    /// as expectedVersionToken to guard the in-transaction update.
    /// </summary>
    Task<Result<NoteWriteOutcome>> ArchiveAsync(
        Note archivedNote,
        Guid expectedVersionToken,
        AuditLogEntry auditEntry,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically persists an imported section inside the active transaction:
    /// - If creating: inserts Note + AuditLogEntry ("import_create", with resolved_default_date).
    /// - If overwriting an existing imported note whose current_body_hash == last_imported_body_hash: inserts a full prior-body
    ///   NoteRevision ("import-overwrite"), updates Note, and inserts AuditLogEntry ("import_overwrite", referencing NoteRevisionId and resolved_default_date).
    /// - If current_body_hash != last_imported_body_hash: returns ErrorCode.ImportConflict without modifying the stored note.
    /// </summary>
    Task<Result<NoteWriteOutcome>> UpsertImportedSectionAsync(
        UpsertImportedSectionRequest request,
        CancellationToken cancellationToken = default);
}
