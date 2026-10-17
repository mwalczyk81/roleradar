# Contract: `INoteStore` Single Write Port

**Feature**: `001-memory-replacement`  
**Layer**: `RoleRadar.Application.Abstractions`  
**ADR Reference**: `docs/adr/0002-inote-store-single-write-port.md`

---

## Port Invariants

1. **Sole Persistence Seam**: `INoteStore` is the **only** storage abstraction in Feature 001 (Constitution Principle IV).
2. **Transactional Write Units**: Every mutating method on `INoteStore` executes inside a single MongoDB multi-document transaction (`IClientSessionHandle.WithTransactionAsync`) across `notes`, `note_revisions` (when applicable), and `audit_log`. If any step throws or fails, the entire transaction rolls back so no partial write ever commits.
3. **Append-Only Revisions & Audit**: `INoteStore` has **no** methods to update or delete `NoteRevision` or `AuditLogEntry` records, and **no** method for agent-facing hard deletion of a `Note`.
4. **Fast Offline Readiness Check**: `CheckReadinessAsync` uses a short connection/server-selection timeout (<=3 seconds, well under the 5-second ceiling in `FR-019`) and returns a structured readiness status that callers translate into an actionable start-service message (`pwsh ./scripts/start-services.ps1` / `docker compose up -d`).

---

## C# Interface Signature

```csharp
namespace RoleRadar.Application.Abstractions;

public interface INoteStore
{
    /// <summary>
    /// Verifies that the backing datastore is reachable and its replica set is ready for transactions within the timeout budget.
    /// </summary>
    Task<StoreReadinessResult> CheckReadinessAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically inserts a new Note and its corresponding AuditLogEntry ("note_create") in a single transaction.
    /// </summary>
    Task<NoteWriteResult> CreateAsync(
        CreateNoteStoreCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a single Note by owner and ID (including archived notes), or null if not found.
    /// </summary>
    Task<Note?> GetByIdAsync(
        string ownerId,
        Guid noteId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically appends markdown text to an existing Note on the server side without requiring a caller version token,
    /// rotates the note's version token, and inserts an AuditLogEntry ("note_append") recording the appended text and
    /// resulting version token. MUST NOT insert a NoteRevision snapshot.
    /// </summary>
    Task<NoteWriteResult> AppendAsync(
        AppendNoteStoreCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically verifies the caller's expected VersionToken, inserts a full prior-body NoteRevision ("replace-section"),
    /// updates the Note body and rotates its VersionToken, and inserts an AuditLogEntry ("note_replace_section") referencing
    /// the new NoteRevisionId. Throws ConcurrencyConflictException if expectedVersionToken does not match.
    /// </summary>
    Task<NoteWriteResult> ReplaceSectionAsync(
        ReplaceSectionStoreCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically marks an existing Note as Archived without altering its body or revisions, rotates its VersionToken,
    /// and inserts an AuditLogEntry ("note_archive").
    /// </summary>
    Task<NoteWriteResult> ArchiveAsync(
        ArchiveNoteStoreCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches notes for the given owner using case-insensitive, whole-word stemmed text search (supporting quoted phrases),
    /// ANDed tag and area filters, and archived-inclusion toggle, ordered by relevance descending, then updated_at descending, then _id ascending.
    /// </summary>
    Task<IReadOnlyList<NoteSearchHit>> SearchAsync(
        NoteSearchQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all previously imported notes for a given owner and source file ID, ordered by section_order_index ascending.
    /// Used by the markdown import service to detect unchanged sections, post-import edit conflicts, and orphaned sections.
    /// </summary>
    Task<IReadOnlyList<Note>> GetImportedBySourceFileAsync(
        string ownerId,
        string sourceFileId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically persists an imported section:
    /// - If creating a new imported note: inserts Note + AuditLogEntry ("import_create", with resolved_default_date).
    /// - If overwriting an existing imported note whose current_body_hash == last_imported_body_hash: inserts a full prior-body
    ///   NoteRevision ("import-overwrite"), updates Note (body, hashes, provenance, section_order_index, rotated version_token),
    ///   and inserts AuditLogEntry ("import_overwrite", referencing NoteRevisionId and resolved_default_date).
    /// </summary>
    Task<NoteWriteResult> UpsertImportedSectionAsync(
        UpsertImportedSectionStoreCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the append-only revision history for a Note, ordered by revision_number ascending.
    /// </summary>
    Task<IReadOnlyList<NoteRevision>> GetRevisionsAsync(
        string ownerId,
        Guid noteId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the append-only audit log entries for a Note (or all notes for the owner when noteId is null),
    /// ordered chronologically.
    /// </summary>
    Task<IReadOnlyList<AuditLogEntry>> GetAuditEntriesAsync(
        string ownerId,
        Guid? noteId = null,
        CancellationToken cancellationToken = default);
}
```

---

## Contract Test Suite Requirements (`RoleRadar.Tests.Contract`)

Per Constitution Principle II, `INoteStore` MUST have a dedicated contract test suite verifying:
1. **Atomicity / Fault Injection**: When a fault is injected mid-transaction (e.g., before `audit_log` insert), neither the `notes` mutation nor the `note_revisions` insert is committed.
2. **Append Invariant**: `AppendAsync` appends atomically without a caller version token, records `appended_text` and `resulting_version_token` in `audit_log`, and leaves `note_revisions` count at `0`.
3. **Replace-Section Concurrency & Revision Invariant**: `ReplaceSectionAsync` with matching `VersionToken` writes a full prior-body `NoteRevision` and links `note_revision_id` in `audit_log`; calling with a stale `VersionToken` throws `ConcurrencyConflictException` and writes zero records.
4. **Import Conflict & Revision Invariant**: `UpsertImportedSectionAsync` creates a `NoteRevision` on overwrite, and rejects overwriting if `current_body_hash != last_imported_body_hash`.
5. **Append-Only Method Reflection Check**: Reflection over the concrete `MongoNoteStore` implementation confirms no public or private method executes `Update*`, `Replace*`, or `Delete*` against `note_revisions` or `audit_log`, and no method executes `Delete*` against `notes`.
