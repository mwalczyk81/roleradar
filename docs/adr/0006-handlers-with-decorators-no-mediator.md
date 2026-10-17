# ADR 0006: Plain Command & Query Handlers with Explicit Decorators Instead of a Mediator Library

- **Status**: Accepted
- **Date**: 2026-10-17
- **Feature**: `001-memory-replacement`

## Context and Problem Statement

RoleRadar's `Application` layer exposes 5 write commands (`CreateNote`, `AppendToNote`, `ReplaceNoteSection`, `ArchiveNote`, `ImportMarkdown`) and 2 read queries (`GetNote`, `SearchNotes`) consumed by `RoleRadar.Mcp.Stdio` tools, `RoleRadar.Cli` commands, and later `REST` endpoints. Every command must enforce input validation, datastore readiness, multi-document transaction boundaries, and append-only audit log recording, while both commands and queries require structured `stderr` logging and typed failure results (`Result<T>`).

Should we introduce a third-party mediator library (such as `MediatR`) or use plain hand-written `ICommandHandler<TCommand, TResponse>` and `IQueryHandler<TQuery, TResponse>` interfaces composed with DI decorators?

## Decision Drivers

- **Constitution v1.1.0 Principle IV (*Named Seams Only*)**: Requires plain `ICommandHandler` and `IQueryHandler` interfaces injected directly into delivery adapters, decorators for cross-cutting concerns, typed `Result<T>` outcomes for expected failures, and explicitly gates mediator libraries (including `MediatR`) behind an ADR addressing MIT license compatibility.
- **Constitution v1.1.0 Principle III (*Test-Enforced Layering*)**: Requires an automated architecture test verifying that every command handler registration is wrapped by the transaction-and-audit decorator so a write path cannot skip auditing.
- **License Compatibility**: RoleRadar is published under the permissive **MIT License**. Per secondary sources, `MediatR` version 13 and later transitioned to a dual-license model (Reciprocal Public License `RPL-1.5` or paid commercial license; exact terms would need legal/upstream verification before any adoption), which conflicts with redistributing an unrestricted MIT portfolio codebase.
- **Explicitness & Debuggability**: Direct handler injection preserves static call graphs and IDE navigation without runtime reflection dispatch.

## Considered Options

1. **Hand-written `ICommandHandler<TCommand, TResponse>` and `IQueryHandler<TQuery, TResponse>` classes injected directly into adapters, wrapped by `TransactionAndAuditCommandDecorator` and `LoggingDecorator`, returning typed `Result<T>`**.
2. **`MediatR` (`ISender` / `IRequestHandler` / `IPipelineBehavior`)**.
3. **Coarse-grained `NoteService` and `MarkdownImportService` classes without per-command decorators**.

## Decision Outcome

Chosen option: **Option 1 — Plain command and query handlers with explicit DI decorators and typed `Result<T>` return values**.

### Handler & Decorator Pipeline Design

1. **Commands & Handlers**:
   - `CreateNoteCommand` → `CreateNoteCommandHandler`
   - `AppendToNoteCommand` → `AppendToNoteCommandHandler`
   - `ReplaceNoteSectionCommand` → `ReplaceNoteSectionCommandHandler`
   - `ArchiveNoteCommand` → `ArchiveNoteCommandHandler`
   - `ImportMarkdownCommand` → `ImportMarkdownCommandHandler`
2. **Queries & Handlers**:
   - `GetNoteQuery` → `GetNoteQueryHandler`
   - `SearchNotesQuery` → `SearchNotesQueryHandler`
3. **Decorator Chain**:
   - Commands: `LoggingCommandDecorator<TCommand, TResponse>` → `TransactionAndAuditCommandDecorator<TCommand, TResponse>` → concrete `TCommandHandler`.
   - Queries: `LoggingQueryDecorator<TQuery, TResponse>` → concrete `TQueryHandler`.
4. **Architecture Test Verification**:
   - `CommandHandlerDecorationArchitectureTests` in `RoleRadar.Tests.Architecture` enumerates all concrete `ICommandHandler<,>` implementations in `RoleRadar.Application`, builds the service provider via `AddRoleRadarApplication()`, and asserts that every resolved `ICommandHandler<,>` has `TransactionAndAuditCommandDecorator<,>` in its decorator chain.

### Consequences

- **Good**: Zero third-party mediator dependencies or `RPL-1.5` / commercial licensing entanglement.
- **Good**: Structural guarantee (enforced in CI by `RoleRadar.Tests.Architecture`) that no write command can ever execute outside a transaction or skip writing an `audit_log` entry.
- **Good**: Expected failures (`NotFound`, `StaleVersionToken`, `SectionNotFound`, `AmbiguousHeading`, `ImportConflict`, `InvalidUtf8`, `DatastoreOffline`) are explicit in method signatures via `Result<T>`.
- **Bad / Trade-off**: Adapters inject the specific handler interface per tool/command rather than a single `ISender` facade.
