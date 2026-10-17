# Contract: `INoteRepository` Aggregate Port & Handler/Decorator Pipeline

**Feature**: `001-memory-replacement`
**Layer**: `RoleRadar.Application.Abstractions`
**ADR References**:
- [ADR-0002: `INoteRepository` as the Aggregate-Level Transactional Write Port](../../../docs/adr/0002-inote-repository-single-write-port.md)
- [ADR-0006: Command & Query Handlers with Explicit Decorators Instead of a Mediator Library](../../../docs/adr/0006-handlers-with-decorators-no-mediator.md)

---

## 1. Architectural Invariants (Constitution v1.1.0 Principles III & IV)

1. **Aggregate-Level Repository (`INoteRepository`)**: `INoteRepository` is the sole persistence seam in Feature 001. Its methods express domain use cases covering a `Note`, its `NoteRevision` snapshots, and the `AuditLogEntry` written in the same multi-document transaction. No generic `IRepository<T>` over the database driver is permitted.
2. **Plain Command & Query Handlers**: Each use case is implemented as a plain handler class implementing `ICommandHandler<TCommand, TResponse>` or `IQueryHandler<TQuery, TResponse>`, injected directly into `RoleRadar.Mcp.Stdio` tools and `RoleRadar.Cli` commands:
   - **Commands**: `CreateNoteCommand`, `AppendToNoteCommand`, `ReplaceNoteSectionCommand`, `ArchiveNoteCommand`, `ImportMarkdownCommand`.
   - **Queries**: `GetNoteQuery`, `SearchNotesQuery`.
3. **Mandatory Decorator Pipeline**:
   - Every `ICommandHandler<TCommand, TResponse>` registration in DI is wrapped by `TransactionAndAuditCommandDecorator<TCommand, TResponse>` (which validates the command, checks datastore readiness, opens the atomic transaction scope, invokes the inner handler, and verifies/commits the audit entry) and outer `LoggingCommandDecorator<TCommand, TResponse>`.
   - Every `IQueryHandler<TQuery, TResponse>` registration is wrapped by `LoggingQueryDecorator<TQuery, TResponse>`.
   - An architecture test in `RoleRadar.Tests.Architecture` inspects the DI container registrations and fails the build if any `ICommandHandler<,>` is not wrapped by `TransactionAndAuditCommandDecorator<,>`.
4. **Typed Failure Results (`Result<T>`)**: Expected failures (`NotFound`, `StaleVersionToken`, `SectionNotFound`, `AmbiguousHeading`, `ImportConflict`, `InvalidUtf8`, `DatastoreOffline`, `ValidationError`) are returned as `Result<T>` values, never thrown as exceptions.
5. **Append-Only Revisions & Audit**: `INoteRepository` has **no** methods to update or delete `NoteRevision` or `AuditLogEntry` records, and **no** method for agent-facing hard deletion of a `Note`.

---

## 2. Typed Result & Handler Interfaces

```csharp
namespace RoleRadar.Application.Abstractions;

public enum ErrorCode
{
    ValidationError,
    NotFound,
    StaleVersionToken,
    SectionNotFound,
    AmbiguousHeading,
    ImportConflict,
    InvalidUtf8,
    DatastoreOffline
}

public sealed record ResultError(
    ErrorCode Code,
    string Message,
    IReadOnlyDictionary<string, object?>? Metadata = null);

public readonly record struct Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public ResultError? Error { get; }

    public static Result<T> Success(T value) => new(true, value, null);
    public static Result<T> Failure(ResultError error) => new(false, default, error);
}

public interface ICommand<TResponse>;
public interface IQuery<TResponse>;

public interface ICommandHandler<in TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    Task<Result<TResponse>> HandleAsync(
        TCommand command,
        CancellationToken cancellationToken = default);
}

public interface IQueryHandler<in TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    Task<Result<TResponse>> HandleAsync(
        TQuery query,
        CancellationToken cancellationToken = default);
}
```

---

## 3. `INoteRepository` C# Interface Signature

```csharp
namespace RoleRadar.Application.Abstractions;

public interface INoteRepository
{
    /// <summary>
    /// Verifies that the backing MongoDB replica set is reachable and ready for transactions within <=3s (under the 5s FR-019 budget).
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
    /// Retrieves a single Note aggregate in full by owner and ID (including archived notes), or ResultError(ErrorCode.NotFound).
    /// Character-based pagination (offset, maxChars default 200,000, totalChars, truncated, nextOffset, with UTF-16 surrogate-pair
    /// boundary protection) is applied by GetNoteQueryHandler when mapping Note to NoteDetailDto.
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
```

---

## 4. Contract & Architecture Test Suite Requirements

Per Constitution v1.1.0 Principles II, III, and IV:
1. **Decorator Registration Architecture Test (`RoleRadar.Tests.Architecture`)**: Verifies that every concrete class implementing `ICommandHandler<,>` in `RoleRadar.Application` is registered in DI with `TransactionAndAuditCommandDecorator<,>` wrapping it, so no write path can bypass transaction or audit enforcement.
2. **Append-Only Method Reflection Test (`RoleRadar.Tests.Architecture`)**: Reflection over `MongoNoteRepository` confirms no public or private method calls `Update*`, `Replace*`, or `Delete*` against `note_revisions` or `audit_log`, and no method calls `Delete*` against `notes`.
3. **Fault-Injection Atomicity Contract Test (`RoleRadar.Tests.Contract`)**: When a fault is injected mid-transaction (after `notes` write or `note_revisions` insert, before `audit_log` insert), the transaction rolls back completely and zero changes are committed to any collection.
4. **Typed Failure Contract Tests (`RoleRadar.Tests.Contract`)**: Verifies that `NotFound`, `StaleVersionToken`, and `ImportConflict` return `Result<T>.IsSuccess == false` with the expected `ErrorCode` rather than throwing exceptions.
