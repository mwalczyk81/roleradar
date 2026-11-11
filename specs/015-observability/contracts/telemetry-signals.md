# Contract: Telemetry Signals (Traces, Logs, and Metrics Over OTLP)

**Feature**: `015-observability` (`RR-68`, `RR-69`, `RR-70`, `RR-86`)
**Builds on**: Feature 013 (`contracts/log-records.md`, ADR 0041) and Feature 014 (`contracts/trace-context.md`, ADR 0044, ADR 0046, ADR 0047)

## 1. Exporter Registration & No-Endpoint Invariant (`FR-001`, `FR-005`, `FR-017`, `SC-003`)

1. **Opt-in by `OTEL_EXPORTER_OTLP_ENDPOINT`**:
   - When `OTEL_EXPORTER_OTLP_ENDPOINT` is unset, empty, or whitespace, **no OTLP exporter** (trace, log, or metric) is registered in the dependency-injection container or OpenTelemetry provider pipeline of any host (`web`, `api`, `identity`, `cli`, `mcp`).
   - With no endpoint configured, zero non-loopback network connections are opened.
2. **Short-lived processes (`cli` and `mcp`)**:
   - By default (no `OTEL_EXPORTER_OTLP_ENDPOINT` set), `RoleRadar.Cli` and `RoleRadar.Mcp.Stdio` register no OTLP exporters; their W3C `traceId` correlation continues to work through `Activity.Current`, console/stderr logs, and `request_correlation_id` audit entries from Feature 014.
   - When a developer explicitly sets `OTEL_EXPORTER_OTLP_ENDPOINT` for `cli` or `mcp`, the OTLP exporter flushes on shutdown with a bounded timeout (`ROLERADAR_TELEMETRY_FLUSH_TIMEOUT_MS`, default `2000` ms) so `cli` never hangs if the collector is down.
   - `RoleRadar.Mcp.Stdio` MUST NEVER write any telemetry, log, or diagnostic byte to `stdout` under any configuration (Constitution Principle VI).

## 2. Distributed Tracing Contract (`RR-68`, `FR-002`, `FR-003`, `SC-001`)

1. **Trace ID continuity**:
   - The 32-character lowercase hexadecimal W3C `traceId` is unchanged from Feature 014.
   - For any request entering `web` and calling `api` (and through it `MongoDB`) or `identity`, every span exported across `roleradar-web`, `roleradar-api`, and `roleradar-identity` carries the exact same `traceId` written to console/OTLP logs, returned in RFC 9457 problem documents (`traceId`), and stored in audit entries (`request_correlation_id`).
2. **One mechanism on the web (`web/app/lib/trace-context.server.ts` & `telemetry.server.ts`)**:
   - The OpenTelemetry JS `BasicTracerProvider` / `NodeTracerProvider`, `AsyncLocalStorageContextManager`, and global `W3CTraceContextPropagator` are the single mechanism for creating spans and propagating `traceparent`.
   - Manual `randomBytes` ID generation and manual `trace.setSpanContext(ROOT_CONTEXT, ...)` in `trace-context.server.ts` are replaced by active spans from `@opentelemetry/api`.
   - The strict 55-character lowercase `traceparent` edge validation rule from Feature 014 remains in front of `propagation.extract` so malformed, uppercase, all-zero, or oversized headers are discarded and replaced by a new root span, and `tracestate` is never forwarded.
3. **Sampling**:
   - Local trace sampling defaults to `100%` (`AlwaysOnSampler`) across `web`, `api`, and `identity`.
4. **MongoDB spans**:
   - `RoleRadar.Observability` subscribes to `MongoDB.Driver` (`MongoTelemetry.ActivitySourceName`), and `ConnectionStringRedactor.CreateClient` explicitly sets `TracingOptions = new TracingOptions { Disabled = false, QueryTextMaxLength = 0 }` so command spans (`find`, `insert`, `update`, `aggregate`, `commitTransaction`) are exported as children of the API request span without `db.query.text` (verified against Testcontainers MongoDB `rs0` in `tests/RoleRadar.Tests.Integration/Observability/MongoTelemetryIntegrationTests.cs` under `[Trait("Lane", "default")]`).
5. **Real-stack end-to-end OTLP proof (`SC-001` in `RealStackEndToEndTests`, `Lane=real-stack`)**:
   - In Gate 2, `tests/RoleRadar.Tests.Integration/Containers/RealStackEndToEndTests.cs` (`[Trait("Lane", "real-stack")]`, executed via `pwsh ./scripts/test-e2e-real.ps1`, never touching the developer's stack) attaches the pinned official CNCF OpenTelemetry Collector container (`otel/opentelemetry-collector-contrib:0.164.0@sha256:340c9ca6a36617084c6661bdc4f0fe92b7cda5a805d61673ec8db375fddc522b`, Apache-2.0, `>= 14` days old) to the throwaway Docker network `rrtest-real-net-<runId>` (`--network-alias otel-collector`) configured with the standard `otlp` receiver (`4317`/`4318`) and `file` exporter (`/telemetry/otlp.jsonl`), sets `OTEL_EXPORTER_OTLP_ENDPOINT` on the throwaway `identity`, `api`, and `web` containers, and asserts that the exported OTLP spans share one 32-character W3C `traceId` across `roleradar-web` and `roleradar-identity` (`signInId`) and one `traceId` across `roleradar-web`, `roleradar-api`, and `mongodb` (`followedId` and `writeTraceId`).
6. **Span attribute allowlist/drop-list & exception events (`ADR 0050`, Gate 2)**:
   - Gate 2 stands completely independent of `RR-86`'s outcome in Gate 3: `RoleRadarSpanAttributeProcessor` (.NET) and `WebSpanAttributeProcessor` (Node) do **not** depend on `LogScrubber`.
   - They enforce an explicit attribute allowlist and drop-list: removing `url.query`, stripping `?query` and `#fragment` from `url.full` (and legacy `http.url`/`http.target`), removing all request/response headers (`http.request.header.*`, `http.response.header.*`), removing `db.query.text` and `db.statement`, and dropping any non-allowlisted attribute.
   - For exception events recorded on spans (`exception` event) and span error status descriptions: only `exception.type` (and `error.type` on the span) is retained; `exception.message`, `exception.stacktrace`, and free-text `StatusDescription` are stripped/cleared on spans so raw exception strings cannot leak secrets on traces.

## 3. Structured Logs Over OTLP Contract (`RR-70`, `RR-86`, `FR-006`, `FR-007`, `SC-004`)

1. **Dual output when OTLP is enabled**:
   - Console logging (`ROLERADAR_LOG_FORMAT=text|json`) continues to function for `docker compose logs` and CLI stderr output.
   - When `OTEL_EXPORTER_OTLP_ENDPOINT` is configured, log records are also exported via OTLP with `TraceId`, `SpanId`, `SeverityNumber`, `SeverityText`, `Body`, structured `Attributes`, and `Resource` (`service.name`).
2. **Evidence-first scrubbing decision (`ADR 0051`, `RR-86`)**:
   - `ADR 0051` documents a concrete producer (or proves none exists) for each of the 7 `LogScrubber` rules, records spike test results on whether an OpenTelemetry `BaseProcessor<LogRecord>` can scrub message bodies, structured attributes, and exception text before export, and compares the OTLP log data model against the Feature 013 console record shape.
   - Regardless of whether scrubbing runs in `BaseProcessor<LogRecord>`, in the formatter, or both, 100% of exported OTLP log records and console lines must pass the `RR-72` sentinel redaction suite.

## 4. Metrics Contract (`RR-69`, `FR-008`, `FR-009`, `SC-005`)

1. **RED (Rate, Errors, Duration) HTTP metrics**:
   - `roleradar-api` and `roleradar-identity` export `http.server.request.duration` and `http.server.active_requests` (from `Microsoft.AspNetCore.Hosting`) and outbound `http.client.request.duration` (from `System.Net.Http`).
   - `roleradar-web` exports `http.server.request.duration`, `http.server.active_requests`, and outbound `http.client.request.duration` over OTLP.
2. **Datastore & Runtime health metrics**:
   - `roleradar-api` and `roleradar-identity` export `roleradar.store.readiness.status`, `roleradar.store.readiness.duration`, and `roleradar.store.operation.duration` (under meter `RoleRadar.Store`), plus `.NET` runtime metrics (`System.Runtime`).
   - `roleradar-web` exports Node runtime health metrics (memory working set / heap and event-loop lag).
3. **Label cardinality allowlist**:
   - Metric attributes are restricted to bounded operational dimensions (`http.request.method`, `http.route`, `http.response.status_code`, `error.type`, `url.scheme`, `server.address`, `server.port`, `db.system.name`, `db.operation.name`, `db.collection.name`, `status`).
   - Raw request paths with dynamic IDs (when `http.route` is available, `http.route` is used; unmatched 404 paths collapse to a constant route template or omit path), `url.query`, `owner_id`, entity GUIDs, `sub`, `username`, and free text are forbidden on metrics.
