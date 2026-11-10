# Constitution changelog

Version history for `.specify/memory/constitution.md`. Newest first.

## 1.2.0 (2026-11-10, MINOR)

Version change: 1.1.1 -> 1.2.0 (new Principle X). Tracked as RR-76.

- Added Principle X, Standards First: before building a mechanism, check for an RFC, a platform feature or an established standard or library; a hand-rolled mechanism needs an ADR naming what it was compared against; a near fit prefers a thin temporary in-repo extension or an upstream contribution; nothing goes upstream without the owner's approval or carries real data.
- Principle IV: added a clarifying bullet that the abstraction and library gates are not a preference for hand-rolled code. Principle VIII: added a bullet that pinning, license and privacy rules apply to what Principle X adopts and are not a reason to hand-roll. No existing rule weakened.
- Compliance Review now covers Principles I through X and adds a Standards Check that `/speckit-analyze` reports as CRITICAL.
- Templates: `plan-template.md` gains a "Standards considered" table and a Constitution Check row; the PR template gains a Principle X line; `AGENTS.md` and `CONTRIBUTING.md` updated.

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
