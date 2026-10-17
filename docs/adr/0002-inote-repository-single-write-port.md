# ADR 0002: `INoteRepository` as the Aggregate-Level Transactional Write Port

- **Status**: Accepted
- **Date**: 2026-10-17
- **Feature**: `001-memory-replacement`

## Context and Problem Statement

Constitution v1.1.0 Principle IV (*Named Seams Only*) adopts the repository pattern at the aggregate level (`INoteRepository` for narrative storage isolation) and forbids generic `IRepository<T>` abstractions over the database driver without an ADR. Feature 001 manages three MongoDB collections (`notes`, `note_revisions`, and `audit_log`) that must be mutated together atomically on every write.

How should the persistence port be structured so that domain invariants, prior-body revisions, and append-only audit entries are committed atomically per use case without leaking database driver or session types into `RoleRadar.Application`?

## Decision Drivers

- **Constitution v1.1.0 Principle IV Compliance**: Repositories are aggregate-level, domain-specific ports whose methods express use cases (`INoteRepository`), and generic `IRepository<T>` is forbidden.
- **Audit & Revision Safety by Construction**: Application command handlers and the `TransactionAndAuditCommandDecorator` must execute within a single atomic transaction covering `Note`, `NoteRevision`, and `AuditLogEntry`.
- **Typed Failure Results**: Expected failures (`NotFound`, `StaleVersionToken`, `ImportConflict`, `DatastoreOffline`) must be returned as typed `Result<T>` values, not thrown as exceptions.

## Considered Options

1. **Single aggregate-level `INoteRepository` port whose transactional methods cover `Note`, `NoteRevision`, and `AuditLogEntry` in one commit unit**.
2. **Generic `IRepository<Note>`, `IRepository<NoteRevision>`, and `IRepository<AuditLogEntry>` ports**.

## Decision Outcome

Chosen option: **Option 1 — Single aggregate-level `INoteRepository` port**, because expressing note mutations (`CreateAsync`, `AppendAsync`, `ReplaceSectionAsync`, `ArchiveAsync`, `UpsertImportedSectionAsync`) inside `INoteRepository.ExecuteInTransactionAsync` guarantees that the `Note`, `NoteRevision`, and `AuditLogEntry` are always persisted together inside one MongoDB multi-document transaction without leaking `IMongoCollection<T>` or `IClientSessionHandle` into `RoleRadar.Application`.

### Consequences

- **Good**: Zero generic `IRepository<T>` leakage; 100% of aggregate persistence invariants can be tested through a single `INoteRepository` contract test suite (`Constitution Principle II`).
- **Good**: Works seamlessly with `TransactionAndAuditCommandDecorator` so no command handler can mutate a note without recording an audit entry.
- **Bad / Trade-off**: `INoteRepository` is broader than a single-table CRUD interface because it spans the full `Note` aggregate (`notes` + `note_revisions`) and its transactional `audit_log` entry.
