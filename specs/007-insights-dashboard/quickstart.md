# Quickstart: validating 007 end to end

Everything here runs against throwaway Testcontainers databases or loopback stubs. Never point any of it at the developer's running Mongo or stack (AGENTS.md). Report numbers from command output only.

## Prerequisites

- Docker running (integration tests start their own MongoDB replica sets).
- Node 22 and pnpm; `pnpm install` done.
- For Phase 2 and later: design kit v20 synced (plan, Phase 0). `web/app/ds/SYNC.md` must name v20.

## 1. Fast loop (while working)

```powershell
dotnet build RoleRadar.sln -c Release -v q --nologo -warnaserror
pwsh ./scripts/test-fast.ps1
pnpm --filter web run typecheck
pnpm --filter web run lint
pnpm --filter web run test
```

Expected: exit 0 on each. Each phase's first step is to write tests and run them **red**; paste that failing output into the phase report before implementing.

## 2. Phase 1: flags and filters

```powershell
dotnet test tests/RoleRadar.Tests.Integration -c Release --filter "FullyQualifiedName~Insights"
dotnet test tests/RoleRadar.Tests.Unit -c Release --filter "FullyQualifiedName~ApplicationMilestone"
dotnet test tests/RoleRadar.Tests.Architecture -c Release
dotnet test tests/RoleRadar.Tests.Contract -c Release --filter "FullyQualifiedName~ODataMetadataDrift"
```

Expected: seeded postings and applications carry the right flags; legacy documents without flags are fixed by a reconcile run and a second dry run reports 0; a posting edit does not clear `has_application`; the architecture test rejects any writer outside the allowlist; `$top=0` behavior is recorded.

## 3. Phase 2: dashboard and drill-down parity

```powershell
pnpm --filter web run test            # lens unit tests, route tests, drift test vs contracts/insights-lenses.json
pnpm --filter web run build
pnpm --filter web run test:e2e        # Chromium: funnel, tiles, drill-down, a11y, light and dark, 360px
dotnet test tests/RoleRadar.Tests.Integration -c Release --filter "FullyQualifiedName~InsightsParity"
```

Expected: for every lens in both windows, `@odata.count` equals the oracle and the list's `@odata.count`; the isolation guard test passes; axe reports no violations in light and dark.

## 4. Phase 3: trends and response time

```powershell
dotnet test tests/RoleRadar.Tests.Unit -c Release --filter "FullyQualifiedName~InsightsStats"
dotnet test tests/RoleRadar.Tests.Integration -c Release --filter "FullyQualifiedName~InsightsStats"
dotnet test tests/RoleRadar.Tests.Contract -c Release --filter "FullyQualifiedName~OpenApi"
pnpm --filter web run test:e2e
```

Expected: median and weekly buckets match hand-computed values, `sampleSize` equals the `responded` lens count, OpenAPI snapshot and generated types are in sync.

## 5. Phase 4: notes Markdown (independent)

```powershell
pnpm --filter web run test
pnpm --filter web run build
pnpm --filter web run test:e2e
```

Expected: hostile fixtures render inert, zero off-origin requests, zero CSP violations, no `rehype-raw` and no `dangerouslySetInnerHTML` in the tree.

## 6. End of each phase

```powershell
pnpm --filter web run typecheck
pwsh ./scripts/test-fast.ps1
pre-commit run --all-files
pwsh ./scripts/review-gate.ps1
```

Stop at the phase boundary. Do not commit.

## Operator step for Matt (after Phase 1 is merged and deployed)

Follow [docs/operations/reconcile-read-models.md](../../docs/operations/reconcile-read-models.md) exactly: deploy, `roleradar backup`, dry run, `roleradar reconcile-read-models --confirm RECONCILE-READ-MODELS`, dry run again (must report 0 updated for postings, applications and criteria). Until this is done the Insights page shows "needs backfill" for flag-dependent numbers by design. Agy, subagents and CI never run this against the real database.
