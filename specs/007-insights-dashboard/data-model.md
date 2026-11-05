# Data Model: Insights Dashboard (007)

No new collections. One new stored field on postings, three on applications, one newly exposed stored field on postings. Nothing is user-writable; every field is derived and written only at the repository choke point (AGENTS.md rule), then backfilled by `reconcile-read-models`.

## Stored fields

### `postings`

| Stored field | Read-model property (OData name) | Type | Source | New? | Backfill? |
| --- | --- | --- | --- | --- | --- |
| `has_application` | `HasApplication` (`hasApplication`) | bool | exists an application for this posting | yes | yes |
| `first_seen_at` | `FirstSeenAt` (`firstSeenAt`) | DateTimeOffset | `Posting.FirstSeenAt` | no (stored today, not exposed) | no |

Writers of `has_application` (complete list, enforced by an architecture test):
1. `MongoApplicationRepository.UpsertApplicationAsync`: `$set has_application = true` on the parent posting, same session.
2. `MongoPostingRepository.UpsertPostingAsync`: computes the value before `SerializePosting`, so a replace never loses it.
3. `ReadModelReconciliationService.ReconcilePostingsAsync`.
Mapping and index files read or name the field but never write values.

Invariant: `has_application` is true if and only if an application exists for `(owner_id, _id)`. It moves false to true once. No status or event change touches it (pinned by a test).

Index: `ix_postings_owner_has_application` = `{owner_id: 1, has_application: 1, _id: 1}`; `ix_postings_owner_first_seen_at` = `{owner_id: 1, first_seen_at: 1, _id: 1}`.

### `applications`

| Stored field | Read-model property (OData name) | Type | Rule | Backfill? |
| --- | --- | --- | --- | --- |
| `has_offer` | `HasOffer` (`hasOffer`) | bool | any event of type `offer` | yes |
| `has_rejection` | `HasRejection` (`hasRejection`) | bool | any event of type `rejected` | yes |
| `rejected_before_interview` | `RejectedBeforeInterview` (`rejectedBeforeInterview`) | bool | `has_rejection` and no `interview` event dated on or before the earliest `rejected` event; false when `has_rejection` is false | yes |

Writers: `SerializeApplication` (full replace, values from events), `AppendEventAsync` (`$set` after each event), `ReconcileApplicationsAsync`. All three call the one extended `ComputeApplicationMilestones`, which returns a record `ApplicationMilestones(HasInterview, FirstResponseAt, LastEventAt, HasOffer, HasRejection, RejectedBeforeInterview)` instead of the current tuple.

Event date for ordering uses the same rule as today: the event's `event_date` (UTC midnight), falling back to `created_at`. Ties (rejected and interview on the same day) count as **not** before, so the rejection is "after".

Index: none new. Application windows use the existing `ix_applications_owner_applied_at_desc`; the flags are low-cardinality and the collection is small. If `explain` in the Phase 1 benchmark shows a collection scan above the 1 second target (SC-007 of 006), add `{owner_id, has_offer}` style indexes then, not before.

## Read-model additions (public, appear in `$metadata`)

```text
PostingReadModel:     + bool HasApplication, + DateTimeOffset FirstSeenAt
ApplicationReadModel: + bool HasOffer, + bool HasRejection, + bool RejectedBeforeInterview
```

Class maps ([ReadModelClassMaps.cs](../../src/RoleRadar.Infrastructure/Mongo/Reading/ReadModelClassMaps.cs)) add the four new element names. `contracts/odata/metadata.xml` and `docs/api/odata-queries.md` are updated in the same phase, and the metadata drift test is the gate.

Allowlists:
- `$orderby`: unchanged. None of the new properties is sortable (no UI need), so no widening.
- `$filter`: the five new properties are allowed (not added to any `DisallowedFilter` set). `ageInDays` stays blocked; lenses use `appliedAt` cutoffs.

## Derived, not stored: Insights snapshot

Computed per request, never persisted.

```text
InsightsView (web, per page load)
  window: "90d" | "all"
  asOf: yyyy-MM-dd (UTC date, pinned in every link)
  windowStart: yyyy-MM-dd | null          // asOf - 89 days, null for all-time
  counts: map<LensKey, int>               // 18 counted lenses from OData $count (a 19th lens, offers-without-interview, is drill-only)
  funnels: { postings: [leads, reviewed, applied-to], applications: [applied, responded, interviewed, offers-interviewed] }
  coverage: { postings: bool, applications: bool }   // R6 sentinels
  rates: list<Rate>                        // see below
  stats?: InsightsStats                    // Phase 3

Rate
  key, label
  numerator: LensKey, denominator: LensKey
  value: number | null                     // null when denominator is 0
  sampleFlag: "ok" | "small_sample" | "no_data"   // denominator < 10, or 0
```

`InsightsStats` is the stats endpoint response, defined in [contracts/insights-stats.md](./contracts/insights-stats.md).

## Lens catalog (the single source of truth for counts and links)

Window rules: posting lenses window on `firstSeenAt`; application lenses window on `appliedAt`. `W` below means the active window clause (omitted for all-time). Cutoffs are UTC midnight.

| Key | Entity | Filter (before window clause) | Funnel / tile use |
| --- | --- | --- | --- |
| `leads` | postings | none (all postings in window) | funnel A: Leads; rate cohort |
| `reviewed` | postings | `(disposition ne 'lead' and disposition ne 'unscored') or hasApplication eq true` | funnel A: Reviewed; application and screen-out rate denominator |
| `applied-to` | postings | reviewed filter `and hasApplication eq true` | funnel A: Applied-to; application-rate numerator (same count, one lens) |
| `screened-out` | postings | reviewed filter `and disposition eq 'screened_out'` | screen-out-rate numerator |
| `expired` | postings | `disposition eq 'closed' and hasApplication eq false` | note under Reviewed |
| `applied` | applications | none | funnel B: Applied; denominator for several rates |
| `responded` | applications | `firstResponseAt ne null` | funnel B: Responded |
| `interviewed` | applications | `hasInterview eq true` | funnel B: Interviewed; offer-rate denominator |
| `offers` | applications | `hasOffer eq true` | offers-over-applications rate numerator; note "offers without an interview" = `offers` minus `offers-interviewed` |
| `offers-interviewed` | applications | `hasOffer eq true and hasInterview eq true` | funnel B: Offers; interview-conditioned offer rate numerator |
| `offers-without-interview` | applications | `hasOffer eq true and hasInterview eq false` | drill-only: the page shows `offers` minus `offers-interviewed` as "n offers had no interview event" and links it here; never counted by its own request (the two lists partition `offers`, proven by a parity test) |
| `mature` | applications | `appliedAt lt <asOf-13d>` | interview-rate denominator |
| `mature-interviewed` | applications | mature `and hasInterview eq true` | interview-rate numerator |
| `rejected` | applications | `hasRejection eq true` | rejection-rate numerator |
| `rejected-before-interview` | applications | `hasRejection eq true and rejectedBeforeInterview eq true` | rejection split |
| `rejected-after-interview` | applications | `hasRejection eq true and rejectedBeforeInterview eq false` | rejection split |
| `ghost-eligible` | applications | `appliedAt lt <asOf-29d>` | ghost-rate denominator |
| `ghosted` | applications | `status eq 'applied' and firstResponseAt eq null and appliedAt lt <asOf-30d>` | ghost-rate numerator |
| `open` | applications | `status eq 'applied'` | Open tile |

Note: for application lenses with a window, the window clause (`appliedAt ge <start> and appliedAt lt <asOf+1d>`) is ANDed with the cutoff clause, so for example `mature` in the 90-day window is both inside the window and at least 14 days old. All string values (`'lead'`, `'screened_out'`, `'applied'`, ...) come from `dispositions.ts` and `application-enums.ts`, which are already pinned to the Domain enums by the existing snapshot tests; no new string-typed contract values are introduced.

Rates (all rendered "x of y", sample flag from the denominator):

| Rate | Numerator | Denominator |
| --- | --- | --- |
| Application rate | `applied-to` | `reviewed` |
| Screen-out rate | `screened-out` | `reviewed` |
| Interview rate (mature) | `mature-interviewed` | `mature` |
| Interview rate (all time, marked lower confidence) | `interviewed` | `applied` |
| Rejection rate | `rejected` | `applied` |
| Offer rate (of interviews) | `offers-interviewed` | `interviewed` |
| Offer rate (of applications) | `offers` | `applied` |
| Ghost rate | `ghosted` | `ghost-eligible` |

The "interview rate (all time)" tile is always the all-time-lens version regardless of the window toggle, per the spec; it links with `window=all`.

## State and validation

- No state transitions are introduced. The flags are functions of existing events.
- Query validation: `lens` must be in the catalog for that entity; `window` is `90d` or `all` (default `90d`); `asOf` must parse as a real UTC date not more than 1 day in the future (default today UTC). Anything else is ignored with the defaults, never forwarded to the API.
