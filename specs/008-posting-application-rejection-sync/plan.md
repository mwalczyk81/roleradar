# Implementation Plan: Posting and Application Rejection Sync

**Branch**: `008-posting-application-rejection-sync` | **Date**: 2026-11-05 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/008-posting-application-rejection-sync/spec.md`

## Summary

Moving an applied posting to rejected also records the rejection on its application, in the same transaction, for the web page and MCP, because the change lives in `TransitionPostingCommandHandler` (Application layer). A second, independent change adds a note-only application event type.

- **Sync core** (US1, US2, US5): after the posting transition, the handler loads the posting's application and, when the application is still `applied` with no rejected event, appends a rejected event (source `posting_transition`) through the existing `Applications.UpsertApplicationAsync` + `AppendEventAsync` path. `AppendEventAsync` is the choke point that recomputes milestones and `has_*` flags, so nothing new writes derived fields (research R2). The handler never calls the application-to-posting sync, so nothing loops (R3).
- **Warnings** (FR-003a, FR-011a): the transition result gains a typed `warnings` list. Two codes: `application_still_rejected` (reopen) and `application_closed` (reject while the application ended another way). Warnings reach the posting page, the MCP JSON and the API response, and are written into the audit details (R4). The code list is a closed set in the Application layer. The web displays the returned `message` and never branches on `code`, so no code snapshot or equality test is needed on the web side.
- **Audit** (FR-002): the posting entry and the new application entry are written in the same transaction and both carry one shared `correlation_id` (a UUIDv7 made once per transition) in their `After` details, so no audit schema change is needed. A test proves both entries exist, share the id, and that a failed application write leaves neither (R10).
- **Scope**: the CLI is out of scope. It has no posting transition command, and adding one is a later, separate item.
- **Event date**: optional `eventDate` (`YYYY-MM-DD`) on the command, MCP tool and API request; default today UTC from `IUtcClock` (R5).
- **Event source on the page** (FR-006, SC-006): `ApplicationEventDto` has no `eventSource` today and the application page does not show it, so both gain it (additive, nullable). Otherwise the owner could not tell a posting-driven rejection from a manual one.
- **Note event type** (US3): `note` is added to the application event enum. It is non-terminal and invisible to milestones, including `last_event_at`, so every derived field is identical with and without notes (R6). The contract snapshot, .NET drift test and web equality test change in one phase.
- **Web** (US4): the transition dialog says the rejection is also recorded on the application when the posting is `applied` and the target is rejected; it shows returned warnings; the application page labels `note`.
- **Decision record**: ADR 0033 (sync direction, small).
- Work is phased so each phase ends at a reviewable boundary, tests first with the failing output shown.

Findings are in [research.md](./research.md); entities and state changes in [data-model.md](./data-model.md); interface changes in [contracts/](./contracts/); validation steps in [quickstart.md](./quickstart.md).

## Technical Context

**Language/Version**: C# 13 / .NET 10 (backend); TypeScript 5.9 / Node 22 (web, React Router v7 SSR)

**Primary Dependencies**: none new. Existing `MongoDB.Driver`, `ModelContextProtocol` (stdio host), `openapi-typescript` generated client.

**Storage**: MongoDB replica set. No new collections, fields or indexes. One new `event_type` string value (`note`) and one new `event_source` string value (`posting_transition`).

**Testing**: xUnit v3, NetArchTest.Rules, Testcontainers.MongoDb (isolated only), Vitest, Playwright Chromium with the shared reduced-motion axe helper. No new test projects.

**Target Platform**: Windows dev, Linux containers.

**Project Type**: monorepo: .NET Domain/Application/Infrastructure/Api/Mcp.Stdio (the CLI is untouched) plus the React Router web app.

**Performance Goals**: one extra application read, one event read and two writes inside the existing transaction, only for `applied -> rejected`. No other transition changes cost.

**Constraints**: expected failures return `Result<T>`; no hard deletes; every write audited; derived fields only at the repository choke point; MCP and API schemas explicitly typed; fictional data only; nothing runs against the developer's Mongo.

**Scale/Scope**: single owner; one extra event per rejection.

## Constitution Check

*GATE: passed before Phase 0 research; re-checked after Phase 1 design. No violations.*

| Principle | Result | How |
| --- | --- | --- |
| I Privacy first | Pass | Fictional fixtures only (including the "second rejected event carries a correction" scenario, rebuilt with invented names). Nothing reads the live database. |
| II Tests first; isolated containers | Pass | Every phase starts red with the failing output captured. Integration uses Testcontainers only. |
| III Layering enforced | Pass | All behaviour is in Application (and Domain for the enum). Hosts (API, MCP) only pass `eventDate` through and return `warnings`; the web page renders the returned message. The decorator test already covers `TransitionPostingCommandHandler`. |
| IV Named seams only | Pass | Uses existing `ITrackingTransactionContext`, `IApplicationRepository` and `IUtcClock`. No new seam, repository, mediator or bus. Decision recorded in ADR 0033. |
| V Untrusted text; narrow audited tools | Pass | No new MCP tool, one optional typed parameter on `posting_transition`. Warning text is fixed server-side strings, never posting text. Both writes are audited. No delete path. |
| VI stdout hygiene | Pass | Warnings are part of the returned JSON frame. No new logging to stdout. |
| VII Durable data | Pass | No data repair, no destructive script. |
| VIII ADRs; pinned deps | Pass | ADR 0033 added. No new package or image. |
| IX Baseline conventions | Pass | UTC dates, UUIDv7 event ids via `ApplicationEvent.Create`, `owner_id` on the event. |

## Project Structure

### Documentation (this feature)

```text
specs/008-posting-application-rejection-sync/
├── plan.md              # This file
├── research.md          # Phase 0
├── data-model.md        # Phase 1
├── quickstart.md        # Phase 1
├── contracts/
│   ├── posting-transition.md
│   └── application-event-note.md
├── checklists/
│   └── requirements.md
└── tasks.md             # /speckit-tasks (not created here)
```

### Source Code (repository root)

```text
src/RoleRadar.Domain/Tracking/
├── TrackingEnums.cs                 # + ApplicationEventType.Note, parse/snake-case
└── Application.cs                   # ApplyEvent: Note is non-terminal

src/RoleRadar.Application/Tracking/Commands/
├── TransitionPostingCommand.cs      # EventDate, application sync, warnings, second audit entry
└── ApplicationCommands.cs           # Note: no posting sync (verify only)

src/RoleRadar.Infrastructure/Mongo/Repositories/
└── MongoApplicationRepository.cs    # ComputeApplicationMilestones ignores note (incl. last_event_at)

src/RoleRadar.Api/
├── Models/ApiDtos.cs                # TransitionPostingRequestDto.EventDate; PostingDetailDto.Warnings and PostingDetailDto.ApplicationId; ApplicationEventDto.EventSource
├── Endpoints/PostingsEndpoints.cs   # pass EventDate; attach warnings to the response
└── Endpoints/ApplicationsEndpoints.cs  # map EventSource into ApplicationEventDto

src/RoleRadar.Mcp.Stdio/Tools/TrackingMcpTools.cs   # posting_transition: eventDate param, warnings in JSON

contracts/
├── application-enums.json           # + "note"
└── openapi/, client/api-types.ts    # regenerated

web/app/
├── lib/application-enums.ts         # + "note", metadata, appendable options
├── components/TransitionPostingDialog.tsx   # notice, optional date, warnings
├── routes/postings.$id.tsx          # forward eventDate, surface warnings
└── routes/applications.$id.tsx      # note label; show each event's source

docs/adr/0033-posting-rejection-syncs-to-application.md   # new

tests/
├── RoleRadar.Tests.Unit/            # handler, milestones, enum, warnings, date validation
├── RoleRadar.Tests.Contract/        # application-enums snapshot, transition contract
├── RoleRadar.Tests.Architecture/    # unchanged tests must stay green
└── RoleRadar.Tests.Integration/     # atomicity, idempotency, Insights oracle/seed + note parity
web/tests/                           # unit (enums equality), e2e (reject applied posting, reopen warning)
```

**Structure Decision**: existing layout; no new projects or folders except the ADR file and this spec directory.

## Phases

Each phase stops at its boundary: run `pwsh ./scripts/test-fast.ps1` (plus the web checks for Phase 4), report numbers from the output, and wait.

| Phase | Scope | Stories | Gate at the end |
| --- | --- | --- | --- |
| 1 | Sync core in Application: event date, rejected event append, idempotency, closed-outcome and reopen warnings, second audit entry, ADR 0033. Unit tests with fakes, contract test for the result shape. | US1, US2, US5 | `test-fast`, build with `-warnaserror` |
| 2 | Hosts: API request/response (`eventDate`, `warnings`), MCP `eventDate` and JSON, openapi and `api-types.ts` regen, API and MCP parity tests, Testcontainers atomicity, rollback and audit-correlation tests. | US1, US5, FR-002, FR-010 | `test-fast`, Integration for the touched tests |
| 3 | Note event type: enum, milestones, contract snapshot + .NET drift + web equality together, Insights seed/oracle and parity evidence. | US3 | `test-fast`, Insights integration tests, web `test` |
| 4 | Web: dialog notice, optional date, warnings display, note label, e2e (success, 400, 409, bad CSRF, 401 for the mutation). | US4, FR-015, FR-016 | Linux web gate, `review-gate.ps1` |

## Scope Notes

1. **CLI is out of scope.** `src/RoleRadar.Cli` has no posting transition command, so the entry points for 008 are the web page (through the API) and MCP. A CLI command that calls the same handler is a later, separate item and would inherit this behaviour.
2. **Read-only consistency check (FR-018)** is deferred (research R8).

## Complexity Tracking

No constitution violations to justify.
