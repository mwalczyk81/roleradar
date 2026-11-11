# Contract: Telemetry Privacy Gate (`RR-72`)

**Feature**: `015-observability` (`RR-72`, `FR-004`, `SC-002`, `SC-003`)
**Principle**: Constitution Principle I (Privacy First) & Principle V (Untrusted Input)

## 1. Delivery Gate Rule

- The Telemetry Privacy Gate (`RR-72`) ships **with the first telemetry exporter in Gate 2 (Tracing)** and is extended in **Gate 3 (Logs)** and **Gate 4 (Metrics)**. No exporter code may merge without its corresponding `RR-72` automated tests failing first and passing in the same PR.

## 2. Sentinel Corpus (Must Never Appear in Exported Spans, Logs, or Metrics)

Every privacy gate test suite (.NET `TelemetryPrivacyGateTests` using `OpenTelemetry.Exporter.InMemory` / loopback OTLP test receiver, and Web `telemetry-privacy.test.ts` using in-memory OTel exporters) MUST inject the following sentinel payloads into traced requests, log events, exceptions, and datastore operations, and assert that **zero** exported telemetry items contain any sentinel substring:

| Rule # | Category | Sentinel Input Shape | Expected in Exported Telemetry |
|---|---|---|---|
| 1 | Bearer & Basic credentials | `Authorization: Bearer SENTINEL-BEARER-TOKEN-991`, `Basic SENTINEL-BASIC-CRED-992` | Absent or `Bearer [redacted]` / `Basic [redacted]` |
| 2 | JWT-shaped strings | `eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJTRU5USU5FTC1KV1QtOTkzIn0.sig` | Absent or `[redacted]` |
| 3 | MongoDB URI credentials | `mongodb://admin:SENTINEL-MONGO-PW-994@localhost:27017` | Absent or `mongodb://[redacted]@localhost:27017` |
| 4 | Sensitive `key=value` pairs in free text | `refresh_token=SENTINEL-REFRESH-995 password=SENTINEL-PW-996 otp=654321` | Absent or `<key>=[redacted]` |
| 5 | Sensitive JSON pairs in free text | `{"client_secret":"SENTINEL-JSON-SECRET-997","code":"SENTINEL-AUTH-CODE-998"}` | Absent or `"<key>":"[redacted]"` |
| 6 | URL query strings | `https://localhost:5003/auth/callback?code=SENTINEL-QUERY-CODE-999&state=SENTINEL-STATE-999` | Query stripped (`url.query` removed, `url.full` query stripped) |
| 7 | Sensitive structured property / attribute keys | Keys matching `password`, `passphrase`, `secret`, `token`, `authorization`, `cookie`, `otp`, `totp`, `code`, `body`, `content`, `appendedtext`, `notebody`, `state`, `nonce`, `codechallenge`, `codeverifier` | Attribute value replaced with `[redacted]` or dropped |
| 8 | Exception messages & stack traces | `new InvalidOperationException("Auth failed for password=SENTINEL-EX-PW-1000 Bearer SENTINEL-EX-BEARER-1001")` | On **Spans** (`ADR 0050`): `exception.message`, `exception.stacktrace`, and free-text `StatusDescription` are dropped/cleared (keeping only `exception.type` / `error.type`). On **Logs** (`ADR 0051`): `exception.message` and `exception.stacktrace` are scrubbed before OTLP export. |
| 9 | Untrusted domain text & private fields | Note `content` (`SENTINEL-NOTE-BODY-1002`), criterion `ruleText` (`SENTINEL-RULE-TEXT-1003`), posting description (`SENTINEL-POSTING-DESC-1004`), OData `$filter` literal (`SENTINEL-FILTER-LITERAL-1005`) | Never present in HTTP span attributes, MongoDB `db.query.text` (`QueryTextMaxLength = 0` + dropped in processor), log records, or metric labels |
| 10 | High-cardinality / identity metric labels | `owner_id`, entity GUIDs, `sub`, `username`, `traceId` | Never present in any metric data point attribute |

## 3. Processor Enforcement Mechanism

1. **`RoleRadarSpanAttributeProcessor` (.NET `BaseProcessor<Activity>`) & `WebSpanAttributeProcessor` (Node `SpanProcessor`) — Gate 2 (`ADR 0050`, Independent of `LogScrubber`)**:
   - Runs synchronously in `OnEnd` before any span reaches an exporter, with **zero dependency on `LogScrubber`** so Gate 2 stands completely independent of `RR-86`'s outcome in Gate 3.
   - **Drop-list**: Explicitly removes `url.query`, all request and response header attributes (`http.request.header.*`, `http.response.header.*`), and `db.query.text` / `db.statement`.
   - **URL sanitization**: Strips the `?query` and `#fragment` portion from `url.full` (and legacy `http.url` / `http.target` if present).
   - **Allowlist**: Retains only allowlisted operational attributes (`http.request.method`, `http.request.method_original`, `http.response.status_code`, `http.route`, `url.scheme`, `url.path`, `url.full` [query/fragment stripped], `server.address`, `server.port`, `network.protocol.version`, `network.peer.address`, `network.transport`, `error.type`, `db.system.name`, `db.operation.name`, `db.operation.summary`, `db.command.name`, `db.namespace`, `db.collection.name`, `db.query.summary`, `db.response.status_code`, `db.mongodb.lsid`, `db.mongodb.txn_number`, `db.mongodb.server_connection_id`, `db.mongodb.driver_connection_id`, `db.mongodb.cursor_id`); drops all other attributes.
   - **Span exception events & status description**: For any `exception` event recorded on a span, retains only `exception.type` (and populates `error.type` on the span if unset) while removing `exception.message` and `exception.stacktrace`; clears any free-text `StatusDescription` (`null`) on the span.
2. **`RoleRadarLogScrubbingProcessor` (.NET `BaseProcessor<LogRecord>`) & Web OTLP Log Emitter — Gate 3 (`ADR 0051`, `RR-86`)**:
   - Ensures `LogRecord.Body` / `FormattedMessage`, all `LogRecord.Attributes`, and `LogRecord.Exception` strings exported over OTLP are scrubbed per the Gate 3 (`ADR 0051`) decision.
