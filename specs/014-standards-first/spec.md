# Feature Specification: Standards-first cleanup

**Feature Branch**: `014-standards-first`

**Created**: 2026-11-10

**Status**: Draft

**Input**: User description: "Standards-first cleanup (Jira epic RR-75). Replace hand-rolled mechanisms with the industry standard where the standard is a clear fit, and justify the ones that stay. Four stories in priority order: the second-factor code check (P1), the API error body and correlation id as one migration (P2), the web retry and concurrency limiter (P3), and four evaluate-or-justify items (P4)."

## Why

RoleRadar is a public showpiece. A reader who opens the repository should see platform features and maintained libraries used where they exist, and a recorded reason wherever the code is hand-written. Today several reusable mechanisms were built by hand: the second-factor code check implements RFC 6238 and Base32 itself, the API error body is a private envelope, the correlation id is a private header with its own middleware, and the web app carries its own retry and concurrency limiter. Each one is code the owner must maintain, review for security and explain, and each one is a miss against the project's own rule (constitution v1.2.0, Principle X: check for a standard first, record the outcome, and justify anything hand-rolled in an ADR).

The cost of changing is lowest now. Nothing is tagged, and every consumer of the API, the CLI and the MCP server is inside this repository.

## Scope

In scope: the four stories below. Each story ends in one of two states for every mechanism it touches: the mechanism now uses the standard, or an ADR names the standard it was compared against and why it stays.

Out of scope (each one is a separate decision): packaging the standards check as a Spec Kit extension (RR-84), tracing, metrics and exporters (RR-66, feature 015), the v0.1.0 release (RR-62), and any change to what the app does for the owner beyond what a story states.

## Clarifications

### Session 2026-11-10

- Q: Which window of one-time codes should sign-in accept, today's one step either side (3 codes) or the platform provider's two steps either side (5 codes, about 150 s)? -> A: The provider's window, together with the replay protection in FR-015. The ADR records the deviation from the RFC 6238 recommendation of at most one step of drift, and the mitigations (replay protection, lockout at 5 attempts and 15 minutes).
- Q: Does an inbound `X-Correlation-ID` header still count as a correlation id? -> A: No. It is ignored unless an ADR names a client that needs it.
- Q: Are audit entries written before this feature rewritten to the new trace id? -> A: No. The audit log is append-only, so old entries keep their stored values.
- Q: Where is the 013 amendment (FR-027) recorded? -> A: In this spec (FR-044), and a one-line pointer is added to the 013 spec at plan time.
- Q: Which API routes get RFC 9457 problem documents? -> A: `/api/v1`, the OData read routes and the 401 and 403 failures raised by authentication and authorization.
- Q: For OData routes, RFC 9457 or OData's native error JSON? -> A: RFC 9457. The only consumer is the typed web client, and one error format gives one parsing path. The plan's "Standards considered" row and the P2 ADR record the OData error format as the alternative compared (FR-020, FR-030).
- Defaults stated without a question (see Assumptions): the used-code record lives in the identity service's own store, the problem `type` is a documentation URL in the repository per error code (see the plan), and no response header returns the trace id.

## Current State (verified 2026-11-10)

**Second-factor code check**

- The identity service's sign-in page verifies the one-time code with a hand-written RFC 6238 check (`OwnerProvisioningService`) whenever the account can be resolved from the pending sign-in. The platform's authenticator provider (through the sign-in manager) is called only in a fallback branch where the account cannot be resolved. Enrolment activation (`create-owner`) and the demo `totp` helper also use the hand-written code.
- Secrets are already stored in the platform's own authenticator-key slot, in Base32. A throwaway probe (Identity 10.0.14, a scratch project outside the repository, not committed) enrolled a key in today's format and read it back through the platform's `GetAuthenticatorKeyAsync` unchanged. No re-enrolment is needed to read existing secrets.
- Window: today's check accepts the code for the current 30-second step and one step either side (three consecutive codes). The platform provider accepts two steps either side (five consecutive codes, up to about 150 seconds). The provider's window is not configurable.
- Replay: today's check keeps no record of a used code, so the same code is accepted again inside its window. The provider has the same gap: the probe accepted one code three times in a row. Today the gap exists and the provider does not close it.
- The provider verifies codes but does not generate them: asked for a token, it returned an empty string. The demo `totp` command and the end-to-end tests need a code source of their own.
- Lockout is configured at 5 failed attempts and 15 minutes. The sign-in events 5001 to 5004 are raised from the sign-in page (feature 013).

**API error body and correlation id**

- The API's error body is the private envelope `{ ok, errorCode, message, details[{ field, issue }], correlationId }` (ADR 0018 section 2, extended in 013).
- The correlation id is a private `X-Correlation-ID` header handled by separate middleware in the API and in the identity service, a log scope key, the `request_correlation_id` on audit entries, a run id in the CLI and a per-call id in the MCP host (ADR 0041).
- 503 responses carry fixed text and `Retry-After: 5`. The web shows 400 field errors inline and 409 as a Toast with Reload.
- Audit entries for posting transitions also carry a field named `correlation_id`. It links a posting event to the application event it caused, is unrelated to request correlation, and keeps its meaning.

**Web retry and concurrency**

- The web app hand-writes its retry and backoff (`fetchWithPolicy`) and a concurrency limiter (`request-limiter.server.ts`). Feature 013 requirement FR-027 forbade a retry policy library; FR-026 (timeouts, reads retried at most twice, writes never retried) is the behavior to keep.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The owner signs in with the platform's authenticator check, and a used code cannot be used twice (Priority: P1)

The owner enters a password and then the six-digit code from their authenticator app, exactly as today. Behind the page, the code is checked by the platform's maintained authenticator provider instead of hand-written RFC 6238 and Base32 code, and a code that has already been accepted is rejected if it is submitted again.

**Why this priority**: It is security-relevant code that guards the only account, it is the largest hand-written security mechanism, and the replay gap is a real weakness that exists today and would survive a plain swap.

**Independent Test**: Provision a fictional owner, sign in with a valid code (succeeds), submit the same code again in a new sign-in inside its window (rejected), then sign in with a fresh code (succeeds). Repeat for enrolment activation. Run the existing sign-in contract tests and the 013 sign-in event tests unchanged.

**Acceptance Scenarios**:

1. **Given** an owner enrolled before this feature with an existing authenticator app, **When** they sign in with a valid code, **Then** they are signed in with no re-enrolment and no change to their stored secret.
2. **Given** a code was accepted once, **When** the same code is submitted again within its validity window, **Then** it is rejected, counts as a failed attempt toward lockout, and raises the one-time-code-failed event (5004).
3. **Given** a code was accepted once, **When** two sign-ins submit it at nearly the same moment, **Then** at most one succeeds.
4. **Given** five consecutive failed attempts, **When** a sixth is made, **Then** the account is locked for 15 minutes, and the lockout event (5003) is raised once as today.
5. **Given** the sign-in outcomes covered by feature 013, **When** each occurs, **Then** exactly one of events 5001 to 5004 is raised with the same fields as today, and no secret, code or username appears in any log line.
6. **Given** a new owner is being enrolled, **When** the activation code is entered, **Then** it is checked by the same provider, and a wrong or already-used code leaves the account inactive.
7. **Given** the demo stack, **When** the owner runs the demo code command, **Then** it prints the current valid code and its remaining seconds as today.
8. **Given** an authenticator app whose clock is off, **When** the owner submits the code for the current step or for two steps either side, **Then** it is accepted once; **When** the code is three steps away, **Then** it is rejected.

---

### User Story 2 - Every API error is a standard problem document carrying one trace id that follows the request everywhere (Priority: P2)

When an API call fails, the body is an RFC 9457 problem document, and its trace id is the same W3C Trace Context id that appears in the logs of the web, API and identity service, on the audit entries the request wrote, and as the run id or per-call id in the CLI and MCP host. The owner or a reader of the repository can take one id from an error and find everything the request did. This is one migration, not two: the error body and the correlation id change together.

**Why this priority**: It is the contract-level change, it is breaking, and it is cheapest before anything is tagged. The error body and the correlation id share the same field, so splitting them would migrate the same id twice.

**Independent Test**: For each of 400, 401, 403, 404, 409, 500 and 503, call the API and check the media type, the members and the headers. Send a request with a trace context from the web and confirm the same trace id in the web, API and identity logs, in the error body, and in the audit entry of a write. Run the web end-to-end tests for inline 400 errors and the 409 Toast.

**Acceptance Scenarios**:

1. **Given** a request that fails validation, **When** the API answers, **Then** the status is 400, the body is a problem document with a field-error list naming each field and issue, and the machine-readable error code the web switches on is still present.
2. **Given** a missing resource or a stale version token, **When** the API answers, **Then** the status is 404 or 409 with a problem document and the same code and message meaning as today.
3. **Given** the database is stopped, **When** the API answers, **Then** the status is 503, the detail text is fixed and free of exception text, endpoint names and remediation commands, and `Retry-After` is present.
4. **Given** an unhandled failure or an authentication or authorization failure, **When** the API answers, **Then** the status is 500, 401 or 403 with a problem document, and a 500 body never contains exception text or a stack trace.
5. **Given** a request that arrives with a W3C trace context, **When** it is handled, **Then** its trace id is the correlation id in the logs, the error body, the audit entry's request correlation id, and downstream calls. A request without one gets a generated trace id used the same way.
6. **Given** a web request that calls the API or the identity service, **When** it runs, **Then** the web forwards the trace context, so each service the request calls shares its trace id: the web and API logs and the audit entry of a write for a request that fails in the API, the web and identity logs for the sign-in callback, and all three only for a request that triggers a token refresh.
7. **Given** a CLI run or an MCP call, **When** it writes audit entries or logs, **Then** its run id or per-call id is a trace id in the same form, and its `{ ok, data }` result shape is unchanged.
8. **Given** a form submission that fails with 400 or 409, **When** the web receives the new body, **Then** it shows inline field errors for 400 and the Toast with Reload for 409, as today.
9. **Given** audit entries written before this feature, **When** the owner lists them, **Then** they are still readable and their old correlation values are shown unchanged.

---

### User Story 3 - The web retries and limits requests through maintained libraries (Priority: P3)

The web app's retry, backoff and concurrency limiting are provided by maintained libraries instead of hand-written helpers, with the same observable behavior and with the server's `Retry-After` honored.

**Why this priority**: It removes hand-written reliability code on the web side, but the current behavior is correct and bounded, so it is less urgent than security and contract changes. It needs the explicit amendment of an earlier requirement.

**Independent Test**: Run the 013 web policy tests and the Insights tests against the library-backed implementation, with fake timers. Add tests for `Retry-After` handling. Check the attempt counts per method and status.

**Acceptance Scenarios**:

1. **Given** a read that fails with a retryable error, **When** it is retried, **Then** it is attempted at most 3 times in total (the first call and at most 2 retries) with bounded backoff.
2. **Given** a write that fails, **When** it fails, **Then** it was sent once and is never retried automatically.
3. **Given** a response of 401, 403 or 404, **When** it arrives, **Then** it is never retried, for any method.
4. **Given** a 503 with a `Retry-After` within the bound, **When** a read is retried, **Then** the wait honors the header.
5. **Given** a 503 with a `Retry-After` longer than the bound, **When** a read gets it, **Then** the read is not retried and fails with the 503 at once.
6. **Given** many concurrent page loads, **When** they call the API, **Then** concurrency stays under the existing cap and no request waits unboundedly.

---

### User Story 4 - Four remaining hand-rolled pieces each end in a swap or a written justification (Priority: P4)

Four candidates are evaluated against the standard. Each ends either in a swap with the existing tests unchanged, or in an ADR that names the standard it was compared against and why it stays. Each item ships on its own.

**Why this priority**: These are smaller or riskier than the first three, and for some the likely right answer is "keep it, and say why". The value is the recorded decision.

**Independent Test**: For each item, the existing suites for that area pass unchanged after a swap, or an ADR exists naming the alternative. A reviewer can find the outcome for all four in one place.

**Acceptance Scenarios**:

1. **Given** the log scrubber (RR-80), **When** it is evaluated against the platform's compliance redaction with data classification, **Then** the outcome is recorded: classify the service's own log parameters at the source and keep a smaller backstop for third-party text, or keep the scrubber with a reason. The unit, contract, integration and web redaction suites pass unchanged.
2. **Given** the custom console formatter and the ten hand-written validators (RR-81), **When** each is evaluated against the built-in console formatters and a maintained validation library, **Then** each half is swapped with unchanged test results or kept with an ADR. The text and JSON log toggle and the scrubber hook keep working, and validation results keep their codes and messages.
3. **Given** the custom backup archive format (RR-82), **When** it is evaluated against `age` and restic, **Then** an evaluation and ADR record the decision, including "keep as is". No backup is put at risk, archives made by the current format stay restorable, backup and restore behavior is unchanged, and the evaluation uses fictional data only. The owner, not an agent, runs anything against the live database.
4. **Given** the licence-audit and action-pin scripts (RR-83), **When** an existing tool already covers each policy, **Then** the script is replaced by that tool configured to the same policy, or kept with a one-line justification. Before any script is deleted, the replacement is shown to fail on a deliberately violating change and to enforce the same allowlist.

---

### Edge Cases

- A code is accepted at the very end of its window, and the owner submits it again after the window rolls over (a different step). The replay rule covers the code and the step it was valid for, not only the current step.
- Two browsers submit the same valid code at the same moment: at most one sign-in succeeds.
- An owner enrolled before this feature has a secret the platform provider has never generated. It must verify without any migration step.
- The owner's authenticator app is a few seconds ahead or behind. The code for the current step and for two steps either side (about 60 seconds of drift each way) is accepted; a code three steps away is rejected (FR-016).
- The owner enrols with a code and signs in a few seconds later with the same code. The second use is rejected as a replay and the owner waits for the next code (at most 30 seconds). This is expected, not a defect.
- The identity service restarts inside the window of an accepted code. The same code is still rejected afterwards (FR-015).
- A request to the API arrives with a malformed or oversized trace context header. It is discarded without being logged and replaced by a generated trace id.
- A request arrives from an old client that still sends `X-Correlation-ID`. Per the assumption below, the header is ignored.
- The web retries a read and the server answers 503 with a `Retry-After` longer than the existing overall bound. The bound wins and the read fails with the 503.
- Audit entries written before the migration carry correlation ids in the old format. They remain listable and are not rewritten.
- A validation error names a field that exists in a nested request object. The problem document still names the field and the issue.
- A swap in P4 changes the text of a log line or a validation message that a test asserts. That is a behavior change and is not allowed unless a story says so.

## Requirements *(mandatory)*

### Functional Requirements

**Whole feature**

- **FR-001**: Every mechanism touched by this feature MUST end with a recorded outcome: it uses the named standard or library, or an ADR names the standard it was compared against and why it lost. Supply-chain minimalism, a smaller dependency count or preference alone MUST NOT be accepted as the reason (Principle X).
- **FR-002**: Owner-visible behavior MUST NOT change except where a story in this spec says so. The owner's sign-in steps, the web pages, the CLI commands and the 12 MCP tools behave as before.
- **FR-003**: Each requirement MUST be covered by a test written and shown failing before the implementation, except a characterization test that pins existing or platform behavior, which MAY start green when the task notes record it as such. The existing contract and integration tests are the parity proof, and an expectation in them MAY change only where a story or FR names the change.
- **FR-004**: All test data MUST be fictional, and no log, report, screenshot or ADR MAY carry real data or a secret.
- **FR-005**: The feature MUST NOT edit `.github/dependabot.yml`, add a test project, or open any upstream issue, pull request or discussion without the owner's explicit approval.
- **FR-006**: Every package or image adopted MUST be pinned, MUST carry a licence compatible with this repository's MIT licence, and MUST NOT send data off the local machine by default.

**P1: second-factor code check**

- **FR-010**: Second-factor code verification, at sign-in and at enrolment activation, MUST use the platform's authenticator provider. No hand-written RFC 6238 or Base32 code MAY remain in the shipped services, CLI or hosts.
- **FR-011**: An owner enrolled before this feature MUST sign in with their existing authenticator app and secret, with no re-enrolment and no change to the stored secret. The enrolment URI format shown for a new enrolment MUST stay compatible with authenticator apps.
- **FR-012**: Lockout MUST stay at 5 failed attempts and 15 minutes, and a rejected code MUST count as a failed attempt exactly once.
- **FR-013**: The sign-in security events 5001 to 5004 MUST fire on the same outcomes, with the same fields, exactly once per outcome, and with the same rules about the subject and the submitted username as in feature 013.
- **FR-014**: No one-time code, secret or submitted username MUST appear in any log record, and the 013 redaction tests for the sign-in path MUST stay green.
- **FR-015**: A code that was accepted once MUST NOT be accepted again, at sign-in or enrolment activation, for as long as it would otherwise be valid, including when two attempts arrive at the same moment and after a restart of the identity service. Because the platform provider does not provide this, it MUST be a thin in-repo extension. The ADR for it MUST mark it temporary, link the upstream issue or state that the issue awaits the owner's approval, and name the library it extends (Principle X).
- **FR-016**: The window of accepted codes MUST be the platform provider's: the code for the current 30-second step and two steps either side (five codes, up to about 150 seconds). This is wider than today's one step either side and wider than the at-most-one-step of drift that RFC 6238 section 5.2 recommends. The ADR for FR-015 MUST record that deviation and its mitigations: replay protection (FR-015), and lockout at 5 failed attempts and 15 minutes (FR-012). No in-repo code MAY narrow the provider's window, because that would need a hand-written code check again.
- **FR-017**: The provider cannot generate codes, so the demo `totp` command and the end-to-end test helpers need a code source. Production code MUST NOT contain a hand-written generator for it. The plan MUST take the code source from a maintained library, or record an ADR for a demo-and-test-only helper that names the library it was compared against. The RFC 6238 test vectors MUST stay as the parity proof for whichever source is used.
- **FR-018**: The verified findings in "Current State" (window, replay, key compatibility, no code generation) MUST be re-run as automated tests, so a later platform upgrade that changes them fails a test instead of passing silently.

**P2: error body and correlation id**

- **FR-020**: Every error response from the API, for `/api/v1`, OData reads and failures raised by authentication and authorization, MUST be an RFC 9457 problem document with media type `application/problem+json`, carrying `status`, `type`, `title`, `detail` and the trace id. OData routes use RFC 9457 as well, not OData's native error JSON, because the only consumer is the typed web client and one error format gives it one parsing path; OData's own error format is the alternative the plan and the ADR compare against.
- **FR-021**: A validation failure MUST be 400 with the field errors listed by field name and issue. The machine-readable error code that the web switches on MUST remain available as a named extension member, so the web maps errors mechanically.
- **FR-022**: 404 and 409 MUST keep their meaning and codes. 503 MUST keep its fixed text and `Retry-After`. A 500 or 503 body MUST NOT contain exception text, stack traces, connection details or remediation commands.
- **FR-023**: The request's W3C Trace Context trace id MUST be the correlation id everywhere: log records, the error body, the audit entry's `request_correlation_id`, the CLI run id and the MCP per-call id. A request without a valid trace context MUST get a generated one, and an invalid inbound value MUST be discarded and not logged.
- **FR-024**: The web MUST forward the trace context to the API and to the identity service, so each service a web request calls shares that request's trace id in its log lines. A request that calls only the API shares it across the web and the API; the sign-in callback, across the web and the identity service; a request that triggers a token refresh, across all three.
- **FR-025**: The custom `X-Correlation-ID` header, its middleware in the API and the identity service, and its scope key MUST be removed. An inbound `X-Correlation-ID` MUST be accepted only if an ADR names a client that needs it.
- **FR-026**: The audit field `correlation_id` on posting transitions MUST keep its meaning. Audit entries written before this feature MUST stay readable through `audit list` with their stored values unchanged.
- **FR-027**: The CLI and MCP result shape `{ ok, data }` MUST stay unchanged, unless the plan decides otherwise and records it in an ADR.
- **FR-028**: The web MUST keep showing inline field errors for 400 and the Toast with Reload for 409, and MUST keep the 401 (one re-login), 403 and 404 handling, mapped from the new body.
- **FR-029**: Breaking changes to the API contract are allowed. The OpenAPI document and the generated client types MUST be regenerated, and contract tests MUST cover the media type, members and headers for 400, 401, 403, 404, 409, 500 and 503.
- **FR-030**: One ADR MUST supersede ADR 0018 section 2 and the correlation section of ADR 0041, and MUST state the mapping from each old field to its new member. It MUST also record OData's native error format as the alternative compared for the OData routes, and why RFC 9457 was preferred.

**P3: web retry and limiter**

- **FR-040**: The web app's retry and backoff and its concurrency limiting MUST be provided by maintained libraries, with no hand-written equivalent left. One comparison of the candidates MUST be recorded in an ADR.
- **FR-041**: Reads MUST be retried at most 2 times after the first attempt, with bounded backoff. Writes MUST be sent once. A response of 401, 403 or 404 MUST NOT be retried.
- **FR-042**: When a read gets a 503 carrying `Retry-After`, the retry delay MUST honor it if it is within the bound. If it asks for longer than the bound, the read MUST NOT be retried and MUST return the 503 at once. The plan names the bound.
- **FR-043**: The per-attempt timeout and the concurrency cap from feature 013 MUST be kept, and the 013 web policy tests and Insights tests MUST pass with the libraries underneath.
- **FR-044**: This feature amends FR-027 of the 013 specification, which said retries and timeouts MUST be implemented without a retry policy library. FR-027 no longer applies once this story ships. FR-026 and FR-028 of 013 (retry bounds, and no data leaves the machine by default) still apply.
- **FR-045**: The added libraries MUST pass the repository's lockfile and licence checks and MUST NOT add telemetry.

**P4: evaluate or justify**

- **FR-050**: Each of the four items MUST end in a swap with the existing tests for that area unchanged, or an ADR naming the standard it lost to. The four outcomes MUST be findable in one place: a "P4 outcomes" table in `docs/adr/README.md` that links the four ADRs and the plan's "Standards considered" table.
- **FR-051**: Redaction (RR-80): the evaluation MUST address both the service's own log parameters and text produced by third parties, which classification at the source cannot cover. The 013 redaction suites MUST pass unchanged.
- **FR-052**: Console formatter and validators (RR-81): each half MUST be decided separately. A swap MUST NOT change a validation code, message or result, the text and JSON log toggle, or the scrubber hook.
- **FR-053**: Backup format (RR-82): the evaluation MUST compare format stability, audit history, streaming of large archives, passphrase handling, restore verification, Windows and container support, and migration. It MUST NOT change the backup format or any script's behavior without the owner's approval, MUST keep archives made by the current format restorable, and MUST NOT run anything against the owner's database.
- **FR-054**: Scripts (RR-83): before any script is removed, its replacement MUST be shown to enforce the same policy and to fail on a deliberate violation. A kept script MUST carry a one-line justification.

### Key Entities *(include if feature involves data)*

- **Second-factor secret**: The owner's stored authenticator key. Its value and format do not change, and it is never logged.
- **Used-code record**: The memory that a code was accepted, needed so the same code is rejected a second time. It holds no secret and no code in readable form.
- **Sign-in security event**: The existing structured log record for the outcomes 5001 to 5004. Unchanged in shape.
- **Problem document**: The standard error body of the API, with a status, type, title, detail, field errors, the machine-readable code and the trace id.
- **Trace id**: The W3C Trace Context id of a request. It replaces the private correlation id in logs, error bodies, audit entries, CLI run ids and MCP per-call ids.
- **Audit entry**: The existing append-only record. Its request correlation value becomes a trace id for new entries, and old entries keep their stored values.
- **Decision record (ADR)**: The written outcome for every mechanism: the standard adopted, or the standard it lost to.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An owner enrolled before this feature signs in with their existing authenticator app on the first attempt, with zero re-enrolments and no change to the stored secret.
- **SC-002**: In tests, a code that was accepted once is rejected on every later attempt within its validity window (100%), including two attempts at the same moment and after a service restart, and each rejection is one failed attempt and one event 5004. A code two steps from the current one is accepted once, and a code three steps away is rejected.
- **SC-003**: The shipped services, CLI and hosts contain zero hand-written RFC 6238 or Base32 implementations, and a search for them returns nothing outside tests and the documented demo-and-test helper, if one survives.
- **SC-004**: Lockout still happens on exactly the fifth consecutive failure and ends after 15 minutes, and each sign-in outcome writes exactly one of events 5001 to 5004, with zero secrets, codes or submitted usernames in any log line.
- **SC-005**: For every API error status in the contract tests (400, 401, 403, 404, 409, 500, 503), the response is `application/problem+json`, and zero responses use the old envelope.
- **SC-006**: For a web request that fails in the API, searching the web and API logs for the error body's trace id returns the lines of both, and for a write it also returns the audit entry. For the sign-in callback, the same search returns the web and identity service lines. For a request that triggers a token refresh, it returns the lines of all three. This holds in 100% of the tested cases, and a case that cannot be forced end to end is reported as the two linked halves.
- **SC-007**: Zero code for the `X-Correlation-ID` header, its middleware or its scope key remains, unless an ADR names a client that needs it.
- **SC-008**: The web end-to-end tests for inline 400 errors and the 409 Toast with Reload pass with no change to what they assert.
- **SC-009**: The web app has zero hand-written retry or concurrency helpers. In the tests, a read makes at most 3 attempts in total, a write makes 1, and a 401, 403 or 404 makes 1.
- **SC-010**: All four P4 items have a recorded outcome, and for every swap the diff of test expectations for that area is empty.
- **SC-011**: The full review gate passes, `.github/dependabot.yml` is unchanged, no test project was added, and no real data appears anywhere in the diff.
- **SC-012**: The plan has a "Standards considered" row for every mechanism in this feature, and every mechanism that stays hand-written links its ADR.

## Assumptions

- The owner is the only account (single-owner, local-first). The existing owner's secret is in the platform's authenticator-key slot in Base32, as verified in "Current State".
- The audit field `correlation_id` for posting transitions is a different concept and is not part of the migration in P2.
- Old audit entries are not rewritten, because the audit log is append-only. A request correlation id of the old format and a new trace id can sit side by side in the audit store.
- An inbound `X-Correlation-ID` is ignored unless the plan's ADR names a client that needs it. All current clients are inside this repository and move to the trace context.
- The used-code record (FR-015) lives in the identity service's own data store. The identity service has no dependency on the domain database (013 FR-019), and a store that survives a restart is what the restart edge case needs. The record is written atomically with the accept decision, so two simultaneous attempts cannot both succeed. The plan decides the exact shape and how long entries are kept, which only needs to cover the window.
- The problem document `type` is a stable URI per error code, and it is a documentation URL in the public repository: `https://github.com/mwalczyk81/roleradar/blob/main/docs/api/problems.md#<errorCode>`, one anchor per code in `docs/api/problems.md` (owner decision, 2026-11-10). RFC 9457 prefers a `type` that resolves to human-readable documentation, and this one does. The base URL is one constant, so it can move if the API is hosted (hosting is epic RR-2); after the first tag the value is a contract. The web switches on the stable `errorCode` member, never on `type`. The alternative compared, an RFC 4151 tag URI on the authority `mattwalczyk.com`, is recorded in the plan and the ADR. Tests assert the type per code.
- No response header returns the trace id. The trace id appears in the problem document of every error, which is where the owner meets it, and successful responses carry no id. Feature 013's rule that every response carries `X-Correlation-ID` ends with that header (FR-025), and its header tests are removed or rewritten for the trace context. Feature 015 (observability) may revisit this.
- How the web produces its trace context is a plan decision under Principle X: a maintained tracing library, or a justified hand-written W3C `traceparent` value with an ADR. Adopting OpenTelemetry as a whole belongs to RR-66 and feature 015.
- The exact names of the extension members of the problem document are decided in the plan and recorded in the ADR in FR-030.
- Nothing is tagged and every consumer is internal, so breaking the API contract is acceptable, provided the OpenAPI document, the generated client and the contract tests are regenerated and updated together.
- Delivery follows the epic's staged order with one review stop per story: P1, then P2, then P3, then the P4 items. Each story is independently testable and shippable.
- P3 amends the 013 specification. The 013 spec text keeps its history, and a one-line pointer to this amendment is added at plan time rather than rewriting the earlier requirement.
- P4 (RR-82) is an evaluation. "Keep as is" is an acceptable and likely outcome, and any change to the backup format is a separate decision that needs the owner's approval, because the archive guards the real data.
- The probe results in "Current State" were measured against Identity 10.0.14 in a scratch project outside the repository. FR-018 turns them into tests so the repository, not this document, is the lasting record.
- Where the owner's existing tests encode the old envelope or the old header, updating them is part of this feature (FR-003 allows it where a story names the change).
