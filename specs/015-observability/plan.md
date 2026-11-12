# Implementation Plan: End-to-End Observability and Local Telemetry Dev Loop

**Branch**: `015-observability` | **Date**: 2026-11-11 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/015-observability/spec.md`

## Summary

Feature 015 (`RR-66`, children `RR-67` to `RR-72` plus `RR-86`) adds end-to-end OpenTelemetry distributed tracing, structured logging over OTLP, RED and datastore health metrics, an automated Telemetry Privacy Gate (`RR-72`), and a single-command local developer telemetry setup using the Aspire Dashboard bound to `127.0.0.1`. It builds directly on Feature 014's W3C Trace Context (`traceparent`, `.NET Activity`, and `@opentelemetry/api` + `@opentelemetry/core` in the web app) without changing the 32-character hexadecimal `traceId` used in logs, RFC 9457 problem documents, and `request_correlation_id` audit entries.

Work is delivered across **five sequential, independently verifiable gates**, each with its own pull request:
1. **Gate 1 (P1, Docs Only)**: `RR-67` spike in an isolated scratch directory with fictional data and `ADR 0049` comparing the Aspire AppHost + Dashboard against Seq, Jaeger, and Grafana LGTM, verifying replica-set MongoDB, Identity issuer/TLS parity, drift prevention, and built-in dashboard views (with a defined fallback to the standalone Aspire Dashboard in an opt-in compose overlay if the AppHost hits a blocker).
2. **Gate 2 (P2)**: `RR-68` distributed tracing across web, API, Identity, and MongoDB (using `MongoDB.Driver` 3.13.0's native `MongoTelemetry` `ActivitySource` and replacing the web's manual `trace-context.server.ts` ID/span wiring with the OpenTelemetry JS SDK so there is one mechanism, not two) + `RR-72` Telemetry Privacy Gate for spans using a `LogScrubber`-independent allowlist and drop-list processor (`ADR 0050`) and no-endpoint registration (`SC-003`). Gate 2 stands completely independent of `RR-86`'s outcome in Gate 3. (If Gate 2's tasks exceed ~45, it splits into Gate 2a for `.NET` first and Gate 2b for `web` second.)
3. **Gate 3 (P3)**: `RR-86` evidence-first runtime log scrubbing evaluation (`ADR 0051`) + `RR-70` OTLP structured logs + `RR-72` log privacy gate tests.
4. **Gate 4 (P4)**: `RR-69` HTTP RED metrics, runtime health metrics, and datastore readiness/command health metrics over OTLP + `RR-72` metric label cardinality/privacy tests.
5. **Gate 5 (P5)**: `RR-71` local Aspire AppHost (or compose overlay fallback per `ADR 0049`), architecture layering rules, and automated Compose-vs-AppHost drift tests in `RoleRadar.Tests.Architecture`.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`, `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`) and TypeScript 5.9.3 / Node 22 (React Router v7 SSR).

**Primary Dependencies**:
- **.NET (`src/RoleRadar.Observability`)**: `OpenTelemetry` `1.20.1`, `OpenTelemetry.Extensions.Hosting` `1.20.1`, `OpenTelemetry.Exporter.OpenTelemetryProtocol` `1.20.1` (all Apache-2.0, published `2026-10-20`, 22 days old); native .NET 10 `ActivitySource`s (`Microsoft.AspNetCore`, `System.Net.Http`) and `Meter`s (`Microsoft.AspNetCore.Hosting`, `Microsoft.AspNetCore.Server.Kestrel`, `Microsoft.AspNetCore.Routing`, `Microsoft.AspNetCore.Diagnostics`, `Microsoft.AspNetCore.RateLimiting`, `System.Net.Http`, `System.Net.NameResolution`, `System.Runtime`); native `MongoDB.Driver` `3.13.0` `MongoTelemetry` (`ActivitySourceName = "MongoDB.Driver"`, `TracingOptions.QueryTextMaxLength = 0`).
- **.NET Tests (`tests/RoleRadar.Tests.*`)**: `OpenTelemetry.Exporter.InMemory` `1.20.1` (Apache-2.0, published `2026-10-20`) for in-process `Unit` and `Contract` tests; pinned official OpenTelemetry Collector container image `otel/opentelemetry-collector-contrib:0.164.0@sha256:340c9ca6a36617084c6661bdc4f0fe92b7cda5a805d61673ec8db375fddc522b` (Apache-2.0, published `2026-10-27`, 15 days old) with its built-in `otlp` receiver and `file` exporter in the existing `real-stack` lane (`tests/RoleRadar.Tests.Integration/Containers/RealStackEndToEndTests.cs`, run via `scripts/test-e2e-real.ps1`, never the dev stack).
- **.NET Local Orchestration (`src/RoleRadar.AppHost`, Gate 5 if Option 2 confirmed in Gate 1)**: `Aspire.Hosting.AppHost` `13.6.4`, `Aspire.Hosting.MongoDB` `13.6.4`, `Aspire.Hosting.JavaScript` `13.6.4` (all MIT, published `2026-10-24`, 18 days old) and container image `mcr.microsoft.com/dotnet/aspire-dashboard:13.6.4@sha256:23411f715be2476da0b2c0ad110039237de657c6cae31bf5c587d12a26c11230`.
- **Web (`web/`)**: Existing `@opentelemetry/api` `1.10.0` and `@opentelemetry/core` `3.0.0` plus `@opentelemetry/context-async-hooks` `3.0.0`, `@opentelemetry/resources` `3.0.0`, `@opentelemetry/sdk-trace` `3.0.0`, `@opentelemetry/sdk-logs` `3.0.0`, `@opentelemetry/sdk-metrics` `3.0.0`, `@opentelemetry/exporter-trace-otlp-http` `0.300.0`, `@opentelemetry/exporter-logs-otlp-http` `0.300.0`, `@opentelemetry/exporter-metrics-otlp-http` `0.300.0` (all Apache-2.0, published `2026-10-20`, 22 days old), and `@opentelemetry/semantic-conventions` `1.44.0` (Apache-2.0, published `2026-10-28`, 14 days old).

**Storage**: MongoDB `9.0.4` replica set (`rs0`) for domain/audit store; SQLite (`Microsoft.EntityFrameworkCore.Sqlite` `10.0.14`) for Identity operational store. No schema changes.

**Testing**: xUnit v3 (`4.0.2`), NetArchTest.Rules (`1.3.2`), Testcontainers.MongoDb (`4.16.0`), Vitest (`5.1.0`), StrykerJS (`10.1.1`), Playwright (`1.64.1`). No new test projects and no new heavy CI lanes.

**Target Platform**: Local-first developer workstations (Windows 11 / Linux / macOS) and Linux containers (`mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled`, `node:22-alpine`).

**Project Type**: Multi-service local-first web application, REST/OData API, OIDC Identity host, CLI, stdio MCP server, and local Aspire AppHost orchestrator.

**Performance Goals**: Zero measurable latency overhead when `OTEL_EXPORTER_OTLP_ENDPOINT` is unset (no exporter registered); bounded CLI/MCP telemetry flush (`<= 2000` ms) when explicitly opted in; CI runtime unchanged (no AppHost container startup in CI; fast architecture drift check runs in `< 1` s).

**Constraints**:
- Nothing leaves the machine by default (`SC-003`): no OTLP exporter registered when `OTEL_EXPORTER_OTLP_ENDPOINT` is unset; Aspire Dashboard binds exclusively to `127.0.0.1`.
- All adopted package releases must be at least 14 days old (published on or before `2026-10-28`), pinned to exact versions, with container images pinned by tag and SHA-256 digest.
- `.github/dependabot.yml` must not be edited in a feature PR.
- Every file written must use LF line endings; gate runs on Windows must execute from Git Bash (`RR-85`).

**Scale/Scope**: Single-owner local-first tracker across 5 runtime hosts (`web`, `api`, `identity`, `cli`, `mcp`) plus the local dev orchestrator (`RoleRadar.AppHost` or compose overlay), delivered in 5 gated PRs.

## Constitution Check

*GATE: Passed before Phase 0 research and re-verified after Phase 1 design.*

| Principle | Status | Evidence |
|-----------|--------|----------|
| I. Privacy First | PASS | `RR-72` Telemetry Privacy Gate ships with the first exporter in Gate 2 (and extends in Gates 3 and 4), verifying 0 sentinel secrets or untrusted domain text (`content`, `ruleText`, `$filter` literals, `db.query.text`) in exported spans, logs, or metrics. Nothing leaves the machine by default (`SC-003`), Dashboard binds to `127.0.0.1`, AppHost uses throwaway volumes only, and spikes use fictional data in a scratch directory. |
| II. Test-First Development | PASS | Every gate writes failing unit/contract/architecture tests first. Privacy gate tests use in-memory/loopback test receivers and isolated containers, never touching the developer's running stack. |
| III. Test-Enforced Layering | PASS | `ObservabilityArchitectureTests` and `LayerDependencyArchitectureTests` enforce that `RoleRadar.Observability` remains free of `RoleRadar.*`, `Microsoft.AspNetCore.*`, `MongoDB.*`, and `Duende.*` references, and that `RoleRadar.AppHost` may reference host projects while nothing in `src/` references `RoleRadar.AppHost`. |
| IV. Named Seams Only | PASS | No generic repository, mediator, or event bus is introduced. Shared `.NET` telemetry setup lives in the existing `RoleRadar.Observability` project (ADR 0041). |
| V. Agent Safety & Auditability | PASS | `MongoClientSettings.TracingOptions.QueryTextMaxLength` is pinned to `0`, the Gate 2 span processor (`ADR 0050`) enforces an attribute allowlist/drop-list (removing `url.query`, query parts of `url.full`, request/response headers, `db.query.text`, and raw exception messages/stack traces without depending on `LogScrubber`), and Gate 3 (`ADR 0051`) governs log scrubbing; `request_correlation_id` audit behavior from Features 013/014 is preserved unchanged. |
| VI. stdio Protocol Hygiene | PASS | `RoleRadar.Mcp.Stdio` registers no OTLP exporter by default, never binds a network port, and routes all logging and any opt-in telemetry diagnostics strictly off `stdout`. |
| VII. Data Durability & Safe Recovery | PASS | The AppHost never mounts `roleradar-mongo-data` or `roleradar-identity-data` (`AppHostComposeParityArchitectureTests` enforces this in CI). |
| VIII. Recorded Decisions & Pinned Dependencies | PASS | Gate 1 records `ADR 0049` (`RR-67`), Gate 2 records `ADR 0050` (span allowlist/drop-list processor), and Gate 3 records `ADR 0051` (`RR-86`). Every NuGet package (`1.20.1`, `13.6.4`), npm package (`3.0.0`, `0.300.0`, `0.34.0`, `1.44.0`), and container image (`mcr.microsoft.com/dotnet/aspire-dashboard:13.6.4@sha256:23411f715be2476da0b2c0ad110039237de657c6cae31bf5c587d12a26c11230`) is MIT/Apache-2.0, `>= 14` days old, and pinned to exact versions/digests. |
| IX. Baseline Technical Conventions | PASS | Targets .NET 10 with `<Nullable>enable</Nullable>` and `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, UTC timestamps, and LF line endings. |
| X. Standards First | PASS | Every mechanism is evaluated in the "Standards considered" table below; native platform/driver capabilities (.NET 10 `ActivitySource`/`Meter`, `MongoDB.Driver` 3.13.0 `MongoTelemetry`, OpenTelemetry SDKs, and .NET Aspire) are used throughout, and the web's manual `trace-context.server.ts` ID/span wiring is replaced by the OpenTelemetry JS SDK so only one mechanism exists. |

## Standards considered

| Mechanism | What was checked | What was chosen | Why (ADR if hand-rolled) |
|-----------|------------------|-----------------|--------------------------|
| Telemetry wire protocol & export format (traces, logs, metrics) | CNCF OpenTelemetry Protocol (OTLP), vendor-specific protocols (Seq CLEF, Jaeger native, Loki/Prometheus push), custom HTTP JSON sinks | OpenTelemetry OTLP (`OpenTelemetry.Exporter.OpenTelemetryProtocol` `1.20.1` in .NET; `@opentelemetry/exporter-*-otlp-http` `0.300.0` in Node) | Vendor-neutral CNCF standard (`FR-001`); switching to a hosted backend later requires only environment variable changes. |
| .NET HTTP, ASP.NET Core & Runtime tracing and metrics | .NET 10 built-in `ActivitySource`s (`Microsoft.AspNetCore`, `System.Net.Http`) and `Meter`s (`Microsoft.AspNetCore.Hosting`, `Microsoft.AspNetCore.Server.Kestrel`, `Microsoft.AspNetCore.Routing`, `Microsoft.AspNetCore.Diagnostics`, `Microsoft.AspNetCore.RateLimiting`, `System.Net.Http`, `System.Net.NameResolution`, `System.Runtime`) + `OpenTelemetry.Extensions.Hosting` `1.20.1`; `OpenTelemetry.Instrumentation.AspNetCore` / `Http` / `Runtime` `1.20.1` | .NET 10 native `ActivitySource`s and `Meter`s wired once in `RoleRadar.Observability` via `OpenTelemetry.Extensions.Hosting` `1.20.1` | .NET 10 BCL and ASP.NET Core natively emit OpenTelemetry semantic conventions; subscribing by source/meter name in `RoleRadar.Observability` shares one setup across `Api`, `Identity`, `Cli`, and `Mcp.Stdio` without adding a `Microsoft.AspNetCore.App` framework reference to `RoleRadar.Observability` (`ObservabilityArchitectureTests`). |
| .NET ASP.NET Core HTTP span instrumentation (`RR-90`, Gate 2b) | Hand-set span tags in `RequestCompletedMiddleware` vs. `OpenTelemetry.Instrumentation.AspNetCore` `1.20.1` | `OpenTelemetry.Instrumentation.AspNetCore` `1.20.1` in `RoleRadar.Api` and `RoleRadar.Identity` with `RecordException = false` | Maintained library first (Principle X, ADR 0050 addendum). Bare .NET 10 ASP.NET Core spans lack HTTP semantic tags; the official package populates standard tags natively while `RoleRadarSpanAttributeProcessor` strips query, user agent, and client address tags. |
| MongoDB command tracing (.NET) | `MongoDB.Driver` `3.13.0` built-in `MongoTelemetry` (`ActivitySourceName = "MongoDB.Driver"`, `MongoClientSettings.TracingOptions` with `Disabled = false`, `QueryTextMaxLength = 0`); third-party `MongoDB.Driver.Core.Extensions.DiagnosticSources` `3.0.0` | Native `MongoDB.Driver` `3.13.0` `ActivitySource("MongoDB.Driver")` with `TracingOptions.QueryTextMaxLength = 0` | Built into the already-pinned `MongoDB.Driver` `3.13.0`; omits `db.query.text` by default (preventing BSON document body and filter literal leaks) and requires zero additional NuGet packages (`R-001`). |
| Web trace context propagation & span lifecycle (Node 22) | `@opentelemetry/sdk-trace` `3.0.0`, `@opentelemetry/context-async-hooks` `3.0.0`, `@opentelemetry/core` `3.0.0` (`W3CTraceContextPropagator`), `@opentelemetry/instrumentation-http` `0.300.0`, `@opentelemetry/instrumentation-undici` `0.34.0` vs. Feature 014's manual `trace-context.server.ts` (`crypto.randomBytes` IDs and manual `trace.setSpanContext`) | OpenTelemetry JS SDK (`@opentelemetry/sdk-trace` `3.0.0`, `AsyncLocalStorageContextManager`, global `W3CTraceContextPropagator`) replacing the manual ID generator and manual span context wiring in `trace-context.server.ts` | One mechanism, not two (`R-003`, ADR 0044): the SDK's active span and global propagator replace the manual 014 bridge, keeping only the 5-line strict 55-char lowercase edge guard in front of `propagation.extract`. Note: `instrumentation-http` and `instrumentation-undici` were pinned in Gate 2, never imported, and removed in Gate 4 (ADR 0052). |
| Structured log export & runtime log scrubbing (`RR-70`, `RR-86`) | OpenTelemetry `ILogger` integration (`ILoggingBuilder.AddOpenTelemetry` in `OpenTelemetry` `1.20.1`), `BaseProcessor<LogRecord>`, `@opentelemetry/sdk-logs` `3.0.0`, `Microsoft.Extensions.Compliance.Redaction` `10.11.0` (rejected in ADR 0046), Serilog `4.4.0` (rejected in ADR 0047), `LogScrubber` (ADR 0046) | `ILoggingBuilder.AddOpenTelemetry` (`1.20.1`) and `@opentelemetry/sdk-logs` `3.0.0`, with the exact scrubbing/processor/formatter disposition decided by evidence in Gate 3 (`ADR 0051`) | All hosts log through `ILogger`; `ADR 0051` (`RR-86`) evaluates producer evidence for all 7 rules, `LogRecord` exception scrubbing, and OTLP log model vs. the 013 console record shape (`R-004`). |
| Span attribute sanitization & exception-event policy (`RR-68`, `RR-72`, Gate 2) | External OTel Collector `transform`/`redaction` processor; regex text scrubbing via `LogScrubber`; in-process OpenTelemetry `BaseProcessor<Activity>` (.NET) and `SpanProcessor` (Node) enforcing an attribute allowlist + drop-list (`RoleRadarSpanAttributeProcessor` / `WebSpanAttributeProcessor`) | In-process allowlist and drop-list span processor (`RoleRadarSpanAttributeProcessor` and `WebSpanAttributeProcessor`) independent of `LogScrubber` (`ADR 0050`) | Keeps Gate 2 completely independent of `RR-86`'s outcome in Gate 3; drops `url.query`, strips query/fragment from `url.full`, drops request/response headers and `db.query.text`, drops non-allowlisted tags, and sanitizes span `exception` events by keeping only `exception.type` (and `error.type`), dropping `exception.message` and `exception.stacktrace`, and clearing free-text `StatusDescription` (`ADR 0050`). |
| Local developer orchestrator & telemetry dashboard (`RR-67`, `RR-71`) | .NET Aspire (`Aspire.Hosting.AppHost` `13.6.4`, `Aspire.Hosting.MongoDB` `13.6.4`, `Aspire.Hosting.JavaScript` `13.6.4`) + Aspire Dashboard (`mcr.microsoft.com/dotnet/aspire-dashboard:13.6.4@sha256:23411f715be2476da0b2c0ad110039237de657c6cae31bf5c587d12a26c11230`), standalone Aspire Dashboard compose overlay, Seq, Jaeger, Grafana LGTM | Aspire AppHost + Aspire Dashboard (Option 2), or standalone Aspire Dashboard in an opt-in compose overlay if Gate 1 spike finds a replica-set Mongo or Identity issuer/TLS blocker (`ADR 0049`) | MIT-licensed, single-command dev loop and unified OTLP traces/logs/metrics/resource-graph viewer on `127.0.0.1` (`R-005`, `ADR 0049`). |
| Real-stack E2E OTLP receiver (`SC-001` in `RealStackEndToEndTests`, `Lane=real-stack`) | Official CNCF OpenTelemetry Collector container (`otel/opentelemetry-collector-contrib:0.164.0@sha256:340c9ca6a36617084c6661bdc4f0fe92b7cda5a805d61673ec8db375fddc522b` with built-in `otlp` receiver and `file` exporter); hand-rolled Kestrel/Node OTLP HTTP/protobuf test receiver | Official OpenTelemetry Collector container (`otel/opentelemetry-collector-contrib:0.164.0@sha256:340c9ca6a36617084c6661bdc4f0fe92b7cda5a805d61673ec8db375fddc522b`, Apache-2.0, `>= 14` days old) attached to the existing `RealStackEndToEndTests` throwaway Docker network (`rrtest-real-net-<runId>`) and writing OTLP JSON lines (`otlp.jsonl`) to a temp mount | Standards first (Principle X): uses the official CNCF Collector's `otlp` receiver (`4317`/`4318`) and `file` exporter instead of hand-rolling a custom OTLP protobuf/JSON server, runs inside the existing `Lane=real-stack` (`scripts/test-e2e-real.ps1`, never the dev stack) so **no new heavy CI lane is added**, and works identically across Linux CI and Windows bridge networks. |
| Compose vs. AppHost drift check (`SC-007`) | xUnit architecture test (`AppHostComposeParityArchitectureTests` in `RoleRadar.Tests.Architecture`) vs. custom PowerShell or Node script | xUnit test in `tests/RoleRadar.Tests.Architecture` parsing `deploy/docker-compose*.yml` and the AppHost/overlay configuration | Runs automatically in `test-fast.ps1` and CI with no new test project and no custom script (`AGENTS.md`, `R-006`). |
| Datastore health and command metrics (.NET, Gate 4) | `Microsoft.Extensions.Diagnostics.HealthChecks`, `MongoDB.Driver` 3.13.0, `Microsoft.Data.Sqlite` native meters vs. hand-rolled `RoleRadar.Store` meter | Hand-rolled `RoleRadar.Store` meter (`RoleRadarStoreMetrics`) in `RoleRadar.Domain.Common` | Checked .NET 10 HealthChecks, MongoDB 3.13.0, and SQLite; none emit native `Meter` or metric instruments. Small hand-rolled meter defined in `Domain` so both `Infrastructure` and `Identity` reference it directly without violating `InnerLayers_DoNotReferenceObservability` ([ADR 0052](../../docs/adr/0052-store-health-and-operation-metrics-meter.md)). |
| Metric label allowlist & cardinality protection (Gate 4) | Custom metric processor vs. standard OpenTelemetry Views (`MetricStreamConfiguration.TagKeys` in .NET, `ViewOptions` with `createAllowListAttributesProcessor` in JS SDK) | OpenTelemetry Views with strict `TagKeys` / `attributeKeys` allowlists | OpenTelemetry specification standard (Principle X); metrics SDK aggregates data points at collection time and drops unlisted tags (including `owner_id`, entity ids, query params, trace ids) ([ADR 0052](../../docs/adr/0052-store-health-and-operation-metrics-meter.md)). |
| Web HTTP RED & runtime metrics (Node 22, Gate 4) | `@opentelemetry/instrumentation-http` `0.300.0` & `@opentelemetry/instrumentation-undici` `0.34.0`, `@opentelemetry/instrumentation-runtime-node` vs. explicit semantic-convention recording on pre-instantiated instruments and Node runtime observable gauges | Explicit recording with route pattern resolution via `matchRoutes` + Node runtime observable gauges (`nodejs.eventloop.utilization`) | Auto-instrumentation packages evaluated and rejected: `server.ts` already runs custom request handler with edge server span; auto-instruments create duplicate spans and cannot resolve React Router routes. Runtime package rejected (0.38.0 published 2026-11-05 is under the 14-day rule until 2026-11-19; hand-written gauges until then, revisit). Hand-rolled observable gauges use standard conventions cleanly ([ADR 0052](../../docs/adr/0052-store-health-and-operation-metrics-meter.md)). |

## Project Structure

### Documentation (this feature)

```text
specs/015-observability/
├── plan.md                          # This file (/speckit-plan command output)
├── research.md                      # Phase 0 output (/speckit-plan command)
├── data-model.md                    # Phase 1 output (/speckit-plan command)
├── quickstart.md                    # Phase 1 output (/speckit-plan command)
├── contracts/
│   ├── telemetry-signals.md         # OTLP traces, logs, metrics, and CLI/MCP opt-in contracts
│   ├── privacy-gate.md              # RR-72 sentinel corpus and processor redaction contract
│   └── local-dev-loop.md            # RR-67 spike matrix, ADR 0049 rules, and RR-71 drift contract
└── tasks.md                         # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)

docs/adr/
├── 0049-local-observability-apphost-and-dashboard.md   # Gate 1 output (RR-67, docs-only PR)
├── 0050-span-attribute-allowlist-processor.md          # Gate 2 output (RR-68 + RR-72 span allowlist/drop-list & exception policy)
├── 0051-otel-logs-and-runtime-scrubbing.md             # Gate 3 output (RR-86)
└── 0052-store-health-and-operation-metrics-meter.md    # Gate 4 output (RR-69 & RR-72)
```

### Source Code (repository root)

```text
src/
├── RoleRadar.Domain/
│   └── Common/
│       └── RoleRadarStoreMetrics.cs        # Meter("RoleRadar.Store") for datastore command & readiness health (Gate 4)
├── RoleRadar.Application/           # Unchanged
├── RoleRadar.Infrastructure/
│   └── Mongo/
│       ├── ConnectionStringRedactor.cs     # Explicit TracingOptions { Disabled = false, QueryTextMaxLength = 0 }
│       └── MongoReadinessProbe.cs          # Emits RoleRadar.Store readiness duration/status metrics (Gate 4)
├── RoleRadar.Observability/
│   ├── RoleRadar.Observability.csproj      # Adds OpenTelemetry, OpenTelemetry.Extensions.Hosting, OpenTelemetry.Exporter.OpenTelemetryProtocol (1.20.1)
│   ├── LoggingSetup.cs                     # Bridges ILoggingBuilder.AddOpenTelemetry when OTLP endpoint configured (Gate 3)
│   ├── LogScrubber.cs                      # Evaluated in Gate 3 (RR-86 / ADR 0051); NOT referenced by Gate 2 span processor
│   ├── TelemetrySetup.cs                   # Shared AddRoleRadarTelemetry for Api, Identity, Cli, Mcp.Stdio (Gates 2-4)
│   ├── RoleRadarSpanAttributeProcessor.cs  # Allowlist & drop-list span processor independent of LogScrubber (Gate 2, ADR 0050)
│   └── RoleRadarLogScrubbingProcessor.cs   # OTLP LogRecord processor per Gate 3 decision (ADR 0051)
├── RoleRadar.Api/
│   └── Program.cs                          # Calls builder.Services.AddRoleRadarTelemetry("roleradar-api", ...)
├── RoleRadar.Identity/
│   └── Program.cs                          # Calls builder.Services.AddRoleRadarTelemetry("roleradar-identity", ...); records SQLite readiness metrics
├── RoleRadar.Cli/
│   └── Program.cs                          # Default-off telemetry; opt-in bounded flush when OTEL_EXPORTER_OTLP_ENDPOINT is set
├── RoleRadar.Mcp.Stdio/
│   └── Program.cs                          # Default-off telemetry; opt-in bounded flush, strictly 0 bytes on stdout
└── RoleRadar.AppHost/                      # Gate 5 (if Option 2 confirmed by ADR 0049; otherwise deploy/docker-compose.telemetry.yml)
    ├── RoleRadar.AppHost.csproj
    └── Program.cs

web/
├── package.json                            # Adds pinned @opentelemetry/* 3.0.0 / 0.300.0 / 0.34.0 / 1.44.0 packages
├── server.ts                               # Uses active OTel server span and records web HTTP/runtime metrics
├── app/lib/
│   ├── telemetry.server.ts                 # Shared web OTel SDK setup, span/log scrubbing processors, and metrics (Gates 2-4)
│   ├── trace-context.server.ts             # Simplified to use global W3CTraceContextPropagator + active span (one mechanism)
│   ├── log.server.ts                       # Emits OTLP log records alongside console when endpoint configured (Gate 3)
│   └── observability.ts                    # Shared scrubbing and pure helpers
└── tests/unit/
    ├── telemetry-tracing.test.ts           # Gate 2 web tracing & no-endpoint tests
    ├── telemetry-privacy.test.ts           # Gates 2-4 RR-72 web privacy gate (spans, logs, metric labels)
    ├── telemetry-logs.test.ts              # Gate 3 web OTLP log correlation & scrubbing tests
    └── telemetry-metrics.test.ts           # Gate 4 web RED & runtime metric tests

tests/
├── RoleRadar.Tests.Architecture/                   # Fast reflection & file-parsing tests (no Docker)
│   ├── LayerDependencyArchitectureTests.cs         # Updated for RoleRadar.AppHost layering rules (Gate 5)
│   ├── ObservabilityArchitectureTests.cs           # Enforces Observability & AppHost reference boundaries
│   └── AppHostComposeParityArchitectureTests.cs    # Gate 5 drift check comparing deploy/docker-compose*.yml and AppHost/overlay
├── RoleRadar.Tests.Unit/                           # Pure in-process unit tests with OpenTelemetry.Exporter.InMemory (no Docker)
│   └── Observability/
│       ├── TelemetrySetupTests.cs                  # SC-003 no-endpoint = 0 exporters, CLI/MCP default-off & bounded flush
│       ├── TelemetryPrivacyGateTests.cs            # RR-72 sentinel redaction across spans, logs, and metrics (Gates 2-4)
│       └── StoreMetricsTests.cs                    # Gate 4 store command & readiness metric unit tests
├── RoleRadar.Tests.Contract/                       # In-process HTTP & SQLite contract tests (no Docker / no Testcontainers)
│   └── Observability/
│       ├── DistributedTracingContractTests.cs      # Gate 2 in-process traceparent across Api (non-Mongo) and Identity (SQLite)
│       ├── OtlpLogCorrelationContractTests.cs      # Gate 3 in-process OTLP log export, traceId/spanId, and exception scrubbing
│       └── ServiceMetricsContractTests.cs          # Gate 4 in-process HTTP RED, Identity SQLite readiness, and label cardinality
└── RoleRadar.Tests.Integration/                    # Docker/Testcontainers & container lanes (explicit [Trait("Lane", "...")] per ADR 0036)
    ├── Observability/
    │   ├── MongoTelemetryIntegrationTests.cs       # [Trait("Lane", "default")] — Testcontainers MongoDB (rs0) spans, QueryTextMaxLength=0 (Gate 2) & store metrics (Gate 4)
    │   └── CliMcpTelemetryIntegrationTests.cs      # [Trait("Lane", "images")] — LaunchedCliRunner & McpProcessClient default-off, bounded flush & 0-byte stdout (Gates 2-3)
    └── Containers/
        └── RealStackEndToEndTests.cs               # [Trait("Lane", "real-stack")] — SC-001 real-stack OTLP Collector proof across web, API & Identity (Gate 2, scripts/test-e2e-real.ps1)
```

**Structure Decision**: Shared `.NET` OpenTelemetry wiring, scrubbing processors, and datastore health metric definitions live in `src/RoleRadar.Observability` so `RoleRadar.Api`, `RoleRadar.Identity`, `RoleRadar.Cli`, and `RoleRadar.Mcp.Stdio` call a single registration method (`AddRoleRadarTelemetry`) without duplicating code or violating inward layering. Web telemetry lives in `web/app/lib/telemetry.server.ts`, unifying `trace-context.server.ts` onto the OpenTelemetry JS SDK. All new automated tests fit inside the existing `Architecture`, `Unit`, `Contract`, `Integration` (with explicit `[Trait("Lane", "default" | "images" | "real-stack")]` per `TestLaneCoverageTests` and `ADR 0036`), and `web/tests/unit` suites (`AGENTS.md`: "Do not add test projects").

## Gate-by-Gate Delivery Strategy

Each gate is a self-contained pull request that must pass `bash scripts/linux-web-check.sh`, `pwsh ./scripts/test-fast.ps1`, `pre-commit run --all-files`, and `pwsh ./scripts/review-gate.ps1` (run from Git Bash per `RR-85`) before stopping at the gate boundary:

1. **Gate 1 (`P1`, `RR-67` — Docs Only)**:
   - Execute the scratch spike outside the repository using fictional data and throwaway containers (`roleradar-spike-*`).
   - Author `docs/adr/0049-local-observability-apphost-and-dashboard.md` and update `docs/adr/README.md`, recording all required checks (Mongo replica set `rs0` transactions, Identity issuer/TLS parity, drift check design, CLI/MCP policy, built-in dashboard view confirmation, Option 3 deferral, and either confirming Option 2 AppHost or triggering the standalone Aspire Dashboard compose overlay fallback).
2. **Gate 2 (`P2`, `RR-68` + `RR-72` — Tracing & Privacy Gate, `ADR 0050`)**:
   - **Independence rule**: Gate 2 uses a `LogScrubber`-independent allowlist and drop-list span processor (`RoleRadarSpanAttributeProcessor` in `.NET` and `WebSpanAttributeProcessor` in `web/`), recorded in `docs/adr/0050-span-attribute-allowlist-processor.md`. Gate 2 stands completely independent of `RR-86`'s outcome in Gate 3.
   - **Task-count split rule**: If Gate 2's tasks in `tasks.md` exceed ~45 tasks, split Gate 2 into two sequential PR gates: **Gate 2a (`.NET` first: `RoleRadar.Observability`, `Infrastructure`, `Api`, `Identity`, `Cli`, `Mcp.Stdio`, `.NET` privacy gate tests, and `ADR 0050`)** and **Gate 2b (`web` second: `web/app/lib/telemetry.server.ts`, `trace-context.server.ts`, `server.ts`, and `web` privacy gate tests)**.
   - Write failing unit, contract (no Docker), and integration tests (`[Trait("Lane", "default")]` for Testcontainers MongoDB spans, `[Trait("Lane", "images")]` for launched `CLI`/`Mcp.Stdio`, and `[Trait("Lane", "real-stack")]` in `RealStackEndToEndTests.cs` via `scripts/test-e2e-real.ps1` with the pinned `otel/opentelemetry-collector-contrib:0.164.0@sha256:340c9ca6a36617084c6661bdc4f0fe92b7cda5a805d61673ec8db375fddc522b` receiver) for `SC-001` (connected trace ID across web, API, Identity, and MongoDB), `SC-002` (`RR-72` span privacy gate: removing `url.query`, query/fragment parts of `url.full`, request/response headers, `db.query.text`, non-allowlisted attributes, and raw `exception.message`/`exception.stacktrace` on span events while preserving `exception.type`/`error.type`), `SC-003` (0 exporters registered when `OTEL_EXPORTER_OTLP_ENDPOINT` is unset), and `FR-017` (`CLI`/`Mcp.Stdio` default-off and bounded flush).
   - Implement `TelemetrySetup` and `RoleRadarSpanAttributeProcessor` in `RoleRadar.Observability`, set explicit `TracingOptions { Disabled = false, QueryTextMaxLength = 0 }` in `ConnectionStringRedactor.CreateClient`, wire `Api`, `Identity`, `Cli`, and `Mcp.Stdio`, author `ADR 0050`, implement `web/app/lib/telemetry.server.ts` while simplifying `trace-context.server.ts` onto the OpenTelemetry SDK propagator and active span, and verify the real-stack OTLP Collector assertion via `pwsh ./scripts/test-e2e-real.ps1` (never touching the dev stack).
3. **Gate 3 (`P3`, `RR-70` + `RR-86` + `RR-72` — OTLP Logs & Scrubbing Decision, `ADR 0051`)**:
   - Execute the `RR-86` producer census and `BaseProcessor<LogRecord>` exception-scrubbing spike, recording `docs/adr/0051-otel-logs-and-runtime-scrubbing.md` and updating `docs/adr/README.md` (with conditional reopening of `ADR 0047` if `LogScrubber` is dropped or moved into a log processor).
   - Write failing unit, contract (no Docker), and `Lane=images` integration tests for OTLP log export, `TraceId`/`SpanId` correlation, and `RR-72` log sentinel redaction (including exception messages).
   - Wire OTLP log export in `RoleRadar.Observability` (`LoggingSetup` / `TelemetrySetup` / `RoleRadarLogScrubbingProcessor`) and `web/app/lib/log.server.ts` + `telemetry.server.ts`.
4. **Gate 4 (`P4`, `RR-69` + `RR-72` — Service & Store Metrics)**:
   - Write failing unit, contract (no Docker), and `Lane=default` Testcontainers integration tests (`MongoTelemetryIntegrationTests.cs`) for HTTP RED metrics, runtime metrics, `RoleRadar.Store` readiness and command duration metrics, and the `RR-72` metric label cardinality/privacy gate (`SC-005`).
   - Wire meter subscriptions and `RoleRadarStoreMetrics` in `RoleRadar.Observability`, `MongoReadinessProbe`, `IdentityReadinessCheck`, and `web/app/lib/telemetry.server.ts`.
5. **Gate 5 (`P5`, `RR-71` — Local Dev Loop & Drift Guard)**:
   - Write failing architecture and drift tests in `tests/RoleRadar.Tests.Architecture` (`AppHostComposeParityArchitectureTests`, `LayerDependencyArchitectureTests`, `ObservabilityArchitectureTests`).
   - Implement `src/RoleRadar.AppHost` (or `deploy/docker-compose.telemetry.yml` if `ADR 0049` triggered the fallback), bind the Aspire Dashboard to `127.0.0.1`, enforce the fail-closed runtime safety guard and throwaway volumes, and update documentation.
   - **Manual Dashboard Verification (`SC-006`)**: Because the Aspire Dashboard (`mcr.microsoft.com/dotnet/aspire-dashboard:13.6.4@sha256:23411f715be2476da0b2c0ad110039237de657c6cae31bf5c587d12a26c11230`) provides no public query API to automate UI view assertions (and OTLP wire emission is already proven automatically by the OpenTelemetry Collector in `RealStackEndToEndTests.cs`), verifying the Dashboard's four built-in views (Resources, Traces, Structured Logs, Metrics) is an explicit manual check on the isolated local stack with strictly fictional data, recording structured verification evidence (bound `127.0.0.1` ports, observed 32-hex `traceId` across `web`->`api`->`mongodb` and `web`->`identity`, correlated redacted log entries, rendered metric instruments, and untouched `roleradar-mongo-data` / `roleradar-identity-data` volumes).

## Follow-ups outside the feature PRs

- **Dependabot grouping PR**: Do not edit `.github/dependabot.yml` inside any Feature 015 PR. After the first exporter (Gate 2) merges to `main`, open a separate small PR to group `@opentelemetry/*` (npm) and `OpenTelemetry*` (NuGet) packages in `.github/dependabot.yml` so lockstep OpenTelemetry releases update together. Expect one extra Dependabot run immediately after that PR merges.

## Complexity Tracking

No Constitution violations exist. If Gate 1 confirms Option 2, `src/RoleRadar.AppHost` is added as the local source orchestrator project:

| Addition | Why Needed | Simpler Alternative Rejected Because |
|----------|------------|-------------------------------------|
| `src/RoleRadar.AppHost` (Option 2, subject to Gate 1 `ADR 0049`) | Owner-selected local setup (`FR-010`, `FR-012`) to run the dev loop from source and launch the Aspire Dashboard with a single command while keeping `deploy/docker-compose*.yml` as the packaged stack. | Replacing Docker Compose (Option 3) was deferred until hosting is un-parked; if Gate 1 shows a replica-set or TLS blocker, Gate 5 falls back to an opt-in compose overlay with zero new `.csproj`. |
