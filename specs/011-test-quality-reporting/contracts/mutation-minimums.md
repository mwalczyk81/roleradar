# Contract: mutation minimums

File: `quality/mutation-minimums.json` (LF, committed).

```json
{
  "schemaVersion": 1,
  "minimums": {
    "web-lib": 0
  }
}
```

Only `web-lib` is listed while .NET mutation is deferred (research.md D1). `Domain` and `Application` entries are added back, by a reviewed commit, only when the revisit trigger in D1 is met. The script itself is project-agnostic and keeps its tests for several projects.

Values are whole percents. The shipped initial values are `0` (report-only) until the first recorded run; the task that records the scores then sets each to the recorded score rounded down.

## `web/scripts/mutation-ratchet.mjs`

| Mode | Invocation | Exit | Behavior |
|---|---|---|---|
| Score | `--project <name> --report <Stryker mutation.json> [--output-directory <dir>]` (also writes the table and `mutation-score.json`) | 0 pass, 1 below floor, 2 input missing or unreadable | Prints `project, score, minimum`. A project with no score (zero mutants) exits 2, never passes by default. |
| Base ref | `--base-ref origin/main` | 0 unchanged or raised, 1 any minimum lowered or project removed | Compares the working file with the base ref version. A missing base file (first introduction) passes. |

Both modes print one line per project in the failure message so the summary can quote it.
