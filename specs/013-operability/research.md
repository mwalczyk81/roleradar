# Research: Operability (013)

Facts were verified against the working tree on 2026-11-09. Each item records the decision, the reason and the alternatives that were rejected. ADR 0041 summarizes R-001 to R-006.

## Baseline facts that shape the design

- No .NET host has logging configuration, and no `appsettings*.json` exists in any project. The API and Identity use the `WebApplication` default providers (console, debug, eventsource). The CLI and MCP clear the providers and add console output to stderr. The CLI picks its level from `--verbose`, then `ROLERADAR_LOG_LEVEL`, and defaults to Warning (`src/RoleRadar.Cli/Program.cs:80-153`).
- No logging package exists: no Serilog and no JSON console formatter. Central package management and lock files are enforced. All Microsoft.Extensions and ASP.NET packages are pinned at 10.0.14, and `Microsoft.Extensions.Logging.Console` comes in transitively.
- `RoleRadar.Identity` references no other RoleRadar project. `RoleRadar.Infrastructure` has no ASP.NET reference. The CLI references Identity.
- The API has no exception handler. When Mongo is down:
  - Writes return `DatastoreOffline` from the decorator's readiness probe, and `ErrorEnvelope.FromResultError` maps that to **400 `validation_error`**.
  - Reads throw `TimeoutException` or `MongoConnectionException`, which surface as **500 with no envelope**.
  - `MongoOptions.ReadinessTimeout` (3 s) sets both `ServerSelectionTimeout` and `ConnectTimeout`. The probe clamps the timeout to at most 4.5 s.
- Audit lives in one collection, `audit_log`, with indexes `(owner_id, target_note_id, occurred_at, _id)` and `(owner_id, occurred_at desc, _id)`. The only reader is `INoteRepository.GetAuditEntriesAsync`. It sorts ascending, cannot read `details` back, and has no query handler.
- `Actor` is a parameter on each command record, with defaults `mcp-stdio`, `agent` and `system`. The API passes the constant `api-user`. `HttpUserOwnerIdProvider` checks `sub` against the allowlist and then discards it.
- The Identity login page (`Pages/Account/Login.cshtml.cs`) handles both the password step and the TOTP step. It logs nothing. Lockout is 5 attempts for 15 minutes. Duende events are switched on, but no `IEventSink` is registered.
- The web server is plain `node:https` around `createRequestListener` (`web/server.ts`). It already wraps each request in an `AsyncLocalStorage` holding the CSP nonce. Only `server.ts:230` prints an informational line through `console.error`. The `insights-*` loaders have a 5 s timeout; every other loader and action has neither a timeout nor a retry.
- The web talks to Identity server-side through `openid-client` (discovery, code grant, refresh). The browser reaches the Identity login pages directly, not through the web server.
- No compose file has a `logging:` section. Every healthcheck hits a static `/health`.
- `DedicatedMongoFixture.StopAsync()` exists and `StoppedMongoReadinessIntegrationTests` uses it, but only for MCP and the CLI. No restart helper exists.
- `web/app/lib/api-client.server.ts` `mapApiError` reads `details[].message`, but the contract field is `issue`. This is an existing defect. It is out of scope here (spec: no behavior change) and is raised as a follow-up card.

## R-001: .NET logging library

**Decision**: Use the built-in `Microsoft.Extensions.Logging` console provider with one custom `ConsoleFormatter`, `RoleRadarConsoleFormatter`. It has two modes:
- `json`: one object per line, using the shared record shape in [contracts/log-records.md](contracts/log-records.md).
- `text`: one human-readable line.

`ROLERADAR_LOG_FORMAT` (`json` or `text`) selects the mode. It defaults to `text`, and the shipped Dockerfiles set it to `json`. Every record passes through the scrubber (R-003) before it is written. Levels come from the standard `Logging__LogLevel__*` environment configuration. The code defaults are Information for the app, Warning for `Microsoft.*` and `System.*`, and Information for `Microsoft.Hosting.Lifetime`.

**Rationale**: No new package is needed (the provider is already in the lock files). Scopes give the correlation id to every record inside a request. Because the formatter is a custom one, the API, Identity, CLI, MCP and web records all share the same field names, so a single search for the id finds them all. Redaction also sits in the one place every record passes through.

**Alternatives rejected**:
- *Serilog (with `Serilog.AspNetCore` and `Serilog.Formatting.Compact`)*: a mature library, but it adds three or more pinned packages and a second logging pipeline, and redaction would still need custom code (a destructuring policy plus a message scrubber).
- *Built-in `AddJsonConsole` unchanged*: it puts the correlation id inside a `Scopes` array and has no service field. Each host would end up with a different shape, and there is no place to scrub.
- *`Microsoft.Extensions.Compliance.Redaction`*: it redacts only properties classified with data-classification attributes. It does nothing about free text in exception messages, and it is a new package family.

## R-002: Shared home for the .NET logging code

**Decision**: Add one small source project, `src/RoleRadar.Observability`. It holds the formatter, the scrubber, the correlation id rules (`CorrelationId.TryAccept`, `CorrelationId.New`, the header name), the `AddRoleRadarLogging(service)` setup extension and the sign-in event definitions.
- Its only dependency is `Microsoft.Extensions.Logging.Console`, with no ASP.NET reference.
- It references no other RoleRadar project.
- All four hosts reference it.
- Domain, Application and Infrastructure must not reference it, and architecture tests pin this.
- The two HTTP hosts each get a correlation middleware of about 40 lines, built on the shared rules.

**Rationale**: Identity cannot reference Infrastructure. That would pull Mongo into the identity host and break the "only Identity references Duende" boundary in the other direction, because the CLI references Identity. Duplicating the redaction logic in four hosts would let them drift apart, which is the exact failure the redaction test exists to catch.

**Alternatives rejected**:
- *Put it in Infrastructure*: Identity cannot use it.
- *Link source files into each host*: invisible coupling, and the four copies each compile under different analyzers.
- *Give Observability an ASP.NET framework reference so the middleware lives there too*: the MCP image would then need the ASP.NET runtime for code it never runs.

## R-003: Redaction

**Decision**: There are two layers, and the test proves the outcome rather than the mechanism.

1. **Do not log the value.** Request logs record method, route path without the query string, status and duration. They never record headers, cookies, bodies or query strings. Validation and command logs record field names and error codes, never values. Sign-in events carry no username.
2. **Scrub every record** in `RoleRadarConsoleFormatter` (.NET) and `formatRecord` (web, R-008):
   - Bearer and Basic credentials (`(Bearer|Basic)\s+\S+`).
   - JWT-shaped strings (`eyJ[\w-]+\.[\w-]+\.[\w-]*`).
   - Userinfo in `mongodb://` and `mongodb+srv://` URIs, using the same rule as `ConnectionStringRedactor`, with a shared test vector set so the two cannot drift.
   - Values of structured properties whose key matches a denylist: `password`, `passphrase`, `secret`, `token`, `authorization`, `cookie`, `set-cookie`, `otp`, `totp`, `code`, `body`, `content`, `appendedText`, `noteBody`, and (added during implementation for OIDC) `state`, `nonce`, `code_challenge`, `code_verifier`. Matching is case-insensitive. The same values are also removed from the rendered message when they were passed as template arguments.
   - Query strings on any URL-shaped value.
   - Sensitive `key=value` pairs in free text (for example `refresh_token=...` inside an exception message). Added during implementation (T021).
   - JSON-shaped `"key": "value"` pairs whose key is sensitive. Added during implementation (T013): Duende logs the raw authorize request (including `state` and `code_challenge`) as JSON text inside its message.

**Test (FR-013)**: Sentinel values that are unique, fictional and secret-shaped are pushed through real paths:
- A password, a TOTP code and a TOTP secret through Identity sign-in.
- A bearer token and a refresh token through API and web calls.
- A connection string with credentials through a forced Mongo failure.
- A sentinel note body through note create and append, including a failing validation.
- The client secret and the session key at web startup.

Each host's output is captured through the production formatter. The test asserts that no sentinel appears. A separate case for each rule feeds that one secret through the formatter directly, so removing any single rule makes exactly one case fail (SC-003).

**Alternatives rejected**:
- *Scrub only* (pattern matching cannot recognize a note body).
- *Discipline only* (one stray `LogInformation("{Request}", request)` leaks everything).

## R-004: Correlation id

**Decision**:
- Header: `X-Correlation-ID`.
- Accepted values: 8 to 64 characters matching `^[A-Za-z0-9][A-Za-z0-9._-]{7,63}$`. Anything else is discarded without being logged and replaced with a new id.
- Generated values: `crypto.randomUUID()` in Node, `Guid.CreateVersion7()` in .NET.

The id is not an entity identifier, so Principle IX does not apply. The .NET hosts still use v7 for consistency.

Propagation:
- **web → API**: `createApiClient` and `fetchOData` set the header from the request's `AsyncLocalStorage`.
- **web → Identity (server-side)**: `openid-client`'s `customFetch` adds the header to discovery, code-grant and refresh calls.
- **browser → Identity (login pages)**: there is no web hop, so Identity accepts or generates the id itself. Sign-in events carry Identity's id. The web's id is carried on the following token exchange. This is documented, not hidden.
- **API → Mongo**: no propagation (out of scope: tracing).

Every web, API and Identity response returns the header. The API error envelope carries the same value as `correlationId`.

**Naming**: header `X-Correlation-ID`, envelope `correlationId`, log field `correlationId`, audit BSON field `request_correlation_id`. None of these reuses the existing audit `details.after.correlation_id`, which links a posting transition to its application event (FR-008).

**Alternatives rejected**:
- *W3C `traceparent` and `Activity` trace ids*: that is the tracing model, which is out of scope. Adopting its format now would suggest tracing that does not exist.
- *ASP.NET `HttpContext.TraceIdentifier`*: the built-in hosting scope captures it before middleware runs, and it cannot be accepted from upstream.

## R-005: Log viewer

**Decision**: Use [Dozzle](https://github.com/amir20/dozzle) (MIT) in a separate file, `deploy/docker-compose.logs.yml`, started deliberately with `docker compose -f deploy/docker-compose.logs.yml up -d`.
- The port is bound to `127.0.0.1:5004` only. The real stack uses 5001 to 5003 and the demo uses 5101 to 5103.
- The Docker socket is mounted read-only.
- `DOZZLE_NO_ANALYTICS=true` turns off Dozzle's default anonymous usage reporting, as FR-028 requires. Implementation (v11.3.5) also turns off the other outbound calls: `DOZZLE_RELEASE_CHECK_MODE=manual`, `DOZZLE_IMAGE_CHECK_MODE=off`, `DOZZLE_DISABLE_AVATARS=true`.
- A label filter limits it to the RoleRadar compose project.
- The container is hardened like the app services (read-only filesystem, `cap_drop: [ALL]`, `no-new-privileges`). Its /data directory is a tmpfs so the read-only filesystem holds, it has its own compose project name (`roleradar-logs`), and its healthcheck is `/dozzle healthcheck`.
- The image is pinned by exact tag and digest, using the latest stable release that is at least two weeks old at implementation time. The Dependabot docker-compose entry is extended to cover the file.
- The demo compose file never references it, and a tooling test enforces this.

**Rationale**: Dozzle reads the same container output as `docker compose logs`, so the apps need no extra log sink. Nothing leaves the machine, and it parses JSON lines into fields. It is one container and is off by default.

**Risk recorded in ADR 0041**: Read access to the Docker socket is effectively host-level Docker access for that container. This is accepted for an opt-in, loopback-only tool on the owner's own machine, where the owner already has Docker access. The ADR states it plainly.

**Alternatives rejected**:
- *Seq*: needs an app-side HTTP sink and is proprietary.
- *Grafana with Loki and Promtail*: three services and a collector, which is out of scope.
- *Dozzle behind a Docker socket proxy*: a second image and more configuration for an optional local tool. This is revisited if the viewer is ever exposed beyond loopback, which is not planned.

## R-006: Liveness and readiness

**Decision**: Use the built-in ASP.NET health checks (`AddHealthChecks` and `MapHealthChecks` with tag predicates). They are in the shared framework, so no package is needed.
- `/health/live`: no dependency checks.
- `/health/ready`:
  - API: the existing readiness probe through `INoteRepository.CheckReadinessAsync` (`hello`, writable primary).
  - Identity: `IdentityAppDbContext.Database.CanConnectAsync`.
  - Each check is bounded at 2 s.
- Responses are minimal: `{"status":"Healthy"|"Unhealthy"}`, with no endpoint or remediation metadata, and they are anonymous.
- `/health` stays as an alias of liveness, so nothing that calls it breaks.
- The images' `--healthcheck` default URL moves to `/health/ready`, so compose (real and, as a side effect, demo) uses readiness without changing any compose healthcheck line.

**Rationale**: Changing the image default puts the readiness rule in one place and leaves the demo compose file untouched (FR-029). The demo already waits for its Mongo to be healthy, so readiness only makes that wait true.

**Alternatives rejected**:
- *Edit each compose healthcheck to pass a URL*: touches the demo file.
- *The `AspNetCore.HealthChecks.MongoDb` package*: a new dependency for a probe that already exists.

## R-007: Bounded 503 and self-recovery

**Decision**:
- `ErrorEnvelope.FromResultError` maps `DatastoreOffline` to **503 `service_unavailable`**.
- A new `IExceptionHandler`, registered with `UseExceptionHandler`, maps exceptions that `Infrastructure`'s `DatastoreFailure.IsUnavailable(Exception)` classifies as unavailable to the same 503 envelope:
  - `MongoConnectionException`.
  - `TimeoutException` raised by server selection.
  - `MongoNotPrimaryException`.
- Every other unhandled exception becomes **500 `internal_error`**, with a generic message, the correlation id and no exception text in the body. The exception is logged, scrubbed.

The API host never references Mongo types: it calls only the classifier, which keeps the "API never touches Mongo directly" rule.

**Bounds**:
- Writes fail at the readiness probe, in at most 3 s (clamp 4.5 s).
- Reads fail at server selection, in 3 s.
- Both are under the 5 s in FR-024.

**Recovery**: The driver's server monitor reconnects on its own. Server selection requests an immediate heartbeat, so the first request after Mongo returns normally succeeds within one selection timeout, well under 30 s (FR-025). The API keeps no "offline" state that would need a restart.

**Test**: An Integration test (`Category=Slow`, default lane, its own collection like `StoppedMongoReadinessCollection`) starts the API in-process through `Program.CreateApp`, the pattern `ODataTestHost` already uses, against a dedicated Mongo container. It then:
1. Stops the container.
2. Asserts that a read (`/odata/Postings`) and a write each return 503 `service_unavailable` with a `correlationId` within 5 s.
3. Starts the container.
4. Polls the same read until it returns 200, with a 30 s limit, without touching the API host.

The container is created with a fixed free host port (`WithPortBinding(freePort, 27017)`) so the address survives the stop and start. Testcontainers' random ports can change on restart.

**Fallback if replica-set membership does not survive a restart**: use `docker pause` and `unpause` (exposed by the Testcontainers container API), which the tasks verify first. A paused server is "unreachable" in the timeout sense, which is the harder case for the bound.

**Alternatives rejected**:
- *Polly or `Microsoft.Extensions.Http.Resilience`*: retry policy libraries, which are out of scope, and the API's Mongo calls are not HTTP anyway.
- *A circuit breaker*: it adds state that delays recovery.

## R-008: Web logging and request handling

**Decision**: A dependency-free logger split in two:
- `web/app/lib/observability.ts`: pure. Contains `formatRecord`, `scrub`, `acceptCorrelationId`, `shouldRetry` and `backoffMs`. It is unit- and mutation-tested and added to the Stryker `mutate` list.
- `web/app/lib/log.server.ts`: wiring. Reads `ROLERADAR_LOG_FORMAT`, gets the correlation id from the existing `AsyncLocalStorage`, and writes to stdout (info and below) or stderr (warn and above).

Other changes:
- `server.ts` extends its store to `{ nonce, correlationId }`, sets `X-Correlation-ID` on every response including static files and `/health`, and writes one request line per request. The line carries method, path without the query, status and duration. The query is never logged, because `/auth/callback` carries `code` and `state`.
- All 12 existing `console.*` calls move to the logger, at their correct levels. `server.ts:230` becomes `info`.
- An ESLint `no-console` rule (allowing `dev.js`) stops new direct calls.

**Rationale**: The web needs about 60 lines of logging. Pino would add a dependency plus `pino-pretty` for the text mode, and its transport worker threads complicate the read-only container. Pino's `redact` covers object paths only, not messages.

**Alternatives rejected**:
- *pino plus pino-pretty* (above).
- *Keep `console.*` and prefix JSON by hand at each call site* (no single place to scrub).

## R-009: Web-to-API timeouts and retries

**Decision**: `api-client.server.ts` gets one `fetchWithPolicy(request)`, used by both `fetchOData` and the `openapi-fetch` client's custom `fetch` option.
- **Per-attempt timeout**: 5 s for `GET` and `HEAD`, 10 s for writes. A write must outlast the API's own 5 s bound so the owner sees the 503 envelope rather than a web timeout. The timeout uses `AbortSignal.timeout`, combined with the caller's signal through `AbortSignal.any` (Node 22).
- **Retries**: only `GET` and `HEAD`, at most 2 after the first attempt. They happen only on a network error, a per-attempt timeout or status 502, 503 or 504. The backoff is fixed at 200 ms, then 500 ms.
- **Never retried**: writes, 4xx, 500 and caller aborts.
- Every attempt carries the same `X-Correlation-ID`.
- The insights loaders' own 5 s timeout is folded into the policy.

**Alternatives rejected**:
- *A retry library*: out of scope (FR-027).
- *Retrying writes that carry a version token*: still not safe. A request the server received but whose reply was lost would be replayed and then hit a 409 the owner did not cause.

## R-010: Real actors

**Decision**:
- A new host-only service, `HttpActorAccessor`, in `src/RoleRadar.Api/Authentication`, returns the authenticated `sub` claim. `MapInboundClaims=false` is already set.
- Every API endpoint passes `Actor: actor.Subject`, and the three `ApiActor` constants are deleted.
- **The `Actor` defaults are removed from every command record** (`mcp-stdio`, `agent`, `system`), so the compiler forces every caller to name an actor.
- MCP tools pass `mcp-stdio` explicitly. CLI paths keep `cli-user`, `cli-import`, `cli-import-tracker` and `demo-seed`.
- An architecture test fails if any `ICommand` record's constructor has a defaulted `Actor` parameter.
- A Contract test calls every API write endpoint with a test subject and asserts that the audit actor equals that subject.
- An Integration test runs every CLI write path and asserts the actor starts with `cli-` or is `demo-seed`.

**Rationale**: Today an API endpoint that forgets the argument silently writes `agent` or `system`. Removing the defaults turns that into a compile error.

**Alternative rejected**: *keep the defaults and rely on tests* (a new endpoint without a test silently misattributes its writes).

## R-011: Request correlation on audit entries

**Decision**:
- A new Application port, `IRequestContext`, exposes `string? CorrelationId`.
- Each host sets it in a scoped implementation:
  - API: from the middleware.
  - CLI: one run id per process.
  - MCP: one id per tool call.
- The two audit choke points stamp it as `request_correlation_id`: `MongoTrackingTransactionContext.AppendAuditEntryAsync` and `MongoNoteRepository.MapAuditToDocument`.
- `AuditLogEntry` gains an optional `RequestCorrelationId`.
- No handler changes.

This is a new abstraction under Principle IV, recorded in ADR 0041.

**Alternative rejected**: *a `CorrelationId` parameter on every command record* (dozens of signatures for an ambient value).

## R-012: Sign-in security events

**Decision**: Four `LoggerMessage`-generated events in `RoleRadar.Observability`'s `SecurityEvents` class, raised from `Login.cshtml.cs`:
- 5001 `SignInSucceeded`.
- 5002 `SignInFailed`.
- 5003 `AccountLockedOut`.
- 5004 `OneTimeCodeFailed`.

Each event carries `event`, the correlation id from the scope, and `subject` only when a user record was matched. For the password step, the subject comes from `FindByNameAsync`, logged only if the result is non-null. It never carries the username, password, code or secret. Logs only (FR-019).

Duende's own events are not used for this: `UserLoginFailureEvent` carries the username. The redaction test also covers Duende's default event logging and its request logging (Duende filters sensitive token-request values by default). The test exercises a real authorize and token flow, so a regression in either fails it.

The existing lockout policy (5 attempts, 15 minutes) is unchanged.

**Alternatives rejected**:
- *Duende `IEventSink`* (username in failure events).
- *Storing events in the audit store* (the owner chose logs only).

## R-013: `audit list`

**Decision**:
- New Application query `ListAuditEntriesQuery`, served by `ListAuditEntriesQueryHandler`, reading through a new storage port, `IAuditLogReader`. Storage ports are permitted under Principle IV.
- The port is implemented in Infrastructure as `MongoAuditLogReader` over `audit_log`, always filtered by `owner_id`.
- **Order**: newest first by `(occurred_at desc, _id desc)`.
- **Paging**: keyset with an opaque cursor, which is safe on an append-only log while new rows arrive.
- **New index**: `ix_audit_log_entity (owner_id, entity_type, entity_id, occurred_at desc, _id)`, created at startup with the others.
- **Output**: metadata only. Never `appended_text`, never before or after values, and never note content. Changed field names appear only with `--fields`. Control characters are neutralized.
- It is registered through `AddQueryHandler`, so it is wrapped by the logging decorator and never by the audit decorator. It writes nothing.

Contract: [contracts/cli-audit-list.md](contracts/cli-audit-list.md).

**Alternatives rejected**:
- *Extend `INoteRepository.GetAuditEntriesAsync`* (note-scoped, ascending, cannot read `details`).
- *An MCP tool* (the owner declined it, and the 12 tools stay unchanged).

## R-014: Compose log caps

**Decision**:
- A top-level `x-logging` anchor with `driver: json-file` and `options: { max-size: "10m", max-file: "3" }`, applied to every service in `deploy/docker-compose.yml`, `deploy/docker-compose.apps.yml` and `deploy/docker-compose.logs.yml`. That is 30 MB per container at most.
- The demo file is unchanged (FR-029, spec assumption).
- A Vitest tooling test (`web/tests/tooling/compose-logging.test.ts`, parsing with the already-pinned `yaml` package) asserts three things:
  - Every service in those three files has both caps.
  - The demo file never mentions Dozzle or `docker-compose.logs.yml`.
  - Every published port in the logs file is bound to `127.0.0.1`.

**Alternatives rejected**:
- *An xUnit regex test like `DemoAbsentFromDefaultsTests`* (regex over YAML is brittle for nested keys).
- *Daemon-level defaults in `daemon.json`* (machine configuration, outside the repo).

## R-015: Cross-service acceptance test

**Decision**: A real-stack Playwright spec, `web/tests/e2e/operability.real-stack.spec.ts`, in the existing `real-stack` project and the isolated stack that `scripts/test-e2e-real.ps1` already starts. It never touches the developer's stack. The spec:
1. Signs in.
2. Opens a posting detail page for a random v7 id, so the API returns 404 and the web renders its not-found page.
3. Reads `X-Correlation-ID` from the document response.
4. Reads the isolated containers' output through the harness's existing `docker logs <container>` calls (`RealStackEndToEndTests.cs:339-341`). It asserts at least one line each from `web` and `api` with that `correlationId`, and that every line from the identity, web and API containers parses as JSON.

A separate Contract test asserts that a failing API call's envelope `correlationId` equals the response header.

**Alternative rejected**: *an in-process-only proof* (the web cannot run inside the .NET test host, so only the real stack shows two services).
