# Phase 0: Technical Research & Architecture Decisions

**Feature**: `006-odata-reads`  
**Date**: 2026-11-01 (Revised)  
**Status**: Complete  

This document consolidates technical evaluations, spike specifications, and architectural invariants for adding read-only OData v4 queries to RoleRadar while upholding Constitution Principles I–IX (Privacy First, Test-First Development, Test-Enforced Layering, Named Seams Only, Pinned Dependencies).

---

## 1. Executable Spike: OData on .NET 10, Auth & Query Translation

Rather than relying on documentation claims, Phase 1 establishes an **executable, throwaway spike** located under `specs/006-odata-reads/spike/` (excluded from production builds and never shipped). The spike gates all subsequent implementation phases with an explicit, written Go / No-Go decision.

### 1.1 Spike Scope & Verification Criteria

The spike proves three specific capabilities running against .NET 10 with centrally pinned NuGet dependencies (`Microsoft.AspNetCore.OData 9.5.0`, `MongoDB.Driver 3.13.0`):

#### (a) Routing & Authentication Coexistence
- **Goal**: Prove `Microsoft.AspNetCore.OData 9.5.0` controllers coexist alongside ASP.NET Core minimal API endpoints (`/api/v1`) without routing conflicts.
- **Verification**:
  - Minimal APIs map to `/api/v1/...` via `MapGroup`.
  - OData controllers map to `/odata/...` via `MapControllers()` and an EDM model.
  - OData endpoints enforce the existing JWT Bearer authentication policy (`roleradar.api` scope and allowlisted owner subject claim).

#### (b) MongoDB IQueryable Query Translation (Testcontainers)
- **Goal**: Determine whether OData query options (`$filter`, `$orderby`, `$top`, `$count`, `$select`) translate efficiently into MongoDB queries over an owner-scoped `IQueryable` using `MongoDB.Driver 3.13.0` in an ephemeral MongoDB Testcontainer.
- **Approaches Evaluated in Spike**:
  1. **Approach 1 (Fewer Dependencies - Preferred)**: Plain `ODataQueryOptions.ApplyTo` with Mongo-safe validation settings (disabling null-propagation wrappers and constant parameterization that confuse Mongo LINQ3 providers).
  2. **Approach 2**: `MongoDB.AspNetCore.OData 1.1.3`. Its package metadata requires `Microsoft.AspNetCore.OData >= 8.1.2` and `MongoDB.Driver >= 3.11.1`. The spike tests whether 1.1.3 loads cleanly against OData 9.5.0 and Driver 3.13.0, and whether its query visitor translates without exception.
- **Query Capabilities to Verify**:
  - `$select`: Projections of scalar fields and collections.
  - `$count`: Native total document count execution (`@odata.count`).
  - `$orderby`: Text sorting with case-insensitive collation (`Collation(locale: "en", strength: CollationStrength.Secondary)`), and `$orderby=disposition` mapped to `disposition_rank`.
  - `$filter`: String functions (`contains`, `startswith`, `endswith`, `tolower`) and logical operators (`eq`, `ne`, `gt`, `ge`, `lt`, `le`, `and`, `or`, `not`).
- **Decision Rule**: Prefer the option with fewer packages that passes all translation checks. If both fail, the fallback is building MongoDB `FilterDefinition` and `SortDefinition` expressions directly from the validated OData AST behind the Application read seam.
- **Honesty Invariant**: The spike report will state plainly what was verified, what failed, and what could not be verified.

#### (c) Error Handling & HTTP Method Guardrails
- **Goal**: Verify that query validation settings produce standard RFC 7807/9457 Problem Details (`ErrorEnvelope`) for 400 Bad Request, and that non-GET methods return HTTP 405.
- **Verification**:
  - Requests exceeding `$top=100`, referencing unlisted properties, or using disallowed operators (arithmetic, lambdas) return HTTP 400 with Problem Details.
  - Non-GET requests (POST, PUT, PATCH, DELETE) to `/odata/...` endpoints return HTTP 405 Method Not Allowed with an `Allow: GET` header.

### 1.2 Written Go / No-Go Gate
The spike writes its empirical findings to `specs/006-odata-reads/spike/REPORT.md`. If routing, auth, or query translation fails without a viable fallback, the feature stops immediately and does not proceed to production code.

---

## 2. The Read Seam Architecture (ADR-0028)

### Constitution Principle IV Compliance
Exposing raw database drivers or un-scoped queries directly to API controllers violates Principle IV (*Named Seams Only*). ADR-0028 defines an explicit, named query interface in the Application layer:

```csharp
namespace RoleRadar.Application.Reading;

public interface IOwnerReadSeam
{
    IQueryable<PostingReadModel> QueryPostings(OwnerId ownerId);
    IQueryable<CompanyReadModel> QueryCompanies(OwnerId ownerId);
    IQueryable<ApplicationReadModel> QueryApplications(OwnerId ownerId);
    IQueryable<NoteReadModel> QueryNotes(OwnerId ownerId);
    IQueryable<CriterionReadModel> QueryCriteria(OwnerId ownerId);
    IQueryable<SearchRunReadModel> QuerySearchRuns(OwnerId ownerId);
}
```

### Invariants Enforced by Architecture Tests
1. **Mandatory OwnerId**: Every method accepts a strongly-typed `OwnerId`.
2. **Pre-Filtered Scoping**: The Infrastructure implementation (`MongoOwnerReadSeam`) applies `.Where(doc => doc.OwnerId == ownerId.Value)` prior to returning `IQueryable<T>`.
3. **No Direct Collection References**: An automated test in `RoleRadar.Tests.Architecture` verifies that `RoleRadar.Api` contains zero references to `MongoDB.Driver` or `IMongoCollection<T>`.

---

## 3. Derived Stored Fields & Single Choke Point

To satisfy SC-006 (< 1 second response over 5,000 records), derived fields are pre-materialized in MongoDB documents rather than computed at read time using multi-collection `$lookup` joins.

### 3.1 Single Choke Point: Repository Upsert
Derived fields must never be calculated haphazardly across disparate services. Instead, the persistence layer acts as the single choke point:
- **`disposition_rank`**: Computed automatically during posting serialization (`SerializePosting` in `MongoPostingRepository`) from the domain `Disposition` enum:
  - `unscored` = 0, `lead` = 1, `scored` = 2, `applied` = 3, `rejected` = 4, `screened_out` = 5, `closed` = 6.
- **`companyName`**: Enriched and verified during posting and application upsert.
- **`postingTitle`**: Enriched and verified during application upsert.
- **Consistency Test**: An automated unit and integration test verifies that for 100% of persisted postings, `doc.disposition_rank == rank(doc.disposition)`. Any discrepancy fails the build.

### 3.2 Write Paths Touching Derived Stored Fields
Every write path in the system routes through these repository choke points:
1. `TransitionPostingHandler`: transitions posting disposition -> calls `UpsertPostingAsync` -> updates `disposition_rank`.
2. `ImportPostingsHandler` (CSV and tracker import pipeline): imports postings -> calls `UpsertPostingAsync` -> updates `disposition_rank` and `companyName`.
3. `RecordPostingHandler` (`posting_record` MCP/CLI tool): creates or updates posting -> calls `UpsertPostingAsync`.
4. `ScorePostingHandler`: recalculates criteria score -> calls `UpsertPostingAsync`.
5. `CreateApplicationHandler`: creates application -> calls `UpsertApplicationAsync` -> sets initial `postingTitle`, `companyName`, `status = applied`, `hasInterview = false`, `lastEventAt = appliedAt`.
6. `AppendApplicationEventHandler`: appends lifecycle event -> calls `UpsertApplicationAsync` -> updates `status`, `hasInterview`, `firstResponseAt`, `lastEventAt`.
7. **Company Rename Write Path** (`UpdateCompanyHandler`): when a company name changes, the handler updates `companyName` on its child postings and applications in the same transaction.
8. **Posting Title Rename Write Path**: when a posting title changes, the handler updates `postingTitle` on its child applications in the same transaction.
9. `reconcile-read-models` CLI command: repairs or backfills missing derived fields across collections.

---

## 4. Renames & Transactional Consistency

When aggregate titles or names change, denormalized read models must not become stale:
- **Company Name Rename**: Updating a company's name updates `company_name` across all associated `postings` and `applications` documents within the same MongoDB transaction.
- **Posting Title Rename**: Updating a job posting's title updates `posting_title` across all associated `applications` documents within the same MongoDB transaction.
- **Tests First**: Dedicated transaction integration tests in `RoleRadar.Tests.Integration` will verify that renaming a company or posting propagates to all child documents atomically, rolling back if any step fails.

---

## 5. Idempotent CLI Reconcile Command: `reconcile-read-models`

To safely backfill and verify historical records, a single unified CLI command replaces any ad-hoc backfill scripts:

```powershell
roleradar tracking reconcile-read-models [--dry-run] [--confirm]
```

### Safety & Operational Rules
1. **Scope**:
   - Postings: backfills `disposition_rank` and resolves missing `companyName`.
   - Applications: backfills `postingTitle`, `companyName`, `hasInterview`, `firstResponseAt`, and `lastEventAt` by replaying historical events.
2. **Dry Run by Default**: Without `--confirm`, the command operates in dry-run mode, printing what would be changed without writing to the database.
3. **Explicit Confirmation**: The command requires `--confirm` to execute mutations, warning the operator to take an encrypted backup prior to running.
4. **Structured Count Report**: Emits a deterministic count summary:
   ```text
   Reconciliation Summary:
     Postings examined:     240
     Postings updated:        0
     Applications examined:  21
     Applications updated:    0
     Unchanged:             261
   ```
5. **Execution Boundary**: agy and automated CI jobs **never** run this command against the developer's live database. It is validated strictly against throwaway Testcontainers. Matt runs it manually on the real database after taking a verified backup.

---

## 6. Authorization & Security Test Strategy

The OData endpoints use the existing `ApiOwnerPolicy` enforcing the `roleradar.api` scope and matching the owner subject claim (`sub`) against the allowlisted owner ID.

### Automated Authorization Test Suite
In `RoleRadar.Tests.Integration`, dedicated tests will verify:
- **Anonymous Request**: `GET /odata/Postings` without an Authorization header returns `HTTP 401 Unauthorized`.
- **Wrong Scope**: Request with a valid JWT lacking the `roleradar.api` scope returns `HTTP 403 Forbidden`.
- **Non-Owner Subject**: Request with a valid JWT carrying a subject claim other than the allowlisted owner returns `HTTP 403 Forbidden`.
- **Valid Owner Token**: Request with a valid JWT carrying `roleradar.api` and the correct owner subject returns `HTTP 200 OK`.

---

## 7. Web Migration & Legacy Deletion

### Elimination of Per-Row Fetching
In the existing web UI, `web/app/routes/applications._index.tsx` relies on `web/app/lib/application-context.server.ts` to execute per-row fetches resolving company and posting details.

Because `ApplicationReadModel` now exposes pre-materialized `companyName` and `postingTitle`, `application-context.server.ts` is obsolete. During Phase 3:
1. `web/app/routes/applications._index.tsx` is migrated to use `/odata/Applications`.
2. `web/app/lib/application-context.server.ts` and its test suite `web/tests/routes/applications.loader.test.ts` are deleted.
3. Applications loader tests are updated to assert on direct OData read models.

---

## 8. Summary of What Was and Was Not Verified

- **Verified by Code Inspection**:
  - `Directory.Packages.props` manages package versions centrally with `CentralPackageTransitivePinningEnabled`.
  - `MongoPostingRepository.SerializePosting` acts as the single serialization point for postings.
  - `web/app/ds` DataTable renders the page-only sort disclaimer based on `columns.some(c => c.sortable)`.
- **What Could NOT Be Verified Prior to the Spike**:
  - The exact behavior of `MongoDB.AspNetCore.OData 1.1.3` when executed against `MongoDB.Driver 3.13.0` and `Microsoft.AspNetCore.OData 9.5.0` on .NET 10.
  - The degree of LINQ expression compatibility between plain `ODataQueryOptions.ApplyTo` and Mongo's LINQ3 provider regarding case-insensitive collation and nulls-last sorting.
  - Both will be proven empirically by the executable spike in Phase 1 before production implementation starts.
