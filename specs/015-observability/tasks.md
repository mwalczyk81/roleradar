---

description: "Task list for Feature 015: End-to-End Observability and Local Telemetry Dev Loop"
---

# Tasks: End-to-End Observability and Local Telemetry Dev Loop (`015-observability`)

**Input**: Design documents from `/specs/015-observability/` (`plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/telemetry-signals.md`, `contracts/privacy-gate.md`, `contracts/local-dev-loop.md`, `quickstart.md`)

**Prerequisites**: `plan.md` (required), `spec.md` (required for user stories), `research.md`, `data-model.md`, `contracts/`

**Tests**: Tests are MANDATORY per Constitution Principle II (Test-First Development). In every code gate, failing test tasks MUST be written first, shown failing via an explicit **RED-RUN** task, and precede all implementation tasks. In Gate 2, the Telemetry Privacy Gate (`RR-72`) tests come before any exporter or span processor implementation.

**Test Project & Lane Placement Rules (`AGENTS.md` & `ADR 0036`)**:
- `tests/RoleRadar.Tests.Architecture`: Fast reflection and file-parsing tests only (no Docker).
- `tests/RoleRadar.Tests.Unit`: Pure in-process unit tests with `OpenTelemetry.Exporter.InMemory` (no Docker).
- `tests/RoleRadar.Tests.Contract`: In-process `Api` (non-Mongo) and `Identity` (SQLite) contract tests with `OpenTelemetry.Exporter.InMemory` (**no Docker / no Testcontainers**).
- `tests/RoleRadar.Tests.Integration`: All Docker, Testcontainers, launched-host, and real-stack tests, carrying explicit `[Trait("Lane", "...")]` attributes enforced by `TestLaneCoverageTests.cs`:
  - `[Trait("Lane", "default")]`: Host-process integration tests using `SharedMongoFixture` (Testcontainers MongoDB replica set `rs0`), such as `MongoTelemetryIntegrationTests.cs`.
  - `[Trait("Lane", "images")]`: Launched-host integration tests using `LaunchedCliRunner` or `McpProcessClient.StartAsync`, such as `CliMcpTelemetryIntegrationTests.cs`.
  - `[Trait("Lane", "real-stack")]`: Full containerized stack E2E tests in `RealStackEndToEndTests.cs` executed via `pwsh ./scripts/test-e2e-real.ps1` (never touching the developer's stack).

**Gates Are PR Boundaries**: Work is organized into **five sequential gates** (`Gate 1` through `Gate 5`, corresponding to User Stories 1 through 5 in `spec.md`). Each gate is its own pull request and MUST stop at its gate boundary for review after passing the standard gate-end verification and hygiene tasks from Git Bash (`RR-85`).

**Out of Scope / Not in Tasks**:
- Do NOT edit `.github/dependabot.yml` in any Feature 015 gate PR. The PR grouping `@opentelemetry/*` (npm) and `OpenTelemetry*` (NuGet) in `.github/dependabot.yml` is a separate small follow-up PR outside the feature gates after Gate 2 merges (expect one extra Dependabot run afterwards).
- Never run anything against the developer's running Docker Compose stack, live MongoDB (`localhost:27017` / `roleradar-mongo` / `roleradar-mongo-data`), or `roleradar-identity-data` volume. All spikes and tests use strictly fictional data and isolated containers/throwaway volumes.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story / gate this task belongs to (`[US1]` = Gate 1, `[US2]` = Gate 2, `[US3]` = Gate 3, `[US4]` = Gate 4, `[US5]` = Gate 5)
- Every task includes exact file paths and quotes `data-model.md` constraints verbatim where applicable.

---

## Phase 1: Setup (Pre-Flight Workspace & Branch Hygiene)

**Purpose**: Verify clean branch state, existing test project count, and unchanged `.github/dependabot.yml` before starting Gate 1 (`US1`, docs-only PR) so zero non-doc files are dirtied prior to Gate 1 review.

- [X] T001 Verify branch `015-observability` is active, `.specify/feature.json` points to `specs/015-observability`, and `.github/dependabot.yml` has zero modifications
- [X] T002 [P] Verify `tests/` contains only the four sanctioned test projects (`tests/RoleRadar.Tests.Architecture/RoleRadar.Tests.Architecture.csproj`, `tests/RoleRadar.Tests.Unit/RoleRadar.Tests.Unit.csproj`, `tests/RoleRadar.Tests.Contract/RoleRadar.Tests.Contract.csproj`, `tests/RoleRadar.Tests.Integration/RoleRadar.Tests.Integration.csproj`) per `AGENTS.md`
- [X] T003 [P] Verify baseline locked restore and offline build succeed from Git Bash (`dotnet restore RoleRadar.sln --locked-mode` and `ROLERADAR_MONGO_CONNECTION_STRING="mongodb://127.0.0.1:1/?serverSelectionTimeoutMS=100" dotnet build RoleRadar.sln -c Release -v q --nologo -warnaserror`) against `RoleRadar.sln`

---

## Phase 2: Foundational (Scratch Spike Safety Guardrails)

**Purpose**: Establish the isolated scratch workspace and fail-closed safety guard outside the repository before any Gate 1 spike container or script executes.

**⚠️ CRITICAL**: No Gate 1 spike work (`US1`) can begin until this safety guard is verified.

- [X] T004 Create an isolated scratch directory outside the repository (`$TEMP/roleradar-spike-015/`) with a safety guard script `$TEMP/roleradar-spike-015/assert-spike-safety.ps1` that refuses to run (exits non-zero) unless the target MongoDB connection string explicitly names the scratch container (`roleradar-spike-mongo`) on a non-`27017` port (`27099`), explicitly rejects port `27017`, host/container `roleradar-mongo`, volume `roleradar-mongo-data`, and volume `roleradar-identity-data`, and verifies all seed payloads in `$TEMP/roleradar-spike-015/` are strictly fictional
- [X] T005 Verify `$TEMP/roleradar-spike-015/assert-spike-safety.ps1` fails closed when passed `mongodb://localhost:27017`, `mongodb://roleradar-mongo:27017`, `roleradar-mongo-data`, or `roleradar-identity-data`, and passes only when passed the isolated scratch parameters (`mongodb://127.0.0.1:27099/?replicaSet=rs0`, volume `roleradar-spike-mongo-data`, and volume `roleradar-spike-identity-data`)

**Checkpoint**: Scratch safety guard verified — Gate 1 (`US1`, docs-only PR) can now begin.

---

## Phase 3: User Story 1 / Gate 1 - Local Observability Spike and Architectural Decision (Priority: P1) 🎯 MVP (Docs-Only PR)

**Goal**: Execute the `RR-67` local observability spike in `$TEMP/roleradar-spike-015/` using strictly fictional data and throwaway containers, and record `ADR 0049` (`docs/adr/0049-local-observability-apphost-and-dashboard.md`) as a **docs-only pull request** comparing `.NET Aspire` (`AppHost` + `Dashboard`) against `Seq`, `Jaeger`, and `Grafana LGTM`, either confirming Option 2 (Aspire AppHost + Dashboard) or reporting a concrete blocker and selecting the fallback (standalone Aspire Dashboard in an opt-in Docker Compose overlay).

**Independent Test**: Verify `docs/adr/0049-local-observability-apphost-and-dashboard.md` and `docs/adr/README.md` are the only repository files changed beyond `specs/015-observability/`, all 6 spike checks in `specs/015-observability/contracts/local-dev-loop.md` are documented with evidence, all throwaway scratch containers/volumes are removed, and the Gate 1 gate-end checks pass.

### Spike Execution & Documentation for User Story 1 (Gate 1 — Docs Only)

- [X] T006 [US1] Invoke `$TEMP/roleradar-spike-015/assert-spike-safety.ps1` and test Check 1 (MongoDB `9.0.4` replica set `--replSet rs0` with `GLIBC_TUNABLES=glibc.pthread.rseq=1` and multi-document transaction execution using fictional data on throwaway volume `roleradar-spike-mongo-data`) in `$TEMP/roleradar-spike-015/`, recording findings for `docs/adr/0049-local-observability-apphost-and-dashboard.md`
- [X] T007 [US1] Invoke `$TEMP/roleradar-spike-015/assert-spike-safety.ps1` and test Check 2 (Identity `ROLERADAR_IDENTITY_ISSUER_URI`, API `ROLERADAR_OIDC_AUTHORITY`, Web `ROLERADAR_IDENTITY_ISSUER_URI`, and development TLS certificate SAN parity with `deploy/docker-compose.apps.yml` on throwaway volume `roleradar-spike-identity-data`) in `$TEMP/roleradar-spike-015/`, recording findings for `docs/adr/0049-local-observability-apphost-and-dashboard.md`
- [X] T008 [US1] Test Check 3 (Compose vs. AppHost drift check mechanics for ports `5001`/`5002`/`5003`, env vars, and pinned image digests) and Check 5 (confirming `mcr.microsoft.com/dotnet/aspire-dashboard:13.6.4@sha256:23411f715be2476da0b2c0ad110039237de657c6cae31bf5c587d12a26c11230` bound to `127.0.0.1` provides built-in traces, structured logs, metrics, and resource graph views and no saved custom dashboards) in `$TEMP/roleradar-spike-015/`, recording findings for `docs/adr/0049-local-observability-apphost-and-dashboard.md`
- [X] T009 [US1] Tear down and delete all throwaway containers, volumes (`roleradar-spike-*`), and scratch files in `$TEMP/roleradar-spike-015/`, and verify `docker ps -a` and `docker volume ls` show zero leftover `roleradar-spike-*` resources and untouched developer volumes
- [X] T010 [US1] Author `docs/adr/0049-local-observability-apphost-and-dashboard.md` comparing Aspire AppHost + Dashboard (`13.6.4`) against Seq, Jaeger, and Grafana LGTM, documenting Checks 1–6 (replica-set Mongo transactions, Identity issuer/TLS parity, drift check design in `RoleRadar.Tests.Architecture`, `CLI`/`Mcp.Stdio` default-off telemetry policy and bounded-flush rationale since `traceId` is already in 014 logs/audit entries, built-in dashboard views with no saved custom dashboards, Option 3 `azd`/ACA deferral until hosting is un-parked, and confirming Option 2 or selecting the standalone Aspire Dashboard compose overlay fallback)
- [X] T011 [US1] Update the ADR index table in `docs/adr/README.md` with the link and status for `0049-local-observability-apphost-and-dashboard.md`

### Gate 1 End Verification & Hygiene (`US1` PR Boundary)

- [X] T012 [US1] Run the Gate 1 standard verification suite from Git Bash (`RR-85`): `dotnet restore RoleRadar.sln --locked-mode`, `ROLERADAR_MONGO_CONNECTION_STRING="mongodb://127.0.0.1:1/?serverSelectionTimeoutMS=100" dotnet build RoleRadar.sln -c Release -v q --nologo -warnaserror`, `pwsh ./scripts/test-fast.ps1`, `pre-commit run --all-files`, and `pwsh ./scripts/review-gate.ps1`
- [X] T013 [US1] Verify Gate 1 PR hygiene: confirm `git diff .github/dependabot.yml` is empty, no new test project was added under `tests/`, only documentation files (`docs/adr/0049-local-observability-apphost-and-dashboard.md`, `docs/adr/README.md`, and `specs/015-observability/*`) are modified/added, and all changed files have LF (`\n`) line endings — **STOP AT GATE 1 PR BOUNDARY FOR REVIEW**

---

## Phase 4: User Story 2 / Gate 2 - End-to-End Distributed Tracing and Telemetry Privacy Gate (Priority: P2)

**Goal**: Deliver end-to-end OpenTelemetry distributed tracing (`RR-68`) across `roleradar-web`, `roleradar-api`, `roleradar-identity`, and `MongoDB.Driver` (`3.13.0` native `MongoTelemetry`), default-off / opt-in bounded-flush tracing for `roleradar-cli` and `roleradar-mcp`, `ADR 0050` (`docs/adr/0050-span-attribute-allowlist-processor.md`), the Telemetry Privacy Gate (`RR-72`, `SC-002`, `SC-003`) using a `LogScrubber`-independent allowlist/drop-list span processor so **Gate 2 stands completely independent of `RR-86`'s outcome in Gate 3**, and the real-stack `SC-001` proof in `Lane=real-stack` (`scripts/test-e2e-real.ps1`, never the dev stack) using the official CNCF OpenTelemetry Collector (`otel/opentelemetry-collector-contrib:0.164.0@sha256:340c9ca6a36617084c6661bdc4f0fe92b7cda5a805d61673ec8db375fddc522b`).

> **Gate 2 Split Note (`plan.md`)**: Gate 2 contains **27 tasks** (`T014`–`T040`), which is well below the ~45-task split threshold, so it runs as a single Gate 2 PR. The tasks are ordered with `.NET` (`T014`–`T028`) first and `web/` + real-stack E2E (`T029`–`T040`) second so that if a split into Gate 2a (`.NET`) and Gate 2b (`web`) is ever requested, the boundary falls cleanly between `T028` and `T029`.

**Independent Test**: Execute multi-service traced requests (`web` -> `api` -> `MongoDB` and `web` -> `identity`) in unit/contract/integration suites and in the isolated `real-stack` E2E lane (`pwsh ./scripts/test-e2e-real.ps1`) and verify: (1) all spans across `roleradar-web`, `roleradar-api` (plus `mongodb`), and `roleradar-identity` share the exact 32-character lowercase hex `traceId` from logs, RFC 9457 problem details, and `request_correlation_id` audit records (`SC-001`); (2) 0 sentinel secrets or restricted attributes (`url.query`, query/fragment in `url.full`, `http.request.header.*`, `http.response.header.*`, `db.query.text`, `exception.message`, `exception.stacktrace`, free-text `StatusDescription`) appear in exported spans (`SC-002`); and (3) with `OTEL_EXPORTER_OTLP_ENDPOINT` unset, 0 OTLP exporters are registered and 0 non-loopback connections are opened (`SC-003`).

### Part 2A — .NET Packages, `ADR 0050`, and Failing .NET Unit, Contract & Integration Tests (MANDATORY Test-First — Privacy Gate Before Any Exporter Code) ⚠️

- [X] T014 [US2] Pin `OpenTelemetry` (`1.20.1`), `OpenTelemetry.Extensions.Hosting` (`1.20.1`), `OpenTelemetry.Exporter.OpenTelemetryProtocol` (`1.20.1`), and `OpenTelemetry.Exporter.InMemory` (`1.20.1`) in `Directory.Packages.props`, reference `OpenTelemetry`, `OpenTelemetry.Extensions.Hosting`, and `OpenTelemetry.Exporter.OpenTelemetryProtocol` in `src/RoleRadar.Observability/RoleRadar.Observability.csproj`, reference `OpenTelemetry.Exporter.InMemory` in `tests/RoleRadar.Tests.Unit/RoleRadar.Tests.Unit.csproj`, `tests/RoleRadar.Tests.Contract/RoleRadar.Tests.Contract.csproj`, and `tests/RoleRadar.Tests.Integration/RoleRadar.Tests.Integration.csproj`, and update `packages.lock.json` across `src/` and `tests/`
- [X] T015 [P] [US2] Author `docs/adr/0050-span-attribute-allowlist-processor.md` and update `docs/adr/README.md` recording the `LogScrubber`-independent allowlist/drop-list span processor design (`RoleRadarSpanAttributeProcessor` and `WebSpanAttributeProcessor`), the removal of `url.query`, query/fragment parts of `url.full`, `http.request.header.*`, `http.response.header.*`, and `db.query.text`, the sanitization of span `exception` events (retaining only `exception.type` and `error.type`, dropping `exception.message` and `exception.stacktrace`, and clearing free-text `StatusDescription`), and stating that Gate 2 stands independent of `RR-86` (`ADR 0051`)
- [X] T016 [US2] Write failing `.NET` Telemetry Privacy Gate unit tests (`RR-72`, `SC-002`) FIRST—before any exporter or processor code—in `tests/RoleRadar.Tests.Unit/Observability/TelemetryPrivacyGateTests.cs` (no Docker, expected to fail before implementation), asserting verbatim `data-model.md` constraints:
  - `HTTP Server / Client Attributes`: *"Allowlisted attributes only (`http.request.method`, `http.request.method_original`, `url.scheme`, `url.path`, `url.full` [with `?query` and `#fragment` stripped], `http.route`, `http.response.status_code`, `server.address`, `server.port`, `network.protocol.version`, `network.peer.address`, `error.type`). `RoleRadarSpanAttributeProcessor` / `WebSpanAttributeProcessor` (`ADR 0050`, independent of `LogScrubber`) explicitly removes `url.query`, strips query/fragment parts of `url.full`, removes all `http.request.header.*` and `http.response.header.*` tags, and drops any attribute not on the allowlist."*
  - `Database Attributes`: *"Allowlisted attributes (`db.system.name` = `"mongodb"`, `db.operation.name`, `db.operation.summary`, `db.command.name`, `db.namespace`, `db.collection.name`, `db.query.summary`, `server.address`, `server.port`, `network.transport`, `db.response.status_code`, `db.mongodb.lsid`, `db.mongodb.txn_number`, `db.mongodb.server_connection_id`, `db.mongodb.driver_connection_id`, `db.mongodb.cursor_id`). `TracingOptions.QueryTextMaxLength` is pinned to `0` and the span processor explicitly drops `db.query.text` and `db.statement`."*
  - `Exception Events & Status Description`: *"Sanitized without `LogScrubber` (`ADR 0050`): on any span `exception` event, only `exception.type` is retained (and copied to `error.type` if unset), while `exception.message` and `exception.stacktrace` are dropped; any free-text span `StatusDescription` is cleared (`null`) so raw exception strings never leave on spans."*
  - `TelemetryResource`: *"Resource attributes MUST NEVER include machine usernames, environment variable dumps, connection strings, or `owner_id`."*
- [X] T017 [P] [US2] Write failing `.NET` telemetry configuration and `SC-003` unit tests in `tests/RoleRadar.Tests.Unit/Observability/TelemetrySetupTests.cs` (no Docker, expected to fail before implementation), asserting verbatim `data-model.md` constraints:
  - `OTEL_EXPORTER_OTLP_ENDPOINT`: *"When unset or whitespace, zero OTLP exporters are registered (`SC-003`) and no outbound telemetry connection is opened. When set (e.g., by the Aspire AppHost or compose overlay to a `127.0.0.1` loopback endpoint), registers OTLP trace, log, and metric exporters."*
  - `OTEL_SERVICE_NAME`: *"Canonical service identifier: `roleradar-web`, `roleradar-api`, `roleradar-identity`, `roleradar-cli`, or `roleradar-mcp`."*
  - `ROLERADAR_TELEMETRY_FLUSH_TIMEOUT_MS`: *"Default `2000` (clamped `100..5000`) — Bounded flush timeout used by short-lived `CLI` and `Mcp.Stdio` processes when `OTEL_EXPORTER_OTLP_ENDPOINT` is explicitly configured (`FR-017`), ensuring the process never hangs on exit if the collector is unreachable."*
  - `Sampling Policy`: *"Samples 100% of traces locally (`FR-003`); production sampling policy is deferred until hosting is un-parked."*
- [X] T018 [P] [US2] Write failing in-process `.NET` distributed tracing contract tests (no Docker / no Testcontainers) in `tests/RoleRadar.Tests.Contract/Observability/DistributedTracingContractTests.cs` (expected to fail before implementation), verifying that `RoleRadar.Api` (on non-Mongo HTTP validation/problem-details/auth endpoints) and `RoleRadar.Identity` (with SQLite) extract inbound W3C `traceparent` and emit spans whose 32-character lowercase hex `TraceId` matches RFC 9457 `traceId` and console logs (`SC-001`), while `RoleRadarSpanAttributeProcessor` strips query strings, headers, and raw exception strings on real ASP.NET Core endpoints (`SC-002`)
- [X] T019 [P] [US2] Write failing Testcontainers MongoDB driver span integration tests with `[Trait("Lane", "default")]` in `tests/RoleRadar.Tests.Integration/Observability/MongoTelemetryIntegrationTests.cs` (using `SharedMongoFixture`, expected to fail before implementation), verifying that `RoleRadar.Api` and `MongoDB.Driver` 3.13.0 (`ActivitySource` `"MongoDB.Driver"` with `TracingOptions.QueryTextMaxLength == 0`) emit child `mongodb` spans (`find`, `insert`, `update`, `commitTransaction`) under the parent API request span sharing the exact 32-character `TraceId` written to `request_correlation_id` in Mongo `audit_log` (`SC-001`), and that `db.query.text` / `db.statement` and sentinel note `content`, `ruleText`, and `$filter` literals are completely absent from exported spans (`SC-002`)
- [X] T020 [P] [US2] Write failing launched-host `CLI` and `Mcp.Stdio` telemetry integration tests with `[Trait("Lane", "images")]` (required by `TestLaneCoverageTests.cs` because it uses `LaunchedCliRunner` and `McpProcessClient.StartAsync`) in `tests/RoleRadar.Tests.Integration/Observability/CliMcpTelemetryIntegrationTests.cs` (expected to fail before implementation), verifying default-off telemetry (`SC-003`), bounded shutdown flush (`ROLERADAR_TELEMETRY_FLUSH_TIMEOUT_MS`) when `OTEL_EXPORTER_OTLP_ENDPOINT` points to an unreachable port (`FR-017`), and strictly 0 non-MCP bytes on `Mcp.Stdio` `stdout` (`Principle VI`)
- [X] T021 [US2] **RED-RUN (.NET)**: Execute `dotnet test tests/RoleRadar.Tests.Unit -c Release --filter "FullyQualifiedName~Telemetry"`, `dotnet test tests/RoleRadar.Tests.Contract -c Release --filter "FullyQualifiedName~DistributedTracingContractTests"`, and `dotnet test tests/RoleRadar.Tests.Integration -c Release --filter "FullyQualifiedName~MongoTelemetryIntegrationTests|FullyQualifiedName~CliMcpTelemetryIntegrationTests"` from Git Bash and confirm the new tests **FAIL (RED)** before writing any `.NET` span processor or exporter implementation

### Part 2B — .NET Tracing & Span Privacy Implementation

- [X] T022 [US2] Implement `RoleRadarSpanAttributeProcessor` (`BaseProcessor<Activity>`, zero dependency on `LogScrubber`) in `src/RoleRadar.Observability/RoleRadarSpanAttributeProcessor.cs` (depends on T016–T021 failing first), enforcing the `ADR 0050` allowlist and drop-list (`url.query`, query/fragment stripping on `url.full`/`http.url`/`http.target`, `http.request.header.*`, `http.response.header.*`, `db.query.text`, `db.statement`), clearing free-text `Activity.StatusDescription`, and sanitizing span `exception` events to retain only `exception.type` (and set `error.type` on the activity if unset) while dropping `exception.message` and `exception.stacktrace`
- [X] T023 [US2] Implement `TelemetrySetup` (`AddRoleRadarTelemetry` and `FlushRoleRadarTelemetryWithBoundedTimeout`) in `src/RoleRadar.Observability/TelemetrySetup.cs` (depends on T022), subscribing by name to `"Microsoft.AspNetCore"`, `"System.Net.Http"`, `"MongoDB.Driver"`, and `"RoleRadar"`, registering `AlwaysOnSampler`, attaching `RoleRadarSpanAttributeProcessor`, and registering `AddOtlpExporter()` ONLY when `OTEL_EXPORTER_OTLP_ENDPOINT` is non-whitespace (`SC-003`), while keeping `ObservabilityArchitectureTests.Observability_ReferencesNoAspNetMongoOrDuende` green
- [X] T024 [P] [US2] Configure explicit `TracingOptions = new TracingOptions { Disabled = false, QueryTextMaxLength = 0 }` in `src/RoleRadar.Infrastructure/Mongo/ConnectionStringRedactor.cs` (`CreateClient`) so `MongoDB.Driver` 3.13.0 emits command spans without `db.query.text`
- [X] T025 [P] [US2] Wire `builder.Services.AddRoleRadarTelemetry("roleradar-api", builder.Configuration)` in `src/RoleRadar.Api/Program.cs`
- [X] T026 [P] [US2] Wire `builder.Services.AddRoleRadarTelemetry("roleradar-identity", builder.Configuration)` in `src/RoleRadar.Identity/Program.cs`
- [X] T027 [P] [US2] Wire opt-in telemetry registration and bounded shutdown flush (`ROLERADAR_TELEMETRY_FLUSH_TIMEOUT_MS`, default `2000` ms clamped `100..5000`) when `OTEL_EXPORTER_OTLP_ENDPOINT` is explicitly configured in `src/RoleRadar.Cli/Program.cs` and `src/RoleRadar.Mcp.Stdio/Program.cs` (preserving 0 bytes on `Mcp.Stdio` `stdout`)
- [X] T028 [US2] Re-run `dotnet test tests/RoleRadar.Tests.Unit -c Release --filter "FullyQualifiedName~Telemetry"`, `dotnet test tests/RoleRadar.Tests.Contract -c Release --filter "FullyQualifiedName~DistributedTracingContractTests"`, `dotnet test tests/RoleRadar.Tests.Integration -c Release --filter "FullyQualifiedName~MongoTelemetryIntegrationTests|FullyQualifiedName~CliMcpTelemetryIntegrationTests|FullyQualifiedName~TestLaneCoverageTests"`, and `dotnet test tests/RoleRadar.Tests.Architecture -c Release` and verify all `.NET` Gate 2 tests and lane coverage rules pass (**GREEN**)

### Part 2C — Web Packages, Failing Web Tests & Failing Real-Stack OTLP Test (MANDATORY Test-First — Privacy Gate Before Any Web Exporter Code) ⚠️

- [X] T029 [US2] Pin `@opentelemetry/context-async-hooks` (`3.0.0`), `@opentelemetry/resources` (`3.0.0`), `@opentelemetry/sdk-trace` (`3.0.0`), `@opentelemetry/exporter-trace-otlp-http` (`0.300.0`), `@opentelemetry/instrumentation-http` (`0.300.0`), `@opentelemetry/instrumentation-undici` (`0.34.0`), and `@opentelemetry/semantic-conventions` (`1.44.0`) in `web/package.json` and update `pnpm-lock.yaml` (Note: `instrumentation-http` and `instrumentation-undici` were pinned in Gate 2, never imported, and removed in Gate 4 (ADR 0052))
- [X] T030 [US2] Write failing web Telemetry Privacy Gate unit tests (`RR-72`, `SC-002`) FIRST—before any web exporter or processor implementation—in `web/tests/unit/telemetry-privacy.test.ts` (expected to fail before implementation), asserting that `WebSpanAttributeProcessor` (independent of `LogScrubber`, `ADR 0050`) strips `url.query`, strips `?query` and `#fragment` from `url.full`, removes `http.request.header.*` and `http.response.header.*`, drops non-allowlisted attributes (including sentinel secrets across Rules 1–9), clears free-text span status `message`, and strips `exception.message` and `exception.stacktrace` from span `exception` events while keeping `exception.type` and `error.type`
- [X] T031 [P] [US2] Write failing web distributed tracing, unified propagator (`R-003`), and `SC-003` no-endpoint unit tests in `web/tests/unit/telemetry-tracing.test.ts` (expected to fail before implementation), asserting that: (1) when `OTEL_EXPORTER_OTLP_ENDPOINT` is unset, 0 OTLP trace exporters are registered; (2) `web/app/lib/trace-context.server.ts` uses the OpenTelemetry SDK active span and `W3CTraceContextPropagator` (one mechanism, not two) while preserving strict 55-character lowercase `traceparent` edge validation and dropping `tracestate`; and (3) outbound `fetch` calls to `api` and `identity` propagate the active span's `traceparent`
- [X] T032 [P] [US2] Extend the isolated `real-stack` E2E test (`[Trait("Lane", "real-stack")]`) in `tests/RoleRadar.Tests.Integration/Containers/RealStackEndToEndTests.cs` (executed via `pwsh ./scripts/test-e2e-real.ps1`, never the dev stack; expected to fail before web/container OTLP wiring):
  - Starts the pinned official CNCF OpenTelemetry Collector container (`otel/opentelemetry-collector-contrib:0.164.0@sha256:340c9ca6a36617084c6661bdc4f0fe92b7cda5a805d61673ec8db375fddc522b`, Apache-2.0, `>= 14` days old; chosen per Principle X Standards First for its built-in `otlp` receiver and `file` exporter without adding a custom receiver or a new heavy CI lane) on the throwaway Docker network `rrtest-real-net-<runId>` (`--network-alias otel-collector`) writing `/telemetry/otlp.jsonl` to a throwaway temp mount
  - Configures `OTEL_EXPORTER_OTLP_ENDPOINT` on the throwaway `identity`, `api`, and `web` containers
  - Asserts (`SC-001`) that the exported OTLP spans in `otlp.jsonl` contain one shared 32-character W3C `traceId` (`signInId`) across `roleradar-web` and `roleradar-identity` spans, and one shared 32-character `traceId` (`followedId` and `writeTraceId` from the `posting_transition` audit entry) across `roleradar-web` and `roleradar-api` (plus child `mongodb`) spans, with zero sentinel passwords, TOTP codes, or tokens present in `otlp.jsonl` (`SC-002`)
- [X] T033 [US2] **RED-RUN (Web)**: Execute `pnpm --filter web run test -- tests/unit/telemetry-privacy.test.ts tests/unit/telemetry-tracing.test.ts` from Git Bash and confirm the new web tests **FAIL (RED)** before implementing `web/app/lib/telemetry.server.ts`

### Part 2D — Web Tracing, Unified Propagation & Real-Stack Verification

- [X] T034 [US2] Implement `WebSpanAttributeProcessor` and `initializeWebTelemetry` in `web/app/lib/telemetry.server.ts` (depends on T030–T033 failing first), registering `AsyncLocalStorageContextManager`, `W3CTraceContextPropagator`, `AlwaysOnSampler`, `WebSpanAttributeProcessor` (`ADR 0050`), and registering `OTLPTraceExporter` ONLY when `OTEL_EXPORTER_OTLP_ENDPOINT` is set and non-whitespace (`SC-003`)
- [X] T035 [US2] Refactor `web/app/lib/trace-context.server.ts` to replace the manual `crypto.randomBytes` trace/span ID generator and manual `trace.setSpanContext(ROOT_CONTEXT, ...)` bridge with the OpenTelemetry SDK tracer and global `W3CTraceContextPropagator` (`propagation.extract` / `propagation.inject` from `@opentelemetry/api`), keeping only the strict 55-character lowercase `traceparent` edge guard (`R-003`, one mechanism)
- [X] T036 [US2] Update `web/server.ts`, `web/app/lib/api-client.server.ts`, and `web/app/lib/oidc.server.ts` to initialize `web/app/lib/telemetry.server.ts` and record server/client HTTP spans so `roleradar-web` -> `roleradar-api` and `roleradar-web` -> `roleradar-identity` share one connected trace tree (`SC-001`)
- [X] T037 [US2] Re-run `pnpm --filter web run typecheck`, `pnpm --filter web run lint`, and `pnpm --filter web run test` from Git Bash and verify all existing and new Gate 2 web unit tests pass (**GREEN**)
- [X] T038 [US2] Execute `pwsh ./scripts/test-e2e-real.ps1` from Git Bash (building the isolated `roleradar-identity:local`, `roleradar-api:local`, and `roleradar-web:local` images and running `RealStackEndToEndTests` in `Lane=real-stack`, never touching the dev stack) and verify the end-to-end `SC-001` OpenTelemetry Collector span assertion across `roleradar-web`, `roleradar-api` (`mongodb`), and `roleradar-identity` passes (**GREEN**)

### Gate 2 End Verification & Hygiene (`US2` PR Boundary)

- [X] T039 [US2] Run the Gate 2 standard verification suite from Git Bash (`RR-85`): `dotnet restore RoleRadar.sln --locked-mode`, `ROLERADAR_MONGO_CONNECTION_STRING="mongodb://127.0.0.1:1/?serverSelectionTimeoutMS=100" dotnet build RoleRadar.sln -c Release -v q --nologo -warnaserror`, `pwsh ./scripts/test-fast.ps1`, `bash scripts/linux-web-check.sh`, `pre-commit run --all-files`, and `pwsh ./scripts/review-gate.ps1`
- [X] T040 [US2] Verify Gate 2 PR hygiene: confirm `git diff .github/dependabot.yml` is empty (remember: the Dependabot grouping PR is a separate follow-up outside the feature PRs after Gate 2 merges), confirm no new test project was added under `tests/`, and confirm all modified/created files have LF (`\n`) line endings — **STOP AT GATE 2 PR BOUNDARY FOR REVIEW**

### Gate 2b Follow-Up — Maintained ASP.NET Core Instrumentation (Principle X, RR-90)

- [X] T040a [US2] Pin `OpenTelemetry.Instrumentation.AspNetCore` `1.20.1` in `Directory.Packages.props`, reference it in `RoleRadar.Api` and `RoleRadar.Identity` only (not in `RoleRadar.Observability`), and update package lock files (`dotnet restore RoleRadar.sln --force-evaluate`)
- [X] T040b [US2] **RED-RUN**: Delete span-tagging code from both `RequestCompletedMiddleware` copies (keeping `RequestCompleted` log records intact) and run `DistributedTracingContractTests` to demonstrate failing tests
- [X] T040c [US2] Wire `AddAspNetCoreInstrumentation(options => options.RecordException = false)` via the `configureTracing` callback in `Api` and `Identity`, remove redundant `AddSource("Microsoft.AspNetCore")` from `TelemetrySetup.cs`, and verify tests pass (**GREEN**)
- [X] T040d [US2] Rewrite native-span contract test in `DistributedTracingContractTests.cs` (documenting bare native span deficiencies, asserting `Api` and `Identity` span HTTP semantic tags/status, and adding SC-002 query/user-agent/header privacy assertions)
- [X] T040e [US2] Update documentation: author addendum in `docs/adr/0050-span-attribute-allowlist-processor.md`, add row in `specs/015-observability/plan.md` Standards Considered table, and record execution notes in `tasks.md`

> **Gate 2 Execution Notes (`T014`–`T040e`, 2026-11-11)**:
> - **ADR 0050 & Addendum (`T015`, `T040e`)**: Authored `docs/adr/0050-span-attribute-allowlist-processor.md` and Gate 2b Addendum establishing the closed operational attribute allowlist, explicit drop-list, and replacement of hand-set span tags with official `OpenTelemetry.Instrumentation.AspNetCore` `1.20.1` (`RR-90`).
> - **Red-First Verification (`T021`, `T033`, `T040b`)**: Confirmed failing tests prior to implementation at each stage, verifying bare native ASP.NET Core spans lack HTTP semantic tags and that removing manual middleware tags broke tests until the instrumentation library was wired.
> - **Real-Stack Proof & `RR-89` Timings (`T032`, `T038`)**: Ran `pwsh ./scripts/test-e2e-real.ps1` against the isolated throwaway stack with `otel/opentelemetry-collector-contrib:0.164.0@sha256:340c9ca6a36617084c6661bdc4f0fe92b7cda5a805d61673ec8db375fddc522b`. Cold run 1 (`2m46.771s` wall-clock, `44 s` xUnit duration) passed Playwright and audit assertions on the first attempt and surfaced a Windows file-sharing lock on `otlp.jsonl` (`File.ReadAllTextAsync` vs. `FileShare.ReadWrite | FileShare.Delete` while the collector container had the file open for writing). After opening `otlp.jsonl` with `FileShare.ReadWrite | FileShare.Delete`, run 2 (`0m54.495s` wall-clock, `28 s` xUnit duration) passed `1/1` (`SC-001` & `SC-002`) with zero retries.
> - **Developer Stack & Certificate Stores**: Untouched (`roleradar-mongo`, `deploy-api-1`, `deploy-identity-1`, `deploy-web-1` untouched; no `dotnet dev-certs` or certificate store commands run).

---

## Phase 5: User Story 3 / Gate 3 - Structured Logs Over Telemetry Pipeline and Scrubbing Evaluation (Priority: P3)

**Goal**: Execute the evidence-first `RR-86` runtime log scrubbing census and `ADR 0051` decision (`docs/adr/0051-otel-logs-and-runtime-scrubbing.md`), and export structured application and security logs over OTLP (`RR-70`) correlated with active `TraceId` and `SpanId` (`SC-004`) and protected by the `RR-72` log privacy gate (`SC-002`).

**Independent Test**: Verify `docs/adr/0051-otel-logs-and-runtime-scrubbing.md` documents producer evidence for all 7 `LogScrubber` rules, run structured log events and exceptions containing all 7 sentinel categories through the OTLP log pipeline into in-memory test receivers across `.NET` and `web/`, and confirm `TraceId`/`SpanId` correlation and 0 unredacted sentinel values in exported log bodies, attributes, or exception details (`SC-002`, `SC-004`).

### Part 3A — `RR-86` Producer Census & `ADR 0051` Architectural Decision (MUST Run Before Any Gate 3 Code)

- [X] T041 [US3] Execute the `RR-86` producer census across `src/`, `web/`, and pinned libraries (`Microsoft.AspNetCore.*`, `System.Net.Http`, `MongoDB.Driver`, `Duende.IdentityServer`, `Microsoft.EntityFrameworkCore`) before writing any Gate 3 production code: identify and record a concrete real producer (or prove none exists) for each of the 7 `LogScrubber` rules in `src/RoleRadar.Observability/LogScrubber.cs`, verify in an isolated test harness whether an OpenTelemetry `BaseProcessor<LogRecord>` can scrub `LogRecord.Body`, `LogRecord.FormattedMessage`, `LogRecord.Attributes`, and `LogRecord.Exception` text, and compare the OTLP log data model against the Feature 013 console record contract (`ADR 0047`)
- [X] T042 [US3] Author `docs/adr/0051-otel-logs-and-runtime-scrubbing.md` and update `docs/adr/README.md` recording the `RR-86` producer census matrix, the `BaseProcessor<LogRecord>` exception-scrubbing findings, the OTLP-vs-013 record shape evaluation, and the final architectural decision—treating **"drop `LogScrubber`"**, **"move `LogScrubber` into an OpenTelemetry log processor (`BaseProcessor<LogRecord>`)"**, **"shrink `LogScrubber`"**, and **"keep `LogScrubber`"** as real, first-class outcomes
- [X] T043 [US3] **CONDITIONAL (execute if `ADR 0051` decides to "drop `LogScrubber`" or "move `LogScrubber` into an OpenTelemetry log processor")**: Reopen the console formatter decision in `docs/adr/0047-console-formatter-and-validators.md` (recording the amendment/supersession in `docs/adr/0047-console-formatter-and-validators.md` and `docs/adr/0051-otel-logs-and-runtime-scrubbing.md`, and updating `src/RoleRadar.Observability/RoleRadarTextConsoleFormatter.cs`, `src/RoleRadar.Observability/RoleRadarJsonConsoleFormatter.cs`, and `src/RoleRadar.Observability/LoggingSetup.cs` to match the reopened formatter decision)

### Part 3B — Failing .NET and Web OTLP Log & Privacy Gate Tests (MANDATORY Test-First) ⚠️

- [X] T044 [US3] Pin `@opentelemetry/sdk-logs` (`3.0.0`) and `@opentelemetry/exporter-logs-otlp-http` (`0.300.0`) in `web/package.json` and update `pnpm-lock.yaml`
- [X] T045 [P] [US3] Extend `.NET` Telemetry Privacy Gate unit tests in `tests/RoleRadar.Tests.Unit/Observability/TelemetryPrivacyGateTests.cs` (no Docker, expected to fail before implementation), asserting verbatim `data-model.md` constraints for `TelemetryLogRecord`:
  - `SeverityNumber` / `SeverityText`: *"Mapped to standard OTLP severity (`TRACE`, `DEBUG`, `INFO`, `WARN`, `ERROR`, `FATAL`)."*
  - `Body`: *"Scrubbed per the Gate 3 (`RR-86` / `ADR 0051`) decision (`RoleRadarLogScrubbingProcessor` / `observability.ts`)."*
  - `TraceId` / `SpanId`: *"Populated automatically whenever an activity/span is current (`SC-004`)."*
  - `Attributes`: *"Sensitive keys redacted and string values scrubbed per `ADR 0051`."*
  - `Exception`: *"Scrubbed before OTLP serialization per `ADR 0051`."* (testing `InvalidOperationException` carrying `password=SENTINEL-EX-PW-1000` and `Bearer SENTINEL-EX-BEARER-1001`)
- [X] T046 [P] [US3] Write failing in-process `.NET` OTLP log export and correlation contract tests (no Docker / no Testcontainers) in `tests/RoleRadar.Tests.Contract/Observability/OtlpLogCorrelationContractTests.cs` and extend launched-host `Mcp.Stdio` stdout hygiene tests (`[Trait("Lane", "images")]`) in `tests/RoleRadar.Tests.Integration/Observability/CliMcpTelemetryIntegrationTests.cs` (expected to fail before implementation), verifying: (1) 0 OTLP log exporters are registered when `OTEL_EXPORTER_OTLP_ENDPOINT` is unset (`SC-003`); (2) when set, `Api` and `Identity` export `LogRecord` items carrying matching `TraceId`, `SpanId`, and `service.name` while console output (`ROLERADAR_LOG_FORMAT=text|json`) remains intact; and (3) `Mcp.Stdio` writes 0 bytes to `stdout` when OTLP logging is enabled (`FR-017`)
- [X] T047 [P] [US3] Extend web Telemetry Privacy Gate unit tests in `web/tests/unit/telemetry-privacy.test.ts` and write failing web OTLP log correlation unit tests in `web/tests/unit/telemetry-logs.test.ts` (expected to fail before implementation), verifying that `web/app/lib/log.server.ts` exports scrubbed OTLP log records correlated with the active `traceId` and `spanId` when `OTEL_EXPORTER_OTLP_ENDPOINT` is configured, and registers 0 OTLP log exporters when unset
- [X] T048 [US3] **RED-RUN**: Execute `dotnet test tests/RoleRadar.Tests.Unit -c Release --filter "FullyQualifiedName~TelemetryPrivacyGateTests"`, `dotnet test tests/RoleRadar.Tests.Contract -c Release --filter "FullyQualifiedName~OtlpLogCorrelationContractTests"`, `dotnet test tests/RoleRadar.Tests.Integration -c Release --filter "FullyQualifiedName~CliMcpTelemetryIntegrationTests"`, and `pnpm --filter web run test -- tests/unit/telemetry-privacy.test.ts tests/unit/telemetry-logs.test.ts` from Git Bash and confirm the new Gate 3 tests **FAIL (RED)** before implementing OTLP log export

### Part 3C — .NET and Web OTLP Log Implementation

- [X] T049 [US3] Implement `RoleRadarLogScrubbingProcessor` (`BaseProcessor<LogRecord>`) in `src/RoleRadar.Observability/RoleRadarLogScrubbingProcessor.cs` and update `src/RoleRadar.Observability/LogScrubber.cs` per the `ADR 0051` decision (depends on T045–T048 failing first), ensuring `LogRecord.Body`, `LogRecord.FormattedMessage`, `LogRecord.Attributes`, and scrubbed `LogRecord.Exception` attributes (`exception.type`, scrubbed `exception.message`, scrubbed `exception.stacktrace`) contain zero sentinel secrets
- [X] T050 [US3] Wire `ILoggingBuilder.AddOpenTelemetry` in `src/RoleRadar.Observability/LoggingSetup.cs` and `src/RoleRadar.Observability/TelemetrySetup.cs` (with `IncludeFormattedMessage = true`, `IncludeScopes = true`, `RoleRadarLogScrubbingProcessor`, and `AddOtlpExporter()` registered ONLY when `OTEL_EXPORTER_OTLP_ENDPOINT` is non-whitespace)
- [X] T051 [US3] Implement OTLP log provider registration in `web/app/lib/telemetry.server.ts` (`LoggerProvider` and `OTLPLogExporter` registered ONLY when `OTEL_EXPORTER_OTLP_ENDPOINT` is non-whitespace) and bridge structured log emission in `web/app/lib/log.server.ts` and `web/app/lib/observability.ts` so every web log record carries the active `traceId`/`spanId` and passes `RR-72` redaction
- [X] T052 [US3] Re-run `dotnet test tests/RoleRadar.Tests.Unit -c Release --filter "FullyQualifiedName~Telemetry|FullyQualifiedName~Log"`, `dotnet test tests/RoleRadar.Tests.Contract -c Release --filter "FullyQualifiedName~Redaction|FullyQualifiedName~OtlpLog"`, `dotnet test tests/RoleRadar.Tests.Integration -c Release --filter "FullyQualifiedName~CliMcpTelemetryIntegrationTests"`, and `pnpm --filter web run test` from Git Bash and verify all Gate 3 tests pass (**GREEN**)

### Gate 3 End Verification & Hygiene (`US3` PR Boundary)

- [X] T053 [US3] Run the Gate 3 standard verification suite from Git Bash (`RR-85`): `dotnet restore RoleRadar.sln --locked-mode`, `ROLERADAR_MONGO_CONNECTION_STRING="mongodb://127.0.0.1:1/?serverSelectionTimeoutMS=100" dotnet build RoleRadar.sln -c Release -v q --nologo -warnaserror`, `pwsh ./scripts/test-fast.ps1`, `bash scripts/linux-web-check.sh`, `pre-commit run --all-files`, and `pwsh ./scripts/review-gate.ps1`
- [X] T054 [US3] Verify Gate 3 PR hygiene: confirm `git diff .github/dependabot.yml` is empty, confirm no new test project was added under `tests/`, and confirm all modified/created files have LF (`\n`) line endings — **STOP AT GATE 3 PR BOUNDARY FOR REVIEW**

> **Gate 2b / Gate 3 Execution Notes (`T040a`–`T054`, 2026-11-11)**:
> - **Part A (Gate 2b: `OpenTelemetry.Instrumentation.AspNetCore` 1.20.1)**: Replaced hand-set HTTP span tags in `RoleRadar.Api` and `RoleRadar.Identity` with official `OpenTelemetry.Instrumentation.AspNetCore` 1.20.1 (Principle X, maintained library). Updated `packages.lock.json` files and confirmed `OpenTelemetry.Instrumentation.AspNetCore` is referenced only in `Api` and `Identity`, never `Observability`.
> - **Part B1 (ADR 0051 Producer Census from Observation)**: Redid producer census from empirical observation at repo's default log levels (`LoggingSetup.DefaultLevels`: `Microsoft = Warning`, `System = Warning`). Established that Rules 1–6 produce no output at default levels. Proved that Rule 6 (`Microsoft.AspNetCore.Hosting.Diagnostics`) query string logging is suppressed at default `Warning` level and appears only when an operator overrides `Logging__LogLevel__Microsoft = Information` (exposing raw OAuth codes/state). Evaluated Duende at default `Information` (validation success event without raw secrets; verbose diagnostics require `Debug`/`Trace`). Set ADR 0051 status to `Proposed` with recommendation to keep `LogScrubber` as a shared utility.
> - **Part B2 (Exception Handling & Scopes in `RoleRadarLogScrubbingProcessor`)**: Fixed exception handling by setting `LogRecord.Exception = null` and exporting structured attributes `exception.type` (original type's `FullName`), scrubbed `exception.message`, and no stack trace. Proved scopes are readonly in OpenTelemetry .NET; set `IncludeScopes = false` across logging builders (`LoggingSetup.cs`, `TelemetrySetup.cs`) to eliminate the unscrubbed scope leakage vector.
> - **Part B3 (RR-90 RequestCompleted Platform Feature Swap)**: Replaced both hand-written `RequestCompletedMiddleware` copies with native ASP.NET Core `AddHttpLogging` (`CombineLogs = true`, `LoggingFields = RequestMethod | RequestPath | ResponseStatusCode | Duration`). Excluded health paths (`/health`, `/health/live`, `/health/ready`) with `.WithHttpLogging(HttpLoggingFields.None)`. Added `("Microsoft.AspNetCore.HttpLogging", LogLevel.Information)` to `LoggingSetup.DefaultLevels`. Updated 013 contract tests and docs first (shown red before the swap, green after). Confirmed multi-line combined record outputs strictly one line per request in both text and JSON console formatters.
> - **Part B4 (`@opentelemetry/api-logs`)**: Dropped direct `@opentelemetry/api-logs` dependency from `web/package.json` and updated `pnpm-lock.yaml`.
> - **Verification Results (`T052`–`T054`)**:
>   - Architecture: 66/66 passed
>   - Unit: 1002/1002 passed
>   - Contract: 324/324 passed
>   - Fast tests total: 1392/1392 passed
>   - Web typecheck (`pnpm --filter web run typecheck`): passed (code 0)
>   - Web lint (`pnpm --filter web run lint`): passed (0 errors)
>   - Web build (`pnpm --filter web run build`): passed (code 0)
>   - Pre-commit (`pre-commit run --all-files`): passed (code 0)
>   - Hygiene: 0 diff in `.github/dependabot.yml`, no new test projects, all files strictly LF line endings.

---

## Phase 6: User Story 4 / Gate 4 - Service Request and Datastore Health Metrics (Priority: P4)

**Goal**: Instrument and export HTTP RED metrics (`http.server.request.duration`, `http.server.active_requests`, `http.client.request.duration`), runtime health metrics, and datastore command/readiness health metrics (`roleradar.store.operation.duration`, `roleradar.store.readiness.status`, `roleradar.store.readiness.duration`) over OTLP across `roleradar-web`, `roleradar-api`, and `roleradar-identity` (`RR-69`), guarded by the `RR-72` metric label cardinality and privacy gate (`SC-005`).

**Independent Test**: Drive synthetic requests, error responses, and healthy/degraded datastore readiness checks across `.NET` and `web/` with in-memory metric readers, verifying all required instruments are exported with accurate measurements and 0 forbidden high-cardinality or personal labels (`owner_id`, entity GUIDs, `traceId`, `sub`, `username`, raw `url.full`/`url.query`, or free text).

### Part 4A — Failing .NET and Web Metric & Label-Cardinality Tests (MANDATORY Test-First) ⚠️

- [X] T055 [US4] Pin `@opentelemetry/sdk-metrics` (`3.0.0`) and `@opentelemetry/exporter-metrics-otlp-http` (`0.300.0`) in `web/package.json` and update `pnpm-lock.yaml`
- [X] T056 [P] [US4] Extend `.NET` Telemetry Privacy Gate unit tests in `tests/RoleRadar.Tests.Unit/Observability/TelemetryPrivacyGateTests.cs` and write failing datastore metric unit tests in `tests/RoleRadar.Tests.Unit/Observability/StoreMetricsTests.cs` (no Docker, expected to fail before implementation), asserting verbatim `data-model.md` constraints for `TelemetryMetricInstrument`:
  - `roleradar.store.operation.duration`: `Histogram (s)` on meter `RoleRadar.Store` with allowed attributes `db.system.name (mongodb, sqlite), db.operation.name, db.collection.name, error.type`
  - `roleradar.store.readiness.status`: `ObservableGauge / Counter ({status})` on meter `RoleRadar.Store` with allowed attributes `db.system.name (mongodb, sqlite), status (healthy, unhealthy)`
  - `roleradar.store.readiness.duration`: `Histogram (s)` on meter `RoleRadar.Store` with allowed attributes `db.system.name (mongodb, sqlite), status (healthy, unhealthy)`
  - `Cardinality & Privacy Rule`: *"Forbidden labels: `owner_id`, entity GUIDs (`posting_id`, `company_id`, `application_id`, `note_id`), `traceId`, `sub`, `username`, raw `url.full` / `url.query`, or free-text search/filter strings."*
- [X] T057 [P] [US4] Write failing in-process `.NET` service metrics contract tests (no Docker / no Testcontainers) in `tests/RoleRadar.Tests.Contract/Observability/ServiceMetricsContractTests.cs` and extend Testcontainers MongoDB metric integration tests (`[Trait("Lane", "default")]`) in `tests/RoleRadar.Tests.Integration/Observability/MongoTelemetryIntegrationTests.cs` (expected to fail before implementation), verifying that: (1) 0 OTLP metric exporters are registered when `OTEL_EXPORTER_OTLP_ENDPOINT` is unset (`SC-003`); (2) when set, `RoleRadar.Api` and `RoleRadar.Identity` export `http.server.request.duration`, `http.server.active_requests`, `http.client.request.duration`, `.NET` runtime metrics (`System.Runtime`), and `RoleRadar.Store` readiness/operation metrics (SQLite in `Contract`, Testcontainers MongoDB `rs0` in `Integration`); and (3) all exported metric data points obey the `SC-005` label cardinality allowlist
- [X] T058 [P] [US4] Extend web Telemetry Privacy Gate unit tests in `web/tests/unit/telemetry-privacy.test.ts` and write failing web metrics unit tests in `web/tests/unit/telemetry-metrics.test.ts` (expected to fail before implementation), asserting that `roleradar-web` exports `http.server.request.duration` (with allowed attributes `http.request.method, http.route, http.response.status_code, error.type, url.scheme`), `http.server.active_requests` (`http.request.method, url.scheme`), `http.client.request.duration` (`http.request.method, http.response.status_code, server.address, server.port, error.type`), and Node runtime memory/event-loop metrics with 0 forbidden high-cardinality labels (`owner_id`, entity GUIDs, `traceId`, `sub`, `username`, raw `url.full`/`url.query`)
- [X] T059 [US4] **RED-RUN**: Execute `dotnet test tests/RoleRadar.Tests.Unit -c Release --filter "FullyQualifiedName~StoreMetricsTests|FullyQualifiedName~TelemetryPrivacyGateTests"`, `dotnet test tests/RoleRadar.Tests.Contract -c Release --filter "FullyQualifiedName~ServiceMetricsContractTests"`, `dotnet test tests/RoleRadar.Tests.Integration -c Release --filter "FullyQualifiedName~MongoTelemetryIntegrationTests"`, and `pnpm --filter web run test -- tests/unit/telemetry-privacy.test.ts tests/unit/telemetry-metrics.test.ts` from Git Bash and confirm the new Gate 4 tests **FAIL (RED)** before implementing metric instrumentation

### Part 4B — .NET and Web Metric Implementation

- [X] T060 [US4] Implement `RoleRadarStoreMetrics` (`Meter("RoleRadar.Store")` with `roleradar.store.operation.duration`, `roleradar.store.readiness.status`, and `roleradar.store.readiness.duration`) in `src/RoleRadar.Observability/RoleRadarStoreMetrics.cs` and wire `.WithMetrics(...)` in `src/RoleRadar.Observability/TelemetrySetup.cs` (depends on T056–T059 failing first), subscribing to `"Microsoft.AspNetCore.Hosting"`, `"Microsoft.AspNetCore.Server.Kestrel"`, `"Microsoft.AspNetCore.Routing"`, `"Microsoft.AspNetCore.Diagnostics"`, `"Microsoft.AspNetCore.RateLimiting"`, `"System.Net.Http"`, `"System.Net.NameResolution"`, `"System.Runtime"`, and `"RoleRadar.Store"` and registering `AddOtlpExporter()` ONLY when `OTEL_EXPORTER_OTLP_ENDPOINT` is non-whitespace (`SC-003`)
- [X] T061 [P] [US4] Record MongoDB readiness status, readiness probe duration, and MongoDB command operation duration into `RoleRadarStoreMetrics` in `src/RoleRadar.Infrastructure/Mongo/MongoReadinessProbe.cs` and `src/RoleRadar.Infrastructure/Mongo/ConnectionStringRedactor.cs` (via `MongoDB.Driver` command succeeded/failed events or activity listener) using only bounded tags (`db.system.name = "mongodb"`, `db.operation.name`, `db.collection.name`, `status`, `error.type`)
- [X] T062 [P] [US4] Record Identity SQLite readiness status and duration into `RoleRadarStoreMetrics` (`db.system.name = "sqlite"`, `status = "healthy" | "unhealthy"`) in `src/RoleRadar.Identity/Observability/IdentityReadinessCheck.cs`
- [X] T063 [US4] Implement `MeterProvider`, `OTLPMetricExporter` (registered ONLY when `OTEL_EXPORTER_OTLP_ENDPOINT` is non-whitespace), HTTP RED instruments (`http.server.request.duration`, `http.server.active_requests`, `http.client.request.duration` with route-template normalization so dynamic path segments and query strings never become labels), and Node runtime health gauges in `web/app/lib/telemetry.server.ts`, `web/server.ts`, `web/app/lib/api-client.server.ts`, and `web/app/lib/oidc.server.ts`
- [X] T064 [US4] Re-run `dotnet test tests/RoleRadar.Tests.Unit -c Release --filter "FullyQualifiedName~StoreMetricsTests|FullyQualifiedName~Telemetry"`, `dotnet test tests/RoleRadar.Tests.Contract -c Release --filter "FullyQualifiedName~ServiceMetricsContractTests"`, `dotnet test tests/RoleRadar.Tests.Integration -c Release --filter "FullyQualifiedName~MongoTelemetryIntegrationTests"`, and `pnpm --filter web run test` from Git Bash and verify all Gate 4 tests pass (**GREEN**)

### Gate 4 End Verification & Hygiene (`US4` PR Boundary)

- [X] T065 [US4] Run the Gate 4 standard verification suite from Git Bash (`RR-85`): `dotnet restore RoleRadar.sln --locked-mode`, `ROLERADAR_MONGO_CONNECTION_STRING="mongodb://127.0.0.1:1/?serverSelectionTimeoutMS=100" dotnet build RoleRadar.sln -c Release -v q --nologo -warnaserror`, `pwsh ./scripts/test-fast.ps1`, `bash scripts/linux-web-check.sh`, `pre-commit run --all-files`, and `pwsh ./scripts/review-gate.ps1`
- [X] T066 [US4] Verify Gate 4 PR hygiene: confirm `git diff .github/dependabot.yml` is empty, confirm no new test project was added under `tests/`, and confirm all modified/created files have LF (`\n`) line endings — **STOP AT GATE 4 PR BOUNDARY FOR REVIEW**

> **Gate 4 Execution Notes (`T055`–`T066`, 2026-11-11)**:
> - **Standards First & Empirical Investigation**: Authored `ADR 0052` documenting that .NET 10 HealthChecks, MongoDB.Driver 3.13.0, and SQLite emit 0 native meters; native .NET 10 meters (`Microsoft.AspNetCore.Hosting`, `System.Net.Http`, `System.Runtime`, `Microsoft.AspNetCore.Server.Kestrel`, `Microsoft.AspNetCore.Routing`) handle HTTP server, client, runtime, and server metrics directly.
> - **Single `RoleRadar.Store` Meter & Architecture Separation**: Moved `RoleRadarStoreMetrics` to `RoleRadar.Domain.Common`. Defined one single `Meter("RoleRadar.Store")` referenced by both `Infrastructure` and `Identity` (with zero references to `RoleRadar.Observability`), enforced by architecture test `ExactlyOneMeterNamedRoleRadarStore_ExistsAcrossSolution`.
> - **Datastore Readiness Status Gauge**: Implemented `roleradar.store.readiness.status` as an `ObservableGauge<int>` reporting the last probe result per `db.system.name` (1 = healthy, 0 = unhealthy), verified with consecutive probe tests asserting the value reports the last state (0) rather than an UpDownCounter sum.
> - **MongoDB Command Duration via Driver Events**: Subscribed to MongoDB.Driver 3.13.0 `CommandStartedEvent`, `CommandSucceededEvent`, and `CommandFailedEvent` via `settings.ClusterConfigurator` in `ConnectionStringRedactor.cs`, recording command duration and known collection names. Removed metric recording code from `RoleRadarSpanAttributeProcessor.cs`.
> - **Web Route Cardinality & Instrument Lifecycle**: Implemented `resolveHttpRoute` using React Router's `matchRoutes` against build/default route definitions, bounding numeric IDs, slugs, query strings, and trailing slashes to route templates (e.g. `/postings/:id`), and clamping unknown 404 paths to `"unmatched"`. Derived `url.scheme` from socket encryption (`req.socket.encrypted`). Pre-instantiated all metric instruments once during `initializeWebTelemetry()` instead of per-call.
> - **Web Auto-Instrumentation & Runtime Packages Evaluation**: Evaluated `@opentelemetry/instrumentation-http`, `@opentelemetry/instrumentation-undici`, and `@opentelemetry/instrumentation-runtime-node`. Removed unneeded http/undici pins from `web/package.json` and `pnpm-lock.yaml`. Added `nodejs.eventloop.utilization` gauge via BCL `performance.eventLoopUtilization()`.
> - **Integration & Privacy Gate Allowlist Assertions**: Replaced negative key lists in integration tests with allowlist assertions using the exact `TelemetrySetup.Allowed*TagKeys` constants that Views use. Drove forbidden tags (`owner_id`, `url.query`, `traceId`) through `RoleRadarStoreMetrics` and confirmed they were dropped by OpenTelemetry Views.
> - **CLI & Mcp.Stdio Metrics Opt-in**: Added integration tests verifying CLI and MCP register 0 metric exporters when endpoint is unset, export metrics safely when opted in, and write 0 non-MCP bytes to `stdout`.

---

## Phase 7: User Story 5 / Gate 5 - One-Command Local Dev Loop and Aspire Dashboard Built-In Views (Priority: P5)

**Goal**: Implement the single-command local telemetry setup (`RR-71`) confirmed by `ADR 0049` (either `src/RoleRadar.AppHost` with `Aspire.Hosting.*` `13.6.4` or the fallback `deploy/docker-compose.telemetry.yml` overlay with the standalone Aspire Dashboard `mcr.microsoft.com/dotnet/aspire-dashboard:13.6.4@sha256:23411f715be2476da0b2c0ad110039237de657c6cae31bf5c587d12a26c11230` bound exclusively to `127.0.0.1`), enforce the runtime and architecture safety guards so it never touches port `27017`/`roleradar-mongo`/`roleradar-mongo-data`/`roleradar-identity-data`, add automated Compose-vs-AppHost drift tests (`SC-007`), and execute the manual Aspire Dashboard verification check on the isolated stack with fictional data (`SC-006`).

**Independent Test**: Run `dotnet test tests/RoleRadar.Tests.Architecture -c Release` to verify layering, real-volume isolation, and zero drift between `deploy/docker-compose*.yml` and the local telemetry setup (`SC-007`), and perform the manual Aspire Dashboard verification check on `127.0.0.1` against the isolated fictional stack, recording the required evidence (`SC-006`).

### Part 5A — Failing Architecture, Safety Guard, and Drift Tests (MANDATORY Test-First) ⚠️

- [ ] T067 [P] [US5] Write failing architecture layering tests for `RoleRadar.AppHost` in `tests/RoleRadar.Tests.Architecture/LayerDependencyArchitectureTests.cs` and `tests/RoleRadar.Tests.Architecture/ObservabilityArchitectureTests.cs` (no Docker, expected to fail before implementation), asserting that `RoleRadar.AppHost` may reference host projects (`RoleRadar.Api`, `RoleRadar.Identity`), MUST NOT reference `Duende.*` packages directly, and zero projects in `src/` (`Domain`, `Application`, `Infrastructure`, `Observability`, `Api`, `Identity`, `Cli`, `Mcp.Stdio`) may reference `RoleRadar.AppHost`
- [ ] T068 [P] [US5] Write failing Compose-vs-AppHost drift and volume-safety architecture tests (`SC-003`, `SC-006`, `SC-007`) in `tests/RoleRadar.Tests.Architecture/AppHostComposeParityArchitectureTests.cs` (no Docker, expected to fail before implementation), parsing `deploy/docker-compose.yml` and `deploy/docker-compose.apps.yml` and comparing them against the local telemetry orchestration (`src/RoleRadar.AppHost/Program.cs` or `deploy/docker-compose.telemetry.yml`) to assert:
  - Matching pinned MongoDB image (`mongo:9.0.4`), replica set (`rs0`), and `GLIBC_TUNABLES=glibc.pthread.rseq=1`
  - Matching service ports (`5001` Identity, `5002` API, `5003` Web) and required OIDC/API environment variables
  - Pinned Aspire Dashboard image digest (`mcr.microsoft.com/dotnet/aspire-dashboard:13.6.4@sha256:23411f715be2476da0b2c0ad110039237de657c6cae31bf5c587d12a26c11230`) bound exclusively to `127.0.0.1`
  - Safety guard invariant: complete absence and active rejection of real volume names (`roleradar-mongo-data`, `roleradar-identity-data`), container `roleradar-mongo`, and the developer's default MongoDB port `27017`
- [ ] T069 [US5] **RED-RUN**: Execute `dotnet test tests/RoleRadar.Tests.Architecture -c Release --filter "FullyQualifiedName~AppHost|FullyQualifiedName~LayerDependency|FullyQualifiedName~ObservabilityArchitecture"` from Git Bash and confirm the new Gate 5 tests **FAIL (RED)** before creating `src/RoleRadar.AppHost` (or `deploy/docker-compose.telemetry.yml`)

### Part 5B — Local Telemetry Setup, Safety Guard & Manual Dashboard Check

- [ ] T070 [US5] Pin `Aspire.Hosting.AppHost` (`13.6.4`), `Aspire.Hosting.MongoDB` (`13.6.4`), and `Aspire.Hosting.JavaScript` (`13.6.4`) in `Directory.Packages.props`, create `src/RoleRadar.AppHost/RoleRadar.AppHost.csproj` (or `deploy/docker-compose.telemetry.yml` if `ADR 0049` selected the fallback), register `src/RoleRadar.AppHost/RoleRadar.AppHost.csproj` in `RoleRadar.sln`, and generate `src/RoleRadar.AppHost/packages.lock.json`
- [ ] T071 [US5] Implement the fail-closed runtime safety guard (`AssertIsolatedAppHostEnvironment`) and local orchestration in `src/RoleRadar.AppHost/Program.cs` (and `src/RoleRadar.AppHost/Properties/launchSettings.json`, or `deploy/docker-compose.telemetry.yml` per `ADR 0049`) (depends on T067–T069 failing first):
  - Safety guard refuses to start (throws `InvalidOperationException` before launching resources) if any configured connection string, container name, host port, or volume name references port `27017`, `roleradar-mongo`, `roleradar-mongo-data`, or `roleradar-identity-data`, requiring isolated throwaway container/volume names (`roleradar-apphost-*`) and fictional seed data only
  - Binds the Aspire Dashboard (`mcr.microsoft.com/dotnet/aspire-dashboard:13.6.4@sha256:23411f715be2476da0b2c0ad110039237de657c6cae31bf5c587d12a26c11230`) UI and OTLP endpoints exclusively to `127.0.0.1` (`SC-003`)
  - Configures `mongo:9.0.4` (`--replSet rs0`, `GLIBC_TUNABLES=glibc.pthread.rseq=1`) on an isolated throwaway volume (`roleradar-apphost-mongo-data`), `roleradar-identity` (`5001`) on an isolated throwaway SQLite/keys path (`roleradar-apphost-identity-data`), `roleradar-api` (`5002`), and `roleradar-web` (`5003`) with `OTEL_EXPORTER_OTLP_ENDPOINT` pointing to the loopback Aspire Dashboard OTLP receiver
- [ ] T072 [P] [US5] Document the one-command local telemetry dev loop, built-in Aspire Dashboard views (traces, structured logs, metrics, resource graph), loopback binding (`127.0.0.1`), and throwaway volume isolation in `README.md` and `specs/015-observability/quickstart.md`
- [ ] T073 [US5] Re-run `dotnet test tests/RoleRadar.Tests.Architecture -c Release` from Git Bash and verify all `LayerDependencyArchitectureTests`, `ObservabilityArchitectureTests`, and `AppHostComposeParityArchitectureTests` pass (**GREEN**)
- [ ] T074 [US5] Execute the manual Aspire Dashboard verification check (`SC-006`) on the isolated local stack with strictly fictional data (since the Aspire Dashboard has no public query API to automate UI view assertions, as documented in `specs/015-observability/contracts/local-dev-loop.md`), and record the verification evidence in the Gate 5 report: (1) startup command and `127.0.0.1` loopback port binding output; (2) **Resources** view showing `roleradar-web`, `roleradar-api`, `roleradar-identity`, and `mongo` (`rs0`) `Running`/`Healthy` on throwaway `roleradar-apphost-*` volumes; (3) **Traces** view showing the observed 32-hex W3C `traceId` for a fictional sign-in (`web`->`identity`) and a fictional posting transition (`web`->`api`->`mongodb` transaction); (4) **Structured Logs** view showing correlated `TraceId`/`SpanId` log records with `[redacted]` placeholders and zero raw secrets; (5) **Metrics** view showing `http.server.request.duration`, `roleradar.store.readiness.status`, and `roleradar.store.operation.duration`; and (6) post-run `docker volume ls` output confirming `roleradar-mongo-data` and `roleradar-identity-data` were untouched

### Gate 5 End Verification & Hygiene (`US5` PR Boundary)

- [ ] T075 [US5] Run the Gate 5 standard verification suite from Git Bash (`RR-85`): `dotnet restore RoleRadar.sln --locked-mode`, `ROLERADAR_MONGO_CONNECTION_STRING="mongodb://127.0.0.1:1/?serverSelectionTimeoutMS=100" dotnet build RoleRadar.sln -c Release -v q --nologo -warnaserror`, `pwsh ./scripts/test-fast.ps1`, `bash scripts/linux-web-check.sh`, `pre-commit run --all-files`, and `pwsh ./scripts/review-gate.ps1`
- [ ] T076 [US5] Verify Gate 5 PR hygiene: confirm `git diff .github/dependabot.yml` is empty, confirm no new test project was added under `tests/`, confirm `docker volume ls` shows zero modifications to `roleradar-mongo-data` or `roleradar-identity-data`, and confirm all modified/created files have LF (`\n`) line endings — **STOP AT GATE 5 PR BOUNDARY FOR REVIEW**

---

## Phase 8: Polish & Cross-Cutting Close-Out

**Purpose**: Final cross-artifact verification and close-out audit across `SC-001` through `SC-007` and `FR-001` through `FR-017` using live command output only.

- [ ] T077 [P] Verify `docs/adr/README.md` lists `0049-local-observability-apphost-and-dashboard.md`, `0050-span-attribute-allowlist-processor.md`, and `0051-otel-logs-and-runtime-scrubbing.md` in sequential numerical order with valid links and LF line endings
- [ ] T078 Close-out verification (T-final): check every Success Criterion (`SC-001` through `SC-007`) and Functional Requirement (`FR-001` through `FR-017`) in `specs/015-observability/spec.md` against actual command output from `pwsh ./scripts/test-fast.ps1`, `bash scripts/linux-web-check.sh`, `pwsh ./scripts/test-e2e-real.ps1`, `pre-commit run --all-files`, and `pwsh ./scripts/review-gate.ps1` (run from Git Bash), plus the Gate 5 manual dashboard evidence record (`T074`), verify zero diff in `.github/dependabot.yml` and LF line endings on all touched files, and report exact numbers from command output only without committing or pushing

---

## Dependencies & Execution Order

### Gate / Phase Dependencies (Sequential PR Boundaries)

1. **Phase 1 (Setup)** + **Phase 2 (Foundational Scratch Safety Guard)** → Must complete before Gate 1 begins.
2. **Phase 3 — Gate 1 (`US1`, P1, `RR-67` Spike & `ADR 0049`)**: Docs-only PR. Stops at `T013` for PR review.
3. **Phase 4 — Gate 2 (`US2`, P2, `RR-68` Tracing + `RR-72` Privacy Gate + `ADR 0050`)**: Depends on Gate 1 completion. Stands completely independent of `RR-86` (`ADR 0051`) in Gate 3 because `RoleRadarSpanAttributeProcessor` and `WebSpanAttributeProcessor` do not depend on `LogScrubber`. Includes the `SC-001` real-stack OTLP Collector proof (`T032`, `T038`). Stops at `T040` for PR review. *(Follow-up outside feature PRs after Gate 2 merges: separate small PR to group `@opentelemetry/*` and `OpenTelemetry*` in `.github/dependabot.yml`.)*
4. **Phase 5 — Gate 3 (`US3`, P3, `RR-86` Scrubbing Evaluation `ADR 0051` + `RR-70` OTLP Logs + `RR-72`)**: Depends on Gate 2 (`TelemetrySetup.cs` and `telemetry.server.ts`). Starts with the `RR-86` producer census (`T041`) and `ADR 0051` decision (`T042`), plus conditional `ADR 0047` formatter reopening (`T043`) before any Gate 3 code. Stops at `T054` for PR review.
5. **Phase 6 — Gate 4 (`US4`, P4, `RR-69` Service RED & Store Health Metrics + `RR-72` Label Cardinality)**: Depends on Gate 2/3 (`TelemetrySetup.cs` and `telemetry.server.ts`). Stops at `T066` for PR review.
6. **Phase 7 — Gate 5 (`US5`, P5, `RR-71` Local AppHost / Compose Overlay + Aspire Dashboard + Drift Tests)**: Depends on Gate 1 (`ADR 0049`) and Gates 2–4 (telemetry signals). Stops at `T076` for PR review.
7. **Phase 8 — Close-Out (`T077`–`T078`)**: Runs after Gate 5 verification to report final command-backed metrics for `SC-001`..`SC-007`.

### Within Each Code Gate (Mandatory Test-First Workflow)

1. Write failing unit/contract/integration/architecture tests first (`RR-72` privacy gate tests before any exporter code; Docker/Testcontainers/launched-host tests in `RoleRadar.Tests.Integration` with `[Trait("Lane", "default" | "images" | "real-stack")]`).
2. Execute the explicit **RED-RUN** task (`T021`, `T033`, `T048`, `T059`, `T069`) and verify the new tests fail.
3. Implement the production code and processors to make the tests pass (**GREEN**).
4. Run the Gate-End verification suite (`dotnet restore --locked-mode`, offline build with Mongo unreachable, `test-fast.ps1`, `linux-web-check.sh` where web changed, `pre-commit run --all-files`, `review-gate.ps1`) from Git Bash (`RR-85`).
5. Run the Gate-End hygiene check (`git diff .github/dependabot.yml` empty, no new test project, LF line endings) and **stop at the gate PR boundary**.

### Parallel Opportunities (`[P]`)

- **Phase 1**: `T002` and `T003` can run in parallel.
- **Gate 2 (`US2`)**: `T015` (`ADR 0050`), `T017` (`TelemetrySetupTests.cs`), `T018` (`DistributedTracingContractTests.cs`), `T019` (`MongoTelemetryIntegrationTests.cs`), and `T020` (`CliMcpTelemetryIntegrationTests.cs`) can be authored in parallel after `T014` (`T016` privacy gate test is written first). Host wiring in `T024` (`ConnectionStringRedactor.cs`), `T025` (`Api`), `T026` (`Identity`), and `T027` (`Cli`/`Mcp.Stdio`) can run in parallel after `T023`. In web/E2E, `T031` (`telemetry-tracing.test.ts`) and `T032` (`RealStackEndToEndTests.cs`) can be authored in parallel after `T030` (`telemetry-privacy.test.ts`).
- **Gate 3 (`US3`)**: Failing test tasks `T045` (`.NET` unit), `T046` (`.NET` contract + `Lane=images` integration), and `T047` (`web` unit) touch separate files and can be written in parallel before `T048` (**RED-RUN**).
- **Gate 4 (`US4`)**: Failing test tasks `T056` (`.NET` unit), `T057` (`.NET` contract + `Lane=default` integration), and `T058` (`web` unit) can be written in parallel before `T059` (**RED-RUN**); store metric hooks `T061` (`MongoReadinessProbe.cs`) and `T062` (`IdentityReadinessCheck.cs`) can be implemented in parallel after `T060`.
- **Gate 5 (`US5`)**: Failing architecture test tasks `T067` (`LayerDependencyArchitectureTests.cs` / `ObservabilityArchitectureTests.cs`) and `T068` (`AppHostComposeParityArchitectureTests.cs`) can be written in parallel before `T069` (**RED-RUN**).

---

## Parallel Example: Gate 2 (`US2`) Test-First Launch

```bash
# Step 1: Write RR-72 Privacy Gate tests first, then parallel tracing/setup/integration tests:
Task T016: "Write failing .NET Telemetry Privacy Gate unit tests in tests/RoleRadar.Tests.Unit/Observability/TelemetryPrivacyGateTests.cs"
Task T017 [P]: "Write failing .NET telemetry configuration and SC-003 unit tests in tests/RoleRadar.Tests.Unit/Observability/TelemetrySetupTests.cs"
Task T018 [P]: "Write failing in-process .NET distributed tracing contract tests in tests/RoleRadar.Tests.Contract/Observability/DistributedTracingContractTests.cs"
Task T019 [P]: "Write failing Testcontainers MongoDB driver span integration tests ([Trait(\"Lane\", \"default\")]) in tests/RoleRadar.Tests.Integration/Observability/MongoTelemetryIntegrationTests.cs"
Task T020 [P]: "Write failing launched-host CLI and Mcp.Stdio telemetry integration tests ([Trait(\"Lane\", \"images\")]) in tests/RoleRadar.Tests.Integration/Observability/CliMcpTelemetryIntegrationTests.cs"

# Step 2: Confirm RED before writing any exporter or processor code:
Task T021: "RED-RUN (.NET): Execute dotnet test ... and confirm the new tests FAIL (RED)"

# Step 3: Implement processor and shared setup, then wire hosts in parallel:
Task T022: "Implement RoleRadarSpanAttributeProcessor in src/RoleRadar.Observability/RoleRadarSpanAttributeProcessor.cs"
Task T023: "Implement TelemetrySetup in src/RoleRadar.Observability/TelemetrySetup.cs"
Task T024 [P]: "Configure explicit TracingOptions in src/RoleRadar.Infrastructure/Mongo/ConnectionStringRedactor.cs"
Task T025 [P]: "Wire AddRoleRadarTelemetry in src/RoleRadar.Api/Program.cs"
Task T026 [P]: "Wire AddRoleRadarTelemetry in src/RoleRadar.Identity/Program.cs"
Task T027 [P]: "Wire opt-in telemetry and bounded flush in src/RoleRadar.Cli/Program.cs and src/RoleRadar.Mcp.Stdio/Program.cs"
```

---

## Implementation Strategy

### MVP First (Gate 1 / User Story 1 Only)

1. Complete Phase 1 (Setup) and Phase 2 (Foundational Scratch Safety Guard).
2. Complete Phase 3 (Gate 1 / `US1`: Scratch spike in `$TEMP/roleradar-spike-015/` + `ADR 0049` docs-only PR).
3. **STOP and VALIDATE**: Run `T012` and `T013` from Git Bash and open the Gate 1 docs-only PR.

### Incremental Gate-by-Gate Delivery

1. **Gate 1 (`US1`, P1)**: `RR-67` spike + `ADR 0049` (docs-only PR).
2. **Gate 2 (`US2`, P2)**: `RR-68` distributed tracing + `RR-72` span privacy gate + `ADR 0050` (independent of `RR-86`) + `SC-001` real-stack OTLP Collector proof (`scripts/test-e2e-real.ps1`). *(Followed outside the feature PRs by the small `.github/dependabot.yml` grouping PR.)*
3. **Gate 3 (`US3`, P3)**: `RR-86` producer census & `ADR 0051` (plus conditional `ADR 0047` formatter reopening) + `RR-70` OTLP structured logs + `RR-72` log privacy gate.
4. **Gate 4 (`US4`, P4)**: `RR-69` HTTP RED, runtime, and datastore readiness/command metrics + `RR-72` metric label cardinality gate.
5. **Gate 5 (`US5`, P5)**: `RR-71` one-command local AppHost (or compose overlay fallback per `ADR 0049`) + Aspire Dashboard on `127.0.0.1` + architecture drift guard + manual dashboard check (`T074`) + `T078` final close-out report.
