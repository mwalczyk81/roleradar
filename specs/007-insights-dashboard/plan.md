# Implementation Plan: Insights Dashboard

**Branch**: `007-insights-dashboard` | **Date**: 2026-11-04 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/007-insights-dashboard/spec.md`

## Summary

A read-only `/insights` page showing the funnel, rates, trends and response time, plus Markdown rendering of note bodies as a separate story. Built on the 006 OData surface:

- **Counts** are OData `$count` requests (`$top=0`, count only), built by one shared lens module that also builds the drill-down link, so a number and its list agree by construction (ADR 0029). The page shows **two single-cohort funnels** (Postings first seen in window: leads, reviewed, applied-to; Applications applied in window: applied, responded, interviewed, offers), never one mixed funnel.
- **Median response time, weekly buckets and run yield** come from one small read-only endpoint over `IOwnerReadSeam`, computed in memory from date-only projections (ADR 0030).
- **New list filters** come from five derived stored fields written only at the repository choke point and backfilled by `reconcile-read-models` (ADR 0031).
- **Markdown** uses pinned `react-markdown` + `remark-gfm` + `remark-breaks` with an element allowlist and no HTML parsing (ADR 0032).
- Work is phased so each priority ends at a reviewable boundary, tests first with the failing output shown.

Findings that shaped this plan are in [research.md](./research.md); the entity and lens catalog is in [data-model.md](./data-model.md); the interface contracts are in [contracts/](./contracts/).

## Technical Context

**Language/Version**: C# 13 / .NET 10 (backend); TypeScript 5.9 / Node 22 (web, React Router v7 SSR)

**Primary Dependencies**: existing `Microsoft.AspNetCore.OData` 9.5.0 and `MongoDB.Driver` 3.13.0 (unchanged). New web runtime dependencies, Phase 4 only: `react-markdown` 10.1.0, `remark-gfm` 4.0.1, `remark-breaks` 4.0.0 (exact pins, all MIT, checked against the registry 2026-11-04). No new NuGet packages. Design kit v20 chart components copied into `web/app/ds` (copy-only).

**Storage**: MongoDB 8.0 replica set. No new collections. Five derived fields (research R4, R7) and two new indexes.

**Testing**: xUnit v3, NetArchTest.Rules, Testcontainers.MongoDb (isolated containers only), Vitest, Playwright Chromium with the shared reduced-motion axe helper. No new test projects.

**Target Platform**: Windows dev, Linux containers; owner's browser, light and dark, phone width (360 px).

**Project Type**: monorepo: .NET Application/Infrastructure/Api plus the React Router web app.

**Performance Goals**: Insights page ready within 2 seconds on a seed of at least 1,000 postings and 300 applications (SC-008). 20 OData counts at concurrency 6 plus 1 stats call.

**Constraints**: read-only; no new write paths; owner-scoped through `IOwnerReadSeam`; `$top=0` is the count-only form (R1, fixed in 006's pipeline); no raw `$filter` from URLs; fictional data only; the real database is never read by tests or screenshots.

**Scale/Scope**: single owner; hundreds of applications, low thousands of postings and search runs. In-memory stats cap of 50,000 rows per source with a typed failure above it (ADR 0030).

### Requests per page load

| Phase | OData `$count` requests | Stats requests | Total | Concurrency |
| --- | --- | --- | --- | --- |
| After Phase 2 | 20 (18 lens counts + 2 read-model coverage checks; unchanged by the two-funnel layout because `applied-to` is the existing application-rate numerator lens, renamed) | 0 | 20 | 6 |
| After Phase 3 | 20 | 1 | 21 | 6 (stats runs first, alongside the first wave) |

Each request has a 5 second timeout; one failing request marks only the tiles that depend on it "unavailable".

## Constitution Check

*GATE: passed before Phase 0 research; re-checked after Phase 1 design. No violations.*

| Principle | Result | How |
| --- | --- | --- |
| I Privacy first | Pass | Seed, fixtures, screenshots and reports are fictional. Isolation guard test proves tests never reach the real database (Seed and isolation section). Gitleaks and denylist hooks run in `pre-commit`. |
| II Tests first; isolated containers | Pass | Every phase starts red with the failing output captured. Integration uses Testcontainers only. |
| III Layering enforced | Pass | Stats is an Application `IQueryHandler` behind a thin endpoint; Api still has zero MongoDB references (existing test). New architecture tests: field-writer allowlist, stats handler uses only `IOwnerReadSeam`. |
| IV Named seams only | Pass | No new seam method, no generic repository, no mediator. Reads use existing `IOwnerReadSeam` queries. Decisions recorded in ADRs 0029 to 0031. |
| V Untrusted text; narrow audited tools | Pass | Note Markdown is an allowlist with no HTML parsing (ADR 0032). No MCP tool added. No write path, so no audit entries needed; derived-field writes are inside existing audited transactions. |
| VI stdout hygiene | Pass | No MCP host change. |
| VII Durable data; destructive scripts confirm | Pass | Reconcile keeps dry-run default and `--confirm RECONCILE-READ-MODELS`. Only Matt runs it on real data. |
| VIII ADRs; pinned versions | Pass | ADRs 0029 to 0032 (Proposed). Markdown packages pinned exactly; no NuGet or image change. |
| IX Conventions | Pass | .NET 10, nullable, warnings as errors, UTC everywhere, `owner_id` first in new indexes, UUIDv7 unchanged. |

AGENTS.md standing rules honored: contract first (generated types, snapshots with drift tests), vendored design system copy-only, no `any` or silencing casts, `validateCsrfToken` on any action (the Insights page has none; notes page unchanged), all posting/company/application text rendered as React text nodes, shared helpers (`formatDate`, `useTheme`, `nav.ts`) reused, list pages read through OData with the typed builder and clamped paging, allowlisted sorts only.

## Project Structure

### Documentation (this feature)

```text
specs/007-insights-dashboard/
├── spec.md
├── plan.md                    # this file
├── research.md                # Phase 0
├── data-model.md              # Phase 1
├── quickstart.md              # Phase 1
├── contracts/
│   ├── insights-stats.md
│   ├── insights-lenses.md
│   └── odata-additions.md
├── checklists/requirements.md
└── tasks.md                   # /speckit-tasks, not created here
docs/adr/0029..0032            # Proposed, written with this plan
```

### Source code (what changes, by phase)

```text
src/RoleRadar.Application/
  Reading/Models/PostingReadModel.cs            # + HasApplication, FirstSeenAt              (P1)
  Reading/Models/ApplicationReadModel.cs        # + HasOffer, HasRejection, RejectedBeforeInterview (P1)
  Insights/                                     # NEW                                         (P3)
    GetInsightsStatsQuery.cs, InsightsStatsCalculator.cs, InsightsStatsDto.cs
src/RoleRadar.Infrastructure/Mongo/
  Repositories/MongoApplicationRepository.cs    # ApplicationMilestones record; set has_application (P1)
  Repositories/MongoPostingRepository.cs        # compute has_application before serialize     (P1)
  Reconcile/ReadModelReconciliationService.cs   # backfill the five fields                     (P1)
  Reading/ReadModelClassMaps.cs                 # map new element names                        (P1)
  TrackingIndexInitializer.cs                   # two new postings indexes                     (P1)
  TrackingBsonFieldNames.cs                     # field-name constants for the allowlist test  (P1)
src/RoleRadar.Api/
  Endpoints/InsightsEndpoints.cs                # GET /api/v1/insights/stats                   (P3)
  OpenApi snapshot + contracts/client/api-types.ts regenerated                                (P3)
contracts/
  odata/metadata.xml                            # regenerated                                  (P1)
  insights-lenses.json                          # NEW snapshot, filters + expected counts      (P2)
docs/
  api/odata-queries.md, operations/reconcile-read-models.md                                   (P1)

web/
  app/lib/insights-lenses.ts                    # NEW, the shared lens module                  (P2)
  app/lib/insights-view.ts                      # NEW, rates, sample flags, coverage logic     (P2)
  app/lib/insights-stats.server.ts              # NEW, typed stats fetch                       (P3)
  app/routes/insights.tsx                       # NEW                                          (P2, extended P3)
  app/routes/postings._index.tsx, applications._index.tsx   # lens param + chip; "Has application" filter (P2)
  app/routes.ts, app/lib/nav.ts                 # route + nav item                             (P2)
  app/ds/**                                       # kit v20 sync, copy-only                      (Phase 0)
  app/ui.ts                                     # export StatTile, BarChart, ColumnChart, Sparkline (P2)
  app/components/MarkdownBody.tsx               # NEW                                          (P4)
  app/routes/notes.$id.tsx                      # render body through MarkdownBody             (P4)
  app/app.css                                   # insights and markdown classes, ds tokens only
  tests/** (unit, routes, architecture, e2e, fixtures)

tests/
  RoleRadar.Tests.Unit/        # milestones, calculator, lens filters
  RoleRadar.Tests.Architecture/# field-writer allowlist, stats handler dependencies, test isolation
  RoleRadar.Tests.Contract/    # OData metadata drift, OpenAPI drift
  RoleRadar.Tests.Integration/Insights/   # NEW folder: InsightsSeed, InsightsOracle, InsightsSeedGuard, parity + stats tests
```

**Structure Decision**: existing layout; no new projects. New code follows the layer rules: Domain unchanged, Application holds the stats query and calculator, Infrastructure holds the choke-point writes and reconcile, Api and web stay thin.

## Phase 0: Design kit v20 sync (Claude and Matt, before web phases 2 and 3)

**Owner**: Claude exports from the published kit while Matt is signed in to claude.ai in the browser pane; agy cannot reach claude.ai and must not attempt it. **Gate**: Phase 2 web tasks that touch charts or tiles do not start until `web/app/ds/SYNC.md` names kit v20. Phase 1 (backend) and Phase 4 (Markdown) do not depend on it and can proceed in parallel.

Rules: copy only, unmodified, `project/` prefix removed; never hand-edit anything under `web/app/ds`; update `SYNC.md` (kit version, sync date, "synced by", Status paragraph for the chart additions, and the sha256 list for every changed or new file); review the diff like any other change.

Expected files (confirm exact paths against the kit export; the kit's existing layout groups sources by folder under `components/src/`):

| Path under `web/app/ds/` | Expected change |
| --- | --- |
| `components/src/data/StatTile.jsx` | new (or `components/src/charts/` if the kit groups charts separately) |
| `components/src/data/BarChart.jsx` | new, includes funnel mode |
| `components/src/data/ColumnChart.jsx` | new |
| `components/src/data/Sparkline.jsx` | new |
| `components/StatTile/StatTile.d.ts`, `components/StatTile/README.md` | new |
| `components/BarChart/BarChart.d.ts`, `components/BarChart/README.md` | new |
| `components/ColumnChart/ColumnChart.d.ts`, `components/ColumnChart/README.md` | new |
| `components/Sparkline/Sparkline.d.ts`, `components/Sparkline/README.md` | new |
| `components/index.d.ts` | updated exports |
| `components/bundle.css` | updated only if the kit's chart styles live there |
| `tokens.css`, `tokens.json` | four new `--ds-chart-*` tokens, light and dark |
| `README.md` | updated only if the kit brand book changed |
| `SYNC.md` | version v20, date, status, hashes |

Not vendored (unchanged from today): `bundle.js`, `preview.html`, `ui_kits/`, `templates/`, `archived/`.

Done when: a file-listing diff of `web/app/ds` matches the table (anything extra is a stop-and-ask), and a vitest/architecture check confirms every file under `web/app/ds` has a matching hash line in `SYNC.md` (add that check in Phase 2 task 1 if it does not already exist). Agy then adds the four components to `web/app/ui.ts` (that file is not copy-only) in Phase 2.

## Phase 1: P1 backend, derived flags and filters (ends at a reviewable boundary)

**Goal**: counts and drill-downs become expressible in the 006 OData surface; nothing user-visible yet.

**Red first** (run, capture failing output, then implement):
1. Integration: `$top=0` against Postings and Applications; record the actual result into ADR 0029.
2. Unit: `ComputeApplicationMilestones` returns `HasOffer`, `HasRejection`, `RejectedBeforeInterview` for: no events; offer only; rejection with no interview; interview then rejection on a later day; rejection and interview same day (counts as after); interview after rejection; multiple rejections (earliest wins); follow-up and withdrawn only.
3. Integration (Testcontainers): seeded through the real repositories, assert the five stored fields; posting edit after an application exists keeps `has_application`; status and event changes never change it; legacy documents without the fields get them from reconcile (dry run reports the count, confirmed run fixes, second dry run reports 0).
4. Integration: OData filter cases for each new property and for `firstSeenAt` edges, owner isolation for both owners; docs examples executed by `ODataDocsExamplesTests`.
5. Contract: `ODataMetadataDriftTests` fails until `contracts/odata/metadata.xml` is regenerated.
6. Architecture: the field-name constants for the five fields appear only in the allowlisted writer, mapper, reconcile and index files; fails on any other reference.
7. Integration: index start-up test lists `ix_postings_owner_has_application` and `ix_postings_owner_first_seen_at`.

**Then**: implement per data-model.md; extend reconcile (examined/updated counts keep their current names, no new CLI flags); update `docs/api/odata-queries.md` and `docs/operations/reconcile-read-models.md`; regenerate `contracts/odata/metadata.xml`; move ADR 0031 and the `$top` result in ADR 0029 to the recorded outcome.

**Also in this phase (shared by Phases 2 and 3)**: the fictional seed and oracle (next section).

**Boundary**: `dotnet build -warnaserror`, fast tests, integration tests for Odata and Insights folders, then `pwsh ./scripts/review-gate.ps1`; report counts from its output. Stop. Operator step for Matt (backup, dry run, confirm, dry run) happens after this merges and before the dashboard is trusted on real data.

## Phase 2: P1 web, funnel, rates and drill-down (needs Phase 0 for charts)

**Red first**:
1. Vitest: lens module. Every lens and window yields the exact `$filter` in the snapshot; windows and cutoffs at 13/14/29/30/31 days and window edges; node count and depth within limits via the real `ValidateFilterExpression` rules mirrored in the test or the .NET snapshot test; count query is `$top=0&$count=true`; invalid `lens`, `window`, `asOf` ignored.
2. Vitest: rates and sample flags: `x of y`, `< 10` is "small sample", `0` is "no data", never NaN; the coverage checks gate flag-dependent numbers and render "needs backfill" with the command.
3. Vitest: list loaders (`postings._index`, `applications._index`): with `?lens=...` the `$filter` they send equals the dashboard's count `$filter` for the same lens (the structural parity test); the lens chip renders; lens for the wrong entity is ignored; a raw `$filter` param is ignored.
4. Vitest route tests: Insights loader runs the 20 counts with the concurrency bound, one failing request degrades only its tiles; 401 re-login once, 403 to `/not-authorized`, 404 handling via `assertApiOk`.
5. .NET integration `InsightsParity` (also covers nesting): for every snapshot lens and both windows, `@odata.count` over the real API equals the independent oracle, equals the snapshot's `expected`, and equals the list's `@odata.count` for the same filter; boundary rows agree with the displayed `ageInDays`.
6. Playwright Chromium: both funnels and the tiles render with their cohort titles; each funnel bar and tile link lands on a list whose displayed total equals the number on the bar; the "offers without an interview" note links to the `offers` lens; keyboard operation; table view toggle on each funnel; axe clean in light and dark at 1280 and 360 px using the shared reduced-motion helper; no horizontal page scroll at 360 px.
7. Nesting tests, both windows: (a) Vitest over the fictional fixture: for each funnel, each stage count is at most its predecessor and the view model refuses to render a funnel that violates this (shows an error state instead of an inverted bar); (b) the .NET oracle test asserts the same for the seed, including rows built to break a naive funnel (offer without interview, rejection with no interview, an application whose posting was first seen before the window).
8. Drill-down equality, per bar: for each of the 7 funnel bars and the offers note, in both windows, the bar's count equals the list `@odata.count` reached through its drill-down link (unit level via identical `$filter`, integration level via the real API, e2e level via the displayed list total).
9. Architecture (web): the Insights route and list loaders import filters only from `insights-lenses.ts` (no filter strings elsewhere), and `web/app/ui.ts` is the only importer of `ds` components (existing rule).

**Then**: implement `insights-lenses.ts`, `insights-view.ts`, `routes/insights.tsx` (window toggle as a link or `<Form method="get">`, shareable URL), lens support and a "Has application" filter on the postings list (`hasApplication eq true`, the "postings I applied to" view), nav item, `ui.ts` exports, two funnels (BarChart funnel mode, one chart per cohort, with a table view each), StatTiles, help text for each rate (definitions, windows, why Funnel B's applied and Funnel A's applied-to can differ), "how reviewed is defined and its limit" note. Generate `contracts/insights-lenses.json`.

**Boundary**: typecheck, lint, test, build, `test:e2e`, `test-fast.ps1`, `pre-commit run --all-files`, `review-gate.ps1`; report numbers from output; list anything not run. Stop.

## Phase 3: P2 trends and response time

**Red first**:
1. Unit: `InsightsStatsCalculator`: odd and even medians, empty, single value, zero days, response before applied clamped to 0, week buckets on Sunday/Monday UTC edges, zero-filled empty weeks, in-window-only counting at the edges, run yield ordering and the 120 cap with `truncated`.
2. Unit/arch: handler reads only through `IOwnerReadSeam`; volume cap returns a typed `Result` failure, not an exception.
3. Integration (seeded): endpoint values equal hand-computed seed values; `responseTime.sampleSize` equals the `responded` lens count for the same window; owner isolation; non-GET is 405 with `Allow: GET`; bad `window` or `asOf` is 400.
4. Contract: OpenAPI snapshot drift fails until regenerated; generated client types updated (ADR 0019).
5. Vitest + Playwright: column chart of weekly applications and sparkline of yield render, each with a table view and keyboard operation; median tile shows days and "n responses" or "no data"; light, dark, 360 px; axe clean.

**Then**: implement the query, calculator and endpoint; typed `insights-stats.server.ts` using generated types; extend the Insights route (stats fetch starts with the first wave, failure degrades only the trend and median tiles).

**Boundary**: same gate as Phase 2. Stop.

## Phase 4: P3 notes Markdown (independent, can ship alone and can run any time after the plan is approved)

**Red first**:
1. Vitest component tests with fictional hostile fixtures: `<script>`, `<img src=x onerror=...>`, raw HTML blocks, `[x](javascript:...)`, `[x](data:text/html,...)`, `[x](vbscript:...)`, `[x](JaVaScRiPt:...)`, whitespace/control-character obfuscated schemes, `//evil.example` protocol-relative, relative links, `![alt](https://img.example/a.png)`. Assert: no `<script>`, `<img>`, `<iframe>` or event-handler attributes in the DOM; disallowed links render as plain text; allowed `http`/`https` links carry `rel="noopener noreferrer"`; image syntax renders the alt text only.
2. Vitest: GFM table, task list (disabled checkboxes), strikethrough, autolink literal, single newline produces a line break; the table sits in a focusable labelled scroll region.
3. Vitest: bodies over 256 KiB render as plain preformatted text with a notice.
4. Architecture/security: `rehype-raw` absent from `package.json` and the lockfile; no `dangerouslySetInnerHTML` under `web/app`; the three packages are exact-pinned (no `^` or `~`); licence audit of the resolved tree passes (allowlist MIT, ISC, BSD-2/3-Clause, Apache-2.0, 0BSD).
5. Playwright Chromium: a hostile fictional note renders with zero requests to any origin other than the app's own, zero CSP violations, axe clean (reduced-motion helper), no horizontal page scroll at 360 px with a wide table.
6. Build: `pnpm --filter web run build` succeeds with the ESM-only packages (R11 verify-first); if SSR bundling needs a Vite setting, record it in ADR 0032.

**Then**: add the three exact-pinned dependencies, `MarkdownBody.tsx`, wire `notes.$id.tsx`, add CSS classes (no inline `style` attributes) to `app.css`.

**Boundary**: same gate as Phase 2, noting Insights absent. Stop.

## Seed and isolation (built in Phase 1, used in Phases 1 to 3)

**What it is**: `tests/RoleRadar.Tests.Integration/Insights/` (no new test project):
- `InsightsSeed`: a deterministic builder with a fixed fictional clock (`asOf` fixed in September 2026, fictional companies such as "Northwind Examples"). It writes through the real repositories and handlers (so flags come from the choke point), plus a legacy variant that writes documents without the new flags to test reconcile. Owner A has about 60 postings in every disposition and about 40 applications covering every event type, rejection before/after/same-day interview, offers with and without an interview, applications at 13/14/29/30/31 days, window edge instants, and a posting first seen before the window but applied inside it. Owner B has rows that must never appear in A's counts. Owner C has nothing (zero-denominator case). A tiny-owner variant produces the "small sample" case.
- `InsightsOracle`: an independent in-memory implementation of the spec's metric definitions over the seed objects. It never calls the production lens, repository or calculator code.
- `contracts/insights-lenses.json` `expected` values come from the oracle and are rewritten only with an explicit `UPDATE_SNAPSHOTS=1` run.

**How the tests prove they never touch the real database**:
1. The fixture builds its own `MongoDbBuilder` container via the existing `ODataTestHost`/start-gate pattern, uses a database name with the `insights-parity-` prefix plus a GUID, and never calls `MongoConnectionStringSource` or reads user secrets or environment connection strings.
2. `InsightsSeedGuard.AssertIsolated(...)` runs in the fixture before any write: the connection host and port must equal the container's mapped endpoint, the database name must match the prefix, and it fails closed otherwise. A unit test feeds it a localhost:27017 connection string and a non-prefixed name and expects rejection.
3. A NetArchTest rule over the Integration assembly: types in the `Insights` namespace must not depend on `MongoConnectionStringSource`.
4. Web side: the Playwright screenshot and e2e specs run only against a loopback stub API serving a checked-in fictional fixture (counts from the oracle snapshot); a guard asserts `API_BASE_URL` is loopback and equals the stub's port, and the `real-stack` project is excluded from these specs. Screenshots are taken only from that stub run, then reviewed before anything is added to the repo.
5. `denylist-check` and gitleaks in `pre-commit` still run on every fixture and image.

## Operator steps (not agy)

| Step | Who | When |
| --- | --- | --- |
| Export kit v20 into `web/app/ds`, refresh `SYNC.md` | Claude with Matt signed in to claude.ai | before Phase 2 chart work |
| Backup, reconcile dry run, confirmed reconcile, dry run reports 0 | Matt, on the real database | after Phase 1 is merged and deployed |
| Approve or change the plan-level decisions in research R13 | Matt | before `/speckit-tasks` |
| Move ADRs 0029 to 0032 from Proposed to Accepted | Matt, at review | at each phase boundary or merge |

## Risks and verify-first items

| Risk | Handling |
| --- | --- |
| `UpsertPostingAsync` replace wipes `has_application` | Computed inside the upsert; dedicated test (Phase 1, test 3) |
| `eq false` undercounts before backfill | Coverage checks and "needs backfill" state (ADR 0029) |
| Real imported postings have a meaningless `first_seen_at` | Cannot be checked by agy; Matt eyeballs the first dry run and the all-time leads count. Fall back is switching the posting window field to `createdAt` in one place (the lens module) with a snapshot update |
| ESM-only markdown packages vs SSR build | Phase 4 verify-first build task |
| Date-boundary drift between lenses and displayed `ageInDays` | One cutoff helper, boundary tests compare with displayed `ageInDays` |
| 20 requests per load too slow | Bounded concurrency, indexed counts, SC-008 benchmark in Phase 2; revisit ADR 0029 only if it misses |
| Kit v20 export differs from the expected file list | Phase 0 stop-and-ask rule on any extra or missing file |

## Complexity Tracking

No constitution violations. Noted for review rather than as violations: four new ADRs, three new pinned web dependencies (Phase 4 only), and a larger-than-usual 006 read-model change (five derived fields) because the drill-down promise requires filters the model lacked.
