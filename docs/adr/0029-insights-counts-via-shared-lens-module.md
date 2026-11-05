# ADR 0029: Insights Counts via OData `$count` and One Shared Lens Module

- **Status**: Accepted
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

- `web/app/lib/insights-lenses.ts` defines 18 counted lenses and one drill-only lens (see `specs/007-insights-dashboard/data-model.md`). It is the only code that builds a count filter or a drill-down filter, using the typed `odata-client` builder.
- Count request: `$filter=<lens>&$top=0&$count=true`. The value is `@odata.count`; `value` is empty, so nothing but the count crosses the wire.
- Drill-down link: `/postings|/applications?lens=<key>&window=<90d|all>&asOf=<yyyy-MM-dd>`. List loaders resolve the lens through the same module and AND it with their own filters. Raw `$filter` is never accepted from a URL.
- `asOf` is pinned in links so a dashboard opened before midnight UTC still agrees after midnight.
- **`$top=0` is a valid count-only request (006 defect fixed by this feature).** Before 007, `$top=0` answered an empty-bodied HTTP 500 on all six entity sets: validation accepted the value, then `ExecuteQuery` applied `Take(0)` and the MongoDB LINQ provider threw `System.ArgumentOutOfRangeException: Value is not greater than 0: 0. (Parameter 'limit')` (captured in a failing run on 2026-11-05). A 500 on client input is a bug, so the shared `ODataQueryPipeline.ExecuteQuery` now skips the page query when `$top=0` and answers `{ "value": [] }`, plus `"@odata.count": n` when `$count=true`. `MaxTop` stays 100 and every other validation rule is unchanged; `$skip`, `$orderby` and `$select` are accepted and have no effect on an empty page. `TopZeroProbeTests` covers all six sets, with `$filter`, with `$select`, without `$count`, and the unchanged `$top=101`, `$top=-1` and `$top=abc` rejections. The typed web query builder accepts `top: 0` only through a count-only option; list pages keep the 1..100 clamp.
- **Requests per page load**: 22 OData count requests in the 90 day view and 20 in the all-time view (18 lens counts, 2 read-model coverage checks, and in the 90 day view the 2 all-time interview counts that the interview-rate tile needs), plus 1 stats request (23 and 21 in total). The stats request starts with the first wave. Run with concurrency 6, a 5 second timeout per request, and one failure marks only the affected tiles "unavailable", not the page.
- **Coverage checks**: MongoDB's `{flag: false}` does not match documents missing the flag, so before `reconcile-read-models` has run, `eq false` counts would be silently low. Two cheap checks (postings with `hasApplication` set, applications with `rejectedBeforeInterview` set, each versus total) gate the flag-dependent numbers; a failed check shows "needs backfill" and the operator command instead of a wrong number.

### Consequences

- Good: agreement is structural; the parity test checks the module, not two implementations.
- Good: no new endpoint for counts, all 006 validation and owner scoping apply.
- Bad: 22 small requests per load (90 day view). Mitigated by indexed counts on a single owner's data, bounded concurrency and a per-request timeout. Revisit only if the SC-008 measurement (tasks T092) misses the 2 second target.
- Bad: the lens module is a contract. A snapshot (`contracts/insights-lenses.json`) with a .NET oracle test and a web equality test guards it.

### Confirmation

Unit test: for every lens and window, the count `$filter` string equals the list loader's `$filter` string. Integration test: for every snapshot lens, `@odata.count` over the real API equals an independent in-memory oracle over the fictional seed.
