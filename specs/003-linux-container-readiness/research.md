# Research & Technical Decisions: Linux Container Readiness for Azure-Style Hosting

**Feature**: `003-linux-container-readiness`  
**Date**: 2026-10-18  
**Status**: Completed (Phase 0)

## 1. Multi-Stage Container Image Architecture & Base Image Selection

### Context
RoleRadar requires hardened, minimal Linux container images for two distinct host executables:
1. `roleradar-mcp`: Stdio-first Model Context Protocol (MCP) server used by AI agent hosts (Claude Code, Desktop, Cowork).
2. `roleradar-cli`: Command-line interface for administrative operations (`status`, `backup`, `restore`, `wipe`, `verify`).

Both images must support `linux/amd64` and `linux/arm64` CPU architectures natively, run as a non-privileged user, support read-only root filesystems (`--read-only`), and adhere to strict supply-chain hygiene.

### Decisions

#### Decision 1.1: Single Multi-Stage Dockerfile with Dedicated Target Stages
- **Choice**: A single `Dockerfile` located at the repository root defining target stages:
  - `build`: Pinned .NET 10 SDK (`mcr.microsoft.com/dotnet/sdk:10.0.402@sha256:...`) matching `global.json`, restoring dependencies with `dotnet restore --locked-mode` and publishing framework-dependent binaries (`-c Release -r linux-x64` / `-r linux-arm64` via `--no-restore`).
  - `tools-downloader`: Dedicated curl/tar stage downloading MongoDB Database Tools for the target architecture, verifying SHA-256 digests, and isolating binaries.
  - `mcp`: Final minimal runtime image for `RoleRadar.Mcp.Stdio`.
  - `cli`: Final runtime image for `RoleRadar.Cli` bundling verified `mongodump` and `mongorestore`.
- **Rationale**: Keeps dependency pinning, build logic, and CI target tags centralized in a single file while allowing independent multi-stage image builds (`docker build --target mcp` and `docker build --target cli`).
- **Alternatives Considered**:
  - Separate `Dockerfile.mcp` and `Dockerfile.cli`: Rejected to avoid duplicating .NET SDK pins, locked restore steps, and base image maintenance.

#### Decision 1.2: Base Runtime Image Selection and Distroless/Chiseled Posture
- **Choice**:
  - For `roleradar-mcp`: `mcr.microsoft.com/dotnet/runtime:10.0-noble-chiseled` (or `runtime-deps:10.0-noble-chiseled`), pinned by multi-arch index digest.
    - Zero shell (`/bin/sh` does not exist).
    - Zero package manager (`apt`/`dpkg` absent).
    - Non-root user: `app` (UID 1654, GID 1654).
    - Size: ~35 MB compressed.
  - For `roleradar-cli`: `mcr.microsoft.com/dotnet/runtime:10.0-noble` (Ubuntu 24.04 noble base), pinned by multi-arch index digest.
    - Bundles MongoDB Database Tools (`mongodump`, `mongorestore`) which are Go binaries dynamically linked against `libc6` (glibc) and `libresolv`.
    - Non-root user: `app` (UID 1654, GID 1654).
    - Package manager present in base layer but strictly uninvoked at build/run time.
- **Rationale**: `mcp` has no external binary dependencies, so distroless/chiseled provides maximum exploit mitigation. `cli` requires glibc shared library dependencies for MongoDB Database Tools; standard `noble` runtime guarantees compatibility without runtime package installs.
- **Alternatives Considered**:
  - Alpine Linux (`musl`): Rejected because MongoDB Database Tools are distributed with official glibc builds; running glibc Go binaries on musl via `gcompat` introduces instability and supply chain complexity.
  - Full SDK image at runtime: Rejected; violates minimal privilege and introduces vast attack surfaces.

---

## 2. Container Runtime Constraints & Supported Execution Mode

### Decisions

#### Decision 2.1: Hardened Execution Mode Specification
- **Choice**: The only formally supported and tested execution configuration is:
  ```bash
  docker run --read-only \
    --tmpfs /tmp:rw,noexec,nosuid,size=64m \
    --cap-drop ALL \
    --security-opt no-new-privileges \
    -e DOTNET_EnableDiagnostics=0 \
    -e DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 \
    -e DOTNET_gcServer=0 \
    -i --rm <image>
  ```
- **Rationale**:
  - `--read-only`: Guarantees immutability; malware or accidental bugs cannot write to rootfs.
  - `--tmpfs /tmp`: Designated in-memory volatile temporary filesystem for .NET temp files and RAM-only credentials config.
  - `--cap-drop ALL` + `--security-opt no-new-privileges`: Strips all Linux capabilities and prevents privilege escalation.
  - `DOTNET_EnableDiagnostics=0`: Prevents creation of Unix domain sockets (`/tmp/.dotnet/`) that require write access and could expose debugging surfaces.
  - `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`: Avoids ICU package dependencies unless specific locale formatting is proven necessary.
  - `DOTNET_gcServer=0`: Workstation GC reduces startup latency and memory overhead for single-session CLI and stdio tools.

---

## 3. Security-Critical Credential Handling for MongoDB Database Tools

### Context
When the CLI executes `mongodump` or `mongorestore`, passing connection strings with passwords on argv (e.g. `--uri "mongodb://user:pass@host/..."`) exposes credentials in `/proc/<pid>/cmdline` and OS process tables (`ps aux`).

### Decisions

#### Decision 3.1: Volatile In-Memory Config File (`--config`) on tmpfs
- **Choice**:
  1. The CLI formats a temporary configuration file matching MongoDB Tools YAML syntax:
     ```yaml
     uri: "mongodb://<user>:<password>@<host>:27017/..."
     ```
  2. The file is written to `/tmp/rr-tools-<uuid>.conf` with `0600` permissions (read/write only by UID 1654).
  3. Because `/tmp` is mounted as a `tmpfs`, the credential file lives strictly in volatile memory and touches no disk blocks.
  4. The tools are invoked with:
     ```bash
     mongodump --config=/tmp/rr-tools-<uuid>.conf --archive ...
     ```
  5. The config file is deleted immediately in a `finally` block upon process exit, with signal handlers (`SIGTERM`, `ProcessExit`) guaranteeing unlinking on unexpected termination.
- **Rationale**: Completely prevents credentials from appearing in process argv, diagnostic streams, or persistent storage.
- **Alternatives Considered**:
  - Direct argv passing: Rejected as an unacceptable security vulnerability.
  - Environment variables: MongoDB Database Tools do not natively parse connection passwords from custom environment variables without command-line flags.

---

## 4. MongoDB Database Tools Version & Cryptographic Verification

### Decisions

#### Decision 4.1: Pinned Version & Checksum Verification
- **Choice**: MongoDB Database Tools version **100.11.0** (compatible with MongoDB 8.0.x server wire protocol).
- **Digests**:
  - `linux-x86_64`: SHA-256 digest pinned in Dockerfile.
  - `linux-arm64`: SHA-256 digest pinned in Dockerfile.
- **Build Enforcement**: The `tools-downloader` stage executes `sha256sum -c` during image build. If the download is corrupted or altered, the build fails immediately.
- **Rationale**: Guarantees supply chain integrity and prevents unvetted upstream binary drift. Replaces previous assumptions of MongoDB 7.0 baseline.

---

## 5. External Database Compatibility Test Runner & Safety Guard Architecture

### Context
To evaluate cloud managed databases (MongoDB Atlas on Azure, Azure Cosmos DB for MongoDB vCore, self-hosted containerized MongoDB) against RoleRadar's requirements (cross-collection transactions, partial unique indexes, text search), the Contract test suite must support running against an external endpoint without risking production data.

### Decisions

#### Decision 5.1: Pure, Unit-Tested Safety Guard Component
- **Choice**: Implement `ExternalMongoSafetyGuard` in the test infrastructure with deterministic rules:
  1. **Explicit Opt-In**: Requires environment variable `ROLERADAR_EXTERNAL_TEST_OPT_IN=1`. If missing, aborts immediately.
  2. **TLS Enforcement**: If the connection string host is not loopback (`127.0.0.1`, `localhost`, `::1`), TLS is strictly required (`tls=true` or `ssl=true`). Unencrypted remote connections are rejected.
  3. **Server Inspection & Prefix Guard**:
     - System databases (`admin`, `config`, `local`) are permitted.
     - Non-system databases: If ANY database exists whose name does NOT start with `rrtest_`, the runner refuses execution with `ForeignDatabaseDetected`.
  4. **Isolation & Tracking**: Test databases are named `rrtest_<guid:N>`. Every created database is registered in an in-memory `TrackedTestDatabases` tracker.
  5. **Guaranteed Cleanup**: On test completion, test failure, or `SIGTERM`, only tracked `rrtest_<guid:N>` databases are dropped.
  6. **Zero Leaks**: All console outputs, exception messages, and capability reports strip hosts, connection strings, and credentials.
- **Decision Table**:

| Scenario | Server Databases | TLS Status | Opt-In Env | Result |
| :--- | :--- | :--- | :--- | :--- |
| Empty local server | None | Plaintext | `1` | **Allowed** |
| Local with system only | `admin`, `local` | Plaintext | `1` | **Allowed** |
| Local with existing test DBs | `admin`, `rrtest_abc` | Plaintext | `1` | **Allowed** |
| Remote with TLS | `admin`, `config` | TLS Enabled | `1` | **Allowed** |
| Remote without TLS | `admin` | Plaintext | `1` | **Rejected** (`TlsRequired`) |
| Foreign database present | `admin`, `production_db` | TLS Enabled | `1` | **Rejected** (`ForeignDatabaseDetected`) |
| Missing opt-in env | `admin` | TLS Enabled | (unset) | **Rejected** (`OptInNotSet`) |

---

## 6. Cold-Start Performance Benchmarking & Native ARM64 Runners

### Decisions

#### Decision 6.1: Benchmark Protocol
- **Metric**: Elapsed wall-clock time from `Process.Start` of `docker run` to the receipt of the first complete, valid JSON-RPC frame on standard output.
- **Conditions**:
  - Measured with container images pre-built / pre-pulled.
  - MongoDB replica set already initialized and passing healthchecks.
  - Benchmarked independently on `linux/amd64` and native `linux/arm64`.
- **Target**: **< 3.0 seconds** on standard CI runners for both architectures.

#### Decision 6.2: Native ARM64 CI Execution
- **Choice**: GitHub Actions runs the ARM64 test job on native ARM64 runners (`ubuntu-24.04-arm64` or self-hosted native ARM64 runner), never under QEMU emulation.
- **Rationale**: QEMU CPU emulation introduces massive 5x-10x latency penalties, produces false positives in concurrency tests, and invalidates cold-start timing benchmarks.

---

## 7. Supply Chain Security, Vulnerability Policy & CI Pipeline

### Decisions

#### Decision 7.1: Vulnerability Policy & Allowlist Governance
- **Choice**:
  - CI runs `trivy` container scanning on both built images.
  - Build fails on any `CRITICAL` vulnerability that has an upstream fix available (`--ignore-unfixed=false`).
  - Unfixed critical findings are permitted only if documented in `.trivy-allowlist.yaml` containing:
    - CVE identifier
    - Package name and installed version
    - Expiration date (hard failure if current date > expiry)
    - Justification and tracking issue link
  - Full vulnerability scan reports and Software Bill of Materials (SBOM via Syft/Trivy) are uploaded as CI job artifacts.

#### Decision 7.2: Cross-Platform Path Handling & CI OS Scopes
- **Choice**:
  - `Ubuntu Linux`: Full suite (image builds, `hadolint`, `trivy`, SBOM, launched-host tests against images, compose tests, external database mode).
  - `Windows` and `macOS`: Compilation, formatting (`dotnet format`), linting, unit tests, architecture tests, and Pester cross-platform script tests (omitting Docker-dependent suites).
  - Scripts updated to use `Join-Path` and platform-specific denylist resolution (`$XDG_DATA_HOME` / `~/.local/share/roleradar/private/denylist.txt` on Linux/macOS).
