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
2. **Typed Failures**:
   - `ErrorCode` is expanded to include domain outcomes:
     - `ErrorCode.NotFound`
     - `ErrorCode.AlreadyDecided` (posting in a decided terminal state with resurface policy `never` or `if_reopened` with `can_reopen` indicator)
     - `ErrorCode.AlreadyTracked` (posting actively tracked in `lead`, `scored`, `applied`, `unscored`)
     - `ErrorCode.PossibleDuplicate` (advisory Tier D candidate matches)
     - `ErrorCode.InvalidTransition` (disallowed state transition, e.g. attempting to transition to `applied` via `posting_transition` or applying to decided posting without override)
     - `ErrorCode.Conflict` / `ConcurrencyConflict` (optimistic version token mismatch)
     - `ErrorCode.ValidationError` (input bounds and schema violations)
3. **Append-Only Event Streams**:
   - `AppendOnlyMongoCollection<T>` is used directly for immutable audit streams, `application_events`, `posting_signals`, and `search_runs`.
4. **Paging & Size Limits**:
   - Queries enforce default limit 50, maximum limit 200 items, and response byte caps, matching `note_search` and `note_get`.
