# Contract: Container scan report evaluation

## Inputs

`TestResults/trivy-report-{cli,mcp,identity,api,web}.json`, produced by a real Trivy run: the CI Trivy step, or `review-gate.ps1`, which runs the same pinned image (`aquasec/trivy:0.59.1`) with the same flags (`--severity CRITICAL --ignore-unfixed --list-all-pkgs --format json`) on the images it just built. The test never writes into `TestResults`.

## Severity policy

The failing severities are one policy value, default `CRITICAL` (owner decision at T024, research R6). The CI Trivy steps, `review-gate.ps1` and the evaluator all use it, and a test asserts they match: every `--severity <value>` found by regex in `.github/workflows/ci.yml` and `scripts/review-gate.ps1` must equal the evaluator policy value (a regex on the flag, not a whole-line comparison).

- The evaluator takes the failing severities as a parameter (default `CRITICAL`).
- Under the `CRITICAL` policy a HIGH finding does not fail the test.
- The HIGH and CRITICAL counts per image are always reported in the verdict and in the test output, whatever the policy.
- Tightening later is a one-line policy change (`HIGH,CRITICAL`), the same change to the CI and gate `--severity`, and fixing the findings tracked on the board card (22 fixable HIGH today: cli 12 from mongodump and mongorestore, web 10 from the npm shipped in node:22-alpine).

Note on report contents: a scan run at `--severity CRITICAL` writes only CRITICAL vulnerabilities into its report, so its HIGH count is 0 by construction. The count is still printed (it is what the report contains); the spike numbers in research R6 come from a separate HIGH,CRITICAL run.

## Verdict table

| Situation | CI, or `ROLERADAR_REQUIRE_SCAN=1` (the gate) | Plain local `dotnet test` |
|-----------|------------------------------------------------|---------------------------|
| report missing | Fail, names the path | Skip, reason: "no Trivy report; run the CI scan step or the gate" |
| malformed JSON or no `Results` | Fail | Fail |
| `ArtifactName` repository is not the expected image (`roleradar-<target>`) | Fail | Fail |
| a vulnerability whose severity is in the policy's failing set | Fail, lists id, package, severity | Fail |
| all reports valid, nothing in the failing set | Pass | Pass |

An empty `Results` array with a valid `ArtifactName` is clean (Trivy writes that for no findings). The `ArtifactName` check compares the image repository (`roleradar-cli`, `roleradar-mcp`, ...) and ignores the tag, because CI tags images `:local` and the gate tags them `:review-<guid>`.

## Seeded tests (Docker-free, fictional ids)

1. a HIGH report passes under `CRITICAL` and fails under `HIGH,CRITICAL` (proves tightening is one policy value)
2. a CRITICAL report fails under both policies and names `CVE-0000-0001`
3. HIGH and CRITICAL counts per image are always in the verdict
4. no report with the require flag or `CI` fails; no report without either skips with the reason; neither leaves a file behind
5. malformed JSON fails
6. wrong `ArtifactName` fails
7. clean report passes
8. workflow and gate `--severity` values equal the evaluator policy (regex on `--severity`)

## Gate behaviour

`review-gate.ps1` scans the five review images after building them, writes the five reports, then runs the scan test with `ROLERADAR_REQUIRE_SCAN=1`. The gate summary reports the number of reports and the CRITICAL count, with the label `HIGH: not scanned (policy CRITICAL)` (a CRITICAL-policy scan lists no HIGH findings, so a 0 would read as a clean result), and the gate fails if the scan test was skipped, failed or ran zero times.

## Windows note

Proven on this machine (research R6): from pwsh the plain `-v /var/run/docker.sock:/var/run/docker.sock` form works with a Windows report path. From Git Bash the CI form fails (MSYS rewrites the socket path), so any Git Bash caller must set `MSYS_NO_PATHCONV=1` and take the report directory from `pwd -W`. `review-gate.ps1` runs under pwsh and uses the plain form; the evidence is repeated inside the gate step itself (T031).
