# Implementation Plan: Contracts and Test Integrity

**Branch**: `010-contracts-test-integrity` | **Date**: 2026-11-06 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/010-contracts-test-integrity/spec.md`

## Summary

Make three claims true. (1) Generate enum values into the OpenAPI schema and client types from one registry, and delete the snapshots that registry replaces. (2) Make the container scan test read real Trivy reports and make gate selection trait-based, with a test that fails when a test class is not run by any lane. (3) Diagnose three flakes with evidence and fix or record each. Plus a small Login fix. Mechanism choices and evidence are in [research.md](research.md).

## Technical Context

**Language/Version**: .NET 10 (C#, nullable, warnings as errors), TypeScript on Node 22 (React Router v7 SSR), PowerShell 7 scripts

**Primary Dependencies**: existing only. `Microsoft.AspNetCore.OpenApi` schema transformers, `openapi-typescript 7.10.1`, xUnit v3, Playwright, Trivy `0.59.1` (CI image, already pinned). No new packages (constitution VIII).

**Storage**: N/A (no data model or Mongo change)

**Testing**: Architecture, Unit, Contract, Integration (Testcontainers), web vitest and Playwright. No new test projects.

**Target Platform**: Windows dev, Linux CI and containers

**Project Type**: multi-project .NET solution with a web app

**Performance Goals**: none; fast loop must not slow noticeably (registry test is reflection only)

**Constraints**: OData metadata and `$filter` behaviour unchanged; no real data; LF endings; no commit or push; stdout reserved for MCP frames

**Scale/Scope**: 5 mandatory enum properties (plus other exposed enums), 4 snapshot files and their tests, 4 scripts or workflow lines for lanes, 3 flakes, 1 Login page branch

## Constitution Check

| Principle | Status |
|-----------|--------|
| I Privacy | Pass. Seeded scan reports and fixtures use fictional CVE ids and image names. Flake evidence must not capture real data; logs are test containers only. |
| II Tests first, isolated containers | Pass. Every story starts red. Flake loops run only in isolated Testcontainers or throwaway `docker run`; never the developer's running Mongo. |
| III Architecture tests | Pass. No layering change; registry lives in Api, which already references Application and Domain. |
| IV Named seams | Pass. Registry is a plain static data class, no generic abstraction. |
| V Untrusted text | N/A |
| VI stdout hygiene | Pass. No change to Mcp.Stdio. |
| VII Durable data, destructive scripts | Pass. No destructive script added. |
| VIII ADRs, pinned versions | Pass with action: ADR 0035 (generated enums) and ADR 0036 (test lanes and gate scan). No new unpinned packages; Trivy stays on the pinned `0.59.1` image. |
| IX .NET 10, UTC, owner_id | Pass. No new collections. |

AGENTS.md rules checked: "Contract first" bullet is amended by this feature; "Never run against the developer's running Mongo" respected; the "web visual" and Linux CI gate must be run before the web phase boundary.

Re-check after design: no violations, no Complexity Tracking entries.

## Project Structure

### Documentation (this feature)

```text
specs/010-contracts-test-integrity/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/              # design notes (not the repo-root contracts/ folder)
│   ├── enum-registry.md
│   ├── scan-report-evaluation.md
│   └── test-lanes.md
├── checklists/requirements.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
src/RoleRadar.Api/OpenApi/
├── ExplicitTypeSchemaTransformers.cs     # calls the registry
└── EnumContractRegistry.cs               # new: entries + allowlist
src/RoleRadar.Identity/Pages/Account/Login.cshtml.cs   # drop TwoFactorInput state on password step

contracts/
├── openapi/roleradar-api.json            # regenerated (enum added)
├── client/api-types.ts                   # regenerated (unions)
├── disposition-matrix.json               # kept, membership arrays removed if unused
├── insights-lenses.json                  # kept
├── company-enums.json                    # deleted
├── application-enums.json                # deleted
└── operational-enums.json                # deleted

tests/RoleRadar.Tests.Contract/           # registry test, OpenAPI enum test, OData metadata unchanged test; snapshot tests for deleted files removed
tests/RoleRadar.Tests.Unit/Identity/LoginModelRazorPageTests.cs
tests/RoleRadar.Tests.Integration/
├── Hosting/ContainerImageContentScanTests.cs     # reads real Trivy reports, never writes
├── Hosting/TrivyReportEvaluator.cs (+ tests)     # new
├── Hosting/ContainerHostingConventionTests.cs    # SIGTERM flake fix + diagnostics
├── MongoContainerStartGate.cs (+ diagnostics tests)
└── TestLaneCoverageTests.cs                      # new, reflection only

web/app/lib/{dispositions,company-enums,application-enums}.ts   # keyed by generated unions; labels and order only
web/tests/unit/*enums*.test.ts, dispositions.test.ts            # equality halves removed, exhaustiveness kept
web/tests/e2e/visual-baseline.spec.ts, identity-signin e2e      # capture waits; login assertion

scripts/{test-images.ps1,review-gate.ps1,test-e2e-real.ps1}, .github/workflows/ci.yml   # select by Lane trait; gate runs Trivy on its review images and the scan test with ROLERADAR_REQUIRE_SCAN=1; CI Trivy severity per research R6 spike
tests/RoleRadar.Tests.Contract/OpenApi/, ODataMetadataDriftTests.cs   # existing contract tests reused (ApiOpenApiContractSnapshotTests, ODataMetadataDriftTests)
docs/adr/0035-*.md, 0036-*.md; AGENTS.md; docs mentioning deleted snapshots
```

**Structure Decision**: no new projects. Everything lands in existing projects named above, in the dependency direction Domain, Application, Infrastructure, hosts.

## Phasing (tests first inside each phase)

1. **P1 Enum generation**: red registry test and red OpenAPI enum test; implement registry and transformer; regenerate contract and client; web helpers keyed by unions; remove replaced snapshots and tests; docs and ADR 0035.
2. **P1 Gate integrity**: Docker-free evaluator tests red (finding at policy, below policy, missing, malformed, wrong image); implement evaluator and rewrite the scan test with the `ROLERADAR_REQUIRE_SCAN` flag; spike (done): pinned Trivy at HIGH,CRITICAL found 22 fixable HIGH and 0 CRITICAL, so the policy stays `CRITICAL` (owner decision, research R6), and the docker.sock mount is proven from pwsh and from Git Bash with `MSYS_NO_PATHCONV=1` and `pwd -W`; lanes, coverage test red then green; scripts and CI (CI and the gate each run every lane wherever it ran before); the gate scans its own review images and runs the scan test with the require flag; ADR 0036.
3. **P2 Flakes**: evidence loops first (SIGTERM, visual), then fixes or recorded notes; Mongo gate second-failure diagnostics.
4. **P3 Login**: red unit and e2e tests, then fix.
5. **Close**: `pwsh ./scripts/test-fast.ps1`, web typecheck, lint, test, build, e2e, `bash scripts/linux-web-check.sh`, `pre-commit run --all-files`, `pwsh ./scripts/review-gate.ps1`. Report numbers from output only. Stop at the phase boundary; no commit or push.

## Risks

- 22 fixable HIGH findings exist today (cli 12, web 10); the policy stays `CRITICAL` by owner decision, the HIGH counts are reported on every run, and tightening is a one-line policy change plus fixing the findings (board card).
- The docker.sock mount may be rewritten by MSYS path conversion on Windows; the spike proves it or switches to `docker save` plus `trivy image --input`.
- Moving container tests into an images lane could lower coverage if CI or the gate forgets to run the lane; the lane coverage test and the "every lane runs wherever it ran before" rule guard it.
- Registry may expose more enum properties than expected; the test output lists them, and R3's default is to register exposed ones.
- A flake may not reproduce; the spec allows evidence plus a confirming observation.
- `openapi-typescript` union output for nullable or array enum properties needs checking against the web code that consumes them.

## Complexity Tracking

No constitution violations to justify.
