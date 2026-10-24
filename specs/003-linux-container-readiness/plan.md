# Implementation Plan: Linux Container Readiness for Azure-Style Hosting

**Branch**: `003-linux-container-readiness` | **Date**: 2026-10-18 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/003-linux-container-readiness/spec.md`

## Summary

Feature 003 establishes Linux container packaging, cross-platform portability, cloud database compatibility testing, and supply-chain hardening for RoleRadar before future HTTP services (Feature 005 API/web, Feature 006 hosted MCP over HTTP) are introduced.

This feature is strictly **packaging, CI, and test-infrastructure work**:
- **Zero changes** to production Domain or Application layers.
- **Zero changes** to database document schemas or collection definitions.
- **Zero new MCP tools** (maintains the exact 12-tool cap established in Feature 002).

Technical highlights:
1. Multi-stage Dockerfile producing `roleradar-cli` and `roleradar-mcp` (stdio MCP) for `linux/amd64` and `linux/arm64` using distroless/chiseled base images (zero shell, zero package manager).
2. Hardened, tested container runtime configuration (`--read-only --tmpfs /tmp --cap-drop ALL --security-opt no-new-privileges`).
3. Bundled MongoDB Database Tools 100.19.1 in the CLI image with atomic 0600 RAM-only (`/tmp`) `--config` credential masking (passwords never exposed on argv or in process trees) and dpkg status entries for vulnerability scanners.
4. Docker Compose profiles overlay (`deploy/docker-compose.apps.yml`) with `directConnection=true` and a cross-platform PowerShell wrapper (`scripts/roleradar-docker.ps1`).
5. External database compatibility mode for `RoleRadar.Tests.Contract` with an isolated, unit-tested safety guard (`ExternalMongoSafetyGuard`) requiring explicit opt-in (`ROLERADAR_EXTERNAL_TEST_OPT_IN=1`), driver-parsed TLS on remote endpoints, prefix isolation (`rrtest_<guid>`), and automatic cleanup on completion or `SIGTERM`.
6. Launched-host test parity executing startup smoke tests, concurrency race tests, contact privacy no-leak scans, and the 12-tool schema contract against container images.
7. Multi-OS CI matrix: Ubuntu Linux runs the complete suite (including image builds, hadolint, trivy, SBOM, container tests, compose tests, and external database mode); native ARM64 runner (`ubuntu-24.04-arm`) verifies ARM64 image parity; Windows and macOS runners execute build, format, lint, unit, architecture, and script portability tests.
8. Two Architecture Decision Records: [ADR-0011](../../docs/adr/0011-hosted-mongodb-selection-and-compatibility.md) (Hosted MongoDB Evaluation) and [ADR-0012](../../docs/adr/0012-azure-container-runtime-target.md) (Azure Container Apps vs. AKS).

---

## Technical Context

**Language/Version**: C# 13 / .NET 10 (`global.json` pinned to SDK `10.0.402`, `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`).

**Primary Dependencies**:
- .NET runtime base images: `mcr.microsoft.com/dotnet/runtime:10.0-noble-chiseled` for both MCP and CLI, pinned by multi-arch digest.
- MongoDB Database Tools 100.19.1 (`mongodump`, `mongorestore`) pinned by SHA-256 digest per architecture with 6 required shared libraries and dpkg status entries extracted from build stage.
- `Testcontainers.MongoDb` 4.16.0 (ephemeral container integration tests).
- `ModelContextProtocol` 2.2.0 (stdio JSON-RPC framing).
- `Geralt` 4.4.0 (XChaCha20-Poly1305 + Argon2id backup encryption).

**Storage**:
- Local: Single-node MongoDB 8.0.34 replica set (`rs0`) on loopback via Docker Compose.
- External Validation Target: Remote MongoDB 8.0+ or compatible endpoints tested via `ROLERADAR_TEST_MONGODB_URI`.

**Testing**:
- xUnit v3 for unit, architecture, contract, and integration tests.
- Launched-host container test harness executing `docker run` natively on amd64 and arm64.
- Pester for cross-platform PowerShell script portability tests.

**Target Platform**: Linux (`linux/amd64` and `linux/arm64`), Windows (`x86_64`), macOS (`arm64`).

**Project Type**: Container packaging, test automation harness, and CI/CD workflows.

**Performance Goals**:
- Cold start of `roleradar-mcp` container from `docker run` to first valid JSON-RPC response < 3.0 seconds on both amd64 and native arm64 CI runners.

**Constraints**:
- Read-only root filesystem (`--read-only`) with writable `/tmp` on tmpfs only.
- Credentials must never appear on argv, in logs, or in persistent storage.
- Zero network listeners in stdio MCP container image.
- Zero private data or denylist terms in image layers or git history.

---

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Requirement | Status | Architecture Justification |
| :--- | :--- | :---: | :--- |
| **I. Privacy First** | No real job/personal data in repo, fixtures, logs, or image layers. Denylist check. | **PASS** | `.dockerignore` denies all by default. Image scanning test extracts container filesystem and asserts zero denylist or secret matches. Capability matrix omits connection strings, hosts, and credentials. |
| **II. Test-First Development** | Tests written before implementation. Ephemeral Testcontainers. Minimum coverage gate. | **PASS** | All new packaging and guard capabilities tested first. Testcontainers used for contract/integration tests. Coverage gate (>=85% on Domain/Application) preserved. |
| **III. Test-Enforced Layering** | Strict inward dependencies. Command handlers wrapped in transaction & audit decorators. | **PASS** | Domain and Application projects remain completely untouched. Host packaging invokes existing Application handlers. |
| **IV. Named Seams Only** | Aggregate-level repositories, plain command/query handlers, typed failure results. | **PASS** | No new database abstractions introduced. `ExternalMongoSafetyGuard` is a pure test-infrastructure utility returning typed validation results. |
| **V. Agent Safety & Auditability** | Narrow, idempotent tools. No hard delete tools. Tool surface capped. | **PASS** | Exact 12-tool cap from Feature 002 is maintained. Zero new tools added. |
| **VI. stdio Protocol Hygiene** | `stdout` reserved exclusively for MCP protocol. `stderr` for logs. Zero network listeners. | **PASS** | MCP Dockerfile exposes zero ports. Protocol framing verified under `docker run -i --rm`. |
| **VII. Data Durability & Recovery** | Persistent volumes. Destructive wipe isolated. Encrypted backups. | **PASS** | Bundled Database Tools in CLI image backup and restore all 12 collections with authenticated encryption, operating against remote hosts without host Docker socket. |
| **VIII. Recorded Decisions** | Decisions recorded in MADRs under `docs/adr/`. Pinned dependencies. | **PASS** | [ADR-0011](../../docs/adr/0011-hosted-mongodb-selection-and-compatibility.md) and [ADR-0012](../../docs/adr/0012-azure-container-runtime-target.md) recorded. All base images pinned by digest. GitHub Actions pinned by commit SHA. |
| **IX. Technical Conventions** | .NET 10, C#, UTC timestamps, Guid.CreateVersion7 ids, `owner_id`. | **PASS** | Pinned .NET 10 SDK, UTC timestamps, time-ordered GUIDs for `rrtest_<guid>` test databases. |

---

## Project Structure

### Documentation (this feature)

```text
specs/003-linux-container-readiness/
├── plan.md              # This file (/speckit-plan output)
├── research.md          # Technical research & decisions (Phase 0 output)
├── data-model.md        # Test & governance schemas (Phase 1 output)
├── quickstart.md        # Clean-machine Docker-only validation guide (Phase 1 output)
└── contracts/           # Interface and execution contracts (Phase 1 output)
    ├── dockerfile-build-contract.md
    ├── container-runtime-contract.md
    ├── wrapper-scripts-contract.md
    ├── external-database-guard-contract.md
    └── ci-workflow-contract.md
```

### Source & Configuration Changes

```text
.
├── Dockerfile                              # Multi-stage build (build, tools-downloader, mcp, cli)
├── .dockerignore                           # Deny-by-default build ignore policy
├── .trivy-allowlist.yaml                   # Vulnerability scan allowlist with expiry & justification
├── deploy/
│   ├── docker-compose.yml                  # Base MongoDB replica set service (unchanged)
│   └── docker-compose.apps.yml             # Profiles overlay for cli and mcp container execution
├── docs/adr/
│   ├── 0011-hosted-mongodb-selection-and-compatibility.md
│   ├── 0012-azure-container-runtime-target.md
│   └── README.md                           # Updated MADR index
├── scripts/
│   ├── roleradar-docker.ps1                # Cross-platform CLI/MCP container execution wrapper
│   ├── test-images.ps1                     # Test harness launching tests against container images
│   ├── denylist-check.ps1                  # Updated for cross-platform path resolution
│   ├── start-services.ps1                  # Updated for forward-slash path safety
│   ├── stop-services.ps1                   # Updated for forward-slash path safety
│   ├── test-all.ps1                        # Updated for forward-slash path safety
│   ├── test-fast.ps1                       # Updated for forward-slash path safety
│   └── verify-action-pins.ps1              # Action pin verification
└── tests/
    ├── RoleRadar.Tests.Contract/
    │   ├── Fixtures/
    │   │   ├── SharedMongoFixture.cs       # Updated with ExternalMongoSafetyGuard & opt-in support
    │   │   └── ExternalMongoSafetyGuard.cs # Pure safety guard component for external DB mode
    │   └── ExternalDatabaseGuardUnitTests.cs # Unit tests for guard decision table
    ├── RoleRadar.Tests.Integration/
    │   ├── Hosting/
    │   │   ├── ContainerStartupSmokeTests.cs   # Smoke tests executing inside container
    │   │   ├── ContainerColdStartBenchmarkTests.cs # Cold start timing benchmark (<3.0s)
    │   │   ├── ContainerCredentialsSecurityTests.cs # Verifies 0600 tmpfs config, no argv leak
    │   │   └── ContainerImageContentScanTests.cs # Filesystem & layer scan for secrets/denylist
    │   └── Tracking/
    │       └── McpProcessClient.cs         # Updated to support container invocation target
    └── RoleRadar.Tests.Unit/
        └── Scripts/
            └── ScriptPortabilityTests.cs   # Cross-platform path & denylist resolution tests
```

---

## Complexity Tracking

*No constitutional violations exist. Clean pass.*
