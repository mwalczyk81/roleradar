# Implementation Plan: Test Quality and Reporting

**Branch**: `011-test-quality-reporting` | **Date**: 2026-11-07 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/011-test-quality-reporting/spec.md`

## Summary

Produce evidence of test quality from pipeline output: nightly mutation testing (StrykerJS for `web/app/lib`; Stryker.NET for Domain and Application is deferred, see research.md D1) with a committed, only-rising minimum; run summaries, coverage HTML and badge, a metrics history and trends page on gh-pages; one combined Allure report (xUnit, Vitest, Playwright) with history, gated by an adapter-verification stop point; FsCheck property tests for the CSV reader and import normalization and dedupe; and PSScriptAnalyzer in the review gate. Work is scripts, workflows and tests. No production code changes are planned unless a property finds a defect.

Pre-merge validation: the nightly workflow also runs on same-repo pull requests that change it (no publish, no secrets), so first mutation scores and the Allure seeded-failure check are read from the pull request before merge. Allure is one report per publishing run, assembled by a final always-running job from existing jobs' results.

Order of dependency: shared PowerShell parsers and the ratchet check first (testable without CI), then the nightly workflow, then summaries and gh-pages publishing, then Allure (after the stop point). Property tests and the gate lint are independent slices.

## Technical Context

**Language/Version**: .NET 10 (C#), PowerShell 7 (scripts), Node 22 + TypeScript (web), GitHub Actions YAML

**Primary Dependencies (exact-pinned; existing unless marked new)**: ReportGenerator 5.5.12 (tools manifest, existing); FsCheck 3.6.0 (`Directory.Packages.props`, existing and already referenced by the Unit project); xunit.v3 4.0.2 (existing; not changed); Vitest 5.0.5 and Playwright 1.64.1 (existing). New, pinned at implementation from verified registry output and never from memory: `@stryker-mutator/core` and `@stryker-mutator/vitest-runner` (web devDependencies), Allure adapters and report generator (decided after the stop point), PSScriptAnalyzer module version (pinned in the shared lint script).

**Storage**: Files only. The gh-pages branch holds `metrics/metrics.jsonl`, the trends page, badge SVGs and the Allure site. The repo holds `quality/mutation-minimums.json`. No database access.

**Testing**: xUnit (Unit) for property tests and for script tests that run `pwsh` against fixture inputs (each with a fixture temp dir and a process timeout, because the Unit project also runs on the macOS and Windows jobs); Vitest for the web-side sanitizer, trends-page logic and the nightly workflow trigger-shape check (parses the workflow YAML; parser pinned, T003 decides which). No Pester and no new test project.

**Target Platform**: GitHub-hosted runners (ubuntu for mutation and Allure, the existing windows job for script lint) and the developer's Windows machine for the gate.

**Project Type**: Existing multi-project .NET solution plus `web/`. This feature adds scripts, workflows and tests only.

**Performance Goals**: Unit lane runtime unchanged apart from bounded property tests (FR-037). Nightly mutation fits per-job budgets set from the first measured run (initial ceiling 60 min web, from local measurements, recorded and adjusted in research).

**Constraints**: Public repo, fictional data only; least-privilege permissions; actions pinned by SHA; zizmor and `verify-action-pins.ps1` clean; LF endings; no commits or pushes by the implementer; secrets never echoed; PR and fork runs never publish.

**Scale/Scope**: no mutated .NET projects (deferred), about 15 to 25 web lib modules, 3 test stacks in one report, one append-only metrics file.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I Privacy first | Pass with controls. Public Pages and Allure are new publication surfaces. Controls: only fictional-data lanes publish attachments; real-stack results publish pass/fail and timings only; a sanitizer plus a failing-first privacy test (FR-028, FR-029); gitleaks and the denylist guard still cover new files. |
| II Test-first | Pass. Each script and workflow behavior has a failing-first test or a shown-red scratch check; tasks order tests before implementation. Property tests are shown red against seeded bugs. |
| III Layering | Pass. No production layering change. Property tests live in Unit. |
| IV Named seams | Pass. No new abstractions in `src`. |
| V Untrusted text | Pass. Trends page and run summaries treat values as untrusted text (text nodes, no innerHTML). |
| VI stdout hygiene | Pass. Not touched. |
| VII Durable data | Pass. No destructive scripts. gh-pages append is retried, never force-pushed. |
| VIII ADRs; pin everything | Pass with action. Two ADRs (mutation; reporting stack). Every new tool, package and action pinned (FR-042, FR-031). |
| IX .NET 10, UTC, owner_id | Pass. UTC timestamps in metrics records; no new collections. |

No violations; Complexity Tracking is empty.

Note on a standing rule: a workflow pushing to the gh-pages branch with the repo token is an automated commit to a publishing branch only, not to main or source, which is what "never commit or push" protects. The ratchet file is never changed by automation (spec clarification).

## Project Structure

### Documentation (this feature)

```text
specs/011-test-quality-reporting/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── mutation-minimums.md
│   ├── metrics-record.md
│   ├── run-summary.md
│   └── pages-layout-and-workflows.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
quality/
└── mutation-minimums.json                   # committed floors (web-lib only), may only rise
# No Stryker.NET tool entry and no stryker-config.json: .NET mutation is deferred (research.md D1)
web/
├── stryker.config.mjs                       # StrykerJS, vitest runner, mutate subset of web/app/lib
├── package.json                             # + pinned @stryker-mutator/* devDependencies
├── playwright.config.ts                     # + screenshot only-on-failure; trace retain-on-failure (was on-first-retry)
├── scripts/allure-sanitize.mjs              # strips attachments from real-stack results (Story 2b)
├── scripts/mutation-ratchet.mjs             # Stryker score, table, floor check, floors never lowered vs base ref
├── scripts/metrics-record.mjs               # run summary tables and the one-line metrics record (no network)
└── scripts/check-combined-report.mjs        # fails when a .NET project or stack is missing from the combined report
scripts/
├── review-gate.ps1                          # + PSScriptAnalyzer step; emits structured summary
├── psscriptanalyzer-settings.psd1           # single shared settings (Severity Warning,Error)
└── (no other scripts: the run summary, metrics record, mutation ratchet and combined-report check are web/scripts/*.mjs; publishing uses peaceiris/actions-gh-pages)
.github/workflows/
├── nightly-quality.yml                      # NEW: triggers exactly schedule, workflow_dispatch, same-repo path-filtered pull_request (validation, no publish, no secrets); mutation (web only), stack jobs, e2e repeat, final always-running Allure job
└── ci.yml                                   # + summary step, coverage HTML/badge, Playwright failure artifacts, metrics job (main only)
tests/RoleRadar.Tests.Unit/
├── Csv/CsvReaderPropertyTests.cs            # round trip, never throws
├── Tracking/ImportNormalizationPropertyTests.cs   # idempotent normalization, dedupe tier order stability
├── Tracking/ImportRegressionTests.cs        # plain regression tests for found counterexamples
└── Scripts/                                 # only the portability test remains; web/tests/unit holds the .mjs tests, trigger-shape, sanitizer privacy and trends tests
docs/adr/                                    # two ADRs (numbers assigned when written)
pages/metrics/index.html                     # static trends page source, published to gh-pages by peaceiris/actions-gh-pages
```

**Structure Decision**: scripts and workflows plus tests in the existing Unit project (AGENTS.md: no new test projects). Property tests extend the existing FsCheck usage (`tests/RoleRadar.Tests.Unit/Tracking/NormalizerPropertyAndUnitTests.cs` already exists).

## Phase 1 re-check (post-design)

Design adds no `src` dependencies and no test projects. The new external surfaces (Pages, Dashboard, Allure site) are covered by the privacy controls above and by the contracts. Gates still pass.

## Complexity Tracking

None.
