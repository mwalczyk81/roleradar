# Contract: W3C Trace Context as the correlation id

Replaces the correlation half of `specs/013-operability/contracts/http-correlation-and-errors.md` and the correlation section of ADR 0041. Decisions: research R-008, R-009.

## The id

The trace id of the request, as defined by W3C Trace Context: 32 lowercase hexadecimal characters, not all zero. It is the correlation id everywhere. There is no second id and no custom header.

## Header

| Rule | Value |
| --- | --- |
| Inbound | `traceparent` (and nothing else). A valid value is honoured: its trace id is the request's. A malformed, all-zero or upper-case value is discarded, never logged, and replaced by a generated trace id. |
| Outbound | The web sends `traceparent` on every server-side call to the API and to Identity (discovery, code grant, refresh). The .NET services propagate it on their own calls through `HttpClient`. |
| `tracestate` | Not used and not forwarded. |
| `X-Correlation-ID` | Gone. Not read, not written, not returned. An inbound one is ignored unless an ADR names a client that needs it (none today). |
| Response | No header returns the trace id. The id reaches a client in the problem document of an error ([problem-details.md](problem-details.md)). Browser navigations to Identity get Identity's own trace. |

## Per host

| Host | Source of the id | Where it is used |
| --- | --- | --- |
| Web | A valid inbound `traceparent`, otherwise ids generated at the request edge (`@opentelemetry/api` and `@opentelemetry/core`, ids from `node:crypto`), held in the existing per-request store | Log field `traceId`; the outbound `traceparent`; the warn record on a final 503 |
| API | `Activity.Current.TraceId` (ASP.NET Core extracts `traceparent` itself) | Log field `traceId`; the problem document `traceId`; `request_correlation_id` on every audit entry the request writes; the API actor and audit context |
| Identity | `Activity.Current.TraceId` | Log field `traceId`; the sign-in events 5001 to 5004 |
| CLI | `ActivityTraceId.CreateRandom()` once per run | The run id in logs; `request_correlation_id` on audit entries the run writes |
| MCP stdio | `ActivityTraceId.CreateRandom()` per tool call | The per-call id in logs (stderr only, Principle VI); audit entries the call writes |

A request without any trace context (a direct call, a health probe, a CLI run) always gets a generated trace id, so no log line or audit entry is ever without one.

## Log records

- The JSON field that carried `correlationId` is now `traceId` (the OpenTelemetry name for the same value, so feature 015 does not rename it again). In text mode the bracketed correlation id shows the trace id.
- The .NET console formatter reads `Activity.Current` instead of a logging scope key. The web logger reads the per-request store.
- Records written with no active request (startup, shutdown) carry no `traceId`, as before.
- The redaction rules are unchanged. A trace id is not a secret.

## Audit

- `request_correlation_id` keeps its stored field name and now holds the trace id for new entries. Old entries keep their stored values and remain listable through `audit list`.
- `details.after.correlation_id` (ADR 0033, a posting event to application event link) is a different concept and is untouched.

## What is deleted

`CorrelationMiddleware` (API and Identity), `CorrelationId` and its header constant in `RoleRadar.Observability`, the logging scope key, the web's `acceptCorrelationId`, `newCorrelationId`, `CORRELATION_HEADER` and `withCorrelationHeader`, and the 013 tests that assert the header (rewritten for `traceparent` and the trace id; FR-003 allows this because the story names the change).

## Feature 015 (OpenTelemetry)

Feature 015 adds the OpenTelemetry SDK, the HTTP and undici instrumentations and any exporter. It builds on `@opentelemetry/api`, which the web already uses here. The instrumentations create the server span and inject `traceparent` themselves, so 015 deletes the manual root span context and explicit injection written in this feature. The trace id format, the `traceId` log field, the problem document member and the audit value do not change.

## Tests (written first)

- A valid inbound `traceparent` produces that trace id in the logs, the error body and the audit entry; a malformed one produces a generated id and no log line carrying the bad value.
- One web page request that fails in the API yields the same `traceId` in the web, API and Identity log lines, in the problem document and in the audit entry of a write (SC-006).
- CLI and MCP writes carry a 32-hex id.
- Old audit entries are still listed with their stored values.
- No `X-Correlation-ID` string remains in source (SC-007).
