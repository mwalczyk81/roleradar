# Tasks: Operability

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md), [ADR 0041](../../docs/adr/0041-operability-logging-correlation-health.md)

**Rules for every task** (from AGENTS.md and the constitution):

- Write tests first and show them failing before the implementation. Record the red run's output line in the task notes.
- LF line endings, fictional data only.
- Never run anything against the developer's running stack, `roleradar-mongo` or port 27017.
- Never read `web/.env.local` or `deploy/.env`.
- Never commit or push.
- Stop at every **STOP** gate and report numbers from command output only.
- `mapApiError` is out of scope (Jira RR-64).

Notation: `[P]` = can run in parallel (different files, no dependency on an incomplete task). `[US#]` = user story. FR and SC ids are in parentheses.

## Phase 1: Setup

- [X] T001 Confirm branch `013-operability` and that the tree is clean apart from the spec folder and ADR 0041. Confirm a green baseline from `dotnet build RoleRadar.sln -c Release -v q --nologo -warnaserror` and `pwsh ./scripts/test-fast.ps1`. Record the test counts from the output in the notes at the bottom of this file.
- [X] T002 Accept ADR 0041 before any code (Principle IV for `IRequestContext`, Principle VIII):
  - `docs/adr/0041-operability-logging-correlation-health.md`: status `Accepted`, date 2026-11-09, covering every decision except the Dozzle image pin, which T073 records.
  - `docs/adr/README.md`: the index row reads `Accepted`.

  Done on 2026-11-09 during the `/speckit-analyze` remediation.

## Phase 2: Foundational (blocks every user story)

**Goal**: The new `RoleRadar.Observability` project exists, builds in every image, and is fenced by architecture tests. A test log sink exists for every test that inspects log output.

- [X] T003 [P] Write the tests first in `tests/RoleRadar.Tests.Architecture/ObservabilityArchitectureTests.cs`:
  - `RoleRadar.Observability` references no `RoleRadar.*` assembly.
  - `RoleRadar.Domain`, `RoleRadar.Application` and `RoleRadar.Infrastructure` do not reference `RoleRadar.Observability`.
  - `RoleRadar.Observability` does not reference `Microsoft.AspNetCore.*`, `MongoDB.*` or `Duende.*`.
  - `RoleRadar.Identity` does not reference `MongoDB.*`, so sign-in events stay logs-only (FR-019). This assertion pins today's behavior and may already pass.
  - A sentinel fails if the `RoleRadar.Observability` assembly has no public types, so the checks cannot pass vacuously.

  Add the project reference to `tests/RoleRadar.Tests.Architecture/RoleRadar.Tests.Architecture.csproj` once T004 exists. Until then, show red by building against the missing project.
- [X] T004 Create `src/RoleRadar.Observability/RoleRadar.Observability.csproj`:
  - A library that inherits `Directory.Build.props`.
  - One direct `PackageReference Include="Microsoft.Extensions.Logging.Console"`. Add it to `Directory.Packages.props` only if it is not already there; the pin is 10.0.14.
  - One placeholder public type, `CorrelationId` (`public static class` with `HeaderName = "X-Correlation-ID"`), so the sentinel can turn green.

  Wire it up:
  - Add it to `RoleRadar.sln` under the `src` folder.
  - Add `ProjectReference`s from `src/RoleRadar.Api`, `src/RoleRadar.Identity`, `src/RoleRadar.Cli` and `src/RoleRadar.Mcp.Stdio`, and from `tests/RoleRadar.Tests.Architecture`, `tests/RoleRadar.Tests.Unit`, `tests/RoleRadar.Tests.Contract` and `tests/RoleRadar.Tests.Integration`.
  - Generate the lock files: run `dotnet restore RoleRadar.sln --force-evaluate`. Confirm that `src/RoleRadar.Observability/packages.lock.json` exists and that the changed host and test lock files differ only by the new project.

  T003 green.
- [X] T005 [P] Extend `tests/RoleRadar.Tests.Unit/Containers/DockerIgnoreCoverageTests.cs` first.
  - Add a case asserting that `.dockerignore` lets through `src/RoleRadar.Observability/RoleRadar.Observability.csproj`, `src/RoleRadar.Observability/packages.lock.json` and a `src/RoleRadar.Observability/*.cs` file.
  - Add a case that parses `Dockerfile`, `deploy/Dockerfile.api` and `deploy/Dockerfile.identity`. Each must have the line `COPY src/RoleRadar.Observability/RoleRadar.Observability.csproj src/RoleRadar.Observability/packages.lock.json src/RoleRadar.Observability/` before its first `dotnet restore`. The two deploy files must also have the source line `COPY src/RoleRadar.Observability/ src/RoleRadar.Observability/` before `dotnet publish`.

  Show red on the Dockerfile case.
- [X] T006 Add the Observability COPY lines.
  - `Dockerfile`: the restore-stage line, next to the other project lines. The source is already covered by `COPY src/ src/`.
  - `deploy/Dockerfile.api` and `deploy/Dockerfile.identity`: the restore-stage line and the source line.

  Leave `.dockerignore` unchanged if the existing `!src/**/*.csproj`, `!src/**/packages.lock.json` and `!src/**/*.cs` rules already cover the new project; T005 proves it either way. T005 green.
- [X] T007 Run the images lane early: `pwsh ./scripts/test-images.ps1`. All three .NET images must build with `--locked-mode` restore, and the launched image suites must pass. Record the pass and fail counts from the output. If the lane cannot run on this machine, say so, then run `docker build -f deploy/Dockerfile.api .`, `docker build -f deploy/Dockerfile.identity .` and `docker build .` with throwaway tags instead.
- [X] T008 Add the test log sink in `tests/RoleRadar.Tests.Integration/Fixtures/FormatterLogCapture.cs`. It is test-only and **not** part of the public API of `RoleRadar.Observability`.
  - `FormatterLogCapture` is an `ILoggerProvider` and `ISupportExternalScope`. It takes any `ConsoleFormatter` (from `Microsoft.Extensions.Logging.Console`) and a service name.
  - Each log call runs `formatter.Write(in LogEntry<TState>, IExternalScopeProvider, TextWriter)` into a per-test `StringWriter`.
  - It exposes `Lines` (the output split on `\n`) and `Clear()`.
  - It is safe for parallel tests because each test owns its own instance.

  Link it into `tests/RoleRadar.Tests.Contract/RoleRadar.Tests.Contract.csproj` and `tests/RoleRadar.Tests.Unit/RoleRadar.Tests.Unit.csproj` with `<Compile Include="..\RoleRadar.Tests.Integration\Fixtures\FormatterLogCapture.cs" Link="Fixtures\FormatterLogCapture.cs" />`, the same way `MongoTestImage.cs` is linked.

  Its own tests come first, in `tests/RoleRadar.Tests.Unit/Observability/FormatterLogCaptureTests.cs`. They use a trivial test `ConsoleFormatter` subclass that writes `"{level}|{message}|{scopes}"`, and check:
  - one line per call;
  - scopes reach the formatter;
  - two instances do not share output.

  Show red, then make them green.

**Checkpoint**: The solution builds with `-warnaserror`. `pwsh ./scripts/test-fast.ps1` is green. The images build with the new project. The test sink is available in Unit, Contract and Integration.

## Phase 3: User Story 1 - Logs I can read and follow (P1) MVP

**Goal**: Structured, scrubbed, correlated logs in web, API, Identity, CLI and MCP; three ways to read them; capped container logs.

**Independent test**: A failing request through the web produces web and API lines that share the response's `X-Correlation-ID`. The envelope carries the same id. The redaction tests fail if any sentinel secret is logged.

### Tests first (US1)

- [X] T009 [P] [US1] Write `tests/RoleRadar.Tests.Unit/Observability/CorrelationIdTests.cs`. Show red.
  - `TryAccept` accepts values matching `^[A-Za-z0-9][A-Za-z0-9._-]{7,63}$`.
  - `TryAccept` rejects null, empty, 7 characters, 65 characters, a leading `-` or `.`, spaces, CR/LF, `%0d%0a`, Unicode and `<script>`.
  - `New()` returns a v7 `Guid` in `"D"` format that `TryAccept` accepts.
- [X] T010 [P] [US1] Write `tests/RoleRadar.Tests.Unit/Observability/LogScrubberTests.cs` with one test per rule, so that removing any single rule fails exactly one case (SC-003). Show red.
  - `Bearer <jwt>` and `Basic <b64>`.
  - A bare JWT shape (`eyJ...`).
  - `mongodb://user:pass@host` and `mongodb+srv://user:pass@host`, using the same vector set as the `ConnectionStringRedactor` tests, shared through one test data class.
  - Each denylisted key, case-insensitive: `password`, `passphrase`, `secret`, `token`, `authorization`, `cookie`, `set-cookie`, `otp`, `totp`, `code`, `body`, `content`, `appendedText`, `noteBody`. The value becomes `"[redacted]"`.
  - A query string is stripped from a URL-shaped value.
  - Values without secrets are left byte-identical.
- [X] T011 [P] [US1] Write `tests/RoleRadar.Tests.Unit/Observability/RoleRadarConsoleFormatterTests.cs`, using `FormatterLogCapture` from T008. Show red.
  - **JSON mode:** exactly one line per record. The fields are `ts` (UTC, milliseconds, `Z`), `level` (`trace|debug|info|warn|error|fatal`), `service`, `category`, `msg`, optional `event` and `eventId`, `correlationId` from a scope, `props`, and `error {type,message,stack}`, as listed in `specs/013-operability/contracts/log-records.md`.
  - **Text mode:** one line, `ts level service [correlationId] msg key=value`.
  - Every field passes through the scrubber, including the exception message, the stack and scope values.
  - Messages with embedded newlines still produce one line.
  - `LogStream.StdErr` never writes to stdout (MCP, Principle VI). `LogStream.Split` sends `info` and below to stdout and `warn` and above to stderr.
  - Setting `ASPNETCORE_ENVIRONMENT=Development` does not change the format; only `ROLERADAR_LOG_FORMAT` does.
- [X] T012 [P] [US1] Write the contract tests in `tests/RoleRadar.Tests.Contract/Api/CorrelationContractTests.cs`, using the existing `ApiContractTestHost` with `FormatterLogCapture` attached. Show red.
  - **Non-vacuous capture first:** before any other log assertion, assert that at least one line was captured and that the expected `RequestCompleted` line is present.
  - Every response carries `X-Correlation-ID`: 200, 400, 401, 404, an OData 400, and `/health`.
  - A well-formed inbound id is echoed back.
  - A malformed inbound id (`bad id\r\n`) is replaced, and the malformed value never appears in the captured lines.
  - Every `ErrorEnvelope` body has a non-empty `correlationId` equal to the header (FR-006, FR-007).
  - Each request writes exactly one `RequestCompleted` record, with `method`, `path` without a query string, `status`, `durationMs` and the `correlationId`.
  - **Every record written during the request carries that `correlationId`** (FR-005). Assert this on the Application logging decorator's command record (EventId 1001, Information) for a posting transition. Assert it also on the query record (EventId 2001, which logs at Debug) for a read; for that case only, set `Logging:LogLevel:RoleRadar.Application` to `Debug` in the test host's configuration.
- [X] T013 [P] [US1] Write the contract tests in `tests/RoleRadar.Tests.Contract/Identity/IdentityCorrelationContractTests.cs`, using `IdentityContractTestServer` with `FormatterLogCapture`. Show red.
  - **Non-vacuous capture first:** before any other log assertion, assert that at least one line was captured and that the expected `RequestCompleted` line is present.
  - The header is present on the login page, on `/.well-known/openid-configuration` and on a token endpoint error.
  - Inbound ids are accepted or replaced by the same rules as T012.
  - The `RequestCompleted` path for `/connect/authorize?...&code_challenge=...` has no query string.
  - Every record written during a login page request carries the request's `correlationId` (FR-005).
- [X] T014 [P] [US1] Write `web/tests/unit/observability.test.ts`. It is red until `web/app/lib/observability.ts` exists; run `pnpm --filter web run test` to show the failure.
  - `formatRecord` emits one JSON line with the contract fields and `service: "web"`, and text mode is one line.
  - `scrub` follows the same rule set as T010, one case per rule.
  - `acceptCorrelationId` follows the same table as T009.
  - `newCorrelationId` returns a UUID that `acceptCorrelationId` accepts.
- [X] T015 [P] [US1] Write `web/tests/unit/log-server.test.ts`. It is red until `web/app/lib/log.server.ts` exists.
  - `info` and `debug` go to stdout; `warn` and `error` go to stderr.
  - `ROLERADAR_LOG_FORMAT=json` writes JSON; unset writes text.
  - `ROLERADAR_LOG_LEVEL` filters out lower levels.
  - The `correlationId` comes from the request store when one is active, and is omitted outside a request.
- [X] T016 [P] [US1] Write `web/tests/unit/server-correlation.test.ts`, driving `createRequestHandler` from `web/server.ts` with fake requests. Show red.
  - `X-Correlation-ID` is on every response: a static file, `/favicon.ico`, `/health`, an app route and a handler error.
  - An inbound well-formed id is echoed; a malformed one is replaced and never logged.
  - Exactly one `RequestCompleted` record per request. For `/auth/callback?code=abc&state=xyz`, its `path` is `/auth/callback`, and neither `abc` nor `xyz` appears anywhere in the output.
  - The startup "listening" line is level `info`, not `error` (FR-002).
- [X] T017 [P] [US1] Write `web/tests/unit/api-client-correlation.test.ts`. It is red until the implementation exists.
  - `createApiClient(token)` and `fetchOData(...)` send `X-Correlation-ID` equal to the active request's id.
  - The `openid-client` configuration from `web/app/lib/oidc.server.ts` sends the header on discovery, code grant and refresh through `customFetch`, tested against a fake fetch.
  - The `Authorization` value is never logged.
- [X] T018 [P] [US1] Write `web/tests/tooling/compose-logging.test.ts`, parsing with the existing `yaml` dev dependency. It is red until the implementation exists.
  - Every service in `deploy/docker-compose.yml`, `deploy/docker-compose.apps.yml` and `deploy/docker-compose.logs.yml` has `logging.driver: json-file`, `options.max-size: "10m"` and `options.max-file: "3"` (FR-032, SC-010).
  - `deploy/demo/docker-compose.yml` contains no `dozzle` image and no reference to `docker-compose.logs.yml` (FR-011, FR-029).
  - Every published port in `deploy/docker-compose.logs.yml` starts with `127.0.0.1:`.
  - The logs service sets `DOZZLE_NO_ANALYTICS=true` (FR-028), mounts the Docker socket read-only (`:ro`), and pins its image as `<repo>:<tag>@sha256:<64 hex>`.
- [X] T019 [P] [US1] Write the redaction tests in `tests/RoleRadar.Tests.Integration/Observability/RedactionIntegrationTests.cs` for the API, CLI and MCP and for the Mongo paths (FR-012, FR-013, SC-003). Use the default lane and a Testcontainers Mongo only. Show red.
  - **Non-vacuous capture first:** for each host, before checking for sentinels, assert that at least one line was captured and that the expected line is present. For the API that is a `RequestCompleted` line; for the CLI, a logged readiness failure; for MCP, the tool call's log line.
  - Use unique fictional sentinels (for example `SENTINEL-TOKEN-7f3a`) for:
    - a bearer access token and a refresh token, through API calls on an in-process API host (`Program.CreateApp`, as `ODataTestHost` does);
    - a connection string with credentials, through a forced Mongo connection failure in the CLI `status` path and in the API;
    - a note body and appended text, through note create and append on the Application handlers, including a validation failure from an over-limit body;
    - an MCP tool call whose arguments carry a sentinel note body and a sentinel token-shaped string.
  - Capture each host's output with `FormatterLogCapture` and the production `RoleRadarConsoleFormatter`.
  - Assert that no sentinel appears and that every captured line parses as JSON (SC-002).
- [X] T020 [P] [US1] Write the Identity redaction tests in `tests/RoleRadar.Tests.Contract/Identity/IdentityRedactionContractTests.cs`, next to the sign-in event tests (T047), using `IdentityContractTestServer` with `FormatterLogCapture`. Show red.
  - **Non-vacuous capture first:** before checking for sentinels, assert that at least one line was captured and that a `RequestCompleted` line for the login request is present.
  - Use fictional sentinels for a password, a TOTP code, a TOTP secret, the username typed on a failed sign-in (FR-018) and the OIDC `code` and `state` values.
  - Drive a wrong password, a wrong code, a successful sign-in and a real authorize, token and refresh flow.
  - Assert that no sentinel appears in any captured line, including Duende's own request and event logging.
- [X] T021 [P] [US1] Write `web/tests/unit/web-redaction.test.ts` (FR-012, FR-013). It is red until the implementation exists.
  - Start the web server with secret-shaped fictional values for the OIDC client secret and `SESSION_ENCRYPTION_KEY`, including the missing-key and wrong-size failure paths in `web/app/lib/session.server.ts` and `web/app/lib/oidc.server.ts`.
  - Drive a loader through a fake API that returns 401 while carrying a sentinel bearer token and a sentinel refresh token.
  - Capture `process.stdout.write` and `process.stderr.write`, and assert that no sentinel appears.
- [X] T022 [P] [US1] Write the CLI and MCP JSON log tests in `tests/RoleRadar.Tests.Integration/Observability/HostLogFormatIntegrationTests.cs` (FR-001). Show red.
  - **Non-vacuous capture first:** before the format checks, assert that at least one stderr line was captured and that the expected line is present (the CLI's readiness or status line, and the MCP tool-call line).
  - With `ROLERADAR_LOG_FORMAT=json` and a Testcontainers Mongo, run the CLI's `status --verbose` in-process.
  - Run one MCP tool call (`note_search`) through the MCP host.
  - Every stderr line parses as JSON, with `service` set to `cli` or `mcp` and a `correlationId` (the run id or the per-call id).
  - No log line is written to stdout.
- [X] T023 [US1] Add the real-stack cross-service check (SC-001, SC-002, US1 scenario 1) to `tests/RoleRadar.Tests.Integration/Containers/RealStackEndToEndTests.cs` (`[Trait("Lane", "real-stack")]`) and `web/tests/e2e/operability.real-stack.spec.ts` (Playwright `real-stack` project in `web/playwright.config.ts`).
  - The spec signs in with the harness's fictional owner, opens `/postings/<random v7 id>` (the API returns 404), and reads `X-Correlation-ID` from the document response.
  - Using the harness's existing `docker logs` calls for the isolated identity, web and API containers (`RealStackEndToEndTests.cs:339-341`), assert:
    - at least one JSON line from `web` and one from `api` carries that `correlationId`;
    - every line from all three containers parses as JSON.
  - Never read the developer's stack.

  Run `pwsh ./scripts/test-e2e-real.ps1` to show red.

### Implementation (US1)

- [X] T024 [US1] Implement `CorrelationId.TryAccept` and `CorrelationId.New` in `src/RoleRadar.Observability/CorrelationId.cs`. T009 green.
- [X] T025 [US1] Implement `src/RoleRadar.Observability/LogScrubber.cs` with the rule set in `specs/013-operability/research.md` R-003. T010 green.
- [X] T026 [US1] Implement the formatter and logging setup. T011 green.
  - In `src/RoleRadar.Observability/RoleRadarConsoleFormatter.cs`: `RoleRadarConsoleFormatter` and `LogStream` (`Split`, `StdErr`).
  - In `src/RoleRadar.Observability/LoggingSetup.cs`: `LoggingSetup.AddRoleRadarLogging(this ILoggingBuilder, string service, LogStream stream)`.
  - The format comes only from `ROLERADAR_LOG_FORMAT` (`json` or `text`, default `text`). Never branch on an environment name, because `DemoEnvironmentParityTests` must stay green.
  - Default levels: Information for app categories, Warning for `Microsoft.*` and `System.*`, Information for `Microsoft.Hosting.Lifetime`. All can be overridden through `Logging__LogLevel__*`.
- [X] T027 [US1] Wire the API host. In `src/RoleRadar.Api/Program.cs`, call `builder.Logging.ClearProviders().AddRoleRadarLogging("api", LogStream.Split)` in `CreateApp` **before** `configureBuilder?.Invoke(builder)` (today at `:64`). If it ran after, it would delete the capture providers that tests attach through that hook, and the redaction tests would pass with nothing captured. Add `src/RoleRadar.Api/Observability/CorrelationMiddleware.cs` and register it first in the pipeline. The middleware:
  - accepts or generates the id;
  - sets the response header through `OnStarting`;
  - opens a `{ CorrelationId }` scope;
  - logs one `RequestCompleted` record, at Debug for `/health*` paths.
- [X] T028 [US1] Add the correlation id to the error envelope. T012 green (with T027). `ApiOpenApiContractSnapshotTests` is green against the regenerated snapshot, and the pre-commit contracts diff is clean.
  - Add `CorrelationId` to `src/RoleRadar.Api/Envelopes/ErrorEnvelope.cs`: a non-null string, serialized as `correlationId`.
  - Add `src/RoleRadar.Api/Envelopes/ErrorResults.cs`, which reads the id from the current `HttpContext`.
  - Route every envelope producer through it: `ErrorEnvelope.FromResultError`, the factories, the 15 uses in `src/RoleRadar.Api/Endpoints/*.cs`, and `src/RoleRadar.Api/Filters/ODataExceptionFilter.cs`. The status and code mapping does not change in this task.
  - Regenerate the contract: `dotnet build src/RoleRadar.Api`, then `npx --yes openapi-typescript@7.10.1 contracts/openapi/roleradar-api.json -o contracts/client/api-types.ts`.
  - Check that `pnpm --filter web run typecheck` still passes.
- [X] T029 [US1] Wire the Identity host. In `src/RoleRadar.Identity/Program.cs`, call `builder.Logging.ClearProviders().AddRoleRadarLogging("identity", LogStream.Split)` **before** any test configuration hook runs: before Identity's equivalent of `configureBuilder`, or before the point where `IdentityContractTestServer` adds its own services. This preserves test capture providers, for the same reason as T027. Add `src/RoleRadar.Identity/Observability/CorrelationMiddleware.cs`, which behaves the same as the API middleware in T027, and register it before Duende and the Razor pages. T013 green.
- [X] T030 [P] [US1] Wire the CLI in `src/RoleRadar.Cli/Program.cs`.
  - Replace `AddConsole` with `AddRoleRadarLogging("cli", LogStream.StdErr)`.
  - Keep `ResolveLogLevel`: `--verbose`, then `ROLERADAR_LOG_LEVEL`, otherwise Warning.
  - Open one process-wide `{ CorrelationId = CorrelationId.New() }` scope. This is the run id.
- [X] T031 [P] [US1] Wire the MCP host.
  - In `src/RoleRadar.Mcp.Stdio/Program.cs`, replace `AddConsole` with `AddRoleRadarLogging("mcp", LogStream.StdErr)`.
  - In `src/RoleRadar.Mcp.Stdio/Filters/UnknownArgumentValidationFilter.cs`, open a per-tool-call `{ CorrelationId }` scope, and replace its two `Console.Error.WriteLine` calls with `ILogger` calls.
  - Confirm the existing stdout-hygiene tests stay green (FR-014).
- [X] T032 [P] [US1] Set `ENV ROLERADAR_LOG_FORMAT=json` in the `mcp` and `cli` final stages of `Dockerfile`, and in `deploy/Dockerfile.api`, `deploy/Dockerfile.identity` and `deploy/Dockerfile.web`. Do not edit any compose file for this.
- [X] T033 [US1] Implement the pure module `web/app/lib/observability.ts` (`formatRecord`, `scrub`, `acceptCorrelationId`, `newCorrelationId`). Add it to the `mutate` list in `web/stryker.config.mjs`, and add its test to `web/vitest.stryker.config.ts`. T014 green.
- [X] T034 [US1] Implement `web/app/lib/log.server.ts`: `log.debug`, `log.info`, `log.warn` and `log.error`, with `service: "web"` and the correlation id taken from the request store. Export the store accessor from the module that owns the `AsyncLocalStorage` in `web/server.ts`, so routes and libs can read it without importing `server.ts`. T015 green.
- [X] T035 [US1] Update `web/server.ts`. T016 green, and `web/tests/tooling/server.test.ts` green.
  - The store becomes `{ nonce, correlationId }`.
  - The header is set on every response, before static and health handling.
  - Each request writes one `RequestCompleted` record, with no query string.
  - Replace each existing `console.*` call (`:35`, `:191`, `:215`, `:221`, `:230`) with `log.*` at the correct level. The listening line becomes `info`.
  - Update `web/tests/tooling/server.test.ts:46`. It spies on `console.error` and expects "Production requires TLS". Change it to capture `process.stderr.write` (or to assert through the logger) and expect the same message in a `fatal` or `error` record.
- [X] T036 [US1] Replace the remaining `console.*` calls with `log.*`, at the same levels, in `web/app/entry.server.tsx`, `web/app/lib/oidc.server.ts`, `web/app/lib/session.server.ts`, `web/app/routes/auth.callback.tsx` and `web/app/routes/auth.logout.tsx`. Leave `web/dev.js` alone.
  - Update `web/tests/routes/auth.test.ts:169`, `:188` and `:317`. They spy on `console.warn`; change them to assert through the logger or by capturing `process.stderr.write`, keeping the same message expectations.
  - In `web/eslint.config.js`, add the rule `no-console: "error"`, with an override that allows console use only in `web/dev.js` and `web/tests/**`.
  - `pnpm --filter web run lint` and `test` must pass.
- [X] T037 [US1] In `web/app/lib/api-client.server.ts`, add `X-Correlation-ID` from the request store to `createApiClient` (headers) and to `fetchOData`. In `web/app/lib/oidc.server.ts`, set `customFetch` on the `openid-client` configuration to add the header.
  - Do not add timeouts or retries here; they belong to US3.
  - Do not touch `mapApiError` (RR-64).

  T017 green.
- [X] T038 [US1] Add a top-level `x-logging: &default-logging` block (`driver: json-file`, `options: { max-size: "10m", max-file: "3" }`) to `deploy/docker-compose.yml` and `deploy/docker-compose.apps.yml`, and set `logging: *default-logging` on every service. Confirm that `IsolatedComposeDefaultCommandsIntegrationTests` still finds every string it replaces (`container_name: roleradar-mongo`, the `127.0.0.1:27017:27017` port, the volume names). Leave `deploy/demo/docker-compose.yml` untouched.
- [X] T039 [US1] Create `deploy/docker-compose.logs.yml` with one service, `logs`:
  - image `amir20/dozzle:<tag>@sha256:<digest>`, pinning the latest stable release that is at least two weeks old, with the digest read from the registry for that tag;
  - port `127.0.0.1:5004:8080`;
  - volume `/var/run/docker.sock:/var/run/docker.sock:ro`;
  - environment `DOZZLE_NO_ANALYTICS=true`, plus a `DOZZLE_FILTER` limited to the RoleRadar compose project label;
  - `read_only: true`, `cap_drop: [ALL]`, `security_opt: [no-new-privileges:true]`;
  - the same `x-logging` caps.

  Extend the `docker-compose` ecosystem entry in `.github/dependabot.yml` to cover the new file. Keep the `docker` block's `/` and `/deploy` entries that `ScriptPortabilityTests` asserts. T018 green, and `tests/RoleRadar.Tests.Unit/Demo/DemoAbsentFromDefaultsTests.cs` green.
- [X] T040 [US1] Make T019, T020, T021 and T022 green. Fix any leak at its source first, by not logging the value; the scrubber is only the last line of defense. Then make T023 green with `pwsh ./scripts/test-e2e-real.ps1`.

### STOP: US1 review gate

- [X] T041 [US1] Run each command below and report its pass and fail counts from the output. Say what could not be run. Then stop for review.
  - `dotnet build RoleRadar.sln -c Release -v q --nologo -warnaserror`
  - `pwsh ./scripts/test-fast.ps1`
  - `dotnet test tests/RoleRadar.Tests.Integration -c Release --filter "FullyQualifiedName~Redaction|FullyQualifiedName~HostLogFormat"`
  - `dotnet test tests/RoleRadar.Tests.Contract -c Release --filter "FullyQualifiedName~Correlation|FullyQualifiedName~IdentityRedaction"`
  - `pnpm --filter web run typecheck`, `lint`, `test`, `build`, `test:e2e`
  - `bash scripts/linux-web-check.sh` (from Git Bash)
  - `pwsh ./scripts/test-images.ps1`
  - `pwsh ./scripts/test-e2e-real.ps1`
  - `pre-commit run --all-files`

## Phase 4: User Story 2a - Actor defaults removed (P2)

**Goal**: A command without a named actor no longer compiles (R-010).

- [X] T042 [US2] Write `tests/RoleRadar.Tests.Architecture/CommandActorArchitectureTests.cs` first.
  - Every public record in `RoleRadar.Application` that implements `ICommand<TResponse>` (`src/RoleRadar.Application/Abstractions/Messaging.cs`) and has a constructor parameter named `Actor` must have `HasDefaultValue == false` on it.
  - A sentinel requires at least 5 such records, so the check cannot pass vacuously.

  Show red. Today's defaults include `mcp-stdio`, `agent` (`src/RoleRadar.Application/Tracking/Commands/ApplicationCommands.cs:31`) and `system` (`src/RoleRadar.Application/Tracking/Commands/RecordCompanyCommand.cs:43`).
- [X] T043 [US2] Remove every `Actor` default from the command records in `src/RoleRadar.Application/**/Commands/*.cs`.
  - Remove the handlers' `string.IsNullOrWhiteSpace(command.Actor) ? "<default>" : ...` fallbacks. A blank actor now returns a `ValidationError` `Result` and is never thrown (plan D5, ADR 0041). Add a unit test for one handler's blank-actor result first and show it red.
  - Fix every compile error by naming the caller's actor explicitly:
    - `"mcp-stdio"` in `src/RoleRadar.Mcp.Stdio/Tools/*.cs`;
    - the existing `cli-user`, `cli-import`, `cli-import-tracker` and `demo-seed` in `src/RoleRadar.Cli/**`;
    - explicit fictional actors in tests.
  - Keep the API's `ApiActor` constants for now; T051 replaces them.

  T042 green, and the full solution builds with `-warnaserror`.

**Checkpoint**: `pwsh ./scripts/test-fast.ps1` is green.

## Phase 5: User Story 2b - Real actors, sign-in events, `audit list` (P2)

**Goal**: API writes name the signed-in subject. Audit entries carry the request id. Identity logs four sign-in events, none of them with the username. The owner can read the audit trail with `roleradar audit list`.

**Independent test**: In `roleradar audit list`, a write made through the API shows the subject as its actor. A failed sign-in and a successful one each produce exactly one event, and the failed one carries no username.

### Tests first (US2)

- [X] T044 [P] [US2] Write the contract tests in `tests/RoleRadar.Tests.Contract/Api/ApiActorContractTests.cs` (FR-015, SC-004). For each API write, call `ApiContractTestHost` with a test JWT whose `sub` is a fictional v7 id. The writes are the two `RecordApplicationCommand` paths in `ApplicationsEndpoints.cs` (`:79`, `:124`), the company record and the posting transition. Read the newest `audit_log` entry and assert that `actor` equals the subject and is never `api-user`, `agent` or `system`. Show red.
- [X] T045 [P] [US2] Write the integration tests in `tests/RoleRadar.Tests.Integration/Cli/CliActorIntegrationTests.cs` (FR-016). Run these commands against Testcontainers Mongo:
  - `contact add` and `contact redact`;
  - `import` (`src/RoleRadar.Cli/Commands/ImportCommand.cs`);
  - `tracker-import` (`src/RoleRadar.Cli/Commands/TrackerImportCommand.cs`);
  - `demo seed`, against its own guarded throwaway database, as the 012 tests do.

  Assert that every new audit entry's actor starts with `cli-` or equals `demo-seed`, and that none is empty or a placeholder. Green after T043 is acceptable, since this test pins the behavior. Record whether it was red or green on its first run.
- [X] T046 [P] [US2] Write the tests that pin request correlation on audit entries (FR-017). Show red.
  - Add `tests/RoleRadar.Tests.Contract/Audit/RequestCorrelationAuditContractTests.cs`. With a scoped `IRequestContext` holding `CorrelationId = "test-corr-0001"`:
    - Both choke points write `request_correlation_id: "test-corr-0001"`: `MongoTrackingTransactionContext.AppendAuditEntryAsync` and `MongoNoteRepository.MapAuditToDocument`.
    - The field is absent when the context is null.
    - `AuditLogEntry.Rehydrate` reads it back.
    - The existing `details.after.correlation_id` is unchanged (FR-008).
  - In `tests/RoleRadar.Tests.Contract/Api/ApiActorContractTests.cs`, add one more case: an API write sent with `X-Correlation-ID: test-corr-0002` stores that value.
- [X] T047 [P] [US2] Write the contract tests in `tests/RoleRadar.Tests.Contract/Identity/SignInEventContractTests.cs` (FR-018, FR-019, SC-005, SC-011). Use a fictional owner with a TOTP key on `IdentityContractTestServer`, and capture logs with `FormatterLogCapture` and the production formatter. Show red.
  - **Non-vacuous capture first:** before the event and no-username checks, assert that at least one line was captured and that the expected event line is present.
  - A wrong password for an existing user writes exactly one `SignInFailed` (5002) with `subject`.
  - An unknown username writes exactly one `SignInFailed` with no `subject`.
  - The correct password with a wrong code writes exactly one `OneTimeCodeFailed` (5004).
  - The correct password and code write exactly one `SignInSucceeded` (5001) with `subject`.
  - The fifth consecutive failure writes `SignInFailed`, then `AccountLockedOut` (5003).
  - Every event carries a `correlationId`.
  - No line contains the typed username, the password or the code.
- [X] T048 [P] [US2] Write the contract suite for `IAuditLogReader` in `tests/RoleRadar.Tests.Contract/Audit/AuditLogReaderContractTests.cs` (Principle II, data-model.md). Show red.
  - Owner isolation: a second fictional owner's entries are never returned.
  - Order: `occurred_at desc, _id desc`.
  - Keyset paging stays stable when entries are appended between pages.
  - Each filter: `EntityType`, `EntityId`, `Actor`, `FromUtc` (inclusive), `ToUtc` (exclusive).
  - `Limit` is honored, and `NextCursor` is null on the last page.
  - The projection never includes `appended_text`, `details.before`, `details.after` or the content summaries.
  - `IncludeChangedFields` returns field names only.
  - Index `ix_audit_log_entity` `(owner_id: 1, entity_type: 1, entity_id: 1, occurred_at: -1, _id: -1)` exists after bootstrap.
- [X] T049 [P] [US2] Write the validation unit tests in `tests/RoleRadar.Tests.Unit/Audit/ListAuditEntriesQueryTests.cs`, quoting the data-model rules. Show red.
  - `Limit` must be 1 to 100, default 25; `0`, `101` and `-1` are `ValidationError`.
  - `EntityType` must be in the known entity-type set.
  - `EntityId` is 1 to 128 characters.
  - `Actor` is 1 to 256 characters.
  - `FromUtc < ToUtc` when both are set.
  - A malformed `Cursor` is `ValidationError`, and the cursor codec round-trips `(occurred_at, _id)`.
  - An offline store returns `DatastoreOffline`.
  - Nothing throws for an expected failure.
- [X] T050 [P] [US2] Write the CLI integration tests in `tests/RoleRadar.Tests.Integration/Cli/AuditListCliIntegrationTests.cs`, per `specs/013-operability/contracts/cli-audit-list.md`. Show red.
  - Text output is newest first, one line per entry: `occurredAt entityType entityId operation actor req=<id>|-`, followed by `next: <cursor>` when more entries exist.
  - `--json` returns `{ ok, data: { items, nextCursor } }`.
  - `--fields` adds `changedFields`, never values.
  - A stored actor and entity id containing `\u001b[31m` and `\r\n` print with `?` in place of each control character.
  - No output contains a sentinel appended note text.
  - Exit codes:
    - `0` on success and on an empty page;
    - `2` for `--limit 0`, `--limit 101`, `--from` after `--to`, and a malformed `--cursor`;
    - `1` when Mongo is unreachable, with no connection-string credential in the message.
  - The `audit_log` count and every other collection's count are unchanged after the command (SC-009).
  - In `tests/RoleRadar.Tests.Contract/OpenApi/ApiOpenApiContractSnapshotTests.cs`, add a case that no OpenAPI path contains `audit` (FR-020).

### Implementation (US2)

- [X] T051 [US2] Add `src/RoleRadar.Api/Authentication/HttpActorAccessor.cs`, a scoped service that returns the authenticated `sub` claim (`MapInboundClaims=false` is already set). T044 green, and both updated tests green.
  - Pass `Actor: actor.Subject` at the 4 command calls in `src/RoleRadar.Api/Endpoints/ApplicationsEndpoints.cs`, `CompaniesEndpoints.cs` and `PostingsEndpoints.cs`.
  - Delete the three `ApiActor = "api-user"` constants.
  - Update the existing tests that assert the old actor (one of the intended changes in SC-008):
    - `tests/RoleRadar.Tests.Integration/Api/ApiMcpParityIntegrationTests.cs:158` (and its comment at `:151`) now expects the test subject;
    - `tests/RoleRadar.Tests.Integration/Containers/RealStackEndToEndTests.cs:374-387` now expects the harness owner's subject for the posting transition and the application creation.
- [X] T052 [US2] Add `src/RoleRadar.Application/Abstractions/IRequestContext.cs` (`string? CorrelationId { get; }`), registered scoped, and stamp it on audit entries. T046 green. `DerivedFieldWriterArchitectureTests` and `LayeringAndAppendOnlyArchitectureTests` stay green.
  - Wire it in the hosts:
    - API: `src/RoleRadar.Api/Observability/HttpRequestContext.cs`, set by `CorrelationMiddleware`.
    - CLI: `src/RoleRadar.Cli/RunRequestContext.cs`, using the same run id as the T030 scope.
    - MCP: a per-tool-call implementation. First confirm that the ModelContextProtocol 2.3.0 server resolves tools in a per-request DI scope. If it does not, have the call filter create one scope per call (plan D6).
    - Tests: a null default.
  - Confirm the lifetimes recorded in plan D6. `INoteRepository` and `ITrackingTransactionContext` are scoped, so they may take `IRequestContext`. `IMongoClient` and `MongoReadinessProbe` are singletons and must not. Keep `ValidateScopes` on in the test hosts.
  - Add an optional `RequestCorrelationId` to `src/RoleRadar.Domain/Audit/AuditLogEntry.cs`, in the factories and in `Rehydrate`.
  - Add `request_correlation_id` to `TrackingBsonFieldNames`, and stamp it in `src/RoleRadar.Infrastructure/Mongo/MongoTrackingTransactionContext.cs` and `src/RoleRadar.Infrastructure/Mongo/MongoNoteRepository.cs`.
  - Change no handler.
- [X] T053 [US2] Add the four `LoggerMessage` events (5001 `SignInSucceeded`, 5002 `SignInFailed`, 5003 `AccountLockedOut`, 5004 `OneTimeCodeFailed`) to `src/RoleRadar.Observability/SecurityEvents.cs`. Raise them in `src/RoleRadar.Identity/Pages/Account/Login.cshtml.cs` at each outcome of the password step (`:144-169`) and the TOTP step (`:81-136`). T047 green, and T020 stays green.
  - On a failed password, call `FindByNameAsync` and pass `subject` only when it returns non-null.
  - Never pass the username, password or code to any logger.
  - Do not raise Duende `UserLoginFailureEvent`.
  - Leave the lockout policy unchanged: 5 attempts, 15 minutes.
- [X] T054 [US2] Add `src/RoleRadar.Application/Abstractions/IAuditLogReader.cs` and `src/RoleRadar.Application/Audit/Queries/ListAuditEntriesQuery.cs`. T049 green.
  - The query file holds the query, `ListAuditEntriesQueryHandler`, `AuditListPage`, `AuditListItem`, the cursor codec and the validators.
  - `AuditListItem` has no field for appended text, before or after values, or summaries.
  - Register the handler with `AddQueryHandler` in `src/RoleRadar.Application/DependencyInjection.cs`.
- [X] T055 [US2] Implement `src/RoleRadar.Infrastructure/Mongo/MongoAuditLogReader.cs`. T048 green.
  - Always filter on `owner_id`.
  - Sort by `occurred_at desc, _id desc`.
  - Fetch `Limit + 1` entries.
  - Project only the listed fields.
  - Return `DatastoreOffline` when the store is not ready.

  Add `ix_audit_log_entity` to `src/RoleRadar.Infrastructure/Mongo/MongoBootstrapAndIndexes.cs`, and register the reader in `src/RoleRadar.Infrastructure/DependencyInjection.cs`.
- [X] T056 [US2] Add `src/RoleRadar.Cli/Commands/AuditCommand.cs` with a `list` subcommand, following the `ContactCommand get` pattern (text or `--json`, exit codes 0, 1 and 2, `OwnerId.Default`) and the options in `contracts/cli-audit-list.md`. T050 green, and the MCP tool count is still 12 (`McpToolSurfaceArchitectureTests`).
  - Neutralize control characters by replacing each with `?`.
  - Register it in `src/RoleRadar.Cli/Program.cs`, and update `CliServiceWiringArchitectureTests` if it lists commands.
  - Document it in `docs/cli-reference.md`.

### STOP: US2 review gate

- [X] T057 [US2] Run each command below and report its pass and fail counts from the output. Say what could not be run. Then stop for review.
  - `dotnet build RoleRadar.sln -c Release -v q --nologo -warnaserror`
  - `pwsh ./scripts/test-fast.ps1`
  - `dotnet test tests/RoleRadar.Tests.Contract -c Release --filter "FullyQualifiedName~Actor|FullyQualifiedName~SignIn|FullyQualifiedName~AuditLogReader|FullyQualifiedName~RequestCorrelation|FullyQualifiedName~IdentityRedaction"`
  - `dotnet test tests/RoleRadar.Tests.Integration -c Release --filter "FullyQualifiedName~CliActor|FullyQualifiedName~AuditListCli|FullyQualifiedName~Redaction|FullyQualifiedName~ApiMcpParity"`
  - `pwsh ./scripts/test-e2e-real.ps1` (the real-stack lane; the actor assertions changed in T051)
  - `pwsh ./scripts/test-images.ps1` (the images lane)
  - `pnpm --filter web run typecheck` (the contract was not regenerated in US2, so it should be unchanged)
  - `pre-commit run --all-files`
  - Confirm by inspection that the MCP tool list is unchanged and that `git diff --stat` shows no file under `deploy/demo/`.

## Phase 6: User Story 3 - Health that tells the truth (P3)

**Goal**: Separate liveness and readiness. A bounded 503 when the database is lost, with recovery and no restart. Web-to-API calls that time out and retry only reads.

**Independent test**: Pause the dedicated database. Within 5 s, a request returns a 503 `service_unavailable` envelope. Unpause it, and the same request succeeds within 30 s without restarting the API.

### Tests first (US3)

- [X] T058 [P] [US3] Write the health tests per `contracts/health.md`. Never start a host that cannot start: Identity migrates SQLite at startup (`src/RoleRadar.Identity/Program.cs:123`). Show red.
  - **Host-level contract tests** in `tests/RoleRadar.Tests.Contract/Api/ApiHealthContractTests.cs` and `tests/RoleRadar.Tests.Contract/Identity/IdentityHealthContractTests.cs`:
    - `/health/live` returns 200 `{"status":"Healthy"}`, and `/health` matches it.
    - `/health/ready` returns 200 when the readiness check passes, and 503 `{"status":"Unhealthy"}` when the host's readiness check is replaced by a stub that returns `Unhealthy`. For the API, an unreachable Mongo address with a short readiness timeout is also acceptable, because the API starts without Mongo.
    - Bodies contain no host, endpoint or error text.
  - **Direct check test** in `tests/RoleRadar.Tests.Unit/Identity/IdentityReadinessCheckTests.cs`: construct the real `IdentityReadinessCheck` with an `IdentityAppDbContext` on an unopenable SQLite path (a file inside a non-existent temp directory) and assert `Unhealthy` within the 2 s bound. A path that can be opened gives `Healthy`.
  - **Default URL**: `RoleRadar.Api.Program.DefaultHealthcheckUrl` and `RoleRadar.Identity.Program.DefaultHealthcheckUrl` both equal `http://127.0.0.1:8080/health/ready`. `ContainerHealthCheckLaunchedHostTests` keeps setting its own URL and stays green.
- [X] T059 [P] [US3] Write the unit tests in `tests/RoleRadar.Tests.Unit/Api/ErrorEnvelopeMappingTests.cs` and `tests/RoleRadar.Tests.Unit/Api/ApiExceptionHandlerTests.cs`. Show red.
  - `FromResultError(DatastoreOffline)` returns 503 `service_unavailable` with `Retry-After: 5`.
  - 400, 404 and 409 are unchanged.
  - The exception handler maps an exception that `DatastoreFailure.IsUnavailable` classifies as unavailable to 503, and anything else to 500 `internal_error`.
  - Neither body contains the exception message, the stack, a host or a remediation command.
  - Both bodies carry `correlationId`.
- [X] T060 [P] [US3] Write `tests/RoleRadar.Tests.Unit/Infrastructure/DatastoreFailureTests.cs`. Show red.
  - `IsUnavailable` is true for `MongoConnectionException`, a server-selection `TimeoutException` and `MongoNotPrimaryException`.
  - It is false for `MongoWriteException` (duplicate key), `ArgumentException` and `InvalidOperationException`.
- [X] T061 [US3] Write the integration test in `tests/RoleRadar.Tests.Integration/Api/DatastoreOutageIntegrationTests.cs` (FR-024, FR-025, SC-006). Show red: today a write returns 400 and a read returns 500.
  - Attributes: `[Trait("Category","Slow")]`, the default lane, and its own xUnit collection (like `StoppedMongoReadinessCollection`).
  - Start a dedicated Testcontainers Mongo replica set on a host port assigned at runtime: Testcontainers' random port or an OS-assigned free port.
  - The port must never be 27017 or any port the dev or demo stacks publish (27017, 5001 to 5004, 5101 to 5103). If the assigned port is on that list, release it and pick again instead of failing.
  - Start the API in-process through `RoleRadar.Api.Program.CreateApp` against that container, the way `tests/RoleRadar.Tests.Integration/ODataTestHost.cs` does.
  - **Primary method: `docker pause` and `docker unpause` of the dedicated container**, through the Testcontainers container API (`PauseAsync` and `UnpauseAsync`) or the Docker client.
    1. While paused, a read (`GET /odata/Postings`) and a write (a posting transition) each return 503 within 5 s, measured with a `Stopwatch`. Each body is an envelope with `errorCode` `service_unavailable` and a non-empty `correlationId`.
    2. Unpause.
    3. Poll the same read until it returns 200, with a 30 s limit. Do not dispose or recreate the API host.
  - Add a stop-and-start variant only if pausing does not exercise the connection-failure path. That variant binds a free port chosen at runtime, under the same exclusions, so the address survives the restart.
- [ ] T062 [P] [US3] Write `web/tests/unit/api-policy.test.ts`, using a fake `fetch` and fake timers. It is red until `shouldRetry`, `backoffMs` and `fetchWithPolicy` exist. The policy it checks (SC-007):
  - `GET` and `HEAD` are attempted at most 3 times, on a network error, a per-attempt timeout (5 s), 502, 503 or 504, with a 200 ms and then a 500 ms backoff.
  - `POST`, `PATCH`, `PUT` and `DELETE` are attempted exactly once, with a 10 s timeout.
  - 400, 401, 404, 409 and 500 are never retried.
  - A caller abort is not retried and propagates.
  - Every attempt carries the same `X-Correlation-ID`.
  - The `insights-*` loaders use the policy and keep their 5 s bound.

### Implementation (US3)

- [X] T063 [US3] Implement `src/RoleRadar.Infrastructure/Mongo/DatastoreFailure.cs` (`IsUnavailable(Exception)`). T060 green.
- [X] T064 [US3] Map outages to 503. T059 green.
  - In `src/RoleRadar.Api/Envelopes/ErrorEnvelope.cs` and `ErrorResults.cs`, map `DatastoreOffline` to 503 `service_unavailable` with `Retry-After: 5`.
  - Add `src/RoleRadar.Api/Observability/ApiExceptionHandler.cs`, an `IExceptionHandler`. It returns 503 when `DatastoreFailure.IsUnavailable` matches and 500 `internal_error` otherwise, and logs the exception through the scrubbing formatter.
  - In `src/RoleRadar.Api/Program.cs`, register it with `AddExceptionHandler` and `UseExceptionHandler`, right after the correlation middleware.
  - `HttpUserOwnerIdProvider`'s `SecurityException` path keeps today's status; confirm with the existing auth tests.
  - Regenerate the OpenAPI document, `contracts/client/api-types.ts` and the `ApiOpenApiContractSnapshotTests` snapshot if response metadata changed.
- [X] T065 [US3] Add the health checks and endpoints. T058 green.
  - `src/RoleRadar.Api/Observability/ApiReadinessCheck.cs` wraps `INoteRepository.CheckReadinessAsync`, bounded at 2 s.
  - `src/RoleRadar.Identity/Observability/IdentityReadinessCheck.cs` wraps `IdentityAppDbContext.Database.CanConnectAsync`, bounded at 2 s.
  - In both `Program.cs` files:
    - `AddHealthChecks()`;
    - `MapHealthChecks("/health/live")`, with no checks;
    - `MapHealthChecks("/health/ready")`, with tag `ready`;
    - `/health` as an alias of live;
    - each writes the minimal `{"status":...}` body;
    - add `public const string DefaultHealthcheckUrl = "http://127.0.0.1:8080/health/ready"` and use it as the `--healthcheck` default.
  - Leave every compose healthcheck line unchanged.
- [X] T066 [US3] Make T061 green. If the 5 s bound is missed, check `MongoOptions.ReadinessTimeout` (3 s) and the server-selection timeout before changing anything. Record the measured 503 and recovery times from the test output.
- [ ] T067 [US3] Implement the outbound policy. T062 green, and the existing insights tests stay green.
  - In `web/app/lib/observability.ts`: `shouldRetry` and `backoffMs`.
  - In `web/app/lib/api-client.server.ts`: `fetchWithPolicy`, combining `AbortSignal.timeout` with the caller's signal through `AbortSignal.any`. Use it in `fetchOData` and as the `fetch` option of the `openapi-fetch` client.
  - Replace the local timeouts in `web/app/lib/insights-counts.server.ts` and `web/app/lib/insights-stats.server.ts` with the policy.
  - No retry library (FR-027). Do not touch `mapApiError` (RR-64).

### STOP: US3 review gate

- [ ] T068 [US3] Run each command below and report its pass and fail counts from the output, plus the measured 503 and recovery times. Say what could not be run. Then stop for review.
  - `dotnet build RoleRadar.sln -c Release -v q --nologo -warnaserror`
  - `pwsh ./scripts/test-fast.ps1`
  - `dotnet test tests/RoleRadar.Tests.Integration -c Release --filter "FullyQualifiedName~DatastoreOutage"`
  - `pnpm --filter web run typecheck`, `lint`, `test`, `build`, `test:e2e`
  - `bash scripts/linux-web-check.sh`
  - `pwsh ./scripts/test-images.ps1` (the images' readiness healthcheck under compose-style hardening)
  - `pwsh ./scripts/test-e2e-real.ps1`
  - `pre-commit run --all-files`

## Phase 7: Polish and cross-cutting

- [ ] T069 [P] Write `docs/operations/logs-and-health.md` and link it from the existing docs list in `README.md` (no new section). Cover:
  - the three ways to read logs: `docker compose logs`, text mode outside compose, and the opt-in viewer at `http://127.0.0.1:5004`, started with `docker compose -f deploy/docker-compose.logs.yml up -d`;
  - searching by `X-Correlation-ID`;
  - `ROLERADAR_LOG_FORMAT` and the level variables;
  - the log caps;
  - the health endpoints and what a 503 means;
  - `roleradar audit list`;
  - the never-logged list from `contracts/log-records.md`.
- [ ] T070 [P] Update `docs/threat-model.md` with:
  - the logging redaction control and the tests that prove it;
  - sign-in events without the username;
  - the viewer's read-only Docker socket risk, and why it is accepted (opt-in, loopback-only, the owner's machine).
- [ ] T071 Run the scope checks and report counts only.
  - `git diff --stat` shows no change under `deploy/demo/` and none to `DemoModeGuard` (FR-029).
  - No new script exists under `scripts/` or `web/scripts/` (FR-030).
  - `git diff Directory.Packages.props web/package.json package.json` shows no new package. In particular, no retry, resilience, OpenTelemetry or exporter package was added (FR-027, FR-028).
  - Run `pwsh ./scripts/denylist-check.ps1` in its default mode.
- [ ] T072 Run the full AGENTS.md verification list for the phase-end gate. Report totals and coverage from the output only, and say what could not be run.
  - `pnpm --filter web run typecheck`, `lint`, `test`, `build`, `test:e2e`
  - `bash scripts/linux-web-check.sh`
  - `pwsh ./scripts/test-fast.ps1`
  - `pre-commit run --all-files`
  - `pwsh ./scripts/review-gate.ps1`
- [ ] T073 Record the Dozzle image tag and digest pinned in `deploy/docker-compose.logs.yml` (T039) in `docs/adr/0041-operability-logging-correlation-health.md`, under Decision Outcome. In the status line, replace the T073 reference with the pinned value. The ADR is already Accepted (T002). This is the last task: stop for review and do not commit.

## Dependencies

- Setup (T001-T002) comes before Foundational (T003-T008), which comes before every user story. T008 (test log sink) must be done before any test that captures logs: T011-T013, T019, T020, T022 and T047.
- **US1** (T009-T041) depends only on Foundational. It is the MVP.
- **US2a** (T042-T043) depends on Foundational and starts after the US1 gate. It touches the Application layer and the MCP command call sites, so do not run it in parallel with the MCP wiring in T031.
- **US2b** (T044-T057) depends on:
  - US2a;
  - US1's middleware (T027), for `HttpRequestContext`;
  - US1's formatter (T026), for log capture in T047.
- **US3** (T058-T068) depends on US1's `ErrorResults` (T028) and on the `fetch` header work (T037). It does not depend on US2.
- Polish (T069-T073) comes after all stories. T073 is last.

Within each story, tests come before implementation, and each test task is shown red before its implementation task starts.

## Parallel opportunities

- Foundational: T003 and T005 together. T008 can run alongside T005-T007.
- US1 tests: T009-T022 are all `[P]`. They are separate files across .NET Unit, Contract and Integration and the web unit and tooling tests.
- US1 implementation:
  - T030, T031 and T032 can run in parallel after T026.
  - The web tasks (T033-T037) can run alongside the .NET tasks (T027-T029).
- US2 tests: T044 and T046-T050 can run in parallel. T045 can run any time after T043.
- US3 tests: T058, T059, T060 and T062 can run in parallel. T061 comes after T060.
- Polish: T069 and T070 together.

## Implementation strategy

1. **MVP = US1.** Stop at T041. Readable, correlated, redacted logs deliver most of the operational value and unblock everything else.
2. **US2a** as its own small, mechanical phase, so the wide compile-driven edit gets its own review.
3. **US2b**, then the gate at T057.
4. **US3**, then the gate at T068.
5. **Polish** and the full gate. T073 records the Dozzle pin in the already-accepted ADR 0041.

## Notes

- Baseline counts (T001, 2026-11-09): Release build with `-warnaserror` passed (0 warnings, 0 errors). `test-fast.ps1`: Architecture 58 passed, Unit 728 passed, Contract 215 passed, 0 failed.
- Red runs, recorded before each implementation (2026-11-09):
  - T003: build failed, CS0234 (`RoleRadar.Observability` missing).
  - T005: 3 failed of 11 (`Dockerfiles_CopyTheObservabilityProjectBeforeRestore` for Dockerfile, deploy/Dockerfile.api, deploy/Dockerfile.identity).
  - T008: build failed, CS2001 (linked `FormatterLogCapture.cs` missing).
  - T009 to T011: build failed with 40 errors (28 CS0103, 12 CS0246) for the missing Observability types.
  - T012, T013, T020: 11 failed of 11 (no header, no envelope id, no RequestCompleted records, no Identity capture).
  - T014 to T017, T021: 5 suites failed to resolve the new modules (`observability`, `log.server`, `request-context.server`, `setRequestListenerForTesting`).
  - T018: 5 failed of 6 (no `logging` caps, no `docker-compose.logs.yml`).
  - T019, T022: 6 failed of 7.
  - New scrub rules found during implementation, each shown red first: free-text `key=value` pairs (added with T021), and JSON pairs plus the OIDC keys `state`, `nonce`, `code_challenge`, `code_verifier` (7 failed of the new cases). The second came from a real leak caught by T013: Duende logs the raw authorize request, including `state` and `code_challenge`.
  - The message-template case in T011 (`EveryField_IsScrubbed`, 2 failed) found that values passed under a sensitive template name were redacted in `props` but still present in the rendered message. Fixed in the formatter.
- Tests that started green instead of red:
  - T005 `.dockerignore` case: the existing `src/**` allow rules already cover the new project.
  - T018 "the demo stack never includes the log viewer": the demo file was never meant to change.
  - T019 `NoteHandlers_NeverLogNoteBodiesOrAppendedText`: the handlers already log only command names. This test pins that behavior.
  - T003 `Identity_DoesNotReferenceMongo`: pins today's behavior (FR-019).
- Implementation decisions not spelled out in the plan:
  - **Envelope id (T028):** `ErrorEnvelope.CorrelationId` is a property initializer that reads the current request through `ErrorResults` (`HttpContextAccessor`). It is not passed by each producer, so all 15 producers and the OData filter carry it without edits. `[Required]` makes it a required `string` in the generated contract.
  - **Web request store (T030, T034):** the store moved to `web/app/lib/request-context.server.ts` and lives on `globalThis` under a registered symbol. `server.ts` and the React Router build are bundled separately and must share one `AsyncLocalStorage`.
  - **Lint exemptions (T036):** the `no-console` exemptions also cover root tool configs (`*.config.{ts,js,mjs}`, for `vite.config.ts`'s dev message) and `web/scripts/**` (CI-only CLI scripts), as well as `dev.js` and `tests/**`.
  - **`LogStream` name:** suppressed CA1711 (the name ends in "Stream") with a justification, to keep the name used across the plan and ADR.
  - **MCP scope:** ModelContextProtocol 2.3.0 creates a DI scope per request by default (`ScopeRequests = true`), which answers the T052 question.
  - **Dozzle (T039):** v11.3.5, the newest release at least two weeks old (published 2026-10-24). It also turns off release checks, registry checks and avatars, and uses a tmpfs `/data`, its own project name and `/dozzle healthcheck`. Smoke-tested: healthy, HTTP 200 on 127.0.0.1:5004, then removed. Recorded in research R-005 and ADR 0041.
  - **Existing tests updated (mechanical, same expectations):** `web/tests/tooling/server.test.ts`, `web/tests/routes/auth.test.ts` (H3); `web/tests/unit/odata-fetch-signal.test.ts` (`fetchOData` now sends a `Request`); `web/tests/tooling/insights-e2e-isolation.test.ts` (the real-stack project also matches `*.real-stack.spec.ts`).
- Pre-existing failure, not caused by this feature: `web/tests/tooling/front-door-links.test.ts` > "opens with the badges and a light and dark hero picture before any section" fails on the README badge assertion. README.md and that test are unchanged on this branch.
- 2026-11-09 `/speckit-analyze` remediation: ADR 0041 accepted (T002); added T008 (log sink), T020 (Identity redaction in Contract), T021 (web redaction) and T022 (CLI and MCP JSON); renumbered from 69 to 73 tasks.
- US1 gate (T041, 2026-11-09), numbers from command output:
  - `dotnet build RoleRadar.sln -c Release -warnaserror`: 0 warnings, 0 errors.
  - `test-fast.ps1`: Architecture 63/63, Unit 843/843, Contract 226/226.
  - Integration `Redaction|HostLogFormat`: 7/7. Contract `Correlation|IdentityRedaction`: 11/11.
  - Web: typecheck clean; lint 0 errors, 1 warning (pre-existing `findByPlaceholderText` unused in `postings.component.test.tsx`); vitest 1224 passed, 1 failed (the pre-existing `front-door-links` README test); build passed.
  - Web `test:e2e`: 175 passed, 77 skipped, 1 failed. The failure was `net::ERR_NO_BUFFER_SPACE` on `page.goto` in `polish.spec.ts`, a Windows socket error. A re-run of `polish.spec.ts` passed 24/24.
  - `linux-web-check.sh`: script exit 1, caused only by the same pre-existing `front-door-links` failure in `test:coverage` (1222 passed, 2 skipped, 1 failed). Typecheck, lint (0 errors), build and e2e (154 passed) ran. No coverage summary was printed because the run aborts on the failed test, so the coverage thresholds were not confirmed in this run.
  - `linux-web-check.sh` re-run after rebasing onto main (PR 75 fixed the `front-door-links` failure), 2026-11-09: **PASS**, exit 0. Typecheck clean; lint 0 errors, 1 warning (same pre-existing one); `test:coverage` 99 files passed, 1223 tests passed, 2 skipped, 0 failed; build passed; e2e 154 passed, 99 skipped. Coverage summary (All files): statements 90.74, branches 83.27, functions 85.74, lines 91.27. Floors in `web/vitest.config.ts` are lines 80 and branches 75, so both are met (lines 91.27, branches 83.27).
  - `test-images.ps1` (images rebuilt from the current tree): 59/59.
  - `test-e2e-real.ps1` with throwaway `:test013` image tags (the dev stack's `:local` images untouched): 1/1, including the new cross-service correlation and JSON-line assertions. T023 was not run red before its implementation: the real-stack lane was run only once, at the end.
  - `pre-commit run --all-files`: 15 hooks passed, 1 skipped (no files to check).
- US2a (T042, T043, 2026-11-09), numbers from command output:
  - T042 red first: `CommandActorArchitectureTests` 1 failed, 1 passed (`NoCommandActorParameter_HasADefaultValue` failed; the sentinel `ActorParameterSentinel_FindsEnoughCommands_SoTheRuleIsNotVacuous` passed). Green after T043: 2 of 2.
  - T043 red first: `NoteHandlersUnitTests.ArchiveNoteCommandHandler_BlankActor_ReturnsValidationErrorAndWritesNothing` and `CreateNoteCommandHandler_BlankActor_ReturnsValidationErrorAndWritesNothing`, each with `""` and `"   "`: 4 failed, 0 passed. Green after T043: 4 of 4.
  - Implementation: new `src/RoleRadar.Application/Abstractions/CommandActor.cs` (`Validate` returns a `ValidationError` for a blank actor); called first in every command validator (the 4 note commands, `ImportMarkdown`, `RecordApplication`, `RecordCompany`, `RecordPosting`, `TransitionPosting`) and inline in the two contact handlers, which have no validator. The `string.IsNullOrWhiteSpace(...) ? "<default>" : ...` fallbacks and the contact `?? "cli-user"` are gone. `Actor` is now `string`, not `string?`, on the contact commands.
  - Positional order changed: C# requires a parameter without a default to come before the ones with defaults, so `Actor` moved to the first required position in nine records. `RecordCompanyCommand` and `RecordPostingCommand` have no required parameter, so `Actor` is now their first parameter. All callers use named arguments, and the build caught every positional use.
  - Callers: new `src/RoleRadar.Mcp.Stdio/Tools/McpActor.cs` (`"mcp-stdio"`) is used by `TrackingMcpTools` and `NoteMcpTools`; `ContactCommand.cs` in the CLI names `cli-user`. The API's `ApiActor` constants are untouched (T051).
  - 231 test call sites (compiler error CS7036; 11 more CS1503 errors disappeared with them) were updated mechanically by appending a named `Actor:` argument with the value the old default supplied (`mcp-stdio`, `cli-import`, `agent`, `system`), so no expectation changed. They are in 15 Contract, 8 Integration and 9 Unit files (85, 44 and 102 sites).
  - US2a checkpoint: `dotnet build RoleRadar.sln -c Release -warnaserror` 0 warnings, 0 errors; `test-fast.ps1` Architecture 65/65, Unit 847/847, Contract 226/226. Not run: the Integration lane (it compiles but was not executed, it needs Docker and is the US2b/review-gate lane).
- US2b part 1 (T044, T046, T051, T052, 2026-11-09), numbers from command output:
  - T044 red first: `ApiActorContractTests` 5 failed, 0 passed. Four failed with actor `"api-user"` instead of the subject; `ApiWrite_WithCorrelationHeader_StoresItOnTheAuditEntry` failed because `request_correlation_id` was absent.
  - T046 red first: `RequestCorrelationAuditContractTests` 3 failed, 4 passed (`TrackingChokePoint_StampsTheRequestCorrelationId`, `TrackingChokePoint_KeepsTheExistingDetailsAfterCorrelationId` and `NoteChokePoint_StampsTheRequestCorrelationId_AndReadsItBack` failed). **Started green:** the three "field absent without a context" cases and `Rehydrate_ReadsTheRequestCorrelationIdBack`, because I added an inert skeleton first (the `IRequestContext` interface, the `AuditLogEntry.RequestCorrelationId` property and optional constructor parameters that did nothing) so the tests compiled and failed on assertions instead of on missing types.
  - After T051 and T052: both files 12 of 12 green.
  - T052 MCP per-call scope: confirmed with `AuditSchemaAndOperationsContractTests.ShippedHosts_StampARequestCorrelationId_PerToolCall_AndPerCliRun` (two `company_record` calls to the shipped MCP server store two different ids; a CLI `contact add` stores its run id). **That test was written after the implementation and was not run red.** It passed 1 of 1 on its first run, so the server does resolve each tool call in its own scope and no extra scope in the call filter was needed.
  - Implementation: `HttpActorAccessor` (scoped, the `sub` claim) is passed as `Actor: actor.Subject` at the four API command calls, and the three `ApiActor = "api-user"` constants are gone. `IRequestContext` (Application) has a `NullRequestContext` default registered with `TryAddScoped`, and each host replaces it: `HttpRequestContext` (API, reads the id `CorrelationMiddleware` stored), `RunRequestContext` (CLI, `Program.RunId`), `McpRequestContext` (MCP, set per tool call by the call filter). Infrastructure stamps `request_correlation_id` in `MongoTrackingTransactionContext.AppendAuditEntryAsync` and `MongoNoteRepository.MapAuditToDocument`, and `MongoCompanyRepository` passes the context to the transaction context it creates. `MapDocumentToAudit` reads the field back. `IMongoClient`, `MongoReadinessProbe` and the singleton `MongoTrackingContextFactory` take no request context. No handler changed.
  - T051 existing tests updated (the intended SC-008 change): `ApiMcpParityIntegrationTests` now expects the token subject (test renamed `..._ApiWritesEmitMcpOperationNamesWithTheSubjectAsActor`), and `RealStackEndToEndTests` expects the harness owner's subject (`ownerSub`) for the transition, application creation and company update entries. The parity test passed 1 of 1. The real-stack test was **not run** (images lane; it compiles).
  - Part 1 checkpoint: `dotnet build RoleRadar.sln -c Release -warnaserror` 0 warnings, 0 errors; `test-fast.ps1` Architecture 65/65, Unit 847/847, Contract 239/239 (226 before, plus 5 + 7 + 1 new).
- US2b part 2 (T047, T053, 2026-11-09), numbers from command output:
  - Before part 2: `HttpActorAccessor` now reads only the `sub` claim (the `ClaimTypes.NameIdentifier` fallback is gone). `.editorconfig` sets `CA1848` and `CA2254` to `error`. A temporary `TempDirectLogProbe.cs` with `logger.LogInformation(...)` and `logger.LogWarning(value)` failed `dotnet build src/RoleRadar.Api -warnaserror` with 4 errors (CA1848 twice, CA2254, CA1873); it was removed, and the full solution builds with 0 warnings and 0 errors. `BeginScope` calls are unchanged. A few test files that log directly keep a file-level `#pragma warning disable CA1848, CA1873` from US1, which overrides the editorconfig severity in those files.
  - T047 red first: `SignInEventContractTests` 5 failed, 0 passed. Each failed at `AssertCaptureIsNotVacuous` on the "expected event present" step, after the line count and the login `RequestCompleted` assertions had passed, so the capture was non-vacuous and only the event was missing. Green after T053: 5 of 5, and `IdentityRedaction` and `IdentityCorrelation` stay green (10 of 10 together).
  - T053: `src/RoleRadar.Observability/SecurityEvents.cs` holds the four `LoggerMessage` events (5001, 5002, 5003, 5004), each taking only `subject`. 5002 for an attempt that matched no account is a second method, `UnmatchedAccountEvents.SignInFailed(logger)`, with the same id and name and no `subject` property at all (not a null one). It is a separate class because the generator rejects two methods with one event id in one class. `Login.cshtml.cs` calls `FindByNameAsync` after the password check and passes the subject only when a user is returned; no username, password or code is passed to any logger; Duende `UserLoginFailureEvent` is not raised; the lockout policy is unchanged.
  - Lockout reporting decision: an attempt that comes back locked out at the password step writes `SignInFailed` then `AccountLockedOut`, because Identity does not say whether that attempt or an earlier one started the lockout. At the code step the two cases are distinguishable: a code attempt on an already locked account writes only `AccountLockedOut`, and a wrong code that triggers the lockout writes `OneTimeCodeFailed` then `AccountLockedOut`. The fallback branch with no two-factor cookie writes the unmatched `SignInFailed`.
  - Two existing unit tests (`LoginModelPasswordStepErrorTests`, `LoginModelRazorPageTests`) now pass `NullLogger<LoginModel>.Instance` to the new constructor parameter; no expectation changed.
  - Part 2 checkpoint: `dotnet build RoleRadar.sln -c Release -warnaserror` 0 warnings, 0 errors; `test-fast.ps1` Architecture 65/65, Unit 847/847, Contract 244/244 (239 plus the 5 new).
- US2b part 3a (T048, T049, T054, T055, 2026-11-09), numbers from command output:
  - Skeleton first: so the tests compile and fail on assertions, I added inert stubs (`IAuditLogReader`, the query, item and page types, a codec that returned nothing, a validator that returned null, a reader that returned an empty page).
  - T049 red first: `ListAuditEntriesQueryTests` 22 failed, 22 passed. The 22 that started green are the accepted-input cases, the offline pass-through, the malformed-token `TryDecode`, the default limit and the `AuditListItem` member allowlist. Green after T054: 44 of 44.
  - T048 red first: `AuditLogReaderContractTests` 19 failed, 1 passed. The one that started green, `Projection_NeverCarriesAppendedTextBeforeAfterOrSummaries`, passed only because the stub returned no items, so I added an item-count assertion and it went red: 20 failed, 0 passed. Green after T055: 20 of 20. One failure after the first implementation run was a bug in my own test (`FromAndTo_AreNormalizedFromAnyOffset` built an offset from a UTC-kind `DateTime`); the reader was right and the test was fixed.
  - T054: `ListAuditEntriesQuery` (`OwnerId` is the `OwnerId` type; `default(OwnerId)` is a `ValidationError`, not an exception), `AuditListItem` (no member for appended text, before or after values or summaries), `AuditListPage`, `AuditEntityTypes` (note, company, posting, application, application_event, contact, criterion, search_run), `AuditCursorCodec` (URL-safe base64 of `v1:{unix ms}:{id}`), `ListAuditEntriesQueryValidator` and `ListAuditEntriesQueryHandler`, registered with `AddQueryHandler`. Every rule is a `Result` failure; nothing is thrown.
  - T055: `MongoAuditLogReader` always starts its filter with `owner_id == query.OwnerId.Value`, then adds the entity type, entity id (a GUID matches the binary form and the text form, since a legacy or foreign entry can hold text), actor, `from` (inclusive) and `to` (exclusive) clauses and the keyset clause `occurred_at < c OR (occurred_at == c AND _id < id)`. It sorts `occurred_at desc, _id desc`, fetches `Limit + 1`, and projects an allowlist (`_id`, `occurred_at`, `entity_type`, `entity_id`, `operation`, `actor`, `request_correlation_id`, plus `details.changed_fields` only with `IncludeChangedFields`). It checks readiness through `MongoReadinessProbe` first; a Mongo or timeout error during the read also returns `DatastoreOffline`, with a message that names no endpoint. `ix_audit_log_entity` is created in `MongoBootstrapAndIndexes`, and the reader is registered scoped in `AddRoleRadarInfrastructure`.
  - Part 3a checkpoint: `dotnet build RoleRadar.sln -c Release -warnaserror` 0 warnings, 0 errors; `test-fast.ps1` Architecture 65/65, Unit 891/891, Contract 264/264 (847 and 244 before, plus 44 and 20 new).
- US2b part 3b (T050, T056, T057, 2026-11-09), numbers from command output:
  - T050 red first: `AuditListCliIntegrationTests` 22 failed, 1 passed (the command did not exist yet). **Started green, vacuously:** `RunningTheCommand_ChangesNoCollectionCount_IncludingAuditLog`, because nothing ran, so I made it assert each run's exit code (0, 0, 0, 0, 2, 2, 2, and 1 for the unreachable store); then 23 failed, 0 passed. Green after T056: 23 of 23 on the first run, including `--limit -5` and `--limit abc` (both exit 2).
  - The OpenAPI case `OpenApiSnapshot_HasNoAuditPath_...` and a new `CliContainer_ResolvesTheAuditListHandler_...` architecture test **started green** (no audit endpoint exists; the handler was registered in 3a). They pin behavior.
  - T056: `AuditCommand` (`audit list`) is registered in `Program.cs`, uses `OwnerId.Default`, and writes nothing. `--limit`, `--from` and `--to` are read as strings and validated, so a bad value is exit 2 with `validation_error`, not a System.CommandLine parse error (exit 1). `--from` and `--to` need `Z` or an offset. Text lines use two spaces between columns, `req=-` when there is no correlation id, `-` for any empty value, and `fields=a,b` with `--fields`. Control characters (and U+2028/U+2029) print as `?` in text; `--json` keeps the stored value and lets JSON escape it. Any unexpected exception becomes exit 1 `internal_error` carrying the exception type name only. Documented in `docs/cli-reference.md`.
  - Two names in T056 and T057 do not match the code: `McpToolSurfaceArchitectureTests` does not exist (the 12-tool count is asserted in `TrackingArchitectureTests` and `ContainerReadinessArchitectureTests`, both green), and `CliServiceWiringArchitectureTests` lists no commands, so I added one resolution test there instead of editing a list.
  - Defect found by the full Integration run and fixed: `HostLogFormatIntegrationTests` and `RedactionIntegrationTests` (US1, committed) use `LaunchedCliRunner` and `McpProcessClient` with `forceContainer: false` but had no `Lane` trait, so `TestLaneCoverageTests.EveryClassUsingTheImageMachinery_...` failed (693 passed, 1 failed). Both now carry `[Trait("Lane", "default")]`, since they run host processes.
  - **US2 gate (T057), run 2026-11-09:**
    - `dotnet build RoleRadar.sln -c Release -v q --nologo -warnaserror`: 0 warnings, 0 errors.
    - `pwsh ./scripts/test-fast.ps1`: Architecture 66/66, Unit 891/891, Contract 265/265.
    - Contract filter (Actor, SignIn, AuditLogReader, RequestCorrelation, IdentityRedaction): 39/39.
    - Integration filter (CliActor, AuditListCli, Redaction, ApiMcpParity): 29/29. **`CliActor` matched zero tests: T045 is not written.**
    - Full Integration lane, `Lane!=images&Lane!=web-container&Lane!=real-stack&Lane!=scan`: 694/694 (2 m 17 s), after the lane fix above.
    - `pwsh ./scripts/test-e2e-real.ps1` (real-stack lane, includes the T051 actor assertions): 1/1.
    - `pwsh ./scripts/test-images.ps1` (`Lane=images|Lane=web-container`): 59/59.
    - Both scripts ran with throwaway `gate-013` image tags (the scripts default to the `:local` and `:test` tags), and those five images were removed afterwards. The dev stack (`roleradar-mongo` on 27017, `deploy-*` on 5001 to 5003) was running throughout and was not touched; no `rrtest` container is left.
    - `pnpm --filter web run typecheck`: exit 0.
    - `pre-commit run --all-files`: every hook passed (the private-directory hook had no files to check).
    - By inspection: MCP tool count is 12 and no MCP, API or `contracts/` file changed in 3b; `git status` shows nothing under `deploy/demo/`; no OpenAPI path contains "audit".
    - Not run: `bash scripts/linux-web-check.sh` (not in the T057 list; US2 changed no web file).

- T045 and the audit-list format characters (2026-11-10), numbers from command output:
  - `CliActorIntegrationTests` (4 tests) **was green on its first run (4 of 4)**, so every CLI path already wrote a valid actor and no source fix was needed. Observed in the test output: `contact add` and `contact redact` 2 entries, all `cli-user`; `import` 2 entries, all `cli-import`; `tracker-import --apply` 6 entries, all `cli-import-tracker` (the dry run wrote 0); demo seed 6 entries, all `demo-seed`.
  - The demo case calls `DemoSeeder` directly in its own container (database named `DemoModeGuard.DemoDatabaseName`), not the `demo seed` command. A first version used the explicit-host guard overload and `DemoArchitectureTests.OnlyTheGuardTestsAndTheIntegrationSeedTest_CallTheExplicitHostOverload` failed on it; the rule was kept and the test changed.
  - `AuditCommand.Printable` now also replaces `UnicodeCategory.Format` (U+202E, U+200B, U+2066 and the rest). `AuditPrintableTests` (23 tests) was red first: 9 failed (8 format characters and the combined case), 14 passed. It is a unit test on the function because an integration test through the redirected Windows console passed before the fix (the console encoding turns an unmappable character into "?" itself), so it could not fail. `Printable` went from private to public for this.
  - After: Integration `CliActor` + `AuditListCli` 27/27; Architecture 66/66; `test-fast.ps1` Architecture 66/66, Unit 914/914, Contract 265/265; build with `-warnaserror` 0 warnings, 0 errors. The full Integration lane and the other gates were not re-run.

- US3 backend (T058 to T061, T063 to T066, 2026-11-10), numbers from command output:
  - **Red first.**
    - T058 contract tests (`ApiHealthContractTests`, `IdentityHealthContractTests`): 11 failed, 2 passed of 13. The 2 that passed are the existing `/health` alias, which started green. Two "bodies carry nothing" tests first passed on empty 404 bodies; they now require a non-empty status body, which is what made them red. The default URL is read by reflection so a missing constant fails one test, not the build.
    - T059, T060 and the `IdentityReadinessCheck` unit tests: red as compile errors (`ApiExceptionHandler` and `IdentityReadinessCheck` did not exist; `DatastoreFailure` and the 503 mapping followed).
    - T061: failed. The read returned 500 after 14.2 s. The write's own command returned `DatastoreOffline` after 3.0 s (mapped to 400 before T064).
  - **Green after.** New Unit tests 25/25, new Contract tests 13/13, T061 1/1. `pwsh ./scripts/test-fast.ps1`: Architecture 66/66, Unit 939/939, Contract 278/278. Build with `-warnaserror`: 0 warnings, 0 errors. Full default Integration lane (`Lane!=images&Lane!=web-container&Lane!=real-stack&Lane!=scan`): 699/699.
  - **Measured by T061 (docker pause of its own Testcontainers replica set, host port assigned by Docker, never a reserved port):** read 503 in 2600 ms, write 503 in 3016 ms (bound 5000 ms); recovery to 200 in 444 ms after unpausing, same API host (bound 30000 ms). An earlier run gave 2895, 3014 and 505 ms.
  - **T066, why the first measurement missed and what changed.** Server selection, connect and readiness are all 3 s (`ReadinessTimeout`), so none of them bounds a read already sent to a server the driver still believes is up. That read waited for the driver's streaming monitor (heartbeat interval 10 s plus connect timeout 3 s) before failing: 14.2 to 14.8 s. Two changes in `ConnectionStringRedactor.CreateClient`, used by every host: the monitor polls (`ServerMonitoringMode.Poll`, 1 s interval, 2 s heartbeat timeout), which brought the read to 5.9 s; and `RetryReads = false`, because the driver's retry selected a server again and waited out a second 3 s server-selection timeout. The CLI's and MCP's reads lose the driver retry too; the web will retry GETs itself (T067). A request deadline through ASP.NET Core request timeouts was tried first and removed: the OData pipeline materialises with a synchronous `ToList()` that takes no token, so a deadline cannot interrupt it.
  - **API-only pieces and one deviation.** `/health` stays a plain `MapGet` in the API (same body) because health-check endpoints are not listed in OpenAPI, so mapping it as a health check removed `/health` from `contracts/openapi/roleradar-api.json` and failed `ApiOpenApiContractSnapshotTests`; the OpenAPI document and `api-types.ts` are unchanged. Identity maps `/health` as the live alias. `SecurityException` from the owner and actor providers still ends as a 500, now with the envelope.
  - Not run in this stop: web tests (T062, T067), the T068 gate, `test-images.ps1`, `test-e2e-real.ps1`, `pre-commit`.
