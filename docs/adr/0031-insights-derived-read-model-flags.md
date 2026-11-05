# ADR 0031: Derived Read-Model Flags for Insights, Written Only at the Repository Choke Point

- **Status**: Accepted
- **Date**: 2026-11-04
- **Feature**: `007-insights-dashboard`

## Context and Problem Statement

Drill-downs need list filters that today's read models cannot express: whether a posting has an application, whether an application had an offer or a rejection, and whether a rejection came before any interview. AGENTS.md requires derived stored fields to be written only at the repository choke point and backfilled by `reconcile-read-models`.

A subtle hazard: `MongoPostingRepository.UpsertPostingAsync` replaces the whole posting document. A new derived field that `SerializePosting` does not emit would be wiped by the next posting edit.

## Decision Outcome

Add four stored, derived, read-only fields. A fifth read-model property, `first_seen_at`, was already stored and is only newly exposed.

| Collection | Field | Rule |
| --- | --- | --- |
| postings | `has_application` | an application exists for the posting |
| applications | `has_offer` | any `offer` event |
| applications | `has_rejection` | any `rejected` event |
| applications | `rejected_before_interview` | rejection exists and no `interview` event dated on or before the earliest rejection (same day counts as after) |

(`first_seen_at` on postings is already stored; it is only newly exposed and needs no backfill. The five OData properties are these four plus `firstSeenAt`.)

Write rules:

- Application flags are computed by the one extended `ComputeApplicationMilestones` (returns a record), shared by `SerializeApplication`, `AppendEventAsync` and `ReconcileApplicationsAsync`.
- `has_application` is set true by `UpsertApplicationAsync` on the parent posting in the same session. `UpsertPostingAsync` computes it from the applications collection (indexed existence check) before serializing, so a replace never loses it. Reconcile recomputes it.
- Applications are unique per posting and cannot be deleted (ADR 0010), so `has_application` only moves false to true. A test pins that no status or event change alters it.
- Indexes: `ix_postings_owner_has_application` `{owner_id, has_application, _id}` and `ix_postings_owner_first_seen_at` `{owner_id, first_seen_at, _id}`, both owner first. No new application indexes until a benchmark shows a need.
- Architecture test: a source scan allows the field-name constants only in the writer, mapper, reconcile and index files; any other reference (Api, Mcp, Cli, Application handlers) fails the build.
- OData: the five properties (the four derived fields and `firstSeenAt`) are public on the read models, filterable, not sortable (no allowlist widening). `contracts/odata/metadata.xml` and `docs/api/odata-queries.md` are updated and the drift tests gate it.
- Reconcile gains the new fields in the existing run order: deploy, backup, dry run, confirm, dry run reports 0. Matt runs it on the real database; agy and CI never do.

### Consequences

- Good: counts are cheap indexed `$count`s; filters stay inside the 006 pipeline.
- Good: one implementation of each derivation.
- Bad: until the backfill runs, `eq false` filters undercount (MongoDB `{flag:false}` does not match a missing field). ADR 0029's coverage checks make the page refuse flag-dependent numbers until the backfill is complete.
- Bad: `UpsertPostingAsync` now does one extra indexed read per posting write. Negligible at this volume.

### Confirmation

Integration tests on Testcontainers: seed through the real repositories, assert flags; seed legacy documents without the flags, run reconcile, assert flags and a second dry run reporting 0; edit a posting after an application exists and assert `has_application` survives.
