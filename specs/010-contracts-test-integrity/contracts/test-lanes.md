# Contract: Test lanes

## Lanes

The trait is `[Trait("Lane", "<name>")]`, on the class (never on a method that differs from its class). Selection is by `--filter "Lane=..."`, never by class-name fragments.

| Lane | Marks | Selected by | Left out of the default run |
|------|-------|-------------|-------------------------------|
| `images` | classes that really use the shipped images or a launched host: everything in `Hosting` and `Containers` except the ones below, plus every class that runs or resolves an image through `ContainerTestHarness` (RunCliAsync, RunMcpAsync, RunRawAsync, EnsureImagesBuiltAsync, Resolve*Image), starts the MCP or CLI host (`McpProcessClient.StartAsync`, `LaunchedCliRunner`), or runs `docker run`, `docker build` or `docker create`. A static helper (`ValidateDatabaseNameForDrop`) or a `docker exec` into a test Mongo does not count | `scripts/test-images.ps1` (`Lane=images\|Lane=web-container`), the review gate (`Lane=images`, after the image build and scan) | no, these classes also run in the default run as they always did (many run in-process there and in a container under `ROLERADAR_TEST_IMAGE=1`) |
| `web-container` | `WebContainerSmokeTests` | `scripts/test-images.ps1`, review gate step 8 | yes |
| `real-stack` | `RealStackEndToEndTests` | `scripts/test-e2e-real.ps1`, review gate with `-IncludeE2E` | yes |
| `scan` | `ContainerScanReportTests` (two tests: the severity-policy check and the Kerberos package inventory), the only tests that need real Trivy reports | CI step after Trivy, review gate after its own Trivy step | yes |
| `default` | explicit marker for a class that names the machinery but needs no heavy lane (the two Docker-free guards) | never selected | no |

Untagged classes belong to the default run.

**Default run** = `Lane!=scan&Lane!=web-container&Lane!=real-stack`: the CI main step and review gate step 6. It leaves out only the lanes that need something the default run does not have: `scan` (the Trivy reports, which do not exist yet at that point), `web-container` and `real-stack` (each has its own step). It does **not** leave out `images`, on purpose: those classes keep running in the default run as they always did (host mode for the dual-mode classes, their own `:test` images for the Docker-only ones), and the `images` lane then runs them again in image mode against the images that were just built and scanned (gate step 7c, the CI launched-host step). Docker-only classes (everything in `Hosting`) therefore run twice per gate run. That duplicate is the accepted cost of not losing the host-mode coverage; a finer split (a lane for classes that only make sense in image mode) is possible later. A plain `dotnet test` or `test-all.ps1` runs everything; there the scan tests skip with their reason.

**Nothing runs less than before**:

- CI: main step as before (minus the three lanes, as its old name filter excluded two of them, plus the scan test, which moved after Trivy); the images lane in the existing `test-images.ps1` step (now also selecting the classes the old fragments missed, listed in research R7); a new scan-lane step directly after Trivy; the real-stack job unchanged in behaviour.
- Gate: step 6 default run, step 7 image build, scan (own Trivy), images lane, step 8 web-container, real-stack with `-IncludeE2E`.
- arm64 workflow: `test-images.ps1` default filter (images and web-container lanes); the cold-start benchmark step still selects its single class by name, and a test proves that name still matches a class.

## Ordering and images

`EnsureImagesBuiltAsync` only builds the CLI and MCP images, and only when the tag is missing (`roleradar-cli:test` by default, or `ROLERADAR_CLI_IMAGE`). In the gate the images lane therefore runs after the image build with `ROLERADAR_{CLI,MCP}_IMAGE` (and the identity and web variables) pointing at the review images, so tests and scan look at the same images. In CI the images are pre-built (`:local`) before both steps.

## Coverage test (reflection and file reads only, no Docker)

`TestLaneCoverageTests` fails when:

1. a class or method has a `Lane` value outside the table,
2. a known lane has no test class (a run selecting it would execute zero tests),
3. a lane named in a script or workflow is unknown or has no test class,
4. a test class that lives in `Hosting` or `Containers`, or really uses the images (the definition in the table), has no `Lane` trait,
4b. a class in the images lane never uses the images (it only adds time and flakes there; found for the two Backup classes),
5. a test method carries a lane different from its class,
6. a test source writes a `trivy-report` file (a test must never produce the report it checks; only the evaluator tests may, into a temporary folder),
7. a script or workflow selects by one of the old name fragments (`WebContainer`, `RealStack`, `Hosting`, `ShippedHost`, `StartupSmoke`, `ContactNoLeak`, `CliComposition`),
8. a `FullyQualifiedName~Name` filter aimed at the Integration project matches no test class.

The message names the class or script line. The images lane is 58 tests after the two Backup classes left it. Evidence: adding a scratch class that uses the harness without a lane turns it red (T032).

## Gate output rules

- The gate prints the scan result (report count, CRITICAL count, HIGH count in the reports, scan test passed and skipped) and the images lane counts (passed, failed, skipped), within 15 summary lines.
- The scan test runs with `ROLERADAR_REQUIRE_SCAN=1`: a missing or invalid report is a failure. Both the scan and the images lane fail the gate on any skipped test, a failed test or zero tests (the images lane had 0 skips in the 2026-11-06 run: 92 passed).
- The container MCP test (`ContainerMcpToolSchemaContractTests`) and the classes the old filter missed are in the `images` lane count.
