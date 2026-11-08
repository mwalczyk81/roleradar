# Data Model: Test Quality and Reporting

No database entities. These are file shapes. Field-level contracts are in `contracts/`.

## MutationScore (per project, produced by the nightly job)

| Field | Type | Notes |
|---|---|---|
| project | string | `web-lib` (`Domain` and `Application` reserved for when .NET mutation is un-deferred, research.md D1) |
| score | number | percent, one decimal; absent when the project has zero mutants |
| killed, survived, noCoverage, timeout, ignored | integer | from the Stryker JSON report |
| runId, commit, runDate | string | provenance, UTC ISO 8601 |

Rule: zero mutants means `score` is absent (reported as unavailable), never 100.

## MutationMinimum (committed, `quality/mutation-minimums.json`)

| Field | Type | Notes |
|---|---|---|
| project | string | same names as MutationScore |
| minimum | number | whole percent; may only rise |

Transitions: created at the first recorded score rounded down; raise or keep by reviewed commit; lowering or removing is rejected by the base-ref check.

## SurvivingMutant (recorded in research)

file, line, mutation, verdict (`real-gap` with test name, or `equivalent` with reason).

## RunSummary (per CI or gate run)

Rows for test counts per lane (passed, failed, skipped), coverage per project (lines, branches where available), image sizes per image, scan counts (reports, CRITICAL, HIGH when scanned). Every cell is `value` or `unavailable`; any `unavailable` fails the step.

## MetricsRecord (one JSON line per main run)

commit, timestampUtc, testCounts per lane, coverage (overall and per project), jobDurationsSeconds per job, mutation (optional: scores plus its own runDate and commit). Unique by commit.

## AllureResult (per test, per stack)

Standard Allure result JSON plus label `lane` (`fictional` | `real-stack`). Invariant: a result with `lane = real-stack` has no attachments in the publish input.

## Relationships

MetricsRecord reads the latest MutationScore set (by workflow run) and the RunSummary numbers. The Allure report reads AllureResults and restores prior history from gh-pages.
