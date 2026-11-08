# Feature Specification: Test Quality and Reporting

**Feature Branch**: `011-test-quality-reporting`

**Created**: 2026-11-07

**Status**: Draft

**Input**: User description: "Feature 011, test quality and reporting. The repo claims a test-first discipline, a 92% coverage gate and 1595 tests. Show how good those tests are, with numbers produced by the pipeline and not typed by hand: nightly mutation testing, reporting from pipeline numbers, property-based tests for the CSV reader and tracker import, and PSScriptAnalyzer in the review gate."

## Context

Today the repo states test numbers (test count, coverage gate) in prose. Counts and coverage say how much code the tests execute, not whether they would notice a bug. Nothing publishes the numbers the pipeline already computes, and a PowerShell lint rule that fails in CI cannot fail locally.

This feature produces evidence instead of claims:

1. A mutation score, measured nightly, for the pure-logic code.
2. A single report per run, built from the pipeline's own outputs, plus a history of those numbers.
3. Property-based tests that probe the CSV reader and import normalization and dedupe with generated input.
4. A local gate step that matches the CI script lint.

Every published number MUST come from a command or workflow output. No number is typed by hand into a document, summary, badge or data file.

## Clarifications

### Session 2026-11-07

Answered by the recommended default because the owner asked for the full chain before review. Confirm or overrule at review.

- Q: How is a committed mutation minimum raised? → A: By a deliberate change in a normal reviewed commit, using the score from a nightly run. No workflow commits or opens pull requests on its own (repo rule: never commit or push automatically).
- Confirmed by the owner (2026-11-07): the two defaults above stand.
- Decision (owner, 2026-11-07): a no-publish validation run of the nightly workflow triggers on same-repo pull requests that change the workflow file, so first scores and the Allure seeded-failure check can be read from the pull request before merge.
- Decision (owner, 2026-11-07): one Allure report per publishing run (CI on main and the nightly), assembled by a final always-running job from results the existing jobs already produce; the nightly adds the end-to-end repeat run; the CI schedule is not moved. The adapter question is settled at the Story 2b stop point.
- Decision (owner, 2026-11-07): the .NET Allure path is the native JUnit XML from each xUnit v3 test executable read by Allure 3 (no csproj change, no Microsoft Testing Platform migration); `Allure.Xunit.v3` is revisited only when it has a stable release; a TRX converter is the fallback only if the JUnit output loses something the report needs.
- Decision (owner, 2026-11-07): Story 1 is reduced to the web pure-logic library (StrykerJS with the Vitest runner on `web/app/lib`). .NET mutation testing (Domain and Application) is deferred because Stryker.NET cannot produce a trustworthy score on this stack (evidence in research.md D1 and the ADR). No Microsoft Testing Platform probe, no runner change and no csproj change. Revisit trigger: a stable Stryker release with working Microsoft Testing Platform support (stryker-net #3094) and xUnit v3 support. Until then Domain and Application have no mutation score, and the Story 3 property tests are the interim substitute for finding weak tests there. The ratchet file keeps only `web-lib`.
- Q: What mutation score goes on the metrics line when the main run is not a nightly? → A: The most recent nightly result, recorded with its own run date and commit. If none exists, the field is absent. It is never shown without that provenance.

Addendum (owner, 2026-11-07): Allure is IN scope as Story 2b (P1) with an adapter-verification stop point before building; Playwright failure artifacts and their retention (7 days real-stack, 14 days fictional) are part of Story 2. This replaces an earlier note that recommended skipping Allure; research records the adopt decision and the verification result instead.

## Scope

**In scope**: the five user stories below (1, 2, 2b, 3, 4).

**Out of scope** (do not touch in this feature):

- The Trivy HIGH findings and their fixes.
- The README rewrite and badge placement. This feature only provides the badge URLs (SHOWPIECE 2 places them).
- TypeScript 7 and cookie 2 major upgrades (the dependabot ignores stay).
- Tooling consolidation.
- Hosting changes. Publishing uses GitHub Actions artifacts and, where enabled, GitHub Pages only.
- The mocked web stack (MSW and Bogus). Allure is no longer out of scope (see User Story 2b).
- Mutation testing of Domain and Application (deferred, see the 2026-11-07 owner decision above; revisit trigger recorded in research.md D1).
- Mutation testing of Infrastructure, API controllers, hosts, web routes and web components. Infrastructure needs Docker and MongoDB to run its tests, which makes a mutation run slow, nondeterministic and impossible on a plain runner job; research records this reasoning.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Nightly mutation testing with a ratchet (Priority: P1)

The owner wants to know whether the unit and contract tests would catch real bugs in the pure-logic code. A nightly job mutates the web pure-logic library (`web/app/lib`), runs only the fast Vitest tests against each mutant, and reports a mutation score. The score is published, and once a baseline is recorded, a committed minimum that can only rise prevents silent regression. Surviving mutants that expose real gaps become tests in this same feature. Domain and Application mutation is deferred (see the clarification above); the property tests of Story 3 are the interim check on those projects.

**Why this priority**: it is the only measure of test strength; the rest of the feature reports numbers, this one makes them meaningful.

**Independent Test**: open a pull request that changes the nightly workflow (or dispatch the workflow after merge). The run produces a web mutation report and a job summary with its score, without the secret configured.

**Acceptance Scenarios**:

1. **Given** the nightly workflow, **When** it is dispatched manually or its schedule fires, **Then** it runs the web mutation job, which uploads its HTML report as an artifact, and the job summary lists the score, killed, survived, no-coverage and timeout counts.
2. **Given** a pull request or push to any branch, **When** CI runs, **Then** no mutation job runs, with one exception: a pull request from this repository (not a fork) that changes the nightly workflow file itself triggers a validation run of that workflow.
2a. **Given** the validation run on such a pull request, **When** it executes, **Then** it runs the same mutation jobs and uploads the same reports and summary as the nightly run, but never publishes to the dashboard or Pages, never receives any secret, and a fork pull request never triggers it.
3. **Given** the dashboard API key secret is absent, **When** the job runs, **Then** it still completes, uploads the HTML report as an artifact, and the job summary says the dashboard publish was skipped and why.
4. **Given** the dashboard API key secret exists, **When** the job runs, **Then** the report and badge are published to the Stryker Dashboard and the secret value appears nowhere in logs, summaries or artifacts.
5. **Given** a mutation job exceeds its time budget, **When** the budget expires, **Then** the job fails with a message naming the documented way to cut scope, rather than hanging.
6. **Given** a committed minimum score for a project, **When** a nightly score falls below it, **Then** that job fails and the summary names the project, the score and the minimum.
7. **Given** a change that lowers a committed minimum, **When** it is checked, **Then** the check fails; raising or keeping it passes.

---

### User Story 2 - Reporting from pipeline numbers (Priority: P1)

The owner wants one readable report per gate or CI run, and a history of the key numbers, all derived from what the pipeline already outputs. The run page shows test counts per lane, coverage per project, image sizes and scan counts. A coverage HTML report and a coverage badge source are produced. Each main-branch run appends one metrics line to a data file on a gh-pages branch, and a small static page shows the trends.

**Why this priority**: it turns existing outputs into visible, trustworthy evidence, and the history makes improvement (or decay) visible over time.

**Independent Test**: run the gate or CI on a branch and open the run summary; run on main with Pages enabled and open the trends page.

**Acceptance Scenarios**:

1. **Given** a completed CI or gate run, **When** the owner opens the run summary, **Then** it shows tables for test counts per lane, coverage per project, image sizes and scan counts, each value traceable to a step output or report file in that run.
2. **Given** the coverage Cobertura files, **When** the report step runs, **Then** a coverage HTML report is uploaded as an artifact and a badge source reflecting the same overall coverage is produced.
3. **Given** a main-branch run with GitHub Pages enabled, **When** the run completes, **Then** exactly one metrics line (commit, date, test counts, coverage, job durations, and mutation score when a recent nightly result exists) is appended to the data file on gh-pages and the trends page renders it.
4. **Given** GitHub Pages is not enabled or the gh-pages branch does not exist, **When** the main run completes, **Then** publishing is skipped without failing the workflow and the summary states what to switch on.
5. **Given** a pull request run (including from a fork), **When** it completes, **Then** nothing is published to gh-pages and no write permission is granted to that job.
6. **Given** a number that cannot be read (missing report, unparsable output), **When** the summary is built, **Then** the table shows the value as unavailable and the step fails visibly; it never shows a stale or default number.
7. **Given** an end-to-end test fails in CI, **When** the job ends, **Then** the Playwright screenshots and traces of the failing tests are uploaded as artifacts, traces being captured on the first retry only.
8. **Given** the end-to-end suite passes, **When** the job ends, **Then** no screenshot or trace artifact is uploaded.
9. **Given** a failure artifact from the real-stack run, **When** it is stored, **Then** its retention is 7 days; from fictional-data runs it is 14 days.

---

### User Story 2b - One combined Allure report with history (Priority: P1)

The owner wants a single report that combines the xUnit (Unit, Contract, Integration), Vitest (unit and route tests) and Playwright (end-to-end) results from the same CI runs, with Playwright screenshots and traces attached to failing tests, and with history so trends and flaky tests show over time. It is published as a static report on GitHub Pages. A nightly run repeats the end-to-end suite several times to expose flaky tests and feeds the same report.

**Why this priority**: it is the one place that shows all three test stacks together with failure evidence and flake history; it builds on the numbers from story 2 and is ranked with it.

**Independent Test**: after the adapter verification stop point, run the nightly workflow twice; the published report shows all supported stacks and trend history, and a seeded failing end-to-end test shows its screenshot and trace in the report.

**Acceptance Scenarios**:

1. **Given** the first task of this story, **When** it completes, **Then** a written report names, for xUnit v3, Vitest and Playwright, whether an Allure adapter exists and is maintained, its exact version, how it is wired, and the report generator's runtime and version requirements; work stops there until the owner reviews it.
2. **Given** no adapter supports xUnit v3, **When** the report is written, **Then** it says so and proposes the least-bad .NET path with its trade-off (for example converting the existing TRX results, or a documented skip of .NET in the combined report), and the xUnit version is not changed to fit an adapter.
3. **Given** the nightly run, **When** it finishes with Pages enabled, **Then** a combined report covering every supported stack is published and its history shows trends and flaky tests.
4. **Given** two nightly runs, **When** the second is published, **Then** the report shows trend data from both.
5. **Given** a deliberately failing end-to-end test, **When** the report is opened, **Then** the failing test shows its screenshot and trace.
6. **Given** a pull request from the same repository, **When** CI runs, **Then** the report is uploaded as an artifact only and is not published to Pages.
7. **Given** a pull request from a fork, **When** CI runs, **Then** no Allure step runs.
8. **Given** a main-branch run or the nightly run, **When** it finishes, **Then** only these publish to Pages, under a path that does not collide with the metrics trends page.
9. **Given** GitHub Pages is not enabled, **When** a publishing run finishes, **Then** publishing is skipped without failing and the job summary names what to switch on.
10. **Given** the real-stack end-to-end lane, **When** its results enter the published report, **Then** only pass or fail and timings are included, with no screenshots, traces or other attachments.
11. **Given** a scratch attachment from the real-stack lane in the publish input, **When** the privacy test runs, **Then** it fails; with the attachment removed it passes.
12. **Given** the nightly end-to-end repeat run, **When** it executes, **Then** the suite runs five times per test and its results feed the report; ordinary CI runs use one retry.

---

### User Story 3 - Property-based tests for CSV reader and import (Priority: P2)

The owner wants generated-input tests for the CSV reader and the tracker import normalization and dedupe, because example tests only cover the cases someone thought of. Properties come from the documented contracts: reading what was written returns the same rows, malformed input never throws, normalizing twice equals normalizing once, and dedupe tiers do not depend on row order.

**Why this priority**: it finds input classes no example test covers, but it adds no pipeline or reporting capability, so it ranks below stories 1 and 2.

**Independent Test**: run the Unit test project; the property tests pass. Introduce a deliberate bug in each area and the matching property fails with a printed seed.

**Acceptance Scenarios**:

1. **Given** any generated set of rows, **When** it is written and read back, **Then** the rows are equal to the original.
2. **Given** arbitrary malformed input (bad quoting, stray separators, invalid encodings, empty fields, very long fields, control characters), **When** it is read, **Then** the reader returns an expected failure result and never throws.
3. **Given** any generated row, **When** normalization is applied twice, **Then** the result equals applying it once.
4. **Given** any generated set of rows, **When** the same rows are processed in different orders, **Then** the dedupe tier assigned to each row is the same.
5. **Given** a property fails, **When** the failure is reported, **Then** the output includes the seed and shrunk counterexample so it can be replayed.
6. **Given** a counterexample is found during this feature, **When** it is fixed, **Then** a plain example-based regression test with that input exists alongside the property.
7. **Given** a seeded bug in each of the four areas, **When** the property tests run, **Then** they fail (shown red), and pass again once the bug is removed.

---

### User Story 4 - Local script lint in the review gate (Priority: P3)

The owner wants the review gate to run the same PowerShell script analysis as CI, so a violation such as a plural cmdlet noun fails on the developer machine before a push.

**Why this priority**: it is a small parity fix with no new capability.

**Independent Test**: add a scratch script with a known violation under the scripts folder and run the gate; the lint step fails. Remove it and the step passes.

**Acceptance Scenarios**:

1. **Given** a script containing a Warning or Error level violation, **When** the review gate runs, **Then** the lint step fails and names the file, line and rule.
2. **Given** the current scripts, **When** the review gate runs, **Then** the lint step passes.
3. **Given** the CI lint settings change, **When** the gate runs, **Then** it uses the same settings from a single shared definition rather than a second copy.

---

### Edge Cases

- A mutation run produces a score for a project with zero mutants (all excluded or no tests): reported as unavailable, not 100%.
- Mutation testing and the coverage gate disagree (high coverage, low mutation score): both are reported; neither overrides the other.
- A test that is flaky under mutation (timeouts counted as killed): timeouts are reported separately and the summary says how many.
- Two nightly runs overlap (manual dispatch during the schedule): the later one cancels or queues; they never publish concurrently.
- The validation run on a pull request overlaps a nightly: they use separate concurrency groups; the validation run never publishes, so no shared state is touched.
- A pull request changes the workflow file but comes from a fork: no validation run starts.
- One test stack fails in a run: the final report job still runs and the report shows the failure plus whatever the other stacks produced.
- Concurrent main runs append to the metrics file: appends must not lose or duplicate lines.
- Pages is enabled but the data file is malformed: the trends page shows an explicit error and skips bad lines rather than rendering nothing.
- The dashboard rejects the key (revoked or wrong): the job reports the failed publish, still uploads the artifact, and does not fail the score check on that account.
- A property generates input that is valid but pathological (huge fields): generation bounds keep the Unit run within its normal time budget.
- A counterexample found by a property is a defect in production code: fixed in this feature with the regression test; if it needs a design decision, recorded in research and left for review.
- An xUnit v3 adapter for Allure does not exist or is unmaintained: handled by the stop point and trade-off in Story 2b; the combined report may then cover .NET through converted results or omit it with a documented reason.
- A real-stack test fails: pass or fail and timing publish, but its screenshot and trace never leave the 7-day CI artifact.
- A flaky end-to-end test passes on retry: the report marks it flaky and the run stays green.
- The Allure history on gh-pages grows: retention of old history is bounded by the pinned report settings; pruning policy is recorded in research.
- All data in tests, summaries, reports and the data file is fictional; the nightly job never touches the developer's live database.

## Requirements *(mandatory)*

### Functional Requirements

**Mutation testing (Story 1)**

- **FR-001**: (Deferred, owner decision 2026-11-07.) Mutation testing for Domain and Application is not run, because Stryker.NET cannot produce a trustworthy score with xUnit v3 on .NET 10 (research.md D1). The limitation, its evidence and the revisit trigger MUST be recorded in research.md and in the mutation ADR, and the docs MUST state that Domain and Application have no mutation score and that the Story 3 property tests are the interim substitute.
- **FR-002**: The system MUST run mutation testing for the web pure-logic library only (not routes or components), using the existing unit test runner.
- **FR-003**: The mutation tools MUST be pinned: the web tools at exact versions in the web package manifest. (No .NET mutation tool is added while FR-001 is deferred.)
- **FR-004**: Mutation jobs MUST run only on a nightly schedule, on manual dispatch, and on one no-publish validation run: a `pull_request` trigger path-filtered to the workflow file itself, active only for pull requests whose head repository is this repository. Mutation MUST NEVER run on pushes or on any other pull request.
- **FR-005**: The mutation workflow MUST use least-privilege permissions, with actions pinned by full commit SHA, and MUST pass the repo's workflow security linting and the action-pin verification script. An automated check MUST fail if the workflow's trigger block is anything other than exactly `schedule`, `workflow_dispatch` and the path-filtered same-repo `pull_request` (no `push`, no `pull_request_target`, no other path filter, no other trigger), or if any secret is reachable on the pull request path. The validation run MUST NOT publish and MUST receive no secrets; it uploads its reports as artifacts only.
- **FR-006**: Each mutation job MUST have an explicit time budget. Research MUST document how to cut scope (file filters, mutation level, sharding) if a run approaches the budget.
- **FR-007**: Each job MUST upload its HTML report as an artifact and write the score and mutant counts (killed, survived, no coverage, timeout, ignored) to the job summary.
- **FR-008**: When the dashboard API key secret exists, the system MUST publish the report and badge to the Stryker Dashboard; when absent, it MUST skip the publish, say so in the job summary, and still pass on its own merits.
- **FR-009**: No secret value MAY appear in logs, summaries or artifacts. The secret MUST be exposed only to the step that publishes, and never to steps that run untrusted or pull request code.
- **FR-010**: The initial threshold MUST be report-only (break at 0). The first recorded scores MUST be written to `research.md` from the first run's output.
- **FR-011 (ratchet policy)**: After the baseline is recorded, each mutation project MUST have a committed minimum score file in the repo. The nightly job MUST fail when a score is below its project's minimum. A committed minimum MAY be raised or kept by a deliberate, reviewed change (never by an automated commit) and MUST NOT be lowered; a check MUST fail any change that lowers it relative to the main branch. The initial minimum MUST be the first recorded score rounded down to a whole percent.
- **FR-012**: The top surviving mutants per project (at least the ten most meaningful, by file and mutation) MUST be listed in research. Each MUST be either turned into a test in this feature (written first and shown failing against the mutant) or recorded as an equivalent mutant with a reason.

**Reporting (Story 2)**

- **FR-013**: The gate and CI jobs MUST write run-summary tables for test counts per lane, coverage per project, image sizes and scan counts, built by parsing the outputs and report files those jobs already produce.
- **FR-014**: The summary builder MUST fail visibly when an expected input is missing or unparsable and MUST NOT substitute a default, remembered or previous value.
- **FR-015**: The system MUST produce a coverage HTML report from the existing Cobertura files and upload it as an artifact.
- **FR-016**: The system MUST produce a coverage badge source. Research MUST choose where it is hosted (gh-pages, a Shields endpoint fed from an artifact, or the mutation dashboard) by the fewest moving parts and record the badge URLs for SHOWPIECE 2.
- **FR-017**: Each main-branch run MUST append exactly one metrics record (commit, UTC timestamp, test counts per lane, coverage, job durations, and the latest nightly mutation score with that run's own date and commit when one exists) to a data file on the gh-pages branch.
- **FR-018**: A small static page on gh-pages MUST read the data file and show trends for test counts, coverage and mutation score. It MUST treat the data as untrusted text, handle malformed lines, and load no third-party scripts that are not pinned and integrity-checked.
- **FR-019**: When GitHub Pages or the gh-pages branch is unavailable, publishing MUST be skipped without failing the workflow, and the summary MUST state the exact settings the owner must change.
- **FR-020**: Publishing to gh-pages MUST run only for main, in a job with the minimum write permission; pull request and fork runs MUST NOT publish.
- **FR-021**: On a CI end-to-end failure the system MUST upload Playwright screenshots and traces as artifacts; traces MUST be captured on the first retry only; nothing MUST be uploaded when the suite passes. Retention MUST be 7 days for anything from the real-stack run and 14 days for fictional-data runs.

**Allure combined report (Story 2b)**

- **FR-022**: The first task of Story 2b MUST be verification of the Allure adapters for this repo's exact stack (xUnit v3 for .NET, Vitest, Playwright): existence, maintenance state, pinned versions, wiring, and the report generator's runtime and version. Work MUST stop after it for owner review. The result is recorded in research.
- **FR-023**: If no adapter supports xUnit v3, research MUST say so and propose the least-bad .NET path with its trade-off. The xUnit version MUST NOT be changed to fit an adapter.
- **FR-024**: The combined report MUST cover xUnit (Unit, Contract, Integration), Vitest (unit and route tests) and Playwright, assembled per publishing run (CI on main, and the nightly run) by a final job from the results that run's existing jobs already produce, with no extra test runs and with the final job running even when a stack failed so one failed stack still yields a report; the nightly run adds the end-to-end repeat. The CI schedule is not changed. The report includes Playwright screenshots and traces as attachments for fictional-data runs.
- **FR-025**: Allure history MUST be kept on gh-pages so the report shows trends and flaky tests.
- **FR-026**: Playwright MUST retry once in CI. A nightly run MUST repeat the end-to-end suite five times per test and feed the report.
- **FR-027**: The report MUST be a static site on GitHub Pages under a path that does not collide with the metrics trends page. When Pages is unavailable, publishing MUST be skipped without failing and the job summary MUST state what to switch on.
- **FR-028**: Only runs on fictional data MAY publish attachments to the public report. Real-stack results MUST publish pass or fail and timings only. Real-stack traces MUST be a CI artifact with 7-day retention and MUST NEVER be uploaded to Pages.
- **FR-029**: An automated test MUST fail when the publish input contains an attachment from the real-stack lane.
- **FR-030**: Allure steps MUST NOT run on forks or on pull requests from forks. On pull requests from the repository they MUST upload the report as an artifact only. Pages publishing MUST run only on main and the nightly run.
- **FR-031**: Allure tools, adapters and runtimes MUST be pinned to exact versions, actions pinned by full commit SHA, the workflow MUST pass zizmor, and permissions MUST be least-privilege per job.

**Property-based tests (Story 3)**

- **FR-032**: Property tests MUST live in the existing Unit test project; no test project is added.
- **FR-033**: The property-testing library MUST be pinned to an exact version.
- **FR-034**: Properties MUST be derived from the documented contracts and cover: CSV round trip, no throw on malformed input, idempotent normalization, and dedupe tier stability under row order.
- **FR-035**: A failing property MUST print its seed and shrunk counterexample so the case can be replayed with that seed.
- **FR-036**: Each counterexample found MUST be turned into a plain example-based regression test. Any fix to production code MUST follow the red-then-green order.
- **FR-037**: Generated data MUST be fictional and bounded so the Unit lane stays within its current time budget.

**Script lint in the gate (Story 4)**

- **FR-038**: The review gate MUST run PSScriptAnalyzer over the scripts folder with severity Warning and Error, the same settings CI uses, from a single shared settings definition.
- **FR-039**: The lint step MUST fail the gate on any finding and print file, line and rule.
- **FR-040**: A test MUST show the step red on a scratch script with a violation and green after it is removed. The scratch script MUST NOT be left in the repo.

**Cross-cutting**

- **FR-041**: Two ADRs MUST record the decisions: one for mutation testing (scope, ratchet, nightly shape and validation run, dashboard) and one for the reporting stack (Allure, ReportGenerator, mutation reports, trends, hosting of badge and history, privacy design).
- **FR-042**: All new packages, tools and images MUST be pinned. All files MUST use LF endings. No real data appears anywhere. Nothing is committed or pushed by the feature work.
- **FR-043**: Every number shown in a summary, badge, data file, trends page, research or ADR MUST be traceable to a command or workflow output, and research records the command or run that produced each.

### Key Entities

- **Mutation score**: percentage of generated mutants killed by the tests, per project, with counts of killed, survived, no coverage, timeout and ignored.
- **Mutation minimum**: committed per-project floor that may only rise.
- **Surviving mutant**: a code change no test noticed; classified as a real gap (becomes a test) or equivalent (reason recorded).
- **Run summary**: the per-run tables of test counts per lane, coverage per project, image sizes and scan counts.
- **Metrics record**: one line per main run with commit, timestamp, counts, coverage, durations and optional mutation score.
- **Property**: a contract-derived rule checked against generated input, with a replayable seed.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A run of the nightly workflow (the pull request validation run before merge, or a manual dispatch or schedule after merge) completes within its stated time budget and produces the web mutation HTML report artifact and a job summary containing its score, verified from the run page.
- **SC-002**: The first recorded web mutation score appears in `research.md` with the run that produced them, and the committed minimums equal those scores rounded down.
- **SC-003**: Lowering a committed minimum in a scratch change makes the minimum check fail; raising it passes.
- **SC-004**: With the dashboard secret absent, the nightly run succeeds and its summary states the publish was skipped; a search of the run's logs for the secret value (when present) finds nothing.
- **SC-005**: Mutation never runs on pull requests except the no-publish validation run when the workflow file itself changes. Verified by the trigger-shape check passing, a review of every workflow's `on:` block showing mutation appears only in the nightly workflow, and a pull request that does not touch the workflow file showing no mutation check.
- **SC-006**: A gate or CI run summary lists test counts for every lane, coverage for every project, image sizes and scan counts, and each value matches the corresponding step output in the same run.
- **SC-007**: A main run with Pages enabled adds exactly one record to the data file and the trends page shows it; with Pages disabled the same run succeeds and the summary names the setting to enable.
- **SC-008**: For each of the four property areas, a seeded bug turns the property red with a printed seed, and removing the bug turns it green; both outputs are shown.
- **SC-009**: The top survivors per project are listed in research; every real gap has a test that fails against its mutant and passes against the real code, and every other survivor has a recorded equivalence reason.
- **SC-010**: A scratch script with a violation turns the gate's lint step red; with it removed the step is green.
- **SC-011**: The ADR exists, workflow security linting and action-pin verification pass, and the fast test lane stays green with no increase in its runtime beyond the property tests' own bounded cost, reported from command output.
- **SC-013**: The adapter verification report exists in research before any Allure build work and names the supported or unsupported path for xUnit v3, Vitest and Playwright.
- **SC-014**: The nightly run produces one combined report containing every supported stack, shown from the run output and the published page.
- **SC-015**: Trends are visible in the published report after two runs.
- **SC-016**: A seeded failing end-to-end test shows its screenshot and trace in the report.
- **SC-017**: The privacy test is red against a scratch real-stack attachment and green once it is removed; both outputs are shown.
- **SC-018**: A passing end-to-end run uploads no screenshot or trace artifacts; a failing run uploads them with the stated retention, verified from the run page.
- **SC-012**: A search of the repo for hand-typed test counts and coverage figures in prose (for example 1595 and 92) shows either none or figures accompanied by the command that regenerates them. The three coverage figures are stated and reported from command output: 92.57% is the measured Domain and Application figure, 85% is the CI line-coverage floor for Domain and Application, and 80% lines and 75% branches are the web floors.

## Assumptions

- The owner will create the Stryker Dashboard API key secret later and, if wanted, enable GitHub Pages from the gh-pages branch; the feature works fully without either.
- The existing Cobertura output, gate output and image scan reports are stable enough to parse; where they are not, this feature adds structured output to the same steps rather than scraping prose.
- The existing CI already runs lanes in separate jobs; the summary builder reads their artifacts or step outputs and does not rerun tests.
- Mutation testing of the web library reuses the existing unit tests as they are; adding tests is limited to killing real surviving mutants.
- A nightly mutation run for these scopes is expected to fit a per-job budget in the tens of minutes on a standard runner; the exact budget is set from the first measured run and recorded in research.
- The metrics history is a single append-only file; retention, rotation and pruning are not needed until it is large enough to matter.
- The repo is public and the owner is its only maintainer, so the dashboard and Pages publication use the owner's account and expose only fictional data and aggregate numbers.
- "Rounded down to a whole percent" for the initial minimum assumes scores are reported to at least one decimal place.
