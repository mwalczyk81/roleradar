# Research: Insights Dashboard (007)

Findings come from reading the 006 code on `main` (commit 229af9e) on 2026-11-04. Anything marked **Verify first** is a claim the plan could not prove by reading, and the first task of the owning phase turns it into a failing-then-passing test.

## R1. Can a count be read with `$top=0`?

**Facts**
- `ODataQueryPipeline.CreateDefaultValidationSettings` sets `MaxTop = 100` and allows `$top`, `$count`, `$filter`, `$select` ([ODataQueryPipeline.cs](../../src/RoleRadar.Api/OData/ODataQueryPipeline.cs)).
- `ExecuteQuery` evaluates `$count` on the filtered query, then applies `$top` and always materializes the page with `ToList()`.
- With `$top=0` the pipeline would run `Take(0)`, which the Mongo LINQ provider turns into `$limit: 0`. MongoDB rejects `$limit: 0` in aggregation ("the limit must be positive").
- The web query builder clamps `top` to `1..100` (`Math.max(1, ...)` in [odata-client.ts](../../web/app/lib/odata-client.ts)), so `$top=0` cannot even be built without changing the builder.

**Outcome (2026-11-05)**: `$top=0` answered an empty-bodied HTTP 500 on all six entity sets. The captured exception is `System.ArgumentOutOfRangeException: Value is not greater than 0: 0. (Parameter 'limit')` from the MongoDB LINQ provider. That is a 006 defect (a 500 on client input), so the shared pipeline now treats `$top=0` as a valid count-only request and every set behaves the same.

**Decision**: use `$top=0&$count=true` for every count. `MaxTop` stays 100; the web query builder allows 0 only through a count-only option and keeps the 1..100 clamp for list pages.

**Alternatives rejected**
- `$top=1&$select=id`: works without a pipeline change but reads a row per request and leaves a 500-on-input defect in place.
- A dedicated `/odata/$count` path: not in the 006 route set, would need its own allowlist and tests.

## R2. Counts: parallel OData `$count` or one stats call?

**Decision**: parallel OData `$count`, driven by one shared filter-definition module (ADR 0029).

**Why**: the user-visible promise is "the number and the list always agree". If the count runs the same filter string the list runs, they agree by construction, and the parity test is a check on the module rather than on two parallel implementations. A server-side stats call would have to re-implement every filter in C#, and the two copies could drift.

**Cost**: 20 OData requests per page load in the all-time view, 22 in the 90 day view (18 lens counts plus 2 read-model sentinels, see R6, plus the 2 all-time interview counts that the 90 day view needs), run with concurrency 6 and a 5 second per-request timeout. Each is an indexed count over a single owner's data. The stats call (Phase 3) adds one more, for 21 (all-time) and 23 (90 day).

**Alternatives rejected**
- One stats call returning every count: duplicates filter logic, drill-down could drift.
- Sequential counts: 20 round trips in series, slower and no safer.
- Unbounded `Promise.all`: 20 simultaneous requests against one API for a single page.

## R3. Stats endpoint: what OData cannot do, and how to compute it

OData here has no `$apply` (the Mongo C# driver cannot translate it), so median response time, weekly buckets and run yield need a small read-only endpoint.

**Decision** (ADR 0030): `GET /api/v1/insights/stats`, handled by an Application-layer query handler that reads through `IOwnerReadSeam` only:
- `QueryApplications(owner)` projected to `(AppliedAt, FirstResponseAt)` for median and weekly buckets. Both fields are public on `ApplicationReadModel`; `FirstResponseAt` already follows the agreed response-event rule (interview, offer, offer accepted, offer declined, rejected; follow-up and withdrawn excluded) because `ComputeApplicationMilestones` is the choke point.
- `QuerySearchRuns(owner)` projected to `(RunDate, NewPostings)` for yield.
- A pure static calculator does the median, Monday-UTC week bucketing and series shaping, so it is unit-testable with no database.

**Volume assumption**: one owner, a job search of years not decades. Expect hundreds of applications and low thousands of search runs. The handler reads at most 50,000 projected rows per source (`Take(51_000)` check) and returns a typed `Result` failure above that rather than silently truncating. Two date columns at 50,000 rows is roughly 1 to 2 MB of transient memory, which is fine. If the volume ever outgrows this, the fix is a stored pre-aggregate, which would need a new ADR.

**No new seam method is needed.** The existing `Query*` methods already return owner-scoped `IQueryable`, and projections run in the database.

## R4. `has_application` on postings: where it must be written

**Facts**
- `MongoPostingRepository.UpsertPostingAsync` does `ReplaceOneAsync(..., IsUpsert)` with `SerializePosting(...)`. A replace rewrites the whole document, so a new derived field that `SerializePosting` does not emit would be wiped on the next posting update.
- `MongoApplicationRepository.UpsertApplicationAsync` already reads the posting and event data to fill the denormalized application fields, and `AppendEventAsync` recomputes and `$set`s milestones.
- Applications are unique per posting (`ux_applications_owner_posting`) and there is no application delete path (ADR 0010). So `has_application` goes false to true once and never back.
- The import writes rejected applications together with a rejected event (`ApplicationsStep`), so event-derived flags are correct for imported data.

**Decision** (ADR 0031):
1. `UpsertApplicationAsync` sets `has_application = true` on the parent posting, in the same session as the application write.
2. `UpsertPostingAsync` computes `has_application` itself (one indexed existence query on `ux_applications_owner_posting`) before it serializes, so a replace can never wipe the value.
3. `ReconcilePostingsAsync` recomputes it from the applications collection.
4. No status or event change affects it. A test pins this so a future "withdraw deletes the application" change cannot slip through.

**Index**: `ix_postings_owner_has_application` on `{owner_id, has_application, _id}` (owner first), plus `ix_postings_owner_first_seen_at` on `{owner_id, first_seen_at, _id}` because the window filters on it.

**Architecture test**: a source scan asserts the field-name constant is referenced only from the allowlisted writer, mapper, reconcile and index files (see data-model.md).

## R5. First-seen date is stored but not exposed

`Posting.FirstSeenAt` is stored as `first_seen_at` and `Rehydrate` and the import keep it. `PostingReadModel` exposes only `CreatedAt` and `UpdatedAt`. The spec windows postings by first-seen date, so Phase 1 adds a read-model property `FirstSeenAt` (stored field, **no backfill needed**).

**Verify first**: that real imported postings have `first_seen_at` populated and meaningful (not just the import timestamp). The plan cannot check the real database and must not. The seed proves the mapping; Matt eyeballs the first dry run.

## R6. Read-model fields missing before the backfill (the `eq false` trap)

In MongoDB, `{ flag: false }` does **not** match documents where `flag` is absent. Until `reconcile-read-models` has run on the real database, a filter such as `rejectedBeforeInterview eq false` would silently undercount.

**Decision**: the page runs two cheap coverage checks and refuses to show flag-dependent numbers until they pass:
- postings: `hasApplication eq true or hasApplication eq false` count equals the total postings count;
- applications: `rejectedBeforeInterview eq true or rejectedBeforeInterview eq false` count equals the total applications count.

If either fails, flag-dependent tiles show "needs backfill" with the exact operator command, never a wrong number. These two requests are the 19th and 20th counts.

## R7. New derived application flags

Spec metrics need facts that `status`, `hasInterview` and `firstResponseAt` cannot give:

| Need | Existing field | Gap |
| --- | --- | --- |
| responded | `firstResponseAt ne null` | none |
| interviewed | `hasInterview` | none |
| open | `status eq 'applied'` (outcome only leaves applied on a rejected, offer accepted, offer declined or withdrawn event) | none, pinned by a test |
| ghosted | `status eq 'applied'`, `firstResponseAt eq null`, `appliedAt` cutoff | none |
| mature cohort | `appliedAt` cutoff | `ageInDays` is blocked from `$filter` (derived), so the lens uses the equivalent `appliedAt` cutoff |
| offers | none | new `hasOffer` |
| rejected | none (`status eq 'rejected'` is outcome-based, and a later accept/decline/withdraw event can overwrite it) | new `hasRejection` |
| rejected before or after interview | none | new `rejectedBeforeInterview` |

All three are computed inside `ComputeApplicationMilestones` (extended to return a small record instead of a tuple) so `SerializeApplication`, `AppendEventAsync` and reconcile share one implementation, exactly like the 006 flags.

`rejectedBeforeInterview` is true when there is a rejected event and no interview event dated on or before the earliest rejected event (events carry a date but no time, so a same-day interview is read as preceding the rejection). A rejection with no interview at all is "before". A rejection after at least one earlier or same-day interview is "after".

## R8. Date arithmetic must match `ageInDays` exactly

`ApplicationReadModel.AgeInDays` is `UtcNow.Date - AppliedAt.UtcDate` in whole days. It is blocked from `$filter`, so lenses use `appliedAt` cutoffs that are equivalent:
- mature (at least 14 days old): `ageInDays >= 14` is `appliedAt < (asOf - 13 days) 00:00Z`
- ghost-eligible (at least 30 days old): `appliedAt < (asOf - 29 days) 00:00Z`
- ghosted (older than 30 days): `appliedAt < (asOf - 30 days) 00:00Z`

A boundary test seeds applications at 13, 14, 29, 30 and 31 days and compares the lens result with the `ageInDays` the list page displays. The cutoff arithmetic lives in the one lens module.

## R9. Drill-down mechanism

List pages build their filter from their own URL params (`q`, `status`, ...). Accepting a raw `$filter` in a page URL would be an injection surface and would break the allowlist rules.

**Decision** (ADR 0029): drill-down links carry `?lens=<key>&window=90d|all&asOf=YYYY-MM-DD`. List loaders call the shared `resolveLens(entity, lens, window, asOf)`; unknown keys are ignored. The count request and the list request therefore build the filter from the same function. `asOf` is pinned in the link so a dashboard opened before midnight UTC and clicked after still agrees.

## R10. Kit v20 chart components

The vendored design system was at kit v17 when this was written, and has since been synced (the version is recorded in [SYNC.md](../../web/app/ds/SYNC.md)). Chart components and the four `--ds-chart-*` tokens are published in the kit at v20 and need an export that only Claude, with Matt signed in to claude.ai, can do (agy cannot reach it). See the plan, Phase 0, for the file list and the gate. Nothing in `web/app/ds` is edited by hand.

## R11. Markdown packages (checked against the npm registry on 2026-11-04)

| Package | Exact version | Licence | Registry last modified |
| --- | --- | --- | --- |
| react-markdown | 10.1.0 | MIT | 2025-03-07 |
| remark-gfm | 4.0.1 | MIT | 2025-02-10 |
| remark-breaks | 4.0.0 | MIT | 2023-11-20 |

All three are the `latest` dist-tag today and well over two weeks old. react-markdown depends on `unified`, `remark-parse` and `remark-rehype` (all `^11`); `rehype-raw` is **not** a dependency and must not be added. A Phase 4 task runs a licence audit over the full resolved tree and fails on anything outside MIT, ISC, BSD-2/3-Clause, Apache-2.0 or 0BSD. Pin exactly (the repo's web `package.json` uses no ranges) and commit the lockfile.

**Verify first**: that the ESM-only packages build under the React Router SSR build (`pnpm --filter web run build`) without `ssr.noExternal` changes.

## R12. Fictional seed and the real-database guarantee

Existing precedent: `ODataTestHost` starts its own Testcontainers replica set; `RemainingEntitiesSeed` inserts fictional documents; `ExternalDatabaseGuardUnitTests` and `MongoScratchDatabaseGuard` guard destructive paths. The plan reuses that shape (see plan.md, section Seed and isolation). No new test project is added.

## R13. Spec follow-ups found while planning

These were plan-level decisions where the spec was silent. **Matt confirmed all four on 2026-11-04.** Item 5 is a plan rule.
1. "Leads" in the funnel is every posting first seen in the window (the top of the funnel), not only postings still in lead disposition. (Confirmed.)
2. "Last 90 days" is 90 calendar days **including** `asOf`: `[asOf-89d, asOf]`. (Confirmed.)
3. The interview-conditioned offer rate uses `hasOffer and hasInterview` as its numerator so it can never exceed 100% when an offer was logged without an interview event. The other offer rate (offers over all applications) uses `hasOffer` alone. (Confirmed.)
4. Search-run yield is windowed by run start date and capped at the latest 120 runs, with a "showing latest 120" label. (Confirmed.)
5. Counts are blocked behind the backfill coverage checks in R6.
