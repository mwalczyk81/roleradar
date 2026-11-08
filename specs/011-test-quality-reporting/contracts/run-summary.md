# Contract: run summary tables

Produced in CI by `web/scripts/metrics-record.mjs summary`, written to `GITHUB_STEP_SUMMARY` and to stdout. The CI summary has two tables (tests per lane, coverage); ReportGenerator's own Markdown summary is appended by its step. Image sizes and scan counts are not in the CI summary (nothing downstream uses them); the local review gate prints them in its console lines, rendered inline from the object it also writes to `gate-summary.json`.

| Table | Columns | Source |
|---|---|---|
| Tests per lane | lane, passed, failed, skipped, total | TRX `Counters` per lane (CI); gate's per-lane counts (gate) |
| Coverage | project, line %, branch % | ReportGenerator `Summary.json` (`JsonSummary`): `summary` and `coverage.assemblies[]` |
| Images | image, size | `docker image inspect` values the gate already computes |
| Scans | reports, CRITICAL, HIGH (or `not scanned`), scan test passed/skipped | Trivy JSON reports and scan-test counts |

Rules:

- Each cell is a parsed value or the literal `unavailable`. Any `unavailable` makes the script exit non-zero with the name of the missing input.
- No cell is filled from a default, a previous run or this document.
- Text from reports is escaped before it is written into Markdown (untrusted text).
