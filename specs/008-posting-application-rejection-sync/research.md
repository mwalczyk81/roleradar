# Research: Posting and Application Rejection Sync

Findings come from reading the current code on branch `008-posting-application-rejection-sync` (2026-11-05). File references are to the repo as it stands.

## R1. Where the change lives

**Decision**: `TransitionPostingCommandHandler` ([TransitionPostingCommand.cs](../../src/RoleRadar.Application/Tracking/Commands/TransitionPostingCommand.cs)) does the application write. Hosts do not.

**Rationale**: The MCP tool and the API endpoint both build a `TransitionPostingCommand` and call this handler through the transaction-and-audit decorator. The web app calls the API. One change gives both hosts the behaviour, and the decorator already makes the posting upsert and the audit append one transaction.

**Alternatives**: a new command that wraps the transition (rejected: a second path to forget), a change in each host (rejected: two copies, and FR-010 asks for identical results).

## R2. Writing the event without bypassing the choke point

**Decision**: Use `ctx.Applications.UpsertApplicationAsync(app)` then `AppendEventAsync(event)`, the same pair `HandleAppendEventAsync` uses ([ApplicationCommands.cs](../../src/RoleRadar.Application/Tracking/Commands/ApplicationCommands.cs)). `AppendEventAsync` in `MongoApplicationRepository` re-reads all events, calls `ComputeApplicationMilestones` and sets `has_interview`, `has_first_response`, `last_event_at`, `has_last_event`.

**Rationale**: That is the only place derived application fields are written, so a posting-driven event gets exactly the same milestones as a manual one (FR-007, US1 scenario 6). `Application.ApplyEvent(Rejected, ...)` sets the outcome to `rejected` and the outcome reason, so the handler does not set outcome by hand.

**Alternatives**: calling `HandleAppendEventAsync` itself (rejected: it also syncs the posting and fetches by posting id again, and it is private to the application commands).

## R3. No loop, no double event

**Decision**: The transition path writes the posting first, then the application event, and never calls the application-to-posting sync. The existing sync in `HandleAppendEventAsync` only changes a posting that is not already `rejected` or `closed`, so an application rejected first leaves a rejected posting, and a later posting transition from `applied` is impossible (the matrix does not allow `rejected -> rejected`).

**Idempotency rules** (checked inside the transaction, using the application and its events):

| Application outcome | Rejected event exists | Action | Warning |
| --- | --- | --- | --- |
| `applied` | no | append rejected event, set outcome | none |
| `applied` | yes | no event | none |
| `rejected` | any | no event | none |
| `closed` | any | no event | `application_closed` |
| no application | n/a | nothing (today's behaviour) | none |

Because the transition only runs from `applied` and an `applied` posting is created only with an application, "no application" is a data inconsistency case; it behaves as today with no error.

## R4. Warnings

**Decision**: Add `Warnings: IReadOnlyList<TransitionWarningDto>` to `PostingTransitionResultDto`, each `{ Code, Message }`, with a fixed set of codes (`application_still_rejected`, `application_closed`) and fixed English messages. The audit details of the posting entry include the codes.

**Web handling**: the web displays the returned `message` and never branches on `code`, so the web needs no code snapshot. The code list stays closed in the Application layer, covered by a unit test there.

**Rationale**: The MCP tool already returns the serialized result, so it shows warnings with no extra work. The API endpoint currently discards the result and returns the posting detail, so `PostingDetailDto` gets a nullable `Warnings` list, set only by the transition endpoint. The web action already stores `posting: transitionRes.data`, so the page reads `posting.warnings`.

**Alternatives**: a separate response envelope `{ posting, warnings }` (rejected: changes the response shape the web client and openapi already depend on; a nullable list is additive); a header (rejected: not visible to MCP and awkward to type).

**Reopen detection**: previous disposition `rejected`, target `lead`, application outcome `rejected`.

## R5. Event date

**Decision**: `EventDate` (`string?`, `YYYY-MM-DD`) on `TransitionPostingCommand`, validated with the same `DateOnly.TryParseExact` rule `ApplicationEvent.Create` uses, default `DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime)`. A bad date is a `ValidationError` before any write. It is ignored for transitions that do not create an event.

## R6. The `note` event type and milestones

**Decision**: Append `Note` at the end of `ApplicationEventType` (snake case `note`). In `Application.ApplyEvent` it joins the non-terminal group. In `ComputeApplicationMilestones` it is not in `responseEventTypes`, does not set interview, offer or rejection, and is skipped when computing `last_event_at`, so notes never move derived fields.

**Rationale**: "Identical with and without notes" is the simplest claim to prove with the Insights oracle and parity tests (FR-013, SC-003). Excluding notes from `last_event_at` also keeps a correction from reordering the applications list by last activity. The ghost lens (`applied`, no first response, older than 30 days) reads `has_first_response` and the application date only, so it is unaffected either way. The posting sync switch has no `Note` case, so the posting does not change (US3 scenario 6).

**Alternative**: let notes count toward `last_event_at` (rejected: makes notes visible to ordering for no requirement).

**Enum plumbing to touch** (found by search): `TrackingEnums.cs` (parse and snake-case), `contracts/application-enums.json`, `web/app/lib/application-enums.ts` (values, metadata, appendable options), `ApplicationEnumsContractSnapshotTests`, `web/tests/unit/application-enums.test.ts`, `ApplicationsStep` import mapping (verify it rejects or ignores `note` consistently), `CreateApplicationDialog.tsx` and `applications.$id.tsx` (labels), `InsightsSeed`/`InsightsOracle` (add note events to the seed and prove no change).

## R7. Web page knowledge of "has an application"

**Decision**: Key the notice on `posting.disposition === "applied"` and target `rejected`. An `applied` disposition is only reachable through `application_record`, which creates the application, so no extra field is needed. The read model also carries `has_application`, but the posting detail DTO does not expose it, and adding it is unnecessary here.

## R8. Read-only consistency check (FR-018)

**Decision**: Defer. `reconcile-read-models` rewrites data, so it is the wrong host for a read-only report. A separate read-only scan would need a new `IOwnerReadSeam` query or a Mongo-reading CLI path, which is more than the "cheap" bar in the spec. Revisit if drift reappears; the real data is consistent as of 2026-11-04.

## R9. CLI

**Finding**: There is no posting transition command in `src/RoleRadar.Cli`. **Decision**: the CLI is out of scope for 008; a command that calls the same handler is a later, separate item.

## R10. Audit

**Decision**: Keep the existing `posting_transition` entry and add a second entry for the new event using the existing `application_event_append` operation (entity type `application`), appended in the same transaction. The details record `event_source: posting_transition`. No new audit operation constant, so the audit-operation allowlist tests need no change.

**Shared identifier**: the handler creates one `Guid.CreateVersion7()` per transition and puts it in the `After` dictionary of both entries as `correlation_id` (string). `AuditDetails` and the Mongo audit mapping already serialize `After`, so there is no schema change. Idempotent no-event cases write only the posting entry, which carries no `correlation_id` (nothing to correlate).
