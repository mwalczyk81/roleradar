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
  - For both `roleradar-mcp` and `roleradar-cli`: `mcr.microsoft.com/dotnet/runtime:10.0-noble-chiseled` (Ubuntu 24.04 noble chiseled distroless base), pinned by multi-arch index digest.
    - Zero shell (`/bin/sh` and `/bin/bash` do not exist).
    - Zero package manager (`apt`/`dpkg` absent).
    - Non-root user: `app` (UID 1654, GID 1654).
    - Size: ~35 MB compressed base.
- **Empirical Dependency Analysis for MongoDB Database Tools (`ldd` / `readelf`)**:
  - Executing `ldd` against the official `mongodump` and `mongorestore` binaries revealed dynamic linkage to `libc.so.6`, `/lib64/ld-linux-x86-64.so.2`, and `libgssapi_krb5.so.2`.
  - Transitively, `libgssapi_krb5.so.2` depends on 5 helper libraries: `libkrb5.so.3`, `libk5crypto.so.3`, `libcom_err.so.2`, `libkrb5support.so.0`, and `libkeyutils.so.1`.
  - Live container verification confirmed that extracting package `libgssapi-krb5-2` in the build stage and copying strictly those 6 shared libraries into `/lib/x86_64-linux-gnu/` (or `/lib/aarch64-linux-gnu/`) in `10.0-noble-chiseled` allows `mongodump` and `mongorestore` to run flawlessly under non-root UID 1654 without needing a package manager, shell, or full standard OS runtime in the final CLI image.
- **Vulnerability Scanner Package Visibility (dpkg status entries)**:
  - Container vulnerability scanners (such as Trivy and Syft) parse `/var/lib/dpkg/status` to catalog installed packages and their exact versions for CVE auditing.
  - When copying the Kerberos libraries from the build stage, the build also copies the corresponding `dpkg` status entries for `libgssapi-krb5-2`, `libkrb5-3`, `libk5crypto3`, `libkrb5support0`, and `libkeyutils1` into `/var/lib/dpkg/status` (or chiseled status slice).
  - An integration test asserts that the Trivy scan report explicitly lists these Kerberos packages so there are zero unindexed/unmanaged binaries or CVE blind spots.
- **Rationale**: Eliminating shells and package managers from both runtime images severely reduces the potential attack surface and eliminates entire classes of container escape and vulnerability findings.
- **Alternatives Considered**:
  - Standard Ubuntu `10.0-noble` runtime with package manager for CLI: Rejected; research proved that copying the 6 required shared libraries and dpkg status entries into `noble-chiseled` works cleanly, avoiding the presence of apt/dpkg.
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
  2. The file is created atomically with `0600` permissions (`UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite` via .NET `FileStreamOptions` with `FileMode.CreateNew`) on the RAM-backed `/tmp` mount (`/tmp/rr-tools-<uuid>.conf`). It is **never** created with loose permissions and chmodded after creation.
  3. Because `/tmp` is mounted as a `tmpfs`, the credential file lives strictly in volatile memory and touches no disk blocks.
  4. The tools are invoked with:
     ```bash
     mongodump --config=/tmp/rr-tools-<uuid>.conf --archive ...
     ```
  5. The config file is deleted immediately in a `finally` block upon process exit, with signal handlers (`SIGTERM`, `ProcessExit`) guaranteeing unlinking on unexpected termination.
- **Verification Invariants & Tests**:
  - File permissions are verified to be `0600` at the moment of first read before child execution.
  - Assert that the database password never appears in `argv`, `stdout`, `stderr`, application diagnostic logs, or audit entries.
  - Assert that no config files remain in `/tmp` after exit, including when the child process is terminated abruptly by `SIGTERM` or on process failure.
- **Rationale**: Completely prevents credentials from appearing in process argv, diagnostic streams, or persistent storage.
- **Alternatives Considered**:
  - Direct argv passing: Rejected as an unacceptable security vulnerability.
  - Environment variables: MongoDB Database Tools do not natively parse connection passwords from custom environment variables without command-line flags.

---

## 4. MongoDB Database Tools Version & Cryptographic Verification

### Decisions

#### Decision 4.1: Pinned Version, Official Release Metadata & Update Procedure
- **Choice**: MongoDB Database Tools version **100.19.1** (compatible with MongoDB 8.0.x server wire protocol).
- **Official Metadata Source**:
  - Feeds: `https://downloads.mongodb.org/tools/db/release.json` (for latest release metadata) and `https://downloads.mongodb.org/tools/db/full.json` (for version catalog and package URLs).
- **Pinned Download URLs & Cryptographic Digests**:
  - `linux-x86_64` (Ubuntu 24.04 noble):
    - URL: `https://fastdl.mongodb.org/tools/db/mongodb-database-tools-ubuntu2404-x86_64-100.19.1.tgz`
    - SHA-256: `f34b84a67ab96b4ff52571ec5d15e2148d96cb140b1ea9ef8c29ac33c48e3480`
  - `linux-arm64` (Ubuntu 24.04 noble aarch64):
    - URL: `https://fastdl.mongodb.org/tools/db/mongodb-database-tools-ubuntu2404-arm64-100.19.1.tgz`
    - SHA-256: `ebf518d9a303b36e612e40be106cd0ecc7135dc15d098626f81ac3ee8654a821`
- **Build Enforcement**: The `tools-downloader` stage executes `sha256sum -c` during image build, re-verifying the digests against `full.json`. If the download is corrupted or altered, the build fails immediately.
- **Update Procedure**: A scheduled script (`scripts/check-tools-release.ps1` or a quarterly GitHub Actions cron job) reads `full.json` for all historical/available versions and `release.json` for the current latest release. It compares the latest stable 100.x version against the Dockerfile pin and opens an automated Dependabot-style PR/issue if a newer release is published.
- **Rationale**: Guarantees supply chain integrity, eliminates binary drift, and provides an audited mechanism for staying current.

---

## 5. External Database Compatibility Test Runner & Safety Guard Architecture

### Context
To evaluate cloud managed databases (MongoDB Atlas on Azure, Azure Cosmos DB for MongoDB vCore, self-hosted containerized MongoDB) against RoleRadar's requirements (cross-collection transactions, partial unique indexes, text search), the Contract test suite must support running against an external endpoint without risking production data.

### Decisions

#### Decision 5.1: Pure, Unit-Tested Safety Guard Component
- **Choice**: Implement `ExternalMongoSafetyGuard` in the test infrastructure with deterministic rules:
  1. **Explicit Opt-In**: Requires environment variable `ROLERADAR_EXTERNAL_TEST_OPT_IN=1`. If missing, aborts immediately (`OptInNotSet`).
  2. **Driver-Based URL Parsing & TLS Enforcement**:
     - Parsed strictly using MongoDB C# driver's URL parser (`new MongoUrl(connectionString)` or `MongoUrlBuilder`), **never** substring checks.
     - `mongodb+srv://` scheme implies TLS (`UseTls == true`), unless `tls=false` or `ssl=false` is explicitly set.
     - Any explicit `tls=false` or `ssl=false` is refused for non-loopback hosts.
     - Conflicting parameters (e.g. `tls=false&ssl=true`) are refused as invalid.
     - **Loopback Definition**: `127.0.0.0/8` (any IPv4 `127.*`, e.g. `127.0.0.1`, `127.0.0.2`), IPv6 `::1`, and `localhost` (case-insensitive).
     - **Non-Loopback Definition**: Any remote hostname, IP outside `127.0.0.0/8`, and container bridge hostnames such as `host.docker.internal` (all require TLS).
  3. **Server Inspection & Permission Guard**:
     - System databases (`admin`, `config`, `local`) are permitted.
     - Executes `client.ListDatabaseNamesAsync()`. If the user account lacks the `listDatabases` privilege (e.g. `MongoCommandException` / unauthorized), execution is refused with a clear, redacted message:
       `"External database guard failed: user lacks 'listDatabases' cluster privilege to verify database prefix safety. Provide an account with cluster monitor or listDatabases privileges, or use a dedicated test cluster."`
       (Zero leakage of hostnames, connection strings, or credentials in error messages).
     - Non-system databases: If ANY database exists whose name does NOT start with `rrtest_`, the runner refuses execution with `ForeignDatabaseDetected`.
  4. **Isolation & Tracking**: Test databases are named `rrtest_<guid:N>`. Every created database is registered in an in-memory `TrackedTestDatabases` tracker.
  5. **Guaranteed Cleanup**: On test completion, test failure, or `SIGTERM`, only tracked `rrtest_<guid:N>` databases are dropped.
  6. **Zero Leaks**: All console outputs, exception messages, and capability reports strip hosts, connection strings, and credentials.
- **Unit Test Table Cases**:

| Scenario / Connection URI | Host Type | TLS Status | Opt-In Env | Result |
| :--- | :--- | :--- | :--- | :--- |
| `mongodb://localhost:27017` | Loopback | Plaintext | `1` | **Allowed** |
| `mongodb://127.0.0.2:27017` | Loopback (`127.0.0.0/8`) | Plaintext | `1` | **Allowed** |
| `mongodb://[::1]:27017` | Loopback (`::1`) | Plaintext | `1` | **Allowed** |
| `mongodb+srv://cluster.example.com/` | Remote (SRV) | Implied TLS | `1` | **Allowed** |
| `mongodb://remote.example.com:27017/?tls=true` | Remote | Explicit TLS | `1` | **Allowed** |
| `mongodb+srv://cluster.example.com/?tls=false` | Remote (SRV) | Explicit `tls=false` | `1` | **Rejected** (`TlsRequired`) |
| `mongodb://host.docker.internal:27017/?tls=false` | Non-loopback | Explicit `tls=false` | `1` | **Rejected** (`TlsRequired`) |
| `mongodb://remote.example.com:27017` | Remote | Plaintext (default) | `1` | **Rejected** (`TlsRequired`) |
| `mongodb://remote.example.com:27017/?tls=false&ssl=true` | Remote | Conflicting TLS flags | `1` | **Rejected** (`InvalidUri`) |
| Remote cluster with account lacking `listDatabases` | Remote (TLS) | Valid TLS | `1` | **Rejected** (`PermissionMissing`) |
| Remote cluster with non-test DB `production_db` | Remote (TLS) | Valid TLS | `1` | **Rejected** (`ForeignDatabaseDetected`) |
| Missing opt-in variable | Any | Any | (unset) | **Rejected** (`OptInNotSet`) |

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
- **Choice**: GitHub Actions runs the ARM64 test job on native ARM64 runners (`ubuntu-24.04-arm`), confirmed against GitHub Actions documentation for public repositories, never under QEMU emulation.
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
