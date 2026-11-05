# Spike Report: OData Reads on .NET 10 with MongoDB Driver 3.13.0 (Including Addendum T006a)

**Document Reference**: `specs/006-odata-reads/spike/REPORT.md`  
**Date**: 2026-11-01  
**Target Solution**: RoleRadar (.NET 10, ASP.NET Core Minimal APIs + OData v4, MongoDB Driver 3.13.0)  
**Spike Project**: `specs/006-odata-reads/spike/SpikeApp.csproj` (Throwaway, isolated, net10.0)  
**Status**: Completed  
**Recommendation**: **GO**

---

## 1. Executive Summary & Decision

The executable spike and its addendum (T006a) for Feature 006 (OData Reads) were executed on .NET 10 against throwaway Testcontainers MongoDB (`mongo:8.0`). All spike questions have been answered empirically with 14 passing automated tests and index explain plan verification:

- **Decision**: **GO**.
- **Coexistence**: ASP.NET Core OData 9.5.0 controllers and Minimal API endpoints (`/api/v1`) coexist seamlessly in the same application, sharing the identical JWT Bearer policy (`roleradar.api` scope and owner `sub` validation).
- **Package Selection**: **Adopt Approach 1 (Native `Microsoft.AspNetCore.OData 9.5.0`)** and **Reject Approach 2 (`MongoDB.AspNetCore.OData 1.1.3`)**. Inspection confirms `MongoDB.AspNetCore.OData 1.1.3` exports only `MongoEnableQueryAttribute`, which merely configures `HandleNullPropagation = False` and `EnableConstantParameterization = false`. It is built against OData 8.x and adds an extra dependency for zero functional benefit. Approach 2 was inspected via reflection and rejected without execution.
- **Public Name Mapping (`$orderby=disposition`)**: Verified that `$orderby=disposition` (asc and desc) maps to stored `disposition_rank` without exposing `disposition_rank` in the OData EDM model or `$metadata`. Native `SortDefinition` mapping orders records in pipeline rank order (`unscored`, `lead`, `scored`, `applied`, `rejected`, `screened_out`, `closed`).
- **Nulls Last in Both Directions**: Verified for `score` (and date fields of the same shape) across records with values, explicit BSON `null`, and missing properties. Using a flag field `has_score: bool` combined with native `SortDefinition` (`has_score: -1, score: 1/-1, _id: 1`) places all scored documents first and all null/missing documents at the end in both asc and desc sorts.
- **Stable Ordering**: Verified that `$orderby` always ends with the `_id` tiebreaker. Two consecutive pages (`$top=2&$skip=0` and `$top=2&$skip=2`) across tied rows produce zero repeating rows and zero skipped rows.
- **Case-Insensitive Search Rule**: Empirically proved that MongoDB LINQ3 `string.Contains` translates to `$indexOfBytes` / `$regex`, which **does not honor collection collation**. Collation applies to equality (`eq`), prefix matches, and sorting, but NOT to substring containment. Therefore, the web builder and seam must follow the strict rule: always use `contains(tolower(property), 'lowercased-term')`.
- **Explain Plan Verification**: Verified via database `explain` that the `owner_id + disposition_rank` sort executes as pure `IXSCAN` on `ix_owner_disposition_rank` (in-memory `SORT` stage rejected), and filtered title query executes as pure `IXSCAN` on `ix_owner_title` using secondary collation.
- **Projection (`$select`) Boundary**: Two-stage execution (database-level filter/sort/page capped at <=100 rows + in-memory `$select` projection) succeeds cleanly, circumventing MongoDB LINQ3 `ExpressionNotSupportedException` on OData's internal `SelectSome<T>`.

---

## 2. Empirical Verification Matrix

| Area / Question | Target Behavior | Empirical Result | Status |
| :--- | :--- | :--- | :--- |
| **Routing Coexistence** | Minimal APIs (`/api/v1`) and OData (`/odata`) coexist in same web host | Both route trees mount and resolve cleanly without conflicts | **VERIFIED (PASS)** |
| **Authentication & AuthZ** | `roleradar.api` scope and owner subject claim enforced | Anonymous -> 401; Bad Scope -> 403; Bad Subject -> 403; Owner -> 200 | **VERIFIED (PASS)** |
| **HTTP 405 Behavior** | Read-only OData endpoints reject non-GET verbs | `POST /odata/Postings` and `DELETE /odata/Postings` return 405 with `Allow: GET` | **VERIFIED (PASS)** |
| **Query Validation (400)** | `$top > 100` or illegal operators return Problem Details | Returns HTTP 400 with `application/problem+json` describing exceeded limit or disallowed operator | **VERIFIED (PASS)** |
| **Public Name Mapping (T006a.1)** | `$orderby=disposition` sorts by `disposition_rank`; rank not in `$metadata` | `PostingODataModel` exposes `Disposition`, rank omitted from `$metadata`. MapODataOrderByToSortDefinition maps asc (0->6) and desc (6->0) | **VERIFIED (PASS)** |
| **Nulls Last Both Ways (T006a.2)** | `score asc` and `score desc` place null/missing values last | `{ has_score: -1, score: 1/-1, _id: 1 }` places valid scores first and all null/missing records last in both directions | **VERIFIED (PASS)** |
| **Stable Ordering (T006a.3)** | One- and two-key sort with ties never repeats/skips rows across pages | Consecutive `$top=2&$skip=0` and `$skip=2` over tied scores/companies have 0 overlapping IDs | **VERIFIED (PASS)** |
| **Case-Insensitive Search (T006a.4)** | `contains(Title, 'ENGINEER')` vs `contains(tolower(Title), 'engineer')` | Raw contains ignores collation (`matchedLowercase = false`). `contains(tolower(x), 'term')` matches all 4 variations | **VERIFIED (RULE ESTABLISHED)** |
| **Index Explain Plans (T006a.5)** | `explain` confirms planned indexes used (`IXSCAN` vs `COLLSCAN`) | `ix_owner_disposition_rank` used for sort (`IXSCAN`); `ix_owner_title` with collation used for filter (`IXSCAN`) | **VERIFIED (PASS)** |
| **Approach 2 Evaluation (T006a.6)** | Evaluate `MongoDB.AspNetCore.OData 1.1.3` (1.1.2 on 2026-02-05, 1.1.3 on 2026-09-17) | Inspected via reflection; only exports `MongoEnableQueryAttribute` (OData 8.x). Redundant with native setting | **INSPECTED & REJECTED** |
| **$select Projection** | Allowlisted property selection | Direct LINQ3 translation of `SelectSome` fails in Driver; two-stage execution (DB page + in-memory project) succeeds cleanly | **VERIFIED (PASS)** |

---

## 3. Detailed Addendum Findings (T006a)

### 1. Public Name Mapping: `$orderby=disposition`
- **Model Isolation**: In `PostingODataModel`, only `Disposition: string` is declared. `disposition_rank` is completely omitted from the EDM builder and generated `$metadata`.
- **Mapping Technique**: The read seam inspects `OrderByQueryOption.OrderByNodes`. When an `OrderByPropertyNode` targets `"disposition"`, the query builder maps it to the database field `disposition_rank` (or in C# `p.DispositionRank`):
  ```csharp
  "disposition" => isDesc ? builder.Descending(p => p.DispositionRank) : builder.Ascending(p => p.DispositionRank)
  ```
- **Monotonic Verification**:
  - `?$orderby=disposition asc` produced ranks: `0 (unscored), 1 (lead), 2 (scored), 3 (applied), 4 (rejected), 5 (screened_out), 6 (closed)`.
  - `?$orderby=disposition desc` produced ranks: `6 (closed), 5 (screened_out), 4 (rejected), 3 (applied), 2 (scored), 1 (lead), 0 (unscored)`.

### 2. Nulls Last in Both Directions for Score
- **MongoDB Default Behavior**: In MongoDB BSON sort order, `null` and `missing` are treated as the lowest possible values. Therefore, a plain ascending sort (`score: 1`) places nulls and missing values at the *beginning* of the result set, while a plain descending sort (`score: -1`) places nulls at the *end*.
- **Technique Evaluated (Flag Field `has_score: bool`)**:
  Every document maintains `has_score: bool` (`true` when score is present, `false` when null or missing).
  In BSON sort order, `false (0) < true (1)`. Sorting by `has_score: -1` guarantees that all scored records (`true`) are processed first, and all unscored records (`false`) are placed at the end.
  - **ASC Sort**: `builder.Descending(p => p.HasScore).Ascending(p => p.Score).Ascending(p => p.Id)`
    *Result*: `[50, 70, 80, 80, 80, 80, 85, 90, 95]`, followed by all 3 null and missing records at the end.
  - **DESC Sort**: `builder.Descending(p => p.HasScore).Descending(p => p.Score).Ascending(p => p.Id)`
    *Result*: `[95, 90, 85, 80, 80, 80, 80, 70, 50]`, followed by all 3 null and missing records at the end.
- **Indexes Needed**:
  - For ASC with nulls last: `{ owner_id: 1, has_score: -1, score: 1, _id: 1 }`
  - For DESC with nulls last: `{ owner_id: 1, has_score: -1, score: -1, _id: 1 }`
- **Applicability to Dates**: `firstResponseAt` and `lastEventAt` follow the exact same pattern with `has_first_response: bool` and `has_last_event: bool`.

### 3. Stable Ordering Across Paged Result Sets
- **Requirement**: Prevent row repetition and skipped items when sort keys tie across page boundaries.
- **Test Result**:
  Four documents were seeded with tied score `80` and tied disposition `applied`.
  - **Single Key (`$orderby=score asc`)**:
    - Page 1 (`$top=2&$skip=0`): retrieved docs with IDs ending in `1111` and `2222`.
    - Page 2 (`$top=2&$skip=2`): retrieved docs with IDs ending in `3333` and `4444`.
    - Intersection: `Empty`.
  - **Two Keys (`$orderby=companyName asc,disposition desc`)**:
    - Tied on company `"TiedCorp A"` and `"TiedCorp B"`.
    - Page 1 (`$top=2&$skip=0`) and Page 2 (`$top=2&$skip=2`) had zero overlapping IDs.
  - **Invariant**: Every sort pipeline constructed by the read seam must append `_id: 1` as the final tiebreaker.

### 4. Case-Insensitive Search Rule (`contains`)
- **Empirical Finding**:
  - Running `_collection.AsQueryable(new AggregateOptions { Collation = collation }).Where(p => p.Title.Contains("ENGINEER"))` against a collection configured with secondary collation `en` **failed to match** lowercase `"software engineer"`.
  - MongoDB Driver 3.13.0 LINQ3 translates `string.Contains` to `$indexOfBytes` or `$regex`. In MongoDB, neither `$indexOfBytes` (byte-level) nor regular expressions (`$regex`) support collation!
  - Running `_collection.AsQueryable().Where(p => p.Title.ToLower().Contains("engineer"))` matched all 4 title variants.
  - Full equality matches (`eq(Title, "SOFTWARE ENGINEER")`) with secondary collation succeeded and utilized the collation index.
- **Rule for Web Builder and Seam**:
  For substring filtering (`contains`, `startswith`, `endswith`), the client/builder must always format the query as:
  `contains(tolower(Property), 'lowercased-term')`
  Collation must NOT be relied upon for substring containment.

### 5. Explain Output Analysis
Database explain was executed with `verbosity: executionStats` on throwaway Testcontainers MongoDB:

#### A. Sort by `owner_id + disposition_rank`:
- **Winning Plan Stage**: `FETCH` -> `IXSCAN`
- **Index Used**: `ix_owner_disposition_rank` (`{ owner_id: 1, disposition_rank: 1, _id: 1 }`)
- **Direction**: `forward`
- **In-Memory Sort**: None (`SORT` stage was in `rejectedPlans`).
- **Raw Extract**:
  ```json
  "winningPlan" : {
    "stage" : "FETCH",
    "inputStage" : {
      "stage" : "IXSCAN",
      "keyPattern" : {
        "owner_id" : 1,
        "disposition_rank" : 1,
        "_id" : 1
      },
      "indexName" : "ix_owner_disposition_rank",
      "direction" : "forward"
    }
  }
  ```

#### B. Filter on `owner_id + title` with Secondary Collation:
- **Winning Plan Stage**: `FETCH` -> `IXSCAN`
- **Index Used**: `ix_owner_title` (`{ owner_id: 1, title: 1, _id: 1 }`)
- **Collation**: `{ locale: "en", strength: 2 }`
- **Execution Stats**: `totalKeysExamined: 3`, `totalDocsExamined: 3`, `nReturned: 3`, `executionTimeMillis: 0`.
- **Raw Extract**:
  ```json
  "winningPlan" : {
    "stage" : "FETCH",
    "inputStage" : {
      "stage" : "IXSCAN",
      "keyPattern" : {
        "owner_id" : 1,
        "title" : 1,
        "_id" : 1
      },
      "indexName" : "ix_owner_title",
      "collation" : {
        "locale" : "en",
        "strength" : 2
      }
    }
  }
  ```

---

## 4. Corrected Evaluation of `MongoDB.AspNetCore.OData`

- **Release History**:
  - `MongoDB.AspNetCore.OData 1.1.2` was released on **2026-02-05**.
  - `MongoDB.AspNetCore.OData 1.1.3` was released on **2026-09-17**.
  *(The previous mention of "no releases since 2022" was factually incorrect and is hereby retracted).*
- **Assessment (Approach 2: Inspected, Not Executed)**:
  - Decompilation and reflection over the package assembly confirmed it contains only one class: `MongoDB.AspNetCore.OData.MongoEnableQueryAttribute`.
  - Its entire logic sets `HandleNullPropagation = HandleNullPropagationOption.False` and `EnableConstantParameterization = false`.
  - It targets OData 8.x abstractions and provides no LINQ3 translation helpers or query rewrites for MongoDB Driver 3.x.
  - Native `Microsoft.AspNetCore.OData 9.5.0` allows setting `HandleNullPropagationOption.False` directly in two lines of configuration.
- **Decision**: Reject `MongoDB.AspNetCore.OData 1.1.3`. It adds a package for zero functional benefit.

---

## 5. Unverified Areas & Implementation Boundaries

The following areas could not be verified in the throwaway spike and must be accounted for during the production implementation:
1. **Whole-Document Materialization for Notes (`$select`)**:
   In the spike, `$select` was applied in memory to materialized entities capped at <=100 rows. For `Notes`, the `content` field can be large (multiple kilobytes of Markdown). Materializing whole `Note` documents across 100 rows only to discard `content` in memory wastes database I/O and memory.
   *Production Seam Guard*: The `MongoOwnerReadSeam` must exclude `content` in MongoDB projection (`Builders<NoteDoc>.Projection.Exclude(n => n.Content)`) unless `$select` explicitly requests `content`.
2. **High-Concurrency Connection Pooling**:
   The spike executed single-threaded and test-runner parallel queries against a single Testcontainers container. Sustained connection pooling under 100+ concurrent requests against a multi-replica MongoDB set will be verified during Phase 6 benchmarks.
3. **Deeply Nested Logical Filters**:
   Filter expressions with complex parentheses (`(A and B) or (C and (D or E))`) beyond 10 AST nodes. Validated by `MaxNodeCount = 100`, but edge-case precedence will be covered in Phase 6 abuse tests.

---

## 6. Verification Commands and Raw Outputs

### Spike App Tests (14 Tests):
Command:
```powershell
dotnet test specs/006-odata-reads/spike/SpikeApp.csproj
```
Raw Output:
```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  SpikeApp -> C:\Users\mwalc\source\repos\roleradar\specs\006-odata-reads\spike\bin\Debug\net10.0\SpikeApp.dll
Test run for C:\Users\mwalc\source\repos\roleradar\specs\006-odata-reads\spike\bin\Debug\net10.0\SpikeApp.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    14, Skipped:     0, Total:    14, Duration: 29 s - SpikeApp.dll (net10.0)
```

### RoleRadar Solution Fast Test Loop (Architecture, Unit, Contract):
Command:
```powershell
pwsh ./scripts/test-fast.ps1
```
Raw Output:
```text
==> Running tests/RoleRadar.Tests.Architecture/RoleRadar.Tests.Architecture.csproj...
Passed!  - Failed:     0, Passed:    30, Skipped:     0, Total:    30, Duration: 349 ms - RoleRadar.Tests.Architecture.dll (net10.0)
==> Running tests/RoleRadar.Tests.Unit/RoleRadar.Tests.Unit.csproj...
Passed!  - Failed:     0, Passed:   447, Skipped:     0, Total:   447, Duration: 20 s - RoleRadar.Tests.Unit.dll (net10.0)
==> Running tests/RoleRadar.Tests.Contract/RoleRadar.Tests.Contract.csproj...
Passed!  - Failed:     0, Passed:   170, Skipped:     0, Total:   170, Duration: 32 s - RoleRadar.Tests.Contract.dll (net10.0)
==> Fast test loop completed successfully!
```
