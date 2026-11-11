# Feature Specification: End-to-End Observability and Local Telemetry Dev Loop

**Feature Branch**: `015-observability`

**Created**: 2026-11-11

**Status**: Draft

**Input**: User description: "Feature 015, observability. Create the branch 015-observability from a clean main first. Epic RR-66. Children RR-67 to RR-72, plus RR-86. What it is. OpenTelemetry across web, API, Identity, CLI and MCP, with a local setup a developer can open and use. The goal is a dev experience worth showing: one trace across the services, metrics, and logs in one place. The dev loop starts the whole stack with one command. Builds on 014. W3C trace context is already the correlation id (traceparent, Activity, @opentelemetry/api and @opentelemetry/core in the web). 015 adds instrumentation and exporters. It does not change the id."

## Clarifications

### Session 2026-11-11

- Q: In what order should the story gates and pull requests be delivered, and how should `RR-67` and `RR-71` be split? → A: Five sequential gates (`FR-016`): Gate 1 (P1) is the `RR-67` spike and `ADR 0049` (docs only); Gate 2 (P2) is distributed tracing with the privacy gate (`RR-68`, `RR-72`); Gate 3 (P3) is OTLP logs with the scrubbing evaluation (`RR-70`, `RR-86`, `RR-72`); Gate 4 (P4) is metrics (`RR-69`); Gate 5 (P5) is the local AppHost and dashboard (`RR-71`).
- Q: What is the expected outcome and fallback if the `RR-67` spike finds the Aspire AppHost cannot reproduce the MongoDB replica set or Identity's issuer URLs and TLS? → A: Confirming Option 2 (Aspire AppHost + Dashboard) or reporting a blocker is the expected outcome of `RR-67`. If a blocker is found on replica-set MongoDB or Identity's issuer URLs and TLS, fall back to running the standalone Aspire Dashboard in an opt-in Docker Compose overlay, recorded in `ADR 0049`.
- Q: Do the short-lived `CLI` and `Mcp.Stdio` processes export telemetry in Feature 015? → A: Decided: no telemetry export in 015 by default, because the W3C `traceId` is already recorded in logs and audit entries from Feature 014. Neither process registers an OTLP exporter unless a developer explicitly configures an endpoint, and then with a bounded flush so the CLI never hangs; nothing ever goes to `stdout`.
- Q: What dashboard views are expected from the Aspire Dashboard? → A: The Aspire Dashboard provides built-in views (traces, structured logs, metrics, and resource graph) and, to the spec author's knowledge, does not support saved custom dashboards; the `RR-67` spike confirms this, and a future hosted setup would use a different backend for custom saved dashboards.
- Q: What trace sampling policy applies in Feature 015? → A: Sample everything (100% / AlwaysOn) locally by default; a production sampling policy waits until hosting is un-parked.
- Q: How should `SC-003` verify that nothing leaves the machine by default? → A: With no OTLP endpoint configured, no OTLP exporter is registered in the service pipeline and no non-loopback network connection is opened.

## User Scenarios & Testing *(mandatory)*

<!--
  Each user story below is an independent delivery gate with its own pull request, following the gated workflow established in Feature 014.
  Gate order: P1 (RR-67 spike & ADR 0049, docs only) -> P2 (RR-68 tracing + RR-72 privacy gate) -> P3 (RR-70 logs + RR-86 scrubbing decision + RR-72) -> P4 (RR-69 metrics) -> P5 (RR-71 local AppHost/overlay & Aspire Dashboard).
-->

### User Story 1 - Local Observability Spike and Architecture Decision Record (Priority: P1)

As the project owner and developer (`RR-67`), I want an exploratory spike run in an isolated scratch directory with fictional data and recorded as `ADR 0049` (a docs-only first gate and PR) that compares the Aspire AppHost and Aspire Dashboard against Seq, Jaeger, and Grafana LGTM, and either confirms Option 2 (Aspire AppHost + Dashboard) or reports a concrete blocker and triggers the fallback (standalone Aspire Dashboard in an opt-in Docker Compose overlay) before any production code is changed.

**Why this priority**: Running the architecture spike and recording `ADR 0049` as Gate 1 (docs only) settles the local collector/orchestrator target, verifies replica-set MongoDB and Identity issuer/TLS parity, confirms the Aspire Dashboard's built-in view capabilities, and locks in the drift-prevention strategy before implementation gates begin.

**Independent Test**: Can be verified independently as a documentation-only pull request (`ADR 0049` and index updates) backed by reproducible evidence from an isolated scratch directory proving every required check without modifying real volumes or repository source code.

**Acceptance Scenarios**:

1. **Given** an isolated scratch directory using strictly fictional data and throwaway volumes, **When** the `RR-67` spike evaluates the Aspire AppHost and Aspire Dashboard against Seq, Jaeger, and Grafana LGTM, **Then** `ADR 0049` is authored and either confirms Option 2 (Aspire AppHost running the dev loop from source with the Aspire Dashboard) or documents a concrete blocker on replica-set MongoDB or Identity's issuer URLs and TLS and selects the fallback (standalone Aspire Dashboard in an opt-in Docker Compose overlay).
2. **Given** `ADR 0049`, **When** it is reviewed for completeness, **Then** it explicitly documents:
   - Whether the AppHost reproduces MongoDB as a replica set for multi-document transactions (or triggers the compose overlay fallback).
   - Whether the AppHost preserves Identity's issuer URLs and TLS behavior as handled by the compose stack today (or triggers the compose overlay fallback).
   - How the chosen local setup and the packaged Docker Compose stack avoid drifting on ports, environment variables, and pinned container image tags/digests.
   - The owner-decided `CLI` and `Mcp.Stdio` policy: no telemetry export by default in Feature 015 (because the W3C `traceId` is already present in logs and audit entries from Feature 014), with an OTLP exporter registered only when an endpoint is explicitly configured, using a bounded flush so the CLI never hangs and never writing to `stdout`.
   - Confirmation of whether the Aspire Dashboard provides only built-in views (traces, logs, metrics, resource graph) with no saved custom dashboards, noting that a future hosted backend would handle custom saved dashboards.
   - Explicit deferral of Option 3 (using the AppHost as the single orchestration and deployment artifact publisher, e.g., for Azure Container Apps via `azd`) until hosting is un-parked and a target is chosen.

---

### User Story 2 - End-to-End Distributed Tracing and Telemetry Privacy Gate (Priority: P2)

As a developer diagnosing a request across RoleRadar (`RR-68`, `RR-72`), I want a single user interaction in the web application to produce one connected distributed trace spanning the web frontend, the backend API, the identity service, and datastore operations—reusing the existing W3C `traceparent` correlation identifier from Feature 014 without altering its format, sampling 100% of traces locally by default—and I want the automated Telemetry Privacy Gate (`RR-72`) shipping in this same gate so that no sensitive personal data, credentials, or untrusted posting content can ever leak into exported telemetry or leave the local machine by default.

**Why this priority**: This is the first code gate that registers an exporter; shipping distributed tracing (`RR-68`) together with the Telemetry Privacy Gate (`RR-72`) guarantees Constitution Principle I (Privacy First) from the very first exported span.

**Independent Test**: Can be fully tested in isolation by executing multi-service requests (web-to-API-to-datastore and web-to-Identity) against an isolated test telemetry receiver, verifying that all spans share the single 32-character hexadecimal trace ID and parent-child span hierarchy, that no exporter is registered when no endpoint is configured, and that sentinel secrets and untrusted payloads are absent from all exported spans.

**Acceptance Scenarios**:

1. **Given** the application services are configured with a local OTLP endpoint and default 100% local sampling, **When** a user or test initiates an action on the web frontend that calls the backend API and queries or mutates the datastore, **Then** all exported spans (web server request, outbound HTTP request, API request, and datastore command) share one trace ID matching the `traceId` in application logs and problem documents, linked in a single parent-child hierarchy.
2. **Given** a sign-in or token request flows between the web frontend and the identity service, **When** traces are exported, **Then** the server-side identity operations are linked to the originating trace context without exposing passwords, one-time codes, authorization codes, or session tokens in span names, attributes, or events.
3. **Given** default configuration with no OTLP endpoint configured, **When** any service (web, API, Identity, CLI, or MCP) starts and handles requests or commands, **Then** no OTLP exporter is registered and no non-loopback network connection is opened.
4. **Given** `CLI` and `Mcp.Stdio` processes, **When** they run with default configuration, **Then** they export no telemetry; **When** a developer explicitly configures an OTLP endpoint for them, **Then** they export spans using a bounded flush timeout so the CLI never hangs on exit and `Mcp.Stdio` writes zero bytes of telemetry or diagnostics to `stdout`.
5. **Given** requests containing sentinel secrets (bearer tokens, basic credentials, JSON Web Tokens, datastore connection strings, OAuth `code`/`state` parameters, passwords, one-time verification codes) and untrusted domain text (note bodies, posting descriptions, filter literals), **When** spans are captured by the Telemetry Privacy Gate (`RR-72`), **Then** zero sentinel values or restricted text payloads appear in any span attribute, event, status description, or resource metadata.

---

### User Story 3 - Structured Logs Over Telemetry Pipeline and Scrubbing Evaluation (Priority: P3)

As a developer inspecting system behavior (`RR-70`, `RR-86`, `RR-72`), I want structured application and security logs exported through the standard OTLP telemetry protocol alongside console output, correlated with the active trace and span identifiers, and backed by an evidence-first architectural decision (`RR-86`) determining whether runtime log scrubbing stays as-is, shrinks, moves into an OpenTelemetry log processor, or is retired.

**Why this priority**: Follows immediately after tracing so logs and traces correlate in one OTLP stream while resolving the open scrubbing and formatter questions from ADRs 0046 and 0047 under the protection of the `RR-72` privacy gate.

**Independent Test**: Can be fully tested by recording the evidence matrix for all seven scrubbing rules (`RR-86`), running application and security log events containing sentinel secrets and exceptions through the configured telemetry log pipeline into an isolated test collector, and verifying both trace/span correlation and complete redaction under the Telemetry Privacy Gate (`RR-72`).

**Acceptance Scenarios**:

1. **Given** the seven scrubbing rules in `LogScrubber` (ADR 0046), **When** the `RR-86` evaluation runs, **Then** it names a concrete real producer (or proves the absence of one) for each of the seven rules, tests whether an OpenTelemetry log processor can scrub message bodies, structured attributes, and exception text before export, compares the OTLP log data model against the Feature 013 console record contract (ADR 0047), and records the resulting decision (keep, shrink, move to a log processor, or drop and reopen the console formatter decision) in an ADR.
2. **Given** an active request or command with a trace context and an OTLP endpoint configured, **When** any service emits a structured log entry, **Then** the exported OTLP log record carries the matching `TraceId` and `SpanId`, service resource attributes, severity, and structured properties.
3. **Given** all seven categories of sentinel secrets (including secrets embedded inside exception messages and third-party structured properties), **When** logs are exported over OTLP, **Then** the Telemetry Privacy Gate (`RR-72`) verifies that zero unredacted sentinel values appear in exported log bodies, attributes, or exception details.
4. **Given** the stdio MCP host (`Mcp.Stdio`), **When** logging and optional OTLP export are configured, **Then** `stdout` remains reserved exclusively for MCP protocol frames and zero diagnostic or telemetry output is written to `stdout` (Constitution Principle VI).

---

### User Story 4 - Service Request and Datastore Health Metrics (Priority: P4)

As a developer monitoring service performance and reliability (`RR-69`), I want each long-running service to emit standard metrics covering request rate, error rate, request duration (RED metrics), runtime health, and datastore health over OTLP so I can spot latency spikes, error surges, and datastore degradation in one place.

**Why this priority**: Completes the three telemetry signals (traces, logs, metrics) across the services before wiring the final developer dashboard loop in Gate 5.

**Independent Test**: Can be tested independently by driving synthetic successful requests, validation/authorization errors, and simulated datastore outages against the services while capturing exported metric points in an isolated test reader, verifying instrument presence, accuracy, and label cardinality compliance.

**Acceptance Scenarios**:

1. **Given** HTTP traffic across the web frontend, backend API, and identity service with an OTLP endpoint configured, **When** metrics are collected, **Then** request rate, error counts by status/category, and request duration histograms are exported with bounded, low-cardinality attributes.
2. **Given** datastore operations and readiness probes on the backend API and identity service, **When** metrics are collected, **Then** store command duration/outcome and store readiness health metrics are exported and accurately reflect healthy versus degraded datastore states.
3. **Given** any exported metric instrument across all services, **When** metric attributes/labels are inspected by automated tests (`RR-72`), **Then** zero high-cardinality or sensitive values (such as `owner_id`, entity GUIDs, usernames, URLs with query strings, or free-text search terms) appear as metric labels.

---

### User Story 5 - Local Developer Loop and Aspire Dashboard (Priority: P5)

As a developer working on RoleRadar (`RR-71`), I want to launch the local development telemetry setup chosen in `ADR 0049` (the Aspire AppHost running the dev loop from source with the Aspire Dashboard, or the fallback standalone Aspire Dashboard in an opt-in Docker Compose overlay if `ADR 0049` recorded a blocker) bound exclusively to loopback (`127.0.0.1`), using isolated throwaway volumes when running the AppHost, and viewing traces, structured logs, metrics, and the service resource graph in the Aspire Dashboard's built-in views.

**Why this priority**: Brings together the tracing, logging, and metric instrumentation from Gates 2–4 into the interactive local developer experience defined by Gate 1 (`ADR 0049`), while keeping the packaged Docker Compose stack intact for real data, `real-stack` E2E tests, and the demo.

**Independent Test**: Can be tested by starting the local setup implemented per `ADR 0049`, verifying replica-set transactions and OIDC sign-in, confirming the Aspire Dashboard binds only to `127.0.0.1` and populates its built-in views (traces, structured logs, metrics, resource graph), verifying real volumes are untouched, and running automated drift checks between the AppHost and the packaged Docker Compose stack.

**Acceptance Scenarios**:

1. **Given** a developer machine with a clean checkout, **When** the developer starts the local dev loop (`RR-71`) per `ADR 0049`, **Then** the services and the Aspire Dashboard start with a single command, the dashboard binds exclusively to `127.0.0.1`, and its built-in views (traces, structured logs, metrics, and resource graph) display live telemetry from the services.
2. **Given** the Aspire AppHost runs the dev loop from source (Option 2), **When** the datastore and identity service start, **Then** they use dedicated throwaway volumes and never mount, read, or mutate the developer's real MongoDB volume or `roleradar-identity-data` volume.
3. **Given** the packaged Docker Compose stack (`deploy/`), the `real-stack` E2E lane, and the fictional demo stack, **When** the local setup (`RR-71`) is added to the repository, **Then** the compose stack remains the unchanged packaged runtime for real data, `real-stack` E2E tests, and the demo, with automated checks preventing port, environment variable, or pinned container image drift.

---

### Edge Cases

- **No OTLP endpoint configured (default)**: When no OTLP endpoint is configured, no OTLP exporter is registered in any service and no non-loopback network connection is opened (`SC-003`).
- **Collector unreachable when an endpoint is configured**: When an OTLP endpoint is configured but unreachable, long-running services MUST continue operating normally without throwing unhandled exceptions, blocking request threads, or growing memory unboundedly; short-lived `CLI` and `Mcp.Stdio` processes MUST use a bounded flush timeout so they never hang on exit.
- **Malformed inbound `traceparent`**: As established in Feature 014, a malformed, all-zero, or invalid `traceparent` header is discarded and replaced with a fresh trace context so exported spans always have a valid trace ID.
- **Datastore query and command text**: Exported database spans MUST NOT include raw document bodies, note `content`, `ruleText`, or sensitive filter literals in span attributes (db statement scrubbing/omission).
- **Short-lived CLI and stdio MCP processes**: By default in Feature 015, neither `CLI` nor `Mcp.Stdio` registers an OTLP exporter (their `traceId` is already present in console/stderr logs and audit entries from Feature 014). If a developer explicitly configures an OTLP endpoint, `CLI` flushes within a strict bounded timeout before exiting, and `Mcp.Stdio` never writes a single byte of telemetry or diagnostics to `stdout` nor binds any network listener (Constitution Principle VI).
- **Exception stack traces and messages containing secrets**: When a downstream driver or authentication failure throws an exception containing a connection string, password, or token in its message, exported spans MUST drop `exception.message`, `exception.stacktrace`, and free-text `StatusDescription` (retaining only `exception.type` / `error.type` per `ADR 0050`, independent of `LogScrubber`), and exported log records MUST scrub or redact the sensitive value before export (`ADR 0051`).
- **Volume and environment isolation**: Starting the local AppHost while the developer's real compose stack is stopped (or running on different ports) MUST never attach to `roleradar-mongo-data` or `roleradar-identity-data`.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: All long-running application services (web frontend, backend API, and identity service) MUST support exporting distributed traces, structured logs, and metrics over the standard OpenTelemetry Protocol (OTLP) when an OTLP endpoint is configured via environment variables, so switching to a hosted OTLP-compatible backend later requires only a configuration change.
- **FR-002**: Distributed tracing MUST build directly on Feature 014's W3C Trace Context (`traceparent`, `.NET Activity`, and `@opentelemetry/api` / `@opentelemetry/core` in the web app) without changing the 32-character lowercase hexadecimal `traceId` used in logs, RFC 9457 problem documents, and `request_correlation_id` audit entries.
- **FR-003**: A single web request that calls the backend API (and through it the datastore) or the identity service MUST produce one connected trace with parent-child spans across service boundaries (`RR-68`), with local trace sampling set to sample everything (100% / AlwaysOn) by default and a production sampling policy deferred until hosting is un-parked.
- **FR-004**: The Telemetry Privacy Gate (`RR-72`) MUST ship in the same pull request as the first telemetry exporter (Gate 2 for traces, extended in Gate 3 for logs and Gate 4 for metric labels), enforce a `LogScrubber`-independent span allowlist/drop-list processor (`ADR 0050`) that removes `url.query`, query/fragment parts of `url.full`, request/response headers, `db.query.text`, `exception.message`, `exception.stacktrace`, and free-text `StatusDescription` (retaining only `exception.type` / `error.type`) so Gate 2 stands independent of `RR-86`, and verify via automated tests that no sentinel secret (bearer tokens, basic auth credentials, JWTs, datastore connection credentials, OAuth `code`/`state` values, passwords, TOTP codes) or sensitive domain content (note bodies, untrusted posting text, raw request bodies) appears in any exported span, log record, or metric.
- **FR-005**: By default, when no OTLP endpoint is explicitly configured, no service MAY register an OTLP exporter or open outbound telemetry connections; when the local Aspire Dashboard is running, its UI and ingestion endpoints MUST bind exclusively to loopback (`127.0.0.1`).
- **FR-006**: Gate 3 (`RR-70`, `RR-86`) MUST execute an evidence-first evaluation of runtime log scrubbing before finalizing the OTLP log pipeline, naming a concrete producer (or proving none exists) for each of the seven `LogScrubber` rules, testing whether an OpenTelemetry log processor can scrub exception text, message bodies, and structured attributes, evaluating whether the OTLP log data model should replace the Feature 013 console record shape, and recording the decision (keep, shrink, move to an OpenTelemetry log processor, or drop and reopen the console formatter decision from ADR 0047) in `ADR 0051`.
- **FR-007**: Services MUST export structured logs over OTLP (when an endpoint is configured) correlated with the active `TraceId` and `SpanId` while preserving console log usability and enforcing every redaction guarantee validated by `RR-72`.
- **FR-008**: Gate 4 (`RR-69`) MUST instrument and export service metrics over OTLP covering HTTP request rate, error counts, request duration distributions (RED metrics), runtime health, and datastore command/readiness health across the web, API, and identity services.
- **FR-009**: Exported metrics MUST enforce strict label cardinality and privacy rules: metric attributes MUST NOT contain `owner_id`, entity identifiers, usernames, raw URLs with query parameters, or free-text inputs.
- **FR-010**: Gate 1 (`RR-67`) MUST perform a spike in an isolated scratch directory using strictly fictional data and record `ADR 0049` (as a docs-only PR) comparing the Aspire AppHost + Aspire Dashboard (Option 2) against Seq, Jaeger, and Grafana LGTM; the expected outcome of the spike is either confirming Option 2 or reporting a concrete blocker and selecting the fallback (standalone Aspire Dashboard in an opt-in Docker Compose overlay).
- **FR-011**: `ADR 0049` (`RR-67`) MUST explicitly verify and document:
  1. Orchestrating MongoDB as a replica set so multi-document transactions work identically to the compose stack (or triggering the standalone Aspire Dashboard compose overlay fallback if blocked).
  2. Preserving Identity's issuer URLs and TLS behavior as handled by the compose stack today (or triggering the standalone Aspire Dashboard compose overlay fallback if blocked).
  3. Preventing drift on ports, environment variables, and pinned container image tags/digests between the AppHost and the Docker Compose files.
  4. Recording the `CLI` and `Mcp.Stdio` telemetry policy and rationale: no telemetry export by default in Feature 015 because the W3C `traceId` is already in logs and audit entries from Feature 014; an OTLP exporter is registered only when an endpoint is explicitly configured, with a bounded flush timeout so the CLI never hangs, and nothing ever written to `stdout` (`Mcp.Stdio` Principle VI).
  5. Confirming that the Aspire Dashboard provides built-in views (traces, structured logs, metrics, resource graph) and no saved custom dashboards, noting that a hosted setup would use a different backend for custom dashboards.
  6. Recording that Option 3 (using the AppHost as the single orchestration and deployment artifact generator, e.g., for Azure Container Apps via `azd`) is deferred until hosting is un-parked and a hosting target is selected.
- **FR-012**: Gate 5 (`RR-71`) MUST implement the local setup confirmed by `ADR 0049` (the Aspire AppHost running the dev loop from source with the Aspire Dashboard, or the fallback opt-in Docker Compose overlay with the standalone Aspire Dashboard if `ADR 0049` recorded a blocker), populating the Aspire Dashboard's built-in views (traces, structured logs, metrics, and resource graph).
- **FR-013**: The Aspire AppHost MUST use dedicated throwaway volumes and MUST NEVER mount, read, or mutate the developer's real MongoDB volume or `roleradar-identity-data` volume; the Docker Compose stack MUST remain the packaged stack for real data, the `real-stack` E2E test lane, the fictional demo, and any future deployment.
- **FR-014**: Every mechanism introduced in this feature MUST comply with Constitution Principle X (Standards First) and Principle VIII (Pinned Dependencies): platform and OpenTelemetry standard instrumentations/exporters must be preferred over hand-rolled code, recorded in the implementation plan's "Standards considered" table, and pinned to exact versions and container digests.
- **FR-015**: Any screenshot, recording, or dashboard capture committed to documentation or shown publicly MUST be generated exclusively from the fictional demo/seed data, never from a developer's personal database (Constitution Principle I).
- **FR-016**: Work MUST be delivered in five sequential, independently verifiable gates, each with its own pull request (with Gate 2 splitting into Gate 2a for `.NET` and Gate 2b for `web` if its task count exceeds ~45):
  1. **Gate 1 (P1)**: `RR-67` spike and `ADR 0049` (docs only).
  2. **Gate 2 (P2)**: `RR-68` end-to-end distributed tracing + `RR-72` telemetry privacy gate + `ADR 0050` (span allowlist/drop-list processor, independent of `RR-86`).
  3. **Gate 3 (P3)**: `RR-70` OTLP structured logs + `RR-86` runtime scrubbing decision (`ADR 0051`) + `RR-72` log privacy tests.
  4. **Gate 4 (P4)**: `RR-69` request and datastore health metrics + metric label privacy/cardinality tests.
  5. **Gate 5 (P5)**: `RR-71` local AppHost (or compose overlay fallback per `ADR 0049`) and Aspire Dashboard built-in views.
- **FR-017**: `CLI` and `Mcp.Stdio` MUST NOT register an OTLP exporter by default; when an OTLP endpoint is explicitly configured via environment variables, `CLI` and `Mcp.Stdio` MUST use a bounded flush timeout so short-lived runs never hang on exit, and `Mcp.Stdio` MUST never write telemetry or diagnostics to `stdout` or bind a network listener.

### Key Entities

- **Telemetry Trace & Span**: A causal record of work across services identified by the 32-character W3C `traceId` and 16-character `spanId`, sampled at 100% locally, carrying service resource metadata, timing, status, and scrubbed operational attributes.
- **Telemetry Log Record**: A structured log event exported via OTLP containing timestamp, severity, body, scrubbed attributes, exception details (scrubbed), resource metadata, and correlated `traceId`/`spanId`.
- **Telemetry Metric Instrument**: A named counter, histogram, or gauge (covering request rate, error count, duration, runtime health, and datastore health) tagged only with bounded, low-cardinality attributes.
- **Local Telemetry Setup (AppHost or Compose Overlay Fallback)**: The local developer orchestration confirmed by `ADR 0049` that runs the services and the loopback-bound (`127.0.0.1`) Aspire Dashboard (providing built-in traces, structured logs, metrics, and resource graph views) without touching real data volumes or replacing the packaged Docker Compose stack.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of traced multi-service requests (web frontend to backend API to datastore, and web frontend to identity service) under local 100% sampling—verified in the isolated `real-stack` E2E lane (`scripts/test-e2e-real.ps1`, never the developer's stack) with an OTLP Collector receiver—produce a single connected trace where every participating service span (`roleradar-web`, `roleradar-api` + `mongodb`, and `roleradar-identity`) shares the exact 32-character `traceId` logged by the services, returned in RFC 9457 problem documents, and stored in `request_correlation_id` audit entries.
- **SC-002**: 100% of test cases in the Telemetry Privacy Gate (`RR-72`) pass across traces, logs, and metrics, confirming that 0 sentinel secrets (across all 7 redaction categories, including exception messages) and 0 restricted domain text payloads (note content, untrusted posting bodies, raw credentials) appear in exported telemetry payloads.
- **SC-003**: With no OTLP endpoint configured, automated tests verify that 0 OTLP exporters are registered in the service dependency-injection/telemetry pipelines (including `CLI` and `Mcp.Stdio` by default) and 0 non-loopback network connections are opened; when the local Aspire Dashboard is started, 100% of its listening ports bind exclusively to `127.0.0.1`.
- **SC-004**: 100% of the seven `LogScrubber` rules have documented producer evidence in `ADR 0051` (`RR-86`), and exported log records preserve 100% pass rates on the existing redaction contract suites while carrying matching `TraceId` and `SpanId` fields whenever an activity is active.
- **SC-005**: All three long-running services (web, API, Identity) export request rate, error count, and duration metrics plus datastore readiness/command health metrics over OTLP, with 0 high-cardinality or personal identifiers (`owner_id`, entity IDs, free text) present in metric labels.
- **SC-006**: A developer can start the local telemetry setup (`RR-71`, per `ADR 0049`) on the isolated stack with fictional data, execute a multi-document transaction against the isolated replica-set datastore, sign in through the identity service, and manually verify (recording the bound `127.0.0.1` ports, observed 32-hex `traceId`, correlated redacted log entries, rendered metric instruments, and untouched volume check, since the Aspire Dashboard has no public query API) the resulting trace, structured logs, metrics, and resource graph in the Aspire Dashboard's built-in views while the real data volumes remain untouched (0 bytes modified).
- **SC-007**: Automated drift checks verify 0 unaligned ports, required environment variables, or container image tags/digests between the local telemetry setup and the packaged Docker Compose stack.

## Scope Boundaries

### In Scope

- Gate 1 (`RR-67`): Local observability spike in an isolated scratch directory and `ADR 0049` (docs-only PR), with an explicit fallback to a standalone Aspire Dashboard in an opt-in Docker Compose overlay if the AppHost cannot reproduce replica-set MongoDB or Identity's issuer URLs and TLS.
- Gate 2 (`RR-68`, `RR-72`): OpenTelemetry distributed tracing across web, API, Identity, and MongoDB (100% local sampling by default; `CLI` and `Mcp.Stdio` off by default with bounded-flush opt-in) plus the Telemetry Privacy Gate (`RR-72`).
- Gate 3 (`RR-70`, `RR-86`, `RR-72`): Evidence-first evaluation and ADR decision on runtime log scrubbing and the OTLP log processor/record shape (`RR-86`), plus OTLP structured log export covered by `RR-72`.
- Gate 4 (`RR-69`): RED + runtime and datastore health metrics over OTLP with strict label cardinality and privacy checks.
- Gate 5 (`RR-71`): Local developer setup (Aspire AppHost + Dashboard, or the `ADR 0049` fallback overlay) populating the Aspire Dashboard's built-in views (traces, structured logs, metrics, resource graph).

### Out of Scope

- Cloud or remote hosting of RoleRadar services, and production trace sampling policies (which wait for hosting).
- Saved custom dashboards in the local viewer (the Aspire Dashboard provides built-in views only; custom saved dashboards belong to a future hosted backend).
- Automated alerting or paging rules that notify external channels or people.
- Provisioning or paying for a hosted observability backend (plain OTLP ensures a hosted backend later is a configuration change only).
- Passkeys, account management, and multi-account support.
- Option 3 orchestration (replacing Docker Compose with the AppHost or publishing deployment artifacts from the AppHost), which is explicitly deferred until hosting is un-parked.

## Assumptions

- Feature 014 is merged on `main`, providing W3C `traceparent` propagation, `.NET Activity` correlation, `@opentelemetry/api` and `@opentelemetry/core` in the web application, RFC 9457 ProblemDetails with `traceId`, and ADRs 0044–0048.
- The Aspire Dashboard provides built-in views for traces, structured logs, metrics, and the service resource graph, and (to the spec author's knowledge, to be confirmed in `RR-67`) does not support saved custom dashboards.
- The next available Architecture Decision Record number after ADR 0048 is `ADR 0049` (Gate 1, `RR-67`); Gate 2's `LogScrubber`-independent span allowlist/drop-list processor decision takes `ADR 0050`, and Gate 3's runtime log scrubbing evaluation (`RR-86`) takes `ADR 0051`.
- All spikes (`RR-67`, `RR-86`) run in isolated scratch directories using strictly fictional data and throwaway containers/volumes, never touching the developer's running compose stack or real data volumes.
