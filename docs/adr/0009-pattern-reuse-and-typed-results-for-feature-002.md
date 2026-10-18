# ADR 0009: Architectural Pattern Reuse and Typed Operational Failures for Feature 002

- **Status**: Accepted
- **Date**: 2026-10-17
- **Feature**: `002-job-search-tracking`

## Context and Problem Statement

Feature 001 established core design patterns for RoleRadar:

- Plain command and query handlers (`ICommandHandler<TCommand, TResult>`, `IQueryHandler<TQuery, TResult>`).
- Cross-cutting decorators (`TransactionAndAuditCommandDecorator`, `LoggingCommandDecorator`, `LoggingQueryDecorator`).
- Append-only collections (`AppendOnlyMongoCollection<T>`) for immutable event logs.
- Typed results (`Result<T>`, `ErrorCode`, `ResultError`) without throwing exceptions for business outcomes.
- Sensitive endpoint redaction (`ConnectionStringRedactor`).
- Optimistic version tokens for concurrency checks (`version_token` as binary GUID).
- Paged read patterns with size caps and continuation cursors (default 50, max 200 items).

Feature 002 introduces new domain operations (tiered posting deduplication, state transitions, criteria queries, application timeline management). Should Feature 002 introduce new framework abstractions, or strictly reuse and extend the proven patterns from Feature 001?

## Decision Drivers

- **Constitution Compliance (Principles III and IV)**: Layering must be enforced by architecture tests, command handlers must be wrapped by decorators, and named seams must be preserved without generic repositories or mediator libraries.
- **Predictable Error Handling**: Expected business conditions (already decided, possible duplicates, invalid transitions, stale versions) must be returned as typed data across delivery boundaries, avoiding exceptions and unhandled stderr noise.
- **Maintainability & Uniformity**: Developers and agents working on the codebase should encounter identical conventions across all features.

## Considered Options

1. **Strictly Reuse and Extend Feature 001 Patterns**:
   - Register all Feature 002 commands behind `TransactionAndAuditCommandDecorator` and `LoggingCommandDecorator`.
   - Use `AppendOnlyMongoCollection<T>` for `application_events`, `posting_signals`, and `search_runs`.
   - Extend `ErrorCode` with the domain failures specified in Feature 002 (`AlreadyDecided`, `AlreadyTracked`, `PossibleDuplicate`, `InvalidTransition`).
   - Use the same paged query conventions and bounds (default 50, max 200 items).
2. **Introduce a Specialized State Machine / Workflow Framework**:
   - Use an external state machine library (such as Stateless) for posting dispositions.
3. **Split Read/Write Stack with CQRS Framework**:
   - Introduce a mediator library or event-sourcing bus.

## Decision Outcome

Chosen option: **Option 1 — Strictly Reuse and Extend Feature 001 Patterns**.

### Details of Reuse

1. **Handlers & Decorators**:
   - Every Feature 002 use case is implemented as an explicit `ICommandHandler` or `IQueryHandler`.
   - Architecture tests enforce that 100% of command handler registrations are wrapped by `TransactionAndAuditCommandDecorator` and `LoggingCommandDecorator`.
   - Any write operation, including updating `last_verified_at` across UTC days, is executed as an audited write through `TransactionAndAuditCommandDecorator`. Zero unaudited writes exist anywhere.
2. **Typed Outcomes vs Failures**:
   - `ErrorCode` is expanded to include genuinely new failure codes:
     - `ErrorCode.InvalidTransition` (disallowed state transition, e.g. attempting to transition to `applied` via `posting_transition` or applying to decided posting without override)
   - Existing error codes are strictly reused:
     - `ErrorCode.StaleVersionToken` is reused for ALL optimistic concurrency, missing, or mismatched version token cases (no redundant `ConcurrencyConflict` code).
     - `ErrorCode.NotFound` and `ErrorCode.ValidationError`.
   - Domain outcomes representing legitimate operational results (`already_decided`, `already_tracked`, and `possible_duplicate`) are returned as SUCCESSFUL results (`Result<T>.Success(...)`) carrying a status discriminator:
     - `already_tracked` on a subsequent UTC day bumps `last_verified_at` and generates an audit entry; because the command returns success, `TransactionAndAuditCommandDecorator` commits the transaction to MongoDB rather than rolling it back.
     - `already_decided` returns the existing posting, prior reason, and `can_reopen` indicator without mutation.
     - `possible_duplicate` returns candidate postings for advisory review without mutating records.
3. **Append-Only Event Streams**:
   - `AppendOnlyMongoCollection<T>` is used directly for immutable audit streams, `application_events`, `posting_signals`, and `search_runs`.
4. **Paging & Size Limits**:
   - Queries enforce default limit 50, maximum limit 200 items, and response byte caps, matching `note_search` and `note_get`.

### Follow-Up Decision: Aggregate-Group Repository Ports (Constitution Principle IV)

Rather than defining a single monolithic `ITrackingRepository` spanning all 9 collections, the repository layer is partitioned into focused, aggregate-group repository ports:

- `IPostingRepository`: Manages `postings` and `posting_signals` aggregates, computed deduplication keys, and in-place ATS upgrades.
- `ICompanyRepository`: Manages `companies` and `career_sources`, including source upserts and `is_active` lifecycle.
- `ICriterionRepository`: Manages `criteria` temporal versioning and point-in-time queries.
- `IApplicationRepository`: Manages `applications` and `application_events` timeline streams.
- `IContactRepository`: Manages `contacts` (isolated CLI-only storage with privacy redaction).
- `ISearchRunRepository`: Manages `search_runs` execution logging and deduplication.

All aggregate-group repositories share a single, unified `ITrackingTransactionContext` (`IClientSessionHandle`), preserving multi-document ACID transactional boundaries across aggregate operations (e.g. creating an application while updating posting disposition and inserting an audit entry) while adhering to Constitution Principle IV (Named Seams Only) and Interface Segregation, eliminating god-interfaces and isolating aggregate test seams.
