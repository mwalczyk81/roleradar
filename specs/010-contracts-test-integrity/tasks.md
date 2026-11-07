---

description: "Task list for feature 010 contracts and test integrity"
---

# Tasks: Contracts and Test Integrity

**Input**: Design documents from `/specs/010-contracts-test-integrity/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Required and written first (constitution II, AGENTS.md). Every test task below must be run and its failing output shown before the matching implementation task. Fictional data only. LF endings. No commit or push. Never run against the developer's running Mongo or stack.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an unfinished task)
- **[Story]**: US1 enum generation, US2 gate integrity, US3 flakes, US4 Login

## Phase 1: Setup

- [X] T001 Record baselines from command output in `specs/010-contracts-test-integrity/research.md` (new "Baseline" section): `pwsh ./scripts/test-fast.ps1` counts, `pnpm --filter web run typecheck` exit code, and `git ls-files contracts`
- [X] T002 [P] Confirm the contract baselines are green before any change and note the result in research.md: `dotnet test tests/RoleRadar.Tests.Contract -c Release --filter "FullyQualifiedName~ApiOpenApiContractSnapshot|FullyQualifiedName~ODataMetadataDrift"` (the OData drift test needs the shared Mongo fixture; run it only against its isolated container)

## Phase 2: Foundational

None. The stories are independent; US1 and US2 can run in parallel, US3 evidence tasks can start anytime.

## Phase 3: User Story 1 - Enum values come from the API (Priority: P1)

**Goal**: schema and client types carry enum values generated from one registry; replaced snapshots deleted.

**Independent Test**: registry test red then green; `roleradar-api.json` has `enum` for the five properties; `pnpm --filter web run typecheck` exits 0; `git ls-files contracts` matches research R4.

How the contract is produced (research R4b): `dotnet build src/RoleRadar.Api` writes `contracts/openapi/roleradar-api.json`; `ApiOpenApiContractSnapshotTests` compares it with the live `/openapi/v1.json`; the client is generated with `npx --yes openapi-typescript@7.10.1 contracts/openapi/roleradar-api.json -o contracts/client/api-types.ts` (as CI does).

### Tests first (show each failing)

- [X] T003 [P] [US1] Write the registry completeness test in `tests/RoleRadar.Tests.Contract/EnumContractRegistryTests.cs`: reflect over `RoleRadar.Domain` types, collect every property whose type (or nullable or collection element type) is an enum, assert each is in `EnumContractRegistry.Entries` or `EnumContractRegistry.Allowlist`; second test asserts every allowlist `Reason` is non-blank. Add only an empty stub `EnumContractRegistry` in `src/RoleRadar.Api/OpenApi/EnumContractRegistry.cs` so it compiles, then run it and show it failing on content
- [X] T004 [P] [US1] Write the OpenAPI enum test in `tests/RoleRadar.Tests.Contract/OpenApi/OpenApiEnumContractTests.cs`: load `contracts/openapi/roleradar-api.json`, for each registry `CarriedBy` pair assert the property has `enum` equal to the Domain enum's `ToSnakeCase()` names in declaration order; assert the five mandatory properties (posting disposition `PostingDisposition`, application status `ApplicationOutcome`, company status, ownership type, application event type) are present. Show it failing
- [X] T005 [P] [US1] Write the web type-level test `web/tests/unit/generated-enums.test.ts` using `expectTypeOf` against `contracts/client/api-types.ts`: for each of the five mandatory properties assert `string extends T ? false : true` (so it fails while the property is plain `string` and a `Record<string, ...>` cannot satisfy it), and assert `DISPOSITION_METADATA`, `COMPANY_STATUS_METADATA`, `APPLICATION_OUTCOME_METADATA`, `APPLICATION_EVENT_TYPE_METADATA` are keyed by exactly those unions. No `any`, no casts. Show it failing (vitest typecheck mode or `pnpm --filter web run typecheck`) while the generated types are still `string`
- [X] T006 [US1] Note in research.md which existing Integration test covers the OData string-filter rule (tolower literal, 400 on other forms) so T011 can run it; `ODataMetadataDriftTests` (reused, not rewritten) is the FR-007 metadata guard. Also check `tests/RoleRadar.Tests.Contract/OpenApi/InsightsStatsOpenApiContractTests.cs` and `PostingRejectionSyncOpenApiTests.cs` for assumptions that break when `enum` appears

### Implementation

- [X] T007 [US1] Inspect every Domain enum-typed property (`src/RoleRadar.Domain/Tracking/TrackingEnums.cs` and entities) and every API DTO that exposes it; fill `EnumContractRegistry.Entries` (DomainProperty, EnumType, CarriedBy, Shape) and `Allowlist` (each with a reason, for example "not exposed by the API") in `src/RoleRadar.Api/OpenApi/EnumContractRegistry.cs`. Register the five mandatory enums plus every other enum the API exposes as a string (research R3, `ApplicationOutcome` is "application status"). Re-run T003 to green
- [X] T008 [US1] Apply the registry in `src/RoleRadar.Api/OpenApi/ExplicitTypeSchemaTransformers.cs`: in the schema transformer, for the DTO type being transformed set `schema.Enum` from the registry (scalar, nullable scalar keeps `null` allowed, array applies to `items`). Leave `DeclareInsightsWindowEnum` as is unless it fits without behaviour change. No hand-written value lists
- [X] T009 [US1] Regenerate: `dotnet build src/RoleRadar.Api -c Release` (writes `contracts/openapi/roleradar-api.json`), then `npx --yes openapi-typescript@7.10.1 contracts/openapi/roleradar-api.json -o contracts/client/api-types.ts`; show `git diff --stat contracts`. Re-run T004 to green and `dotnet test tests/RoleRadar.Tests.Contract -c Release --filter "FullyQualifiedName~ApiOpenApiContractSnapshot"` plus the two tests checked in T006
- [X] T010 [US1] Run `ODataMetadataDriftTests` and the Integration OData filter test noted in T006; both must pass unchanged (FR-007)
- [X] T011 [P] [US1] Rewrite `web/app/lib/dispositions.ts` so `PostingDisposition` is the generated union from `contracts/client/api-types.ts`, `DISPOSITION_METADATA` is `Record<PostingDisposition, DispositionMeta>`, ordering is an explicit `DISPOSITION_ORDER` typed `readonly PostingDisposition[]` with a compile-time completeness check, `isPostingDisposition` uses the record keys, and `DISPOSITION_VALUES` is removed or derived from the order list. Keep `RESURFACE_POLICY_VALUES` only if the type cannot supply it
- [X] T012 [P] [US1] Rewrite `web/app/lib/company-enums.ts` the same way: `CompanyStatus` and `OwnershipType` from the generated types, `COMPANY_STATUS_VALUES` and `OWNERSHIP_TYPE_VALUES` removed or derived from the options lists, guards via record keys; keep labels, tones and ordering
- [X] T013 [US1] Rewrite `web/app/lib/application-enums.ts` the same way for outcome, event type and resume variant; then update `web/app/components/CreateApplicationDialog.tsx`, `web/app/routes/applications.$id.tsx`, `applications._index.tsx`, `companies.$id.tsx`, `companies._index.tsx`, `criteria.tsx`, `postings._index.tsx`, `notes.$id.tsx`, and `web/app/lib/insights-*.ts` only where they imported a removed value list (sequential after T011 and T012, since these files import all three helpers)
- [X] T014 [US1] Update web unit tests `web/tests/unit/dispositions.test.ts`, `company-enums.test.ts`, `application-enums.test.ts`: remove equality-against-snapshot halves, keep label, tone, order and guard behaviour. Run `pnpm --filter web run typecheck` (exit 0), `pnpm --filter web run lint`, `pnpm --filter web run test`; T005 now green
- [X] T015 [US1] Before each delete below, grep every consumer of the file name across `src`, `tests`, `web`, `docs`, `scripts`, `.github`, agent instruction files; record the hits in research.md R4. Then delete `contracts/company-enums.json` with `tests/RoleRadar.Tests.Contract/CompanyEnumsContractSnapshotTests.cs`, `contracts/application-enums.json` with `ApplicationEnumsContractSnapshotTests.cs`, and `contracts/operational-enums.json` with `OperationalEnumsContractSnapshotTests.cs` (its `supportedEntityRoutes` is a literal with no production source, research R4)
- [X] T016 [US1] Decide `contracts/disposition-matrix.json`: if no consumer reads `dispositions` or `resurfacePolicies` after T011, remove those two arrays and narrow `tests/RoleRadar.Tests.Contract/DispositionMatrixContractSnapshotTests.cs` to the transition table; otherwise keep and say why. `contracts/insights-lenses.json` stays untouched. Write the final per-file decision table into research.md R4
- [X] T017 [US1] Write `docs/adr/0035-generated-enum-contract.md` (context, decision, consequences, OData left as Edm.String per ADR 0028, supersedes the snapshot approach) and add it to `docs/adr/README.md`
- [X] T018 [US1] Amend the "Contract first" bullet in `AGENTS.md` (generation, not snapshots; keep the rule that tasks.md loses to the contract) and every doc or agent file that names a deleted snapshot (`docs/adr/0029-insights-counts-via-shared-lens-module.md` and the hits from T015); confirm `git grep -n "company-enums.json\|application-enums.json\|operational-enums.json"` returns nothing outside `specs/`
- [X] T019 [US1] Scratch evidence, then revert: add a throwaway Domain enum-typed property, run T003 and show it failing with the property named, remove it, show green. Show `git ls-files contracts` output for SC-001

**Checkpoint**: US1 done when T003, T004, T005 green, ApiOpenApiContractSnapshot and ODataMetadataDrift green, typecheck exit 0, snapshots gone.

## Phase 4: User Story 2 - The review gate cannot pass vacuously (Priority: P1)

**Goal**: scan test requires real Trivy reports with the scan's own severity policy; the gate scans its own images and fails on a skipped scan test; tests are selected by lane trait with a coverage test; every lane runs wherever it ran before.

**Independent Test**: seeded finding fails; no report skips under plain `dotnet test`, fails with the require flag or in CI; coverage test red on an unclassified harness-using class; gate prints per-lane counts and the scan result.

### Tests first (show each failing)

- [X] T020 [P] [US2] Write Docker-free evaluator tests in `tests/RoleRadar.Tests.Integration/Hosting/TrivyReportEvaluatorTests.cs` with fixtures built in code (fictional `CVE-0000-0001`, image `roleradar-cli:test`): the evaluator takes the failing severities as a policy value (default `CRITICAL`); a HIGH report passes under `CRITICAL` and fails under `HIGH,CRITICAL`; a CRITICAL report fails under both; HIGH and CRITICAL counts per image are always in the verdict; clean report passes, empty `Results` with valid `ArtifactName` passes, malformed JSON fails, wrong `ArtifactName` fails, missing file fails with `CI` or `ROLERADAR_REQUIRE_SCAN=1` and skips with reason otherwise, no case writes any file (assert the directory is unchanged), and every `--severity <value>` found by a regex in `.github/workflows/ci.yml` and `scripts/review-gate.ps1` equals the evaluator policy value (a regex on `--severity`, not a whole-line comparison). Add a stub `TrivyReportEvaluator` so it compiles; show failing
- [X] T021 [P] [US2] Write the lane coverage test in `tests/RoleRadar.Tests.Integration/TestLaneCoverageTests.cs` per `contracts/test-lanes.md` (unknown lane, lane with zero classes, harness-using class without heavy lane, scripts or workflow still selecting by `FullyQualifiedName~` for these suites). Show failing against the current repo (it must report the classes the old `scripts/test-images.ps1` filter missed; list them in research.md R7)
- [X] T022 [US2] Show the existing vacuous behaviour for the record: run `dotnet test tests/RoleRadar.Tests.Integration -c Release --filter "FullyQualifiedName~ContainerSecurityScanReport"` with no report and with a seeded finding report in `TestResults/` (delete the seeded file afterwards); the current test passes both times

### Spike (decides the severity policy; stop point)

- [X] T023 [US2] Trivy spike: run `aquasec/trivy:0.59.1` with `--ignore-unfixed --severity HIGH,CRITICAL --format json` once against the current five images and fill the counts table in research.md R6 (CRITICAL fixable, HIGH fixable, HIGH unfixed). Run the exact command from Git Bash (with `MSYS_NO_PATHCONV=1` or a `//var/run/docker.sock` form as needed) and from pwsh on this machine, record which form works for both the socket mount and the `TestResults` mount, and the `docker save` plus `trivy image --input` fallback if the socket mount is unreliable. Write output outside the repo or into the ignored `TestResults/`
- [X] T024 [US2] Policy decided by the owner after the T023 spike (22 fixable HIGH, 0 CRITICAL): keep `CRITICAL` with `--ignore-unfixed` in CI and the evaluator; the HIGH findings are not fixed in this feature (board card). Recorded in research.md R6 and `contracts/scan-report-evaluation.md`.

### Implementation

- [X] T025 [US2] Implement `TrivyReportEvaluator` in `tests/RoleRadar.Tests.Integration/Hosting/TrivyReportEvaluator.cs` per `contracts/scan-report-evaluation.md`, with one policy constant (T024) that the CI workflow and gate script values must equal; T020 green
- [X] T026 [US2] Rewrite `ContainerSecurityScanReport_ExistsAndPasses` in `tests/RoleRadar.Tests.Integration/Hosting/ContainerImageContentScanTests.cs` to read `TestResults/trivy-report-{cli,mcp,identity,api,web}.json` through the evaluator, delete the hard-coded report and the `Directory.CreateDirectory` and `File.WriteAllTextAsync` calls, `Assert.Skip` with the explicit reason when reports are absent and neither `CI` nor `ROLERADAR_REQUIRE_SCAN=1` is set, fail otherwise. Re-run T022 steps: a finding at policy now fails, no report skips with reason, require flag fails, `git status` shows no new file
- [X] T027 [US2] CI keeps `--severity CRITICAL` (T024); verify with a read of `.github/workflows/ci.yml` that all five Trivy steps use the same `--severity` value as the evaluator policy and keep the pinned `aquasec/trivy:0.59.1` tag. No CI severity change.
- [X] T028 [US2] Add `[Trait("Lane", "images")]`, `"web-container"` or `"real-stack"` to the Integration classes that need them (every `ContainerTestHarness` user including the container MCP test and the classes found in T021; `WebContainerSmokeTests`; `RealStackEndToEndTests`) under `tests/RoleRadar.Tests.Integration/`
- [X] T029 [US2] Switch selection to traits and keep every lane running wherever it ran before: `scripts/test-images.ps1` (`Lane=images`), `scripts/test-e2e-real.ps1` (`Lane=real-stack`), `.github/workflows/ci.yml` (main step excludes the heavy lanes by trait; add an explicit images lane step after the images are built; keep web-container and real-stack handling as before), and the `ContainerColdStartBenchmarkTests` filter in `.github/workflows/arm64.yml` only if it names a lane class. Keep `--no-build` behaviour
- [X] T030 [US2] Update `scripts/review-gate.ps1`: step 6 runs the default run (`Lane!=scan&Lane!=web-container&Lane!=real-stack`; the images lane is deliberately included, it runs again in image mode in 7c); after step 7 (image build) add (a) a Trivy step scanning the five `roleradar-*:review-<guid>` images with the pinned image and the same flags and policy as CI, writing `TestResults/trivy-report-<target>.json` using the mount form proven in T023, and (b) the images lane with `ROLERADAR_{CLI,MCP,IDENTITY,API,WEB}_IMAGE` set to the review images and `ROLERADAR_REQUIRE_SCAN=1`; step 8 uses `Lane=web-container`; real-stack stays under `-IncludeE2E`. The gate fails a lane that ran zero tests or skipped any (including the scan test), and prints per-lane counts, the scan report count and findings at policy, within 15 summary lines
- [X] T031 [US2] Run the gate steps from T030 end to end on this machine from Git Bash and from pwsh (the Windows docker.sock and MSYS path risk): show the five reports written, the scan test passing with the require flag, and a deliberately removed report making the gate fail. Not done until this has run; if the socket mount is not reliable, switch the step to `docker save` plus `trivy image --input`. T021 green
- [X] T032 [US2] Scratch evidence, then revert: add a harness-using class with no lane, run T021 and show it red naming the class; remove it
- [X] T033 [US2] Write `docs/adr/0036-test-lanes-and-gate-scan.md` (lanes by trait, gate scans its own images, require flag) and add it to `docs/adr/README.md`; amend any doc that lists the old filter fragments (search `Hosting|ShippedHost` across `docs/`, `AGENTS.md`, README)

**Checkpoint**: US2 done when T020 and T021 are green, the scan test fails on a seeded finding and with a missing report under the require flag, the gate output lists the container MCP test inside the images lane count and shows the scan ran, and the Windows run in T031 succeeded.

## Phase 5: User Story 3 - Flakes get diagnosed, not rerun (Priority: P2)

**Goal**: each flake has a fix with test, or a recorded diagnosis with evidence and a confirming observation.

**Independent Test**: read research R8 to R10 final text and run the added tests; 20-run loops show no failure after fixes.

### Evidence (do before fixing; throwaway Docker only)

- [X] T034 [US3] SIGTERM evidence: temporarily instrument `CliContainer_ReceivesSigterm_TerminatesWithinBoundedTimeout` in `tests/RoleRadar.Tests.Integration/Hosting/ContainerHostingConventionTests.cs` to print, on every run, the time `docker run -d` returned, the time `docker kill` returned, and `docker inspect --format '{{json .State}}'` plus container logs after `wait`. Run it 20 times while the rest of the Integration suite runs in parallel (isolated containers only). Record results, which hypothesis (H1 signal before handler, H2 daemon latency, other) the data supports, and the exit codes seen, in research.md R8
- [X] T035 [P] [US3] Visual evidence: run `pnpm --filter web exec playwright test visual-baseline --grep company-detail-errors --repeat-each=20` with CPU throttling, keep any failing actual-versus-expected diff, record the differing region and whether it is a capture race or an app-side shift in research.md R10 (baselines are not rewritten; confirm in the Linux image via `bash scripts/linux-web-check.sh` conventions)
- [X] T036 [P] [US3] Mongo evidence: read the current `MongoContainerStartGateDiagnosticsTests` in `tests/RoleRadar.Tests.Integration/MongoContainerStartGateDiagnosticsTests.cs` and confirm what it asserts; note in research.md R9 any occurrence found in `ROLERADAR_TEST_RESTART_LOG` output if the file exists

### Tests first and fixes

- [X] T037 [US3] Test first for the Mongo gap: extend `MongoContainerStartGateDiagnosticsTests.cs` so a failed second start and a non-"is not running" start failure both carry docker State and the last log lines in the thrown message; show failing
- [X] T038 [US3] Implement in `tests/RoleRadar.Tests.Integration/MongoContainerStartGate.cs`: include `DescribeFailureAsync` output for the second attempt and State for the generic failure path. T037 green
- [X] T039 [US3] SIGTERM fix according to T034 evidence, test first: if H1, change the test to wait for a real condition (container State Running and the process ready, for example a first log line) before `docker kill`, assert exit code 0 or 143, and add a Docker-free check that the CLI host installs its shutdown handling before long work if the evidence points at the app; if H2, measure only kill-to-`FinishedAt`, not host `docker` call latency. No bare timeout increase. Add State and logs to the failure message. If the loop never reproduced a failure, record the evidence and the observation that would confirm the cause instead of changing behaviour. Show 20 consecutive green runs under load
- [X] T040 [US3] Visual fix according to T035 evidence, test first: in `web/tests/e2e/visual-baseline.spec.ts` make the `company-detail-errors` scenario wait for all three inline errors and the banner, the focused element to settle, and two consecutive frames with an unchanged sidebar bounding box, via a shared helper added to the existing `settle` function; if the diff shows an app-side layout shift, fix the app under `web/app/` (never `web/app/ds`) and add a test. Show 20 consecutive green runs
- [X] T041 [US3] Write the final diagnosis or "not reproduced, evidence, confirming observation" note for each of the three flakes into `specs/010-contracts-test-integrity/spec.md` under a new "Flake Records" section (short)

**Checkpoint**: each flake has a passing regression test or a written record.

## Phase 6: User Story 4 - Failed password shows only the password error (Priority: P3)

**Goal**: password step shows only "Invalid username or password".

**Independent Test**: unit and e2e tests red then green.

- [X] T042 [US4] Write failing unit tests in `tests/RoleRadar.Tests.Unit/Identity/LoginModelRazorPageTests.cs`: wrong password yields exactly one ModelState error ("Invalid username or password.") and no `TwoFactorInput.Code` entries; the code step still yields the required and length messages for an empty or wrong-length code. Show failing
- [X] T043 [P] [US4] Write the failing e2e assertion in `web/tests/e2e/identity-signin.spec.ts` (the existing wrong-password step near `input[name="Input.Password"]`): after a wrong password exactly one error is visible and neither "The Code field is required" nor the length message is present
- [X] T044 [US4] Fix `src/RoleRadar.Identity/Pages/Account/Login.cshtml.cs`: when `RequiresTwoFactor` is false, remove the `TwoFactorInput` entries from ModelState before password validation (not a blanket clear, so `Input` field errors survive). T042 and T043 green

## Phase 7: Polish and close

- [X] T045 Search for CRLF in every changed or new file (`git diff --name-only` plus untracked) and convert to LF; confirm `.gitattributes` rules
- [X] T046 Run `dotnet build RoleRadar.sln -c Release -v q --nologo -warnaserror`, `pwsh ./scripts/test-fast.ps1`, `pnpm --filter web run typecheck`, `lint`, `test`, `build`, `test:e2e`, `bash scripts/linux-web-check.sh`, `pre-commit run --all-files`
- [X] T047 Run `pwsh ./scripts/review-gate.ps1` once; report counts from its output only, include per-lane counts and the scan result, and say what could not run
- [X] T048 Verify each success criterion SC-001 to SC-010 in `spec.md` against command output and list any that are not met; do not commit or push; stop at the phase boundary

## Dependencies and order

- US1 (T003 to T019) and US2 (T020 to T033) are independent and can run in parallel; both are P1. Inside US1: T007 to T009 first, then T010, then T011 and T012 (parallel), then T013, then T014 to T019.
- Inside US2: T023 and T024 (spike and policy) come before T025 and T027; T028 before T029 and T030; T030 before T031. T023 can stop the tightening part (T024), not the rest of US2.
- US3 evidence (T034 to T036) can start any time; fixes (T037 to T040) follow their evidence. T040 and T043 both touch `web/tests/e2e/`, different files, so no conflict.
- US4 (T042 to T044) is independent.
- T045 to T048 come last.

## Parallel examples

- US1 tests: T003, T004, T005 together. Web helpers: T011 and T012 together.
- US2 tests: T020, T021 together.
- US3 evidence: T035 and T036 while T034 runs.

## Implementation strategy

MVP is US1 and US2 together (both P1), ending at their checkpoints. Then US3, then US4. Stop for review at each checkpoint if you prefer smaller reviews; stop after T048 in any case.
