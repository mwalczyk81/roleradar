# Feature Specification: Operability

**Feature Branch**: `013-operability`

**Created**: 2026-11-09

**Status**: Draft

**Input**: User description: "Operability before announcing the repo. Make RoleRadar's running behavior observable, accountable and honest about its own health, without changing what the app does for the owner. Three user stories in priority order: logs I can read and follow (P1), audit that names the real actor and covers sign-in (P2), health that tells the truth (P3)."

## Why

When something goes wrong today, the owner cannot follow it. The API, identity service and CLI write free-form text, the web app prints ordinary informational lines as errors, and nothing ties a web request to the API and identity lines it caused. Every write made through the API is attributed to the same constant placeholder actor instead of the signed-in owner, and sign-in successes, failures and lockouts leave no structured trace. Both services report "healthy" without checking whether they can reach their data store, so a stopped database looks like a running stack until a request fails. A stranger who reads the repository before it is announced should find a system that can explain itself.

## Scope

In scope: structured, correlated, redacted logs across web, API, identity and CLI, with three local ways to read them (US1); real actor attribution for writes, structured sign-in security events in the logs, and a read-only `audit list` CLI command (US2); separate liveness and readiness, bounded web-to-API timeouts and retries, and a bounded, self-recovering response to a lost database (US3).

Out of scope (each one is a separate decision): distributed tracing, metrics, telemetry collectors or exporters, retry policy libraries, runbooks, hosting, multiple accounts. Also out of scope: any change to what the app does for the owner, to the demo stack's behavior or to its guard, and the choice of logging library or log viewer (decided in the plan, recorded in an ADR).

## Clarifications

### Session 2026-11-09

- Q: Should sign-in security events also be stored in the durable audit store, or only logged? -> A: Logs only. The identity service does not write to the domain database, and no new store is added for these events.
- Q: The owner cannot read the audit log today; should this feature add a read-only way? -> A: Yes. A read-only, owner-scoped CLI command `audit list` with filters and paging, served by an Application query. No MCP tool is added and the 12 MCP tools do not change.
- Q: Is the attempted username logged on a failed sign-in? -> A: No. A failed attempt never logs the submitted username; it carries the account's subject only when the attempt matched an existing account.
- Q: Can container logs grow without limit? -> A: No. The compose files cap each container's log size and file count.

## Current State (verified 2026-11-09)

- API write endpoints attribute every write to the constant actor `api-user`.
- CLI writes already carry named actors (`cli-user`, `cli-import`, `demo-seed`); the MCP host uses `mcp-stdio`.
- There is no way for the owner to read audit entries: no MCP tool, CLI command or API endpoint exposes them.
- Audit entries for posting transitions already carry a field named `correlation_id` that links a posting event to the application event it caused. It is unrelated to request correlation and must keep its meaning.
- The standard API error response is the error envelope (`ok`, `errorCode`, `message`, optional `details`).
- The API and identity service each expose one health endpoint that always answers healthy without checking a data store; compose healthchecks call it.
- The identity service keeps its accounts in its own data store, separate from the domain database.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Logs I can read and follow (Priority: P1)

As the owner running the stack locally, when something goes wrong I open the logs and follow one request across the web app, the API and the identity service by a single correlation id. Every service writes one structured record per line. The id is created or accepted at the web edge, passed on to the API and identity service, present on every log line written for that request, returned to the caller in a response header, and included in the standard error response. I can read logs through `docker compose logs`, in a human-readable form when I run a service outside compose, and through an optional log viewer that I switch on deliberately. No log line ever contains a secret or a note body.

**Why this priority**: Logs are the first thing anyone opens when something breaks, and every other story in this feature (security events, readiness failures, 503s) is only useful if its lines can be found and followed.

**Independent Test**: Start the stack, make a request through the web app that fails in the API, then search all service logs for the id returned in the response header and in the error response; lines from at least two services carry it. Separately, run the redaction test with a secret-shaped value injected into a logged value and see it fail.

**Acceptance Scenarios**:

1. **Given** the stack is running, **When** a request through the web app fails in the API, **Then** log lines from two or more services share one correlation id, and the same id appears in the response header and in the error response body.
2. **Given** a caller sends a well-formed correlation id to the web edge, **When** the request is handled, **Then** that id is used on every log line, passed to downstream services and echoed in the response header.
3. **Given** a caller sends no correlation id or a malformed one, **When** the request is handled, **Then** a new id is generated and used everywhere instead, and the malformed value is never written to a log.
4. **Given** any service is running under compose, **When** the owner runs `docker compose logs`, **Then** every line from the API, identity service and web app is a single structured record with at least time, level, service name, message and (when inside a request) the correlation id.
5. **Given** a service is run outside compose, **When** it logs, **Then** the owner sees a human-readable form of the same records.
6. **Given** the web app logs an informational event, **When** it is written, **Then** it carries an informational level, not an error level.
7. **Given** the owner has not switched the log viewer on, **When** the stack starts, **Then** no log viewer runs; **When** the owner switches it on, **Then** it is reachable only from the local machine; **and** the demo stack never contains it.
8. **Given** the redaction test feeds tokens, passwords, one-time codes, connection-string credentials and note bodies through the logging paths, **When** it inspects the output, **Then** none of those values appears, and the test fails if any one of them does.

---

### User Story 2 - Audit that names the real actor and covers sign-in (Priority: P2)

As the owner, every write is attributed to who actually made it: API writes to the signed-in owner's subject instead of a placeholder, CLI writes to a named CLI actor. Sign-in security events (successful sign-in, failed sign-in, lockout, failed one-time code) are written to the logs as structured events with the correlation id and no secrets. A failed attempt never records the username that was typed. Every write keeps its append-only audit entry and nothing becomes deletable. I can read the audit trail through a read-only CLI command, `audit list`, filtered and paged.

**Why this priority**: An audit trail that names a placeholder cannot answer "who did this", and sign-in is the one place an outsider would probe. It depends on US1 for the correlation id but delivers value without US3.

**Independent Test**: Sign in, make a write through the web app, and read the resulting audit entry with `audit list`: it names the owner's subject. Attempt one failed and one successful sign-in and see exactly one structured security event in the logs for each, with no username on the failed one.

**Acceptance Scenarios**:

1. **Given** the owner is signed in, **When** they make any write through the API, **Then** its audit entry names the owner's subject as the actor and never the placeholder.
2. **Given** a write made through the CLI, **When** its audit entry is written, **Then** the actor is a named CLI actor.
3. **Given** a sign-in attempt with a wrong password, **When** it fails, **Then** exactly one structured "sign-in failed" event is logged with the correlation id; it never contains the password or the submitted username, and it carries the account's subject only when the attempt matched an existing account.
4. **Given** a successful sign-in, **When** it completes, **Then** exactly one structured "sign-in succeeded" event is recorded with the correlation id and the owner's subject.
5. **Given** an account reaches its lockout threshold, **When** it locks, **Then** one "account locked out" event is recorded.
6. **Given** a wrong one-time code is submitted, **When** it is rejected, **Then** one "one-time code failed" event is recorded without the code.
7. **Given** any write, **When** this feature ships, **Then** it still produces its append-only audit entry, and no tool, command or endpoint can delete or modify audit entries.
8. **Given** audit entries exist for the owner, **When** the owner runs `audit list` with filters and a page size, **Then** only that owner's matching entries are shown, newest first, one bounded page at a time, and running the command writes nothing.
9. **Given** an audit entry holds untrusted stored text, **When** `audit list` prints it, **Then** control characters are neutralized and note bodies are never printed.

---

### User Story 3 - Health that tells the truth (Priority: P3)

As the owner, the API and identity service report separately whether they are alive and whether they are ready (able to reach their data store), and compose healthchecks use readiness. Calls from the web app to the API time out, and only safe read requests are retried, a bounded number of times. If the database stops, the API answers with a service-unavailable error in the standard error format within a bounded time, and it recovers by itself when the database returns, without a restart.

**Why this priority**: The stack works today when everything is up. This story matters when something is down, which is rarer than needing to read a log, so it comes last.

**Independent Test**: With the stack running, stop the database, call the API, and see a service-unavailable response in the standard error envelope within the bound; start the database again and see the same call succeed without restarting the API.

**Acceptance Scenarios**:

1. **Given** the API is running and its database is reachable, **When** liveness and readiness are checked, **Then** both report healthy.
2. **Given** the API is running and its database is stopped, **When** they are checked, **Then** liveness reports healthy and readiness reports not ready.
3. **Given** the identity service and its data store, **When** the same checks run, **Then** they behave the same way.
4. **Given** the database is stopped, **When** the owner calls an API endpoint that needs it, **Then** the API answers with a service-unavailable status in the standard error envelope, including the correlation id, within the bound in FR-024.
5. **Given** the database comes back, **When** the same call is repeated, **Then** it succeeds within the recovery bound in FR-025 without restarting the API.
6. **Given** a web-to-API read request times out or meets a transient failure, **When** it is retried, **Then** it is retried at most the bounded number of times, and a write is never retried.
7. **Given** compose starts the stack, **When** it decides a service is healthy, **Then** it uses the readiness check.

---

### Edge Cases

- A caller sends a correlation id that is too long, contains control characters or line breaks, or looks like a log-injection payload: it is rejected and replaced, never logged as sent.
- A caller calls the API or identity service directly, bypassing the web edge: the service generates its own correlation id and behaves the same way.
- A request fails before the correlation id is assigned (for example, malformed HTTP): the failure is still logged as a structured record, without an id.
- An exception message or stack trace contains a connection string or token: it is redacted before it is written.
- A note body or posting text appears in a validation error: the log carries the field name and the issue, never the body.
- Log lines written outside any request (startup, background index creation, CLI runs): structured, with no correlation id or with a run-scoped one.
- The MCP stdio host: its log output stays on stderr and stdout stays reserved for protocol frames.
- Repeated failed sign-ins: one event per attempt, no batching that hides a lockout, and no event carries the attempted password or username.
- A failed sign-in for a username that matches no account: the event carries no subject and no username, so the logs never reveal which usernames were tried.
- `audit list` with a page size outside the allowed range or an invalid filter: it fails with a clear validation message and a non-zero exit code, without querying.
- `audit list` when the database is unreachable: it fails with a clear message and a non-zero exit code, and prints no connection-string credential.
- The database is slow rather than stopped: readiness uses a bounded check, so a hung data store reports not ready instead of hanging the health endpoint.
- The database stops in the middle of a write: the write fails with the service-unavailable error and does not leave a partial write or a missing audit entry (the existing transaction rules hold).
- The web app's retried read succeeds on a later attempt: the owner sees the normal page and the logs show the attempts under one correlation id.
- The log viewer is switched on while the demo stack is running: the demo stack is unaffected and does not gain it.

## Requirements *(mandatory)*

### Functional Requirements

**Structured, correlated logs (US1)**

- **FR-001**: The API, identity service, web app and CLI MUST write logs as one structured record per line, each with at least a timestamp in UTC, a level, the service name and a message.
- **FR-002**: The web app MUST log informational events at informational level and reserve the error level for errors.
- **FR-003**: The web edge MUST accept a well-formed inbound correlation id or generate a new one when none, or a malformed one, is supplied. Well-formed means a bounded length and a restricted character set defined in the plan.
- **FR-004**: The web app MUST pass the correlation id on every call it makes to the API and identity service; the API and identity service MUST accept a well-formed inbound id or generate one when called directly.
- **FR-005**: Every log record written while handling a request MUST carry that request's correlation id.
- **FR-006**: Every response from the web app, API and identity service MUST return the correlation id in a response header.
- **FR-007**: The standard error envelope MUST include the correlation id. The field is additive, the generated contract MUST be regenerated, and existing consumers MUST keep working.
- **FR-008**: The new request correlation id MUST be named and stored distinctly from the existing audit `correlation_id` field, which keeps its current meaning.
- **FR-009**: Logs MUST be readable through `docker compose logs` with no extra setup.
- **FR-010**: When a service runs outside compose, its logs MUST be readable in a human-readable form without a separate tool.
- **FR-011**: An optional log viewer MUST be off by default, switched on only by a deliberate owner action, reachable only from the local machine, and absent from the demo stack.
- **FR-012**: Log records MUST NOT contain an access token, refresh token, identity token, authorization header, cookie value, password, one-time code or TOTP secret, connection-string credential, or note body.
- **FR-013**: An automated test MUST push secret-shaped values of each kind in FR-012 through the logging paths of each service and fail if any appears in the output.
- **FR-014**: The MCP stdio host MUST keep stdout free of log output.
- **FR-032**: The compose files MUST cap every container's log by maximum size and maximum file count, so local logs cannot grow without limit.

**Real actors and sign-in events (US2)**

- **FR-015**: Every write made through the API MUST record the signed-in owner's subject as the audit actor; the constant placeholder MUST no longer be used.
- **FR-016**: Every write made through the CLI MUST record a named CLI actor; a test MUST fail if any CLI write path records an empty or placeholder actor.
- **FR-017**: Audit entries written while handling an API request MUST record the request's correlation id.
- **FR-018**: The identity service MUST record exactly one structured security event for each successful sign-in, failed sign-in, lockout and failed one-time code, each carrying the event type, time and correlation id. An event MUST carry the account's subject only when the attempt matched an existing account. Events MUST NOT carry a password, one-time code, secret or the submitted username.
- **FR-019**: Sign-in security events MUST be written to the logs only. They are not stored in the audit store or any other durable store, and the identity service gains no dependency on the domain database.
- **FR-020**: The CLI MUST provide a read-only command, `audit list`, served by an Application query and scoped to the owner, that lists audit entries newest first with filters (at least entity type, entity id, actor and a UTC time range) and bounded paging (page size clamped to a range defined in the plan, offset or cursor at or above zero). It MUST write nothing, MUST neutralize control characters in stored text when printing, and MUST NOT print note bodies. No MCP tool is added, the 12 MCP tools MUST NOT change, and no API endpoint is added for audit.
- **FR-021**: Every write MUST keep its append-only audit entry; no tool, command or endpoint added or changed by this feature may delete or modify an audit entry.

**Truthful health (US3)**

- **FR-022**: The API and identity service MUST each expose a liveness check (the process is running and can serve) and a readiness check (it can reach its data store within a bounded time), reported separately.
- **FR-023**: Compose healthchecks for the API and identity service MUST use the readiness check.
- **FR-024**: When the database is unreachable, any API request that needs it MUST receive a service-unavailable status in the standard error envelope, with the correlation id, within 5 seconds of the request arriving.
- **FR-025**: When the database becomes reachable again, the API MUST serve requests successfully within 30 seconds without a restart.
- **FR-026**: Web-to-API calls MUST have a per-attempt timeout, and only safe read requests MAY be retried, at most 2 times after the first attempt; write requests MUST never be retried automatically.
- **FR-027**: Retries and timeouts MUST be implemented without a retry policy library. *(Superseded by feature 014, FR-044: a maintained retry library is now allowed. See `specs/014-standards-first/spec.md`.)*

**Constraints (all stories)**

- **FR-028**: No log, event or health data may leave the local machine by default: no exporter, collector or remote sink is enabled.
- **FR-029**: The demo stack's behavior and its guard MUST NOT change, and the demo stack MUST NOT include the log viewer.
- **FR-030**: The feature MUST NOT add a new custom script; existing tools are preferred.
- **FR-031**: Every requirement MUST be covered by a test written and shown failing before its implementation; all test data MUST be fictional.

### Key Entities

- **Correlation id**: An opaque identifier for one request as it crosses the web app, API and identity service. Created or accepted at the edge, carried on every log record, response header and error envelope for that request. Distinct from the existing audit link between a posting event and its application event.
- **Log record**: One structured line with timestamp, level, service, message, optional correlation id and optional structured properties. Never contains the values listed in FR-012.
- **Audit entry**: The existing append-only record of a write. Gains the real actor (owner subject or named CLI actor) and the request correlation id; never deletable.
- **Security event**: A structured record of a sign-in outcome (succeeded, failed, locked out, one-time code failed) with time, correlation id and, only when an existing account was matched, the account's subject. Never the submitted username. Written to the logs only (FR-019).
- **Audit listing**: An owner-scoped, read-only, paged view of audit entries returned by `audit list`, with the filters in FR-020.
- **Health report**: Two separate answers per service: alive, and ready (data store reachable within a bound).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Given only the id from a failed request's response, the owner finds every related log line across all services with one search, in under one minute.
- **SC-002**: 100% of log lines from the API, identity service, web app and CLI parse as structured records under compose.
- **SC-003**: Zero secret-shaped values from the redaction test's inputs appear in any log output, and removing any one redaction makes the test fail.
- **SC-004**: 100% of audit entries written through the API in the test suite name the owner's subject; zero name the placeholder.
- **SC-005**: Each of the four sign-in outcomes produces exactly one security event in the tests.
- **SC-006**: With the database stopped, 100% of API calls that need it return service-unavailable in the standard envelope within 5 seconds; after the database returns, the next call succeeds within 30 seconds with no restart.
- **SC-007**: In the tests, write requests are attempted exactly once, and read requests are attempted at most 3 times.
- **SC-008**: The owner-facing behavior of every existing page, tool, command and endpoint is unchanged, shown by the existing test suites passing without changes to their expectations, except for these intended changes:
  - the additive `correlationId` envelope field and the `X-Correlation-ID` response header;
  - a data store outage returns 503 `service_unavailable` (previously 400 `validation_error` for writes, and a 500 without an envelope for reads);
  - any other unhandled exception returns a 500 `internal_error` envelope;
  - audit actors name the real actor (the signed-in subject instead of `api-user`);
  - the images' built-in healthcheck checks readiness by default.

  Only tests that assert one of these may change their expectations. Mechanical edits that keep an expectation, such as naming an actor explicitly or capturing logger output instead of `console`, are allowed.
- **SC-009**: Using `audit list` alone, the owner finds who made a given write and when in under one minute; the command returns only the owner's entries and leaves the audit store unchanged.
- **SC-010**: Every service in the compose files has a log size and file-count cap; a test fails if any service lacks one.
- **SC-011**: Zero failed sign-in events in the tests contain the submitted username.

## Assumptions

- The owner is the only account (single-owner, local-first); "the signed-in owner's subject" is the subject claim already used for owner checks.
- CLI writes keep their existing named actors (`cli-user`, `cli-import`, `demo-seed`); this feature makes the rule enforced by a test rather than renaming them. The MCP host's `mcp-stdio` actor is unchanged.
- The demo stack runs the same images, so it gains structured logs, the correlation header and truthful health as a side effect; its seed, guard, account handling and compose layout do not change.
- The bounds in FR-024, FR-025 and FR-026 (5 seconds, 30 seconds, 2 retries) are defaults chosen for a local stack; the plan may tighten them but not loosen them without updating this spec.
- Exact header and field names for the correlation id, the logging library, the log viewer and how it is switched on are decided in the plan and recorded in an ADR, with every package and image tag pinned.
- The CLI has no inbound request, so its log records carry a run-scoped id rather than a request correlation id.
- Identity's own data store is the dependency for its readiness check; the API's is the domain database.
- The log caps in FR-032 apply to the real compose files (`deploy/docker-compose.yml`, `deploy/docker-compose.apps.yml`). The demo compose file is left unchanged to honor FR-029; it is a throwaway stack.
- `audit list` follows the existing CLI patterns (owner from `OwnerId`, Result-based errors, non-zero exit on failure). Its exact output format and page-size range are decided in the plan.
- Because sign-in events are logs only, they last only as long as the capped container logs; that is accepted for a single-owner local stack.
