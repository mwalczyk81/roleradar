# ADR 0051: OTLP Structured Logs, Runtime Scrubbing Processor, and Producer Census (RR-70, RR-86, RR-90)

- **Status**: Accepted
- **Date**: 2026-11-11
- **Feature**: `015-observability`
- **Decider**: Owner (Matt), 2026-11-11: keep all seven LogScrubber rules as the backstop at the console and OTLP boundaries; no shrink, no drop
- **Consulted**: `AGENTS.md`, `.specify/memory/constitution.md` (Principles I, II, X), `ADR 0046`, `ADR 0047`, `ADR 0050`

---

## Context and Problem Statement

Feature 015 User Story 3 exports structured application and security logs over OpenTelemetry OTLP (`RR-70`), correlated with the active W3C `TraceId` and `SpanId` (`SC-004`), and protected by the privacy gate (`RR-72`, `SC-002`).

In Feature 014, `ADR 0046` preserved the custom regex-based `LogScrubber` for console formatting after comparing it to `Microsoft.Extensions.Compliance.Redaction`. Jira issue `RR-86` requires an evidence-based census of all 7 `LogScrubber` rules across `src/`, `web/`, and pinned dependencies (`Duende.IdentityServer`, `Microsoft.AspNetCore.*`, `Microsoft.EntityFrameworkCore`, `MongoDB.Driver`, `System.Net.Http`) before wiring production OTLP log export.

The owner's standing governance principle is:
> *"We should not log secrets in the first place."*

Therefore, filtering at the source is prioritized over scrubbing at the sink. A scrubbing rule with no real producer must be considered for removal, not defended reflexively. Conversely, any rule with a real producer outside application control requires verifiable empirical evidence from real producer runs.

Additionally:

1. An isolated test harness evaluates what an OpenTelemetry `BaseProcessor<LogRecord>` can and cannot mutate across `Body`, `FormattedMessage`, `Attributes`, and `Exception` text.
2. Jira issue `RR-90` requires comparing the existing hand-written `RequestCompletedMiddleware` with ASP.NET Core native HTTP logging (`AddHttpLogging` with `CombineLogs`).

---

## Part 1: RR-86 Producer Census Matrix (Observed Pre-Scrub Evidence at Default Levels)

Each of the 7 rules was executed against real producers using `PreScrubLogCapture`, which intercepts log records directly from `ILogger.Log<TState>` before any console formatter or OpenTelemetry processor runs. Tests ran under the repository's DEFAULT log levels (`LoggingSetup.DefaultLevels`: `Microsoft = Warning`, `System = Warning`, `Microsoft.Hosting.Lifetime = Information`, `Microsoft.AspNetCore.HttpLogging = Information`, `Duende.IdentityServer.Validation.AuthorizeRequestValidator = Warning`, `Duende.IdentityServer.Endpoints.AuthorizeEndpoint = Warning`).

| Rule # | Target Category & Pattern | Observed at Default Levels? | Producing Test Class & Method | Status at Default Levels (Pre-Scrub) | Reachable Only by Operator Override? | Rationale & Trade-offs |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **Rule 1** | Bearer & Basic credentials (`\b(Bearer\|Basic)\s+[^\s"',;]+`) | **No** | `PreScrubCensusContractTests.SignInFlows_AtDefaultLevels_EmitNoBearerBasicOrJwt_PreScrub`<br>`PreScrubCensusContractTests.OutboundHttpClient_FailedCall_EmitsNoBearerOrBasicCredentials_PreScrub` | *No output at default levels.* Neither `System.Net.Http` nor hosting logs request headers; `AddHttpLogging` excludes headers; failed outbound calls emit no wire headers at `System = Warning`. | **Yes**: Operator overrides `Logging__LogLevel__System.Net.Http = Trace` (dumps wire headers) or an unhandled third-party exception message embeds headers. | **Option A (Keep)**: Protects against third-party exception messages and diagnostic traces.<br>**Option B (Drop)**: Eliminates header regex evaluation on log lines; relies on upstream callers not embedding headers in messages. |
| **Rule 2** | JWT-shaped strings (`eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]*`) | **No** | `PreScrubCensusContractTests.SignInFlows_AtDefaultLevels_EmitNoBearerBasicOrJwt_PreScrub` | *No output at default levels.* Full login and token issuance emits no raw JWTs in pre-scrub logs; Duende logs subject IDs and event records, not signed JWT tokens. | **Yes**: Operator overrides `Logging__LogLevel__Duende = Debug` or `Trace`. | **Option A (Keep)**: Prevents raw signed token leakage during IdentityServer diagnostics.<br>**Option B (Drop)**: Relies on default log levels never being overridden to Trace in production. |
| **Rule 3** | MongoDB URI user info (`(mongodb(?:\+srv)?://)[^@/\s]+@`) | **No** | `PreScrubCensusContractTests.ApiHost_FailedMongoConnection_EmitsNoMongoCredentials_PreScrub` | *No output at default levels.* `ConnectionStringRedactor` strips credentials before driver initialization; failed connection attempts log driver address without userinfo. | **Yes**: Operator configures `Logging__LogLevel__MongoDB = Trace` or driver exception formats connection string. | **Option A (Keep)**: Backstop against driver exception formatting.<br>**Option B (Drop)**: Relies entirely on `ConnectionStringRedactor` at startup. |
| **Rule 4** | Sensitive key=value pairs (`password=...`, `refresh_token=...`) | **No** | `DuendeLoggingCensusContractTests.FailedTokenRequest_DoesNotEmitClientSecretOrRefreshToken_AtDefaultLevels` | *No output at default levels.* Controllers do not log credentials; `AddHttpLogging` excludes request bodies; failed token requests emit client failure without secret or refresh token values. | **Yes**: Operator overrides log levels or unhandled exception formats request payload. | **Option A (Keep)**: Defensive backstop for unhandled exception formatting.<br>**Option B (Drop)**: Low likelihood under standard controller exception handling. |
| **Rule 5** | JSON-shaped `"key": "value"` pairs (`"state": "..."`, `"code_challenge": "..."`) | **No (Filtered at source)** | `DuendeLoggingCensusContractTests.AuthorizeRequest_AtDefaultLevels_EmitsNoSentinelsFromDuendeCategories_PreScrub`<br>Override tested in `DuendeLoggingCensusContractTests.AuthorizeRequest_WhenDuendeLoggingOverriddenToInformation_EmitsRawRequestJson_AndRule5ScrubsIt` | *No output at default levels.* While Duende's `AuthorizeRequestValidator` and `AuthorizeEndpoint` naturally emit raw request JSON at `Information`, they are now filtered at the source to `Warning` in `LoggingSetup.DefaultLevels`. | **Yes**: Suppressed by default. Emitted only if an operator overrides `Logging__LogLevel__Duende.IdentityServer.Validation.AuthorizeRequestValidator = Information`. | **Option A (Keep)**: Redacts `"state"`, `"code_challenge"`, and `"nonce"` if an operator enables verbose Duende logging during authentication troubleshooting.<br>**Option B (Drop)**: Source filter in `DefaultLevels` is the primary control; accepts that explicitly setting Duende validator to `Information` will log raw request JSON. |
| **Rule 6** | Query strings on URL-shaped values (`(?<=[A-Za-z0-9/._%-])\?[^\s"'<>]*=[^\s"'<>]*`) | **No** | `DuendeLoggingCensusContractTests.AuthorizeRequest_AtDefaultLevels_EmitsNoSentinelsFromDuendeCategories_PreScrub`; override tested in `TelemetryPrivacyGateTests.QueryString_RedactedWhenEmitted` | *No output at default levels.* `Microsoft.AspNetCore.Hosting.Diagnostics` logs `Request starting HTTP/1.1 GET /path?query` at `Information`, but `DefaultLevels` sets `Microsoft = Warning`, suppressing it. | **Yes**: Emitted only if an operator overrides `Logging__LogLevel__Microsoft = Information`. | **Option A (Keep)**: Prevents query parameters (OAuth `code`, `state`, OData filters) from leaking if an operator enables `Microsoft = Information` for debugging.<br>**Option B (Drop)**: Saves query string regex matching on every line; accepts that setting `Microsoft = Information` will log query strings. |
| **Rule 7** | Structured properties with sensitive keys (`IsSensitiveKey(key)`) | **No (Not observed in production codebase or pinned libraries)** | Synthetic harness only: `TelemetryPrivacyGateTests.StructuredArguments_WithSensitiveKey_AreRedacted` | *None found.* A search across `src/` and pinned dependencies identified zero calls that log structured properties matching `IsSensitiveKey`. | **No**: Dormant until code introduces a matching template property. | **Option A (Keep)**: Acts as a safeguard against developers accidentally logging `{Password}` or `{Token}` in future code.<br>**Option B (Drop)**: Removes key-checking logic; enforces sensitive parameter prevention solely via code review and architecture tests. |

**Measured Conclusion Across All 7 Rules**:
Following the addition of the source filter for Duende authorize endpoints in `DefaultLevels`, **zero rules have an active producer under default configuration**.

---

## Part 2: OpenTelemetry `BaseProcessor<LogRecord>` Capabilities & Limitations

Empirical testing against `OpenTelemetry` `1.20.1` (`OpenTelemetry.Logs.LogRecord`) established:

### What `BaseProcessor<LogRecord>` CAN Mutate

- `LogRecord.Body`: Public getter and setter (`CanWrite = true`). String bodies are rewritten via `LogScrubber.ScrubText(body)`.
- `LogRecord.FormattedMessage`: Public getter and setter (`CanWrite = true`). Formatted strings are rewritten directly.
- `LogRecord.Attributes`: Public getter and setter (`CanWrite = true`). The attribute collection is sanitized by redacting sensitive keys (`LogScrubber.IsSensitiveKey`) and scrubbing string values. Dropped attributes (such as `exception.stacktrace`) are removed.
- `LogRecord.Exception`: Public getter and setter (`CanWrite = true`). Mutating `Exception.Message` in-place is impossible because `Exception.Message` is get-only. Replacing `Exception` with `new InvalidOperationException(...)` loses the original exception type. **Resolved approach**: `RoleRadarLogScrubbingProcessor` sets `data.Exception = null`, and adds structured attributes:
  - `exception.type`: Full name of the original exception type (`data.Exception.GetType().FullName`).
  - `exception.message`: Scrubbed exception message (`LogScrubber.ScrubText(data.Exception.Message)`).
  - Stack trace is completely omitted (no `exception.stacktrace`), preventing stack frame leakage.

### Scopes Handling

- `LogRecordScope` is a readonly struct that wraps an immutable scope state object. A processor cannot mutate captured scope objects.
- In OpenTelemetry .NET, setting `IncludeScopes = true` causes any `logger.BeginScope(...)` containing sensitive data to bypass scrubbing when scopes are enumerated via `ForEachScope`.
- **Architectural Decision**: In RoleRadar, neither the API, Identity, nor web hosts require scopes for correlation (correlation is derived strictly from `Activity.Current` via W3C Trace Context). Therefore, `IncludeScopes = false` is configured across all logging setups (`LoggingSetup.cs`, `TelemetrySetup.cs`), completely closing the scope leakage vector at zero allocation cost.

---

## Part 3: Recommendation on the Four Architectural Outcomes

With the source filter added to `LoggingSetup.DefaultLevels`, all 7 rules now produce zero output at default log levels. The architectural outcomes evaluated are:

1. **"Drop `LogScrubber`" (Not Recommended as Primary Choice)**:
   - Dropping `LogScrubber` entirely would mean that whenever an operator overrides logging levels during an outage or troubleshooting (such as setting `Logging__LogLevel__Microsoft = Information` to debug routing, or `Logging__LogLevel__Duende... = Information` to debug auth failures), raw OAuth authorization codes, PKCE verifiers, state tokens, and request query strings would immediately flow unredacted into console logs and external OTLP collectors.
   - *Evidence needed to drop*: The owner would need to accept that operator overrides carry the inherent risk of logging sensitive query strings or diagnostic auth JSON, and commit to enforcing security boundaries solely at the source without a defense-in-depth scrubber at the telemetry export boundary.
2. **"Move `LogScrubber` into an OpenTelemetry Log Processor" (`Recommended for OTLP`)**:
   - `RoleRadarLogScrubbingProcessor` (`BaseProcessor<LogRecord>`) runs in the OpenTelemetry logging pipeline. It scrubs `LogRecord.Body`, `LogRecord.FormattedMessage`, `LogRecord.Attributes`, and sanitizes `LogRecord.Exception` before export over OTLP (`RR-72`, `SC-002`).
3. **"Shrink `LogScrubber`" (Owner's Decision)**:
   - Rules 1, 2, 3, and 4 have zero live producers even under normal failure scenarios. Rule 7 has zero producers in `src/`.
   - The owner could shrink `LogScrubber` to only the rules protecting operator overrides (Rule 5 for Duende JSON and Rule 6 for query strings), dropping Rules 1–4 and 7 to reduce regex matching overhead.
   - *Evidence needed to shrink*: Benchmarking proving that regex evaluation in `RoleRadarConsoleFormatter` and `RoleRadarLogScrubbingProcessor` introduces measurable latency under target throughput, balanced against the risk of unhandled third-party exception messages.
4. **"Keep `LogScrubber` as Shared Utility" (`Recommended`)**:
   - Retain `LogScrubber` in `RoleRadar.Observability` as a shared utility used by both `RoleRadarLogScrubbingProcessor` (for OTLP log export) and `RoleRadarConsoleFormatter` (for console stdout/stderr).
   - This keeps a robust defense-in-depth safety net across both local console streams and external OTLP telemetry collectors while respecting the primary rule: **secrets are filtered at the source first, and scrubbed at the sink as a backstop.**

---

## Part 4: RR-90 Measured Findings — ASP.NET Core `AddHttpLogging` Platform Feature Swap

ASP.NET Core native HTTP logging (`AddHttpLogging` with `CombineLogs = true`) was implemented to replace the hand-written `RequestCompletedMiddleware` in `RoleRadar.Api` and `RoleRadar.Identity`.

### Measured Comparison Matrix

| Property / Criterion | `RequestCompletedMiddleware` (Hand-written, Retired) | ASP.NET Core `AddHttpLogging` (`CombineLogs = true`, Platform) | Measured Finding & Status |
| :--- | :--- | :--- | :--- |
| **Event Identification** | `EventId = 6001`, `EventName = "RequestCompleted"` | `EventId = 9`, `EventName = "RequestResponseLog"` | Changed to platform standard. |
| **Category** | `RoleRadar.Api.Observability.RequestCompletedMiddleware` | `Microsoft.AspNetCore.HttpLogging.HttpLoggingMiddleware` | Enabled at default levels via `("Microsoft.AspNetCore.HttpLogging", LogLevel.Information)`. |
| **Formatted Message Shape** | Single-line: `GET /odata/Postings responded 400 in 12.345 ms` | Multi-line key-value text: `Request and Response:\r\nMethod: GET\r\nPathBase: \r\nPath: /odata/Postings\r\nStatusCode: 400\r\nDuration: 4.1109` | Formatter converts newlines: JSON writer escapes `\r\n`, Text formatter uses `OneLine(\n)`. Strictly **one line per request** in both formatters. |
| **Structured Fields (`props`)** | `props.method`, `props.path`, `props.status`, `props.durationMs` | `props.Method`, `props.Path`, `props.StatusCode`, `props.Duration` | Structured fields reach JSON record directly from `state` key-value pairs. |
| **Health Check Filtering** | Demoted `/health/*` paths to `Debug`. | Suppressed completely via `.WithHttpLogging(HttpLoggingFields.None)`. | Zero log records emitted for health probes, eliminating log flooding. |
| **Throwing Request Handling** | Logged on response completion. | `UseHttpLogging()` precedes `UseExceptionHandler()`. Exception is handled to 500, logging once with `StatusCode: 500`. | Throwing requests log exactly once with status 500 and measured duration. |
| **Credential & Query Privacy** | Query string stripped at middleware entry. | `LoggingFields = RequestMethod \| RequestPath \| ResponseStatusCode \| Duration`. | Query strings, headers, and request/response bodies are never captured; OAuth `code`/`state` and OData `$filter` never reach the log. |
| **Code Elimination** | Required two identical middleware classes across Api and Identity. | Zero custom middleware classes. Uses built-in ASP.NET Core platform feature (Principle X). | Net reduction of 82 lines of custom middleware code. |

---

## Part 5: Implementation Summary

1. **Source Filtering & OTLP Log Pipeline Registration**:
   - `LoggingSetup.DefaultLevels` sets Warning filters for `Microsoft`, `System`, `Duende.IdentityServer.Validation.AuthorizeRequestValidator`, and `Duende.IdentityServer.Endpoints.AuthorizeEndpoint`.
   - Registered in `.NET` via `ILoggingBuilder.AddOpenTelemetry` in `RoleRadar.Observability/LoggingSetup.cs` and `TelemetrySetup.cs`.
   - Enabled conditionally when `OTEL_EXPORTER_OTLP_ENDPOINT` is configured (`SC-003`).
   - Registers `RoleRadarLogScrubbingProcessor` to ensure zero unredacted secrets leave the process over OTLP.
   - Sets `IncludeFormattedMessage = true` and `IncludeScopes = false`.
2. **Web OTLP Log Pipeline (`Node 22`)**:
   - Uses `@opentelemetry/sdk-logs` `3.0.0` and `@opentelemetry/exporter-logs-otlp-http` `0.300.0`. Direct dependency on `@opentelemetry/api-logs` is dropped.
   - Bridges `web/app/lib/log.server.ts` to emit OTLP log records correlated with the active span (`traceId` and `spanId`) and scrubbed per `web/app/lib/observability.ts`.
3. **HTTP Logging Adoption**:
   - `RequestCompletedMiddleware` is removed from both `RoleRadar.Api` and `RoleRadar.Identity`.
   - ASP.NET Core `AddHttpLogging` configured with `CombineLogs = true` and selective `LoggingFields`. Health endpoints excluded via `.WithHttpLogging(HttpLoggingFields.None)`.

---

## Consequences

- **Good**: Secrets and raw OAuth parameters are filtered at the source; zero credentials or query strings are logged under default configuration.
- **Good**: `LogScrubber` functions as a robust defense-in-depth backstop if log levels are temporarily raised.
- **Good**: Zero credentials or query strings leak over OTLP logs or console streams (`SC-002`).
- **Good**: Log records carry correlated `TraceId` and `SpanId` whenever an active activity or span exists (`SC-004`).
- **Good**: Unscrubbed scopes leak vector is eliminated by setting `IncludeScopes = false`.
- **Good**: Net reduction of hand-written middleware by utilizing ASP.NET Core HTTP logging (Principle X).
- **Good**: Protocol hygiene preserved on `stdout` in `Mcp.Stdio` (`FR-017`).
