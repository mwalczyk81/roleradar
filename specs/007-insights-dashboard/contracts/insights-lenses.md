# Contract: Insights lenses (counts and drill-down)

A **lens** is a named filter. One TypeScript module, `web/app/lib/insights-lenses.ts`, owns the catalog (see [data-model.md](../data-model.md#lens-catalog-the-single-source-of-truth-for-counts-and-links)). It is the only place a count filter or a drill-down filter is built.

## Module API (shape, not code)

```text
type LensKey      = one of the 19 keys in the catalog (18 counted, plus the drill-only offers-without-interview)
type FunnelId     = "postings" | "applications"
type LensWindow   = "90d" | "all"
interface LensContext { window: LensWindow; asOf: string /* yyyy-MM-dd */ }

lensEntity(key)                         -> "postings" | "applications"
lensFilter(key, ctx)                    -> FilterExpression        // built with the typed odata-client builder
lensCountQuery(key, ctx)                -> string                  // "$filter=...&$top=0&$count=true"
lensListHref(key, ctx)                  -> string                  // "/postings?lens=...&window=...&asOf=..." or "/applications?..."
parseLensParams(url, entity)            -> { key, ctx } | undefined // validates; unknown/invalid -> undefined
funnelStages(funnel)                    -> LensKey[]               // fixed order, see below
```

## Funnels (each one cohort)

| Funnel | Title shown | Cohort and date basis | Stages, in order |
| --- | --- | --- | --- |
| `postings` (A) | Postings first seen in window | postings by `firstSeenAt` | `leads`, `reviewed`, `applied-to` |
| `applications` (B) | Applications applied in window | applications by `appliedAt` | `applied`, `responded`, `interviewed`, `offers-interviewed` |

Why two funnels: Leads and Reviewed are counted by first-seen date, applications by application date, so a single six-bar funnel would compare different cohorts and its bars could not nest. Within each funnel every stage is a subset of the one before it:
- A: `applied-to` implies `reviewed` implies `leads`, because `reviewed` includes any posting with an application and every reviewed posting is a posting in the window.
- B: an interview event and an offer event are both response events, so `interviewed` is a subset of `responded`, which is a subset of `applied`; the Offers bar is `offers-interviewed` so it is a subset of `interviewed`. Offers logged without an interview event are shown as a note computed as `offers` minus `offers-interviewed`, with their own drill-down: the drill-only lens `offers-without-interview` (`hasOffer eq true and hasInterview eq false`). It costs no request, and its list is exactly the difference because the two lists partition `offers`; the parity tests assert that against the live API and the e2e drill-down test follows the link.

The page never draws a bar from one funnel next to a bar from the other, and renders a one-line explanation that Funnel A's `applied-to` and Funnel B's `applied` can differ. `applied-to` is the same lens, and the same count, as the application-rate numerator.

Rules:
- `lensCountQuery` and the list loader both call `lensFilter`; neither builds a filter any other way. This is what makes the dashboard number and the list row count agree by construction.
- Count requests use `$top=0&$count=true` (research R1, ADR 0029): a count-only request, `value` is empty. The count is the response's `@odata.count`.
- Date literals use the existing `toODataDateTime` helper (UTC `...Z` literals only).
- Value strings (dispositions, application statuses) are imported from `dispositions.ts` and `application-enums.ts`. No string literal for a Domain value is typed in the lens module.
- Window clause: posting lenses on `firstSeenAt`, application lenses on `appliedAt`; `90d` is `ge <asOf-89d>T00:00:00Z and lt <asOf+1d>T00:00:00Z`; `all` keeps only the `lt <asOf+1d>` bound.
- Cutoffs: `mature` `appliedAt lt <asOf-13d>`, `ghost-eligible` `appliedAt lt <asOf-29d>`, `ghosted` `appliedAt lt <asOf-30d>`, all at 00:00Z.

## URL contract on list pages

`/postings?lens=<key>&window=<90d|all>&asOf=<yyyy-MM-dd>` and `/applications?lens=...`. The list loaders:
1. call `parseLensParams`; if it returns a value, AND its `lensFilter` with the page's own filters (`q`, `status`, ...);
2. keep paging, sorting and `limit`/`offset` clamping exactly as today;
3. show a removable chip "Insights: <lens label>, <window label>" so the owner can see and clear the lens;
4. never accept a raw `$filter` from the URL.

A lens key that belongs to the other entity (for example `ghosted` on `/postings`) is ignored.

## Snapshot: `contracts/insights-lenses.json`

A checked-in JSON file lists, for a fixed fictional `asOf` and both windows, each lens key, its entity, and the exact `$filter` string the module emits. Two tests share it:
- web equality test: `lensFilter` output serialized equals the snapshot (drift in the module fails);
- .NET integration test (Phase 2): executes each snapshot filter against the seeded API and compares `@odata.count` with an independent in-memory oracle that implements the spec's metric definitions directly over the seed objects.

This follows the existing pattern for Domain-derived contract data (`contracts/*.json`, a .NET drift test, a web equality test).

## Parity guarantee tests (spec FR-018, SC-001)

1. Unit (vitest): for every key and window, the `$filter` in `lensCountQuery` equals the `$filter` the postings/applications loader sends when given `lensListHref`'s query string.
2. Integration (xUnit, Testcontainers, seeded): for every snapshot lens, `$count` over the real API equals the oracle count, and a second request with the list's paging (`$top=25&$count=true&$orderby=...`) returns the same `@odata.count`.
3. Nesting: for each funnel and both windows, `count(stage[i]) <= count(stage[i-1])`, asserted against the oracle (so it also holds for the seed's offer-without-interview, same-day and cross-window rows) and against the live API counts.
4. Boundary: ages 13/14/29/30/31 days and window edges (first and last instant) match what the list page displays as `ageInDays`.
