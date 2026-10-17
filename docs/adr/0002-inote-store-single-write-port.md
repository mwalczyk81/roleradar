# ADR 0002: `INoteStore` as the Single Transactional Write Port

- **Status**: Accepted
- **Date**: 2026-10-17
- **Feature**: `001-memory-replacement`

## Context and Problem Statement

Constitution Principle IV (*Named Seams Only*) permits `INoteStore` for narrative storage isolation and forbids generic repositories, mediator frameworks, or unapproved abstractions. Feature 001 manages three MongoDB collections (`notes`, `note_revisions`, and `audit_log`) that must be mutated together atomically on every write.

Should `notes`, `note_revisions`, and `audit_log` have separate repository interfaces coordinated by an application-level unit of work, or should a single `INoteStore` port encapsulate the entire transactional write unit?

## Decision Drivers

- **Constitution Principle IV Compliance**: Only named seams (`INoteStore`) are permitted without additional ADRs.
- **Audit & Revision Safety by Construction**: Application services must not be able to mutate a `Note` while forgetting to write an `AuditLogEntry` or `NoteRevision`.
- **Storage Isolation**: Keep MongoDB session handles (`IClientSessionHandle`) strictly inside `RoleRadar.Infrastructure`.

## Considered Options

1. **Single `INoteStore` port encapsulating note mutations, prior-body revisions, and audit entries in atomic command methods**.
2. **Separate `INoteRepository`, `INoteRevisionRepository`, `IAuditRepository`, and `IUnitOfWork` interfaces**.

## Decision Outcome

Chosen option: **Option 1 — Single `INoteStore` port**, because expressing each write (`CreateAsync`, `AppendAsync`, `ReplaceSectionAsync`, `ArchiveAsync`, `UpsertImportedSectionAsync`) as a complete domain command on `INoteStore` guarantees that the `Note`, `NoteRevision`, and `AuditLogEntry` are always persisted together inside one storage transaction without leaking database session abstractions into `RoleRadar.Application`.

### Consequences

- **Good**: Zero unapproved abstractions; 100% of contract invariants can be tested through a single `INoteStore` contract test suite (`Constitution Principle II`).
- **Good**: Eliminates any code path that could mutate a note without writing an audit entry.
- **Bad / Trade-off**: `INoteStore` has 10 cohesive methods covering note CRUD/search, import lookup/upsert, and revision/audit reads rather than single-entity CRUD methods.
