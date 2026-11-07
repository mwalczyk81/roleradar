# Research: Contracts and Test Integrity

Evidence below comes from reading the files named in the request. Items marked **Hypothesis** are not yet proven; the tasks include the step that confirms or kills them.

## R1. How enums reach the contract today

- Domain has 16 enums in `src/RoleRadar.Domain/Tracking/TrackingEnums.cs` (OwnershipType, CompanyStatus, CultureLevel, PostingDisposition, ResurfacePolicy, SourceKind, RemoteType, FetchMethod, CriterionCategory, ProvenanceKind, ResumeVariant, ApplicationOutcome, ApplicationEventType, SignalType, ContactRelationship, plus DateResolutionKind). Serialized form is snake_case via `ToSnakeCase()`.
- API DTOs carry them as plain `string`, so `openapi-typescript` emits `string`. The only existing enum in the schema is the stats `window`, added in `src/RoleRadar.Api/OpenApi/ExplicitTypeSchemaTransformers.cs` by `DeclareInsightsWindowEnum` (sets `schema.Enum` from `InsightsWindows.Names`).
- `contracts/package.json` regenerates `client/api-types.ts` with `openapi-typescript 7.10.1`; it will emit string-literal unions once the schema has `enum`.

**Consequence for the registry test**: DTO properties are strings, so reflection over DTOs cannot find "enum properties". The detectable fact is on the Domain side: entity and value-object properties whose type is an enum. The test therefore walks Domain, and every enum-typed property must be registered (with the DTO properties that carry it) or allowlisted with a reason.

## R2. Decision: registry shape and where it lives

**Decision**: `EnumContractRegistry` in `src/RoleRadar.Api/OpenApi/`, an immutable list of entries `{ DomainProperty (type + name), EnumType, CarriedBy: [(DtoType, jsonProperty)] }` plus an `Allowlist` of `{ DomainProperty, Reason }`. The existing schema transformer calls the registry: for the DTO currently being transformed, each registered `(DtoType, jsonProperty)` gets `schema.Enum` from the enum's snake_case names, preserving nullability and array items. The stats `window` special case moves onto the same mechanism only if it fits without behaviour change; otherwise it stays (not required).

**Rationale**: Same mechanism the request names; Api already references Application and Domain (dependency direction holds, no new project). The registry is data, so the test can read it.

**Alternatives**: typing DTO properties as the enums and letting the serializer emit schema enums (rejected: changes wire binding and error behaviour, the handlers deliberately accept strings and return ErrorEnvelope, as the `window` comment explains); a source generator (rejected: ADR-worthy machinery for 5 to 10 properties).

## R3. Decision: which enums to register

**Decision (confirmed)**: the five named enums are mandatory. Register every other Domain enum the API exposes as a string (ResurfacePolicy, ResumeVariant, CriterionCategory are duplicated in web helpers today). Enums the API does not expose go on the allowlist with the reason "not exposed by the API". The registry test makes this list complete either way. "Application status" in the request is the Domain enum `ApplicationOutcome`; the registry and its docs use that name.

## R4. Decision: disposition of each `contracts/` file (provisional, confirmed during implementation)

| File | Content | Decision |
|------|---------|----------|
| `company-enums.json` | CompanyStatus, OwnershipType values | **Deleted** (generated). `CompanyEnumsContractSnapshotTests` deleted; web equality half removed. |
| `application-enums.json` | outcomes (application status), eventTypes, resumeVariants | **Deleted** (generated). `ApplicationEnumsContractSnapshotTests` deleted; its one behavioural assertion (Domain parser round-trips the nine event types, note last) moved to `ApplicationEventTypeRoundTripTests`. |
| `operational-enums.json` | criterionCategories and supportedEntityRoutes | **Kept, trimmed to `criterionCategories`.** CriterionCategory is not exposed by the REST schema (criteria are read through OData), so it is on the registry allowlist and cannot be generated; the snapshot still guards the Domain to web copy (`web/app/lib/operational-enums.ts`). `supportedEntityRoutes` was a literal inside the test with no production source and no snapshot value, so it is removed. |
| `disposition-matrix.json` | disposition list, resurface policies, transition table | **Kept, trimmed to `transitions`.** The two membership arrays are generated now and no production code read them (only the two tests). The transition table is behaviour shared by Domain and web (`TRANSITIONS`). |
| `insights-lenses.json` | lens definitions shared by .NET and web (ADR 0029) | Keep untouched. Definitions, not membership. |
| `client/api-types.ts`, `openapi/roleradar-api.json`, `odata/metadata.xml` | generated artifacts | Keep; regenerate. OData metadata must not change (FR-007). |

Open check in tasks: grep every consumer again before each delete.

Correction to the earlier draft of R3 and R4: `CriterionCategory` is **not** exposed by the REST schema, so it is allowlisted, not registered. The REST API exposes nine registered enum properties (12 DTO properties): PostingDisposition, ResurfacePolicy, SignalType, CompanyStatus, OwnershipType, FetchMethod, ApplicationOutcome, ResumeVariant, ApplicationEventType. The other eleven Domain enum properties are allowlisted with a reason.

## Baseline (recorded before any change, from command output)

- `pwsh ./scripts/test-fast.ps1`: Architecture 46, Unit 613, Contract 211, all passed.
- `pnpm --filter web run typecheck`: no errors.
- `git ls-files contracts`: 9 files (client/api-types.ts, openapi/roleradar-api.json, odata/metadata.xml, package.json, application-enums.json, company-enums.json, disposition-matrix.json, insights-lenses.json, operational-enums.json).
- `ApiOpenApiContractSnapshotTests` and `ODataMetadataDriftTests`: 3 passed.

## R4b. How the contract is produced today (found for T009)

- `contracts/openapi/roleradar-api.json` is generated by `dotnet build src/RoleRadar.Api` (the build writes the committed file). `tests/RoleRadar.Tests.Contract/OpenApi/ApiOpenApiContractSnapshotTests.cs` compares the live `/openapi/v1.json` of a started app with the committed file and also checks explicit types on every schema property.
- `contracts/client/api-types.ts` is generated by `npx --yes openapi-typescript@7.10.1 contracts/openapi/roleradar-api.json -o contracts/client/api-types.ts` (exactly what `.github/workflows/ci.yml` does at line 244, followed by `git diff --exit-code -- contracts`). `contracts/` is not a pnpm workspace package (`pnpm-workspace.yaml` lists only `web`), so `pnpm --filter @roleradar/contracts` does not work; the earlier draft of this plan and quickstart used it by mistake.
- OData: `tests/RoleRadar.Tests.Contract/ODataMetadataDriftTests.cs` already pins `contracts/odata/metadata.xml`. The FR-007 guard reuses it instead of a new test.
- Other tests that read the OpenAPI document and may need updating when `enum` appears: `InsightsStatsOpenApiContractTests.cs`, `PostingRejectionSyncOpenApiTests.cs`.

## R5. OData stays Edm.String (deliberate)

ADR 0028 requires string `$filter` terms to be `tolower()` over a lowercase literal, anything else 400. Turning these into Edm.EnumType would change filter syntax and the 400 behaviour. Only OpenAPI and client types change. The existing `ODataMetadataDriftTests` keeps passing unchanged, and an existing OData filter test keeps passing.

## R6. Scan test: what actually exists

- `ContainerSecurityScanReport_ExistsAndPasses` writes `TestResults/container-scan-report.json` with hard-coded `"passed": true`. Nothing else in the repo reads or produces that file (searched), and its hadolint, sbom and contentScan fields are invented.
- CI (`.github/workflows/ci.yml`) already runs Trivy `0.59.1` for all five images and writes real reports `TestResults/trivy-report-{cli,mcp,identity,api,web}.json`, with `--severity CRITICAL --ignore-unfixed --exit-code 1`.

**Decision**: the test reads the real `trivy-report-*.json` files and applies the same severity policy the scan used. A report is valid only if it parses as Trivy JSON and its `ArtifactName` matches the expected image. Missing reports: fail when `CI` is set or `ROLERADAR_REQUIRE_SCAN=1`, skip with an explicit reason otherwise. The test never writes. Evaluation logic lives in a small `TrivyReportEvaluator` in the Integration test project, with Docker-free tests using seeded fictional reports (a finding at policy, a finding below policy, a clean report, malformed JSON, wrong image, missing file).

**Severity policy (decided at T024 by the owner)**: the failing severities are one policy value, default `CRITICAL`, used identically by the CI Trivy steps (`--severity CRITICAL --ignore-unfixed`), `review-gate.ps1` and `TrivyReportEvaluator`. A test asserts they match by a regex on `--severity <value>` in `.github/workflows/ci.yml` and `scripts/review-gate.ps1`. The evaluator always reports the HIGH and CRITICAL counts per image, and never fails on HIGH under the CRITICAL policy. Tightening later means changing the policy value, the CI and gate `--severity`, and fixing the findings below.

**Spike result (T023, run 2026-11-06 on images built from this working tree, tags `roleradar-<target>:spike-010`, `aquasec/trivy:0.59.1`, Trivy DB built 2026-11-06T18:26Z, flags as CI plus `--severity HIGH,CRITICAL`)**

| Image | CRITICAL (fixable) | HIGH (fixable) | HIGH or CRITICAL unfixed (ignored by `--ignore-unfixed`) |
|-------|-------------------|----------------|--------------------------------------------------------|
| cli | 0 | **12** | 0 |
| mcp | 0 | 0 | 0 |
| identity | 0 | 0 | 0 |
| api | 0 | 0 | 0 |
| web | 0 | **10** | 0 |

Total fixable HIGH: **22**. Total CRITICAL: 0. A second scan without `--ignore-unfixed` found no unfixed HIGH or CRITICAL either, so the ignore flag hides nothing today.

Where they come from:

- **cli (12):** only the two bundled MongoDB tools, `usr/local/bin/mongodump` and `usr/local/bin/mongorestore` (6 findings each): Go `stdlib v1.26.8` (fixed in 1.26.9, 1.26.10, 1.27.2, 1.27.3) and `golang.org/x/net v0.59.0` (fixed in 0.60.0, 0.62.0). The tools come from `mongodb-database-tools-ubuntu2404-*-100.19.1.tgz` downloaded in the `tools-downloader` stage of `Dockerfile`. Nothing in the .NET layers.
- **web (10):** all under `usr/local/lib/node_modules/npm/node_modules/...`, that is the npm that ships inside the `node:22-alpine` base image of `deploy/Dockerfile.web` (runtime stage): brace-expansion 2.0.2 (5 findings), ip-address 10.1.0, pacote 19.0.2 and 20.0.1, picomatch 4.0.3, sigstore 3.1.0. None are application dependencies from the pnpm lockfile.

Evidence files (outside the repo): the scratchpad `spike/fixed/trivy-report-*.json` (CI flags plus HIGH,CRITICAL) and `spike/all/trivy-report-*.json` (no `--ignore-unfixed`).

**Decision (T024): option 1, keep `CRITICAL` with `--ignore-unfixed`.** The 22 fixable HIGH findings are not fixed in this feature; the owner filed them as a board card (cli: mongo-tools Go stdlib and x/net; web: npm inside node:22-alpine). CI severity is unchanged.

**Windows docker.sock evidence (run 2026-11-06 on this machine, Docker Desktop, Linux engine)**

| Form | Result |
|------|--------|
| Git Bash, CI command unchanged (`-v /var/run/docker.sock:/var/run/docker.sock -v "$dir:/TestResults"`) | **Fails**: `docker: Error response from daemon: mkdir C:Program FilesGitar: Access is denied.` (MSYS rewrites the socket path) |
| Git Bash with `MSYS_NO_PATHCONV=1`, socket as `/var/run/docker.sock`, report dir as `$(cd dir && pwd -W)` (the technique `scripts/linux-web-check.sh` already uses) | **Works**: exit 0, valid JSON report, `ArtifactName` `roleradar-cli:test` |
| pwsh, socket as `/var/run/docker.sock`, report dir as a plain Windows path (`C:...`) | **Works**: exit 0, valid JSON report, `ArtifactName` `roleradar-cli:test` |

`review-gate.ps1` runs under pwsh, so its step can use the plain form; any Git Bash caller must set `MSYS_NO_PATHCONV=1` and use `pwd -W`. The `docker save` plus `trivy image --input` fallback is not needed. The spike also used a named volume `trivy-cache-010` for the Trivy DB cache (CI has none), which does not change findings. Note that Trivy printed "Using severities from other vendors for some vulnerabilities"; the report's `Severity` field is what the evaluator will read.

**Gate runs the scan itself (E1)**: `review-gate.ps1` builds the five `roleradar-*:review-<guid>` images in step 7. Add a step after it that runs Trivy (same pinned image, same flags as CI) on those images, writing `TestResults/trivy-report-<target>.json`, then runs the scan test with `ROLERADAR_REQUIRE_SCAN=1` and `ROLERADAR_{CLI,MCP,IDENTITY,API,WEB}_IMAGE` pointing at the review images, so the tests and the scan look at the same images. Plain `dotnet test` without the flag still skips with the reason.

**Windows docker.sock risk**: CI mounts `/var/run/docker.sock`. From Git Bash on Windows, MSYS rewrites a leading `/var/...` path into a Windows path, and the `TestResults` mount path has the same problem. Spike task: run the exact Trivy command from Git Bash and from pwsh on this machine; use `MSYS_NO_PATHCONV=1` or a double slash (`//var/run/docker.sock`) as needed. Fallback if the socket mount is not reliable: `docker save` each image to a tar outside the repo and scan with `trivy image --input <tar>`, which needs no socket. Not done until the gate step has run end to end on this machine.

## R7. Review gate: what drifted

- `review-gate.ps1` step 6 runs `scripts/test-all.ps1`, which runs the whole solution, so the container MCP test does run there. The hand-maintained positive lists are elsewhere: `scripts/test-images.ps1` (`Filter` = Hosting|ShippedHost|StartupSmoke|ContactNoLeak|CliComposition|WebContainer), gate step 8 (`WebContainer`), `scripts/test-e2e-real.ps1` (`RealStackEndToEndTests`), and the CI exclusion `!~WebContainer&!~RealStack`.
- Tests are discovered by name fragments, so a class named outside those fragments is silently skipped by the image run. That is the 008 miss.

**Decision**: classify by xUnit trait instead of name. Add `[Trait("Lane", "<name>")]` (lanes: `images`, `web-container`, `real-stack`; untagged = `default`). Scripts and CI select by `Lane=...` and the default run excludes the heavy lanes by trait. A test (Integration project, reflection only, no Docker) enumerates all test classes and fails when a class uses an unknown lane, when a lane named in a script or workflow has no classes (a lane that would run zero tests), or when a container-harness user is not in the `images` lane. The gate output prints per-lane counts and fails a lane that ran zero tests.

**Where each lane runs (no coverage may drop)**: CI and the gate each run default, images, web-container and real-stack in every place each ran before. Today CI's main step runs the Hosting container tests inside the whole-solution run (`!~WebContainer&!~RealStack`), so after the change CI runs default plus an explicit images lane step, and keeps the existing web-container and real-stack handling. The gate runs default (step 6), then images after the image build and scan (new step), then web-container (step 8), then real-stack with `-IncludeE2E`.

**Image order**: `ContainerTestHarness.EnsureImagesBuiltAsync` builds only the CLI and MCP images, and only if the tag does not exist (`roleradar-cli:test` by default, or `ROLERADAR_CLI_IMAGE`). So order is not irrelevant in the gate: the images lane must run after step 7 with `ROLERADAR_*_IMAGE` set to the review images, otherwise it would test separately built `:test` images and not the ones the gate scanned. In CI the images are pre-built before the lane, as today.

**Alternatives**: keep name filters and add only a coverage test (rejected: still two lists to keep in sync); one `Filter` file read by all scripts (rejected: a manifest still needs a drift test, and traits live next to the code).

Confirming evidence step: list Integration classes and apply the old `test-images.ps1` filter to find the classes it misses (expected to include the two from the 008 review). Record the list here during implementation.

**R7 evidence (T021, T028, run 2026-11-06)**

- The old `scripts/test-images.ps1` filter matched a class only if its full name contained `Hosting`, `ShippedHost`, `StartupSmoke`, `ContactNoLeak`, `CliComposition` or `WebContainer`. Scanning the 84 Integration test classes for the image machinery (`ContainerTestHarness`, `McpProcessClient`, `LaunchedCliRunner`), the classes that use it and were **not** selected by that filter are: `Api.PostingRejectionHostParityTests` (McpProcessClient), `Backup.BackupRestoreWipeIntegrationTests` and `Backup.TrackingBackupSafetyAndCompatibilityTests` (ContainerTestHarness), plus `Containers.ContainerHealthCheckLaunchedHostTests` and `Containers.IdentityImageLoginPageTests` (launched images). `ContainerMcpToolSchemaContractTests` was matched by the old filter only through its namespace (`Hosting`); the review gate never ran image mode at all before this feature.
- The 008 review's "two tests missed" are not named in the repo, so this list is what the scan found; all five are now in the `images` lane.
- Trait filters were checked against the real test count: 696 tests in total; `Lane=images` 92, `Lane=web-container` 1, `Lane=real-stack` 1, `Lane=scan` 1, `Lane=default` 29; the default run (`Lane!=scan&Lane!=web-container&Lane!=real-stack`) selects 693 (696 minus the three heavy single-test lanes).
- The images lane was run in image mode against the five freshly built images (`ROLERADAR_TEST_IMAGE=1`, `scripts/test-images.ps1 ... -Filter "Lane=images" -NoBuild`): **92 passed, 0 skipped, 0 failed, 4 m 6 s**, including the five newly selected classes.
- CI order found while designing this: the CI main test run happens before the images are built and scanned, which is why the scan test needed its own lane to run after Trivy, instead of writing a report to pass.

**R7 gate runs (T031, 2026-11-06, `pwsh ./scripts/review-gate.ps1` from this working tree, Docker Desktop, pwsh)**

- Run 1: steps 1 to 7 passed, the first Trivy scan ran inside the gate (plain `/var/run/docker.sock` mount from pwsh, Windows report path) and wrote its report, then the gate's report-count code crashed under strict mode (a Trivy result entry without `Vulnerabilities`). Fixed by probing the properties and wrapping the count in try/catch; validated against the five spike reports under `Set-StrictMode -Version Latest` (CRITICAL 0, HIGH 22).
- Run 2: Restore PASS, Format PASS, Build PASS, Typecheck PASS, Tests PASS (Unit 613, Arch 46, Contract 215, Integration 693), coverage 92.57%, image builds PASS, **web image and smoke PASS, container scan PASS (5 reports, CRITICAL 0, HIGH in reports 0; the scan test ran with `ROLERADAR_REQUIRE_SCAN=1`: passed 1, skipped 0)**, **images lane FAIL (91 passed, 1 failed, 0 skipped)**. Verdict: GATE FAILURE, from the one image-lane failure below, not from the scan step.
- A report removed on purpose was shown to fail at the test level (`ROLERADAR_REQUIRE_SCAN=1` with no reports: `Missing Trivy report(s), and a real scan is required here`), not by a second full gate run.
- The gate's own cleanup did not run after the crash in run 1, which left five `review-<guid>` images; they were removed by hand.

**A second self-fabricating test (found during T031)**: `ContainerVulnerabilityInventoryTests.TrivyScanReport_CliImage_ExistsAndInventoriesKerberosPackages` synthesized a `TestResults/trivy-report-cli.json` (hard-coded packages, empty `Vulnerabilities`) whenever none existed, at the very path the real scan writes. It left a fake report behind after any default run, which made the new scan test fail locally with "not a Trivy report (no ArtifactName)" (seen on 2026-11-06 21:55 after the images-lane run), and it could only pass. Not named in the request; fixed because it is the same defect and collides with the scan reports: the test is removed from that class, its one unique assertion (the report lists `libgssapi-krb5-2` and `libkrb5-3`) moved into the scan lane where it reads the real report, and `NoTestWritesAScanReport_OnlyTheRealScanDoes` fails when any test source writes a `trivy-report` file (shown red on `Hosting/ContainerVulnerabilityInventoryTests.cs`, then green).

Checked afterwards against five real Trivy reports of this tree (scanned at HIGH,CRITICAL, so they hold 22 HIGH findings): both scan-lane tests pass under the `CRITICAL` policy with `ROLERADAR_REQUIRE_SCAN=1`; with no reports they skip in a plain run and fail with the flag.

## R8. Flake: CLI container SIGTERM (12 s vs 0.6 s)

Facts: the test ran `docker run -d ... image status` with an unreachable Mongo URI and sent `docker kill -s SIGTERM` immediately after `run -d` returned, with no readiness wait (the MCP twin waited a fixed 1 s). It then timed `docker wait` against a 5 s bound.

**Hypothesis H1 (leading)**: the signal can arrive before the .NET runtime has installed its SIGTERM handler. PID 1 in a container ignores signals that have no handler, so SIGTERM is dropped, `docker wait` then waits until `status` ends by itself, which looks like a long, load-dependent duration.

**Hypothesis H2**: the Docker daemon is starved by parallel container starts (the same load the Mongo gate comment describes), so `docker kill` or `docker wait` themselves are slow.

**R8 outcome (T034, T039; run 2026-11-07 on this machine)**

Evidence, replaying the test's exact docker commands with timing (run -d, immediate kill -s SIGTERM, wait):

| Condition | Runs | exit 143 | exit 1 (SIGTERM not handled) | kill to exit, ms |
|-----------|------|----------|------------------------------|------------------|
| idle | 20 | 20 | 0 | 337 to 422 |
| CPU saturated (24 burner containers on 24 cores, 3 parallel replay loops) | 80 | 73 | **7** | 733 to 5995, median 1047 |

- All 7 exit-1 runs had a slow "docker run -d" (3.3 to 9.9 s, median of the other runs 1.3 s): the container was reported started, the immediate SIGTERM reached PID 1 before the process could catch it, PID 1 has no default action for it, so the signal was dropped and the command ran to its own end (exit 1, 1.3 to 5.9 s). H1 is confirmed; H2 (daemon latency) is real but secondary: it only stretches the start-up that exposes H1, and one run spent 4.6 s in "docker wait" (see below).
- The CLI registers its SIGTERM handler as the first statement of Main, so the window is in host and runtime start-up, before any application code. Not an application defect.
- The old test accepted exit code 1 as well as 0 and 143. Six of the seven dropped-signal runs finished inside its 5 s bound, so the old test would have PASSED with SIGTERM ignored: it was racy and vacuous at once. Its 12 s failure was a slower instance of the seventh run (5995 ms).
- The real test did not fail in my own attempts (25 of 25 under load, 12 of 12 with a container-start burst): the failing window is a burst of daemon activity at the moment "docker run -d" is called. The replay reproduced it; the real test did not within 37 tries.

Fix, with tests (readiness probe tests red against a stub, then 13 of 13 green):

- Wait on a real condition: a throw-away helper container that shares the target's PID namespace reads SigCgt in /proc/1/status and the test signals only once the SIGTERM bit (bit 14, 0x4000) is set. The helper image is the ubuntu digest the Dockerfile already pins, and a test keeps the two equal.
- The CLI test now runs "backup --passphrase-stdin", which blocks reading the passphrase from the open stdin and never touches the database, so it cannot end by itself; only a handled SIGTERM ends it. It accepts exit 0 or 143 only. The MCP test uses the same readiness wait instead of a fixed one-second delay.
- The 5 s bound is unchanged, but the stopwatch now starts when "docker kill" returns (the signal has been sent): the one run of 5525 ms after the fix spent 893 ms in the kill call and 4631 ms in the wait, under saturation that no normal run has. The failure messages now carry the container State and the last log lines.
- After: the fixed flow replayed under the same saturation, 80 of 80 exit 143 (the slowest start took 14.7 s to return from "docker run -d", the readiness wait 9 s, and the container still ended in 1.9 s). The real tests, 20 consecutive runs with 24 burner containers plus a fresh container-start burst each time: 20 of 20 passed.

## R9. Flake: Mongo "is not running"

Reading `tests/RoleRadar.Tests.Integration/MongoContainerStartGate.cs`: it already captured docker State, memory, concurrent-start count and the last 40 log lines on the "is not running" path (`DescribeFailureAsync`), logged a visible restart warning, optionally appended to `ROLERADAR_TEST_RESTART_LOG`, and had `MongoContainerStartGateDiagnosticsTests`. The request's premise was therefore mostly done; only two gaps were left: the second-failure message carried the first attempt's diagnostics but not the second's, and the generic path attached logs but not State.

**R9 outcome (T036 to T038)**

- Evidence: MongoContainerStartGateDiagnosticsTests asserted State, memory and log tail only for a healthy container on the first-failure path. ROLERADAR_TEST_RESTART_LOG is not set anywhere in the repo, so no recorded occurrence of the "is not running" failure exists to read; none appeared in the gate or images-lane runs of this feature.
- Gaps found and closed, with tests that were red first (4 of 6 failed against the unchanged messages, 6 of 6 after): the second-failure message now carries the docker State and log tail of both attempts ("First attempt" and "Second attempt"); the generic path now carries the full diagnostics (a mongod that exits on start reports "exited with code 2", which takes that path, and it previously showed a log tail without State); and the diagnostics no longer throw for a container that was never created (an image that cannot be pulled has no id). The retry policy was moved behind two delegates so the "is not running" path is tested on demand instead of waiting for a rare mongod death.
- What would confirm the original cause: the next occurrence now prints docker State (ExitCode, OOMKilled, FinishedAt), memory, concurrent starts and the log tail for both attempts, and appends to ROLERADAR_TEST_RESTART_LOG when that variable names a file.

## R10. Flake: web visual baseline `company-detail-errors`

Facts: `capture()` ran `settle()` (reduced motion, fonts ready, animations finished, two animation frames) then `toHaveScreenshot` full page. The scenario's only explicit wait was for the text "Company name cannot be blank."; the other two inline errors and the banner were not awaited.

**Hypothesis H3**: the capture sometimes happens between commits or while layout is still moving, and the sidebar position (same pixels in dark theme) depends on something that is not stable yet.

**R10 outcome (T035, T040)**

- Evidence: the visual harness is a developer-side tool (it only runs when ROLERADAR_VISUAL_DIR points outside the repo). Under moderate load it did not fail (20 of 20 and 40 of 40 captures). Under extreme saturation (32 busy host processes, 24 burner containers, 4 workers) 2 of the dark captures that completed failed with 2072 differing pixels (the reported failure was 1833): every differing pixel is the sticky sidebar, shifted by about 3 px (brand at y=572 expected, y=575 actual). The 29 other failures were navigation timeouts from the saturated machine.
- Cause (a capture race, not an app defect): the sidebar is position: sticky, and a full-page capture shows it at the current scroll offset. Playwright scrolls to "Save Changes" to click it and the page then reflows around the error banner, so the offset is still settling when the capture starts. The old wait covered fonts, animations and two frames, not scroll or layout.
- Fix, with tests (red first, 3 of 3 failing against the old behaviour, then green): settleForCapture (web/tests/e2e/support/capture-settle.ts) scrolls to the top and waits until the scroll offset, the document height and the shell landmarks are unchanged for four consecutive frames, and refuses to capture (naming the geometry that kept changing) after 240 frames. tests/e2e/capture-settle.spec.ts reproduces the race on demand: a page scrolled down, and a scroll that is still moving. The visual-baseline spec uses it. Local baselines need "--update-snapshots" once, because captures are now taken from the top.
- After, under the same extreme saturation: 0 screenshot mismatches in the 13 captures that completed (27 timeouts from the saturation, no pixel diffs). That is thin on its own (the earlier run had 2 diffs in a similar number of completed captures), which is why the deterministic tests above are the evidence of the mechanism.


**R10 addendum (close-out review, 2026-11-07): effect of the fix on the recorded baselines**

Compared on this machine against a pre-010 baseline re-recorded from the HEAD spec (74 of 74 on a repeat compare; the first compare of the new spec against it failed at company-detail-edit (light) with 2795 px, the serial group then stopped, as reported in review).

- **Sidebar (intended, was not reported):** the old baselines had the sticky sidebar about 224 px down (the scroll offset the click left), the new ones have it at the top. This changes six captures: company-detail-edit and company-detail-errors (light and dark) and insights-drill-link-focus (light and dark). The focus pair differs only in the sidebar (0 px outside it), the focus ring is intact.
- **Regression found and fixed:** `settleForCapture` scrolled to the top after the pointer was positioned, so the insights-drill-link-hover capture showed a tooltip on the Leads bar and a highlighted row. A hover capture now passes `keepScroll`, which waits for stable geometry without scrolling (hover cases: postings-role-link-hover, applications-title-link-hover, insights-drill-link-hover). Test red first (the pointer left the link), then green with the tooltip and chart-row checks. With the fix the hover captures are byte-identical to the pre-010 baseline.
- **Main content of company-detail-edit / company-detail-errors:** the pixels outside the sidebar are two things. (a) company-detail-errors: 4778 px in one block (x 305 to 427, y 1264 to 1303), which is the Save Changes button: the old capture had it in its hover state (the click left the pointer on it, darker brown), the new capture has it at rest, because scrolling to the top moves the content out from under the pointer. That is an incidental hover in the old baseline, not a designed state, so the new capture is the better baseline, but it is a baseline change that is a side effect of the scroll, not of the sidebar fix. (b) both pairs: 8 to 21 scattered px on card edges and corners (rows 475, 1000 to 1034 and the matching rows on the errors page), antialiasing of card borders at a different capture offset. Both are intended in the sense that nothing in the app changed; they are not app regressions.
- Not changed: the default mode still leaves the pointer wherever the last action put it. Moving it to a fixed neutral point before capturing would make the default mode independent of the page position, at the cost of possibly changing more baselines.

## R11. Login: failed password lists TOTP messages

`src/RoleRadar.Identity/Pages/Account/Login.cshtml.cs` binds both `Input` and `TwoFactorInput`. `TwoFactorModel.Code` is `[Required][StringLength(7, MinimumLength = 6)]`, so on the password step model binding already put "The Code field is required" and the length error into ModelState. The failed-password branch adds its own error to a ModelState that still holds those, and the page lists every ModelState error. The `RequiresTwoFactor` success path avoids it with `ModelState.Clear()`.

**Decision**: when not in the two-factor step, remove the code entries from ModelState before adding errors (not a blanket clear, so `Input` field errors survive). Tests first in `tests/RoleRadar.Tests.Unit/Identity` and in the identity-signin e2e.

**R11 outcome (T042 to T044)**

- Tests were red first: 3 of 4 new unit tests failed with the code messages listed under the password error, and the new identity-signin e2e failed with 2 extra lines in the alert.
- A first fix that removed keys starting with "TwoFactorInput" passed the unit tests but **failed the e2e**: a temporary diagnostic in the real page showed the key the binder actually uses on the password step is plain "Code" (the form posts no TwoFactorInput field, so MVC falls back to the empty prefix), not "TwoFactorInput.Code". My unit test had used the wrong key, so it passed while the page did not. The unit tests now use the real key (and the prefixed key as a second case), went red again, and the fix now drops "Code", "TwoFactorInput" and "TwoFactorInput.*". Result: Login unit tests 6 of 6, identity-signin e2e 22 of 22 including the new test.
- The code step keeps its own field messages (a guard test, passing before and after). Not changed, and worth knowing: the mirror situation on the code step (the posted form has no Username or Password, so the same empty-prefix fallback probably lists "The Username field is required" there) was not part of the request and was not tested, since it needs an owner with two-factor enabled.

## R12. ADR number

Last ADR is `0034-identity-serves-kit-assets-and-csp.md`. Two ADRs (confirmed): **0035 Generated enum contract replaces snapshots** and **0036 Test lanes by trait, with the gate running the scan**.

## R13. Docs that quote "Contract first"

`AGENTS.md` quotes it. A repo search found no other markdown file with that exact phrase, but ADR 0029 and CLAUDE-facing docs reference the snapshot files by name; tasks include a search for each deleted filename across `docs/`, `README`, and agent instruction files and amending every hit.

## R14. Newly surfaced flake: BackupRestoreWipeIntegrationTests.Backup_WhenCancelled_TerminatesEntireProcessTree

Seen once in the gate's images lane (2026-11-06, run 2): "Expected child PID file to be created by test process." after the test's own 5000 ms wait. The test spawns a host python as the dump command and polls for the PID file it writes.

**R14 outcome (T041, flake 4)**

- Does the test need an image at all? No. The class (BackupRestoreWipeIntegrationTests) calls ContainerTestHarness once, for the static ValidateDatabaseNameForDrop, and otherwise runs host processes against a Testcontainers Mongo (it uses "docker exec" into that Mongo for mongodump). It was put in the images lane by a text match in US2, which was wrong, and the same is true of TrackingBackupSafetyAndCompatibilityTests. Both are now untagged, and a new check (EveryImagesLaneClass_ReallyUsesTheImages) fails for a class in the images lane that never uses the images (shown red on exactly those two, then green); the "uses the images" definition is now the real calls, not the word "ContainerTestHarness". The images lane is 58 tests (it was 91). This also corrects the US2 statement that five classes were missed: three were (PostingRejectionHostParityTests, ContainerHealthCheckLaunchedHostTests, IdentityImageLoginPageTests).
- Elapsed-time loop (instrumented throw-away replica, since removed): 30 runs at idle, 40 under extreme load (32 busy host processes, 24 burner containers). Process spawn about 1 ms (max 162 ms under load), interpreter start about 30 ms (max 441 ms), PID file visible after a median 67 ms idle and 180 ms loaded, **maximum 489 ms** against the test's 5000 ms budget. A slow child does not explain a 5 s miss. The whole class passed 5 of 5 in the gate's image-mode environment (ROLERADAR_TEST_IMAGE=1 and the image variables), and the test alone 8 of 8.
- The failure could not be reproduced. The remaining hypothesis: the backup ended before it launched python (the dump command could not be started, or an early exception), because the test only polled for the file and never looked at the backup task, so it reported the same bare message for "slow" and "never launched". Shown with a dump command that fails at once: the old test waited the whole 5 s and said only "Expected child PID file to be created by test process."
- Fix, with tests (4 of 4 red against a stub, then green): BackupProcessWait watches the backup task while it waits and reports its outcome at once (faulted with the exception, completed without a child, or still running with the elapsed time and status). With the failing command the message is now "the backup task completed after 61 ms without any child having written the PID file".
- What would confirm the cause: the next occurrence now says which of the three it was. If it says "completed ... without any child", the next step is to capture the backup result (stderr of the dump command) for that run.
