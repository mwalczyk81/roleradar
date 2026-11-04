---

description: "Task list for 007-insights-dashboard"
---

# Tasks: Insights Dashboard

**Input**: Design documents from `/specs/007-insights-dashboard/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/ (all present). ADRs 0029 to 0032 are drafted (Proposed).

**Tests**: MANDATORY (Constitution II, AGENTS.md). In every phase the test tasks come first, are run **red**, and the failing output is pasted into the phase report before any implementation task starts. Implementation tasks say "make T0xx pass".

**Organization**: Phases follow the plan. The mapping to plan phases is in each heading. Story labels: US1 funnels and rates, US2 drill-down, US3 trends, US4 response time, US5 notes Markdown.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an unfinished task)
- Paths are repo-relative. `Insights/` under tests means `tests/RoleRadar.Tests.Integration/Insights/`.
- **Never** run anything against the developer's running Mongo or stack; never `docker compose down -v`, `wipe` or `restore --force-overwrite`; never run `reconcile-read-models` against the real database (Matt only). Never commit. Never touch `bin/`, `obj/`, `dist/`. Never read or print `web/.env.local` or `deploy/.env`.
- Report numbers from command output only; say what you could not run; stop at each **Boundary** task.

---

## Phase 1: Setup (plan Phase 0, design kit gate)

**Purpose**: bring kit v20 chart components into the vendored design system. Agy cannot reach claude.ai, so T001 and T002 are done by Claude with Matt signed in to claude.ai. **Phase 3 chart and tile tasks are blocked until T004 passes.** Phases 2 and 5 do not depend on this phase and may run in parallel with it.

- [X] T001 [Claude+Matt] DONE before this run: kit synced into `web/app/ds/` copy-only at kit version 1793792015-d5b6 (v23), including StatTile, BarChart (funnel mode), ColumnChart, Sparkline, the four `--ds-chart-*` tokens and a fixed `tokens.css`
- [X] T002 [Claude+Matt] DONE before this run: `web/app/ds/SYNC.md` refreshed (kit version, date, status, sha256 lines)
- [X] T003 Diff the file listing of `web/app/ds/` against the expected list: the four chart components (`components/src/data/{StatTile,BarChart,ColumnChart,Sparkline}.jsx`, each with `components/<Name>/<Name>.d.ts` and `README.md`), plus `components/src/data/chartParts.jsx` (shared internals, no `.d.ts`; never import it directly, import only the four components), updated `components/index.d.ts`, `components/bundle.css`, `tokens.css`, `tokens.json`, `README.md`, `SYNC.md`. `preview.html` cards are not vendored. Any other extra or missing file is a stop-and-ask; do not edit anything under `web/app/ds/`
- [X] T004 Add `web/tests/architecture/ds-sync.test.ts`: every file under `web/app/ds/` (except `SYNC.md` itself) has a matching sha256 line in `web/app/ds/SYNC.md`, no SYNC line is stale, and `SYNC.md` names kit version `1793792015-d5b6`. Show it failing first against a temp copy with one hash altered (the test also carries a permanent self-test for an altered file, an unlisted file and a wrong version), then passing on the real tree
- [X] T005 [P] Record baseline numbers before any change: `dotnet build RoleRadar.sln -c Release -v q --nologo -warnaserror`, `pwsh ./scripts/test-fast.ps1`, `pnpm --filter web run typecheck`, `pnpm --filter web run test`. Paste the counts in the report so later phases compare against output, not memory

**Checkpoint**: Phase 3 may start once T004 passes. Until then only Phase 2 and Phase 5 proceed.

---

## Phase 2: Foundational, derived flags, filters, seed (plan Phase 1, serves US1 and US2)

**Purpose**: make every count and drill-down expressible in the 006 OData surface; build the fictional seed and independent oracle used by all later parity tests. Nothing user-visible yet. Blocks Phases 3 and 4.

### Tests first (run red, paste failing output)

- [X] T006 [P] Integration probe `Insights/TopZeroProbeTests.cs`: against a Testcontainers API host (existing `ODataTestHost` pattern, seeded with the existing `RemainingEntitiesSeed`), send `GET /odata/Postings?$top=0&$count=true` and `/odata/Applications?$top=0&$count=true` and assert-and-record the actual status. Expected per research R1: validation accepts 0 and Mongo rejects `$limit: 0`. The test must pass with whatever the real behavior is while documenting it; the decision stays `$top=1`
- [X] T007 [P] Unit tests in `tests/RoleRadar.Tests.Unit/ApplicationMilestoneTests.cs` (extend the existing file) for `ComputeApplicationMilestones` returning a record with `HasOffer`, `HasRejection`, `RejectedBeforeInterview`. Cases: no events; offer only; rejected with no interview (before = true); interview on an earlier day then rejected (before = false); rejected and interview on the same day (an interview dated on or before the rejection makes it "after", before = false); interview dated after the rejection (before = true); two rejected events (earliest wins); follow_up and withdrawn only (all false); `event_date` missing falls back to `created_at`
- [X] T008 [P] Unit tests `Insights/InsightsSeedGuardTests.cs` (no Docker needed): `InsightsSeedGuard.AssertIsolated` accepts only a host and port equal to the container's mapped endpoint and a database name matching `insights-parity-<guid>`; it rejects `localhost:27017`, a bare `roleradar` database name, a null connection string, and a name without the prefix
- [X] T009 [P] Architecture test `tests/RoleRadar.Tests.Architecture/DerivedFieldWriterArchitectureTests.cs`: scan `src/**/*.cs`; the field-name constants for `has_application`, `has_offer`, `has_rejection`, `rejected_before_interview` may appear only in `MongoApplicationRepository.cs`, `MongoPostingRepository.cs`, `ReadModelReconciliationService.cs`, `ReadModelClassMaps.cs`, `TrackingIndexInitializer.cs`, `TrackingBsonFieldNames.cs`; a reference in Api, Mcp.Stdio, Cli or Application handlers fails with the offending file name
- [X] T010 [P] Architecture test `tests/RoleRadar.Tests.Architecture/InsightsTestIsolationArchitectureTests.cs`: scan `tests/RoleRadar.Tests.Integration/Insights/*.cs`; none may mention `MongoConnectionStringSource`, `UserSecrets`, or read connection-string environment variables
- [X] T011 [P] Contract: confirm `tests/RoleRadar.Tests.Contract/ODataMetadataDriftTests.cs` goes red as soon as T027 adds the five read-model properties (the committed `contracts/odata/metadata.xml` is then stale); record that failing output, then it turns green at T031

### Seed and oracle (test support, no new test project)

- [X] T012 `Insights/InsightsSeedGuard.cs`: implement `AssertIsolated(connectionString, databaseName, containerEndpoint)` to make T008 pass; fails closed
- [X] T013 `Insights/InsightsSeed.cs`: deterministic builder with a fixed fictional `asOf` in September 2026 and fictional company names (for example "Northwind Examples"); writes through the real repositories and handlers so flags come from the choke point. Owner A: about 60 postings in every disposition and about 40 applications covering: every event type; rejection before, after and on the same day as an interview; offers with and without an interview; applications aged 13, 14, 29, 30 and 31 days; instants on both window edges; a posting first seen before the window but applied inside it; a posting first seen inside the window with an application dated after `asOf`. Owner B: rows that must never appear in owner A's counts. Owner C: nothing (zero-denominator case). A small-owner variant with fewer than 10 of everything. A legacy variant that writes posting and application documents without the five new fields (shape of pre-007 data)
- [X] T014 `Insights/InsightsOracle.cs`: independent in-memory implementation of the spec's metric definitions (reviewed, applied-to, expired, mature, ghosted, open, funnel stages, response events) over the seed objects. It must not call any production lens, repository or calculator code
- [X] T015 `Insights/InsightsFixture.cs`: xUnit fixture that starts its own Testcontainers Mongo (existing `ODataTestHost` and start-gate pattern), uses database name `insights-parity-<guid>`, calls `InsightsSeedGuard.AssertIsolated` before the first write, and exposes the API client and database

### Integration tests (run red)

- [X] T016 [P] `Insights/InsightsFlagsTests.cs`: seeded through the real repositories, assert stored `has_application`, `has_offer`, `has_rejection`, `rejected_before_interview` equal the oracle for every seeded row; assert `has_application` is false for postings without an application
- [X] T017 [P] `Insights/InsightsFlagSurvivalTests.cs`: after an application exists, editing the posting (title, score, transition to every disposition) keeps `has_application = true`; every application status and event change (all eight event types) leaves it unchanged; there is no code path that sets it back to false
- [X] T018 [P] `Insights/InsightsReconcileTests.cs`: load the legacy variant; dry run reports the expected updated counts and writes nothing; confirmed run writes the five fields; a second dry run reports `Postings Updated: 0` and `Applications Updated: 0`; run against the Testcontainers database only
- [X] T019 [P] `Insights/InsightsODataFilterTests.cs`: `$filter` cases for `hasApplication`, `firstSeenAt` (both edges), `hasOffer`, `hasRejection`, `rejectedBeforeInterview`, `firstResponseAt ne null`, `appliedAt lt <cutoff>`, and a combined lens-shaped filter; owner isolation for owners A and B; assert `ageInDays` is still rejected in `$filter` (400)
- [X] T020 [P] `Insights/InsightsIndexTests.cs` (extend `ODataIndexStartupTests` pattern): `ix_postings_owner_has_application` = `{owner_id, has_application, _id}` and `ix_postings_owner_first_seen_at` = `{owner_id, first_seen_at, _id}` exist after start-up, owner first
- [X] T021 [P] Extend `tests/RoleRadar.Tests.Integration/ODataDocsExamplesTests.cs` expectations: one executed example per new property in `docs/api/odata-queries.md` (fails until T031)

### Implementation (make the red tests pass)

- [X] T022 `src/RoleRadar.Infrastructure/Mongo/TrackingBsonFieldNames.cs`: add constants for the four new field names (the `first_seen_at` name already exists or is added here)
- [X] T023 `src/RoleRadar.Infrastructure/Mongo/Repositories/MongoApplicationRepository.cs`: change `ComputeApplicationMilestones` to return a record `ApplicationMilestones(HasInterview, FirstResponseAt, LastEventAt, HasOffer, HasRejection, RejectedBeforeInterview)`; `HasOffer` = any `offer` event; `HasRejection` = any `rejected` event; `RejectedBeforeInterview` = rejection exists and no `interview` event dated on or before the earliest rejected event (same day counts as after), false when `HasRejection` is false; event ordering uses `event_date` then `created_at` exactly as today. Update every caller (make T007 pass)
- [X] T024 `MongoApplicationRepository.cs`: `SerializeApplication` emits the three flags; `AppendEventAsync` `$set`s all milestone fields including the three flags in the same update
- [X] T025 `MongoApplicationRepository.cs`: in `UpsertApplicationAsync`, after the application write and in the same session, `$set has_application = true` on the parent posting filtered by `owner_id` and `_id`
- [X] T026 `src/RoleRadar.Infrastructure/Mongo/Repositories/MongoPostingRepository.cs`: `UpsertPostingAsync` computes `has_application` with one existence query on `ux_applications_owner_posting` (`owner_id`, `posting_id`) before serializing, and passes it to `SerializePosting`, which emits the field, so a full replace never wipes it (make T016, T017 pass)
- [X] T027 `src/RoleRadar.Application/Reading/Models/PostingReadModel.cs` add public `bool HasApplication` and `DateTimeOffset FirstSeenAt`; `ApplicationReadModel.cs` add public `bool HasOffer`, `bool HasRejection`, `bool RejectedBeforeInterview`
- [X] T028 `src/RoleRadar.Infrastructure/Mongo/Reading/ReadModelClassMaps.cs`: map `has_application`, `first_seen_at`, `has_offer`, `has_rejection`, `rejected_before_interview`; none added to any `$orderby` allowlist and none added to any `DisallowedFilter` set
- [X] T029 `src/RoleRadar.Infrastructure/Mongo/TrackingIndexInitializer.cs`: create `ix_postings_owner_has_application` `{owner_id: 1, has_application: 1, _id: 1}` and `ix_postings_owner_first_seen_at` `{owner_id: 1, first_seen_at: 1, _id: 1}`, owner first (make T020 pass)
- [X] T030 `src/RoleRadar.Infrastructure/Mongo/Reconcile/ReadModelReconciliationService.cs`: `ReconcilePostingsAsync` recomputes `has_application` from the applications collection; `ReconcileApplicationsAsync` recomputes the three flags via `ComputeApplicationMilestones`; counts keep their current names, no new CLI flags, dry run still writes nothing (make T018 pass)
- [X] T031 Regenerate `contracts/odata/metadata.xml` and update `docs/api/odata-queries.md` with one example per new property (make T011, T021 pass)
- [X] T032 [P] Update `docs/operations/reconcile-read-models.md` field lists and expected dry-run output for the five fields; keep the run order deploy, backup, dry run, confirm, dry run reports 0, and the rule that only Matt runs it on real data
- [X] T033 Make T009, T010, T019 pass; if T019 shows a lens-shaped filter exceeding node or depth limits, report it instead of widening limits
- [X] T034 Update ADR 0029 with the recorded `$top=0` result from T006 and confirm ADR 0031 matches what was built (status stays Proposed; Matt moves it)
- [X] T035 **Boundary (Phase 2)**: run `dotnet build RoleRadar.sln -c Release -v q --nologo -warnaserror`, `pwsh ./scripts/test-fast.ps1`, `dotnet test tests/RoleRadar.Tests.Integration -c Release --filter "FullyQualifiedName~Insights|FullyQualifiedName~OData"`, then `pwsh ./scripts/review-gate.ps1`. Report counts from output, list anything not run, and stop
- [X] T036 [Matt] (done 2026-11-04: dry run 247 postings and 22 applications to update, confirmed run updated them, second dry run reported 0 for postings, applications and criteria) Operator step after Phase 2 merges and deploys: `roleradar backup`, `roleradar reconcile-read-models`, `roleradar reconcile-read-models --confirm RECONCILE-READ-MODELS`, dry run again must report 0 updated; eyeball the all-time posting count and that imported postings' first-seen dates look real (research R5). Not for agy, subagents or CI

**Checkpoint**: flags, filters, seed and oracle exist; Phases 3 and 4 may start (Phase 3 also needs T004).

---

## Phase 3: US1 Funnels and rates, US2 Drill-down (plan Phase 2)

**Goal**: `/insights` with two single-cohort funnels and rate tiles, every count a link to a list with the identical filter.

**Independent test**: with the fictional fixture, both funnels and all tiles show oracle values for both windows, every bar nests, and following any bar or tile link shows a list whose total equals the number.

### Tests first (run red, paste failing output)

- [X] T037 [P] [US2] `web/tests/unit/insights-lenses.test.ts`: for every one of the 19 lens keys (18 counted plus the drill-only `offers-without-interview`) and both windows, the exact `$filter` string; window clause `ge <asOf-89d>T00:00:00Z and lt <asOf+1d>T00:00:00Z` (90d) and `lt <asOf+1d>` only (all); cutoffs `mature` `appliedAt lt <asOf-13d>`, `ghost-eligible` `lt <asOf-29d>`, `ghosted` `lt <asOf-30d>`; posting lenses window on `firstSeenAt`, application lenses on `appliedAt`; count query is `$top=0&$count=true` (count only, no `$select`); `parseLensParams` ignores unknown keys, wrong-entity keys, bad `window`, bad `asOf` (not a date, more than 1 day in the future); no Domain string literal is typed in the module (values come from `dispositions.ts` and `application-enums.ts`)
- [X] T038 [P] [US2] `web/tests/unit/insights-lenses-snapshot.test.ts`: lens filters equal `contracts/insights-lenses.json` (fails until T052); with `UPDATE_SNAPSHOTS=1` it rewrites only the `filters` section
- [X] T039 [P] [US1] `web/tests/unit/insights-view.test.ts`: rates render "x of y"; denominator under 10 is `small_sample`; 0 is `no_data` and never NaN or a percentage; Application rate = `applied-to` over `reviewed` and is at most 100%; the interview rate (mature) and the all-time interview rate tile (`interviewed` over `applied`, always window `all`); rejection split; both offer rates (`offers-interviewed` over `interviewed`, `offers` over `applied`); ghost rate; coverage checks gate flag-dependent numbers and produce the "needs backfill" state with the exact operator command
- [X] T040 [P] [US1] Same file or `web/tests/unit/insights-funnels.test.ts`: Funnel A stages `leads`, `reviewed`, `applied-to`; Funnel B stages `applied`, `responded`, `interviewed`, `offers-interviewed`; for each funnel and both windows a stage count at most its predecessor; a violating input makes the view model return an error state, never an inverted bar; the "offers without an interview" note equals `offers` minus `offers-interviewed`
- [X] T041 [P] [US2] `web/tests/routes/postings.lens.test.ts` and `web/tests/routes/applications.lens.test.ts`: with `?lens=...&window=...&asOf=...` the `$filter` the loader sends equals `lensFilter` output and equals the dashboard count's `$filter` (structural parity); the lens is ANDed with `q`, `status` and `hasApplication`; the lens chip data is present; a lens for the other entity and a raw `$filter` param are ignored; paging and sort clamping unchanged
- [X] T042 [P] [US1] `web/tests/routes/insights.test.ts`: the loader issues 22 OData count requests for the 90 day view (18 counted lenses, 2 coverage checks and the 2 all-time interview counts) and 20 for the all-time view with at most 6 in flight and a 5 second timeout each; one failing request degrades only the tiles that depend on it; 401 re-login once with no loop, 403 goes to `/not-authorized`, 404 handled via `assertApiOk`; `asOf` defaults to today UTC and is echoed into every link
- [X] T043 [P] [US2] `web/tests/routes/postings.component.test.tsx` (extend): a "Has application" filter (any, yes, no) maps to `hasApplication eq true` or `eq false` and round-trips through the URL
- [X] T044 [P] [US2] `web/tests/architecture/insights-filters.test.ts`: filter strings and `$filter` construction for lenses exist only in `web/app/lib/insights-lenses.ts`; the Insights route and both list loaders import from it; `web/app/ui.ts` remains the only importer of `web/app/ds`
- [X] T045 [P] [US1] `web/tests/components/insights-page.test.tsx`: renders both funnels with their cohort titles ("Postings first seen in window", "Applications applied in window"), the explanation that Funnel A's applied-to and Funnel B's applied can differ, the reviewed definition and its limit note, each funnel's table view, and no text node from untrusted data treated as markup
- [X] T046 [P] [US1] `web/tests/e2e/insights.spec.ts` (Chromium, loopback stub API, reduced-motion helper): both funnels and tiles render; window toggle changes the URL and numbers; keyboard operation of bars, links and table view; axe clean in light and dark at 1280 px and 360 px; no horizontal page scroll at 360 px
- [X] T047 [P] [US2] `web/tests/e2e/insights-drilldown.spec.ts`: for each of the 7 funnel bars, the offers-without-interview note and every counted tile, in both windows, following the link lands on a list whose displayed total equals the number on the dashboard
- [X] T048 [P] [US2] .NET `Insights/InsightsParityTests.cs`: for every lens in `contracts/insights-lenses.json` and both windows, `@odata.count` over the real API equals the oracle, equals the snapshot's `expected`, and equals the list request's (`$top=25&$count=true`) `@odata.count`; nesting holds for both funnels and both windows against the oracle and the API; boundary rows at 13, 14, 29, 30 and 31 days and both window-edge instants agree with the `ageInDays` the applications list returns; owner B and owner C counts never leak into owner A; `open` equals "no terminal event" for every terminal event type
- [X] T049 [P] [US2] .NET `Insights/InsightsLensesContractTests.cs`: the lens keys in `contracts/insights-lenses.json` equal the oracle's key set; with `UPDATE_SNAPSHOTS=1` it rewrites only the `expected` section from the oracle
- [X] T050 [P] [US1] Isolation guard test for web e2e: `web/tests/e2e/insights-isolation.spec.ts` asserts `API_BASE_URL` is loopback and equals the stub's port, and that the `real-stack` project is excluded from the Insights specs; add the stub `web/tests/e2e/support/insights-stub.ts` and the fictional fixture `web/tests/fixtures/insights-fictional.json` (counts from the oracle snapshot, keyed by `$filter` string)

### Implementation (make the red tests pass)

- [X] T051 [US2] `web/app/lib/insights-lenses.ts`: lens catalog per `data-model.md` and `contracts/insights-lenses.md` (18 keys, `lensEntity`, `lensFilter` via the typed `odata-client` builder and `toODataDateTime`, `lensCountQuery`, `lensListHref`, `parseLensParams`, `funnelStages`, one shared cutoff helper); values imported from `dispositions.ts` and `application-enums.ts` (make T037 pass)
- [X] T052 [US2] Generate `contracts/insights-lenses.json` (filters via `UPDATE_SNAPSHOTS=1` on T038, expected via T049) and make T038, T048 and T049 pass
- [X] T053 [US1] `web/app/lib/insights-view.ts`: rates, sample flags, funnel view models with the nesting guard, coverage gating, "offers without an interview" note; takes counts as input, no I/O (make T039, T040 pass)
- [X] T054 [US1] `web/app/lib/insights-counts.server.ts`: runs the count requests (22 in the 90 day view, 20 in the all-time view) through `fetchOData`, concurrency 6, 5 second timeout, per-request failure isolation, `assertApiOk` handling (make T042 pass)
- [X] T055 [P] [US2] `web/app/routes/postings._index.tsx`: accept `?lens=&window=&asOf=` through `parseLensParams`, AND with existing filters, render the removable lens chip, add the "Has application" filter control (`hasApplication eq true|false`, the "postings I applied to" view) (make T041, T043 pass)
- [X] T056 [P] [US2] `web/app/routes/applications._index.tsx`: same lens support and chip (make T041 pass)
- [X] T057 [US1] `web/app/ui.ts`: export `StatTile`, `BarChart`, `ColumnChart`, `Sparkline` with prop types from the vendored declarations (requires T004; do not edit `web/app/ds`)
- [X] T058 [US1] `web/app/routes/insights.tsx`: loader, window toggle as a link or `<Form method="get">` (shareable URL), two funnels using `BarChart` funnel mode with a table view each, `StatTile`s for every rate with "x of y" and "small sample", offers-without-interview note, definitions and limits help text, posting-window help text per FR-008a (first-seen date is when RoleRadar first recorded a posting; postings imported in bulk share one date, so the window can group them; no real dates or counts), coverage "needs backfill" state; React text nodes only; every number a link built by `lensListHref` (make T042, T045 pass)
- [X] T059 [US1] `web/app/routes.ts` add `route("insights", "routes/insights.tsx")`; `web/app/lib/nav.ts` add the nav item (keep `web/tests/architecture/nav-consistency.test.ts` green)
- [X] T060 [P] [US1] `web/app/app.css`: classes for the insights layout using `--ds-*` tokens and the four `--ds-chart-*` tokens only, light and dark, phone width, no inline `style` attributes (CSP)
- [X] T061 [US1] Make T046, T047, T050 pass against the loopback stub; capture fictional screenshots from the stub run only and review each image before adding any to the repo
- [X] T062 **Boundary (Phase 3)**: `pnpm --filter web run typecheck`, `lint`, `test`, `build`, `test:e2e`, then `pwsh ./scripts/test-fast.ps1`, `pre-commit run --all-files`, `pwsh ./scripts/review-gate.ps1`. Report counts from output, list anything not run, and stop

**Checkpoint**: US1 and US2 are complete and demonstrable; an MVP can ship here.

---

## Phase 4: US3 Trends, US4 Response time (plan Phase 3)

**Goal**: weekly applications column chart, search-run yield sparkline, median response time, from the stats read (ADR 0030).

**Independent test**: with the seed, stats values equal hand-computed values and `responseTime.sampleSize` equals the `responded` lens count for the same window.

### Tests first (run red, paste failing output)

- [X] T063 [P] [US4] `tests/RoleRadar.Tests.Unit/Insights/InsightsStatsCalculatorTests.cs`: odd and even medians (even = mean of the two middle values), empty (`null`), single value, zero days, response dated before applied clamped to 0, whole UTC days
- [X] T064 [P] [US3] Same file: Monday-start UTC week buckets, Sunday and Monday edge instants, zero-filled empty weeks, only in-window applications counted at the window edges, run yield oldest first, 120-run cap sets `truncated`
- [X] T065 [P] [US3] `tests/RoleRadar.Tests.Unit/Insights/GetInsightsStatsQueryTests.cs`: handler reads only through `IOwnerReadSeam`; above 50,000 projected rows from either source it returns a typed `Result` failure and does not throw; `asOf` more than 1 day in the future is a typed validation failure
- [X] T066 [P] [US3] Architecture test `tests/RoleRadar.Tests.Architecture/InsightsStatsArchitectureTests.cs`: the stats handler's assembly dependencies exclude MongoDB types and it is registered as an `IQueryHandler`, not wrapped as a command; Api still has zero MongoDB references (existing test must stay green)
- [X] T067 [P] [US3] `Insights/InsightsStatsTests.cs`: endpoint values equal hand-computed seed values for both windows; `responseTime.sampleSize` equals the `responded` lens count for the same window; owner isolation (A, B, C); GET only (other methods 405 with `Allow: GET`); bad or missing `window` or `asOf` is 400 with the existing error envelope
- [X] T068 [P] [US3] Contract: `tests/RoleRadar.Tests.Contract/Api/` OpenAPI snapshot drift test fails until `contracts/openapi/roleradar-api.json` and `contracts/client/api-types.ts` are regenerated (ADR 0019); `window` is a string enum in the schema, `asOf` is `format: date`
- [X] T069 [P] [US3] `web/tests/unit/insights-stats.test.ts` and `web/tests/routes/insights.stats.test.ts`: typed fetch uses the generated types; the stats request starts with the first wave of counts (request totals asserted exactly per view: 23 in the 90 day view, 21 in the all-time view, i.e. 22 and 20 counts plus 1 stats); a stats failure degrades only the trend and median tiles; labels "showing latest 120 runs" when `truncated`
- [X] T070 [P] [US3] `web/tests/components/insights-trends.test.tsx` and `web/tests/e2e/insights-trends.spec.ts`: `ColumnChart` of weekly applications and `Sparkline` of run yield each with a table view and keyboard operation; the median tile shows days and "n responses" or "no data"; light, dark, 360 px; axe clean with the reduced-motion helper; the isolation guard from T050 still applies

### Implementation (make the red tests pass)

- [X] T071 [US3] `src/RoleRadar.Application/Insights/InsightsStatsCalculator.cs`: pure static functions for median, Monday-UTC weekly buckets and run series (make T063, T064 pass)
- [X] T072 [US3] `src/RoleRadar.Application/Insights/GetInsightsStatsQuery.cs` and `InsightsStatsDto.cs`: handler projects `(AppliedAt, FirstResponseAt)` from `IOwnerReadSeam.QueryApplications` and `(RunDate, NewPostings)` from `QuerySearchRuns`, 50,000-row cap with a typed failure, window math identical to the web lens module (`[asOf-89d, asOf+1d)`); register like the existing tracker query handlers (make T065, T066 pass)
- [X] T073 [US3] `src/RoleRadar.Api/Endpoints/InsightsEndpoints.cs`: `GET /api/v1/insights/stats?window=&asOf=` per `contracts/insights-stats.md`, `ReadPolicyName`, explicit types for every parameter, wired in `Program.cs` (make T067 pass)
- [X] T074 [US3] Regenerate `contracts/openapi/roleradar-api.json` and `contracts/client/api-types.ts` per ADR 0019 (make T068 pass)
- [X] T075 [US3] `web/app/lib/insights-stats.server.ts`: typed stats call using the generated types and `assertApiOk` (make T069 pass)
- [X] T076 [US4] Extend `web/app/routes/insights.tsx`: median response time `StatTile` (days, "n responses", "no data"), weekly applications `ColumnChart` with table view, run-yield `Sparkline` with table view and the "showing latest 120" label; start the stats request with the first wave (make T070 pass)
- [X] T077 [P] [US3] `web/app/app.css`: trend and chart classes with tokens only
- [X] T078 [US3] Update ADR 0030 with measured numbers (rows projected and time on the seed) and confirm it matches what was built; status stays Proposed
- [X] T079 **Boundary (Phase 4)**: same gate as T062 plus `dotnet test tests/RoleRadar.Tests.Contract -c Release --filter "FullyQualifiedName~OpenApi"`. Report counts from output, list anything not run, and stop

**Checkpoint**: US3 and US4 complete and independently verifiable.

---

## Phase 5: US5 Notes Markdown (plan Phase 4, independent, can ship alone)

**Goal**: note detail renders the body as safe Markdown. Does not depend on Phases 2 to 4 and may run in parallel with them.

**Independent test**: open the fictional hostile note; formatting renders, nothing executes, no off-origin request, tables scroll on a phone.

### Tests first (run red, paste failing output)

- [ ] T080 [P] [US5] `web/tests/components/markdown-body.hostile.test.tsx`: inputs `<script>`, `<img src=x onerror=...>`, raw HTML blocks, `[x](javascript:...)`, `[x](data:text/html,...)`, `[x](vbscript:...)`, `[x](JaVaScRiPt:...)`, whitespace and control-character obfuscated schemes, `//evil.example`, relative links, `![alt](https://img.example/a.png)`. Assert no `script`, `img`, `iframe`, `style` element and no `on*` attribute in the DOM; disallowed links render as plain text; allowed `http` and `https` links carry `rel="noopener noreferrer"`; image syntax renders alt text only
- [ ] T081 [P] [US5] `web/tests/components/markdown-body.format.test.tsx`: headings, lists, GFM table inside a focusable labelled horizontal scroll region, task list with disabled checkboxes, strikethrough, autolink literal, a single newline produces a line break; a body over 256 KiB renders as plain preformatted text with a notice
- [ ] T082 [P] [US5] `web/tests/security/markdown-invariants.test.ts`: `rehype-raw` absent from `web/package.json` and the lockfile; no `dangerouslySetInnerHTML` under `web/app`; `react-markdown` 10.1.0, `remark-gfm` 4.0.1 and `remark-breaks` 4.0.0 are exact-pinned (no `^` or `~`); a licence audit script over the resolved tree fails on any licence outside MIT, ISC, BSD-2-Clause, BSD-3-Clause, Apache-2.0, 0BSD
- [ ] T083 [P] [US5] `web/tests/e2e/notes-markdown.spec.ts`: a hostile fictional note via the loopback stub makes zero requests to any origin other than the app's, zero CSP violations, axe clean (reduced-motion helper), and no horizontal page scroll at 360 px with a wide table; the stub's fictional note lives in `web/tests/fixtures/notes-markdown-fictional.json`
- [ ] T084 [US5] Verify first: add the three exact-pinned dependencies in `web/package.json` (`react-markdown` 10.1.0, `remark-gfm` 4.0.1, `remark-breaks` 4.0.0), install, and run `pnpm --filter web run build` before writing the component. If the ESM-only packages need a Vite SSR setting, record it in ADR 0032; if the build fails for another reason, stop and report

### Implementation (make the red tests pass)

- [ ] T085 [US5] `web/app/components/MarkdownBody.tsx`: `react-markdown` with `remark-gfm` and `remark-breaks`, no rehype plugins, `allowedElements` allowlist (paragraphs, headings, lists, blockquote, pre, code, em, strong, del, hr, br, a, table parts, task-list input), custom `urlTransform` and `a` accepting only absolute `http:` and `https:` with `rel="noopener noreferrer"`, `img` rendering alt text only, disabled task checkboxes, table wrapper region, 256 KiB plain-text fallback (make T080, T081 pass)
- [ ] T086 [US5] `web/app/routes/notes.$id.tsx`: render the note body through `MarkdownBody`; heading, tags and metadata stay React text nodes (make T083 pass)
- [ ] T087 [P] [US5] `web/app/app.css`: markdown classes using `--ds-*` tokens only, no inline `style` attributes
- [ ] T088 [US5] Update ADR 0032 with the build-verification result from T084 and the licence audit output; status stays Proposed
- [ ] T089 **Boundary (Phase 5)**: same web gate as T062. Report counts from output, list anything not run, and stop

**Checkpoint**: US5 ships independently of the Insights page.

---

## Phase 6: Polish and cross-cutting

- [ ] T090 [P] Run the quickstart sections for every shipped phase and report each expected outcome as met or not, from command output
- [ ] T091 [P] Review every fixture, test name, screenshot and report for real names or data; run `pre-commit run --all-files` and `pwsh ./scripts/denylist-check.ps1`
- [ ] T092 [P] Performance check on the seed with at least 1,000 postings and 300 applications: Insights ready within 2 seconds (SC-008); if not met, report the slowest requests rather than changing ADR 0029
- [ ] T093 [P] Verify SC-001 to SC-009 each against evidence from a named test or command in a short table in the final report
- [ ] T094 Final `pwsh ./scripts/review-gate.ps1`; report numbers from its output; leave all changes uncommitted in the working tree for review
- [ ] T095 [Matt] Review ADRs 0029 to 0032 and move each from Proposed to Accepted when satisfied; run the T036 operator step on the real database before trusting the dashboard

---

## Dependencies and execution order

- **Phase 1** (kit gate): T001 to T004 are sequential and external; T005 is parallel. Only Phase 3 chart and tile tasks wait on it (T057 onward and anything rendering charts).
- **Phase 2** blocks Phases 3 and 4. Inside it: T006 to T011 first (red), then seed T012 to T015, then T016 to T021 (red), then implementation T022 to T034, then T035. T036 is Matt's, after merge.
- **Phase 3** needs Phase 2 and T004. **Phase 4** needs Phase 2; its web tasks (T069, T070, T075, T076) need Phase 3's route and the kit. **Phase 5** needs nothing from earlier phases.
- Within a phase: tests before implementation; models and flags before filters; lens module (T051) before the route and loaders (T054 to T058); calculator (T071) before handler (T072) before endpoint (T073) before OpenAPI regeneration (T074) before the web stats client (T075).

### Parallel opportunities

- Phase 2: T006 to T011 together; T016 to T021 together after T015; T032 any time after T030.
- Phase 3: T037 to T050 together (distinct files); T055 and T056 together after T051; T060 any time.
- Phase 4: T063 to T070 together; T077 any time.
- Phase 5: T080 to T083 together; whole phase parallel to Phases 2 to 4.
- Phase 6: T090 to T093 together.

## Implementation strategy

1. **MVP = Phases 1 to 3** (US1 and US2): trustworthy funnels, rates and verified drill-down. Stop and review at T062.
2. Add **Phase 4** (US3, US4) for trends and median, stop at T079.
3. **Phase 5** (US5) is a separate pull request and can land first, last or in between.
4. Matt runs the real-database backfill (T036) before using the dashboard on live data; until then the page shows "needs backfill" by design.
