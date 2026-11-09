# Contract: Log Records and Security Events

Applies to the web app, the API, Identity, the CLI and the MCP host. Decisions: research R-001, R-003, R-008, R-012.

## Format switch

`ROLERADAR_LOG_FORMAT` = `json` | `text`. Unset means `text`. The shipped Dockerfiles (`Dockerfile`, `deploy/Dockerfile.api`, `deploy/Dockerfile.identity`, `deploy/Dockerfile.web`) set `json`. Levels use the standard `Logging__LogLevel__<Category>` variables (.NET), `ROLERADAR_LOG_LEVEL` (web), and the CLI's existing `--verbose` / `ROLERADAR_LOG_LEVEL`.

## JSON record (one object per line)

| Field | Type | Always | Notes |
| --- | --- | --- | --- |
| `ts` | string | yes | ISO 8601 UTC with milliseconds and `Z` |
| `level` | string | yes | `trace`, `debug`, `info`, `warn`, `error`, `fatal` |
| `service` | string | yes | `web`, `api`, `identity`, `cli`, `mcp` |
| `msg` | string | yes | rendered message, scrubbed |
| `category` | string | .NET only | logger category |
| `event` | string | no | event name, for example `SignInFailed`, `RequestCompleted` |
| `eventId` | number | no | .NET event id |
| `correlationId` | string | inside a request | see [http-correlation-and-errors.md](http-correlation-and-errors.md); the CLI uses a run id, MCP a per-call id |
| `props` | object | no | structured properties, scrubbed; denylisted keys carry `"[redacted]"` |
| `error` | object | no | `{ "type", "message", "stack" }`, each scrubbed |

`text` mode prints the same content on one line: `ts level service [correlationId] msg key=value ...`.

## Output streams

- API, Identity, web: stdout for `info` and below, stderr for `warn` and above (compose shows both).
- CLI and MCP: stderr only, as today. stdout stays reserved for command output and MCP frames.

## Request record

One `RequestCompleted` record per request in web, API and Identity, with `props` = `method`, `path` (no query string), `status`, `durationMs`. Health endpoints log at Debug. Headers, cookies, bodies and query strings are never logged.

## Never logged (FR-012)

Access, refresh and identity tokens; `Authorization` and `Cookie` header values; passwords and backup passphrases; one-time codes and TOTP secrets; client secrets and the session key; credentials in connection strings; note bodies and appended note text; OIDC `code` and `state` values; the username typed on a failed sign-in.

## Security events (Identity, logs only)

| `eventId` | `event` | Level | `props` |
| --- | --- | --- | --- |
| 5001 | `SignInSucceeded` | info | `subject` |
| 5002 | `SignInFailed` | warn | `subject` only when the username matched an account; otherwise none |
| 5003 | `AccountLockedOut` | warn | `subject` |
| 5004 | `OneTimeCodeFailed` | warn | `subject` |

Each carries the request's `correlationId`. Exactly one event per outcome. A failed attempt that also triggers lockout writes `SignInFailed` (or `OneTimeCodeFailed`) and then `AccountLockedOut`.
