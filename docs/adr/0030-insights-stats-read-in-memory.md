# ADR 0030: Insights Stats Read Computed In Memory Through `IOwnerReadSeam`

- **Status**: Proposed
- **Date**: 2026-11-04
- **Feature**: `007-insights-dashboard`

## Context and Problem Statement

Median response time, applications per week and search-run yield need grouping and a median. OData in this API cannot do that (no `$apply`; the MongoDB C# driver cannot translate it). Constitution Principle IV allows only named seams, and ADR 0028 forbids the API from touching MongoDB directly.

## Decision Drivers

- Read-only, owner-scoped, no direct collection access.
- No new generic abstraction (Principle IV).
- Keep the calculation unit-testable without a database.
- Volume is one owner's job search.

## Considered Options

1. **A MongoDB aggregation pipeline in Infrastructure with a new seam method.** Efficient at scale but adds a seam method, hand-written BSON, and a second place owner scoping must be right.
2. **Stored pre-aggregates updated at write time.** Fast reads, but a new write path and drift risk; out of scope (read-only feature).
3. **A query handler that projects two date columns through the existing `IOwnerReadSeam` and computes in memory.** (chosen)

## Decision Outcome

Chosen option 3.

- `GET /api/v1/insights/stats?window=&asOf=` is a thin endpoint over an Application-layer `IQueryHandler` (Principle III). It reads `IOwnerReadSeam.QueryApplications(owner)` projected to `(AppliedAt, FirstResponseAt)` and `QuerySearchRuns(owner)` projected to `(RunDate, NewPostings)`. No new seam method.
- A pure static calculator does median (whole UTC days, mean of the middle two for an even count), Monday-start UTC week buckets (zero-filled) and series shaping.
- `FirstResponseAt` already follows the agreed response-event rule at the repository choke point, so the stats read cannot disagree with the `responded` lens: a test asserts `responseTime.sampleSize` equals that lens count for the same window.
- **Volume assumption**: one owner, hundreds of applications and low thousands of search runs. Hard cap of 50,000 projected rows per source; above it the handler returns a typed `Result` failure rather than truncating. Two date columns at the cap is about 1 to 2 MB transient. Search-run yield returns at most the latest 120 runs with a `truncated` flag.
- It is a query handler, not a command: no transaction and no audit entry (no write). It is registered like the existing tracker query handlers.

### Consequences

- Good: no new seam, no BSON, owner scoping stays in the one existing place.
- Good: calculator logic is covered by fast unit tests with hand-computed expectations.
- Bad: O(n) memory per request. Acceptable at the stated volume; the cap makes the failure explicit. If the volume assumption breaks, supersede this ADR with a stored pre-aggregate.

### Confirmation

Unit tests on the calculator (even/odd medians, empty, single, zero-day, clamped negative, week boundaries on Sunday/Monday UTC). Integration test over the seeded API. Architecture test: the handler depends on `IOwnerReadSeam` and not on any MongoDB type (Application already has no MongoDB reference).

## Measured (T078, 2026-11-04)

Measured on the fictional integration seed (Testcontainers MongoDB, owner A, all-time window, as-of 2026-09-30) and with a synthetic in-memory load. One machine, one run each; treat them as order of magnitude, not a benchmark.

| What | Result |
| --- | --- |
| Rows projected on the seed, all-time | 27 applications (two date columns each) and 7 search runs (two columns each) |
| Rows projected on the seed, last 90 days | 25 applications and 6 search runs |
| Endpoint round trip on the seed (12 calls) | first call 278 ms (cold: connection, query translation, JIT), median of the next 11 calls 5.3 ms |
| Calculation alone at the cap (50,000 applications and 50,000 runs, all-time) | 23.6 ms, giving 33,333 responses, 353 weekly buckets and 120 runs |

The window is applied in the database query, so the 50,000-row cap bounds what is actually read, not only what is kept. The calculation is not the cost at any volume this ADR assumes; reading the rows would be.

## As built

- The handler is `GetInsightsStatsQueryHandler` over `GetInsightsStatsQuery`, registered with `AddQueryHandler` (logging decorator only, no transaction or audit). It depends on `IOwnerReadSeam` and `IUtcClock` only.
- Over the cap, and an as-of date more than one day in the future, return `Result` failures with `ErrorCode.ValidationError`. The existing envelope mapping turns that into HTTP 400, not a distinct 422.
- The endpoint binds `window` and `asOf` as plain strings and parses them exactly (`last90Days` or `allTime`, `yyyy-MM-dd`), so a bad value gets the standard `ErrorEnvelope`. A transformer declares the required string enum and `format: date` in the OpenAPI document and the generated client types.
- All-time weekly buckets run from the week of the earliest application to the week of the as-of date, and are empty when there are no applications.
- `responseTime.sampleSize` equals the `responded` lens count for the same window and as-of date; an integration test asserts it against the real API for both windows.
