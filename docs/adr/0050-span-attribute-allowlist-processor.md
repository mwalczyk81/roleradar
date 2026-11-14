# ADR 0050: Span Attribute Allowlist, Drop-List, and Exception-Event Sanitization Processor

- **Status**: Accepted
- **Date**: 2026-11-11
- **Feature**: `015-observability` (Gate 2, backlog RR-68 & RR-72)

## Context and Problem Statement

Feature 015 Gate 2 (`RR-68` and `RR-72`) adds OpenTelemetry distributed tracing and OTLP span export across `roleradar-web`, `roleradar-api`, `roleradar-identity`, `MongoDB.Driver` (`3.13.0` native `MongoTelemetry`), and opt-in bounded-flush export for `roleradar-cli` and `roleradar-mcp`.

Under Constitution Principle I (Privacy First) and Principle V (Untrusted Input), exported trace spans must never leak:

1. **URL query strings or fragments**: OAuth/OIDC authorization codes (`?code=...&state=...`), PKCE verifiers, or OData `$filter` terms (`?$filter=contains(tolower(title),'...')`).
2. **HTTP request or response headers**: `Authorization` (`Bearer` / `Basic`), `Cookie`, `Set-Cookie`, CSRF headers, or custom header values.
3. **Database query text or BSON command bodies**: MongoDB command payloads (`db.query.text`, `db.statement`) that could carry note `content`, criterion `ruleText`, posting descriptions, contact details, or filter literals.
4. **Raw exception messages, stack traces, or free-text status descriptions**: Exceptions recorded on spans (`Activity.AddException` / `span.recordException` or `Activity.SetStatus(ActivityStatusCode.Error, description)`) can embed connection strings, tokens, passwords, or untrusted user input.
5. **Unbounded or non-allowlisted custom tags**: Arbitrary span tags that were not explicitly reviewed for privacy.

Furthermore, Gate 3 (`RR-86`, `ADR 0051`) will evaluate whether runtime log scrubbing (`LogScrubber`, [ADR-0046](./0046-log-redaction.md)) stays, shrinks, moves into an OpenTelemetry log processor, or is dropped. **Gate 2 must stand completely independent of `RR-86`'s outcome in Gate 3**, meaning the Gate 2 span protection mechanism must have **zero dependency on `LogScrubber`**.

## Standards and Options Considered

| Candidate | Mechanism | Depends on `LogScrubber`? | Assessment |
| --- | --- | --- | --- |
| **In-process OpenTelemetry `BaseProcessor<Activity>` (.NET) & `SpanProcessor` (Node) with structural allowlist + drop-list + exception-event sanitization** | OpenTelemetry SDK extension point (`OnEnd` processor) | **No** | **Selected.** Runs synchronously in-process before any exporter sees the span; enforces a closed semantic-convention allowlist, an explicit drop-list, URL query/fragment stripping, and exception-event sanitization without regexes or `LogScrubber`. |
| **External OpenTelemetry Collector `transform` / `redaction` processor** | Out-of-process collector YAML rules | No | **Rejected.** Unredacted query strings, headers, or exception messages would leave the application process over OTLP before scrubbing and would not protect direct OTLP export when no intermediate collector container is present. |
| **Regex text scrubbing of span tags via `LogScrubber`** | Calling `LogScrubber.Scrub` on every tag value | **Yes** | **Rejected.** Couples Gate 2 to `LogScrubber` ahead of Gate 3 (`RR-86`) and treats structured OpenTelemetry semantic-convention attributes as free-text log lines instead of enforcing a strict schema allowlist. |

## Decision Outcome

We implement an in-process, `LogScrubber`-independent span processor in both runtimes:

1. **`.NET`**: `RoleRadarSpanAttributeProcessor : BaseProcessor<Activity>` in `src/RoleRadar.Observability/RoleRadarSpanAttributeProcessor.cs`, registered by `TelemetrySetup.AddRoleRadarTelemetry`.
2. **Web (`Node 22`)**: `WebSpanAttributeProcessor` (`SpanProcessor`) in `web/app/lib/telemetry.server.ts`, registered by `initializeWebTelemetry`.

**Gate 2 stands completely independent of `RR-86` (`ADR 0051`) in Gate 3.** Neither `RoleRadarSpanAttributeProcessor` nor `WebSpanAttributeProcessor` references or calls `LogScrubber`.

### 1. Explicit Drop-List and Driver Configuration

1. **`MongoDB.Driver` 3.13.0 `TracingOptions`**: `ConnectionStringRedactor.CreateClient` explicitly configures `settings.TracingOptions = new TracingOptions { Disabled = false, QueryTextMaxLength = 0 }` so the driver never populates `db.query.text` on `MongoDB.Driver` activities.
2. **Processor Drop-List**: Even if an instrumentation library or caller attaches them, the processor unconditionally removes:
   - `url.query` (and legacy `http.Flavor` / query tags)
   - All HTTP request and response header attributes (`http.request.header.*`, `http.response.header.*`, `http.request.header`, `http.response.header`)
   - `db.query.text` and `db.statement`
   - Any attribute key not present in the closed allowlist below.

### 2. URL Query and Fragment Stripping

For `url.full` (and legacy `http.url` or `http.target` if ever emitted before drop/normalization):

- Any `?query` string and `#fragment` suffix is stripped in-place before export (for example, `https://localhost:5003/auth/callback?code=secret&state=123#frag` becomes `https://localhost:5003/auth/callback`), and any `userinfo` (`https://user:pass@host/path`) is stripped so only scheme, host, port, and path remain.

### 3. Closed Operational Attribute Allowlist

Only the following OpenTelemetry semantic-convention attributes are retained on exported spans; every other tag key is dropped in `OnEnd`:

- **HTTP Server & Client Attributes**:
  - `http.request.method`
  - `http.request.method_original`
  - `http.response.status_code`
  - `http.route`
  - `url.scheme`
  - `url.path` (with any accidental `?query` or `#fragment` stripped)
  - `url.full` (with `?query`, `#fragment`, and `userinfo` stripped)
  - `server.address`
  - `server.port`
  - `network.protocol.version`
  - `network.peer.address`
  - `network.transport`
  - `error.type`
- **Database Attributes (`MongoDB.Driver` 3.13.0 `MongoTelemetry`)**:
  - `db.system.name` (`"mongodb"`)
  - `db.operation.name`
  - `db.operation.summary`
  - `db.command.name`
  - `db.namespace`
  - `db.collection.name`
  - `db.query.summary`
  - `db.response.status_code`
  - `db.mongodb.lsid`
  - `db.mongodb.txn_number`
  - `db.mongodb.server_connection_id`
  - `db.mongodb.driver_connection_id`
  - `db.mongodb.cursor_id`

### 4. Span `exception` Events and `StatusDescription` Sanitization

Raw exception messages and stack traces on spans can contain credentials, connection strings, or untrusted input, and stripping them on spans does not require `LogScrubber`:

1. **Producer Hygiene**:
   - Handlers such as `ApiExceptionHandler` set `ActivityStatusCode.Error` with no status description, set `error.type` on the span, and attach an `exception` event carrying only `exception.type`. Handlers never place `exception.message`, `exception.stacktrace`, or a free-text `StatusDescription` on the span.
2. **Free-Text `StatusDescription` / `status.message`**:
   - Whenever a span has a non-empty status description (`Activity.StatusDescription` in .NET or `span.status.message` in Node), the processor clears it to `null` / `undefined` while preserving the `ActivityStatusCode` / `SpanStatusCode` (`Error`, `Ok`, `Unset`).
3. **Span `exception` Events**:
   - When an `exception` event is recorded on a span (`ActivityEvent` with `Name == "exception"` in .NET, or `TimedEvent` with `name === "exception"` in Node):
     - The processor extracts `exception.type` (if present and non-whitespace).
     - If `error.type` is not already set on the span's attributes, the processor copies `exception.type` to the span's `error.type` attribute.
     - The processor replaces the `exception` event's attributes so **only `exception.type` is retained**, and **`exception.message` and `exception.stacktrace` (and any other event attributes) are dropped**.
   - Non-`exception` span events (if any carry attributes) have all non-allowlisted attributes stripped.

### 5. Reflection Over `Activity._events` and Fail-Closed Guard (.NET)

In .NET (`System.Diagnostics.Activity`), span events are appended via `Activity.AddEvent(ActivityEvent)` and copied into an internal array. The BCL provides no public API to clear, remove, or modify existing `ActivityEvent` items on an `Activity`.

#### Standards and Alternatives Considered

1. **No Public Event Mutation API**: `Activity` exposes `IEnumerable<ActivityEvent> Events`, but lacks any `ClearEvents()`, `RemoveEvent()`, or in-place tag editor.
2. **Wrapping Exporter (`BaseExporter<Activity>` decorator)**:
   - *Alternative*: Wrap `OtlpTraceExporter` (and any other exporter) in a custom decorator that transforms activities during `Export(in Batch<Activity>)`.
   - *Why not chosen*: A wrapping exporter runs only at the tail end of export. It bypasses processors, leaves unredacted events visible to any other processor or in-memory exporter in the pipeline, requires bespoke wrapping logic around each exporter registration, and breaks standard OpenTelemetry `WithTracing(b => b.AddProcessor(...))` composition.
3. **Reflection over `Activity._events` (`Selected`)**:
   - `RoleRadarSpanAttributeProcessor` reads the recorded events, resets the private `_events` field to `null` via reflection, and re-adds sanitized `ActivityEvent` instances containing only `exception.type`.

#### Failure Mode and Fail-Closed Guard

- **Failure Mode**: If a future .NET runtime refactors or renames `_events`, `ActivityEventsField` would resolve to `null`, silently skipping event sanitization and potentially allowing unredacted exception messages or stack traces from third-party libraries to escape.
- **Fail-Closed Guard**:
  - `RoleRadarSpanAttributeProcessor.ActivityEventsField` resolves `typeof(Activity).GetField("_events", BindingFlags.Instance | BindingFlags.NonPublic)` once statically.
  - `TelemetrySetup.AddRoleRadarTelemetry` calls `ValidateActivityEventsField(RoleRadarSpanAttributeProcessor.ActivityEventsField)` during host setup. If the field is `null`, it immediately throws an `InvalidOperationException`:
    > *"System.Diagnostics.Activity._events private field could not be resolved. RoleRadar telemetry privacy gate requires Activity._events reflection to sanitize span exception events."*
  - This ensures the application fails to start rather than running with an unredacted telemetry pipeline.
  - Producer defense-in-depth: Application handlers (such as `ApiExceptionHandler`) never put sensitive exception messages or stack traces on spans in the first place, ensuring privacy even before the processor runs.

### 6. Telemetry Resource Attributes (`TelemetryResource`)

Both `.NET` (`TelemetrySetup`) and `web` (`telemetry.server.ts`) construct an explicit OpenTelemetry `Resource` containing only:

- `service.name` (`roleradar-web`, `roleradar-api`, `roleradar-identity`, `roleradar-cli`, or `roleradar-mcp`, overridable via `OTEL_SERVICE_NAME`)
- `service.namespace` (`"roleradar"`)
- `service.version`

Environment variable detectors, OS username detectors, and process command-line detectors are never enabled so machine usernames, connection strings, and `owner_id` never enter resource attributes.

## Consequences

- **Good**: Gate 2 (`RR-68` + `RR-72`) is 100% self-contained and independent of `RR-86`'s log-scrubbing evaluation in Gate 3 (`ADR 0051`).
- **Good**: Because the processor enforces a closed attribute allowlist plus URL query/fragment stripping and `exception.message`/`exception.stacktrace` removal, new or unexpected tags added by future library upgrades fail closed (are dropped) rather than leaking over OTLP.
- **Neutral**: Detailed exception text (after redaction) belongs to the structured log pipeline evaluated in Gate 3 (`ADR 0051`), while trace spans carry the correlated `traceId`/`spanId`, `error.type`, and `exception.type`.

## Addendum: Maintained ASP.NET Core Instrumentation (Gate 2b, Principle X)

- **Context & Decision**: In `RoleRadar.Api` and `RoleRadar.Identity`, replace hand-set HTTP span tags in `RequestCompletedMiddleware` with official `OpenTelemetry.Instrumentation.AspNetCore` (`1.20.1`, published 2026-10-21, clearing the 14-day rule). `RequestCompletedMiddleware` retains only the `RequestCompleted` log record; all Activity reads and writes (`DisplayName`, `SetTag`, `SetStatus`) are removed.
- **Why Maintained Library First (Principle X)**: A bare ASP.NET Core application with `.AddSource("Microsoft.AspNetCore")` emits native activities named `"Microsoft.AspNetCore.Hosting.HttpRequestIn"`, but under .NET 10 it sets zero HTTP semantic convention tags (`http.request.method`, `url.scheme`, `url.path`, `http.response.status_code`, and `http.route` are all null, and status remains `Unset` on 5xx responses). Rather than hand-rolling tag extraction in custom middleware, `OpenTelemetry.Instrumentation.AspNetCore` implements the W3C and OpenTelemetry HTTP semantic conventions natively.
- **Configuration & ActivitySource**:
  - `RecordException = false`: Prevents raw unredacted exception messages and stack traces from being recorded by the instrumentation library.
  - No enrich callbacks: No request or response headers are captured.
  - Subscribing to `"Microsoft.AspNetCore"`: `AddAspNetCoreInstrumentation` automatically subscribes to `"Microsoft.AspNetCore"` internally. The redundant `.AddSource("Microsoft.AspNetCore")` in `TelemetrySetup.cs` is removed.
- **Privacy Gate Interaction (What the Library Sets and the Processor Removes)**:
  - By default, `OpenTelemetry.Instrumentation.AspNetCore` records `url.query` (from query strings), `user_agent.original` (from the `User-Agent` header), and `client.address` (from client IP).
  - `RoleRadarSpanAttributeProcessor` drops `url.query`, `user_agent.original`, `client.address`, and any `http.request.header.*` tags per the allowlist and drop-list, leaving only safe operational attributes (`http.request.method`, `url.scheme`, `url.path`, `http.response.status_code`, `http.route`, route-based `DisplayName`) on exported spans (verified by `Api_RequestWithSentinelsInQueryUserAgentAndHeaders_ExportsSpanWithNoneOfThem`).
