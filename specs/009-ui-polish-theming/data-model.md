# Data Model: UI Polish and Theming

No entity, collection, field, index, DTO or API shape changes. The feature reads and writes no domain data, so `contracts/client/api-types.ts` and the OpenAPI snapshot are untouched and no `reconcile-read-models` run is needed.

## State the feature does touch

| State | Where | Change |
| --- | --- | --- |
| `roleradar_theme` cookie (`light` or `dark`) | Browser, web origin only | None. Same name, values, `Path=/`, `SameSite=Lax`, `Max-Age=31536000`, `Secure` on HTTPS. |
| `data-theme` attribute on `<html>` | Server-rendered HTML and client | Same values. After Phase 2 it is written by one module. |
| Identity theme | `data-theme` set by `theme.js` from `prefers-color-scheme` | New, Identity origin only, not persisted, no cookie. |
| Inline-style ceiling | `web/tests/architecture/inline-style-ceiling.json` | New test data: per-file integers, initial total 203, only ever lowered. |
| Baseline screenshots | `%TEMP%\roleradar-visual\<label>\` (or `ROLERADAR_VISUAL_DIR`) | New, outside the repository, never committed. |
| Generated Identity assets | `src/RoleRadar.Identity/wwwroot/ds/` | New, build output, git-ignored. |

## Display data used by the search-run chart

The Insights loader already returns `stats.runs.rows` as `{ date, newPostings }[]` and `stats.runs.values`. The chart maps them to `ColumnChartItem` (`label` short date, `fullLabel` full date, `value` new postings). The "latest run" text is the last row; the point count is `rows.length`. Both are derived in the component from existing loader data; no loader change.
