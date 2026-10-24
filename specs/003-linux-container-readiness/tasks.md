# Tasks: Linux Container Readiness for Azure-Style Hosting

**Input**: Design documents from `/specs/003-linux-container-readiness/`  
**Prerequisites**: [plan.md](./plan.md) (required), [spec.md](./spec.md) (required for user stories), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)  
**Tests**: Tests are MANDATORY per Constitution Principle II (Test-First Development). For every user story, test tasks MUST come first, be marked as expected to fail, and precede all implementation tasks.  
**Organization**: Tasks are grouped by user story (`US1`–`US7`) to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (`[US1]`, `[US2]`, `[US3]`, `[US4]`, `[US5]`, `[US6]`, `[US7]`)
- Every task cites the specific Functional Requirements (`[FR-xxx]`) and Success Criteria (`[SC-xxx]`) it satisfies.
- Every task includes exact repository-relative file paths.

---

## Phase 1: Setup (Traceability, Compose Overlays, Dependabot & Supply-Chain Governance)

**Purpose**: Establish requirements traceability automation, deny-by-default Docker build filtering, vulnerability allowlist schema, compose profiles overlay, and Dependabot Docker coverage.

- [x] T001 Implement automated requirements traceability verification script `scripts/verify-traceability-003.ps1` parsing `specs/003-linux-container-readiness/spec.md` to extract all 30 requirements (FR-001 through FR-030) and 8 success criteria (SC-001 through SC-008), verifying each is cited in `tasks.md`, each maps to a test or CI job, and all referenced files exist on disk [FR-001..FR-030, SC-001..SC-008]
- [x] T002 [P] Create repository deny-by-default `.dockerignore` file per `contracts/dockerfile-build-contract.md` strictly excluding sensitive directories (`import-review/`, `private/`, `db/`), credentials (`*.env`), backup archives (`*.rrbak`, `*.dump`), build artifacts (`bin/`, `obj/`, `dist/`), and git history (`.git/`) [FR-023, SC-001]
- [x] T003 [P] Create container vulnerability scanning allowlist `.trivy-allowlist.yaml` per `specs/003-linux-container-readiness/data-model.md` defining schema for CVE ID, package name, version, severity, fix availability, expiration date, and architectural justification [FR-022, SC-002]
- [x] T004 [P] Create Docker Compose profiles overlay `deploy/docker-compose.apps.yml` per `contracts/wrapper-scripts-contract.md` defining `roleradar-cli` and `roleradar-mcp` services under profiles `cli` and `mcp`, connecting to `roleradar_default` network with `directConnection=true` [FR-001, FR-005]
- [x] T005 [P] Create Dependabot configuration `.github/dependabot.yml` adding `docker` package-ecosystem entries covering the repository root Dockerfile (`/`) and Docker Compose files (`/deploy`), with a test or script asserting the Dockerfile is covered [FR-020, SC-002]

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Living architecture tests enforcing that `RoleRadar.Domain` and `RoleRadar.Application` contain zero references to container/hosting/Docker types and maintain the 12-tool cap, architecture and unit tests enforcing UTC timestamps, pure unit-tested `ExternalMongoSafetyGuard` with exhaustive 11-case table, shared container test harness executing under `--read-only --tmpfs /tmp --cap-drop ALL --security-opt no-new-privileges`, and cross-platform script path helpers.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Foundational Tests (MANDATORY - Test-First per Principle II & III) ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [x] T006 [P] Create living architecture test in `tests/RoleRadar.Tests.Architecture/ContainerReadinessArchitectureTests.cs` asserting that `RoleRadar.Domain` and `RoleRadar.Application` projects have zero references to container, hosting, or Docker types, exactly 12 MCP tools are registered with zero tool additions, strict inward layering is preserved, and banning `DateTime.Now`, `DateTime.Today`, `DateTime.ToLocalTime()`, and `TimeZoneInfo.Local` across all source code in `src/` [FR-004, FR-012, SC-004]
- [x] T007 [P] Write failing unit tests for cross-platform script path helpers and denylist resolution in `tests/RoleRadar.Tests.Unit/Scripts/ScriptPortabilityTests.cs`, asserting that Windows resolution equals `%LOCALAPPDATA%\roleradar\private\denylist.txt` byte-for-byte, macOS/Linux resolves `${XDG_DATA_HOME:-$HOME/.local/share}/roleradar/private/denylist.txt`, and the guard fails closed when the file is missing [FR-017, FR-018, SC-001]
- [x] T008 [P] Write failing pure unit tests for `ExternalMongoSafetyGuard` in `tests/RoleRadar.Tests.Contract/ExternalDatabaseGuardUnitTests.cs` implementing the complete 11-case decision table from `contracts/external-database-guard-contract.md`:
  - `mongodb+srv://cluster0.example.mongodb.net/` -> Allowed (SRV implies TLS)
  - `mongodb+srv://cluster0.example.mongodb.net/?tls=false` -> Rejected (`TlsRequiredException`)
  - `mongodb://host.docker.internal:27017/?tls=false` -> Rejected (`TlsRequiredException`, non-loopback)
  - `mongodb://127.0.0.2:27017` -> Allowed (loopback `127.0.0.0/8`, TLS optional)
  - `mongodb://[::1]:27017` -> Allowed (IPv6 loopback `::1`, TLS optional)
  - `mongodb://localhost:27017` -> Allowed (loopback `localhost`, TLS optional)
  - `mongodb://remote-db.example.com:27017/?tls=false&ssl=true` -> Rejected (`ConflictingTlsParametersException`)
  - `mongodb://remote-db.example.com:27017/?tls=true` -> Allowed (remote with TLS)
  - `mongodb://remote-db.example.com:27017` -> Rejected (`TlsRequiredException`)
  - Remote cluster where user lacks `listDatabases` privilege -> Rejected with clear redacted message (zero secret or host leak)
  - Target server containing non-test database `production_db` -> Rejected (`ForeignDatabaseDetectedException`) [FR-013, FR-014, SC-005]
- [x] T009 [P] Write failing process-level startup smoke tests in `tests/RoleRadar.Tests.Integration/Hosting/ContainerStartupSmokeTests.cs` verifying that both CLI and MCP entrypoints launch, print version/help, log exclusively to stderr, and exit cleanly when invoked via container test harness [FR-001, FR-003, FR-004, SC-004]

### Foundational Implementation

- [x] T010 [P] Implement pure `ExternalMongoSafetyGuard` component in `tests/RoleRadar.Tests.Contract/Fixtures/ExternalMongoSafetyGuard.cs` using MongoDB C# driver's URL parser (`new MongoUrl(connectionString)`), loopback subnet matcher (`127.0.0.0/8`, `::1`, `localhost`), permission exception interceptor, and thread-safe test database registry (`TrackedTestDatabases`) (depends on T008 failing first) [FR-013, FR-014, FR-015, SC-005]
- [x] T011 [P] Implement shared container test execution harness in `tests/RoleRadar.Tests.Integration/Hosting/ContainerTestHarness.cs` supporting building container images once per test session (or reusing CI tag via `ROLERADAR_IMAGE_TAG`), executing `docker run` with exact flags `--read-only --tmpfs /tmp:rw,noexec,nosuid,size=64m --cap-drop ALL --security-opt no-new-privileges`, and streaming stderr/stdout separately (depends on T009 failing first) [FR-002, FR-003, FR-026, SC-004]
- [x] T012 [P] Refactor every `scripts/*.ps1` (backup.ps1, restore.ps1, wipe-data.ps1, start-services.ps1, test-all.ps1, test-fast.ps1, denylist-check.ps1, verify-action-pins.ps1, verify-traceability.ps1, plus any new scripts) to use `Join-Path`, forward-slash paths, and cross-platform denylist discovery (`$env:LOCALAPPDATA\roleradar\private\denylist.txt` on Windows, `${XDG_DATA_HOME:-$HOME/.local/share}/roleradar/private/denylist.txt` on Unix) (depends on T007 failing first) [FR-017, FR-018, SC-001]

**Checkpoint**: Foundation ready — architecture tests, script portability unit tests, safety guard unit tests, and container test harness compile and pass. User story work can begin.  
*Commit*: `feat(foundation): add container test harness, safety guard, and cross-platform path helpers`

---

## Phase 3: User Story 1 - Secure Linux Container Images for CLI and MCP Host (Priority: P1) 🎯 MVP

**Goal**: Deliver hardened, multi-stage Linux container images for `roleradar-cli` and `roleradar-mcp` (stdio MCP) targeting `linux/amd64` and `linux/arm64` using official .NET 10 chiseled distroless base images (zero shell, zero package manager, non-root UID 1654), with Database Tools 100.19.1 and extracted Kerberos shared libraries bundled in the CLI image, buildable reproducibly with a single command.

**Independent Test**: Build images with single `docker build` commands for `cli` and `mcp` targets; execute CLI `status` and MCP stdio handshake under `docker run --read-only --tmpfs /tmp --cap-drop ALL --security-opt no-new-privileges`; verify non-root UID 1654, zero listening ports, and cold-start response under 3.0 seconds.

### Tests for User Story 1 (MANDATORY - Test-First per Principle II) ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [x] T013 [P] [US1] Write failing container image metadata tests in `tests/RoleRadar.Tests.Integration/Hosting/ContainerImageMetadataTests.cs` verifying non-root UID `1654`, zero listening network ports (`EXPOSE`), no shell (`/bin/sh` and `/bin/bash` absent), and no package manager (`apt` and `dpkg` absent) in both CLI and MCP container images under `docker run --read-only --tmpfs /tmp` (expected to fail before implementation) [FR-001, FR-003, FR-004, SC-004]
- [x] T014 [P] [US1] Write failing cold-start benchmark test in `tests/RoleRadar.Tests.Integration/Hosting/ContainerColdStartBenchmarkTests.cs` asserting elapsed wall-clock time from `docker run` to first valid JSON-RPC response is strictly under 3.0 seconds with pre-pulled images and healthy MongoDB on both amd64 and native arm64 (expected to fail before implementation) [FR-030, SC-003]

### Implementation for User Story 1

- [x] T015 [US1] Create root `Dockerfile` per `contracts/dockerfile-build-contract.md` defining multi-stage build targets:
  - `build`: SDK 10.0.402 pinned by digest, locked mode package restore, framework-dependent publish
  - `tools-downloader`: Ubuntu 24.04 stage downloading MongoDB Database Tools 100.19.1 for target architecture, re-verifying SHA-256 against `full.json` (amd64: `f34b84a67ab96b4ff52571ec5d15e2148d96cb140b1ea9ef8c29ac33c48e3480`, arm64: `ebf518d9a303b36e612e40be106cd0ecc7135dc15d098626f81ac3ee8654a821`), extracting `bin/mongodump`, `bin/mongorestore`, installing `libgssapi-krb5-2` and isolating the 6 shared libraries and their dpkg status entries
  - `mcp`: Runtime `mcr.microsoft.com/dotnet/runtime:10.0-noble-chiseled` pinned by multi-arch digest, UID 1654, zero ports, entrypoint `RoleRadar.Mcp.Stdio`
  - `cli`: Runtime `mcr.microsoft.com/dotnet/runtime:10.0-noble-chiseled` pinned by multi-arch digest, UID 1654, copying tools, shared libraries, and dpkg status entries, zero ports, entrypoint `RoleRadar.Cli` (depends on T013, T014 failing first) [FR-001, FR-002, FR-003, FR-006, FR-020, SC-004]
- [x] T016 [US1] Implement cross-platform PowerShell container execution wrapper `scripts/roleradar-docker.ps1` per `contracts/wrapper-scripts-contract.md` providing unified `--cli` and `--mcp` execution forwarding arguments to `docker run --read-only --tmpfs /tmp:rw,noexec,nosuid,size=64m --cap-drop ALL --security-opt no-new-privileges` [FR-001, FR-003, FR-017]

**Checkpoint**: User Story 1 complete — `roleradar-cli` and `roleradar-mcp` container images build reproducibly, metadata tests pass, cold start < 3.0s verified.  
*Commit*: `feat(docker): add multi-stage Dockerfile and roleradar-docker wrapper script`

---

## Phase 4: User Story 2 - Hosting-Readiness Conventions Enforced by Automated Verification (Priority: P1)

**Goal**: Guarantee 100% environment-based configuration, strict stderr diagnostic logging with unpolluted stdout, graceful bounded `SIGTERM` shutdown, strict UTC timestamps across all entities, and complete credential/connection-string masking in all outputs.

**Independent Test**: Execute process-level hosting tests verifying zero local config file dependencies, zero diagnostic text on stdout, clean process exit upon `SIGTERM` within grace period, UTC time enforcement with `TZ=America/Chicago`, and sanitized connection strings in exceptions.

### Tests for User Story 2 (MANDATORY - Test-First per Principle II) ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [x] T017 [P] [US2] Write failing hosting convention tests in `tests/RoleRadar.Tests.Integration/Hosting/ContainerHostingConventionTests.cs` verifying:
  - Application operates with 100% environment variable configuration and fails gracefully if local config files are absent
  - Diagnostic and log output written strictly to stderr; stdout pure for MCP JSON-RPC frames and CLI output
  - Process intercepts `SIGTERM`, initiates graceful teardown, and terminates within bounded timeout
  - All generated timestamps across all entities and audit records are strictly UTC when executed under non-UTC host environment (`TZ=America/Chicago`)
  - Database connection strings, passwords, and tokens are completely masked in logs, diagnostics, and traces (expected to fail before implementation) [FR-005, FR-010, FR-011, FR-012, FR-029]

### Implementation for User Story 2

- [x] T018 [US2] Refactor host bootstrapping in `src/RoleRadar.Cli/Program.cs` and `src/RoleRadar.Mcp.Stdio/Program.cs` to ensure console logging routes strictly to stderr, configuration reads exclusively from environment variables, `PosixSignalRegistration` handles `SIGTERM`/`SIGINT` for bounded graceful cancellation, UTC timestamps are enforced in all formatters, and connection string credentials are masked in all unhandled exception handlers (depends on T017 failing first) [FR-005, FR-010, FR-011, FR-012, FR-029]

**Checkpoint**: User Story 2 complete — all hosting-readiness conventions enforced and passing.  
*Commit*: `feat(hosting): enforce environment configuration, stderr logging, SIGTERM handling, and credential masking`

---

## Phase 5: User Story 3 - Self-Contained Database Backup, Restore, and Wipe Inside the CLI Container (Priority: P1)

**Goal**: Execute database backup, restore, and wipe commands entirely inside the CLI container against any reachable MongoDB host using bundled Database Tools 100.19.1, creating temporary `--config` files atomically with mode `0600` on tmpfs, guaranteeing zero password exposure on argv/logs/files after exit, supporting all 12 collections and 001-format legacy archive restores.

**Independent Test**: Run `roleradar backup`, `restore`, and `wipe` inside the CLI container (`docker run --read-only --tmpfs /tmp`) against compose MongoDB; verify encrypted backup produced, 12 collections restored, 001 archive restored with `entity_type: "note"`, credentials never leaked, and temp config mode is 0600 on first read.

### Tests for User Story 3 (MANDATORY - Test-First per Principle II) ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [x] T019 [P] [US3] Write failing in-container credentials security test in `tests/RoleRadar.Tests.Integration/Hosting/ContainerCredentialsSecurityTests.cs` asserting:
  - Temporary config file `/tmp/rr-tools-<uuid>.conf` has POSIX file mode exactly `0600` at instant of first read
  - Database password NEVER appears in `argv` (`/proc/<pid>/cmdline`), `stdout`, `stderr`, logs, or audit entries
  - No config files remain under `/tmp` after process exit, including when the child is killed by `SIGTERM` and on process failure (expected to fail before implementation) [FR-003, FR-006, FR-011, SC-007]
- [x] T020 [P] [US3] Write failing containerized backup, restore, and wipe integration tests in `tests/RoleRadar.Tests.Integration/Hosting/ContainerBackupRestoreTests.cs` executing `backup`, `restore`, and `wipe --yes` inside `roleradar-cli` under `docker run --read-only --tmpfs /tmp` against compose MongoDB covering all 12 collections without host Docker socket (expected to fail before implementation) [FR-006, FR-007, FR-008, FR-009, SC-007]
- [x] T021 [P] [US3] Write failing legacy 001-format backup restore test in `tests/RoleRadar.Tests.Integration/Hosting/ContainerLegacyBackupCompatibilityTests.cs` verifying that a 001-format archive restores inside the CLI container with audit entries reading as `entity_type: "note"`, tracking indexes created after restore, and dry-run reporting unchanged (expected to fail before implementation) [FR-008, SC-007]

### Implementation for User Story 3

- [x] T022 [US3] Update MongoDB Database Tools invocation in `src/RoleRadar.Infrastructure/Backup/MongoDatabaseToolsBackupDriver.cs` (or CLI backup handler) to create temporary config file atomically using .NET `FileStreamOptions { UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite }` with `FileMode.CreateNew`, invoke `mongodump`/`mongorestore` with `--config={configPath}`, and guarantee immediate unlinking in `finally` block and `SIGTERM`/`ProcessExit` handlers (depends on T019, T020, T021 failing first) [FR-006, FR-007, FR-008, FR-009, SC-007]
- [x] T023 [US3] Update backup and restore helper scripts (`scripts/backup.ps1`, `scripts/restore.ps1`, `scripts/wipe-data.ps1`) to support container execution via `scripts/roleradar-docker.ps1` with mounted backup directory [FR-007, FR-008, FR-009, SC-007]

**Checkpoint**: User Story 3 complete — in-container backup, restore, wipe, 001 restore, and atomic credential security verified.  
*Commit*: `feat(backup): bundle Database Tools in CLI container with atomic 0600 config and 12-collection coverage`

---

## Phase 6: User Story 4 - Launched-Host Test Parity Against Container Images (Priority: P1)

**Goal**: Execute the complete suite of launched-host tests against the built Linux container images on both `linux/amd64` and native `linux/arm64` runners under `docker run --read-only --tmpfs /tmp`, proving identical behavior to native binaries for concurrency, contact privacy, and tool-schema contracts.

**Independent Test**: Execute concurrency race tests, contact privacy no-leak scans, 12-tool schema contracts, and the full existing test suite against container images, asserting 100% pass rate without emulation.

### Tests for User Story 4 (MANDATORY - Test-First per Principle II) ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [x] T024 [P] [US4] Write containerized concurrency race tests in `tests/RoleRadar.Tests.Integration/Hosting/ContainerConcurrencyRaceTests.cs` executing parallel container processes under `docker run --read-only --tmpfs /tmp` asserting search run logging unique constraint and posting deduplication idempotency under race conditions (expected to fail before implementation) [FR-026, SC-004]
- [x] T025 [P] [US4] Write containerized contact privacy no-leak scan tests in `tests/RoleRadar.Tests.Integration/Hosting/ContainerContactPrivacyNoLeakTests.cs` verifying that confidential contact details never appear in stdout, stderr, logs, or unencrypted backup archives during container operations (expected to fail before implementation) [FR-026, SC-001, SC-004]
- [x] T026 [P] [US4] Write containerized 12-tool schema contract tests in `tests/RoleRadar.Tests.Integration/Hosting/ContainerMcpToolSchemaContractTests.cs` verifying tool count is exactly 12 with identical schemas and descriptions over the container stdio session (expected to fail before implementation) [FR-004, FR-026, SC-004]

### Implementation for User Story 4

- [x] T027 [US4] Update `tests/RoleRadar.Tests.Integration/Tracking/McpProcessClient.cs` to support invoking the MCP server via `docker run --read-only --tmpfs /tmp` and implement `scripts/test-images.ps1` to run the full existing test suite unchanged against the built container images (depends on T024, T025, T026 failing first) [FR-002, FR-026, SC-004]

**Checkpoint**: User Story 4 complete — launched-host test parity proven across container images on amd64 and arm64.  
*Commit*: `test(images): extend launched-host test suites to verify container images under read-only tmpfs`

---

## Phase 7: User Story 5 - Hosted Database Compatibility Matrix & Validation Mode (Priority: P2)

**Goal**: Provide an external database compatibility test mode for the contract test suite, requiring explicit opt-in (`ROLERADAR_EXTERNAL_TEST_OPT_IN=1`), driver-based TLS validation, `rrtest_<guid>` prefix guard, and guaranteed cleanup, generating a structured pass/fail capability matrix as JSON and markdown.

**Independent Test**: Run contract tests with opt-in against local MongoDB replica set; verify safety guard rejects foreign databases, allows `rrtest_*`, tracks created databases, drops only tracked databases on completion or `SIGTERM`, and outputs sanitized JSON and markdown capability matrix reports.

### Tests for User Story 5 (MANDATORY - Test-First per Principle II) ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [x] T028 [P] [US5] Write failing compatibility matrix runner tests in `tests/RoleRadar.Tests.Contract/HostedDatabaseCompatibilityMatrixTests.cs` verifying execution against a local MongoDB instance (with `ROLERADAR_EXTERNAL_TEST_OPT_IN=1`), asserting cross-collection transactions, partial unique indexes, and text search, and validating emitted JSON and markdown reports in `TestResults/database-capability-matrix.json` and `.md` with zero connection string leaks (expected to fail before implementation) [FR-013, FR-014, FR-015, FR-016, SC-005]

### Implementation for User Story 5

- [x] T029 [US5] Implement `DatabaseCapabilityMatrixReporter` in `tests/RoleRadar.Tests.Contract/Fixtures/DatabaseCapabilityMatrixReporter.cs` formatting empirical test results into JSON and Markdown matching schemas in `specs/003-linux-container-readiness/data-model.md` sanitized of connection strings, hosts, and credentials (depends on T028 failing first) [FR-016, SC-005]
- [x] T030 [US5] Wire `ExternalMongoSafetyGuard` and `DatabaseCapabilityMatrixReporter` into `tests/RoleRadar.Tests.Contract/Fixtures/SharedMongoFixture.cs` to activate when `ROLERADAR_EXTERNAL_TEST_OPT_IN=1`, recording tracked databases and generating capability matrix upon fixture teardown [FR-013, FR-014, FR-015, FR-016, SC-005]

**Checkpoint**: User Story 5 complete — external database compatibility test mode and capability matrix generator operational.  
*Commit*: `feat(compat): implement external database safety guard and capability matrix reporter`

---

## Phase 8: User Story 6 - Cross-Platform Automation & Multi-OS CI Verification (Priority: P2)

**Goal**: Deliver cross-platform automation scripts operating cleanly on Windows, macOS, and Linux, and establish GitHub Actions CI workflows tailored to runner capabilities, including native ARM64 verification on `ubuntu-24.04-arm`.

**Independent Test**: Execute all repository scripts under Pester across Windows and Linux/macOS; execute CI workflow jobs asserting full suite on Ubuntu Linux, native ARM64 image test on `ubuntu-24.04-arm`, and unit/arch/portability tests on Windows and macOS.

### Tests for User Story 6 (MANDATORY - Test-First per Principle II) ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [x] T031 [P] [US6] Write failing Pester tests in `tests/RoleRadar.Tests.Unit/Scripts/ScriptPortability.Tests.ps1` invoking all repository scripts under PowerShell Core on Linux/macOS and Windows, asserting zero path separator or backslash failures [FR-017, SC-006]

### Implementation for User Story 6

- [x] T032 [US6] Create multi-OS GitHub Actions workflow in `.github/workflows/ci.yml` defining:
  - `ubuntu-build-and-test` on `ubuntu-latest`: Full build, hadolint, trivy scan, SBOM, launched-host tests against images, compose integration, external DB mode, coverage gate (>=85%)
  - `linux-arm64-verify` on native `ubuntu-24.04-arm` runner: Native multi-stage build, native launched-host test execution under `docker run --read-only --tmpfs /tmp`, cold-start timing (<3.0s)
  - `windows-verify` on `windows-latest`: Build, format, lint, unit tests, architecture tests, PSScriptAnalyzer
  - `macos-verify` on `macos-latest`: Build, format, lint, unit tests, architecture tests, Pester script portability tests (depends on T031 failing first) [FR-002, FR-019, SC-006]
- [x] T033 [US6] Implement Database Tools update check script `scripts/check-tools-release.ps1` reading `full.json` for all historical versions and `release.json` for the latest stable release, comparing with Dockerfile pin and outputting update status [FR-006, SC-002]

**Checkpoint**: User Story 6 complete — cross-platform scripts and multi-OS CI workflow matrix established.  
*Commit*: `ci(workflows): add multi-OS GitHub Actions workflow with native ubuntu-24.04-arm runner`

---

## Phase 9: User Story 7 - Supply Chain Hardening & Container Vulnerability Governance (Priority: P2)

**Goal**: Enforce immutable base image digest pinning, `hadolint` Dockerfile linting on Linux, Trivy vulnerability scanning with allowlist governance, SBOM generation, container layer privacy scanning, and SHA-pinned GitHub Actions verified via `zizmor`.

**Independent Test**: Execute `hadolint`, run Trivy scan asserting zero unapproved critical vulnerabilities and that copied Kerberos packages appear in scan output, scan image layers for private data, verify SBOM generation, and verify action SHA pins.

### Tests for User Story 7 (MANDATORY - Test-First per Principle II) ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [x] T034 [P] [US7] Write failing container image content scan tests in `tests/RoleRadar.Tests.Integration/Hosting/ContainerImageContentScanTests.cs` extracting container layer filesystems and history to assert zero denylist terms, secrets, `.git`, `.env`, or sensitive build artifacts exist in either image (expected to fail before implementation) [FR-023, FR-024, SC-001]
- [x] T035 [P] [US7] Write failing vulnerability scanner package inventory test in `tests/RoleRadar.Tests.Integration/Hosting/ContainerVulnerabilityInventoryTests.cs` asserting that Trivy scan report explicitly lists the Kerberos packages (`libgssapi-krb5-2`, `libkrb5-3`, `libk5crypto3`, `libkrb5support0`, `libkeyutils1`) with exact versions and zero unindexed binaries (expected to fail before implementation) [FR-006, FR-022, SC-002]

### Implementation for User Story 7

- [x] T036 [US7] Configure Dockerfile linting (`hadolint`) step and Trivy container vulnerability scanning in `.github/workflows/ci.yml` enforcing zero critical vulnerabilities with available fixes and verifying against `.trivy-allowlist.yaml` (depends on T034, T035 failing first) [FR-020, FR-021, FR-022, SC-002]
- [x] T037 [US7] Configure Syft/Trivy SBOM generation in `.github/workflows/ci.yml` producing SPDX/CycloneDX JSON SBOM artifacts for both images [FR-025]
- [x] T038 [US7] Pin every GitHub Action in `.github/workflows/*.yml` to immutable commit SHAs, and run `scripts/verify-action-pins.ps1` and `zizmor` pre-commit hook to verify zero supply chain or workflow security warnings [FR-019, SC-006]

**Checkpoint**: User Story 7 complete — supply chain hardened, vulnerability scanning active, zero private data in images.  
*Commit*: `ci(security): configure hadolint, trivy scanning, SBOM generation, and action SHA pinning`

---

## Phase 10: Polish, Traceability & Validation

**Purpose**: Verify end-to-end requirement traceability mapping every FR and SC to a task and test, and validate clean-machine quickstart workflow.

- [ ] T039 Execute requirements traceability verification script `scripts/verify-traceability-003.ps1` asserting that all 30 requirements (FR-001 through FR-030, including ADR records FR-027 and FR-028) and 8 success criteria (SC-001 through SC-008) are cited in tasks.md, each maps to a test or CI job, and all referenced test files exist on disk [FR-001, FR-027, FR-028, SC-008]
- [ ] T040 Execute clean-machine validation per `specs/003-linux-container-readiness/quickstart.md` using only Docker (build CLI and MCP images, start compose MongoDB, execute status, create encrypted backup via `--passphrase-stdin`, test MCP stdio initialize handshake, and run `test-images.ps1`) [FR-001, FR-003, FR-007, SC-003, SC-007]

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion — BLOCKS all user stories
- **User Story 1 (Phase 3)**: Depends on Foundational completion — Delivers core container images (MVP)
- **User Story 2 (Phase 4)**: Depends on User Story 1 completion — Enforces runtime hosting conventions
- **User Story 3 (Phase 5)**: Depends on User Story 1 completion — Enforces in-container Database Tools operations
- **User Story 4 (Phase 6)**: Depends on User Stories 1, 2, 3 completion — Proves launched-host test parity
- **User Story 5 (Phase 7)**: Depends on Foundational completion — Enables external DB compatibility testing
- **User Story 6 (Phase 8)**: Depends on User Story 1 completion — Implements CI workflows and cross-platform scripts
- **User Story 7 (Phase 9)**: Depends on User Story 1 and CI setup — Enforces supply chain security
- **Polish & Traceability (Phase 10)**: Depends on all prior phases

### Parallel Opportunities

- Within Phase 1: T002, T003, T004, T005 can run in parallel
- Within Phase 2: T006, T007, T008, T009 can be written in parallel; T010, T011, T012 can be implemented in parallel
- Within Phase 3: T013, T014 can be written in parallel
- Within Phase 5: T019, T020, T021 can be written in parallel
- Within Phase 6: T024, T025, T026 can be written in parallel
- Within Phase 7: T028 can be written in parallel
- Within Phase 8: T031 can be written in parallel
- Within Phase 9: T034, T035 can be written in parallel
- User Story 3 (Phase 5) and User Story 5 (Phase 7) can proceed in parallel once Phase 2 is complete

---

## Implementation Strategy (MVP First)

1. Complete Phase 1: Setup + Phase 2: Foundational (blocking architecture and safety guards)
2. Complete Phase 3: User Story 1 (build and verify `roleradar-cli` and `roleradar-mcp` images) -> **MVP Milestone**
3. Complete Phase 4: User Story 2 (hosting conventions) + Phase 5: User Story 3 (in-container Database Tools)
4. Complete Phase 6: User Story 4 (launched-host test parity across architectures)
5. Complete Phase 7: User Story 5 (external database compatibility matrix)
6. Complete Phase 8: User Story 6 (multi-OS CI) + Phase 9: User Story 7 (supply chain hardening)
7. Complete Phase 10: Traceability verification and clean-machine quickstart validation
