# ADR 0036: Test Lanes by Trait, and a Gate That Scans Its Own Images

- **Status**: Accepted
- **Date**: 2026-11-06
- **Feature**: `010-contracts-test-integrity`

## Context and Problem Statement

The heavy Integration suites were picked by hand-maintained class-name fragments: `scripts/test-images.ps1` listed `Hosting|ShippedHost|StartupSmoke|ContactNoLeak|CliComposition|WebContainer`, the gate and CI each carried their own `WebContainer` and `RealStack` names, and `test-e2e-real.ps1` named one class. Three image-sensitive classes were never selected by the image run (`PostingRejectionHostParityTests`, `ContainerHealthCheckLaunchedHostTests`, `IdentityImageLoginPageTests`), and the review gate never ran image mode at all. (Two more classes, the Backup ones, looked image-sensitive from a text match but only run host processes against a test Mongo; a check now keeps such classes out of the images lane.) Separately, `ContainerSecurityScanReport_ExistsAndPasses` wrote its own hard-coded passing report when none existed (and `TrivyScanReport_CliImage_ExistsAndInventoriesKerberosPackages` synthesized a `trivy-report-cli.json` the same way, into the very path the real scan uses), so it could not fail on a real scan, and in CI it ran before the images were built and scanned.

## Decision Outcome

- **Lanes by trait.** `[Trait("Lane", "images" | "web-container" | "real-stack" | "scan" | "default")]` on the class. Scripts and workflows select with `--filter "Lane=..."` and exclude with `Lane!=...`; none selects by a name fragment. Untagged classes are the default run. See `specs/010-contracts-test-integrity/contracts/test-lanes.md`.
- **A test keeps the lanes honest.** `TestLaneCoverageTests` (no Docker) fails on a test source that writes a `trivy-report` file, a class in the images lane that never uses the images, an unknown lane, an empty lane, a lane named in a script that has no class, a class in `Hosting` or `Containers` or using the container and launched-host machinery without a lane, a method lane that differs from its class, a script still selecting by the old fragments, and a name filter aimed at the Integration project that matches no class.
- **The scan tests read real reports and never write.** `ContainerScanReportTests` (lane `scan`, two tests: the policy check, and that the CLI report inventories the Kerberos packages) evaluates `TestResults/trivy-report-<target>.json` through `TrivyReportEvaluator`. Missing reports fail in CI and with `ROLERADAR_REQUIRE_SCAN=1`, and skip with an explicit reason otherwise. A report is valid only if it is a Trivy report for the expected image repository (tag ignored).
- **One severity policy.** The failing severities are one value (default `CRITICAL`, with `--ignore-unfixed`) shared by the evaluator, the CI Trivy steps and the gate; a test asserts every `--severity` flag in `ci.yml` and `review-gate.ps1` equals it. The HIGH and CRITICAL counts per image are always reported, and HIGH does not fail the run under the CRITICAL policy. A spike on 2026-11-06 found 0 CRITICAL and 22 fixable HIGH findings (cli 12 from the bundled mongodump and mongorestore, web 10 from the npm inside `node:22-alpine`); the owner kept CRITICAL and filed the HIGH findings as a separate card. Tightening is one policy value plus fixing those findings.
- **The gate scans what it builds.** After building the review images, `review-gate.ps1` runs the pinned `aquasec/trivy:0.59.1` with the CI flags on them, writes the reports, runs the scan lane with `ROLERADAR_REQUIRE_SCAN=1`, then runs the images lane against the same review images. The gate cannot print a pass with the scan skipped.
- **CI.** The main run excludes `scan`, `web-container` and `real-stack` by trait (the `images` lane is deliberately not excluded: those classes also run in host mode there, as before, and again in image mode in the launched-host step and the gate's step 7c) (the scan test cannot run before Trivy has written its reports); a scan-lane step runs right after Trivy; the existing launched-host step runs the images and web-container lanes.
- **docker.sock on Windows.** Proven from pwsh with the plain `/var/run/docker.sock` mount and a Windows report path. From Git Bash the CI form fails because MSYS rewrites the socket path, so a Git Bash caller must set `MSYS_NO_PATHCONV=1` and take the report directory from `pwd -W`.

### Consequences

- Good: adding an image-sensitive test class without a lane fails a test instead of silently dropping out of the image run.
- Good: the scan test can fail on a real finding and cannot pass by writing its own report; the gate cannot pass with the scan skipped.
- Good: tightening the scan later is a one-line policy change, with the counts already visible.
- Bad: three more classes run in image mode now (CI, gate, arm64), so the image runs are longer.
- Bad: the gate is longer: it adds a Trivy run per image and the images lane.
- Bad: a scan at the CRITICAL policy writes no HIGH findings into its reports, so the HIGH count shown on a normal run is what the report holds (0). The 22 HIGH findings are recorded in the spike (research R6), not re-counted on every run.
