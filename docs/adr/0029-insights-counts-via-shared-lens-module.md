# ADR 0029: Insights Counts via OData `$count` and One Shared Lens Module

- **Status**: Proposed
- **Date**: 2026-11-04
- **Feature**: `007-insights-dashboard`

## Context and Problem Statement

The Insights page shows counts (the two funnels' stages, rate numerators and denominators). The owner must be able to click any count and land on a list with exactly that many rows. If the count and the list build their filters in two places, they can drift, and a dashboard whose numbers cannot be verified is not trustworthy.

## Decision Drivers

- The number on the dashboard and the row count of its list must agree by construction, not by test luck.
- No raw `$filter` in page URLs (injection surface, breaks the 006 allowlists).
- Reuse the 006 OData surface, validation and owner scoping.
- Keep page load bounded.

## Considered Options

1. **One server-side stats call returning all counts.** One round trip, but every filter is re-implemented in C#, so the drill-down filter (OData) and the count (C#) are two implementations.
2. **Parallel OData `$count` requests, filters built ad hoc in each page.** Reuses OData, but count and list could diverge.
3. **Parallel OData `$count` requests, with one shared filter-definition module used for both the count and the drill-down.** (chosen)

## Decision Outcome

Chosen option 3.

- `web/app/lib/insights-lenses.ts` defines 18 named lenses (see `specs/007-insights-dashboard/data-model.md`). It is the only code that builds a count filter or a drill-down filter, using the typed `odata-client` builder.
- Count request: `$filter=<lens>&$top=1&$select=id&$count=true`. The value is `@odata.count`.
- Drill-down link: `/postings|/applications?lens=<key>&window=<90d|all>&asOf=<yyyy-MM-dd>`. List loaders resolve the lens through the same module and AND it with their own filters. Raw `$filter` is never accepted from a URL.
- `asOf` is pinned in links so a dashboard opened before midnight UTC still agrees after midnight.
- **`$top=1`, not `$top=0`**: `ExecuteQuery` always materializes the page, and `Take(0)` becomes `$limit: 0`, which MongoDB aggregation rejects; the web query builder also clamps `top` to at least 1. A first integration test (`TopZeroProbeTests`) records the real `$top=0` behavior. The decision stays `$top=1` either way, because it needs no change to the shared pipeline.
  - **Recorded result (2026-11-04, Postings and Applications):** `$top=0&$count=true` returns **HTTP 500 with an empty body**, not a 400, so OData validation lets it through and the failure comes later, consistent with MongoDB rejecting `$limit: 0` (the exact exception was not captured). `$top=1&$select=id&$count=true` returns 200 with one row and the total. The test pins this behavior.
- **Requests per page load**: 20 OData requests (18 lens counts and 2 read-model coverage checks), plus 1 stats request in Phase 3. Run with concurrency 6, a 5 second timeout per request, and one failure marks only the affected tiles "unavailable", not the page.
- **Coverage checks**: MongoDB's `{flag: false}` does not match documents missing the flag, so before `reconcile-read-models` has run, `eq false` counts would be silently low. Two cheap checks (postings with `hasApplication` set, applications with `rejectedBeforeInterview` set, each versus total) gate the flag-dependent numbers; a failed check shows "needs backfill" and the operator command instead of a wrong number.

### Consequences

- Good: agreement is structural; the parity test checks the module, not two implementations.
- Good: no new endpoint for counts, all 006 validation and owner scoping apply.
- Bad: 20 small requests per load. Mitigated by indexed counts on a single owner's data, bounded concurrency and a per-request timeout. Revisit only if the benchmark in Phase 2 misses the 2 second target (SC-008).
- Bad: the lens module is a contract. A snapshot (`contracts/insights-lenses.json`) with a .NET oracle test and a web equality test guards it.

### Confirmation

Unit test: for every lens and window, the count `$filter` string equals the list loader's `$filter` string. Integration test: for every snapshot lens, `@odata.count` over the real API equals an independent in-memory oracle over the fictional seed.
