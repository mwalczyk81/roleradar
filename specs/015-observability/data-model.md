# Phase 1 Data Model: End-to-End Observability and Local Telemetry Dev Loop (`015-observability`)

**Date**: 2026-11-11
**Feature**: [spec.md](./spec.md)

Feature 015 does not add new persistent domain collections or alter MongoDB/SQLite schemas. It defines the runtime configuration and in-flight telemetry signal models emitted across services over OpenTelemetry Protocol (OTLP).

## 1. `TelemetryOptions` (Configuration Model)

Shared configuration resolved from environment variables across `.NET` (`RoleRadar.Observability.TelemetrySetup`) and Node (`web/app/lib/telemetry.server.ts`).

| Field / Env Variable | Type | Default | Validation & Behavior |
|---|---|---|---|
| `OTEL_EXPORTER_OTLP_ENDPOINT` | `string?` (URI) | `null` (unset) | When unset or whitespace, **zero** OTLP exporters are registered (`SC-003`) and no outbound telemetry connection is opened. When set (e.g., by the Aspire AppHost or compose overlay to a `127.0.0.1` loopback endpoint), registers OTLP trace, log, and metric exporters. |
| `OTEL_EXPORTER_OTLP_PROTOCOL` | `"grpc"` \| `"http/protobuf"` | SDK default (`grpc` in .NET, `http/json` or `http/protobuf` in Node) | Standard OTLP transport selector. |
| `OTEL_SERVICE_NAME` | `string` | Host default (`roleradar-web`, `roleradar-api`, `roleradar-identity`, `roleradar-cli`, `roleradar-mcp`) | Populates `service.name` resource attribute. |
| `ROLERADAR_TELEMETRY_FLUSH_TIMEOUT_MS` | `int` | `2000` (clamped `100..5000`) | Bounded flush timeout used by short-lived `CLI` and `Mcp.Stdio` processes when `OTEL_EXPORTER_OTLP_ENDPOINT` is explicitly configured (`FR-017`), ensuring the process never hangs on exit if the collector is unreachable. |
| Sampling Policy | `AlwaysOnSampler` (100%) | `100%` locally | Samples 100% of traces locally (`FR-003`); production sampling policy is deferred until hosting is un-parked. |

## 2. `TelemetryResource` (Resource Attributes)

Attached to every exported span, log record, and metric stream.

| Attribute Key | Type | Value / Rule |
|---|---|---|
| `service.name` | `string` | Canonical service identifier: `roleradar-web`, `roleradar-api`, `roleradar-identity`, `roleradar-cli`, or `roleradar-mcp`. |
| `service.version` | `string` | Assembly informational version or `package.json` version (`0.1.0`). |
| `service.namespace` | `string` | `"roleradar"` |
| Privacy Constraint | Invariant | Resource attributes MUST NEVER include machine usernames, environment variable dumps, connection strings, or `owner_id`. |

## 3. `TelemetrySpan` (Distributed Trace Span)

Represents a unit of work in a distributed trace (`RR-68`).

| Field / Attribute | Source | Rule / Privacy Constraint (`RR-72`) |
|---|---|---|
| `TraceId` | W3C `traceparent` / `Activity.TraceId` | 32-character lowercase hexadecimal string; identical to the `traceId` in console logs, RFC 9457 problem documents, and `request_correlation_id` audit entries (`FR-002`). |
| `SpanId` / `ParentSpanId` | W3C `traceparent` / `Activity.SpanId` | 16-character lowercase hexadecimal string linking web -> API -> MongoDB and web -> Identity into a single trace tree. |
| HTTP Server / Client Attributes | `.NET 10` (`Microsoft.AspNetCore`, `System.Net.Http`) & Node (`@opentelemetry/instrumentation-http`, `undici`) | Allowlisted attributes only (`http.request.method`, `http.request.method_original`, `url.scheme`, `url.path`, `url.full` [with `?query` and `#fragment` stripped], `http.route`, `http.response.status_code`, `server.address`, `server.port`, `network.protocol.version`, `network.peer.address`, `error.type`). `RoleRadarSpanAttributeProcessor` / `WebSpanAttributeProcessor` (`ADR 0050`, independent of `LogScrubber`) explicitly removes `url.query`, strips query/fragment parts of `url.full`, removes all `http.request.header.*` and `http.response.header.*` tags, and drops any attribute not on the allowlist. |
| Database Attributes | `MongoDB.Driver` 3.13.0 (`MongoTelemetry`) | Allowlisted attributes (`db.system.name` = `"mongodb"`, `db.operation.name`, `db.operation.summary`, `db.command.name`, `db.namespace`, `db.collection.name`, `db.query.summary`, `server.address`, `server.port`, `network.transport`, `db.response.status_code`, `db.mongodb.lsid`, `db.mongodb.txn_number`, `db.mongodb.server_connection_id`, `db.mongodb.driver_connection_id`, `db.mongodb.cursor_id`). `TracingOptions.QueryTextMaxLength` is pinned to `0` and the span processor explicitly drops `db.query.text` and `db.statement`. |
| Exception Events & Status Description | Span `exception` event & `StatusDescription` | Sanitized without `LogScrubber` (`ADR 0050`): on any span `exception` event, only `exception.type` is retained (and copied to `error.type` if unset), while `exception.message` and `exception.stacktrace` are dropped; any free-text span `StatusDescription` is cleared (`null`) so raw exception strings never leave on spans. |

## 4. `TelemetryLogRecord` (OTLP Log Record)

Represents a structured log event exported over OTLP (`RR-70`, `RR-86`).

| Field | Source | Rule / Privacy Constraint (`RR-72`, `ADR 0051`) |
|---|---|---|
| `Timestamp` | UTC clock | UTC timestamp. |
| `SeverityNumber` / `SeverityText` | `LogLevel` / web `LogLevel` | Mapped to standard OTLP severity (`TRACE`, `DEBUG`, `INFO`, `WARN`, `ERROR`, `FATAL`). |
| `Body` | Formatted log message | Scrubbed per the Gate 3 (`RR-86` / `ADR 0051`) decision (`RoleRadarLogScrubbingProcessor` / `observability.ts`). |
| `TraceId` / `SpanId` | Active `Activity` / active OTel `Span` | Populated automatically whenever an activity/span is current (`SC-004`). |
| `Attributes` | Structured log properties & `EventId` | Sensitive keys redacted and string values scrubbed per `ADR 0051`. |
| `Exception` | `exception.type`, `exception.message`, `exception.stacktrace` | Scrubbed before OTLP serialization per `ADR 0051`. |

## 5. `TelemetryMetricInstrument` (OTLP Metrics)

Represents aggregate service and datastore health measurements (`RR-69`).

| Instrument Name | Meter | Type / Unit | Allowed Attributes (Bounded Low-Cardinality Only) |
|---|---|---|---|
| `http.server.request.duration` | `Microsoft.AspNetCore.Hosting` / `roleradar-web` | Histogram (`s`) | `http.request.method`, `http.route`, `http.response.status_code`, `error.type`, `url.scheme` |
| `http.server.active_requests` | `Microsoft.AspNetCore.Hosting` / `roleradar-web` | UpDownCounter (`{request}`) | `http.request.method`, `url.scheme` |
| `http.client.request.duration` | `System.Net.Http` / `roleradar-web` | Histogram (`s`) | `http.request.method`, `http.response.status_code`, `server.address`, `server.port`, `error.type` |
| `roleradar.store.operation.duration` | `RoleRadar.Store` | Histogram (`s`) | `db.system.name` (`mongodb`, `sqlite`), `db.operation.name`, `db.collection.name`, `error.type` |
| `roleradar.store.readiness.status` | `RoleRadar.Store` | ObservableGauge / Counter (`{status}`) | `db.system.name` (`mongodb`, `sqlite`), `status` (`healthy`, `unhealthy`) |
| `roleradar.store.readiness.duration` | `RoleRadar.Store` | Histogram (`s`) | `db.system.name` (`mongodb`, `sqlite`), `status` (`healthy`, `unhealthy`) |
| Runtime Metrics (`dotnet.process.*`, `dotnet.gc.*`, `nodejs.eventloop.*`, `process.runtime.nodejs.memory.*`) | `System.Runtime` / `roleradar-web` | Counter / Gauge / Histogram | Standard runtime dimension tags only. |
| Cardinality & Privacy Rule | All meters | Invariant (`FR-009`, `SC-005`) | **Forbidden labels**: `owner_id`, entity GUIDs (`posting_id`, `company_id`, `application_id`, `note_id`), `traceId`, `sub`, `username`, raw `url.full` / `url.query`, or free-text search/filter strings. |
