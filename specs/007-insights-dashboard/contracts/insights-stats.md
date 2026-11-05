# Contract: Insights stats read

One read-only endpoint for what OData cannot compute. Added to the committed OpenAPI snapshot (`contracts/openapi/roleradar-api.json`) and the generated client types (`contracts/client/api-types.ts`) per ADR 0019; field names below are the OpenAPI names and the web must take them from the generated types.

## Request

```text
GET /api/v1/insights/stats?window={window}&asOf={asOf}
Authorization: bearer, ReadPolicy (same policy as the other GET endpoints)
```

| Parameter | Type | Rules |
| --- | --- | --- |
| `window` | enum `InsightsWindow`: `last90Days` \| `allTime` | Required. Declared as a string enum in the schema, so the typed client sees a literal union. |
| `asOf` | string, `format: date` (`yyyy-MM-dd`) | Required. UTC date. More than 1 day in the future is a 400. |

Only GET is mapped. Owner comes from `IOwnerIdProvider`, never from the request.

Window arithmetic (identical to the web lens module):
- `last90Days`: `windowStart = asOf - 89 days`; included instants are `[windowStart 00:00Z, asOf + 1 day 00:00Z)`.
- `allTime`: no lower bound; upper bound still `asOf + 1 day` so the response agrees with the pinned dashboard.

## Response 200

```text
InsightsStatsDto {
  asOf: string (date)
  window: InsightsWindow
  windowStart: string (date) | null
  responseTime: {
    medianDays: number | null        // median of whole UTC days, applied date to first response date; null when sampleSize = 0
    sampleSize: integer              // applications in the window that have a first response
  }
  weeklyApplications: [              // oldest first, Monday-start UTC weeks overlapping the window, zero-filled
    { weekStart: string (date), count: integer }
  ]
  searchRunYield: {
    runs: [                          // oldest first, at most the latest 120 runs in the window
      { startedAt: string (date-time), newPostings: integer }
    ]
    truncated: boolean               // true when more than 120 runs fell in the window
  }
}
```

Definitions:
- Response time uses `ApplicationReadModel.FirstResponseAt`, which already counts only interview, offer, offer accepted, offer declined and rejected events. Days = whole UTC calendar days between the applied date and the first-response date, never negative (a response dated before the applied date is clamped to 0 and counted). Median of an even count is the mean of the two middle values (so a half day is possible); odd count is the middle value.
- Applications are included when `appliedAt` is inside the window, the same rule as the `applied` lens, so `sampleSize` for responses equals the `responded` lens count for the same window. A parity test asserts this.
- Weekly buckets count applications by `appliedAt` in the window. Weeks that overlap the window edge count only in-window applications.

## Errors

| Status | When | Body |
| --- | --- | --- |
| 400 | missing or invalid `window`/`asOf`, or `asOf` too far in the future | RFC 7807 problem details (existing `ErrorEnvelope`) |
| 401 / 403 | as other reads | as other reads |
| 405 | any non-GET | `Allow: GET` |
| 422-style typed failure | more than 50,000 projected rows from either source | `Result` failure mapped by the existing envelope mapping; message says the volume limit was reached |

## Non-goals

No write, no filter parameters beyond the window, no per-company breakdown, no export.
