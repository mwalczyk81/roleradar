# ADR 0052: Datastore Readiness, Command Health Metrics, and Metric View Tag Filtering

- **Status**: Accepted
- **Date**: 2026-11-12
- **Feature**: `015-observability` (Gate 4, Jira RR-69 & RR-72)

## Context and Problem Statement

Feature 015 Gate 4 (`RR-69` and `RR-72`) adds HTTP RED metrics, runtime metrics, and datastore health/operation metrics exported over OTLP across `roleradar-web`, `roleradar-api`, and `roleradar-identity` (with opted-in metrics support in `roleradar-cli` and `roleradar-mcp`). Under Constitution Principle X (Standards First), we evaluate native platform meters and driver metrics before introducing any hand-rolled instrumentation. Under Constitution Principle I (Privacy First) and Principle V (Untrusted Input), metric labels must never leak high-cardinality or sensitive identifiers (`owner_id`, entity GUIDs like `posting_id`, `company_id`, `application_id`, `note_id`, `traceId`, `sub`, `username`, raw `url.full` / `url.query`, query strings, or free-text search strings).

We require metrics for:

1. HTTP server RED metrics (`http.server.request.duration`, `http.server.active_requests`) and client request duration (`http.client.request.duration`).
2. Runtime health metrics (process memory, CPU, GC, event loop utilization).
3. Datastore readiness status, probe duration, and command operation duration for MongoDB replica set (`rs0`) and Identity SQLite.
4. Metric label cardinality and privacy filtering.

## Standards and Options Considered

### 1. HTTP Server, Client, and Runtime Metrics (.NET)

| Candidate | Mechanism | Assessment |
| --- | --- | --- |
| **Native .NET 10 Meters (`Microsoft.AspNetCore.Hosting`, `Microsoft.AspNetCore.Server.Kestrel`, `Microsoft.AspNetCore.Routing`, `System.Net.Http`, `System.Runtime`)** | Built into .NET 10 BCL and ASP.NET Core runtime | **Selected.** Empirical investigation confirmed `Microsoft.AspNetCore.Hosting` emits `http.server.request.duration` and `http.server.active_requests` with native route template tags (`http.route`), `System.Net.Http` emits `http.client.request.duration`, and `System.Runtime` emits runtime counters. Subscribed by meter name in `TelemetrySetup.cs` with 0 custom instruments. |
| **Custom HTTP metrics middleware** | Hand-rolled middleware with custom `Meter` | **Rejected.** Redundant with native .NET 10 meters (Principle X). |

### 2. Datastore Readiness and Operation Metrics (.NET)

| Candidate | Native Meter Available? | Assessment |
| --- | --- | --- |
| **`Microsoft.Extensions.Diagnostics.HealthChecks`** | No | Checked .NET 10 BCL. `HealthCheckService` executes registered probes but exposes 0 `Meter` instances or metric instruments. |
| **`MongoDB.Driver` 3.13.0** | No | Checked driver assemblies and runtime diagnostics. Provides `MongoTelemetry` (`ActivitySource("MongoDB.Driver")`) for tracing, but emits 0 `Meter` instances or metric instruments for command duration or health. |
| **`Microsoft.Data.Sqlite` / EF Core SQLite** | No | Emits no metric meters for connection readiness or query duration. |
| **Custom `RoleRadar.Store` Meter (`RoleRadarStoreMetrics`)** | Yes (Hand-rolled) | **Selected.** Implemented in `RoleRadar.Domain.Common` as a single static `Meter("RoleRadar.Store")` emitting `roleradar.store.operation.duration` (Histogram, seconds), `roleradar.store.readiness.status` (ObservableGauge, 1 = healthy, 0 = unhealthy), and `roleradar.store.readiness.duration` (Histogram, seconds). Defined in `RoleRadar.Domain.Common` because `Domain` is the innermost layer with 0 dependencies. Both `RoleRadar.Infrastructure` and `RoleRadar.Identity` reference `RoleRadar.Domain` (with `RoleRadar.Identity` gaining a project reference to `RoleRadar.Domain`), allowing both datastores to record metrics without referencing each other or `RoleRadar.Observability`, preserving `InnerLayers_DoNotReferenceObservability`. |

Because none of the underlying datastore libraries or health check packages emit native OpenTelemetry metrics, a minimal custom meter `RoleRadar.Store` is required. It records probe timings from `MongoReadinessProbe` and `IdentityReadinessCheck`, and command durations from MongoDB driver `CommandStartedEvent`, `CommandSucceededEvent`, and `CommandFailedEvent` subscribed via `MongoClientSettings.ClusterConfigurator` in `ConnectionStringRedactor.cs`.

### 3. MongoDB Command Duration Recording: Driver Events vs. Span Attribute Processor

| Candidate | Mechanism | Assessment |
| --- | --- | --- |
| **MongoDB.Driver Command Events (`ClusterConfigurator`)** | Subscribe to `CommandStartedEvent`, `CommandSucceededEvent`, `CommandFailedEvent` on `MongoClientSettings.ClusterConfigurator` | **Selected.** Measures operation duration directly at the driver transport layer with monotonic timers (`e.Duration`). The handler reads the first element of `CommandStartedEvent.Command` (`e.Command.GetElement(0)`) to get the collection name string; if it matches `KnownCollections`, the name is cached by `e.RequestId`. Nothing else from `e.Command` is read or stored (no query filters, no documents, no values). Error types come directly from `e.Failure?.GetType().Name`. Clean separation of concerns: processor handles tracing; driver configurator handles datastore metrics. |
| **Custom Span Processor Metric Recording (`RoleRadarSpanAttributeProcessor`)** | Record metrics in `OnEnd` callback of `RoleRadarSpanAttributeProcessor` | **Rejected.** Conflates tracing privacy gate with metric generation. Activity duration is dependent on span sampling and end lifecycle. Driver command events provide direct, exact execution duration and failure semantics without parsing span attributes or risking data leakage. |

### 4. Web Service & Client RED Metrics (Node 22)

| Candidate | Mechanism | Assessment |
| --- | --- | --- |
| **Auto-instrumentation (`@opentelemetry/instrumentation-http` 0.300.0 & `@opentelemetry/instrumentation-undici` 0.34.0)** | Official OpenTelemetry JS instrumentation libraries | **Evaluated and Rejected.** `web/server.ts` already runs a custom request handler (`createRequestHandler`) with an edge-level server span (`startServerRequestSpan`) enforcing strict W3C trace validation and AsyncLocalStorage propagation. Monkey-patching `http` creates duplicate server spans, misses React Router route patterns, and `undici` does not reliably hook `globalThis.fetch` in Node 22 without redundant tracing. The pins are not needed and were removed from `web/package.json`. |
| **Explicit Hand-Rolled RED Recording (`web/server.ts`, `api-client.server.ts`, `oidc.server.ts`)** | Explicit recordings on pre-instantiated histograms and UpDownCounters | **Selected.** Instruments (`http.server.request.duration`, `http.server.active_requests`, `http.client.request.duration`) are instantiated once during `initializeWebTelemetry()` on `DEFAULT_WEB_SERVICE_NAME` meter. `url.scheme` is derived directly from the socket (`(req.socket as { encrypted?: boolean })?.encrypted ? 'https' : 'http'`). Routes are resolved via `matchRoutes` against React Router's route configuration (`resolveHttpRoute`), clamping unmatched paths to `"unmatched"`. |

### 5. Node Runtime Metrics (Node 22)

| Candidate | Mechanism | Assessment |
| --- | --- | --- |
| **`@opentelemetry/instrumentation-runtime-node`** | OpenTelemetry JS contrib runtime package | **Evaluated and Rejected.** `pnpm view @opentelemetry/instrumentation-runtime-node@0.37.0 dependencies --json` yields `{"@opentelemetry/core": "^3.0.0", "@opentelemetry/instrumentation": "^0.300.0"}`. `pnpm view @opentelemetry/instrumentation-runtime-node@0.38.0 dependencies --json` yields `{"@opentelemetry/core": "^3.0.0", "@opentelemetry/instrumentation": "^0.301.0"}`. Version 0.38.0 was published 2026-11-05T21:35:55.930Z and is under the 14-day rule until 2026-11-19; hand-written gauges until then, revisit. |
| **Explicit Observable Gauges** | Standard Node runtime APIs `process.memoryUsage()` and `performance.eventLoopUtilization()` | **Selected.** Registered on `roleradar-web-runtime` meter: `nodejs.eventloop.utilization` (ratio 0.0–1.0, unit "1", using `METRIC_NODEJS_EVENTLOOP_UTILIZATION` from `@opentelemetry/semantic-conventions/incubating`), and two custom non-semantic-convention gauges `process.runtime.nodejs.memory.heap_used` (By) and `process.runtime.nodejs.memory.rss` (By). Lightweight, reliable, and without extra dependencies. |

### 6. Metric Label Allowlist and Cardinality Protection (Views)

| Candidate | Mechanism | Assessment |
| --- | --- | --- |
| **OpenTelemetry Views (`MetricStreamConfiguration.TagKeys` in .NET, `ViewOptions` with `createAllowListAttributesProcessor` in JS)** | Standard OpenTelemetry SDK View mechanism for pre-aggregation attribute filtering | **Selected.** OpenTelemetry metrics SDKs aggregate measurements into buckets at emission time. Views allow configuring an exact allowlist of tag/attribute keys per instrument (`MetricStreamConfiguration.TagKeys` in .NET `AddView`, and `createAllowListAttributesProcessor` from `@opentelemetry/sdk-metrics` passed to `ViewOptions.attributesProcessors` in JS). Any tags not explicitly allowlisted (e.g., `owner_id`, entity ids, query parameters, user names, trace IDs) are dropped before aggregation. |
| **Custom Mutable Metric Processor / Downstream Scrubbing** | Hand-rolled metric processor or custom exporter intercepting and mutating `ResourceMetrics` / `MetricRecord`s | **Rejected.** Unlike tracing's `BaseProcessor<Activity>`, OpenTelemetry metrics SDKs do not offer a mutable metric processor interface in the export pipeline, and post-aggregation downstream mutation in custom exporters is non-standard and fragile. SDK Views are the standard-mandated mechanism for metric attribute filtering and cardinality control. |

## Decision Outcome

1. **Native Meters First in .NET & Explicit Web Metrics**:
   - .NET subscribes directly to native meters: `"Microsoft.AspNetCore.Hosting"`, `"Microsoft.AspNetCore.Server.Kestrel"`, `"Microsoft.AspNetCore.Routing"`, `"Microsoft.AspNetCore.Diagnostics"`, `"Microsoft.AspNetCore.RateLimiting"`, `"System.Net.Http"`, `"System.Net.NameResolution"`, `"System.Runtime"`.
   - Web uses explicit recording on standard semantic convention instruments (`http.server.request.duration`, `http.server.active_requests`, `http.client.request.duration`), resolving routes through `matchRoutes` against `build.routes` (`resolveHttpRoute`, returning `"unmatched"` when unconfigured or unmatched). Runtime metrics record `nodejs.eventloop.utilization` (from `@opentelemetry/semantic-conventions/incubating`) and custom non-semantic-convention gauges `process.runtime.nodejs.memory.heap_used` and `process.runtime.nodejs.memory.rss`.
2. **Datastore Meter (`RoleRadar.Store`)**:
   - `RoleRadarStoreMetrics` is implemented in `RoleRadar.Domain.Common` as a single static `Meter("RoleRadar.Store")`. Because `RoleRadar.Domain` is dependency-free, both `RoleRadar.Infrastructure` and `RoleRadar.Identity` can reference it (with `RoleRadar.Identity` gaining a project reference to `RoleRadar.Domain`) without referencing each other or `RoleRadar.Observability`.
   - Instruments:
     - `roleradar.store.operation.duration`: Histogram (`s`) with allowed tags `db.system.name` (`mongodb`, `sqlite`), `db.operation.name`, `db.collection.name`, `error.type`.
     - `roleradar.store.readiness.status`: ObservableGauge (`{status}`) reporting the last probe result per `db.system.name` (1 = healthy, 0 = unhealthy; not an UpDownCounter, not summed) with allowed tags `db.system.name`, `status`.
     - `roleradar.store.readiness.duration`: Histogram (`s`) with allowed tags `db.system.name`, `status`.
   - `MongoReadinessProbe` records readiness status and duration without creating a duplicate meter.
   - `ConnectionStringRedactor.cs` subscribes to MongoDB driver 3.13.0 `CommandStartedEvent`, `CommandSucceededEvent`, and `CommandFailedEvent` on `MongoClientSettings.ClusterConfigurator`. The handler reads the first element of `CommandStartedEvent.Command` (`e.Command.GetElement(0)`) to get the collection name string; if it matches `KnownCollections`, the candidate name is cached by `e.RequestId`. Nothing else from `e.Command` is read or stored (no query filters, no documents, no values).
   - `IdentityReadinessCheck` records SQLite readiness status and duration.
   - Exactly one Meter named `"RoleRadar.Store"` exists across the solution, enforced by `ObservabilityArchitectureTests.ExactlyOneMeterNamedRoleRadarStore_ExistsAcrossSolution`.
3. **OpenTelemetry Views Enforce Cardinality Allowlist**:
   - **In .NET**: Registered via `AddView` on `MeterProviderBuilder` for each instrument using `MetricStreamConfiguration.TagKeys`. Public static arrays in `TelemetrySetup.cs` define the exact allowlists reused by tests.
   - **In Node/TS**: Registered via `ViewOptions` on `MeterProvider` using `createAllowListAttributesProcessor` from `@opentelemetry/sdk-metrics@3.0.0`.
   - All tags not present in the allowlist (including `owner_id`, entity GUIDs, `traceId`, `sub`, `username`, raw `url.full` / `url.query`, free text) are dropped.
4. **Hosts Exporting Metrics & Hygiene**:
   - All hosts (`roleradar-web`, `roleradar-api`, `roleradar-identity`, `roleradar-cli`, `roleradar-mcp`) register `MeterProvider` and export metrics over OTLP when opted in via `OTEL_EXPORTER_OTLP_ENDPOINT`.
   - When `OTEL_EXPORTER_OTLP_ENDPOINT` is unset or empty, 0 metric exporters are registered (`SC-003`).
   - For `RoleRadar.Cli` and `RoleRadar.Mcp.Stdio`, metrics flush with bounded timeout (`ROLERADAR_TELEMETRY_FLUSH_TIMEOUT_MS`, default 2000 ms, clamped 100..5000 ms) without hanging, and `RoleRadar.Mcp.Stdio` logs strictly to `stderr` and writes 0 bytes to `stdout` (Constitution VI).
