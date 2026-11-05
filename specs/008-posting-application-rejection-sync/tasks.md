---

description: "Task list for 008-posting-application-rejection-sync"
---

# Tasks: Posting and Application Rejection Sync

**Input**: Design documents from `/specs/008-posting-application-rejection-sync/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/ (all present). ADR 0033 is drafted in Phase 1 (Proposed) and accepted by Matt at T017 in the final phase.

**Tests**: MANDATORY (Constitution II, AGENTS.md). In every phase the test tasks come first, are run **red**, and the failing output is pasted into the phase report before any implementation task starts. Implementation tasks say "make T0xx pass".

**Organization**: One phase per plan phase: Phase 1 sync core, Phase 2 hosts, Phase 3 note type, Phase 4 web, then a final consistency phase. Story labels: US1 reject posting rejects application, US2 idempotent and loop-free, US3 note event type, US4 posting page wording, US5 reopen warning.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an unfinished task)
- **[Matt]**: a task only Matt can do or approve. Agents stop and wait.
- Paths are repo-relative.
- **Never** run anything against the developer's running Mongo or stack; never `docker compose down -v`, `wipe` or `restore --force-overwrite`; never run `reconcile-read-models` against the real database. Never commit or push. Never touch `bin/`, `obj/`, `dist/`. Never read or print `web/.env.local` or `deploy/.env`. Fictional data only.
- Report numbers from command output only; say what you could not run; stop at each **Boundary** task.
- Out of scope: the CLI (no posting transition command exists), duplicate-event cleanup, Insights definition changes, data repair, the read-only consistency check (FR-018, deferred).

---

## Phase 1: Sync core in Application (plan Phase 1; US1, US2, US5)

**Purpose**: the `applied -> rejected` transition records the rejection on the application in the same transaction, with idempotency, warnings, an event date and linked audit entries. No host changes yet.

### Tests first (run red, paste failing output)

Use the fake `ITrackingTransactionContext` pattern already in `tests/RoleRadar.Tests.Contract/PostingTransitionContractTests.cs`; reuse its fakes where they exist and extend them in place instead of adding a second set.

- [X] T001 [P] [US1] Unit tests in new `tests/RoleRadar.Tests.Unit/Tracking/PostingRejectionSyncTests.cs` for `TransitionPostingCommandHandler`: applied posting with an `applied` application and no rejected event, rejected with a reason: posting is rejected; application gains exactly one `rejected` event with `EventSource` `posting_transition`, notes equal to the reason, `Outcome` `rejected`; derived write went through `AppendEventAsync` (assert the fake saw that call and no other milestone write). Expected to fail before implementation
- [X] T002 [P] [US1] Same file: event date. Without `EventDate` the event date is the fake clock's UTC date; with `EventDate` `2026-09-01` it carries that date; `2026-13-40` and `09/01/2026` return `ValidationError` and nothing is written (no posting change, no event, no audit entry); `EventDate` is ignored (not validated for creation) when the transition creates no event
- [X] T003 [P] [US2] Same file, idempotency table from research R3: application already `rejected` (no event added, transition succeeds, no warning); `applied` outcome that already holds a rejected event (no second event, succeeds); applied posting with no application (succeeds exactly as today, no event, no warning); `closed` application (no event, succeeds, warning `application_closed`, application unchanged)
- [X] T004 [P] [US2] Same file: no loop. Run the handler for `applied -> rejected` and assert the posting is upserted once, the application is upserted once, one event is appended once, and nothing calls the application-event-to-posting sync. Then append a manual `rejected` event through `RecordApplicationCommandHandler` on the already-rejected posting and assert the posting stays rejected, with no extra event from the sync side
- [X] T005 [P] [US2] Same file: transitions that must not change. `applied -> closed` leaves the application and its events untouched (posting-only), `lead -> rejected` and `scored -> rejected` leave any application untouched, and the matrix still rejects `rejected -> rejected`
- [X] T006 [P] [US5] Same file: reopen. `rejected -> lead` with `OverrideReason` and a `rejected` application: posting becomes lead, application and events unchanged, result carries warning `application_still_rejected`; with no application, or an `applied` or `closed` application, there is no warning and behaviour is as today; the missing `OverrideReason` error is unchanged
- [X] T007 [P] [US1] Same file: audit. A syncing transition writes two entries in the same context call: `posting_transition` (entity `posting`) and `application_event_append` (entity `application`, `event_source` `posting_transition` in `After`), and both carry the same non-empty `correlation_id` in `After`. A no-event case writes only the posting entry, with no `correlation_id`. Warning codes appear in the posting entry's details. This is the correlation evidence for FR-002
- [X] T008 [P] [US1] Same file: rollback. Make the fake application write throw or return a failure and assert the handler returns a failure and the decorator-visible state (fake postings, events, audit list) shows no posting change, no event and no audit entry; see T026 for the real-Mongo version
- [X] T009 [P] [US5] Unit test for the warning contract in `tests/RoleRadar.Tests.Unit/Tracking/TransitionWarningTests.cs`: the code list is a closed set of exactly `application_still_rejected` and `application_closed`; each code has a fixed non-empty message; messages contain no posting or application text
- [X] T010 Run T001 to T009 and paste the failing output. Confirm the failures are for the intended reason (missing members, missing behaviour), not setup errors. Done in two runs: behaviour tests first (6 failed, 13 pinned-behaviour guards passed), then the tests needing new members (compile errors CS1061/CS0103/CS0117/CS1739 for `Warnings`, `TransitionWarnings`, `EventDate`)

### Implementation (make the tests pass)

- [X] T011 [US5] Add `TransitionWarningDto` (`Code`, `Message`) and the closed code list with fixed messages to `src/RoleRadar.Application/Tracking/Commands/TransitionPostingCommand.cs` (or a sibling file in the same folder). Add `Warnings` (`IReadOnlyList<TransitionWarningDto>`, default empty) to `PostingTransitionResultDto` and `string? EventDate = null` to `TransitionPostingCommand`, before `Actor`. Update the existing call sites with named arguments. Make T009 pass
- [X] T012 [US2] In `TransitionPostingCommandValidator`, validate `EventDate` format with the same rule as `ApplicationEvent.Create` (`yyyy-MM-dd`, invariant). Make T002 pass
- [X] T013 [US1] In `TransitionPostingCommandHandler.HandleAsync`, after `posting.Transition(...)` and the posting upsert, when the previous disposition is `Applied` and the target is `Rejected`: load the application with `Applications.GetByPostingIdAsync`; apply the research R3 table using `GetEventsAsync` to detect an existing rejected event; when an event is due create it with `ApplicationEvent.Create(... eventSource: "posting_transition", notes: reason ...)`, call `app.ApplyEvent(Rejected, reason, now)`, then `Applications.UpsertApplicationAsync` and `AppendEventAsync`. Do not call any posting sync. Make T001, T003, T004, T005 pass
- [X] T014 [US5] In the same handler, compute warnings: `application_closed` for the closed-application case, `application_still_rejected` for `rejected -> lead` with a rejected application. Put the codes into the posting audit entry's `After`, return them in `PostingTransitionResultDto.Warnings`. Make T003 (closed case) and T006 pass
- [X] T015 [US1] Audit linkage: create one `Guid.CreateVersion7()` per syncing transition and put it in the `After` dictionary of both the `posting_transition` entry and the new `application_event_append` entry as `correlation_id` (string), plus `event_source` on the application entry. Append both with `_context.AppendAuditEntryAsync` inside the same handler call so the decorator's transaction covers them. Make T007 and T008 pass
- [X] T016 Draft `docs/adr/0033-posting-rejection-syncs-to-application.md` (MADR, status Proposed, short): context (one-way sync, double entry), decision (posting `applied -> rejected` appends a rejected application event in the same transaction; application events keep syncing to the posting; `closed` stays posting-only; reopen allowed with a warning; warnings are a closed code list; audit entries share a `correlation_id`), consequences, rejected alternatives (block the reopen, derive posting state from the application, a new sync command). Add the row to `docs/adr/README.md` with spec `008-posting-application-rejection-sync`
- [X] T018 Run `dotnet build RoleRadar.sln -c Release -v q --nologo -warnaserror` then `pwsh ./scripts/test-fast.ps1`; paste the counts. The architecture tests (decorator wrapping, layering, append-only audit) must stay green unchanged
- [X] T019 **Boundary: Phase 1.** Report: red output (T010), green counts (T018), files changed. Wait for "go" before Phase 2

**Checkpoint**: both US1 and US2 hold in the Application layer; US5 warning is produced. Hosts do not expose it yet.

---

## Phase 2: Hosts, API and MCP (plan Phase 2; US1, US5, FR-002, FR-010)

**Purpose**: pass `eventDate` through, return `warnings`, keep schemas typed, and prove parity and atomicity on a real Mongo.

### Tests first (run red, paste failing output)

- [X] T020 [P] [US1] Contract tests in `tests/RoleRadar.Tests.Contract/Api/PostingsEndpointsContractTests.cs` and the applications endpoint contract tests: application detail events expose `eventSource` (value `posting_transition` for a posting-driven event, otherwise the stored value or null); `POST /api/v1/postings/{id}/transition` accepts `eventDate`; a malformed `eventDate` returns 400 with a field error named `eventDate` and no change; the 200 body has a `warnings` array (empty when none); a plain `GET /api/v1/postings/{id}` returns `warnings: null`
- [X] T021 [P] [US1] Contract tests in `tests/RoleRadar.Tests.Contract/PostingTransitionContractTests.cs` and `McpToleranceAndSchemaContractTests.cs`: `posting_transition` exposes optional `eventDate` of explicit type `string`, the description mentions the application side effect, the tool count is still 12, and the serialized result contains `warnings` as an array of `{ code, message }`
- [X] T022 [P] [US1] OpenAPI and client drift: update expectations in `tests/RoleRadar.Tests.Contract/OpenApi/` so the committed spec and `contracts/client/api-types.ts` must contain `eventDate` on `TransitionPostingRequestDto`, nullable `warnings` on `PostingDetailDto` and nullable `eventSource` on `ApplicationEventDto`. Expected red until T031
- [X] T023 [P] [US1] Integration parity in `tests/RoleRadar.Tests.Integration/Api/ApiMcpParityIntegrationTests.cs`: the same fictional applied posting rejected through the API and through the MCP tool yields identical posting disposition, application outcome, one `posting_transition` event, event date and audit shape (apart from actor and timestamps)
- [X] T024 [P] [US1] Integration in `tests/RoleRadar.Tests.Integration/Tracking/PostingRejectionSyncIntegrationTests.cs` (Testcontainers, isolated database only): after the transition, Mongo shows `has_first_response` true, `has_rejection` true, `rejected_before_interview` as for a manual rejected event (compare with an application rejected by a manual event on a twin seed), and `last_event_at` equal to the event date
- [X] T025 [P] [US1] Same file, correlation evidence against Mongo: read the audit collection after the transition and assert exactly one `posting_transition` and one `application_event_append` entry with equal `correlation_id`
- [X] T026 [P] [US1] Same file, rollback evidence: wrap the application repository so `AppendEventAsync` throws after the posting write, run the transition through the real decorator, then assert the posting document is unchanged (disposition, version token), no event exists and the audit collection has no new entry
- [X] T027 [P] [US2] Same file, idempotency against Mongo: reject, then reject again via a manual rejected event and via a repeated API call (expect the matrix error); the application ends with exactly one rejected event
- [X] T028 Run T020 to T027 and paste the failing output. Done: 6 Contract tests failed on the missing members and 1 failed to compile (`eventDate`); the 2 host-parity tests failed with `Unknown argument(s): 'eventDate'`. The 6 real-Mongo tests (T024 to T027) pass against the Phase 1 handler by design; a temporary mutation (swallowing the failed result) made the `ReturnFailedResult` case fail, then was reverted

### Implementation (make the tests pass)

- [X] T029 [US1] `src/RoleRadar.Api/Models/ApiDtos.cs`: add `EventDate` (`string?`) to `TransitionPostingRequestDto` and nullable `Warnings` (list of `{ Code, Message }` DTO) to `PostingDetailDto`. `src/RoleRadar.Api/Endpoints/PostingsEndpoints.cs`: pass `EventDate` to the command, and have the transition endpoint attach the returned warnings to the detail it builds (other endpoints leave it null). Map the validator's `eventDate` error to a 400 field error. In `ApiDtos.cs` add nullable `EventSource` to `ApplicationEventDto` and map it in `src/RoleRadar.Api/Endpoints/ApplicationsEndpoints.cs` (around the `ApplicationEventDto` construction). Make T020 pass
- [X] T030 [US1] `src/RoleRadar.Mcp.Stdio/Tools/TrackingMcpTools.cs`: add optional `string? eventDate` to `posting_transition` with a `[Description]`, pass it into the command, and add one sentence to the tool description about the application side effect. Do not add a tool. Make T021 pass
- [X] T031 [US1] Regenerate `contracts/openapi/roleradar-api.json` with `dotnet build src/RoleRadar.Api` (the message in `ApiOpenApiContractSnapshotTests`), then `contracts/client/api-types.ts` with the `generate-client` script in `contracts/package.json`; do not hand-edit generated files. Ran: `dotnet build src/RoleRadar.Api -c Release`, then (from `contracts/`) `npx --yes openapi-typescript@7.10.1 openapi/roleradar-api.json -o client/api-types.ts` (the script body, same pin as CI; `contracts/` is not a pnpm workspace package, so the script cannot run through pnpm) Make T022 pass
- [X] T032 Make T023 to T027 pass. If one needs a production change outside T029 to T031, stop and report instead of widening scope
- [X] T033 Run `dotnet build RoleRadar.sln -c Release -v q --nologo -warnaserror`, `pwsh ./scripts/test-fast.ps1`, and `dotnet test tests/RoleRadar.Tests.Integration -c Release --filter "FullyQualifiedName~PostingRejectionSync|FullyQualifiedName~ApiMcpParity"`; paste counts
- [X] T034 **Boundary: Phase 2.** Report red output (T028), green counts (T033), generated files touched. Wait for "go"

**Addendum (decided before Phase 4):** the posting detail did not expose whether a posting has an application, which FR-015 needs. Decision: add nullable `applicationId` to `PostingDetailDto` (contract change, additive). Spec files updated first (`contracts/posting-transition.md`, `data-model.md`, `plan.md`, FR-015).

- [X] A1 [P] Tests first: OpenAPI snapshot and `api-types.ts` declare `PostingDetailDto.applicationId` as a nullable uuid (`PostingRejectionSyncOpenApiTests.cs`); endpoint tests in `PostingRejectionSyncEndpointTests.cs` (GET returns the id for a posting with an application and null for one without; the transition response returns the same; real Mongo, equals the application's id)
- [X] A2 Run A1 and paste the failing output
- [X] A3 `ApiDtos.cs`: `Guid? ApplicationId` on `PostingDetailDto`; `PostingsEndpoints.cs` `BuildPostingDetailResultAsync`: one owner-scoped `GetByPostingIdAsync` lookup, failure follows existing handling and never throws. No other endpoint changes
- [X] A4 Regenerate `contracts/openapi/roleradar-api.json` (`dotnet build src/RoleRadar.Api -c Release`) and `contracts/client/api-types.ts` (pinned `openapi-typescript` from `contracts/`)
- [X] A5 Gate: `dotnet build RoleRadar.sln -c Release -v q --nologo -warnaserror`, `pwsh ./scripts/test-fast.ps1`, integration filters `PostingRejection|ApiMcpParity`, `pnpm --filter web run typecheck`

**Checkpoint**: US1, US2 and US5 hold end to end through API and MCP on real Mongo.

---

## Phase 3: Note event type (plan Phase 3; US3)

**Purpose**: add `note`, prove it changes no derived field, no Insights figure and no posting, and move the shared list in one phase.

### Tests first (run red, paste failing output)

- [X] T035 [P] [US3] Unit tests in `tests/RoleRadar.Tests.Unit/ApplicationMilestoneTests.cs`: `ComputeApplicationMilestones` over each existing event set with and without added `note` events returns equal milestones, including `last_event_at` and `has_last_event`; an application whose only events are the applied event plus a note has no first response; a note dated after the rejection or interview does not change `rejected_before_interview`
- [X] T036 [P] [US3] Unit tests in `tests/RoleRadar.Tests.Unit/Tracking/ApplicationDomainTests.cs`: `Application.ApplyEvent(Note, ...)` leaves `Outcome`, `OutcomeReason`, `UpdatedAt` and `VersionToken` unchanged; `note` parses and serializes as `note` in `TrackingEnumExtensions`; the append path with a `note` event does not change the posting's disposition (extend the posting-sync cases in `PostingRejectionSyncTests.cs` or a sibling test)
- [X] T037 [P] [US3] Contract: update `tests/RoleRadar.Tests.Contract/ApplicationEnumsContractSnapshotTests.cs` and `web/tests/unit/application-enums.test.ts` to expect `note` as the ninth event type, and add the same expectation for any other test that lists event types (search `tests/` and `web/tests/` for `offer_declined`). All three (JSON snapshot, .NET drift, web equality) must be red together before T041
- [X] T038 [P] [US3] Integration, Insights evidence in `tests/RoleRadar.Tests.Integration/Insights/`: extend `InsightsSeed.cs` so fictional applications carry `note` events, including (a) an old no-response application older than 30 days (must stay ghosted), (b) an application with a rejection plus a later correction note (counts one rejection), (c) an application with an interview plus a note. Also add a reconcile case in `tests/RoleRadar.Tests.Integration/ReconcileApplicationsTests.cs`: `reconcile-read-models` run on a seed that has note events (isolated database only) reports no drift and leaves every derived field equal to the no-notes twin, because the reconcile service reuses `ComputeApplicationMilestones`. `InsightsOracle.cs` stays independent and does not count notes. The existing parity tests (counts, drill-down, stats) must stay green with the same expected figures. Add one explicit test that the full figure set equals the one computed from a twin seed without notes
- [X] T039 [P] [US3] Integration import path: confirm `ApplicationsStep` handles a `note` event type consistently (accept as a non-terminal event or reject with a clear error, matching the importer's existing unknown-type rule) with a test in `tests/RoleRadar.Tests.Unit/Tracking/ApplicationsImportStepTests.cs`
- [X] T040 Run T035 to T039 and paste the failing output. Done in two parts: behaviour tests first (15 of 16 milestone tests failed on `last_event_at`; the pinned .NET list test failed on the missing `note`; 2 of 7 web tests failed), then the tests that need the enum (CS0117 `ApplicationEventType` has no `Note`, in Unit and Integration)

### Implementation (make the tests pass)

- [X] T041 [US3] `src/RoleRadar.Domain/Tracking/TrackingEnums.cs`: append `Note` to `ApplicationEventType` and update parse/snake-case helpers. `src/RoleRadar.Domain/Tracking/Application.cs`: `ApplyEvent` treats `Note` as non-terminal. Update `contracts/application-enums.json` (`note` last in `eventTypes`). Make T036 and the .NET part of T037 pass
- [X] T042 [US3] `src/RoleRadar.Infrastructure/Mongo/Repositories/MongoApplicationRepository.cs` `ComputeApplicationMilestones`: skip `note` entirely, including the `last_event_at` update. Do not touch any other write path. Make T035 pass
- [X] T043 [US3] `web/app/lib/application-enums.ts`: add `note` to the values (taken in the same order as the snapshot), metadata (label "Note", description "A remark or correction. Not a response.") and appendable options; update labels used by `web/app/components/CreateApplicationDialog.tsx`. Make the web part of T037 pass
- [X] T044 [US3] Make T038 and T039 pass. No oracle or lens change was needed; also updated the pinned enum-name list in `TrackingDomainAndAuditTests` (now nine names). If the oracle or an Insights lens needs a change, stop: that means a definition changed, which is out of scope
- [X] T045 Run `dotnet build RoleRadar.sln -c Release -v q --nologo -warnaserror`, `pwsh ./scripts/test-fast.ps1`, `dotnet test tests/RoleRadar.Tests.Integration -c Release --filter "FullyQualifiedName~Insights"`, `pnpm --filter web run typecheck`, `pnpm --filter web run test`; paste counts
- [X] T046 **Boundary: Phase 3.** Report red output (T040), green counts (T045), and the figure-equality result from T038. Wait for "go"

**Checkpoint**: US3 holds; the shared event list moved in one phase.

---

## Phase 4: Web (plan Phase 4; US4, FR-015, FR-016, warnings display)

**Purpose**: the posting page says what will happen, shows returned warnings, and the application page shows notes. Follow the web standing rules in AGENTS.md: contract first, `web/app/ds` untouched, no `any`, CSRF on every action, `<Form>`/`useFetcher`, axe with the shared reduced-motion helper, fictional data.

### Tests first (run red, paste failing output)

- [X] T047 [P] [US4] Component tests in `web/tests/routes/postings.detail.test.tsx` (and the dialog's component test file if one exists): for a posting with disposition `applied` and target `rejected` the dialog shows "This also records the rejection on the application."; for any other disposition or target it does not; an optional date field defaults to today's UTC date and posts as `eventDate`
- [X] T048 [P] [US5] Same files: when the action returns `posting.warnings`, the page shows each returned `message` as text (React text node, no `dangerouslySetInnerHTML`) and does not branch on `code` (test with an unknown code and assert the message still renders)
- [X] T049 [P] [US3] `web/tests/routes/applications.component.test.tsx`: an event with type `note` renders with the label "Note" in the event list and is offered in the append form; every event shows its `eventSource` when present (so a `posting_transition` rejection is distinguishable from a manual one, SC-006) and nothing extra when it is null
- [X] T050 [P] [US4] Action tests in `web/tests/routes/applications.action.test.ts` style for the postings transition action: `eventDate` is forwarded; a 400 `eventDate` field error shows inline; 409 shows the Toast with Reload; success revalidates; bad CSRF is rejected
- [X] T051 [P] [US4] Playwright e2e in `web/tests/e2e/posting-rejection-sync.spec.ts` against the isolated test stack and fictional seed: reject an applied posting (success shows the application as rejected with a "posting_transition" source), 400 for a bad date, 409 stale version, bad CSRF, 401 re-login; reopen a rejected posting whose application is rejected and see the warning; axe checks for the dialog and warning in light and dark with the shared reduced-motion helper
- [X] T052 Run T047 to T051 and paste the failing output (e2e may be shown as the spec failing to find the new UI)

### Implementation (make the tests pass)

- [X] T053 [US4] `web/app/components/TransitionPostingDialog.tsx`: add the application notice and the optional date field for `applied -> rejected`; import components only through `web/app/ui.ts`; use `formatDate` for display. Make T047 pass
- [X] T054 [US5] `web/app/routes/postings.$id.tsx`: forward `eventDate` in the transition action, and render `posting.warnings[].message` after a successful transition. Keep the existing CSRF, `assertApiOk`, `data()` and Toast handling. Make T048 and T050 pass
- [X] T055 [US3] `web/app/routes/applications.$id.tsx`: show `note` events with the label from `APPLICATION_EVENT_TYPE_METADATA`, and show each event's `eventSource` as plain text next to its date (React text node). Make T049 pass
- [X] T056 Make T051 pass; fix only what the e2e proves broken
- [X] T057 Run `pnpm --filter web run typecheck`, `lint`, `test`, `build`, `test:e2e`, then `bash scripts/linux-web-check.sh` from Git Bash; paste counts and say what could not run
- [X] T058 **Boundary: Phase 4.** Report red output (T052), green counts (T057). Wait for "go"

**Checkpoint**: US4 and the web side of US3 and US5 hold.

---

## Phase 5: Final consistency

**Purpose**: prove the whole feature agrees with itself and with the spec.

- [X] T059 Walk the spec: for each FR-001 to FR-019 and SC-001 to SC-008, name the test or task that proves it (a table in the phase report). Anything unproven is a finding, not a pass. FR-018 is recorded as deferred
- [X] T060 Run `pwsh ./scripts/test-fast.ps1`, `pre-commit run --all-files`, and `pwsh ./scripts/review-gate.ps1`; report numbers from output only
- [X] T061 Confirm the unchanged behaviour: the existing posting transition and application event tests ran unmodified (list any test edited outside this feature's files and why), and a posting without an application plus every `closed` transition show zero behaviour change (SC-005)
- [X] T062 Check the docs agree: `plan.md`, `research.md`, `data-model.md`, `contracts/*` and ADR 0033 match what shipped (event source string, warning codes and messages, `correlation_id`, `eventDate` name); fix drift in the spec files, not the code, unless the code is wrong
- [X] T063 [Matt] Review the working tree and decide on commit and PR. Agents do not commit or push
- [X] T017 [Matt] Review and accept ADR 0033 (drafted at T016): change its status from Proposed to Accepted in the ADR file and in `docs/adr/README.md`. Not a blocker for any earlier phase; it must be done before the feature is called finished

---

## Dependencies & Execution Order

- Phase 1 first. Phase 2 needs Phase 1 (ADR acceptance, T017, is in the final phase and blocks nothing earlier). Phase 3 does not depend on Phase 2 but runs after it so each boundary is reviewable. Phase 4 needs Phases 2 and 3 (generated client and shared event list). Phase 5 last.
- Inside a phase: tests, then the red run, then implementation, then the gate run, then the boundary. Tasks marked [P] touch different files.

## Parallel Opportunities

- T001 to T009 (Phase 1 tests), T020 to T027 (Phase 2 tests), T035 to T039 (Phase 3 tests) and T047 to T051 (Phase 4 tests) can each be written in parallel.
- T016 (ADR draft) can be written alongside T011 to T015.

## Implementation Strategy

MVP is Phase 1 plus Phase 2: the defect is fixed for both entry points. Phase 3 is independent of the sync and could ship alone. Phase 4 completes the owner-facing wording. Stop at every Boundary.
