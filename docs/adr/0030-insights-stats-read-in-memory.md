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
