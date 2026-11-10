# Contract: Correlation Header and Error Envelope

> **Superseded in part by [ADR 0044](../../../docs/adr/0044-problem-details-and-trace-context.md) (feature 014).** The correlation header is replaced by W3C `traceparent` and the bare trace id, and the error envelope by RFC 9457 problem documents: see `specs/014-standards-first/contracts/trace-context.md` and `problem-details.md`. The text below is the 013 contract as shipped.

Applies to the web app, the API and the Identity service. Decisions: research R-004, R-007.

## `X-Correlation-ID`

| Rule | Value |
| --- | --- |
| Header name | `X-Correlation-ID` (case-insensitive on input) |
| Accepted inbound value | `^[A-Za-z0-9][A-Za-z0-9._-]{7,63}$` (8 to 64 characters) |
| Inbound value that does not match | Discarded, never logged, replaced by a generated id |
| Generated value | Node: `crypto.randomUUID()`; .NET: `Guid.CreateVersion7().ToString("D")` |
| Response | Every response from web, API and Identity carries the header, including static files, health endpoints, redirects and errors |
| Propagation | web to API (all calls); web to Identity (server-side discovery, code grant, refresh). Browser-to-Identity navigations get Identity's own id. |

The same id is the `correlationId` field on every log record written while handling the request ([log-records.md](log-records.md)) and the `request_correlation_id` of every audit entry the request writes ([../data-model.md](../data-model.md)).

## Error envelope (API, additive change)

```json
{
  "ok": false,
  "errorCode": "service_unavailable",
  "message": "The data store is not reachable. Try again shortly.",
  "details": null,
  "correlationId": "01970f3e-5b1c-7d2a-9a77-3c2f1e0d4b5a"
}
```

- New field `correlationId`: string, present on every envelope the API returns, equal to the response's `X-Correlation-ID`.
- Existing fields and codes are unchanged. `ErrorFieldDetail` stays `{ field, issue }`.
- The OpenAPI document and `contracts/client/api-types.ts` are regenerated (AGENTS.md command). `EnumContractRegistry` is not involved: `errorCode` stays a string.

## Status and code mapping

| Situation | Status | `errorCode` | Today |
| --- | --- | --- | --- |
| Validation failure | 400 | `validation_error` | unchanged |
| Not found | 404 | `not_found` | unchanged |
| Stale version token | 409 | `concurrency_conflict` | unchanged |
| Data store unreachable (readiness probe or driver connectivity failure) | 503 | `service_unavailable` | 400 `validation_error` (writes); 500 without envelope (reads) |
| Any other unhandled exception | 500 | `internal_error` | 500 without envelope |

- 503 responses arrive within 5 s of the request (FR-024) and carry `Retry-After: 5`.
- 500 and 503 bodies never contain exception text, stack traces, connection details or remediation commands; those go to the scrubbed log only.
- OData reads (`/odata/*`) use the same envelope for 503 and 500. OData query errors stay 400 through `ODataExceptionFilter`, now with `correlationId`.

## Web behavior

- The web always uses its own request id; the API echoes the same value because the web sent it.
- No page shows the id. Behavior for 400, 401, 403, 404 and 409 is unchanged; a 503 falls through to the existing error boundary.
