# ADR 0028: OData Read Seam and Query Execution Architecture

- **Status**: Accepted
- **Date**: 2026-11-01
- **Feature**: `006-odata-reads`

## Context and Problem Statement

Feature 006 replaces client-side in-memory sorting/filtering across fixed 25-row result sets with server-side querying over the full dataset across all domain entity sets (`postings`, `companies`, `applications`, `notes`, `criteria`, `search_runs`). Reads move to OData v4 (`$filter`, `$orderby`, `$top`, `$skip`, `$count`, `$select`) under `/odata`, while writes remain REST command endpoints under `/api/v1` (with transaction decorators and audit entries).

Key architectural questions and requirements:

1. **Constitution Principle IV (Named Seams Only)**: The API host must never access MongoDB collections directly. All queries must flow through an Application-layer, owner-scoped read seam.
2. **Constitution Principle IX (.NET 10, UTC, UUIDv7, `owner_id` on every collection)**: Multi-tenant safety requires that every query is unconditionally pre-scoped by the caller's verified `OwnerId`.
3. **LINQ Provider Compatibility**: MongoDB Driver 3.13.0 uses the LINQ3 provider. We must determine how OData query expressions translate to MongoDB pipelines without runtime failures.
4. **Third-Party Package Evaluation**: Evaluate `MongoDB.AspNetCore.OData 1.1.3` against native `Microsoft.AspNetCore.OData 9.5.0`.
5. **Sort Order Invariants**:
   - `disposition` must sort in job-search pipeline order (`unscored`, `lead`, `scored`, `applied`, `rejected`, `screened_out`, `closed`) via public name `$orderby=disposition` without exposing internal `disposition_rank` in the EDM model or `$metadata`.
   - `score`, `firstResponseAt`, and `lastEventAt` must sort with nulls and missing values last in *both* ascending and descending directions.
   - Sorting must be deterministic: tied rows must never repeat or skip across consecutive `$skip` pages.
6. **Case-Insensitive Search**: Substring searches (`contains`) must operate case-insensitively.
7. **Performance Target**: Paged queries over 5,000+ rows must execute in under 1 second backed by index scans (`IXSCAN`).

## Decision Drivers

- **Constitution Principle IV**: Named seams only; no generic repositories or leaky abstractions.
- **Constitution Principle I & IX**: Absolute owner isolation (`owner_id` index prefix and mandatory pre-scoping).
- **Dependency Hygiene**: Avoid unnecessary packages; prefer officially supported Microsoft and MongoDB packages.
- **Robust Error Handling**: Return RFC 7807/9457 Problem Details (`ErrorEnvelope`) for invalid query parameters and HTTP 405 Method Not Allowed for non-GET methods.

## Decision Outcome

### 1. Rejection of `MongoDB.AspNetCore.OData 1.1.3` (Inspected, Not Executed)

In the Phase 1 spike, reflection and decompilation of `MongoDB.AspNetCore.OData 1.1.3` (released 2026-09-17, following 1.1.2 on 2026-02-05) confirmed:

- It exports only a single class: `MongoDB.AspNetCore.OData.MongoEnableQueryAttribute`.
- It is built against OData 8.x abstractions.
- Its entire implementation consists of setting `HandleNullPropagation = HandleNullPropagationOption.False` and `EnableConstantParameterization = false`.
- It provides no custom expression visitors, LINQ3 pipeline optimizations, or projection translators for MongoDB Driver 3.x.

**Decision**: `MongoDB.AspNetCore.OData 1.1.3` was inspected and **rejected without execution**. We adopt native `Microsoft.AspNetCore.OData 9.5.0` directly, which configures `HandleNullPropagationOption.False` natively.

### 2. The Application Layer Read Seam (`IOwnerReadSeam`)

In compliance with Constitution Principle IV, a named read seam interface is defined in the Application layer (`RoleRadar.Application.Reading.IOwnerReadSeam`).

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

- **Owner Scoping at the Source**: Every query method requires a strongly-typed `OwnerId`.
- **Infrastructure Implementation**: `MongoOwnerReadSeam` in `RoleRadar.Infrastructure` returns an `IQueryable<T>` initialized with `collection.AsQueryable(aggregateOptions).Where(doc => doc.OwnerId == ownerId.Value)`.
- **Architecture Enforcement**: Architecture tests enforce that `RoleRadar.Api` controllers only query through `IOwnerReadSeam`, never referencing `IMongoCollection<T>` or `IMongoDatabase`.

### 3. Two-Stage Query Execution Mechanics (Addressing MongoDB LINQ3 Limitations)

Empirical testing during the spike identified a critical boundary in MongoDB Driver 3.13.0 LINQ3:

- `$filter`, `$orderby`, `$skip`, `$top`, and `$count` translate natively to MongoDB aggregation stages (`$match`, `$sort`, `$skip`, `$limit`, and `$count`).
- `$select`: When `ODataQueryOptions.ApplyTo` applies `$select` to a MongoDB `IQueryable`, OData injects its internal dynamic type `SelectSome<T>`. MongoDB's LINQ3 provider throws `MongoDB.Driver.Linq.ExpressionNotSupportedException` because it does not recognize `SelectSome<T>` as a valid `$project` expression.

**Execution Pattern**:

1. The OData controller injects `ODataQueryOptions<T>` and validates allowed properties, limits, and operators (`options.Validate(validationSettings)`).
2. The controller executes `$filter`, `$orderby`, `$skip`, and `$top` against the `IQueryable<T>` returned by `IOwnerReadSeam` with `HandleNullPropagation = HandleNullPropagationOption.False`.
3. MongoDB executes the query on indexed fields, returning the capped page of matching documents (maximum 100 rows per `MaxTop`).
4. If `$select` is present, `options.SelectExpand.ApplyTo` is applied to the materialized list in memory before serialization.
5. If `$count=true` is present, the count is evaluated against the filtered `IQueryable<T>` via `.Count()` in MongoDB.
6. **Payload Guard for Notes**: To prevent reading whole `Note` documents across large result sets, the database query will project away `content` unless `$select` explicitly includes it.

### 4. Ordering & Collation Strategy

#### A. Public Name Mapping for Disposition

Clients request sorting by the public name `$orderby=disposition asc` or `desc`. The EDM model declares `Disposition: string`; `disposition_rank` is never exposed in `$metadata`. The read seam maps `disposition` to the stored integer field `disposition_rank`:

- `disposition asc` -> `disposition_rank: 1` (ranks 0 to 6: `unscored`, `lead`, `scored`, `applied`, `rejected`, `screened_out`, `closed`).
- `disposition desc` -> `disposition_rank: -1` (ranks 6 to 0: `closed`, `screened_out`, `rejected`, `applied`, `scored`, `lead`, `unscored`).
Database explain confirmed this uses index `ix_owner_disposition_rank` (`{ owner_id: 1, disposition_rank: 1, _id: 1 }`) with zero in-memory sort stages.

#### B. Nulls Last in Both Directions

MongoDB BSON sorts treat `null` and `missing` as the lowest rank. Thus, a plain ascending sort places nulls first.
To guarantee nulls last in both directions, documents maintain boolean flag fields (`has_score`, `has_first_response`, `has_last_event`).
The sort definition maps:

- `score asc` -> `{ has_score: -1, score: 1, _id: 1 }`
- `score desc` -> `{ has_score: -1, score: -1, _id: 1 }`
In BSON boolean sort order, `false (0) < true (1)`, so sorting `has_score: -1` guarantees valid values always precede null and missing records.
Indexes required:
- `{ owner_id: 1, has_score: -1, score: 1, _id: 1 }`
- `{ owner_id: 1, has_score: -1, score: -1, _id: 1 }`

#### C. Deterministic Ordering Tiebreaker

Every sort pipeline appends `_id: 1` as the final tiebreaker. The spike proved that consecutive pages (`$top=2&$skip=0` and `$skip=2`) across tied values produce zero repeated rows and zero skipped rows.

#### D. Case-Insensitive Search Rule & Scan Reality

Empirical testing proved that MongoDB LINQ3 `string.Contains` translates to `$indexOfBytes` or `$regex`, neither of which honors MongoDB collection collation.
**Rule**: Substring searches (`contains`, `startswith`, `endswith`) must always be formatted by clients and the seam as:
`contains(tolower(Property), 'lowercased-term')`
Collation (`en` locale, strength 2) is used for equality (`eq`), prefix lookups, and text sorting.

**Unindexed Scan Note**: In MongoDB, substring containment expressions (`contains`, `startswith`, `endswith`) cannot perform B-tree index seeks and execute as unindexed scans over the owner's pre-filtered documents. To prevent silent case-sensitivity bugs, the server strictly rejects raw `contains/startswith/endswith` unless applied as `function(tolower(property), 'lowercased term')` (returning HTTP 400 Problem Details otherwise); the typed web query builder always emits this canonical form.

### 5. Derived Fields & Write Choke Points

- **Application Enriched Fields**: To ensure list queries over 5,000+ applications execute in <1s without complex `$lookup` aggregation joins at read time, `postingTitle`, `companyName`, `hasInterview`, `firstResponseAt`, and `lastEventAt` are materialized directly on the application document at write time.
- **Time-Derived Fields**: `ageInDays` is computed on the read model from `appliedAt` and UTC `now`. Sorting by `ageInDays` maps directly to sorting by `appliedAt`.
- **Transactional Renames**: When a company name changes, postings and applications referencing that company are updated within the same transaction. When a posting title changes, referencing applications are updated within the same transaction.
- **Reconciliation**: A CLI command (`reconcile-read-models`) provides an idempotent backfill for existing documents.

## Consequences

### Positive

- Zero external legacy dependencies: eliminates `MongoDB.AspNetCore.OData 1.1.3`.
- Full .NET 10 and ASP.NET Core OData 9.5.0 compatibility.
- Guaranteed owner isolation enforced at the Application seam layer.
- Proven `IXSCAN` index utilization for sorting and case-insensitive lookups with zero in-memory sort overhead.
- True pipeline rank ordering under public OData name `$orderby=disposition`.
- Guaranteed nulls-last ordering in both sort directions for nullable score and date fields.
- RFC 7807/9457 Problem Details consistency across minimal API and OData error responses.

### Negative / Trade-offs

- Materialized fields require write-time updates across multiple collections when companies or postings are renamed.
- Dual compound indexes are required for nullable sorted fields (`has_score: -1, score: 1` and `has_score: -1, score: -1`).
- Two-stage projection requires materializing the top <=100 full read models before projecting `$select` fields. For `Notes`, database-level exclusion of `content` must be applied unless selected.
