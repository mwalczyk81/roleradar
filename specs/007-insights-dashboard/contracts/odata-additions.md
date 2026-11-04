# Contract: OData additions (006 surface)

Additions to the `/odata/Postings` and `/odata/Applications` entity types. Everything else in 006 is unchanged.

## Postings

| Property | Type | `$filter` | `$orderby` | `$select` | Notes |
| --- | --- | --- | --- | --- | --- |
| `hasApplication` | `Edm.Boolean` | yes (`eq`, `ne`) | no | yes | The "postings I applied to" filter. Stored, derived, backfilled. |
| `firstSeenAt` | `Edm.DateTimeOffset` | yes (`ge`, `lt`, ...) | no | yes | Stored today, newly exposed. |

## Applications

| Property | Type | `$filter` | `$orderby` | `$select` | Notes |
| --- | --- | --- | --- | --- | --- |
| `hasOffer` | `Edm.Boolean` | yes | no | yes | Any `offer` event. |
| `hasRejection` | `Edm.Boolean` | yes | no | yes | Any `rejected` event. |
| `rejectedBeforeInterview` | `Edm.Boolean` | yes | no | yes | See data-model.md. False when there is no rejection. |

## Filters the drill-downs need, and how each maps to 006

| Needed filter | Expressible today? | Mapping | Tests |
| --- | --- | --- | --- |
| Offers | no | `hasOffer eq true` (new property) | applications OData test: seeded offer/no-offer rows |
| Responded | yes | `firstResponseAt ne null` | applications OData test (add explicit case; 006 covers sorting only) |
| Interviewed | yes | `hasInterview eq true` | existing, plus lens parity |
| Rejected, before, after | no | `hasRejection`, `rejectedBeforeInterview` | seeded: reject with no interview, reject after interview, reject same day as interview |
| Mature cohort | yes, via `appliedAt` | `appliedAt lt <cutoff>`; `ageInDays` stays blocked from `$filter` (AGENTS.md: derived fields) | boundary test at 13/14 days compared with displayed `ageInDays` |
| Ghosted | yes, via existing fields | `status eq 'applied' and firstResponseAt eq null and appliedAt lt <cutoff>` | boundary at 29/30/31 days |
| Ghost-eligible | yes | `appliedAt lt <cutoff>` | boundary |
| Open | yes | `status eq 'applied'` | seeded applications covering every terminal event type; the test asserts `open` equals "no terminal event" for each |
| Has application (postings) | no | `hasApplication eq true` | postings OData test; also asserts postings list UI filter |
| Posting window | no | `firstSeenAt ge ... and firstSeenAt lt ...` | postings OData test, edges |

## Guardrails that stay as they are

- String `$filter` terms must still be `tolower()` over lowercase literals. The lenses use only `eq` on enum-like strings, which is allowed today (the rule applies to `contains`, `startswith`, `endswith`).
- `MaxTop = 100`, `MaxNodeCount = 100`, filter depth 10. The deepest lens (`applied-to`, windowed) stays well under all three; a unit test asserts every lens filter passes `ValidateFilterExpression` and the node limits.
- `$top=0` is a valid count-only request on every set (fixed in this feature, research R1, ADR 0029): empty `value`, `@odata.count` when `$count=true`. `MaxTop` stays 100.
- Heavy or private properties (`content`, `ruleText`) stay blocked; none of the new properties is heavy or private.

## Files that change with this surface

- `src/RoleRadar.Application/Reading/Models/PostingReadModel.cs`, `ApplicationReadModel.cs`
- `src/RoleRadar.Infrastructure/Mongo/Reading/ReadModelClassMaps.cs`
- `contracts/odata/metadata.xml` (drift test `ODataMetadataDriftTests`)
- `docs/api/odata-queries.md` (examples are executed by `ODataDocsExamplesTests`, so add one example per new property)
- `docs/operations/reconcile-read-models.md`
