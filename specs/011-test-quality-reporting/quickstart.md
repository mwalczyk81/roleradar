# Quickstart: validating feature 011

Run from the repo root. Report numbers from command output only.

## Story 4: script lint in the gate
1. Create a scratch script in a temp copy of `scripts/` with a plural cmdlet noun; run `Invoke-ScriptAnalyzer -Path <temp copy> -Recurse -Settings scripts/psscriptanalyzer-settings.psd1 -EnableExit`. Expect exit 1 naming file, line and rule.
2. Remove it. Expect exit 0.
3. `pwsh ./scripts/review-gate.ps1` shows the lint step PASS.

## Story 3: property tests
1. `dotnet test tests/RoleRadar.Tests.Unit -c Release --filter "FullyQualifiedName~Property"` passes.
2. Seed a bug in the CSV reader, then in normalization, then in dedupe ordering (working tree only, reverted after). Each run fails and prints the seed. Re-run with that seed to replay.

## Story 1: mutation
1. Run the web Stryker command from `nightly-quality.yml` locally (or dispatch the workflow). Expect an HTML report and `mutation-score.json`. .NET mutation is deferred (research.md D1).
2. `node web/scripts/mutation-ratchet.mjs --base-ref origin/main` passes; lower a floor in a scratch change and expect exit 1.
3. Open the pull request. The validation run of `nightly-quality.yml` (path-filtered, same-repo only, no publish, no secrets) uploads both reports and the summary says the dashboard publish was skipped. After merge, dispatch it once more, with the secret added, to check the dashboard publish.
4. `pnpm --filter web exec vitest run tests/unit/workflow-triggers.test.ts` passes (trigger block is exactly schedule, dispatch and the path-filtered same-repo pull request).

## Story 2: reporting
1. Open a CI or gate run summary: tables for tests per lane, coverage, images, scans.
2. Delete an input file in a scratch run of `node web/scripts/metrics-record.mjs summary`: expect a non-zero exit naming the input.
3. With gh-pages absent, the main run's publish job succeeds and the summary names what to enable.
4. Failing e2e test in a scratch branch: screenshots and traces upload with 14-day retention (7 for real-stack); a green run uploads none.

## Story 2b: Allure (after the stop point)
1. Read the adapter verification report in research and approve.
2. Dispatch the nightly workflow twice; open `allure/` and confirm all supported stacks and trends.
3. Seeded failing e2e test shows screenshot and trace.
4. Privacy test: scratch real-stack attachment makes it red; removing it makes it green.

## Standing checks
`dotnet build RoleRadar.sln -c Release -v q --nologo -warnaserror`, `pwsh ./scripts/test-fast.ps1`, `pre-commit run --all-files` (zizmor), `pwsh ./scripts/verify-action-pins.ps1`, `pwsh ./scripts/review-gate.ps1` at the end.
