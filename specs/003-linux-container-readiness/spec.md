# Feature Specification: Linux Container Readiness for Azure-Style Hosting

**Feature Branch**: `003-linux-container-readiness`

**Created**: 2026-10-18

**Status**: Draft

**Input**: User description: "Linux container readiness for Azure-style hosting: multi-stage Linux images (linux/amd64 and linux/arm64) for CLI and stdio MCP server, non-root, read-only rootfs, no network listeners, no Docker socket, bundled MongoDB Database Tools for backup/restore across all 12 collections; hosting-readiness conventions enforced by tests (env config, no writes outside configured dirs, stderr logs, SIGTERM shutdown, UTC, no Windows path assumptions, redacted connection strings); existing launched-host tests run against images; external-Mongo compatibility checklist mode for Contract test suite; cross-platform scripts and pre-commit hooks (Windows, macOS, Linux) with macOS CI job; supply-chain hygiene (digest-pinned base images, hadolint, vuln scan with zero critical vulns, SBOM, private-data exclusion); ADRs for hosted MongoDB options and Azure runtime target."

## Clarifications

### Session 2026-10-18

- Q: What is the CI scope across operating systems? → A: The Linux job runs the entire suite including image-based launched-host tests, compose tests, and external-database mode tests; Windows and macOS jobs run build, format and lint, unit tests, architecture tests, and script/portability tests (path handling, denylist resolution) without Docker-dependent tests because hosted Windows and macOS runners cannot run Linux containers.
- Q: What is the roadmap feature numbering across future milestones? → A: Feature 003 is Linux container readiness, Feature 004 is the legacy importer, Feature 005 is the login/API and web frontend, and Feature 006 is hosted MCP over HTTP.
- Q: What safety guards, network requirements, and cleanup policies govern external database compatibility testing? → A: Testing executes only when an explicit opt-in environment variable is set; refuses execution if the target server contains any non-system database without the `rrtest_` prefix; creates only uniquely named `rrtest_<guid>` databases, records created databases, and drops only those on completion, failure, or `SIGTERM`; requires TLS for any non-loopback host; never prints the connection string, host, or credentials; and emits the capability matrix as JSON and markdown without connection details.
- Q: What is the supported and tested container execution configuration for read-only root filesystems? → A: The supported and tested execution mode is `docker run --read-only` with a tmpfs mount for the temporary directory only (`/tmp`), .NET diagnostics disabled (`COMPlus_EnableDiagnostics=0` / `DOTNET_EnableDiagnostics=0`), and no other writable path except an explicitly mounted backup output directory. Image tests execute strictly in this mode.
- Q: How are target CPU architectures and cold-start benchmarks verified? → A: `linux/amd64` and `linux/arm64` launched-host tests run natively (using native arm64 runners for arm64), never under emulation; cold-start duration (SC-003) is measured per architecture with the container image already pulled and MongoDB already healthy, with the sub-3.0s threshold applying to each.
- Q: What policy governs container vulnerability scanning and build failures in CI? → A: The build fails on any critical-severity findings that have an available fix; unfixed critical findings are permitted only on a reviewed allowlist file specifying an expiration date and justification; all scan outputs are retained as CI artifacts.
- Q: Which MongoDB Database Tools version and compatibility baseline are bundled in the CLI image? → A: Bundled Database Tools are pinned to a specific version with verified cryptographic checksums per architecture, matching the pinned server version (MongoDB 8.0.x), replacing the 7.0 assumption, and evaluated against candidate hosted services in the hosting ADR.
- Q: How are container images built and linted in the automation pipeline? → A: Container images build reproducibly from a clean repository clone with a single command; Dockerfile linting (`hadolint`) and image builds execute exclusively on the Linux CI runner.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Secure Linux Container Images for CLI and MCP Host (Priority: P1)

As a system owner preparing for cloud hosting and cross-platform use, I want hardened, multi-stage Linux container images for both the command-line interface (CLI) and the stdio Model Context Protocol (MCP) server so that both tools can run in isolated, rootless, immutable environments on any Linux container host (x86_64 and native ARM64) without host-system dependencies, built reproducibly with a single command.

**Why this priority**: Foundational container artifacts are required before hosting readiness, automated image testing, or cloud runtime selection can be verified.

**Independent Test**: Can be tested independently by building the images on Linux/amd64 and Linux/arm64 from a clean clone and running `docker run --read-only --tmpfs /tmp -i --rm` (with .NET diagnostics disabled) to execute CLI commands and MCP stdio protocol handshakes under a non-root user.

**Acceptance Scenarios**:

1. **Given** a built CLI container image, **When** executed with `--help` or `status` under `docker run --read-only --tmpfs /tmp` with .NET diagnostics disabled, **Then** the container executes as a non-privileged user (non-root), emits output to standard output, logs to standard error, opens no network listeners, and exits cleanly with code 0.
2. **Given** a built MCP stdio container image, **When** launched in interactive mode (`docker run --read-only --tmpfs /tmp -i --rm`) with standard input connected to an MCP client, **Then** it initializes the MCP session over standard input/output without binding any network port, runs as a non-root user, and handles protocol commands idempotently.
3. **Given** either container image, **When** inspected for user privileges and network configuration, **Then** the container runs under a designated non-root UID/GID, exposes zero listening network ports (`EXPOSE`), and mounts no Docker socket.
4. **Given** a clean repository clone on a Linux host, **When** the container build command executes, **Then** both images build reproducibly with a single command.

---

### User Story 2 - Hosting-Readiness Conventions Enforced by Automated Verification (Priority: P1)

As a maintainer, I want automated test suites to enforce production hosting conventions across all host processes so that future cloud container deployments (such as Azure Container Apps or Kubernetes) operate predictably and safely without platform-specific assumptions.

**Why this priority**: Catching hosting anti-patterns (hardcoded paths, credential leakage, stdout corruption, unhandled signals) via automated tests prevents production runtime failures and security vulnerabilities before HTTP services are introduced.

**Independent Test**: Can be tested by running automated architecture and process-level tests that assert environment-only configuration, standard-error-only logging, signal termination compliance, UTC time generation, and complete connection string and credential masking.

**Acceptance Scenarios**:

1. **Given** any host process (CLI or MCP), **When** configuration is supplied exclusively via environment variables, **Then** the application starts and functions without requiring local configuration files in the working directory.
2. **Given** an MCP host process, **When** any informational, debug, or diagnostic event is emitted, **Then** it is written strictly to standard error, ensuring standard output contains only valid protocol frames.
3. **Given** a running host process, **When** a `SIGTERM` signal is delivered, **Then** the process initiates a graceful shutdown, completes in-flight work, releases resources, drops any recorded temporary test databases, and terminates within a bounded shutdown timeout.
4. **Given** a database connection string containing passwords, user credentials, or target host details, **When** connection diagnostics, errors, or capability reports are generated, **Then** the connection string, host, credentials, and sensitive tokens are completely masked and never printed to stdout, stderr, or reports.
5. **Given** any timestamp generated by the system across all entities and audit entries, **When** recorded or formatted, **Then** the timestamp is strictly in Coordinated Universal Time (UTC).

---

### User Story 3 - Self-Contained Database Backup, Restore, and Wipe Inside the CLI Container (Priority: P1)

As an operator, I want database backup, restore, and wipe commands to execute completely inside the CLI container against any reachable database host without mounting the host Docker socket or invoking host-level tools, utilizing bundled MongoDB 8.0.x Database Tools, while protecting all 12 system collections.

**Why this priority**: Eliminates Docker-out-of-Docker security hazards, enabling scheduled backup jobs in rootless cloud container environments (e.g., Azure Container Apps jobs) against remote managed databases.

**Independent Test**: Can be tested by running `roleradar backup`, `restore`, and `wipe` commands entirely inside the CLI container (with only `/tmp` and an explicitly mounted backup directory writable) against an isolated database service across all 12 collections, verifying that no host docker socket or external binary is required.

**Acceptance Scenarios**:

1. **Given** a database containing documents across all 12 collections (notes, note revisions, audit log, companies, career sources, criteria, postings, posting signals, applications, application events, contacts, search runs), **When** `roleradar backup` is executed inside the container specifying a mounted output path, **Then** it produces an encrypted backup archive covering all 12 collections directly using bundled, checksum-verified Database Tools (MongoDB 8.0.x baseline) without contacting a Docker daemon.
2. **Given** a valid encrypted backup archive, **When** `roleradar restore` is executed inside the container against a clean database, **Then** all 12 collections are fully restored with identical document counts, indexes, and content integrity, reporting restored counts per collection.
3. **Given** an existing populated database, **When** `roleradar wipe --yes` is executed inside the container, **Then** all documents across all 12 collections are deleted, collections are reset, and a detailed summary of removed records is reported.

---

### User Story 4 - Launched-Host Test Parity Against Container Images (Priority: P1)

As a quality engineer, I want the full suite of end-to-end launched-host tests to execute identically against the built Linux container images on both `linux/amd64` and native `linux/arm64` runners so that behavior in containers is proven equivalent to native binary execution.

**Why this priority**: Ensures that containerization introduces zero regressions in protocol framing, concurrency safety, data privacy, or tool schemas on either CPU architecture without emulation artifacts.

**Independent Test**: Can be tested by executing the existing startup smoke tests, concurrency race tests, PII/contact no-leak scans, and tool-schema contract tests targeting containerized host invocations (`docker run --read-only --tmpfs /tmp`) natively on both architectures.

**Acceptance Scenarios**:

1. **Given** the built container images on native `linux/amd64` and native `linux/arm64` runners, **When** the startup smoke test suite executes under `docker run --read-only --tmpfs /tmp`, **Then** all CLI subcommands and MCP protocol initialization commands succeed with expected exit codes and clean output streams without emulation.
2. **Given** the built MCP container image, **When** concurrent container processes execute search run logging simultaneously, **Then** database unique constraints and idempotency guarantees hold exactly as in native execution (exactly one record created, duplicates report existing status).
3. **Given** the built container images, **When** a contact with distinctive fictional details is created and system commands (status, verify, backup, MCP tool listing, error generation) are executed, **Then** the confidential contact details appear nowhere in standard output, standard error, logs, or unencrypted backup archives except when specifically retrieved via `contact get`.
4. **Given** the built MCP container image, **When** the tool-schema contract test suite runs, **Then** the tool count is verified to be exactly 12 with identical schemas and descriptions.

---

### User Story 5 - Hosted Database Compatibility Matrix & Validation Mode (Priority: P2)

As an architect evaluating cloud databases, I want to execute the storage contract test suite in a dedicated mode against an external MongoDB endpoint so that candidate cloud services (such as Azure Cosmos DB vCore or MongoDB Atlas) can be verified for transaction, partial index, and text search compatibility, producing an actionable pass/fail capability matrix while strictly protecting non-test databases.

**Why this priority**: Identifies functional discrepancies or unsupported database features in cloud managed databases prior to infrastructure provisioning, while ensuring safety guards prevent accidental data destruction.

**Independent Test**: Can be tested by setting the explicit opt-in environment variable with an authorized external database connection string, verifying safety guards, running the contract suite, and inspecting the generated capability report.

**Acceptance Scenarios**:

1. **Given** an external database connection string, **When** the compatibility test suite is initiated without the explicit opt-in environment variable set, or when the server contains any non-system database that does not carry the `rrtest_` prefix, **Then** the test runner immediately aborts with a clear safety warning without modifying or querying non-test data.
2. **Given** an external connection string pointing to a non-loopback host without TLS enabled, **When** executed, **Then** the runner immediately refuses connection, requiring TLS for any remote endpoint.
3. **Given** an authorized external database endpoint, **When** the compatibility suite executes, **Then** it creates only uniquely named `rrtest_<guid>` databases, tracks all created test databases, verifies multi-document cross-collection transactions, partial unique indexes, and text search, and thoroughly drops all created test databases upon completion, failure, or `SIGTERM`.
4. **Given** the completion of the compatibility test run, **When** test results are compiled, **Then** the system outputs a structured pass/fail capability matrix formatted as JSON and markdown, containing zero connection strings, hostnames, or credentials.

---

### User Story 6 - Cross-Platform Automation & Multi-OS CI Verification (Priority: P2)

As a developer working across different operating systems, I want all repository scripts, automation tools, and pre-commit hooks to run seamlessly on Windows, macOS, and Linux, and to have continuous integration verify pull requests across all three platforms according to their supported runner capabilities.

**Why this priority**: Prevents platform drift, broken developer scripts, and path-separator failures across multi-OS development while recognizing that hosted Windows and macOS runners cannot execute Linux containers.

**Independent Test**: Can be tested by running the entire script suite and pre-commit checks on Windows, Linux, and macOS environments, and verifying that CI executes dedicated jobs for Windows, Ubuntu Linux, and macOS.

**Acceptance Scenarios**:

1. **Given** any repository script (service startup, test runners, build scripts, formatting, analysis), **When** executed using PowerShell (`pwsh`) on Windows, macOS, or Linux, **Then** the script completes successfully without path separator errors, backslash assumptions, or environment-specific failures.
2. **Given** the local pre-commit guard, **When** executed on macOS or Linux, **Then** it properly resolves the external privacy denylist from standard OS configuration directories (e.g., `~/.config/roleradar/denylist.txt`) without assuming Windows `LOCALAPPDATA`.
3. **Given** a pull request or push to the repository, **When** the CI workflow runs, **Then**:
   - The **Ubuntu Linux** job executes the entire suite: image builds, Dockerfile linting, container-based launched-host tests, compose integration tests, and external-database compatibility tests.
   - The **Windows** and **macOS** jobs execute code compilation, formatting and linting, unit tests, architecture tests, and cross-platform script/portability tests (omitting Linux container-dependent tests).

---

### User Story 7 - Supply Chain Hardening & Container Vulnerability Governance (Priority: P2)

As a security auditor, I want container base images pinned to immutable cryptographic digests, Dockerfiles linted against security standards on Linux, and container images scanned for vulnerabilities and sensitive data leakage with a strict fix policy so that the software supply chain is verifiable and secure.

**Why this priority**: Minimizes attack surfaces, prevents image tampering or drift, and ensures zero unapproved critical vulnerabilities enter deployment pipelines.

**Independent Test**: Can be tested by running Dockerfile linting (`hadolint`), container vulnerability scanning (`trivy`), checking the vulnerability allowlist policy, and scanning the container filesystem and history for private data or sensitive file inclusion.

**Acceptance Scenarios**:

1. **Given** the project Dockerfiles, **When** inspected or linted with `hadolint` on the Linux runner, **Then** all base images are pinned by sha256 cryptographic digests, instructions follow non-root and minimal-privilege standards, and the linter passes with zero warnings or errors.
2. **Given** the built container images, **When** subjected to container vulnerability scanning, **Then** the build fails on any critical-severity findings that have an available fix; any unfixed critical findings are permitted only if listed in a reviewed allowlist file with an expiration date and justification, and all scan reports are retained as CI artifacts.
3. **Given** the container build context and built images, **When** scanned for sensitive files, developer secrets, or private data matches, **Then** zero unauthorized files or sensitive data traces exist in the image layers or filesystem history.
4. **Given** the build pipeline, **When** container images are generated, **Then** a Software Bill of Materials (SBOM) is produced documenting all included packages and dependencies.

---

### User Story 8 - Cloud Hosting & Database Architecture Decision Records (Priority: P3)

As a technical lead, I want documented Architecture Decision Records (ADRs) evaluating candidate cloud database options and Azure container runtime environments against our verified compatibility matrix and MongoDB 8.0.x Database Tools so that future hosting choices are well-justified, transparent, and recorded.

**Why this priority**: Solidifies architectural consensus and documents trade-offs prior to committing time and infrastructure costs to cloud deployments.

**Independent Test**: Can be tested by reviewing the committed MADR documents under `docs/adr/` ensuring all decision criteria, trade-offs, and compatibility results are comprehensively addressed.

**Acceptance Scenarios**:

1. **Given** candidate cloud database options (MongoDB Atlas on Azure, Azure Cosmos DB for MongoDB vCore, self-hosted containerized MongoDB with persistent storage), **When** evaluated in a formal ADR, **Then** the record documents their support for cross-collection transactions, partial unique indexes, text search, operational complexity, pricing tiers, and Database Tools 8.0.x compatibility against our checklist.
2. **Given** candidate Azure container runtimes (Azure Container Apps vs. Azure Kubernetes Service), **When** evaluated in a formal ADR, **Then** the record analyzes operational trade-offs, scale-to-zero capabilities, secret management, job scheduling for backups, and migration path for future HTTP services.
3. **Given** the completion of both ADRs, **When** reviewed, **Then** neither record initiates cloud provisioning or requires live cloud infrastructure deployments in this feature phase.

---

### Edge Cases

- **Read-Only Root Filesystem Restrictions**: The supported and tested container execution configuration is `docker run --read-only` with a tmpfs mount for the temporary directory only (`/tmp`) and .NET diagnostics disabled (`COMPlus_EnableDiagnostics=0` / `DOTNET_EnableDiagnostics=0`). No other writable paths exist except when an explicit backup output directory is mounted.
- **External Database Non-Empty Collision & Prefix Guard**: What happens if an external MongoDB endpoint provided for compatibility testing contains non-test databases? The test runner must refuse execution immediately if any non-system database without the `rrtest_` prefix exists, preventing data corruption.
- **TLS Requirement for Remote Databases**: What happens if an external database connection string targets a remote host over unencrypted plaintext? The test runner must reject the connection and require TLS for all non-loopback endpoints.
- **Abrupt Termination and Test Database Cleanup (SIGTERM / SIGINT)**: What happens if an external compatibility test run receives a termination signal midway through execution? The test process must intercept `SIGTERM`, query its internal registry of created `rrtest_<guid>` databases, drop only those databases, and terminate cleanly.
- **Credential Protection in Output and Matrix**: How are connection strings handled in failure reports or capability matrices? The test runner must completely sanitize and mask all connection strings, target hosts, and credentials, omitting them entirely from console output, error messages, and generated markdown/JSON reports.
- **Unsupported Database Feature Encountered**: How does the compatibility suite behave when a candidate hosted database returns an error for cross-collection transactions or partial indexes? The suite must isolate the failure, record the specific diagnostic reason in the compatibility matrix, and continue testing remaining capabilities without crashing.
- **Vulnerability Allowlist Governance**: What happens if a base image has a critical vulnerability with no available upstream fix? The scan passes only if the vulnerability is explicitly recorded in a reviewed allowlist file containing an active, non-expired date and justification. If expired or unlisted, the build fails.
- **Standard Input Disconnect in MCP Host**: When an MCP client closes the stdio pipe unexpectedly, the MCP server container detects EOF on standard input and exits cleanly with code 0 without hanging or spinning CPU.
- **Missing Denylist on Non-Windows Hosts**: If a developer on macOS or Linux runs pre-commit checks before creating an external denylist file, the hook reports a helpful setup instruction indicating the expected configuration path (`~/.config/roleradar/denylist.txt`) rather than failing with an unhandled exception.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide multi-stage Dockerfiles producing minimal, hardened Linux container images for both the RoleRadar CLI and the stdio MCP server, buildable reproducibly from a clean repository clone with a single command.
- **FR-002**: Container images MUST support both `linux/amd64` and `linux/arm64` CPU architectures natively without emulation, verified by executing launched-host tests on native amd64 and native arm64 runners respectively.
- **FR-003**: Container images MUST run under an unprivileged non-root user account with a fixed UID/GID and MUST operate reliably in the supported and tested mode: `docker run --read-only --tmpfs /tmp` with .NET diagnostics disabled (`COMPlus_EnableDiagnostics=0` / `DOTNET_EnableDiagnostics=0`) and no writable filesystem paths outside `/tmp` and explicitly mounted backup directories.
- **FR-004**: The stdio MCP container image MUST NOT declare or bind any network listening ports, reserving standard output exclusively for MCP protocol JSON-RPC messages and standard error for diagnostic logging.
- **FR-005**: All host applications (CLI and MCP server) MUST support full operational configuration exclusively through environment variables, requiring no local configuration files in the working directory.
- **FR-006**: The CLI container image MUST bundle native MongoDB Database Tools (`mongodump`, `mongorestore`) pinned by version and verified by cryptographic checksum per architecture, compatible with the pinned MongoDB 8.0.x server baseline, eliminating any requirement for a host Docker socket, `docker exec`, or host-installed database utilities.
- **FR-007**: The containerized backup command (`roleradar backup`) MUST execute against any reachable MongoDB connection string and produce an encrypted, authenticated archive covering all 12 system collections (notes, note_revisions, audit_log, companies, career_sources, criteria, postings, posting_signals, applications, application_events, contacts, search_runs).
- **FR-008**: The containerized restore command (`roleradar restore`) MUST verify archive integrity and restore all 12 collections with exact document, index, and schema fidelity against an empty database.
- **FR-009**: The containerized wipe command (`roleradar wipe`) MUST refuse to run without explicit user confirmation and, upon confirmation, purge all documents across all 12 collections while emitting an itemized removal count.
- **FR-010**: All host processes MUST intercept `SIGTERM` and `SIGINT` signals, perform graceful cleanup of active connections and child processes, drop any tracked ephemeral test databases, and terminate within a configurable grace period.
- **FR-011**: All host processes MUST sanitize connection strings, tokens, and credentials in all exception messages, logs, diagnostic outputs, and traces, completely masking passwords and sensitive parameters.
- **FR-012**: All timestamps created, stored, or processed by the system MUST be strictly in Coordinated Universal Time (UTC).
- **FR-013**: The system MUST provide an external database compatibility test mode for the storage contract test suite, executing only when an explicit opt-in environment variable is set and requiring TLS for any non-loopback host.
- **FR-014**: The external database compatibility test runner MUST enforce strict safety guards, immediately refusing execution if the target server contains any non-system database that does not carry the `rrtest_` prefix.
- **FR-015**: The external database compatibility test runner MUST isolate individual test fixtures using uniquely generated `rrtest_<guid>` database names, track all created databases, and automatically drop only those tracked test databases upon test completion, test failure, or `SIGTERM`.
- **FR-016**: The compatibility test suite MUST generate a structured capability matrix detailing test outcomes for multi-document cross-collection transactions, partial unique indexes, and text search, emitted as JSON and markdown without printing connection strings, hostnames, or credentials.
- **FR-017**: All repository automation scripts (PowerShell `/scripts`) MUST execute without error across Windows, Linux, and macOS environments, using platform-agnostic path handling and avoiding Windows-specific backslash assumptions.
- **FR-018**: The local pre-commit guard MUST resolve the external confidential denylist file from platform-appropriate standard configuration paths on Windows, macOS, and Linux.
- **FR-019**: Continuous integration (CI) workflows MUST include automated jobs tailored to runner capabilities:
  - The **Ubuntu Linux** runner MUST execute the entire test suite: container image build, Dockerfile linting (`hadolint`), container-based launched-host tests, compose integration tests, and external-database compatibility tests.
  - The **Windows** and **macOS** runners MUST execute compilation, code formatting, linter checks, unit tests, architecture tests, and cross-platform script/portability tests, omitting Docker-dependent tests.
- **FR-020**: Base container images in Dockerfiles MUST be cryptographically pinned using immutable `sha256` digests rather than floating tags.
- **FR-021**: Dockerfile definitions MUST adhere to best practice container linting rules verified via automated `hadolint` execution on the Linux CI runner.
- **FR-022**: Built container images MUST be verified by automated vulnerability scanning in CI that fails the build on any critical findings with an available fix; unfixed critical findings are permitted only if documented in a reviewed allowlist file with an expiration date and justification, and all scan reports MUST be retained as CI artifacts.
- **FR-023**: Container build configurations MUST utilize strict build-ignore rules (`.dockerignore`) to ensure private data, local test results, git history, and secrets are excluded from image contexts.
- **FR-024**: Automated tests MUST scan the filesystem and layer history of built container images to verify zero occurrences of confidential denylist terms, personal data, or unauthorized files.
- **FR-025**: The build process MUST generate a comprehensive Software Bill of Materials (SBOM) for each produced container image.
- **FR-026**: The existing launched-host test suite (startup smoke, concurrency race tests, contact privacy no-leak scans, and tool-schema contracts) MUST be extended to execute against the built container images with 100% pass rate under `docker run --read-only --tmpfs /tmp`.
- **FR-027**: The project MUST document an Architectural Decision Record (MADR) evaluating cloud MongoDB hosting options (MongoDB Atlas on Azure, Azure Cosmos DB for MongoDB vCore, and self-hosted containerized MongoDB with persistent storage) against the compatibility checklist and MongoDB 8.0.x Database Tools.
- **FR-028**: The project MUST document an Architectural Decision Record (MADR) evaluating Azure container runtime targets (Azure Container Apps vs. Azure Kubernetes Service) for future workload hosting.
- **FR-029**: In-container diagnostic logging across all processes MUST be directed to standard error, ensuring standard output remains unpolluted for CLI output and protocol framing.
- **FR-030**: Container cold-start performance MUST be verified by automated benchmark tests on both `linux/amd64` and native `linux/arm64` runners, measured with images pre-pulled and MongoDB healthy, ensuring rapid initialization under 3.0 seconds per architecture.

### Key Entities

- **Container Image Specification**: Defines the multi-stage build, base image digests, non-root user permissions, read-only root filesystem constraints (`docker run --read-only --tmpfs /tmp`), and bundled MongoDB 8.0.x Database Tools dependencies for the CLI and MCP host images.
- **Hosted Database Compatibility Matrix**: A structured assessment artifact detailing the operational status and pass/fail results for critical database capabilities (cross-collection transactions, partial unique indexes, text search) against candidate database engines, emitted as JSON and markdown sanitized of connection details.
- **Database Safety Guard Descriptor**: Configuration rules and validation logic that enforce explicit opt-in, verify that all existing non-system databases carry the `rrtest_` prefix, require TLS on remote endpoints, and track `rrtest_<guid>` test databases for guaranteed cleanup.
- **Supply Chain Security Profile**: A collection of compliance artifacts including Dockerfile lint reports (`hadolint` on Linux), vulnerability scan results (with allowlist governance for unfixed findings), SBOM inventories, and image layer privacy scans.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Filesystem and commit history scanning of built container images and repository contents reports **zero matches** against confidential denylist terms and private data patterns.
- **SC-002**: Automated vulnerability scanning of both built container images reports **zero unapproved critical vulnerabilities** (build fails on any critical finding with an available fix; any unfixed critical finding requires an active, reviewed allowlist entry with justification).
- **SC-003**: The cold start duration of the MCP stdio container—measured from container launch (`docker run`) to receipt of the first valid JSON-RPC tool response with images pre-pulled and MongoDB healthy—is **under 3.0 seconds** on both `linux/amd64` and native `linux/arm64` runners.
- **SC-004**: **100%** of the existing launched-host test suite (including startup smoke tests, concurrency race tests, contact privacy no-leak tests, and the 12-tool schema contract test) passes without modification when targeting the container images under `docker run --read-only --tmpfs /tmp` on both `linux/amd64` and native `linux/arm64`.
- **SC-005**: Executing the database compatibility test suite against an external MongoDB endpoint produces a comprehensive pass/fail checklist itemizing transaction, index, and search capabilities, with **zero unintentional mutations** to non-test databases and guaranteed cleanup of all `rrtest_<guid>` databases.
- **SC-006**: Continuous integration passes cleanly across all three operating systems according to their runner capabilities: the complete suite (including image builds, lint, container tests, compose tests, and external-database tests) passes on Linux, and build, format, lint, unit, architecture, and script portability tests pass on Windows and macOS.
- **SC-007**: Complete database backup, restore, and wipe operations covering all 12 system collections execute successfully inside the CLI container with **zero dependency** on a host Docker daemon or host Docker socket.
- **SC-008**: Two formal Architecture Decision Records (MADRs) covering hosted database options and cloud container runtime targets are completed and committed under `docs/adr/`.

## Assumptions

- The target deployment model for future HTTP services (Feature 005 API/web, Feature 006 MCP over HTTP) will utilize Linux containers in Azure; this feature establishes baseline containerization and hosting readiness without deploying cloud resources. The legacy importer is Feature 004.
- Container execution environments support Open Container Initiative (OCI) image standards and allow mounting ephemeral volumes (`--tmpfs /tmp`) for temporary file operations when the root filesystem is read-only.
- Bundled native database tools (`mongodump` and `mongorestore`) are pinned and checksum-verified against MongoDB version 8.0.x and compatible wire-protocol endpoints (replacing earlier 7.0 assumptions).
- External database compatibility testing requires an explicit opt-in environment variable (`ROLERADAR_EXTERNAL_TEST_OPT_IN=1`) and a secure connection string (`ROLERADAR_TEST_MONGODB_URI`); if not provided, the test suite defaults to local ephemeral Testcontainers.
- The single-owner, local-first security and privacy model (Principle I and Principle VII) continues to govern all operations, requiring encrypted backups and strict isolation of confidential contact information.
- No public network-listening endpoints (HTTP, WebSockets) are deployed or exposed in this feature phase.
