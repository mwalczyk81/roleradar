# ADR 0041: Structured Logs, One Correlation Id, and Health That Checks Its Store

- **Status**: Accepted (2026-11-09; the viewer is pinned to `amir20/dozzle:v11.3.5@sha256:dd09e6aaa9d5ad686b6017427a502c40dc862ba28445609813e5ea1f66f6dbdf`)
- **Date**: 2026-11-09
- **Feature**: `013-operability`

## Context and Problem Statement

Before the repository is announced, the running stack has to explain itself. Today:

- The API, Identity, CLI and MCP write free text through the default console provider.
- The web prints with `console.*`, and one informational line goes out as an error.
- Nothing ties a web request to the API and Identity lines it caused.
- API writes are attributed to the constant `api-user`.
- Sign-in outcomes leave no trace.
- Both health endpoints answer "healthy" without checking a store.
- A stopped database returns 400 for writes and a bare 500 for reads.

Feature 013 specifies the outcomes. This ADR records the library and tooling choices the spec left to the plan. Full alternatives are in `specs/013-operability/research.md`.

## Decision Outcome

- **Logging (.NET): built-in console provider plus one custom formatter.** `RoleRadarConsoleFormatter` writes either one JSON object per line or one readable line, selected by `ROLERADAR_LOG_FORMAT`. Every field is scrubbed (bearer and basic credentials, JWT shapes, connection-string credentials, denylisted property keys and the same values in the rendered message, sensitive `key=value` pairs and JSON pairs in free text, query strings). No Serilog and no logging library. The one package this adds is `Microsoft.Extensions.Logging.Console` 10.0.14 (see Consequences).
- **A new source project, `RoleRadar.Observability`,** holds the formatter, scrubber, correlation id rules, logging setup and sign-in event definitions. It references no RoleRadar project, and Domain, Application and Infrastructure must not reference it. It exists because Identity references no other RoleRadar project and the redaction logic must have one copy.
- **Logging (web): a dependency-free logger** with the same record shape. The pure part is unit- and mutation-tested. `no-console` is enforced by lint.
- **Correlation: `X-Correlation-ID`.**
  - Accepted when it matches `^[A-Za-z0-9][A-Za-z0-9._-]{7,63}$`, otherwise replaced.
  - Created or accepted at the web edge and sent to the API and to Identity's server-side endpoints.
  - Returned on every response and added to the API error envelope as `correlationId`.
  - Stamped on audit entries as `request_correlation_id`. The existing `details.after.correlation_id` (ADR 0033) keeps its meaning.
  - This is not tracing: no `traceparent`, no spans, no exporter.
- **`IRequestContext`**, a small Application port that exposes the current correlation id, is accepted as a named seam under Principle IV. It is read only at the two audit choke points and for log scopes.
- **Real actors.** API writes record the `sub` claim. The `Actor` defaults are removed from command records, so a missing actor is a compile error. Handlers no longer fall back to a default when given a blank actor; they return a `ValidationError` result instead, never an exception.
- **Sign-in events** (`SignInSucceeded`, `SignInFailed`, `AccountLockedOut`, `OneTimeCodeFailed`) are written to the logs only. They carry the subject only when an account matched and never the typed username. Duende's `UserLoginFailureEvent` is not used because it carries the username.
- **Health.** The built-in ASP.NET health checks serve `/health/live` and `/health/ready` (store reachable within 2 s), with `/health` kept as an alias of live. The images' `--healthcheck` default moves to readiness, so no compose healthcheck line changes.
- **Outages.**
  - `DatastoreOffline` maps to 503 `service_unavailable`.
  - An `IExceptionHandler` maps driver connectivity failures, classified in Infrastructure, to the same 503.
  - Every other unhandled exception becomes 500 `internal_error`, with no exception text in the body.
  - The driver reconnects on its own, so nothing keeps an "offline" state.
- **Web outbound policy, written in-house.** Per-attempt timeout of 5 s for reads and 10 s for writes. Only `GET` and `HEAD` are retried, at most twice, on network error, timeout, 502, 503 or 504. No retry library.
- **Log retention and viewing.**
  - Every service in the real compose files is capped at `json-file` 10 MB × 3.
  - The optional viewer is Dozzle, in its own file `deploy/docker-compose.logs.yml`: pinned by tag and digest (`amir20/dozzle:v11.3.5@sha256:dd09e6aaa9d5ad686b6017427a502c40dc862ba28445609813e5ea1f66f6dbdf`, as in `deploy/docker-compose.logs.yml`), bound to `127.0.0.1:5004`, read-only Docker socket, analytics, release checks, registry checks and avatar fetches all disabled, hardened like the app services, with its own compose project name.
  - The demo stack never includes the viewer, and its compose file is unchanged.

## Consequences

- **Good**:
  - One search for an id finds the matching lines in every service.
  - Secrets are kept out by both discipline and a scrubber, and a test with sentinels proves it.
  - The audit trail names who actually made each change and can be read with `roleradar audit list`.
  - Health and 503s report what is really happening, and the stack recovers without a restart.
  - No new npm package, and one new NuGet entry: `Microsoft.Extensions.Logging.Console` 10.0.14, a direct reference of `RoleRadar.Observability`. That project is a class library and does not get the console provider from the ASP.NET shared framework, as the web hosts do. It is a first-party Microsoft package already resolved transitively, and it is not a logging framework, retry, resilience or OpenTelemetry package. No retry, resilience, tracing or exporter package was added anywhere.
- **Bad**:
  - Another source project and its architecture rules.
  - A wide, mechanical edit to remove `Actor` defaults.
  - Sign-in history lasts only as long as the capped container logs.
  - **Dozzle's read access to the Docker socket is effectively host-level Docker access for that container.** This is accepted only because the viewer is opt-in, loopback-only and runs on the owner's own machine. Exposing it beyond loopback would require a socket proxy and a new decision.

## Known limits and follow-ups

Each is tracked in Jira project RR.

- The web client ignores the API's `Retry-After` header on a 503 and uses its own fixed backoff (RR-79).
- The Mongo client uses a polling server monitor and has the driver's read retry turned off, because the OData read path materialises with a `ToList()` that cannot take a cancellation token, so a request deadline cannot cut it short (RR-73).
- Audit entries written by `tracker-import` carry no `request_correlation_id`, because that import's factory is a singleton and cannot see the current request context (RR-65).
- `roleradar audit list` without a filter sorts the entries in memory (RR-65).
- The correlation id is a custom `X-Correlation-ID` header and the error body is a custom envelope. Both are queued to move to the W3C trace context and ProblemDetails (RR-78, RR-74).
