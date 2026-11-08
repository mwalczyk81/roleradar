# ADR 0024: Monorepo Orchestration with Nx and @nx/dotnet

- **Status**: Accepted
- **Date**: 2026-10-31
- **Feature**: `005b-management-ui`

## Context and Problem Statement

RoleRadar has evolved from a pure .NET solution into a multi-language repository containing:

1. .NET 10 projects (`src/RoleRadar.Domain`, `src/RoleRadar.Application`, `src/RoleRadar.Infrastructure`, `src/RoleRadar.Identity`, `src/RoleRadar.Api`, `src/RoleRadar.Mcp.Stdio`, `src/RoleRadar.Cli`, and four test projects under `tests/`).
2. A TypeScript / React Router v7 web management application under `web/`.
3. Contract definitions under `contracts/`.

Requirement FR-024 mandates unified monorepo orchestration using Nx with the first-party `@nx/dotnet` plugin (Nx 22 or later) managing both .NET and web projects from the root `package.json` with a pinned pnpm lockfile.

In addition:

- The third-party `@nx-dotnet/core` plugin is unmaintained and must NOT be used.
- Contract and Integration test projects depend on Docker and Testcontainers with ephemeral state; their Nx task results must NEVER be cached.
- .NET 10 project test detection must work reliably with Nx.

How should the monorepo build, lint, and test tasks be configured?

## Decision Drivers

- **First-Party Support**: Use official `@nx/dotnet` maintained by Nrwl/Nx.
- **Reproducible CI Workflow**: Ensure locked dependency restoration (`dotnet restore --locked-mode` and `pnpm install --frozen-lockfile`) precedes affected execution.
- **Cache Correctness**: Unit and lint tasks can be cached; Docker/Testcontainers integration tests and contract tests must never execute from stale cache.
- **Compatibility with .NET 10**: Prevent test runner detection conflicts with .NET 10's Microsoft.Testing.Platform.

## Decision Outcome

Chosen configuration: **Nx 24.0.3 + @nx/dotnet 24.0.3 + Root package.json + pnpm packageManager**.

### 1. Pinned Dependencies & Tooling

- `nx`: `24.0.3`
- `@nx/dotnet`: `24.0.3`
- `@nx/js`: `24.0.3`
- `packageManager`: `pnpm@10.4.0`
- `node`: `22.23.4` (LTS, pinned in `.node-version` and `engines`)
- `typescript`: `5.9.3` (newest stable on the 5.x branch; 6.x is in beta)

### 2. TypeScript Compilation & Vendored Design System (`allowJs`)

In `web/tsconfig.json`:

- `allowJs: true` and `checkJs: false` allow importing vendored `.jsx` design system components without typecheck errors in the vendored files.
- All application code under `web/app/**` outside `web/app/ds/**` is authored in strictly typed TypeScript (`strict: true`, `noImplicitAny: true`).
- Lint rules prohibiting raw hex colors and pixel values explicitly exclude `web/app/ds`.

### 3. Monorepo Project Layout

```text
roleradar/
├── .node-version                # 22.23.4 (LTS)
├── nx.json                      # Nx monorepo config
├── package.json                 # Root orchestration package.json
├── pnpm-lock.yaml               # Pinned pnpm lockfile
├── RoleRadar.sln                # Root .NET 10 solution
├── Directory.Build.props        # Global MSBuild properties (IsTestingPlatformApplication=false)
├── Directory.Packages.props     # Central Package Management (CPM)
├── contracts/                   # OpenAPI contracts and TypeScript generator
├── src/                         # .NET domain, application, infra, and host projects
├── tests/                       # .NET architecture, unit, contract, integration tests
└── web/                         # React Router v7 management UI (Nx project)
    ├── package.json
    ├── project.json
    ├── vite.config.ts
    └── app/
```

### 3. Caching Invariants in `nx.json`

Nx task caching is selectively configured:

- `build`, `lint`, and `unit` targets for `web` and .NET libraries have `cache: true`.
- For `RoleRadar.Tests.Contract` and `RoleRadar.Tests.Integration`, task caching is explicitly disabled (`"cache": false` in target defaults or project configurations) because they execute against Docker daemons and Testcontainers.

```json
{
  "$schema": "./node_modules/nx/schemas/nx-schema.json",
  "targetDefaults": {
    "build": {
      "cache": true,
      "dependsOn": ["^build"]
    },
    "lint": {
      "cache": true
    },
    "test": {
      "cache": true
    }
  },
  "plugins": [
    {
      "plugin": "@nx/dotnet",
      "options": {
        "solutionFile": "RoleRadar.sln",
        "inferProjects": true
      }
    }
  ]
}
```

### 4. .NET 10 Compatibility Invariant

In `Directory.Build.props`, the property `<IsTestingPlatformApplication>false</IsTestingPlatformApplication>` is explicitly set across all test projects. This prevents MSBuild from emitting standalone Testing Platform executables that bypass traditional VSTest discovery, ensuring `@nx/dotnet` can invoke `dotnet test` transparently.

### 5. CI Workflow Shape, Required Ruleset Checks, and Affected Orchestration

The GitHub Actions CI pipeline (`.github/workflows/ci.yml`) is structured to execute only what a change affects while strictly preserving security and quality invariants:

#### Job Topology and Action Pins

1. **`changes` (Detect Changed Paths)**:
   - Runner: `ubuntu-24.04`
   - Uses `dorny/paths-filter@ceb8a2b8f2d89434be7ff52d3de7ec3738c5cc9d # v4.0.3`
   - Evaluates filter sets:
     - `backend`: `.github/workflows/**`, `.editorconfig`, `NuGet.Config`, `.pre-commit-config.yaml`, `src/**`, `tests/**`, `contracts/**`, `RoleRadar.sln`, `Directory.Build.props`, `Directory.Packages.props`, `global.json`, `scripts/**`
     - `web`: `.github/workflows/**`, `.editorconfig`, `.pre-commit-config.yaml`, `web/**`, `contracts/**`, `package.json`, `pnpm-lock.yaml`, `nx.json`
     - `images`: `.github/workflows/**`, `.dockerignore`, `Dockerfile`, `deploy/**`, `src/**`, `web/**`
     - `contracts`: `contracts/**`

2. **`pre-commit` (Hygiene, Gitleaks, Zizmor & Action Pin Verification)**:
   - Runner: `ubuntu-24.04`
   - **Unconditional**: Executes on all pushes and pull requests regardless of changed paths.
   - Runs `pre-commit` (gitleaks, markdownlint, zizmor, hygiene, forbidden-paths guard), `scripts/verify-action-pins.ps1`, and the zero-drift OpenAPI check (`git diff --exit-code -- contracts`).

3. **`windows-verify` (Windows Build & Architecture Tests)**:
   - Check name: `Windows (Unit, Architecture & PSScriptAnalyzer)`
   - Runner: `windows-latest`
   - Gating: `needs.changes.outputs.backend == 'true'` (plus scheduled / manual triggers).
   - Runs `PSScriptAnalyzer`, locked restore, format verification, solution build, unit/architecture tests, and script portability tests.
   - **Skipped** on web-only, docs-only, and dockerignore-only changes.

4. **`ubuntu-build-and-test` (Backend Testcontainers & Container Security)**:
   - Check name: `Ubuntu (Build, Testcontainers & Container Security)`
   - Runner: `ubuntu-24.04`
   - Gating: `needs.changes.outputs.backend == 'true' || needs.changes.outputs.images == 'true'`
   - Internal step gating:
     - `Run DockerIgnore coverage unit tests` executes `dotnet test tests/RoleRadar.Tests.Unit -c Release --no-build --filter "FullyQualifiedName~DockerIgnoreCoverageTests"` whenever `backend == 'true' || images == 'true'`.
     - Full test suite with Testcontainers and >=85% line coverage gate on Domain and Application runs **only if `backend == 'true'`**.
     - Hadolint, Docker image builds (CLI, MCP, Identity, API, Web), Trivy vulnerability scans, CycloneDX SBOMs, and launched-host container tests run **only if `images == 'true'`**.
   - **Invariant**: A web-only or docs-only change never triggers the Testcontainers integration suite; a `.dockerignore`-only change executes DockerIgnore unit tests and image validation without the slow integration database overhead.

5. **`web` (Web Management UI Build, Lint & Test)**:
   - Check name: `Web (Build, Lint & Test)`
   - Runner: `ubuntu-24.04` (hosted on the existing Ubuntu runner; no new OS matrix).
   - Gating: `needs.changes.outputs.web == 'true'`
   - Execution Sequence:
     1. `actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1` (`fetch-depth: 0`)
     2. `nrwl/nx-set-shas@e602fada7ad5987b0f7b1905e7a99ea1c661514c # v5.1.0` (derives `NX_BASE` from the PR target branch)
     3. `actions/setup-node@949feb2413d6458794dcd2491c4babbbce0c15c1 # v7.1.0` (`node-version: 22.x`)
     4. `pnpm/action-setup@ea17c68df8912ef543352723c149a84f56e3d413 # v6.1.0` (`version: 10.4.0`)
     5. `actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68 # v6.0.0`
     6. `dotnet restore RoleRadar.sln --locked-mode`
     7. `pnpm install --frozen-lockfile`
     8. `pnpm exec nx affected -t lint build test`

6. **`ci-gate` (Final Required Status Checks Gate)**:
   - Check name: `Ubuntu (Build, Testcontainers, >=85% Coverage & Pre-Commit)`
   - Runner: `ubuntu-24.04`
   - Gating: Unconditional (`if: always()`), `needs: [changes, pre-commit, ubuntu-build-and-test, web, windows-verify]`.
   - **Ruleset Alignment Pattern**: The repository's "Main" branch ruleset requires the exact status check names:
     - `Ubuntu (Build, Testcontainers, >=85% Coverage & Pre-Commit)`
     - `Windows (Unit, Architecture & PSScriptAnalyzer)`
     - `Dependency Review`
     - `CodeQL`
   - **Amended 2026-11-08**: read from the live ruleset, the required checks are the gate (`Ubuntu (Build, Testcontainers, >=85% Coverage & Pre-Commit)`), `CodeQL` and `Dependency Review`, plus a code-scanning rule on CodeQL. `Windows (Unit, Architecture & PSScriptAnalyzer)` is not listed separately because the gate already requires it (`windows-verify`). See `docs/repo-settings.md`.
   - Rather than altering the branch ruleset, the actual Ubuntu build, test, and container security workload runs under the renamed job `Ubuntu (Build, Testcontainers & Container Security)`, while this gate job assumes the exact check name `Ubuntu (Build, Testcontainers, >=85% Coverage & Pre-Commit)`.
   - In a single evaluation step, the gate checks the results of `changes` and all downstream jobs (`pre-commit`, `ubuntu-build-and-test`, `web`, `windows-verify`).
   - The gate strictly requires that `changes` is `success` (a failed or cancelled `changes` job causes downstream jobs to skip, which must not pass) and that every needed job result is either `success` or `skipped`. Any result of `failure` or `cancelled` fails the gate.
   - This ensures `pre-commit` (gitleaks, hygiene) and `web` are transitively required on every pull request under the required status checks without requiring ruleset modifications.

#### Change Scenarios Matrix

| Scenario | Files Changed (Example) | `changes` | `pre-commit` | `windows-verify` | DockerIgnore Tests | Testcontainers (`ubuntu`) | Images (`ubuntu`) | `web` Job | `ci-gate` | Result |
| :--- | :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :--- |
| **Docs-Only** | `docs/**`, `specs/**`, `README.md` | Success | **Runs** | Skipped | Skipped | Skipped | Skipped | Skipped | **Passes** | Runs only pre-commit and gate |
| **Web-Only** | `web/**`, `web/package.json` | Success | **Runs** | Skipped | **Runs** | Skipped | **Runs** | **Runs** | **Passes** | Fast web validation + web container build |
| **Backend-Only** | `src/**`, `tests/**`, `RoleRadar.sln` | Success | **Runs** | **Runs** | **Runs** | **Runs** | **Runs** | Skipped | **Passes** | Backend tested; web UI untouched |
| **Workflow-Only** | `.github/workflows/**` | Success | **Runs** | **Runs** | **Runs** | **Runs** | **Runs** | **Runs** | **Passes** | All jobs and suites execute |
| **Dockerignore-Only** | `.dockerignore` | Success | **Runs** | Skipped | **Runs** | Skipped | **Runs** | Skipped | **Passes** | DockerIgnore tests + image builds/scans |
| **Failed-Changes** | *(any change)* | **Failure** | **Runs** | Skipped | Skipped | Skipped | Skipped | Skipped | **Fails** | Gate catches changes failure and aborts PR |

Automated verification is implemented in `scripts/verify-ci-affected.ps1`, which dynamically parses the filter definitions and job/step `if:` expressions directly from `.github/workflows/ci.yml` at runtime.

> **Note (2026-11-04)**: `scripts/verify-ci-affected.ps1` was removed. Nothing ran it (it was not called from `ci.yml`, pre-commit, `package.json`, the nx configuration or any other script), and it re-implemented the workflow's path filters and `if:` expressions in a second place that had to be kept in step by hand. The `ci-gate` job in `.github/workflows/ci.yml` is the verification: it aggregates the result of every job and fails the pull request when a job that should have run did not succeed. The scenario table above still describes the intended behaviour.

## Consequences

### Positive

- A single command (`pnpm exec nx run-many -t build test lint`) validates the entire repository across .NET and TypeScript.
- Affected analysis accelerates CI by only building and testing projects impacted by git diffs.
- Avoids unmaintained `@nx-dotnet/core` vulnerabilities and deprecations.
- Strict gating ensures web developers do not wait on Testcontainers spin-up and docs contributors only wait on pre-commit hygiene.

### Negative / Trade-offs

- Developers must have Node 22 and pnpm installed locally in addition to the .NET 10 SDK.
