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

### 5. CI Execution Sequence

1. `pnpm install --frozen-lockfile`
2. `dotnet restore --locked-mode`
3. `pnpm exec nx affected -t build test lint`

## Consequences

### Positive

- A single command (`pnpm exec nx run-many -t build test lint`) validates the entire repository across .NET and TypeScript.
- Affected analysis accelerates CI by only building and testing projects impacted by git diffs.
- Avoids unmaintained `@nx-dotnet/core` vulnerabilities and deprecations.

### Negative / Trade-offs

- Developers must have Node 22 and pnpm installed locally in addition to the .NET 10 SDK.
