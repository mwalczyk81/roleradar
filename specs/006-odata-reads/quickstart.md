# Quickstart & Verification Guide: OData Read Service

**Feature**: `006-odata-reads`  
**Date**: 2026-11-01  
**Status**: Draft  

This guide provides step-by-step verification procedures for validating the OData read service, owner scoping, abuse guards, contract drift, and multi-page web sorting against an isolated test environment.

---

## 1. Prerequisites & Environment

All verification steps run against isolated local processes or ephemeral Testcontainers. Never run commands against live or developer MongoDB databases.

- **.NET SDK**: 10.0+
- **Node.js**: 22+ with `pnpm`
- **Docker**: Running (for Testcontainers integration and Playwright web E2E tests)
- **Repo Directory**: `C:\Users\mwalc\source\repos\roleradar`

---

## 2. Fast Test Verification

Execute the fast test suite (Architecture, Unit, and Contract tests):

```powershell
pwsh ./scripts/test-fast.ps1
```

**Expected Results**:
- `RoleRadar.Tests.Architecture`: Passes. Enforces that `RoleRadar.Api` contains zero direct references to `MongoDB.Driver` collections and that all OData controller queries route through `IOwnerReadSeam`.
- `RoleRadar.Tests.Unit`: Passes. Validates query options parsing, `buildODataQuery` TypeScript utility, disposition rank mapping, and null ordering.
- `RoleRadar.Tests.Contract`: Passes. Asserts byte-for-byte / canonical equality between running `/odata/$metadata` and `contracts/odata/$metadata.xml`.

---

## 3. Querying the OData Service via HTTP (curl)

Start the local API host in test mode or launch integration tests. Querying endpoints requires an authenticated owner JWT token with the `roleradar.api` scope.

### 3.1 Fetch Service Metadata ($metadata)

```bash
curl -s -k https://localhost:5002/odata/\$metadata
```
**Expected Outcome**: Returns HTTP 200 OK with `Content-Type: application/xml` matching the EDM schema in [`contracts/odata-metadata.xml`](./contracts/odata-metadata.xml).

### 3.2 Global Sorting, Filtering & Count over Postings

```bash
curl -s -k -X GET \
  "https://localhost:5002/odata/Postings?\$filter=disposition%20eq%20'applied'&\$orderby=score%20desc&\$top=25&\$count=true" \
  -H "Authorization: Bearer <VALID_OWNER_JWT>"
```
**Expected Outcome**: Returns HTTP 200 OK with:
- `@odata.count`: Total count of applied postings across the entire catalog (e.g. `240`).
- `value`: An array of 25 postings ordered by `score desc` with null scores placed last.
- Response payload strictly omits `owner_id`.

### 3.3 Case-Insensitive Search on Postings

```bash
curl -s -k -X GET \
  "https://localhost:5002/odata/Postings?\$filter=contains(tolower(title),'engineer')%20or%20contains(tolower(companyName),'engineer')" \
  -H "Authorization: Bearer <VALID_OWNER_JWT>"
```
**Expected Outcome**: Returns HTTP 200 OK with matching postings evaluated case-insensitively.

---

## 4. Abuse & Constraint Verification

### 4.1 Non-GET Method Rejection (HTTP 405)

```bash
curl -s -k -X POST \
  "https://localhost:5002/odata/Postings" \
  -H "Authorization: Bearer <VALID_OWNER_JWT>" \
  -H "Content-Type: application/json" \
  -d '{"title": "Attempted Mutation"}'
```
**Expected Outcome**: Returns HTTP 405 Method Not Allowed with an `Allow: GET` response header.

### 4.2 Excessive Page Size Rejection (HTTP 400)

```bash
curl -s -k -X GET \
  "https://localhost:5002/odata/Postings?\$top=500" \
  -H "Authorization: Bearer <VALID_OWNER_JWT>"
```
**Expected Outcome**: Returns HTTP 400 Bad Request with an RFC 7807/9457 Problem Details payload explaining that `$top` exceeds the maximum allowed limit of 100.

### 4.3 Disallowed Property / Operator Rejection (HTTP 400)

```bash
# Querying an unlisted or forbidden field (e.g. note content in filter)
curl -s -k -X GET \
  "https://localhost:5002/odata/Notes?\$filter=contains(content,'confidential')" \
  -H "Authorization: Bearer <VALID_OWNER_JWT>"
```
**Expected Outcome**: Returns HTTP 400 Bad Request indicating that filtering on `content` is not permitted.

---

## 5. Idempotent CLI Backfill Dry-Run

Verify the application milestone backfill command against a local test database:

```powershell
# 1. Dry run (reports examined documents without mutating database)
dotnet run --project src/RoleRadar.Cli -- tracking backfill-application-read-models --dry-run --confirm

# 2. Mutating run (in isolated test environment only)
dotnet run --project src/RoleRadar.Cli -- tracking backfill-application-read-models --confirm
```

**Expected Outcome**:
Outputs a structured summary:
```text
Backfill application read models completed:
  Examined: 21
  Updated:  21
  Unchanged: 0
```
Running the command a second time reports `Updated: 0, Unchanged: 21` confirming idempotency.

---

## 6. Web E2E Multi-Page Sorting Verification

Run the web Playwright test suite to verify server-side sorting and multi-page pagination in real Chromium:

```powershell
pnpm --filter web run test:e2e
```

**Expected Validations**:
1. Postings table sorts by `Score` descending; navigating from page 1 to page 2 preserves global sort order across the entire dataset.
2. The "Sorting applies to the rows on this page only" footer disclaimer does **not** appear on any OData-backed list table.
3. Network assertions verify that zero requests are dispatched to legacy `/api/v1/postings` or other legacy list endpoints.
