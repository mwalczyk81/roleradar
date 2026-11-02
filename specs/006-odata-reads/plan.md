# Implementation Plan: OData Read Service & Multi-Page Server Querying

**Branch**: `006-odata-reads` | **Date**: 2026-11-01 (Revised) | **Spec**: [specs/006-odata-reads/spec.md](file:///C:/Users/mwalc/source/repos/roleradar/specs/006-odata-reads/spec.md)

**Input**: Feature specification from [`specs/006-odata-reads/spec.md`](file:///C:/Users/mwalc/source/repos/roleradar/specs/006-odata-reads/spec.md)

---

## Summary

Migrate all read-only list queries across Postings, Companies, Applications, Notes, Criteria, and Search Runs from un-paged/in-memory sorted REST endpoints to a dedicated OData v4 query service under `/odata`. All data mutations, transitions, and append-only audits remain on existing REST command endpoints (`/api/v1`). Queries execute through a named, owner-scoped Application read seam (`IOwnerReadSeam`) backed by compound MongoDB indexes (`owner_id` leading), enforcing strict tenant isolation, allowlisted properties, and abuse controls (MaxTop 100, HTTP 405 on non-GET). Derived stored fields (`disposition_rank`, `companyName`, `postingTitle`) have a single choke point in repository upserts, supported by transactional rename cascades and an idempotent `reconcile-read-models` CLI tool. The plan gates production code with an executable spike under `specs/006-odata-reads/spike/`. Once the web client list views migrate to OData, legacy `/api/v1` list endpoints and the obsolete `application-context.server.ts` helper are cleanly retired.

---

## Technical Context

**Language/Version**: C# 13 / .NET 10 (Backend), TypeScript 5.8 / Node 22 (Web UI)  
**Primary Dependencies**:
- Backend: `Microsoft.AspNetCore.OData` 9.5.0 (pinned), `MongoDB.Driver` 3.13.0 (pinned), `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.14 (pinned)
- Spike Evaluation: `MongoDB.AspNetCore.OData` 1.1.3 vs. plain `ODataQueryOptions.ApplyTo` with Mongo-safe settings
- Frontend: React Router v7 SSR, native ESM fetch, lightweight typed query builder (0 external runtime deps)  
**Storage**: MongoDB 8.0 (Replica Set), indexed with `owner_id` leading and secondary collation  
**Testing**: xUnit v3, NetArchTest.Rules, Testcontainers.MongoDb 4.16.0, Playwright (Chromium E2E), Vitest (Web unit/SSR)  
**Target Platform**: Linux containers / Windows dev (Docker Compose)  
**Project Type**: Monorepo with .NET Web API host, Application/Domain/Infrastructure libraries, and React Router SSR web app  
**Performance Goals**: < 500ms initial list page loads; < 1 second for 5,000-row filtered & sorted benchmark queries (SC-007)  
**Constraints**:
- Max `$top = 100` (default 25)
- Non-GET methods return HTTP 405 with `Allow: GET`
- Disallowed query options/operators return HTTP 400 Problem Details
- Never expose `owner_id` in query read models
- Strict layering: API never touches MongoDB driver collections directly  
**Scale/Scope**: 6 entity sets (`Postings`, `Companies`, `Applications`, `Notes`, `Criteria`, `SearchRuns`), 6 web list pages migrated, 6 legacy `/api/v1` list endpoints retired  

---

## Constitution Check

*GATE: All principles evaluated and passed prior to phase execution.*

| Principle | Requirement | Compliance in this Plan |
| :--- | :--- | :--- |
| **I. Privacy First** | Strictly fictional test data; secrets via environment variables | All unit, contract, and Testcontainers tests utilize synthetic fictional fixtures. Zero personal data. |
| **II. Test-First Development** | Tests fail before implementation; isolated ephemeral containers | Every phase specifies failing contract/abuse/translation tests before production code. Zero interaction with live developer DBs. |
| **III. Test-Enforced Layering** | Strict one-way inward dependencies; command handlers decorated | `RoleRadar.Api` depends only on `Application` abstractions; architecture tests enforce zero references to `IMongoCollection<T>`. |
| **IV. Named Seams Only** | No generic repositories without an ADR; explicit interfaces | Introducing `IOwnerReadSeam` as the single owner-scoped query boundary, formally recorded in **ADR-0028**. |
| **V. Agent Safety & Auditability** | Narrow tools; untrusted text hygiene; append-only audit | All writes remain on existing command handlers with audit logging. Text rendered as React text nodes only. Read endpoints strictly read-only. |
| **VI. stdio Protocol Hygiene** | stdout reserved for protocol frames | No changes to MCP host; OData host is `RoleRadar.Api`. |
| **VII. Data Durability** | Named volumes; confirmation for destructive tools | `reconcile-read-models` CLI tool defaults to dry-run and requires `--confirm`. Tested only on Testcontainers; agy never runs it on live DBs. |
| **VIII. Recorded Decisions** | Decisions in MADR ADRs; all packages pinned | ADR-0028 written for OData seam. Pinned packages in `Directory.Packages.props`. |
| **IX. Baseline Conventions** | .NET 10, nullable, warnings as errors, UTC, UUIDv7 | Fully adhered to across all new classes, DTOs, and controllers. |

---

## Project Structure

### Documentation & Spike (this feature)

```text
specs/006-odata-reads/
├── spec.md              # Feature specification
├── plan.md              # Implementation plan (this file)
├── research.md          # Technical research & spike analysis
├── data-model.md        # Read models, indexes, single choke points, and rename cascades
├── quickstart.md        # End-to-end verification guide
├── contracts/
│   └── odata-metadata.xml  # Draft EDM schema (definitive version generated in Phase 2)
├── checklists/
│   └── requirements.md  # Specification quality checklist
└── spike/               # Phase 1 executable throwaway spike (not shipped)
    ├── SpikeApp.csproj  # Pinned dependencies (.NET 10, OData 9.5.0, Driver 3.13.0)
    ├── Program.cs       # Minimal API + OData controller coexistence & auth
    ├── MongoTests.cs    # Testcontainers LINQ translation tests (ApplyTo vs 1.1.3)
    └── REPORT.md        # Written Go / No-Go decision report
```

### Source Code Impact

```text
src/
├── RoleRadar.Domain/
│   └── Tracking/
│       └── ApplicationMilestones.cs         # Materialized milestone domain logic
├── RoleRadar.Application/
│   ├── Reading/
│   │   ├── IOwnerReadSeam.cs               # Named owner-scoped read seam (ADR-0028)
│   │   └── Models/                          # Read models (PostingReadModel, etc.)
│   └── Tracking/Commands/
│       ├── CreateApplicationHandler.cs     # Materializes postingTitle, companyName
│       ├── AppendApplicationEventHandler.cs # Materializes interview, response, lastEventAt
│       ├── UpdateCompanyHandler.cs          # Cascades companyName to postings & applications
│       └── UpdatePostingTitleHandler.cs     # Cascades postingTitle to applications
├── RoleRadar.Infrastructure/
│   └── Mongo/
│       ├── Reading/
│       │   └── MongoOwnerReadSeam.cs        # Native Mongo LINQ execution pre-filtered by owner
│       ├── TrackingIndexInitializer.cs      # Adds owner-first compound indexes + collation
│       ├── Repositories/
│       │   ├── MongoPostingRepository.cs    # Choke point: computes disposition_rank & companyName
│       │   ├── MongoCompanyRepository.cs    # Executes transactional company rename cascade
│       │   └── MongoApplicationRepository.cs# Choke point: materializes title, company, milestones
│       └── Reconcile/
│           └── ReadModelReconciliationService.cs # Reconcile engine for CLI tool
├── RoleRadar.Api/
│   ├── Controllers/OData/                   # OData controllers for the 6 entity sets
│   ├── Filters/
│   │   └── ODataExceptionFilter.cs          # Formats ODataException as RFC 7807/9457 Problem Details
│   └── Program.cs                           # Configures AddOData with validation & MapControllers
├── RoleRadar.Cli/
│   └── Commands/
│       └── ReconcileReadModelsCommand.cs    # Unified CLI command (--dry-run default, --confirm)
contracts/
├── odata/
│   └── metadata.xml                         # Generated from running service & committed
├── openapi/
│   └── roleradar-api.json                   # Regenerated after retiring /api/v1 list endpoints
└── client/
    └── api-types.ts                         # Regenerated TypeScript definitions
web/
├── app/
│   ├── lib/
│   │   └── odata-client.ts                  # Lightweight typed query builder (Option B)
│   ├── routes/
│   │   ├── postings._index.tsx              # Migrated to /odata/Postings
│   │   ├── companies._index.tsx             # Migrated to /odata/Companies
│   │   ├── applications._index.tsx          # Migrated to /odata/Applications (replaces per-row fetch)
│   │   ├── notes._index.tsx                 # Migrated to /odata/Notes
│   │   ├── criteria._index.tsx              # Migrated to /odata/Criteria
│   │   └── search-runs._index.tsx           # Migrated to /odata/SearchRuns
│   └── ui.ts                                # DataTable wrapper ensuring server-sort hygiene
tests/
├── RoleRadar.Tests.Architecture/
│   └── ODataSeamArchitectureTests.cs        # Asserts API zero Mongo refs & seam routing
├── RoleRadar.Tests.Unit/
│   ├── ODataValidationTests.cs              # Unit tests for options, null ordering, rank
│   └── ReadModelConsistencyTests.cs         # Asserts 100% of postings have rank == rank(disposition)
├── RoleRadar.Tests.Contract/
│   └── ODataMetadataDriftTests.cs           # Asserts running /odata/$metadata == snapshot
├── RoleRadar.Tests.Integration/
│   ├── ODataAuthorizationTests.cs          # 401 anon, 403 scope, 403 subject, 200 owner
│   ├── ODataAbuseTests.cs                   # Top > 100, invalid fields, 405 on non-GET
│   ├── ODataOwnerIsolationTests.cs          # Second-owner data isolation verification
│   ├── ODataMongoTranslationTests.cs        # LINQ3 translation for operators & collation
│   ├── ODataBenchmarkTests.cs               # 5,000-row query latency < 1s (SC-007)
│   ├── RenameCascadeTests.cs                # Atomic propagation of company & posting renames
│   └── ReconcileReadModelsTests.cs          # Tests CLI reconcile idempotency & safety
└── web/tests/
    ├── unit/odata-client.test.ts            # Compile-time & runtime query builder tests
    └── e2e/postings-server-sort.spec.ts     # Playwright multi-page server sorting tests
```

---

## Phased Implementation Sequence

### Phase 1: Executable Spike & Go / No-Go Gate (ADR-0028)
- **Goal**: Create throwaway spike under `specs/006-odata-reads/spike/` to empirically prove runtime viability before writing production feature code.
- **Spike Tasks**:
  1. Build minimal standalone host proving `Microsoft.AspNetCore.OData 9.5.0` controllers coexist with minimal API endpoints on .NET 10 under the existing JWT Bearer auth policy.
  2. In Testcontainers Mongo, test query translation over `IQueryable` (comparing plain `ApplyTo` with Mongo-safe settings vs. `MongoDB.AspNetCore.OData 1.1.3`).
     - Test `$select`, `$count`, `$orderby` with secondary collation, `$orderby=disposition` mapped to `disposition_rank`, and string filter functions (`contains`, `startswith`, `endswith`, `tolower`).
     - Report whether 1.1.3 loads and translates, or fails against `MongoDB.Driver 3.13.0`.
     - Prefer the passing approach with fewer packages.
  3. Prove validation settings return HTTP 400 Problem Details and non-GET methods return HTTP 405 with `Allow: GET`.
  4. Write `specs/006-odata-reads/spike/REPORT.md` documenting verified results, unverified boundaries, and the Go/No-Go decision.
  5. Write `docs/adr/0028-odata-read-seam-and-query-execution.md` recording the agreed architecture.
  6. **STOP GATE**: Matt reviews `REPORT.md` and approves Go/No-Go.

### Phase 2: Foundational Infrastructure & Read Seam Setup
- Pin packages in `Directory.Packages.props` and configure OData middleware.
- Define `IOwnerReadSeam` interface in `src/RoleRadar.Application/Reading/IOwnerReadSeam.cs`.
- Implement `ODataExceptionFilter` for Problem Details (400) and `MongoOwnerReadSeam` for owner pre-scoping.

### Phase 3: User Story 1 - Global Sorting, Filtering & Postings Reconcile (Priority: P1) [US1]
- **Tests First**:
  - **Read seam composition test** (`ODataReadSeamCompositionTests.cs`): written first against a throwaway Testcontainers Mongo instance, testing one combined query with `contains(tolower(title), 'x')` filter, `$orderby=score asc` with nulls last (`has_score: -1, score: 1, _id: 1`), `$skip`/`$top` page 2, `$count=true`, and secondary collation. *Fallback*: if LINQ composition fails on complex combinations, fallback is native filter and sort definitions built from the validated OData filter AST for allowlisted operators; using this fallback widens scope and requires Matt's review.
  - Architecture test: `ODataSeamArchitectureTests.cs` (verifies API layer cannot touch MongoDB collection types).
  - Authorization tests: `ODataAuthorizationTests.cs` (asserts 401 for anonymous, 403 for missing scope, 403 for non-owner subject, 200 for valid owner).
  - Consistency test: `ReadModelConsistencyTests.cs` asserting every posting's `disposition_rank == rank(disposition)` and `has_score == (score != null)`.
  - Postings query integration tests: `ODataPostingsTests.cs` in Testcontainers testing POST (405 with `Allow: GET`), case-insensitive collation, disposition pipeline rank sort (`unscored` -> `lead` -> `scored` -> `applied` -> `rejected` -> `screened_out` -> `closed`), null scores sorted last in both directions, string function rejection (400 if `contains/startswith/endswith` not applied as `function(tolower(p), 'lowercased')`), and pagination continuity.
  - Postings multi-owner isolation test: `ODataPostingsIsolationTests.cs` in Testcontainers using two distinct owner IDs, proving zero cross-owner document exposure and asserting `owner_id` is never present in JSON responses.
  - Postings abuse tests: `ODataPostingsAbuseTests.cs` in Testcontainers testing `$top > 100` (400), unlisted properties, filter trees exceeding 100 nodes, depth limits, and arithmetic/lambda operators.
  - Postings reconcile tests: `ReconcilePostingsTests.cs` in Testcontainers verifying dry-run default, `--confirm` requirement, count report accuracy, and idempotency for postings (`disposition_rank`, `companyName`, and `has_score`).
  - Contract drift test: `ODataMetadataDriftTests.cs` comparing running `/odata/$metadata` against generated snapshot.
- **Backend Implementation**:
  - Add `PostingReadModel` and `IOwnerReadSeam.QueryPostings(OwnerId)`.
  - Update `MongoPostingRepository.SerializePosting` (the single choke point) to compute `disposition_rank`, populate `company_name`, and maintain `has_score = (score != null)`.
  - Add compound indexes in `TrackingIndexInitializer`: `{ owner_id: 1, disposition_rank: 1, _id: 1 }`, `{ owner_id: 1, has_score: -1, score: 1, _id: 1 }` (ASC), `{ owner_id: 1, has_score: -1, score: -1, _id: 1 }` (DESC), and secondary collation text indexes.
  - Implement query validation in `PostingsController : ODataController` under `/odata/Postings`: reject `contains/startswith/endswith` unless applied as `function(tolower(property), 'lowercased term')` returning HTTP 400 Problem Details (noting that such filters are unindexed scans).
  - Generate and commit definitive `contracts/odata/metadata.xml` from the running service.
  - Implement postings portion of `reconcile-read-models` CLI command in `src/RoleRadar.Cli/Commands/ReconcileReadModelsCommand.cs` backfilling `disposition_rank`, `companyName`, and `has_score`.
  - Document operational rule in `docs/operations/reconcile-read-models.md`: Matt runs `reconcile-read-models` on the real database after taking an encrypted backup, running dry run first; agy and CI jobs never run it on live databases.
- **Frontend Implementation**:
  - Create typed query builder `web/app/lib/odata-client.ts`, ensuring it always emits string functions in `function(tolower(property), 'lowercased term')` form.
  - Migrate `web/app/routes/postings._index.tsx` to `/odata/Postings`.
  - Remove "Sorting applies to the rows on this page only" footer disclaimer.
  - Add Playwright E2E test verifying server-side sort and pagination.
  - **Operator Note**: Matt runs postings reconcile on the real database (dry run first, after a backup) before using the new postings sort in production.

### Phase 4: User Story 2 - Enriched Application Tracking, Renames, App Reconcile & Cleanup (Priority: P1) [US2]
- **Tests First**:
  - Rename cascade tests (`RenameCascadeTests.cs`): assert company name change atomically updates postings and applications; posting title change updates applications.
  - Applications reconcile tests (`ReconcileApplicationsTests.cs`): assert dry-run default, `--confirm` requirement, count report accuracy, and idempotency for applications (`postingTitle`, `companyName`, `hasInterview`, `firstResponseAt`, `lastEventAt`, `has_first_response`, `has_last_event`).
  - Applications query tests verifying derived milestones (`hasInterview`, `firstResponseAt`, `lastEventAt`, `ageInDays`) and null timestamps sorted last in both directions via stored flags.
  - Replacement web loader tests in `web/tests/routes/applications.odata-loader.test.ts` re-creating every behavior from `applications.loader.test.ts` (401 one re-login, 403, 404, limit clamp, supported params only) against OData.
- **Backend Implementation**:
  - Update `CreateApplicationHandler` and `AppendApplicationEventHandler` to materialize initial milestones, `postingTitle`, `companyName`, and maintain `has_first_response = (firstResponseAt != null)` and `has_last_event = (lastEventAt != null)` at the repository choke point.
  - Add transactional rename logic in `MongoCompanyRepository` and posting title update paths.
  - Extend `reconcile-read-models` CLI command in `src/RoleRadar.Cli/Commands/ReconcileReadModelsCommand.cs` to backfill applications (`postingTitle`, `companyName`, `hasInterview`, `firstResponseAt`, `lastEventAt`, `has_first_response`, `has_last_event`).
  - Add compound indexes for applications in `TrackingIndexInitializer`: ASC and DESC indexes per nullable sort field (`{ owner_id: 1, has_first_response: -1, first_response_at: 1, _id: 1 }`, `{ owner_id: 1, has_first_response: -1, first_response_at: -1, _id: 1 }`, `{ owner_id: 1, has_last_event: -1, last_event_at: 1, _id: 1 }`, `{ owner_id: 1, has_last_event: -1, last_event_at: -1, _id: 1 }`).
  - Expose `/odata/Applications` in `ApplicationsController`.
- **Frontend Implementation & Deletions**:
  - Migrate `web/app/routes/applications._index.tsx` to `/odata/Applications`.
  - **Delete obsolete code**: Delete `web/app/lib/application-context.server.ts` and its test `web/tests/routes/applications.loader.test.ts`. Replace with direct OData loader assertions, maintaining line coverage >= 80% and branch coverage >= 75%.

### Phase 5: User Story 3 - Server-Side Querying for Companies, Notes, Criteria & Search Runs (Priority: P2) [US3]
- **Tests First**:
  - Query contract tests for remaining entity sets.
  - Filter restrictions test (verifying note `content` is rejected in `$filter` / `$orderby`).
  - **Notes content projection test**: verify that the Notes query projects `content` away in the database query (`content == null` or omitted in MongoDB fetch) unless `$select` explicitly requests it.
- **Backend Implementation**:
  - Add read models and seam methods for Companies, Notes, Criteria, and SearchRuns.
  - Implement database-level projection in `MongoOwnerReadSeam.QueryNotes` excluding `content` unless `$select` requests it.
  - Add compound indexes with `owner_id` first in `TrackingIndexInitializer`.
  - Expose controllers for each entity set under `/odata`.
- **Frontend Implementation**:
  - Migrate `/companies`, `/notes`, `/criteria`, and `/search-runs` list pages to OData.

### Phase 6: User Story 4 - Query Guardrails, Abuse Prevention & Benchmark (Priority: P2) [US4]
- Extend query abuse integration test suite across Companies, Applications, Notes, Criteria, and SearchRuns.
- Extend multi-owner isolation tests across all entity sets.
- Execute 5,000-document Testcontainers benchmark test verifying query latency < 1 second (SC-007).

### Phase 7: User Story 5 - Contract Drift, Legacy Retirement & Web Verification (Priority: P3) [US5]
- **Tests First**:
  - Playwright assertion verifying zero network requests go to `/api/v1/` list endpoints during full UI traversal.
- **Retirement Implementation**:
  - Remove the 6 list endpoints from `RoleRadar.Api/Endpoints/`.
  - Verify detail endpoints (`GET /api/v1/{entity}/{id}`), mutation endpoints (`POST`, `PATCH`), the CLI, and the MCP server remain operational.
  - Run `dotnet build` to regenerate `contracts/openapi/roleradar-api.json`.
  - Run `pnpm --filter web run generate-api-types` to update `contracts/client/api-types.ts`.
  - Update OpenAPI contract drift assertions.

### Phase 8: Polish, Operational Docs & Spike Cleanup
- **Deliverables**:
  - Update documentation with curl examples demonstrating bearer token querying of `/odata`.
  - Document operational instructions for `reconcile-read-models` (emphasizing that Matt runs it on the real database after taking a backup; agy never does).
  - Document Excel and Power BI connectivity as a prospective follow-up feature, detailing required headers and bearer auth configuration.
  - Update `AGENTS.md` with standing rules: OData allowlists, mandatory `IOwnerReadSeam`, and zero bypass of repository choke points.
  - Delete throwaway spike code in `specs/006-odata-reads/spike/` while preserving `REPORT.md` and `docs/adr/0028-odata-read-seam-and-query-execution.md`.

---

## Test Strategy

```text
                  /\
                 /  \     Playwright Multi-Page E2E Tests
                /----\    - Server sorting across pages in Chromium
               /      \   - Zero calls to retired /api/v1 list endpoints
              /--------\  5,000-Row Benchmark & Translation (Testcontainers)
             /          \ - Latency < 1s for 5,000 synthetic rows (SC-007)
            /------------\- Case-insensitive collation & nulls-last ordering
           /              \ Abuse, Isolation & Authorization Tests (Testcontainers)
          /----------------\- 401 anon, 403 scope/subject, 405 on non-GET, 400 bad queries
         /                  \- Owner A cannot see Owner B data (0 docs returned)
        /--------------------\ Contract Drift Tests (xUnit)
       /                      \- Running /odata/$metadata == contracts/odata/metadata.xml
      /------------------------\ Architecture & Unit Tests (NetArchTest / xUnit / Vitest)
     /                          \- API zero Mongo refs; consistency test: rank == rank(disp)
```

1. **Authorization Tests**:
   - Verify anonymous requests return HTTP 401.
   - Verify tokens lacking `roleradar.api` scope return HTTP 403.
   - Verify tokens with non-owner subjects return HTTP 403.
   - Verify valid owner tokens return HTTP 200.
2. **Abuse Tests**:
   - Verify `$top=500` returns HTTP 400 Bad Request.
   - Verify POST/PUT/PATCH/DELETE to `/odata/...` returns HTTP 405 Method Not Allowed with `Allow: GET`.
   - Verify unlisted properties and disallowed operators (e.g. arithmetic, lambdas) return HTTP 400.
3. **Owner Isolation Tests**:
   - Seed data for `owner-1` and `owner-2` in Testcontainers.
   - Query `/odata` with `owner-1` JWT: returns only `owner-1` items.
   - Query `/odata` with `owner-2` JWT: returns 0 items belonging to `owner-1`.
   - Confirm `owner_id` is never serialized in response JSON.
4. **Consistency & Rename Cascade Tests**:
   - Unit test asserting `disposition_rank == rank(disposition)` across all postings.
   - Integration test asserting company and posting renames propagate atomically to child documents.
5. **5,000-Row Performance Sanity Check**:
   - Benchmark test in Testcontainers verifying filtered and sorted queries against a 5,000-document collection complete in under 1 second.
6. **Web E2E Verification**:
   - Real Chromium Playwright test verifying table column sorting updates the URL, preserves order across page navigation, and dispatches zero requests to legacy list endpoints.
