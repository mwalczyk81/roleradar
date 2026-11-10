# Research: Standards-first cleanup (014)

Facts were verified on 2026-11-10 against the working tree, the public package registries and the public `dotnet/aspnetcore` issue tracker (read only: nothing was filed, commented or reacted to). Probes ran in scratch projects outside the repository with fictional data and an in-memory database, and none of that code is committed. Each item records the decision, the reason and the alternatives rejected. The plan's "Standards considered" table summarizes it.

The two-week rule used for every version below is the repository's usual one (ADR 0038): take the newest release that is at least 14 days old, which on 2026-11-10 means published on or before 2026-10-27.

## Baseline facts that shape the design

- The identity sign-in page checks the one-time code with the hand-written verifier (`OwnerProvisioningService.VerifyTotpAndActivateAsync`) whenever `GetTwoFactorAuthenticationUserAsync()` returns the account, and counts failures with `AccessFailedAsync` itself. The platform provider through `SignInManager.TwoFactorAuthenticatorSignInAsync` is only the fallback branch for an unresolved account. Enrolment activation (`create-owner`) and `demo totp` use the hand-written code too.
- Existing secrets are stored in the platform's own authenticator-key slot (`AspNetUserTokens`, provider `[AspNetUserStore]`, name `AuthenticatorKey`), as Base32.
- API errors are `ErrorEnvelope` (`ok`, `errorCode`, `message`, `details[{field, issue}]`, `correlationId`), written through `ErrorResults`, `ApiExceptionHandler` (already an `IExceptionHandler`) and `ODataExceptionFilter`.
- `CorrelationMiddleware` exists twice (API, Identity) plus `CorrelationId` in `RoleRadar.Observability`, `HttpRequestContext`, `RunRequestContext` (CLI), `McpRequestContext`, and on the web `acceptCorrelationId` and `newCorrelationId` in `observability.ts`, the `RequestContext` store and `withCorrelationHeader`.
- Web `mapApiError` reads `details[].message` where the contract field is `issue`, and compares `errorCode` against upper-case literals (`CONCURRENCY_CONFLICT`) where the server sends lower case (`concurrency_conflict`). Both are existing defects (013 research, RR-64). Story P2 rewrites this function from the new contract, so the defects disappear as a side effect and need a test.
- `fetchWithPolicy` (web, 013 US3): reads attempted at most 3 times (network error, 5 s per-attempt timeout, 502, 503, 504) with a 200 ms then 500 ms backoff, writes sent once with a 10 s timeout, 400, 401, 404, 409 and 500 returned at once, a caller abort never retried, one warn record on a final 503. `request-limiter.server.ts` is a 36-line FIFO limiter used by the Insights loaders.
- Identity keeps its own SQLite store with EF Core migrations applied at startup (`Database.Migrate()`, ADR 0015). The API starts at build time to generate the OpenAPI document, so any startup code in the API must stay quiet when Mongo is unreachable (013 lesson).
- NuGet uses central package management and `RestorePackagesWithLockFile`: 12 `packages.lock.json` files (8 source projects, 4 test projects). A Dependabot PR failed with NU1004 when one was not regenerated.

## R-001: One-time code verification

**Decision**: Verify with the platform's authenticator provider: `UserManager.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, code)`. The sign-in page keeps its structure (resolve the account, check lockout, verify, `AccessFailedAsync`, the four security events), and only the verifier behind `VerifyTotpAndActivateAsync` changes. Key generation uses `UserManager.GenerateNewAuthenticatorKey()` or `ResetAuthenticatorKeyAsync`.

**Probe (Identity 10.0.14, in-memory store)**:
- A key in today's format (20 random bytes, Base32, written straight into the token row) reads back unchanged through `GetAuthenticatorKeyAsync`. No re-enrolment.
- Accepted offsets from the current 30-second step: -2, -1, 0, +1, +2 (five codes). Rejected: -4, -3, +3, +4.
- The same code was accepted on three consecutive verifications.
- `GenerateTwoFactorTokenAsync` with the authenticator provider returns an empty string, so it cannot supply a code for the demo or the tests.
- `ResetAuthenticatorKeyAsync` produces a 32-character Base32 key.

**Rationale**: It is the platform feature ADR 0017 already named, it is maintained by Microsoft, and it removes the hand-written RFC 6238 and Base32 code from a security path.

**Alternatives rejected**:
- *Otp.NET verification (`VerifyTotp` with a window)*: a maintained library, but the platform provider is already in the sign-in path and the lockout and security-stamp handling hang off it.
- *Keep the hand-written verifier with an ADR*: it has no replay protection either, and nothing makes it better than the platform's.

## R-002: Window of accepted codes

**Decision**: Accept the provider's window (two steps either side). It cannot be configured: the open upstream issue `dotnet/aspnetcore#27088` asks for exactly that. The ADR records the deviation from RFC 6238 section 5.2 ("at most one time step ... as the network delay") and the mitigations: replay protection (R-003) and lockout at 5 attempts and 15 minutes. No in-repo code narrows the window, because narrowing needs a hand-written code check again (clarification, 2026-11-10).

## R-003: Replay protection (what upstream has)

Searched `dotnet/aspnetcore` (read only) on 2026-11-10:
- `#14423` "User can verify multiple times with the same two factor token": quotes RFC 6238 section 5.2 ("The verifier MUST NOT accept the second attempt of the OTP after the successful validation"). Closed as completed on 2024-08-16, the day after `dotnet/AspNetCore.Docs#33373` "Document that TOTP codes can be reused before it expires" was merged. A community pull request, `#57728` (2024-09-06, "#14423 ..."), was closed without merging on 2024-10-11. Two later comments (2024-09-09, 2024-10-28) ask for it to be reopened; it is still closed.
- `#54524` "ASP.NET Core Identity .NET 8 two factor authenticator does not comply with the rfc6238 standard": open. A commenter suggests a table of used tokens with periodic cleanup.
- `#45969` "TOTP are not one time": closed as a duplicate of `#14423`.
- `#27088` "Allow Rfc6238AuthenticationService time-step size and time-step window to be configurable": open since 2020, labels `triaged`, `severity-major`, `affected-very-few`.
- `#5761` "Improve test ability of TOTP": open, no comments.

**Reading**: upstream documents the gap and has no fix planned. A contribution is possible in principle, but the one community attempt was closed unmerged, so acceptance is uncertain. Nothing was filed. Contributing needs the owner's explicit approval (Principle X), and `dotnet/aspnetcore` requires the .NET Foundation CLA.

**Decision**: A thin in-repo extension, temporary, with an ADR that links `#14423` and `#54524` and states that an upstream contribution awaits the owner's approval. The extension wraps the provider call:

1. The provider says the code is valid.
2. The extension records the code for that user, atomically, in the identity store.
3. If the record already exists, the attempt is a replay: it returns false, and the page treats it as a failed attempt (`AccessFailedAsync`, event 5004, lockout rules unchanged).

The record key is an HMAC-SHA-256 of the code, keyed by the user's authenticator key, so it holds no code in readable form and becomes meaningless if the key is rotated. A unique index on (user id, hash) makes two simultaneous attempts safe: one insert wins. Entries older than 5 minutes are removed on the next insert (a code stays valid for at most 150 seconds after acceptance). The record lives in the identity store (a new EF Core migration), which survives a restart.

**Known, accepted limit**: the provider does not say which step matched, so the record is per code value. A genuine later code that happens to equal an earlier used one inside the 5-minute retention (about one chance in 100,000) is rejected once and the owner enters the next code. The ADR records it.

**Alternatives rejected**:
- *Track the last accepted time step*: needs the matched step, which the provider does not return, so it would need a second hand-written verifier.
- *In-memory record*: lost on a restart inside the window (edge case in the spec).
- *Otp.NET `VerifyTotp(..., out timeStepMatched)`*: returns the matched step but would replace the provider as the verifier and still needs our own store.

## R-004: Code source for the demo command and the tests

**Decision**:
- .NET (the demo `totp` command ships in the CLI): **Otp.NET 1.4.1** (MIT, published 2025-12-01, the library the .NET community uses for this). It also supplies the `otpauth://` enrolment URI (`OtpUri`) in place of the hand-formatted string. It is referenced by `RoleRadar.Identity` (which provisions) and reaches the CLI through its project reference. The hand-written `ComputeTotpCode`, `ComputeTotpForStep`, `Base32Encode`, `Base32Decode` and `GenerateBase32Secret` are deleted.
- Web end-to-end helper (`web/tests/e2e/support/totp.ts`, 41 lines of test-only RFC 6238 and Base32): replace with **otpauth 9.5.2** (MIT, published 2026-09-03, dev dependency, one dependency of its own, `@noble/hashes`). `web/tests/tooling/totp-helper.test.ts` keeps the RFC 6238 test vectors and runs them against the library, which is the parity proof RR-77 asks for.

**Rationale**: Principle X needs a reason for hand-written test code, and "a small file" is not one. A test-only helper would be a justified survivor only if the library cost more than it saves. Both libraries are small, maintained and licensed MIT, and the web one is a dev dependency, so it never ships.

**Alternatives rejected**:
- *Keep the 41-line TypeScript helper with an ADR*: possible, but the library is a dev dependency with no runtime cost.
- *`otplib`*: also maintained; `otpauth` is smaller and has fewer transitive packages.
- *Add a code generator next to the provider*: this is the hand-rolled mechanism the story removes.

## R-005: Problem details on the .NET side

**Probe (a scratch ASP.NET Core 10 app with `AddProblemDetails`, an `IExceptionHandler` writing through `IProblemDetailsService`, `UseStatusCodePages`, `TypedResults.ValidationProblem`)**:
- 500, 400, 404 and 409 came back as `application/problem+json`. A 404 with no body was filled in by `UseStatusCodePages`.
- The `errors` member of `ValidationProblemDetails` is `{ "field": ["issue", ...] }`, which carries our `details[{field, issue}]`.
- Extension members (`code` in the probe) appear in the body, so `errorCode` can be carried as an extension.
- **`traceId` is the full `Activity.Id` (`00-<32 hex>-<16 hex span>-<flags>`), not the bare trace id.** The span part differs on every hop, so that default value would not match across web, API and Identity. A `CustomizeProblemDetails` callback must set `traceId` to `Activity.Current.TraceId` (32 hex).
- The default `type` is an RFC 9110 URL (`https://tools.ietf.org/html/rfc9110#section-15.5.1`). Our `type` replaces it per error code.
- The ASP.NET default `type` for 401 and 403 is produced by the status-code pages; the plan includes a test that a bearer-token 401 and a 403 from authorization get the same body.

**Decision**: `AddProblemDetails` with `CustomizeProblemDetails` (sets `traceId`, `errorCode`, `type`), the existing `ApiExceptionHandler` writing through `IProblemDetailsService`, `UseStatusCodePages` for empty-body 4xx, `ValidationProblemDetails` for field errors, and `Retry-After` kept on 503. `ErrorEnvelope`, `ErrorResults` and `HeaderAddingResult` are deleted.

**Problem `type` identifier**: RFC 9457 section 3.1.1 wants a URI reference, ideally one that resolves to documentation. There is no hosted domain yet. The spec's working assumption was a URN, but an informal `urn:roleradar:` namespace is not registered (RFC 8141), so it would be the kind of home-grown identifier this feature removes. RFC 4151 tag URIs exist for exactly this case (a permanent, non-resolvable identifier with no hosting): `tag:mattwalczyk.com,2026:roleradar:problem:<errorCode>`. The authority name must be one the author controlled on that date; the personal site domain qualifies. **The owner confirms the authority name at review.** Fallback if not: a documentation URL in the public repository (`docs/api/problem-types.md#<code>`), which RFC 9457 prefers because it resolves, at the cost of tying the identifier to the repository location. A short `docs/api/problem-types.md` lists each code either way.

**Alternatives rejected**:
- *Keep the envelope*: it is the miss the feature exists to fix.
- *A custom `ProblemDetails` subclass with typed `ErrorCode` and `TraceId`*: ASP.NET already writes `traceId` as an extension, so a typed property of the same name risks a duplicate JSON key. Typed members come from an OpenAPI schema transformer instead (R-007).
- *`Hellang.Middleware.ProblemDetails`*: predates the platform feature; the platform now covers it.

## R-006: OData error format

**Decision**: RFC 9457 for the OData routes too (clarification, 2026-11-10). `ODataExceptionFilter` keeps catching `ODataException` and now returns a 400 problem document with `errorCode` `validation_error` and the parser message as `detail`.

**Compared**: OData JSON Format 4.01 error response (`{ "error": { "code", "message", "target", "details" } }`), which is what the OData library emits natively and what a generic OData client such as a spreadsheet or BI tool would expect.

**Reason**: The only consumer is the typed web client (`odata-client`), which already branches on the status and reads one error shape from `/api/v1`. One format gives it one parsing path and removes a second error model from the contract tests. **Cost, recorded in the ADR**: a generic OData client sees a problem document instead of the OData error shape. RoleRadar is single-owner and local-first and does not publish `/odata` for third-party clients, so the cost is accepted. This is a choice between two standards, so the ADR names the OData format as the alternative and the reason it lost.

## R-007: Typed members in the OpenAPI document

**Decision**: Keep `application/problem+json` response entries on each operation (`ProducesProblem` and `ProducesValidationProblem`), and add an OpenAPI schema transformer that gives `ProblemDetails` the typed members `errorCode` (string) and `traceId` (string). The generated client types then carry them, and the web reads them through the generated types without casts (AGENTS.md rule). The transformer runs at build time while the API starts, so it must log nothing at warning level (013 lesson). `EnumContractRegistry` is not involved: `errorCode` stays a string. A snapshot test fails if the operation responses or the schema change.

## R-008: Trace context on the .NET side

**Probe**: ASP.NET Core creates the request `Activity` without any listener, in W3C format. A valid inbound `traceparent` is honoured (same trace id, new span id, the inbound span becomes the parent). A malformed `traceparent` is ignored and a new trace id is generated.

**Decision**: Use `System.Diagnostics.Activity` and the built-in `DistributedContextPropagator` (default W3C). ASP.NET Core and `HttpClient` already extract and propagate `traceparent`, so no middleware is needed.
- A single helper in `RoleRadar.Observability` replaces `CorrelationId`: the current trace id is `Activity.Current?.TraceId.ToString()`, and where no activity exists (CLI run, MCP call, a background task) it is `ActivityTraceId.CreateRandom().ToHexString()`, a BCL type for exactly this.
- `HttpRequestContext` (API actor and audit), `RunRequestContext` (CLI) and `McpRequestContext` read it. The console formatter reads `Activity.Current` instead of a scope key.
- The log field that carried `correlationId` becomes `traceId` (OpenTelemetry names the same value `TraceId`, so feature 015 does not rename it again).
- The audit field keeps its stored name `request_correlation_id`, because audit entries are append-only and renaming means migrating data; new entries hold the 32-character trace id, old entries keep their values. `details.after.correlation_id` (ADR 0033) is a different concept and is untouched.

**Alternatives rejected**: a custom middleware and header (what 013 built); `Activity.Id` as the id (full traceparent string, differs per hop).

## R-009: Trace context on the web side (and what feature 015 builds on)

**Compared**:

| Option | What it gives | Cost and fit with 015 |
| --- | --- | --- |
| Hand-written `traceparent` (about 12 lines of `node:crypto` and string formatting) | Nothing new | Re-implements validation that is easy to get wrong (all-zero ids, version `ff`, upper case, the flags byte). Feature 015 deletes it. Needs an ADR. |
| `@opentelemetry/api` 1.10.0 plus `@opentelemetry/core` 3.0.0 (the W3C propagator), no SDK | Extract and validate an inbound `traceparent`, inject an outbound one; ids from `node:crypto` | 2 packages (+ `@opentelemetry/semantic-conventions`), all Apache-2.0, Node 22 compatible. No exporter, no instrumentation, nothing leaves the machine (013 FR-028). The API package is the one feature 015's SDK registers under, so the same calls keep working. |
| The full SDK now (`sdk-trace-node`, `instrumentation-http`, `instrumentation-undici`) | Spans created and propagated automatically | It is feature 015: preload flag in the image, exporter policy, sampling, more packages. Not needed to deliver one id. |
| Node or undici built-ins | Nothing generates or propagates trace context | Not an option. |

**Probe (Node 22, api 1.10.0, core 3.0.0)**: the propagator accepted a valid `traceparent`, and rejected a malformed one, an all-zero trace id and an upper-case one. It injected a correct `traceparent` from a span context built from random ids.

**Decision**: `@opentelemetry/api` and `@opentelemetry/core`, no SDK. At the request edge the web extracts a valid inbound `traceparent` (otherwise generates ids), stores `traceId` and `spanId` in the existing `AsyncLocalStorage` request context, injects `traceparent` on every server-side call to the API and to Identity (the `openid-client` `customFetch` hook already used for the correlation header), and logs `traceId`. No response header (spec assumption).

**What feature 015 builds on**: `@opentelemetry/api`, with the SDK and the HTTP and undici instrumentations on top. Those create the server span from the inbound request and inject `traceparent` on outgoing calls, so 015 deletes the manual root span context and explicit injection written here. The trace id format, the log field name `traceId`, the error-body member `traceId` and the audit value are unchanged, so no id is migrated twice. The ADR says this in one paragraph.

`@opentelemetry/core` 3.1.1 and 3.1.0 are under 14 days old, so 3.0.0 is pinned.

## R-010: Web retry, backoff, timeout and `Retry-After`

**Compared**, at the 013 policy (reads at most 3 attempts, 5 s per-attempt timeout, 200 and 500 ms backoff, writes once with 10 s, 400, 401, 403, 404, 409 and 500 returned at once, a caller abort not retried):

| Option | Retry-After | Timeout and abort | Fit with the current code and tests |
| --- | --- | --- | --- |
| undici retry interceptor (`undici` 8.12.3) | Honoured by default (`retryAfter: true`, capped by `maxTimeout`) | Per attempt through socket timeouts, not an abort signal | Defaults retry `PUT` and `DELETE` and status 500, 429, so the policy must be restated. Works on dispatchers: needs undici's own `fetch` with the dispatcher (Node's bundled fetch can reject a dispatcher from the npm package). The tests stub `globalThis.fetch` and use fake timers, which would need a mock agent and real sockets instead. A bigger change than the others. |
| `p-retry` 8.0.1 | Not supported; needs our own header parsing | `signal`, `shouldRetry`, `onFailedAttempt`; fake timers work | Retry only; timeout and limiter stay hand-written, and Retry-After becomes a new hand-written helper. (9.0.0 was published 2026-11-02, too new.) |
| `cockatiel` 4.0.0 | Not built in. A `DelegateBackoff` receives the failed result, so a small parser of our own would read the header | `timeout` and `bulkhead` (a limiter) in the same package, abort signals, fake timers work | One package covers retry, timeout and limiter, but the Retry-After parser is new hand-written code. |
| **`ky` 2.2.0** | **Honoured** (`afterStatusCodes` 413, 429, 503; seconds or HTTP date; `maxRetryAfter` clamps the wait, and a `shouldRetry` veto refuses a longer one) | **Per-attempt `timeout`**, separate `totalTimeout`, abort signal, `retryOnTimeout` | Built on the global `fetch`; takes a `Request`; `methods`, `statusCodes`, `retry.limit`, `delay`, `shouldRetry`, `beforeRetry` hooks cover the policy. Statuses are retried through its `HTTPError`, so the wrapper catches `HTTPError` and returns `error.response`. |

**Probe (Node 22, ky 2.2.0, p-limit 7.3.3, a local server)**, with reads `limit 2`, `methods ["get","head"]`, `statusCodes [502,503,504]`, `retryOnTimeout true`, `delay` 200 then 500, `maxRetryAfter 2000`:
- GET 503 with `Retry-After: 1` then 200: 2 attempts, 1.02 s (the header was honoured), status 200.
- GET persistent 503: 3 attempts, then the 503 response; 2.0 s total.
- GET 404 and GET 500: 1 attempt each. POST 503: 1 attempt.
- GET that never answers (1.5 s timeout): 3 attempts, then `TimeoutError`. A caller abort at 300 ms: 1 attempt, abort propagated.
- With `throwHttpErrors: false` ky does not retry on status, so the policy must keep the default and catch `HTTPError` (an easy mistake the tests will catch).
- p-limit with 2 slots and 6 tasks: at most 2 running.

**Decision**: **`ky` for retry, timeout and `Retry-After`, and `p-limit` 7.3.3 (MIT, published 2026-09-18) for the limiter.** The hand-written `sleep`, `abortable`, backoff and `shouldRetry` helpers and `request-limiter.server.ts` are deleted. `apiFetch` stays the single entry point and still passes `fetch: (input, init) => globalThis.fetch(input, init)` to ky so tests can stub `globalThis.fetch` per call.

**Retry-After bound**: the API sends `Retry-After: 5` on a 503. Honouring it in full would put 10 s of waiting into two retries and break the Insights loaders' 5 s caller bound. The bound is 2000 ms (`RETRY_AFTER_BOUND_MS`). Per the spec (edge case, FR-042) a header above the bound means **no retry**, not a clamped wait. A second probe (2026-11-10, ky 2.2.0, `Retry-After: 5`, `maxRetryAfter` 2000) showed that ky's own option clamps: 3 attempts and 4 s. A `retry.shouldRetry` that returns false when the header exceeds the bound gave 1 attempt and an immediate 503. So the veto is the mechanism, and the first red test of the story re-asserts it against the pinned version. **Consequence the owner should know**: the API always sends `Retry-After: 5`, so a real 503 from the API is returned at once by the web and is not retried; network errors, timeouts, 502, 504 and a 503 without the header still retry. If that is not wanted, the options are to raise the bound to 5000 ms (the Insights loaders' own 5 s signal then bounds them) or to lower the API's `Retry-After`; either is a one-constant change and an ADR line.

**Fallback if a fake-timer or abort test shows ky cannot keep the 013 `api-policy` tests green**: `cockatiel` (one package for retry, timeout and limiter) plus a documented Retry-After parser with its own ADR. The first red test decides; it is an early task of the story.

**Amendment**: this supersedes 013 FR-027 ("without a retry policy library"). The plan adds a one-line pointer under FR-027 in the 013 spec; FR-026 and FR-028 of 013 stay.

**Supply chain**: `ky` has no dependencies; `p-limit` has one (`yocto-queue`). Both MIT, both require Node 22 or newer for the versions pinned (`ky` >=22, `p-limit` >=20), and the web image is `node:22-alpine`.

## R-011: Redaction (RR-80)

**Compared**: `Microsoft.Extensions.Compliance.Redaction` (MIT; 10.11.0 published 2026-10-14) with data classification and `[LogProperties]` on `LoggerMessage` parameters, against `LogScrubber` (84 lines of regex over formatted text, mirrored in `web/app/lib/observability.ts`).

**Facts**: classification redacts at the source, only for parameters the code classifies. It does not touch text from third parties (013 caught a raw Duende authorize request with the scrubber) or exception messages. 013 rejected it as a replacement (013 R-001). The evaluation is different: adopt it for what it covers and keep the scrubber for the rest.

**Expected outcome (the spike decides)**: classify our own log parameters with the platform redactors so sentinel values never reach a log line, keep a smaller scrubber as the backstop for third-party text, and document why both exist. The 013 redaction suites (unit, contract, integration, web) stay green unchanged. If classification touches many `LoggerMessage` definitions for little gain, the spike records "keep as is" with the numbers.

## R-012: Console formatter and validators (RR-81)

**Console formatter** (`RoleRadarConsoleFormatter`, 314 lines): compare `AddJsonConsole` and `AddSimpleConsole`. 013 found the built-in JSON formatter puts scopes in an array and has no service field or scrubber hook. **Expected outcome**: keep with an ADR now, because the text and JSON toggle and the scrubber hook have no built-in equivalent; revisit in feature 015 when the OpenTelemetry log exporter may make the formatter redundant. The formatter is edited in P2 anyway to read the trace id.

**Validators** (10 classes implementing `ICommandValidator<T>` or query validators in `RoleRadar.Application`, plus `CsvHeaderValidator`): compare FluentValidation 12.x (Apache-2.0) and .NET 10 minimal API validation (HTTP layer only, so it cannot serve the CLI and MCP). Validators live in Application and run in the decorator for all three hosts, so DataAnnotations do not fit. **Rule from the card**: adopt only if it removes code without changing a result. **Expected outcome**: the spike converts two validators, measures lines and results, and either adopts or records "keep" with the numbers. Either way ADR 0006 and 0009 are re-read first for the recorded reason.

## R-013: Backup archive format (RR-82)

**Compared**: the current `SecretStreamBackupArchiveService` (1,420 lines, libsodium secretstream and Argon2id, ADR 0007) against `age` (passphrase mode uses scrypt; recipient mode X25519; the file format is specified at C2SP), `mongodump --archive --gzip` piped to `age`, and `restic`.

**Criteria (from the card)**: format stability, audit history (to be checked, not assumed), streaming of large archives, passphrase handling, restore verification into a scratch database, Windows and container support, and migration of existing archives (the current format must stay restorable).

**Constraints**: fictional data only, in a Testcontainers scratch Mongo. The spike never connects to the developer's database or the compose Mongo (AGENTS.md). The owner, not an agent, runs anything against the real database. The result is a comparison and an ADR (0043, the number RR-82 names), including "keep as is" if that wins. Any change to the backup format is a separate, owner-approved decision.

**Expected outcome**: keep, unless `age` meets every criterion including old-archive restore.

## R-014: Scripts already covered by existing tools (RR-83)

**Compared**:
- `scripts/licence-audit.mjs` (207 lines, an allowlist of SPDX ids for the Markdown dependency tree, failing on an unknown or missing licence) against `trivy fs --scanners license` (Trivy already runs in CI and in the review gate), `pnpm licenses`, and the Dependency Review action (already runs on pull requests with `deny-licenses`).
- `scripts/verify-action-pins.ps1` (58 lines: each `uses:` is a 40-character SHA with a version comment, and the SHA exists) against `zizmor` (pre-commit v1.32.0 and its `unpinned-uses`, `impostor-commit` and `ref-version-mismatch` audits) and Dependabot, which keeps the pins current.

**Not yet verified** (the card says so, and it is the first task of the item): that zizmor is configured to require hash pins, and that Trivy's licence classification matches the ADR 0032 allowlist, which fails on unknown licences. **Method**: for each policy, make a deliberate violation in a throwaway change (never committed), show the script and the tool both fail, then replace or keep. `denylist-check.ps1` and `review-gate.ps1` are project-specific orchestration and stay.

## R-015: Lock files, startup noise, Dependabot (lessons from 013)

- Every NuGet change, including a version bump in `Directory.Packages.props`, regenerates the lock file of every project that references the package directly or through a project reference. The recipe is `dotnet restore RoleRadar.sln --force-evaluate`, and `dotnet restore RoleRadar.sln --locked-mode` must then pass. Each story's gate lists both. Packages touched: Otp.NET (Identity, reaching the CLI and the test projects that reference them) in P1, and `Microsoft.Extensions.Compliance.Redaction` and FluentValidation only if P4 adopts them. No NuGet package is added by P2 or P3 (the platform features are in the shared framework).
- npm: `pnpm install` regenerates `pnpm-lock.yaml`, and CI installs with `--frozen-lockfile`. Packages: `otpauth` (dev) in P1, `@opentelemetry/api` and `@opentelemetry/core` in P2, `ky` and `p-limit` in P3. Each is pinned exactly in `web/package.json`.
- The review gate builds with Mongo unreachable. New code in the API (problem-details registration, the OpenAPI schema transformer, the exception handler) must write nothing at warning level or above at startup. A test or the gate's build step shows it.
- `.github/dependabot.yml` is never edited (any edit triggers an immediate run). New packages are picked up by the existing weekly schedule.
