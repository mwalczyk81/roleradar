# Implementation Plan: Operability

**Branch**: `013-operability` | **Date**: 2026-11-09 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/013-operability/spec.md`

## Summary

This feature makes the running stack explain itself without changing what it does for the owner.

- **Logging (.NET):** every .NET host gets one console formatter that writes scrubbed records. It writes JSON in containers and readable text outside them. The formatter lives in a new dependency-free project, `RoleRadar.Observability`, because Identity cannot reference Infrastructure.
- **Logging (web):** the web app gets a matching dependency-free logger.
- **Correlation id:** a validated `X-Correlation-ID` is accepted or generated at the web edge, sent to the API and to Identity's server-side endpoints, scoped into every log record, returned on every response, and added to the API error envelope. It is also stamped on audit entries.
- **Actors:** API writes are attributed to the signed-in subject. The `Actor` defaults are removed from command records, so a missing actor no longer compiles.
- **Sign-in events:** Identity writes four structured sign-in events to the logs. They never include the typed username.
- **`audit list`:** a new read-only, owner-scoped CLI command reads the audit trail through a new Application query and storage port.
- **Health:** the API and Identity split health into liveness and readiness, and the images' built-in healthcheck now uses readiness.
- **Outages:** a lost database returns 503 `service_unavailable` in the envelope within 5 s and recovers without a restart. Web-to-API calls get per-attempt timeouts and at most two retries, for reads only.
- **Compose:** the compose files cap log size. An optional Dozzle viewer, bound to loopback only, lives in its own compose file and never in the demo.

Decisions and rejected alternatives are in [research.md](research.md), summarized in ADR 0041.

## Technical Context

**Language/Version**: C# on .NET 10 (SDK 10.0.402; API, Identity, CLI, MCP, Application, Infrastructure, new Observability); TypeScript on Node 22.23.4 (web)

**Primary Dependencies**: existing only.
- `Microsoft.Extensions.Logging.Console` 10.0.14 becomes a direct reference of `RoleRadar.Observability`. It is already pinned transitively.
- ASP.NET health checks are in the shared framework.
- MongoDB.Driver 3.13.0, Duende 8.0.10, System.CommandLine 2.0.14, openid-client 6.8.9, openapi-fetch 0.17.0, `yaml` 2.9.1 (dev).
- One new image: Dozzle, opt-in, pinned by tag and digest at implementation (R-005).
- No new NuGet or npm package.

**Storage**: MongoDB 9.0.4 `audit_log` (one optional field, one new index); Identity SQLite (read for readiness only)

**Testing**:
- xUnit v3: Architecture, Unit, Contract, and Integration with Testcontainers.
- Vitest: `web/tests/unit`, `web/tests/tooling`.
- Playwright: `chromium` and `real-stack` projects.
- Stryker: `observability.ts` is added to the web mutation list.

**Target Platform**: Docker Compose v2 on Windows, macOS and Linux; Linux CI and images

**Project Type**: existing multi-host .NET solution plus `web/`

**Performance Goals**:
- 503 within 5 s while the database is down.
- Recovery within 30 s after it returns.
- Web read attempts at most 3, each bounded at 5 s; writes attempted once, bounded at 10 s.
- Logging overhead that the owner never notices on a local stack.

**Constraints**:
- Nothing leaves the machine. The viewer's analytics are off.
- No change to demo behavior or its guard. The demo compose file is untouched.
- No new custom script.
- Fictional data only.
- LF endings.
- Never touch the developer's stack.
- Tests first.

**Scale/Scope**:
- 5 log sources (web, api, identity, cli, mcp).
- 4 API command calls to re-attribute (`RecordApplicationCommand` ×2, `RecordCompanyCommand`, `TransitionPostingCommand`).
- About 12 web `console.*` call sites.
- 3 compose files to cap (real Mongo, apps, logs viewer).
- 1 new CLI command.

## Constitution Check

| Principle | Status | Why |
| --- | --- | --- |
| I Privacy first | Pass | The feature exists to keep secrets and note bodies out of logs. Two layers do it (never log the value, then scrub every record), and the redaction test proves it with sentinel values in each host. Sign-in failures never log the typed username. `audit list` never prints note text or before/after values. All test data is fictional. Nothing is exported, and Dozzle analytics are disabled. |
| II Tests first, isolated containers | Pass | Every unit starts with a failing test (the task order enforces it). The outage test uses a dedicated Testcontainers Mongo with a fixed free port. The real-stack check uses the isolated stack that `test-e2e-real.ps1` starts. `IAuditLogReader` gets its own Contract suite. |
| III Layering enforced | Pass | Observability references no RoleRadar project, and Domain, Application and Infrastructure must not reference it (new architecture rules). Adapters call handlers in-process. `audit list` is an `IQueryHandler`. Write paths keep the transaction-and-audit decorator, whose registration test is unchanged. |
| IV Named seams only | Pass | Two new seams. `IAuditLogReader` is a storage port, which is permitted. `IRequestContext` is a small ambient-context port, not in the permitted list; ADR 0041 records it and was **accepted on 2026-11-09, before any implementation** (task T002). No generic repository, mediator, bus or factory. Expected failures stay `Result` (validation, `DatastoreOffline`). Only genuinely unexpected exceptions reach the new exception handler. |
| V Untrusted text, narrow audited tools | Pass | MCP tools are unchanged (still 12). No delete path is added. Every write keeps its append-only entry, and each entry now names the real actor. `audit list` neutralizes control characters and prints metadata only. |
| VI stdout hygiene | Pass | CLI and MCP logs stay on stderr. The formatter's stream choice is per host, and the MCP host is pinned to stderr by an existing test plus a new formatter test. The MCP host opens no listener. |
| VII Durable data, destructive confirmation | Pass | No data is deleted or rewritten. Old audit entries keep their old actors. The outage test stops only its own container. Log caps limit disk use. |
| VIII ADRs, pins | Pass | ADR 0041 (accepted 2026-11-09) records the logging approach, the Observability project, the correlation id, the viewer and its socket risk, health, the 503 mapping and `IRequestContext`. The Dozzle image is pinned by tag and digest in `deploy/docker-compose.logs.yml`, and the tag and digest are recorded in ADR 0041. The Dependabot docker-compose entry covers the new file. No floating versions. |
| IX .NET 10, nullable, warnings as errors, UTC, UUIDv7, `owner_id` | Pass | The new project inherits `Directory.Build.props`. Log timestamps are UTC. .NET correlation ids are v7 (the web's are v4, which is fine because they are not entity ids). The audit query always filters `owner_id`. No new collection. |

Post-design re-check after D1 to D12: no new violation. ADR 0041 is accepted before any code is written. The Dozzle tag and digest are pinned in `deploy/docker-compose.logs.yml` and recorded in ADR 0041.

## Design

### D1. `RoleRadar.Observability` (new project, R-001, R-002)

`src/RoleRadar.Observability/`:
- `RoleRadarConsoleFormatter` (json and text modes, scrubbing every field).
- `LogScrubber`.
- `CorrelationId` (`HeaderName`, `TryAccept`, `New`).
- `LoggingSetup.AddRoleRadarLogging(this ILoggingBuilder, string service, LogStream stream)`.
- `SecurityEvents` (`LoggerMessage` 5001 to 5004).

It references `Microsoft.Extensions.Logging.Console` only. The four hosts call `AddRoleRadarLogging("api" | "identity" | "cli" | "mcp", ...)`.
- `LogStream` has two members. `Split` sends `info` and below to stdout and `warn` and above to stderr; the API and Identity use it. `StdErr` sends everything to stderr; CLI and MCP use it, which keeps their current stream.
- The CLI keeps its level resolution.
- Category defaults are set in code and can be overridden with `Logging__LogLevel__*`.
- The format comes only from `ROLERADAR_LOG_FORMAT`. The setup never branches on an environment name such as `IsDevelopment()`, because `DemoEnvironmentParityTests` forbids new environment branches.
- The test log sink is not part of this project's public API. It lives in `tests/RoleRadar.Tests.Integration/Fixtures/` and is linked into the Contract and Unit projects the way `MongoTestImage.cs` is.

### D2. Correlation middleware in the API and Identity (R-004)

A short middleware runs first in each pipeline. It:
1. Accepts or generates the id.
2. Sets `X-Correlation-ID` on the response (through `OnStarting`).
3. Opens a logging scope `{ CorrelationId }`.
4. Sets the scoped `IRequestContext` (API only; Identity has no Application layer).
5. Writes one `RequestCompleted` record: method, path without the query, status, duration. Health paths log at Debug.

### D3. Error envelope and outages (R-007)

- `ErrorEnvelope` gains `CorrelationId` (non-null string in the schema). Producers take it from one `ErrorResults` helper that reads the current `HttpContext`. A Contract test fails if any envelope response has an empty `correlationId` or one that differs from the header.
- `FromResultError` maps `DatastoreOffline` to 503 `service_unavailable` with `Retry-After: 5`.
- New `ApiExceptionHandler : IExceptionHandler` (`UseExceptionHandler`):
  - `DatastoreFailure.IsUnavailable(ex)` (Infrastructure) becomes 503.
  - Anything else becomes 500 `internal_error`.
  - The body never includes exception text. The log does, scrubbed.
- `ODataExceptionFilter` adds the id.
- Regenerate OpenAPI and `api-types.ts`.

### D4. Health (R-006)

- `AddHealthChecks()`.
- `/health/live` (no checks), `/health/ready` (tagged checks with a 2 s bound), and `/health` as an alias of live, in both hosts.
- `ApiReadinessCheck` wraps `INoteRepository.CheckReadinessAsync`. `IdentityReadinessCheck` wraps `CanConnectAsync`.
- `--healthcheck` default URL becomes `/health/ready`. It is held in a `public const string DefaultHealthcheckUrl` on each host's `Program` so tests can assert it. Compose healthcheck lines are unchanged.
- Identity readiness is tested two ways, and never through a host that cannot start (Identity migrates its SQLite store at startup):
  - `IdentityReadinessCheck` called directly against an unopenable SQLite path.
  - The host with a stubbed check.

### D5. Real actors (R-010)

- `HttpActorAccessor` (API Authentication) returns `sub`. Endpoints pass it. The three `ApiActor` constants are deleted.
- Remove the `Actor` defaults from every command record. MCP tools pass `mcp-stdio`, and every compile error is fixed by naming the caller's actor.
- Remove the handlers' `string.IsNullOrWhiteSpace(command.Actor) ? "<default>" : ...` fallbacks. A blank actor returns a `ValidationError` `Result` (an expected failure, not an exception). This is recorded in ADR 0041.
- Existing tests that assert `api-user` change to the test subject: `ApiMcpParityIntegrationTests.cs:158` and `RealStackEndToEndTests.cs:374-387`. This is one of the intended changes listed in SC-008.
- Architecture test: no `ICommand` record has a defaulted `Actor`.
- Contract test: every API write endpoint's audit actor equals the test subject.
- Integration test: every CLI write path records a `cli-*` actor or `demo-seed`.

### D6. `IRequestContext` and the audit field (R-011)

- Application: `IRequestContext { string? CorrelationId }`.
- Hosts:
  - API: `HttpRequestContext`, set by the middleware.
  - CLI: `RunRequestContext`, one v7 id per process, also opened as the logging scope.
  - MCP: per-tool-call id set in the existing call filter.
- Infrastructure stamps `request_correlation_id` in `MongoTrackingTransactionContext.AppendAuditEntryAsync` and `MongoNoteRepository.MapAuditToDocument`.
- `AuditLogEntry.RequestCorrelationId` is optional. Rehydrate reads it.
- **Lifetimes (verified 2026-11-09).** `INoteRepository` and `ITrackingTransactionContext` are registered `TryAddScoped` in `src/RoleRadar.Infrastructure/DependencyInjection.cs`, so a scoped `IRequestContext` can be injected into both choke points without capturing the wrong scope. `IMongoClient` and `MongoReadinessProbe` are singletons and must never take `IRequestContext`.
- **MCP scope.** The per-call id needs one DI scope per tool call. T052 confirms that the ModelContextProtocol 2.3.0 server resolves tools in a per-request scope. If it does not, the call filter creates a scope per call and sets the id on that scope's `IRequestContext`.
- `ValidateScopes` stays on in tests, so a captive dependency fails at startup.

### D7. Sign-in events (R-012)

- `Login.cshtml.cs` raises `SecurityEvents` at each outcome of the password and TOTP steps.
- On a failed password, `FindByNameAsync` supplies `subject` only when the user exists. The username is never passed to a logger.
- Lockout writes the failure event and then `AccountLockedOut`.
- Duende request and event logging stays at its defaults. The redaction test covers it through a real authorize, token and refresh flow.

### D8. `audit list` (R-013)

- Application: `ListAuditEntriesQuery`, `ListAuditEntriesQueryHandler`, `AuditListPage`, `AuditListItem`, `IAuditLogReader`, a cursor codec, and validators.
- Infrastructure: `MongoAuditLogReader` and index `ix_audit_log_entity`.
- CLI: `AuditCommand` with `list` ([contracts/cli-audit-list.md](contracts/cli-audit-list.md)), following the `ContactCommand get` pattern (text or `--json`, exit codes 0, 1 and 2).
- `docs/cli-reference.md` gets the command.

### D9. Web logging and correlation (R-008)

- `web/app/lib/observability.ts` (pure): `formatRecord`, `scrub`, `acceptCorrelationId`, `newCorrelationId`, `shouldRetry`, `backoffMs`.
- `web/app/lib/log.server.ts`: `log.info|warn|error|debug` with `service: "web"` and the id from the store.
- `server.ts`:
  - The store becomes `{ nonce, correlationId }`.
  - Sets the header on every response.
  - Writes one `RequestCompleted` per request (no query string).
  - Replaces its `console.*` calls; the `[INFO]` line moves to `info`.
- Every other `console.*` call moves to `log`.
- ESLint `no-console`, allowed only in `web/dev.js` and `web/tests/**`. Test files may stub console output.
- Stryker `mutate` gains `observability.ts`.
- `Dockerfile.web` sets `ROLERADAR_LOG_FORMAT=json`.

### D10. Web outbound policy (R-009)

- `fetchWithPolicy` in `api-client.server.ts`, used by `fetchOData` and as `openapi-fetch`'s `fetch`.
- It adds `X-Correlation-ID`, a per-attempt timeout (5 s for reads, 10 s for writes), and retries only `GET` and `HEAD`, at most twice, on network error, timeout, 502, 503 or 504, with 200 ms then 500 ms backoff.
- The `insights-*` local timeouts are replaced by the policy.
- `oidc.server.ts` sets `customFetch` to add the header on discovery, code grant and refresh.
- The existing `mapApiError` `message` versus `issue` defect is not fixed here (out of scope). It becomes a follow-up card.

### D11. Compose log caps and viewer (R-005, R-014)

- `x-logging` anchor (`json-file`, `10m`, `3`) on every service in `deploy/docker-compose.yml` and `deploy/docker-compose.apps.yml`.
- New `deploy/docker-compose.logs.yml` with one `logs` service:
  - Dozzle, pinned by tag and digest.
  - `127.0.0.1:5004:8080`.
  - Read-only socket.
  - `DOZZLE_NO_ANALYTICS=true`.
  - Label filter on the RoleRadar project.
  - Hardened (`read_only`, `cap_drop: [ALL]`, `no-new-privileges`).
  - Same `x-logging` caps.
- `.github/dependabot.yml` docker-compose entry covers it.
- `deploy/demo/docker-compose.yml` is not edited.
- Tooling test: caps on every service in the three files, the demo free of the viewer, viewer ports on loopback only.
- `DemoAbsentFromDefaultsTests` still passes unchanged.

### D12. Docs

- New `docs/operations/logs-and-health.md`, covering:
  - The three ways to read logs.
  - The correlation id and how to search for it.
  - The format and level switches.
  - Health endpoints and what 503 means.
  - `audit list`.
  - What is never logged.
- `docs/threat-model.md` gains the viewer socket note and the logging redaction control.
- `docs/adr/0041-operability-logging-correlation-health.md` and its index row.
- README links the operations page in the existing docs list (no new section).

## Test Strategy (tests first, by story)

**US1**
- Unit tests for the formatter, the scrubber (one case per rule) and the correlation rules.
- Vitest tests for `observability.ts`, `log.server.ts` (level and stream), `server.ts` (header on every response, inbound acceptance and replacement, no query in the request record) and the `no-console` lint.
- Tooling test for compose caps.
- Integration redaction test across API, Identity, CLI and MCP with sentinels.
- Contract test: envelope id equals header id.
- Real-stack Playwright: web and API lines share the id.

**US2**
- Architecture test: no defaulted `Actor`.
- Contract test: actor equals subject for each API write endpoint.
- Integration test: CLI actors.
- Contract test: sign-in events, one per outcome, with no username and with `subject` only on a match.
- `IAuditLogReader` Contract suite.
- Handler unit tests for validation and cursor.
- CLI tests for `audit list`: output, `--json`, exit codes, control characters, no excluded fields, and no writes (the collection count is unchanged).

**US3**
- Contract tests: live and ready in both hosts, ready returns 503 when the store is down (Identity with a missing SQLite path), and the `--healthcheck` default URL.
- Unit test: `FromResultError` maps `DatastoreOffline` to 503.
- Exception handler tests: 503 and 500 bodies carry no exception text.
- Integration `DatastoreOutage` test (stop, 503 within 5 s, start, 200 within 30 s, no restart).
- Vitest tests for the retry policy: reads at most 3 attempts, writes exactly 1, caller abort not retried, same id on every attempt.

**Phase end**: the full verification list in AGENTS.md, including `linux-web-check.sh` and `review-gate.ps1`. Numbers come from command output only.

## Project Structure

### Documentation (this feature)

```text
specs/013-operability/
├── spec.md
├── plan.md                 # this file
├── research.md             # R-001 to R-015
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── http-correlation-and-errors.md
│   ├── health.md
│   ├── log-records.md
│   └── cli-audit-list.md
├── checklists/requirements.md
└── tasks.md                # /speckit-tasks, not created here
```

### Source Code (repository root)

```text
src/
├── RoleRadar.Observability/            # NEW: formatter, scrubber, CorrelationId, LoggingSetup, SecurityEvents
├── RoleRadar.Domain/Audit/AuditLogEntry.cs                       # + RequestCorrelationId
├── RoleRadar.Application/
│   ├── Abstractions/IRequestContext.cs, IAuditLogReader.cs       # NEW
│   ├── Audit/Queries/ListAuditEntriesQuery.cs (+ handler, page, cursor)  # NEW
│   └── **/Commands/*.cs                                           # Actor defaults removed
├── RoleRadar.Infrastructure/
│   ├── Mongo/MongoAuditLogReader.cs, DatastoreFailure.cs          # NEW
│   ├── Mongo/MongoBootstrapAndIndexes.cs                          # + ix_audit_log_entity
│   └── Mongo/MongoTrackingTransactionContext.cs, MongoNoteRepository.cs  # stamp request_correlation_id
├── RoleRadar.Api/
│   ├── Program.cs                     # logging, middleware, health, exception handler, --healthcheck URL
│   ├── Observability/CorrelationMiddleware.cs, HttpRequestContext.cs, ApiExceptionHandler.cs, ApiReadinessCheck.cs  # NEW
│   ├── Authentication/HttpActorAccessor.cs                        # NEW
│   ├── Envelopes/ErrorEnvelope.cs, ErrorResults.cs                # + correlationId, 503 mapping
│   └── Endpoints/*.cs                                             # Actor from subject
├── RoleRadar.Identity/
│   ├── Program.cs                     # logging, middleware, health, --healthcheck URL
│   ├── Observability/CorrelationMiddleware.cs, IdentityReadinessCheck.cs  # NEW
│   └── Pages/Account/Login.cshtml.cs  # security events
├── RoleRadar.Cli/
│   ├── Program.cs                     # AddRoleRadarLogging, RunRequestContext
│   └── Commands/AuditCommand.cs       # NEW
└── RoleRadar.Mcp.Stdio/               # AddRoleRadarLogging (stderr), per-call context, explicit actors
web/
├── app/lib/observability.ts, log.server.ts                         # NEW
├── app/lib/api-client.server.ts, oidc.server.ts, insights-*.server.ts  # policy, header
├── server.ts, app/entry.server.tsx, app/routes/auth.*.tsx, app/lib/session.server.ts  # logger
├── eslint.config.*, stryker.config.mjs
└── tests/unit/observability.test.ts, tests/unit/api-policy.test.ts,
    tests/tooling/compose-logging.test.ts, tests/e2e/operability.real-stack.spec.ts  # NEW
deploy/
├── docker-compose.yml, docker-compose.apps.yml                      # x-logging caps
├── docker-compose.logs.yml                                          # NEW: opt-in Dozzle
├── Dockerfile.api, Dockerfile.identity, Dockerfile.web              # ROLERADAR_LOG_FORMAT=json
└── demo/docker-compose.yml                                          # UNCHANGED
Dockerfile                                                           # CLI/MCP: ROLERADAR_LOG_FORMAT=json
tests/
├── RoleRadar.Tests.Architecture/   # Observability references, no defaulted Actor
├── RoleRadar.Tests.Unit/           # formatter, scrubber, correlation, envelope mapping, query validation
├── RoleRadar.Tests.Contract/       # envelope id, health, actors, sign-in events, IAuditLogReader suite
└── RoleRadar.Tests.Integration/    # redaction, CLI actors, DatastoreOutage, audit list CLI
docs/
├── adr/0041-operability-logging-correlation-health.md, adr/README.md
├── operations/logs-and-health.md   # NEW
├── cli-reference.md, threat-model.md, README.md
```

**Structure Decision**: Keep the existing multi-host layout. Add one source project, `RoleRadar.Observability`, and no test projects. Everything else extends existing projects in place. The architecture tests gain the new project's reference rules.

## Complexity Tracking

| Item | Why Needed | Simpler Alternative Rejected Because |
| --- | --- | --- |
| New source project `RoleRadar.Observability` | Identity references no RoleRadar project and must not pull in Infrastructure (Mongo) or let Duende leak through the CLI's reference to Identity. The redaction logic must have exactly one copy. | Infrastructure: Identity cannot use it. Linked source files: hidden coupling. Four copies: drift is the very failure the redaction test guards against. |
| `IRequestContext` (seam outside the Principle IV list) | Audit entries need the request id without threading it through dozens of command signatures. | A parameter on every command record: wide churn for an ambient value, and easy to forget on new commands. ADR 0041 records it. |
| Dozzle with a read-only Docker socket | Gives the owner a viewer with no app-side sink and nothing leaving the machine. | Seq: needs a sink and is proprietary. Loki: three services and a collector. A socket proxy: a second image for an opt-in loopback tool. The ADR states the risk. |
| Removing `Actor` defaults (wide, mechanical edit) | Makes misattribution a compile error instead of a silent `agent` or `system`. | Tests alone: a new endpoint without a test would still misattribute silently. |

## Follow-ups Raised (not in scope)

- `mapApiError` reads `details[].message`, but the contract field is `issue`, so inline field errors may lose their text. This is a separate bug card.
- Decide later whether to raise the default log level for Duende categories after seeing real output volume.
