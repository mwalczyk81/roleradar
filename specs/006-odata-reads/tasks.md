# Tasks: OData Read Service & Multi-Page Server Querying

**Input**: Design documents from [`specs/006-odata-reads/`](file:///C:/Users/mwalc/source/repos/roleradar/specs/006-odata-reads/) (`plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/`, `quickstart.md`)  
**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/`  
**Constitution Invariants**: Principle I (strictly fictional data), Principle II (tests first, verified to fail before implementation; isolated Testcontainers only; no live developer DB access), Principle III (test-enforced layering), Principle IV (named seams only via ADR-0028), Principle VIII (pinned packages).

---

## Task Summary by Phase

| Phase | Description | Task Count |
| :--- | :--- | :--- |
| **Phase 1** | Setup & Executable Spike Gate (ADR-0028) | 9 tasks (T001–T009) |
| **Phase 2** | Foundational Infrastructure & Read Seam Setup | 6 tasks (T010–T015) |
| **Phase 3** | User Story 1 - Global Sorting and Filtering Across All Postings (P1) [US1] | 13 tasks (T016–T028) |
| **Phase 4** | User Story 2 - Enriched Application Tracking, Renames & Reconcile Tool (P1) [US2] | 14 tasks (T029–T042) |
| **Phase 5** | User Story 3 - Server-Side Querying for Companies, Notes, Criteria & Search Runs (P2) [US3] | 10 tasks (T043–T052) |
| **Phase 6** | User Story 4 - Query Guardrails, Abuse Prevention & Benchmark (P2) [US4] | 4 tasks (T053–T056) |
| **Phase 7** | User Story 5 - Contract Drift, Legacy Retirement & Web Verification (P3) [US5] | 6 tasks (T057–T062) |
| **Phase 8** | Polish, Operational Docs & Spike Cleanup | 5 tasks (T063–T067) |
| **Total** | | **67 tasks** |

---

## Phase 1: Setup & Executable Spike Gate (ADR-0028)

**Purpose**: Build an isolated, throwaway executable spike project to prove runtime compatibility on .NET 10, minimal API coexistence, JWT Bearer authentication, and MongoDB `IQueryable` translation before any production feature code is written.

- [ ] T001 Create throwaway spike project structure `specs/006-odata-reads/spike/SpikeApp.csproj` (isolated, NOT added to `RoleRadar.sln`) targeting `net10.0` with pinned package references `Microsoft.AspNetCore.OData 9.5.0` and `MongoDB.Driver 3.13.0`.
- [ ] T002 Implement minimal ASP.NET Core host in `specs/006-odata-reads/spike/Program.cs` to test routing coexistence between minimal API endpoints (`/api/v1`) and OData controllers (`/odata`) under the existing JWT Bearer authentication policy (`roleradar.api` scope and owner subject check).
- [ ] T003 Implement Testcontainers MongoDB translation test in `specs/006-odata-reads/spike/MongoTests.cs` testing Approach 1: plain `ODataQueryOptions.ApplyTo` with Mongo-safe settings (no null-propagation wrappers, no constant parameterization) over an owner-scoped `IQueryable`.
- [ ] T004 In `specs/006-odata-reads/spike/MongoTests.cs`, evaluate Approach 2: reference `MongoDB.AspNetCore.OData 1.1.3` (requiring OData >= 8.1.2 and Driver >= 3.11.1), test whether it loads against OData 9.5.0 and Driver 3.13.0, and compare translation of `$select`, `$count`, `$orderby` with secondary collation, `$orderby=disposition` mapped to `disposition_rank`, and string filter functions (`contains`, `startswith`, `endswith`, `tolower`).
- [ ] T005 In `specs/006-odata-reads/spike/Program.cs`, verify query validation settings producing RFC 7807/9457 Problem Details (`ErrorEnvelope`) with HTTP 400 Bad Request for `$top > 100` or disallowed operators, and verify non-GET methods return HTTP 405 Method Not Allowed with an `Allow: GET` header.
- [ ] T006 Compile empirical findings into `specs/006-odata-reads/spike/REPORT.md`, plainly documenting verified capabilities, unverified boundaries, which translation approach won (preferring fewer packages), and an explicit Go / No-Go decision.
- [ ] T007 Write architecture decision record `docs/adr/0028-odata-read-seam-and-query-execution.md` documenting the accepted query execution architecture, `IOwnerReadSeam` boundary, and rejection or adoption of `MongoDB.AspNetCore.OData 1.1.3`.
- [ ] T008 **STOP: Matt reviews specs/006-odata-reads/spike/REPORT.md and approves Go/No-Go**. (CRITICAL GATE: No task in Phase 2 or beyond may begin before Matt approves the Go/No-Go decision. If No-Go or fallback is reported, remaining tasks are re-planned).
- [ ] T009 Phase 1 verification: execute spike tests in `specs/006-odata-reads/spike/`, run `pwsh ./scripts/test-fast.ps1`, and verify `RoleRadar.sln` build and locked restore are 100% clean and untouched.

---

## Phase 2: Foundational Infrastructure & Read Seam Setup

**Purpose**: Register centrally pinned packages, define the owner-scoped query seam, and configure OData middleware in `RoleRadar.Api`.

- [ ] T010 Pin `Microsoft.AspNetCore.OData 9.5.0` (and `MongoDB.AspNetCore.OData 1.1.3` only if approved by the spike) in `Directory.Packages.props` and add `<PackageReference Include="Microsoft.AspNetCore.OData" />` to `src/RoleRadar.Api/RoleRadar.Api.csproj`.
- [ ] T011 [P] Define `IOwnerReadSeam` interface in `src/RoleRadar.Application/Reading/IOwnerReadSeam.cs` with strongly-typed `OwnerId` on every query method (`QueryPostings`, `QueryCompanies`, `QueryApplications`, `QueryNotes`, `QueryCriteria`, `QuerySearchRuns`).
- [ ] T012 [P] Create `ODataExceptionFilter` in `src/RoleRadar.Api/Filters/ODataExceptionFilter.cs` converting `ODataException` into standard `ErrorEnvelope` with HTTP 400 Problem Details.
- [ ] T013 Configure `AddControllers().AddOData(...)` with route prefix `odata`, `MaxTop = 100`, allowed query options (`$filter`, `$orderby`, `$top`, `$skip`, `$count`, `$select`), and `ODataExceptionFilter` in `src/RoleRadar.Api/Program.cs`.
- [ ] T014 Implement `MongoOwnerReadSeam` in `src/RoleRadar.Infrastructure/Mongo/Reading/MongoOwnerReadSeam.cs` implementing `IOwnerReadSeam` and enforcing `owner_id == ownerId.Value` pre-scoping on all returned `IQueryable<T>`.
- [ ] T015 Phase 2 verification: run `pwsh ./scripts/test-fast.ps1` and verify clean build with zero warnings under `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.

---

## Phase 3: User Story 1 - Global Sorting and Filtering Across All Postings (Priority: P1) [US1]

**Goal**: Deliver end-to-end server-side sorting, filtering, and paging across all job postings, removing the page-only sort limitation.

### Tests for User Story 1 (MANDATORY - Test-First per Principle II)

- [ ] T016 [P] [US1] Write architecture test `tests/RoleRadar.Tests.Architecture/ODataSeamArchitectureTests.cs` asserting `RoleRadar.Api` contains zero references to `MongoDB.Driver` or `IMongoCollection<T>` (expected to fail before implementation).
- [ ] T017 [P] [US1] Write authorization integration tests in `tests/RoleRadar.Tests.Integration/ODataAuthorizationTests.cs` for `/odata/Postings` asserting HTTP 401 for anonymous, HTTP 403 for wrong scope, HTTP 403 for non-owner subject, and HTTP 200 for valid owner token (expected to fail before implementation).
- [ ] T018 [P] [US1] Write read model consistency test in `tests/RoleRadar.Tests.Unit/ReadModelConsistencyTests.cs` asserting every persisted posting document has `disposition_rank == rank(disposition)` (expected to fail before implementation).
- [ ] T019 [P] [US1] Write postings query integration tests in `tests/RoleRadar.Tests.Integration/ODataPostingsTests.cs` in Testcontainers testing `$top > 100` (400), POST (405 with `Allow: GET`), case-insensitive collation on `title`, disposition pipeline rank sort (`unscored` -> `lead` -> `scored` -> `applied` -> `rejected` -> `screened_out` -> `closed`), null scores sorted last, and pagination continuity (expected to fail before implementation).

### Implementation for User Story 1

- [ ] T020 [US1] Implement `PostingReadModel` in `src/RoleRadar.Application/Reading/Models/PostingReadModel.cs` with exact fields: `id`, `companyId`, `companyName`, `title`, `requisitionId`, `location`, `disposition`, `resurfacePolicy`, `score`, `createdAt`, `updatedAt` (depends on T016–T019 failing first).
- [ ] T021 [US1] Update `MongoPostingRepository.SerializePosting` in `src/RoleRadar.Infrastructure/Mongo/Repositories/MongoPostingRepository.cs` (the single choke point) to compute `disposition_rank` (0..6) from `Posting.Disposition` and populate `company_name`.
- [ ] T022 [US1] Add compound indexes `{ owner_id: 1, disposition_rank: 1, _id: 1 }`, `{ owner_id: 1, score: -1, _id: 1 }`, and secondary collation text indexes for `title` and `company_name` in `src/RoleRadar.Infrastructure/Mongo/TrackingIndexInitializer.cs`.
- [ ] T023 [US1] Implement `PostingsController : ODataController` in `src/RoleRadar.Api/Controllers/OData/PostingsController.cs` mapping `GET /odata/Postings` to `IOwnerReadSeam.QueryPostings`.
- [ ] T024 [US1] Generate machine-readable EDM metadata snapshot from the running service and commit to `contracts/odata/$metadata.xml`.
- [ ] T025 [P] [US1] Implement lightweight typed query builder `web/app/lib/odata-client.ts` with comprehensive unit tests in `web/tests/unit/odata-client.test.ts` verifying compile-time and runtime query construction.
- [ ] T026 [US1] Migrate postings list loader and UI in `web/app/routes/postings._index.tsx` to `/odata/Postings`, binding URL state (`$filter`, `$orderby`, `$top`, `$skip`, `$count`) and removing the "Sorting applies to the rows on this page only" footer disclaimer.
- [ ] T027 [US1] Write Chromium Playwright E2E test in `web/tests/e2e/postings-server-sort.spec.ts` verifying multi-page server sorting across page boundaries in a real browser.
- [ ] T028 [US1] Phase 3 verification: run `pwsh ./scripts/review-gate.ps1 -IncludeE2E` once, and run web CI steps in a throwaway Linux container (`git ls-files | tar into node:22, pnpm install --frozen-lockfile, build, lint, typecheck, e2e, test:coverage`) confirming 80% line / 75% branch coverage passes.

---

## Phase 4: User Story 2 - Enriched Application Tracking, Renames & Reconcile Tool (Priority: P1) [US2]

**Goal**: Deliver enriched application read models with pre-materialized lifecycle milestones (`hasInterview`, `firstResponseAt`, `lastEventAt`, `ageInDays`), atomic rename cascades, and the `reconcile-read-models` CLI tool.

### Tests for User Story 2 (MANDATORY - Test-First per Principle II)

- [ ] T029 [P] [US2] Write rename cascade integration tests in `tests/RoleRadar.Tests.Integration/RenameCascadeTests.cs` in Testcontainers verifying company name change updates `companyName` on postings and applications, and posting title change updates `postingTitle` on applications in the same transaction (expected to fail before implementation).
- [ ] T030 [P] [US2] Write reconcile tool tests in `tests/RoleRadar.Tests.Integration/ReconcileReadModelsTests.cs` in Testcontainers verifying dry-run default, `--confirm` requirement, count report accuracy, and idempotency (expected to fail before implementation).
- [ ] T031 [P] [US2] Write applications query integration tests in `tests/RoleRadar.Tests.Integration/ODataApplicationsTests.cs` in Testcontainers testing pre-materialized milestones (`hasInterview`, `firstResponseAt`, `lastEventAt`, `ageInDays`) and null timestamps sorted last (expected to fail before implementation).
- [ ] T032 [P] [US2] Write replacement web loader tests in `web/tests/routes/applications.odata-loader.test.ts` re-creating every behavior from `applications.loader.test.ts` (401 one re-login, 403, 404, limit clamp 1..100, supported params only) against OData (expected to fail before implementation).

### Implementation for User Story 2

- [ ] T033 [US2] Implement `ApplicationReadModel` in `src/RoleRadar.Application/Reading/Models/ApplicationReadModel.cs` with exact fields: `id`, `postingId`, `companyId`, `postingTitle`, `companyName`, `status` (`applied` | `rejected` | `closed`), `appliedAt`, `hasInterview`, `firstResponseAt`, `lastEventAt`, `ageInDays` (depends on T029–T032 failing first).
- [ ] T034 [US2] Update `CreateApplicationHandler.cs` and `AppendApplicationEventHandler.cs` in `src/RoleRadar.Application/Tracking/Commands/` to materialize `postingTitle`, `companyName`, `hasInterview`, `firstResponseAt`, and `lastEventAt` during upsert in the same transaction as audit logging.
- [ ] T035 [US2] Implement transactional rename cascade logic in `src/RoleRadar.Infrastructure/Mongo/Repositories/MongoCompanyRepository.cs` and posting title update handlers updating child documents atomically.
- [ ] T036 [US2] Add compound indexes for applications (`{ owner_id: 1, applied_at: -1, _id: 1 }`, `{ owner_id: 1, last_event_at: -1, _id: 1 }`, `{ owner_id: 1, has_interview: 1, _id: 1 }`, `{ owner_id: 1, status: 1, _id: 1 }`) in `src/RoleRadar.Infrastructure/Mongo/TrackingIndexInitializer.cs`.
- [ ] T037 [US2] Implement `ApplicationsController : ODataController` in `src/RoleRadar.Api/Controllers/OData/ApplicationsController.cs` mapping `GET /odata/Applications` to `IOwnerReadSeam.QueryApplications`.
- [ ] T038 [US2] Implement `reconcile-read-models` CLI command in `src/RoleRadar.Cli/Commands/ReconcileReadModelsCommand.cs` with dry-run default, `--confirm` requirement, and count report covering postings (rank, companyName) and applications (postingTitle, companyName, milestones).
- [ ] T039 [US2] Document operational rule in `docs/operations/reconcile-read-models.md`: Matt runs `reconcile-read-models` on the real database after taking an encrypted backup, running dry run first; agy and CI jobs never run it on live databases.
- [ ] T040 [US2] Migrate applications list route `web/app/routes/applications._index.tsx` to `/odata/Applications`.
- [ ] T041 [US2] Delete obsolete `web/app/lib/application-context.server.ts` and legacy `web/tests/routes/applications.loader.test.ts`, verifying that the replacement tests in `web/tests/routes/applications.odata-loader.test.ts` keep line coverage >= 80% and branch coverage >= 75%.
- [ ] T042 [US2] Phase 4 verification: run `pwsh ./scripts/review-gate.ps1 -IncludeE2E` once, and run web CI steps in Linux container confirming tests, lint, typecheck, and coverage pass.

---

## Phase 5: User Story 3 - Server-Side Querying for Companies, Notes, Criteria & Search Runs (Priority: P2) [US3]

**Goal**: Extend server-side querying across all remaining collections with consistent URL binding and filter restrictions.

### Tests for User Story 3 (MANDATORY - Test-First per Principle II)

- [ ] T043 [P] [US3] Write query contract and restriction integration tests in `tests/RoleRadar.Tests.Integration/ODataRemainingEntitiesTests.cs` in Testcontainers for Companies, Notes, Criteria, and SearchRuns, asserting that note `content` is rejected with HTTP 400 in `$filter` and `$orderby` (expected to fail before implementation).
- [ ] T044 [P] [US3] Write web route loader unit tests in `web/tests/routes/remaining-entities.loader.test.ts` for Companies, Notes, Criteria, and SearchRuns OData queries (expected to fail before implementation).

### Implementation for User Story 3

- [ ] T045 [P] [US3] Implement `CompanyReadModel`, `NoteReadModel`, `CriterionReadModel`, and `SearchRunReadModel` in `src/RoleRadar.Application/Reading/Models/` matching specifications in `data-model.md` (depends on T043, T044 failing first).
- [ ] T046 [US3] Add compound indexes for Companies, Notes, Criteria, and SearchRuns in `src/RoleRadar.Infrastructure/Mongo/TrackingIndexInitializer.cs`.
- [ ] T047 [US3] Implement `CompaniesController`, `NotesController`, `CriteriaController`, and `SearchRunsController` in `src/RoleRadar.Api/Controllers/OData/`.
- [ ] T048 [P] [US3] Migrate companies list route `web/app/routes/companies._index.tsx` to `/odata/Companies`.
- [ ] T049 [P] [US3] Migrate notes list route `web/app/routes/notes._index.tsx` to `/odata/Notes`.
- [ ] T050 [P] [US3] Migrate criteria list route `web/app/routes/criteria._index.tsx` to `/odata/Criteria` with `effectiveFrom`/`effectiveTo` interval filter mapping.
- [ ] T051 [P] [US3] Migrate search runs list route `web/app/routes/search-runs._index.tsx` to `/odata/SearchRuns`.
- [ ] T052 [US3] Phase 5 verification: run `pwsh ./scripts/review-gate.ps1 -IncludeE2E` once, and run web CI steps in Linux container confirming tests, lint, typecheck, and coverage pass.

---

## Phase 6: User Story 4 - Query Guardrails, Abuse Prevention & Benchmark (Priority: P2) [US4]

**Goal**: Enforce system stability guardrails, verify multi-owner tenant isolation, and validate the 5,000-row performance SLA.

### Tests & Validation for User Story 4

- [ ] T053 [P] [US4] Write comprehensive query abuse integration test suite in `tests/RoleRadar.Tests.Integration/ODataAbuseGuardrailsTests.cs` asserting HTTP 400 Problem Details for expression trees exceeding 100 nodes, depth limits, arithmetic functions, and lambda expressions.
- [ ] T054 [P] [US4] Write multi-owner isolation integration test in `tests/RoleRadar.Tests.Integration/ODataMultiOwnerIsolationTests.cs` using two distinct owner IDs, proving zero cross-owner document exposure and asserting `owner_id` is never present in JSON responses.
- [ ] T055 [US4] Implement performance benchmark test in `tests/RoleRadar.Tests.Integration/ODataBenchmarkTests.cs` verifying filtered and sorted queries against a 5,000-document collection in Testcontainers complete in under 1 second (SC-006).
- [ ] T056 [US4] Phase 6 verification: run `pwsh ./scripts/review-gate.ps1 -IncludeE2E` once, asserting that benchmark tests pass under 1 second and all abuse tests pass.

---

## Phase 7: User Story 5 - Contract Drift, Legacy Retirement & Web Verification (Priority: P3) [US5]

**Goal**: Eliminate duplicate list endpoints, verify contract drift, and finalize OpenAPI / client type definitions.

### Tests for User Story 5

- [ ] T057 [P] [US5] Write .NET contract drift test in `tests/RoleRadar.Tests.Contract/ODataMetadataDriftTests.cs` and web contract equality test in `web/tests/unit/odata-metadata-drift.test.ts` asserting running `/odata/$metadata` equals committed `contracts/odata/$metadata.xml`.
- [ ] T058 [US5] Write Playwright network interception test in `web/tests/e2e/legacy-api-retirement.spec.ts` asserting that zero HTTP requests are dispatched to `/api/v1/` list endpoints during full UI navigation.

### Implementation for User Story 5

- [ ] T059 [US5] Decommission and delete the 6 legacy list endpoints (`GET /api/v1/postings`, `GET /api/v1/companies`, `GET /api/v1/applications`, `GET /api/v1/notes`, `GET /api/v1/criteria`, `GET /api/v1/search-runs`) from `src/RoleRadar.Api/Endpoints/` (depends on T057, T058 passing).
- [ ] T060 [US5] Rebuild `src/RoleRadar.Api` to regenerate `contracts/openapi/roleradar-api.json` and run `pnpm --filter web run generate-api-types` to update `contracts/client/api-types.ts`.
- [ ] T061 [US5] Verify that single-entity detail endpoints (`GET /api/v1/{entity}/{id}`), command mutation endpoints (`POST`, `PATCH`), the CLI, and the agent MCP server remain fully functional and pass contract checks.
- [ ] T062 [US5] Phase 7 verification: run `pwsh ./scripts/review-gate.ps1 -IncludeE2E` once, and run web CI steps in Linux container confirming zero requests dispatched to retired endpoints.

---

## Phase 8: Polish, Operational Docs & Spike Cleanup

**Purpose**: Update standing agent rules, operational documentation, and clean up throwaway spike artifacts before merge.

- [ ] T063 [P] Update operational documentation in `docs/api/odata-queries.md` with copy-pasteable `curl` examples demonstrating bearer token querying of `/odata` with `$filter`, `$orderby`, `$select`, and `$count`.
- [ ] T064 [P] Document Excel and Power BI connectivity as a prospective follow-up feature in `docs/integrations/excel-powerbi.md`, detailing bearer authentication headers and OData v4 URL structures.
- [ ] T065 [P] Update `AGENTS.md` with feature standing rules: OData property allowlists, mandatory use of `IOwnerReadSeam`, and strict rule never to bypass the repository choke point for derived stored fields.
- [ ] T066 Delete throwaway spike code in `specs/006-odata-reads/spike/` (`SpikeApp.csproj`, `Program.cs`, `MongoTests.cs`), preserving only `specs/006-odata-reads/spike/REPORT.md` and `docs/adr/0028-odata-read-seam-and-query-execution.md`.
- [ ] T067 Final verification: execute `pwsh ./scripts/review-gate.ps1 -IncludeE2E` once and run full web CI verification on Linux in a throwaway container (`node:22`) before leaving working tree for review.

---

## Dependencies & Execution Order

```text
Phase 1: Spike (T001..T007) ──► T008 [MATT APPROVAL GATE] ──► T009 [Spike Verify]
                                                                    │
Phase 2: Foundational Setup (T010..T015) ◄─────────────────────────┘
      │
      ▼
Phase 3: US1 - Postings End-to-End (T016..T028)
      │
      ▼
Phase 4: US2 - Applications & Renames & Reconcile (T029..T042)
      │
      ▼
Phase 5: US3 - Remaining Entity Sets (T043..T052)
      │
      ▼
Phase 6: US4 - Guardrails, Abuse & Benchmark (T053..T056)
      │
      ▼
Phase 7: US5 - Contract Drift & Legacy Retirement (T057..T062)
      │
      ▼
Phase 8: Polish, Docs & Spike Cleanup (T063..T067)
```

- **Phase 1 Gate**: `T008` blocks all subsequent tasks. If `REPORT.md` is rejected or requires fallback, downstream tasks are re-planned.
- **Tests First**: In every user story phase, tests come first, are verified to fail before implementation, and must pass before the phase verification gate.
- **Verification Gates**: Every phase ends with a gate executing `pwsh ./scripts/review-gate.ps1 -IncludeE2E` and reproducing Linux web CI in a throwaway container before proceeding.
- **Parallel Opportunities**: Tasks marked `[P]` touch non-overlapping files and can be executed concurrently within their respective phase.
