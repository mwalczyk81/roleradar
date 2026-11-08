# ADR 0037: Nightly Mutation Testing for the Web Library, .NET Deferred

- **Status**: Accepted
- **Date**: 2026-11-07
- **Feature**: `011-test-quality-reporting`

## Context and Problem Statement

The repository claims test-first discipline and a coverage gate. Coverage says which lines ran, not whether the tests would notice a bug. Mutation testing measures that: it changes the code one small way at a time and counts how many changes the tests catch. A full run executes the tests once per mutant, so it is slow and does not belong on pull requests.

## Decision Outcome

- **Scope: the web pure-logic library only.** StrykerJS with the Vitest runner, `mutate` limited to the pure modules in `web/app/lib` that have direct unit tests (the enum modules, `dispositions`, `format-date`, `insights-*`, `markdown-limits`, `markdown-url`, `nav`, `odata-client`, `theme`). Server modules (cookies, sessions, OIDC, the network, the request limiter), React hooks, routes and components are out: they are I/O or rendering, where mutants mostly time out or cannot be killed. Both tools are pinned at exact versions in `web/package.json` (`@stryker-mutator/core` and `@stryker-mutator/vitest-runner`, 10.1.1).
- **.NET is deferred (Domain and Application have no mutation score for now).** Stryker.NET 5.0.0 cannot produce a trustworthy score with xUnit v3 on .NET 10. Evidence, all recorded in `specs/011-test-quality-reporting/research.md` D1:
  - stryker-net #3117 (open): Stryker.NET does not handle xUnit v3. The maintainer states there is no plan to support xUnit v3 through VSTest and that the only route is the Microsoft Testing Platform (MTP).
  - stryker-net #3920 (open): 0 kills on .NET 10 with xUnit v3 on VSTest, on 5.0.0 and 4.16.0.
  - The MTP runner has its own open xUnit v3 problems: #3915, #3960, #3871, #3563. MTP support is tracked in #3094.
  - A local probe on Stryker.NET 5.0.0 (Unit project, VSTest): coverage capture failed, and 0 of 82 mutants were killed on code the tests demonstrably cover, with default settings and again with `coverage-analysis: off`. A mutation applied by hand to the same code is killed by 5 of 6 tests, so the tests are fine and the tool is not engaging the mutants. One 100-line file took 17 minutes 22 seconds.
  - The owner's own whole-solution run on Windows logged the capture failure for each of the 7 projects, created 17,564 mutants and projected about 11 days before it was aborted (an observation, not reproduced by the agent; see research.md D1).
- **No Microsoft Testing Platform probe, no runner change and no csproj change.** Moving the test projects to MTP to rescue the tool would change the runner for a preview feature whose xUnit v3 support has open problems of its own.
- **Revisit trigger**: a stable Stryker release with working MTP support (#3094) and xUnit v3 support.
- **Interim substitute for Domain and Application**: the FsCheck property tests of this feature (CSV reader, import normalization and dedupe) look for weak tests by generating the inputs. They do not produce a score and do not replace mutation testing.
- **Infrastructure stays out of scope for any mutation run.** Its meaningful tests are the Integration lane, which needs Docker and a MongoDB replica set. A run executes the suite once per mutant, which would multiply container start-up and election time, turn timeouts into false kills, and cannot run on a plain job. Api, Cli, Mcp.Stdio and Identity are thin hosts and are out for the same reason.
- **Ratchet.** `quality/mutation-minimums.json` holds one floor per project (now `web-lib` only). It starts at 0 (report-only). After the first recorded run the floor is set to that score rounded down to a whole percent, by a deliberate reviewed commit. The nightly job fails below the floor (`web/scripts/mutation-ratchet.mjs --project ... --report ...`, which also computes the score and writes the job summary table), and a pull request check fails any change that lowers or removes a floor against the base branch (`--base-ref`). Automation never edits the file.
- **Workflow shape.** `.github/workflows/nightly-quality.yml` has exactly three triggers: `schedule` (daily 02:00 UTC), `workflow_dispatch`, and a `pull_request` trigger path-filtered to the workflow file itself and guarded to pull requests from this repository. The pull request run is a validation run: it publishes nothing and receives no secret, so the first scores can be read before merge. Mutation never runs on pushes or on any other pull request. A Vitest test (`web/tests/tooling/workflow-triggers.test.ts`) fails if the trigger block, the same-repository guards, the write permissions or the secret references drift. Permissions are `{}` at the workflow level with `contents: read` per job, actions are pinned by SHA, and checkout does not persist credentials.
- **Dashboard.** When the secret `STRYKER_DASHBOARD_API_KEY` exists, a single step uploads the finished JSON report to the Stryker Dashboard with an HTTP PUT. That step runs no repository code, receives the key through its own `env` only, and never runs on a pull request. When the secret is absent the job still runs, uploads the HTML and JSON reports as an artifact (14 days) and the job summary says the dashboard publish was skipped. A rejected key is reported and does not fail the score check. `web/scripts/mutation-ratchet.mjs` never reads the key.
- **Budget.** Each job's `timeout-minutes` is its budget (60 minutes for the web job to start, from local measurements of about 35 minutes; replaced by the measured value after the validation run). If a job approaches it, scope is cut in this order: narrow `mutate`, lower the mutation level, sample mutants, shard by file group. The failure text points to research.md D4.
- **A launcher keeps the contract tests working.** Stryker runs the tests in a sandbox copy of `web/`, where tests that read `../contracts/*.json` cannot find the repository's `contracts/` folder. `web/scripts/stryker-run.mjs` (run with `pnpm --filter web run mutation`) copies `contracts/` next to the sandbox first, so no test is excluded and none is edited.

### Consequences

- Good: the web logic gets a measured test-strength number from pipeline output, with a floor that can only rise.
- Good: the deferral is explicit, evidenced and has a concrete trigger, instead of a score that would look real and mean nothing.
- Bad: Domain and Application, the largest and most important code, have no mutation score. The property tests are weaker evidence than a score.
- Bad: the nightly run adds runner minutes (bounded by the job budget).
- Bad: the launcher is one more script to maintain; it copies the contracts folder on every run.
