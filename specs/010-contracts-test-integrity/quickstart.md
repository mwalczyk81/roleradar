# Quickstart: Validating feature 010

Prerequisites: Docker running (for Integration and e2e), `pnpm install` done, branch `010-contracts-test-integrity`. Run only against throwaway containers, never the developer's running stack.

## Story 1: generated enums

1. Red first: `dotnet test tests/RoleRadar.Tests.Contract -c Release --filter "FullyQualifiedName~EnumContractRegistry"` fails (registry test), then passes after implementation.
2. Regenerate: `dotnet build src/RoleRadar.Api -c Release` (writes `contracts/openapi/roleradar-api.json`), then `npx --yes openapi-typescript@7.10.1 contracts/openapi/roleradar-api.json -o contracts/client/api-types.ts`. `git diff --stat contracts` shows `enum` added and unions in `api-types.ts`.
3. `dotnet test tests/RoleRadar.Tests.Contract -c Release --filter "FullyQualifiedName~ApiOpenApiContractSnapshot|FullyQualifiedName~ODataMetadataDrift"` is green.
4. `pnpm --filter web run typecheck` exits 0.
5. `git ls-files contracts` shows only the files kept in research R4.
6. Scratch check: add an unregistered enum property in Domain, run the registry test, see it name the property, then revert.

## Story 2: gate integrity

1. `dotnet test tests/RoleRadar.Tests.Integration -c Release --filter "FullyQualifiedName~TrivyReportEvaluator"` shows the seeded-finding and missing-report tests failing first, then passing.
2. Plain `dotnet test tests/RoleRadar.Tests.Integration -c Release --filter "FullyQualifiedName~ContainerSecurityScanReport"` with no `TestResults/trivy-report-*.json` skips with the reason; with `ROLERADAR_REQUIRE_SCAN=1` it fails; `ls TestResults` shows no new `container-scan-report.json` either way.
3. Spike (research R6): run the pinned Trivy command from Git Bash and from pwsh at `HIGH,CRITICAL`, record the counts.
4. `dotnet test tests/RoleRadar.Tests.Integration -c Release --filter "FullyQualifiedName~TestLaneCoverage"` is green; add a scratch harness-using class without a lane and see it red.
5. `pwsh ./scripts/review-gate.ps1` prints per-lane counts, the scan report count and findings, and the scan test passing with the require flag; the container MCP test is in the images lane count.

## Story 3: flakes

1. SIGTERM: run the evidence loop from the tasks (20 runs under load); the recorded State and logs are in `research.md` R8.
2. Mongo gate: `dotnet test tests/RoleRadar.Tests.Integration -c Release --filter "FullyQualifiedName~MongoContainerStartGateDiagnostics"` green.
3. Visual: `pnpm --filter web exec playwright test visual-baseline --grep company-detail-errors --repeat-each=20` shows zero failures, and passes in `bash scripts/linux-web-check.sh` (the Linux image).

## Story 4: Login

1. `dotnet test tests/RoleRadar.Tests.Unit -c Release --filter "FullyQualifiedName~LoginModelRazorPage"` red then green.
2. `web/tests/e2e/identity-signin.spec.ts` asserts exactly one error on a wrong password.

## Close

`pwsh ./scripts/test-fast.ps1`, `pnpm --filter web run typecheck`, `lint`, `test`, `build`, `test:e2e`, `bash scripts/linux-web-check.sh`, `pre-commit run --all-files`, `pwsh ./scripts/review-gate.ps1`. Quote numbers from command output only.
