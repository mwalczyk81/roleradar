# Constitution changelog

Version history for `.specify/memory/constitution.md`. Newest first.

## 1.1.1 (2026-10-27, PATCH)

- Wording only: each rationale shortened to one sentence; the Sync Impact Report moved here from the constitution header. No principle changed.

## 1.1.0 (2026-10-17, MINOR)

Version change: 1.0.1 -> 1.1.0 (material expansion of Principles III and IV).

- Modified principles:
  - III. Test-Enforced Layering (added mandatory architecture test verifying every command handler registration is wrapped by the transaction-and-audit decorator; updated delivery adapter rule to reference command/query handlers)
  - IV. Named Seams Only (replaced INoteStore with aggregate-level INoteRepository; adopted plain ICommandHandler / IQueryHandler pattern with decorator pipeline for transaction, audit, validation, and logging; required typed result returns for expected failures instead of exceptions; gated mediator libraries including MediatR behind an ADR addressing MIT license compatibility alongside factories, plugin frameworks, event buses, and generic IRepository<T>)
- Added sections: None
- Removed sections: None
- Templates & downstream artifacts status:
  - .specify/templates/plan-template.md: compatible (reads constitution dynamically)
  - .specify/templates/spec-template.md: compatible
  - .specify/templates/tasks-template.md: compatible
  - Follow-up action needed in specs/001-memory-replacement/ (plan.md, research.md, contracts/, and docs/adr/0002) via /speckit-plan to align Feature 001 design artifacts with INoteRepository, ICommandHandler/IQueryHandler decorators, and typed results.
- Follow-up TODOs: None
