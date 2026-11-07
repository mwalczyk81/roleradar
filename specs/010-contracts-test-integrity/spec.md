# Feature Specification: Contracts and Test Integrity

**Feature Branch**: `010-contracts-test-integrity`

**Created**: 2026-11-06

**Status**: Draft

**Input**: User description: "Feature 010, contracts and test integrity. Three places where the repo says something that is not enforced or not true: enum values in the API contract are hand-maintained snapshots, the review gate can pass vacuously, and flaky tests are rerun instead of diagnosed. Plus one small Login page defect."

## Context

Today the repo makes three claims it does not back up:

1. "The contract is the source of truth" -- but five enum-like values (disposition, application status, company status, ownership type, application event type) are typed as plain strings in the API schema and the generated client types. The real value lists live in hand-maintained snapshot files plus drift and equality tests.
2. "The review gate passes only when the repo is healthy" -- but one security-scan test writes its own passing report when none exists, and the gate's test filter lists are maintained by hand and have already missed tests once.
3. "Failures mean something" -- but three known flakes have been rerun, not explained.

This feature makes each claim true, or records exactly why it is not yet.

## Scope

**In scope**: the four user stories below.

**Out of scope** (do not touch in this feature):

- Stryker, Allure, Bogus and property-based tests (feature 011).
- The CLI posting transition and the audit actor fix (surface parity work).
- TypeScript 7 and cookie 2 major upgrades (the dependabot ignores stay).
- Tooling consolidation.
- Any hosting work.

**Deliberate non-change**: OData `$filter` on these properties stays `Edm.String` with the lowercase-comparison rule from ADR 0028. Only the OpenAPI schema and the generated client types gain enum constraints. OData metadata is not changed, and string filters keep being rejected unless they use the lowercased-literal form.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Enum values come from the API, not hand snapshots (Priority: P1)

A developer adds or renames a value on one of the five Domain enums. The API schema and the generated client types change with it, the web app sees the new union type at typecheck time, and no one edits a snapshot file by hand. A developer who adds a new enum-typed API property and forgets to register it gets a failing test.

**Why this priority**: The "Contract first" rule currently depends on discipline and duplicated lists. Every enum change touches several files that can silently disagree. Generation removes the class of drift.

**Independent Test**: Change a Domain enum value in a scratch branch, regenerate the contract, and observe the schema, the client types and the web typecheck all react; delete the registry entry for one property and observe the registry test fail. Delivers a contract that cannot disagree with Domain.

**Acceptance Scenarios**:

1. **Given** the generated API schema, **When** it is inspected for disposition, application status, company status, ownership type and application event type properties, **Then** each carries an explicit list of allowed values equal to the Domain enum's serialized values.
2. **Given** the generated client types, **When** the web app is typechecked, **Then** those properties are string-literal unions and assigning an unknown value fails typecheck.
3. **Given** a Domain enum property exposed by the API that is not in the single enum registry and not on the explicit allowlist (with a written reason), **When** the test suite runs, **Then** the registry test fails and names the property.
4. **Given** the registry test is run before the registry exists, **When** it is run, **Then** it fails first (red), and passes after generation is in place (green).
5. **Given** the web helper modules for dispositions and company enums, **When** they are read, **Then** value lists are derived from the generated types where the type can supply them, and only labels and ordering (UI facts) remain hand-written.
6. **Given** each file under `contracts/`, **When** it is inspected, **Then** snapshot files whose values are now generated are removed, with their drift and equality tests; files encoding behaviour (`disposition-matrix.json`, `insights-lenses.json`) and any enum-like lists the API does not type as enums are each kept or removed on their merits, with the decision and reasoning written in `research.md`.
7. **Given** the guidance bullet "Contract first" in `AGENTS.md` and every doc that quotes it, **When** the feature is done, **Then** all of them describe generation, not snapshots, and a new ADR (next number) records the decision.
8. **Given** an OData request with a string `$filter` on one of these properties, **When** the contract change ships, **Then** behaviour is identical to before (tolower rule enforced, other forms rejected with 400).

---

### User Story 2 - The review gate cannot pass vacuously (Priority: P1)

A developer runs the review gate. If the container security scan did not actually run, or found a CRITICAL finding (the severity policy the scan uses), the gate does not pass. If a new Integration test class is added, the gate either runs it automatically or a test fails to say it is uncovered.

**Why this priority**: A gate that can pass without checking anything is worse than no gate. Two Integration tests were already missed during the 008 review.

**Independent Test**: Seed a scan report containing a CRITICAL finding and observe `ContainerSecurityScanReport_ExistsAndPasses` fail; remove the report and observe skip-with-reason or clear failure and no file written; add a dummy test class and observe the coverage test fail.

**Acceptance Scenarios**:

1. **Given** no scan report exists and no require flag is set, **When** `ContainerSecurityScanReport_ExistsAndPasses` runs with plain `dotnet test`, **Then** it skips with an explicit reason and never writes a report.
2. **Given** no scan report exists, **When** it runs in CI or with `ROLERADAR_REQUIRE_SCAN=1`, **Then** it fails (a real scan report is mandatory there).
3. **Given** a scan report containing a finding whose severity is in the failing set of the severity policy (default CRITICAL), **When** the test runs, **Then** it fails and names the finding. A finding outside the failing set (for example HIGH under the CRITICAL policy) does not fail the test, and the HIGH and CRITICAL counts per image are still reported.
4. **Given** a genuine passing scan report produced by the real scan, **When** the test runs, **Then** it passes.
5. **Given** the review gate runs, **When** it reaches the scan, **Then** the gate itself scans the images it just built with the same pinned Trivy image and flags as CI, writes the reports the test reads, and runs the test with `ROLERADAR_REQUIRE_SCAN=1`, so the gate cannot print a pass with the scan skipped.
6. **Given** the review gate script, **When** it runs the Integration tests, **Then** every Integration test class runs, including the container MCP test and the two tests missed in the 008 review.
7. **Given** a test class that no gate step runs, **When** the step-coverage check runs, **Then** it fails and names the uncovered class. If the gate is changed to derive its steps from the tests themselves instead of hand-maintained lists, the same guarantee holds by construction and the check proves it.

---

### User Story 3 - Flakes get diagnosed, not rerun (Priority: P2)

For each of three known intermittent failures, the repo ends up with either a fix backed by a test, or a short recorded diagnosis with the evidence gathered and the observation that would confirm it. A longer timeout alone does not count as a diagnosis.

**Why this priority**: Rerunning until green hides real defects and erodes trust in the gate. Not blocking, because each flake is intermittent and currently passes on rerun.

**Independent Test**: For each flake, read the spec/research note or run the added test; the cause (or the explicit "not yet reproduced, here is what would confirm it") is on record.

**Acceptance Scenarios**:

1. **Given** `CliContainer_ReceivesSigterm_TerminatesWithinBoundedTimeout` (failed once at 12 s, passes alone in about 0.6 s), **When** investigated, **Then** the cause is identified from evidence (timing under load, shared Docker, shutdown handler) and either fixed with a test or recorded with that evidence.
2. **Given** a Mongo test container reported as "is not running", **When** the start gate fires, **Then** the container State and recent logs are captured into the failure output so the next occurrence is explained.
3. **Given** the web visual baseline capture `company-detail-errors` (intermittent, same 1833 differing pixels, sidebar position, dark theme), **When** investigated, **Then** the layout race is found and fixed in the app or the test, or the capture waits on a real observable condition instead of time.
4. **Given** any flake that cannot be reproduced during this feature, **When** the feature closes, **Then** a note in this spec records the evidence gathered and what observation would confirm the cause.

---

### User Story 4 - Failed password shows only the password error (Priority: P3)

A person enters a wrong password on the Login page. They see "Invalid username or password" and nothing else. The one-time-code field messages ("The Code field is required" and the length message) do not appear on that step.

**Why this priority**: Small, cosmetic but confusing; no data or security impact.

**Independent Test**: Submit a wrong password in the Identity page tests and in the identity-signin end-to-end test; assert only the password-step error is present.

**Acceptance Scenarios**:

1. **Given** the password step, **When** a wrong password is submitted, **Then** only the invalid username or password message is shown.
2. **Given** the one-time-code step, **When** an empty or wrong-length code is submitted, **Then** the code field messages still appear as before.
3. **Given** the tests for this behaviour, **When** they are written, **Then** they are shown failing before the fix.

### Edge Cases

- A Domain enum value is added with no change to the registry: generation picks it up (registered property), so the contract updates; an unregistered new property fails the registry test.
- An enum value that is serialized differently from its name (snake_case like `pe_backed`): the generated schema uses the serialized value, not the C# name.
- A property is nullable or an array of enum values: the generated constraint allows null or applies to the items.
- A web consumer that relied on a hand-written list order: order stays a UI fact and stays hand-written.
- The scan report exists but is empty, malformed or from a different image: the test fails clearly, it does not pass.
- Local developer has no Trivy: the test skips with the reason rather than failing the local fast loop.
- A flake does not reproduce within the investigation budget: the note records evidence and the confirming observation instead of a speculative fix.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The generated API schema MUST carry explicit allowed values for disposition, application status, company status, ownership type and application event type, taken from the Domain enums. ("Application status" is the Domain enum `ApplicationOutcome`; the registry and its docs use that name.) Every other Domain enum the API exposes as a string MUST be registered the same way.
- **FR-002**: Enum-to-property mapping MUST come from one registry. The schema generation MUST be driven by it.
- **FR-003**: A test MUST fail when an API-exposed Domain enum property is neither in the registry nor on an explicit allowlist that states a reason for each entry.
- **FR-004**: Generated client types MUST express these values as string-literal unions.
- **FR-005**: Web code MUST take value lists from the generated types where the type can supply them; hand-written lists are limited to labels and ordering.
- **FR-006**: Snapshot files whose values are generated MUST be removed together with their drift and equality tests. Every other file in `contracts/` MUST be inspected, and each keep or remove decision MUST be justified in `research.md`.
- **FR-007**: OData `$filter` behaviour and OData metadata MUST NOT change (string type, lowercase rule, ADR 0028).
- **FR-008**: `AGENTS.md` and all docs quoting the "Contract first" rule MUST be amended, and a new ADR MUST record generated enums replacing snapshots.
- **FR-009**: `ContainerSecurityScanReport_ExistsAndPasses` MUST NOT write a report. It MUST require a report produced by the real scan and apply the same severity policy the scan used. It MUST fail when a report is absent in CI or when `ROLERADAR_REQUIRE_SCAN=1` is set, and skip with an explicit reason otherwise. The failing severities are one policy value, default `CRITICAL` (decided at T024 after a spike, see research R6), used identically by the CI Trivy steps (`--severity`), `review-gate.ps1` and the evaluator, and a test asserts they match (a regex on `--severity <value>`). The test always reports the HIGH and CRITICAL counts per image without failing on HIGH under the CRITICAL policy; tightening later is a one-line policy change. `review-gate.ps1` MUST run the same Trivy scan itself on the images it built, write the reports, and run the test with the require flag.
- **FR-010**: The review gate MUST run every Integration test class, or a test MUST fail when a test class is not covered by some gate step.
- **FR-011**: The container MCP test and the two tests missed in the 008 review MUST be covered by the gate.
- **FR-012**: The SIGTERM container flake MUST end with a fix plus test, or a recorded diagnosis with evidence. A timeout increase alone is not acceptable.
- **FR-013**: When the Mongo container start gate fires, the failure output MUST include the container State and recent logs.
- **FR-014**: The `company-detail-errors` capture MUST wait on a real observable condition, or the layout race MUST be fixed, so the capture is deterministic.
- **FR-015**: A wrong password on the Login page MUST show only the password-step error; code-step messages MUST still show on the code step.
- **FR-016**: All new and changed tests MUST be shown failing before the implementation, use fictional data only, and keep LF line endings.

### Key Entities

- **Enum registry**: the single mapping from an API property to its Domain enum, plus an allowlist of enum properties deliberately not constrained, each with a reason.
- **Generated contract**: the API schema and client types, produced from Domain and the registry, never hand-edited.
- **Scan report**: the output of the real container vulnerability scan; the only valid input to the scan test.
- **Gate step**: one verification stage of the review gate; every test class belongs to at least one.
- **Flake record**: for each intermittent failure, a fix with test, or evidence plus the confirming observation.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: `contracts/*-enums.json` snapshots whose values are generated no longer exist (`git ls-files contracts` shows only the files justified in `research.md`).
- **SC-002**: The registry test is red before the registry and generation exist, and green after; adding an unregistered Domain enum property to a scratch change turns it red again.
- **SC-003**: The generated API schema contains an allowed-values list for all five properties, and the web typecheck exits 0 using the generated unions.
- **SC-004**: `ContainerSecurityScanReport_ExistsAndPasses` fails against a report with a CRITICAL finding under the default policy (and a HIGH finding fails only under `HIGH,CRITICAL`), skips with a stated reason with no report under plain `dotnet test`, fails with no report when `ROLERADAR_REQUIRE_SCAN=1`, and leaves no file behind in any case. `review-gate.ps1` output shows the scan ran (report count and findings) and the scan test passed with the require flag.
- **SC-005**: The review-gate step coverage test is green, and turns red when a test class outside every step is added.
- **SC-006**: The Integration tests named in the 008 review (including the container MCP test) are covered by the gate through lane selection, and the gate output prints the per-lane test count (for example the images lane), not test names. `TestLaneCoverageTests` is the check that a named class is in a lane the gate runs.
- **SC-007**: Each of the three flakes has either a passing regression test or a written record with evidence and a confirming observation. Reruns of the SIGTERM test and the visual baseline test under load (20 consecutive runs each) show no failure after the fix, or the record states why not.
- **SC-008**: Login page tests (Identity page tests and the identity-signin e2e) fail before the fix and pass after, asserting exactly one error message on a wrong password.
- **SC-009**: `AGENTS.md` and every doc that quoted "Contract first" no longer mention hand snapshots; a new ADR exists with the next number.
- **SC-010**: Fast tests, web typecheck, lint, tests, build and e2e, and the review gate all pass at the end, with counts reported from command output only.

## Assumptions

- The existing stats `window` enum schema transformer is the pattern to follow for the new generation; no new mechanism is introduced.
- The five Domain enums already serialize to the values currently in the snapshots; generation must match them exactly (drift would be a contract break).
- Trivy produces the scan reports in CI today (`CRITICAL`, `--ignore-unfixed`). Plain local `dotnet test` without reports skips; the gate and CI never skip because they scan first and set the require flag or `CI`.
- The scan severity policy is `CRITICAL` with `--ignore-unfixed`. The T024 spike found 0 CRITICAL and 22 fixable HIGH findings (cli 12, web 10, none in mcp, identity or api; research R6). The owner chose to keep CRITICAL and not fix the HIGH findings in this feature; they are tracked on a board card (cli: mongo-tools Go stdlib and x/net; web: npm inside node:22-alpine).
- The enum registry covers all Domain enums the API exposes, not only the five named; unexposed enums go on the allowlist with a reason. Two ADRs are written: 0035 (generated enum contract) and 0036 (test lanes).
- Enum-like lists in `application-enums.json` and `operational-enums.json` that the API does not type as enums (for example resume variants, criterion categories, supported entity routes) are decided per list in `research.md`; they are removed only if generated, kept if they still encode something nothing else supplies.
- Flake investigation is time-boxed. A flake that does not reproduce ends with evidence and a confirming observation, not a guess.
- The Mongo start gate already captures container State and logs on the first "is not running" failure (found while planning); FR-013 therefore closes the remaining gaps: the second-failure and the generic-failure paths.
- Data in all tests is fictional.

## Flake Records

Recorded at the end of user story 3 (2026-11-07). Evidence and numbers are in research.md R8 to R10 and R14. The spec asked for a fix with a test, or the evidence and what would confirm the cause, for each flake. A longer timeout was not used for any of them.

| Flake | Diagnosis | Fix and test | What would confirm or reopen it |
|-------|-----------|--------------|----------------------------------|
| CLI container SIGTERM (12 s once, 0.6 s alone) | SIGTERM sent right after docker run -d returns can reach PID 1 before the process can catch it and is then dropped. Replaying the test's commands: idle 20 of 20 exit 143; under CPU saturation 7 of 80 exited 1 (signal dropped), one run took 5995 ms. The old test also accepted exit 1, so it could pass with SIGTERM ignored. | The tests wait until SigCgt shows the SIGTERM handler (helper container sharing the PID namespace), use a command that cannot end by itself (backup --passphrase-stdin), accept exit 0 or 143 only, and time the shutdown from the moment the kill call returns. 13 parser tests red then green; 20 of 20 consecutive runs green under saturation plus container-start bursts. The MCP SIGTERM test uses the same wait. | A failure now prints the container State and logs. A run that fails with "stopped before it could be signalled" or a readiness timeout points at the Docker daemon, not the application. |
| Mongo test container "is not running" | Not reproduced and not previously recorded anywhere: the restart log variable is not set in the repo or in any run of this feature. The gate already printed State and logs for the first failure, but not for the second, and the generic failure path printed a log tail without State. | Second-failure and generic-failure messages now carry the docker State and log tail (both attempts); the diagnostics no longer throw for a container that was never created. 6 of 6 tests, 4 of them red first. | The next occurrence prints State (ExitCode, OOMKilled, FinishedAt), memory, concurrent starts and log tail for both attempts, and appends to the file named by ROLERADAR_TEST_RESTART_LOG when set. |
| Web visual baseline company-detail-errors | A capture race, not an app defect: the sidebar is position sticky and a full-page capture shows it at the current scroll offset, which is still settling (Playwright scrolled to the Save button, then the page reflowed around the error banner). Reproduced under extreme load: 2 of the completed dark captures differed by 2072 px, all of them the sidebar, shifted about 3 px. | settleForCapture scrolls to the top and waits until scroll offset, document height and shell geometry are unchanged for four frames, or refuses to capture and names the geometry. 3 deterministic Playwright tests, red first. | 0 mismatches in the 13 captures that completed under the same load (27 timeouts from saturation), which is thin; a local baseline needs one update-snapshots because captures now start from the top. Reopen if a mismatch with a non-sidebar region appears. |
| Backup_WhenCancelled_TerminatesEntireProcessTree (found at the end of US2) | Not reproduced. Not slowness: the child needs 67 ms at idle and at most 489 ms under extreme load against a 5000 ms budget. The test never looked at the backup task, so a backup that ended before launching a child would give the same bare message. The class is not an image test (one static helper call) and was wrongly in the images lane. | The wait now watches the backup task and reports faulted, completed-without-a-child or still-running with elapsed time (4 tests, red first); the class is out of the images lane and a new check fails for any images-lane class that never uses the images. | The next failure says which of the three it was. If it says completed without any child, capture the dump command stderr for that run. |
