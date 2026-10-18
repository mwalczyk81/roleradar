# Contract: Continuous Integration (CI) Workflow & Security Governance

**Feature**: `003-linux-container-readiness`  
**Date**: 2026-10-18  
**Status**: Approved

## 1. Operating System Runner Matrix & Responsibility Split

GitHub Actions workflows are split across runners based on host virtualization capabilities:

```text
├── ubuntu-latest (Linux amd64): Full build, lint, container image tests, compose integration, security scans
├── ubuntu-24.04-arm (Linux arm64 native): Container build and launched-host parity tests (no emulation)
├── windows-latest: .NET build, format, lint, unit tests, architecture tests, PSScriptAnalyzer, script portability
└── macos-latest: .NET build, format, lint, unit tests, architecture tests, cross-platform script portability
```

### 1.1 Job Matrix Specification

| Job Name | Runner OS | CPU Arch | Scope |
| :--- | :--- | :--- | :--- |
| `ubuntu-build-and-test` | `ubuntu-latest` | `x86_64` | Multi-stage Docker build, hadolint, trivy scan, SBOM, launched-host tests against images, compose integration, coverage gate (>=85%). |
| `linux-arm64-verify` | `ubuntu-24.04-arm` | `aarch64` | Native ARM64 multi-stage image build, native launched-host test execution under `docker run --read-only --tmpfs /tmp`, cold-start timing (<3.0s). |
| `windows-verify` | `windows-latest` | `x86_64` | .NET 10 compilation (`-warnaserror`), `dotnet format --verify-no-changes`, unit tests, architecture tests, PSScriptAnalyzer on scripts. |
| `macos-verify` | `macos-latest` | `arm64` | .NET 10 compilation (`-warnaserror`), `dotnet format --verify-no-changes`, unit tests, architecture tests, Pester script portability tests. |

---

## 2. Supply-Chain & Linting Tooling

1. **Dockerfile Linting (`hadolint`)**:
   - Executes in the `ubuntu-latest` job.
   - Verifies digest pinning, non-root user setup, uninvoked package managers, and standard instructions.
2. **Container Vulnerability Scanning (`trivy`)**:
   - Scans both `roleradar-cli` and `roleradar-mcp`.
   - Fails the build on any `CRITICAL` severity vulnerability that has an upstream vendor fix available.
   - Any unfixed critical vulnerability must match an active entry in `.trivy-allowlist.yaml` with a valid future expiration date.
   - Scan results (`trivy-report-cli.json`, `trivy-report-mcp.json`) uploaded as GitHub Actions artifacts.
3. **Software Bill of Materials (SBOM)**:
   - Generated using `syft` or `trivy` in SPDX or CycloneDX JSON format.
   - Retained as build artifacts.
4. **Action SHA Pinning**:
   - Every GitHub Action step is pinned to an immutable commit SHA (enforced by `scripts/verify-action-pins.ps1`).
   - Clean verification under `zizmor`.
