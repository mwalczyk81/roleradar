# Implementation Plan: Standards-first cleanup

**Branch**: `014-standards-first` | **Date**: 2026-11-10 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/014-standards-first/spec.md`

## Summary

Replace hand-rolled mechanisms with the standard where it is a clear fit, and write down the reason for each one that stays. Four stages, each shippable on its own and each ending at a review gate: P1 the one-time-code check (platform authenticator provider plus a thin, temporary replay guard), P2 RFC 9457 problem documents and W3C Trace Context as one migration, P3 maintained web retry and limiter libraries, P4 four evaluate-or-justify items.

The plan's main artifact is the "Standards considered" table below. Its evidence is in [research.md](research.md), including the probes that changed the design: the platform provider accepts a reused code and a five-code window, and cannot generate codes; ASP.NET Core's problem-details `traceId` is the full span-qualified `Activity.Id` and must be overridden; ky honours `Retry-After` and fits the existing tests where undici, p-retry and cockatiel each leave a gap.

## Technical Context

**Language/Version**: C# on .NET 10 (nullable, warnings as errors); TypeScript on Node 22 for the web app.

**Primary Dependencies**: ASP.NET Core Identity 10.0.14, ASP.NET Core ProblemDetails and OpenAPI, EF Core (Identity store), Otp.NET 1.4.1 (new); web: React Router 7, `openapi-fetch`, `openid-client`; new `@opentelemetry/api` 1.10.0, `@opentelemetry/core` 3.0.0, `ky` 2.2.0, `p-limit` 7.3.3, and `otpauth` 9.5.2 (dev). P4 may add `Microsoft.Extensions.Compliance.Redaction` and FluentValidation, only if its spike adopts them.

**Storage**: Identity SQLite store gains one table (used one-time codes) through an EF Core migration. MongoDB is unchanged: the audit field `request_correlation_id` keeps its name and now holds a trace id for new entries.

**Testing**: xUnit across the existing Architecture, Unit, Contract and Integration projects (no new test projects); Vitest and Playwright for the web. Tests first and shown failing. The existing contract, 013 sign-in event, 013 correlation and redaction, `api-policy` and Insights tests are the parity proof. Isolated containers only; fictional data only.

**Target Platform**: Linux containers (API, Identity, web) and the Windows or Linux CLI and MCP host.

**Project Type**: Multi-host service (API, Identity, web, CLI, MCP) on a shared Application layer.

**Performance Goals**: None new. The 013 bounds are kept (reads at most 3 attempts, writes once) with one change the owner accepted: a 503 carrying `Retry-After` is retried only when the header asks for no more than 2000 ms. The API always sends `Retry-After: 5` on a 503, so a real API 503 is returned at once and is **not retried by the web**. A 502, a 504, a network error, and a 503 without the header or within the bound are still retried. Recorded in ADR 0045 and the tasks Notes.

**Constraints**: Owner-visible behavior unchanged except where a story says so (FR-002). Breaking the API contract is allowed, once, regenerating the OpenAPI document and the generated client. No real data anywhere. `.github/dependabot.yml` is not edited. Every new package pinned exactly, MIT or Apache-2.0, no telemetry, release at least 14 days old (ADR 0038).

**Scale/Scope**: Single owner. About 12 .NET lock files and the pnpm lock file regenerate. About 15 mechanisms get a recorded outcome.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Evidence |
|-----------|--------|----------|
| I. Privacy first | PASS | All probes and tests use fictional users and in-memory or scratch stores. The backup spike (P4) uses a Testcontainers scratch Mongo only. No real data, secret or code in logs; the used-code record stores an HMAC, not a code. |
| II. Test-first | PASS | Each story starts with red tests (task order in `tasks.md`). Integration tests use Testcontainers. The one existing parity suite per area is named in the stage gates. |
| III. Test-enforced layering | PASS | No new project and no layering change. Command handlers keep the transaction-and-audit decorator. The new Identity code stays inside `RoleRadar.Identity`; `RoleRadar.Observability` still references no RoleRadar project. |
| IV. Named seams only | PASS | No generic repository, mediator, factory or event bus. The replay guard is a concrete class inside Identity, not an abstraction. ky and p-limit are libraries, not seams. |
| V. Agent safety and auditability | PASS | No MCP tool added or changed (still 12). Audit stays append-only; old entries are not rewritten. Posting and error text stays untrusted. |
| VI. stdio hygiene | PASS | The MCP host changes only the per-call id. Nothing is written to stdout. |
| VII. Data durability | PASS | No datastore reset or destructive script. The backup format is evaluated, not changed, and any change needs the owner's approval. |
| VIII. ADRs and pins | PASS | Every package is pinned exactly and checked against MIT. Seven ADRs are planned (below). Lock files regenerate with each package change. |
| IX. Baseline conventions | PASS | The new `UsedAuthenticatorCodes` table stores UTC timestamps and carries `OwnerId` (equal to the account's user id) from creation. Framework-owned ASP.NET Identity tables are exempt from the `owner_id` rule because we do not create them and cannot add columns without forking the schema; ADR 0042 says so. Audit stays on UUIDv7 ids. |
| X. Standards first | PASS | Every mechanism has a row below; every survivor links a planned ADR. Re-checked after design: no row changed. |

## Standards considered

Every mechanism in the spec has a row. "ADR" is the planned record (numbers are reserved here; RR-82 already names 0043 for the backup decision, and `docs/adr/README.md` is the source of truth). Detail and probes are in [research.md](research.md).

### P1: one-time-code check

| Mechanism | What was checked | What was chosen | Why (ADR if hand-rolled) |
|-----------|------------------|-----------------|--------------------------|
| One-time code verification | RFC 6238; ASP.NET Core Identity `UserManager.VerifyTwoFactorTokenAsync` with the default authenticator provider; Otp.NET `VerifyTotp` (R-001) | The Identity provider | Maintained by Microsoft, already named in ADR 0017, already in the sign-in fallback path, reads existing secrets unchanged (probe), keeps the lockout and security events. The hand-written RFC 6238 and Base32 code is deleted. |
| Accepted code window | RFC 6238 section 5.2 (at most one step of drift); provider window (two steps either side, not configurable, upstream `#27088` open) (R-002) | The provider's window | Narrowing needs a hand-written check. **ADR 0042 records the deviation and the mitigations** (replay guard, lockout 5 attempts and 15 minutes). |
| Replay protection | RFC 6238 section 5.2 ("MUST NOT accept the second attempt"); NIST SP 800-63B 5.1.4.2 as cited upstream; the provider (none, probe); `dotnet/aspnetcore` issues `#14423` (closed after the docs said codes can be reused; community PR `#57728` closed unmerged), `#54524` (open), `#45969` (closed duplicate); Otp.NET (returns the matched step but would replace the provider) (R-003) | **A thin in-repo extension**: a used-code record (HMAC of the code, per user, unique index, 5-minute retention) in the Identity store, wrapped around the provider call | Upstream documents the gap and has no fix planned, and the one attempt to fix it was closed. A near fit extends thinly (Principle X). **ADR 0042** marks it temporary, links `#14423` and `#54524`, and states that an upstream contribution awaits the owner's approval (nothing was filed). |
| Code source for the demo `totp` command and the e2e helper | The provider (cannot generate: empty string, probe); Otp.NET 1.4.1; `otpauth` 9.5.2; `otplib`; a test-only helper (the existing 41-line TypeScript one) (R-004) | Otp.NET in Identity and the CLI; `otpauth` as a web dev dependency; RFC 6238 vectors kept as the parity test | The demo command ships in the CLI, so production code may not hold a hand-written generator. For the test-only TypeScript helper a library costs a dev dependency and nothing at runtime. Otp.NET also replaces the hand-formatted `otpauth://` URI. |

### P2: error body and correlation id

| Mechanism | What was checked | What was chosen | Why (ADR if hand-rolled) |
|-----------|------------------|-----------------|--------------------------|
| API error body | RFC 9457; ASP.NET Core `AddProblemDetails`, `IProblemDetailsService`, `IExceptionHandler`, `UseStatusCodePages`, `ValidationProblemDetails`; `Hellang.Middleware.ProblemDetails`; the private envelope (R-005) | RFC 9457 through the platform features, with `errorCode` and `traceId` as extension members; the envelope, `ErrorResults` and `HeaderAddingResult` deleted | Built in, no gap. Probe finding: the default `traceId` is `Activity.Id` and is overridden with the bare trace id in `CustomizeProblemDetails`. **ADR 0044** supersedes ADR 0018 section 2. |
| OData error format | OData JSON Format 4.01 error response (the alternative compared); RFC 9457 (R-006) | RFC 9457 for `/odata` too | The only consumer is the typed web client, so one format gives one parsing path. Cost recorded in the ADR: a generic OData client sees a problem document. |
| Problem `type` identifier | RFC 9457 section 3.1.1; ASP.NET default RFC 9110 URLs; informal `urn:roleradar:` (not a registered URN namespace, RFC 8141); RFC 4151 tag URI `tag:mattwalczyk.com,2026:roleradar:problem:<errorCode>` (permanent, needs no hosting, but does not resolve); a documentation URL in the repository (R-005) | A documentation URL in the repository, one anchor per error code: `https://github.com/mwalczyk81/roleradar/blob/main/docs/api/problems.md#<errorCode>`, with `docs/api/problems.md` holding the anchors | RFC 9457 prefers a `type` that resolves to human-readable documentation, and this one does. Owner decision, 2026-11-10. The cost: the identifier is tied to the repository location, so the base URL is one constant (`ProblemTypes.TypeUriPrefix`) that can move if the API is hosted. The tag URI is the alternative compared and is recorded in **ADR 0044**. The web switches on `errorCode`, not on `type`. |
| Typed `errorCode` and `traceId` in the generated client | `ProducesProblem`; a `ProblemDetails` subclass (risks a duplicate `traceId` key); an OpenAPI schema transformer (R-007) | Schema transformer on the platform's OpenAPI generation | Keeps the web on generated types without casts (AGENTS.md); a snapshot test guards it. It must stay quiet at build time with Mongo unreachable. |
| Trace context, .NET | W3C Trace Context; `System.Diagnostics.Activity` and `DistributedContextPropagator`; ASP.NET Core and `HttpClient` propagation; `ActivityTraceId.CreateRandom()` (R-008) | The platform features; one helper in `RoleRadar.Observability` returns the current trace id; CLI and MCP use `ActivityTraceId.CreateRandom()` | Built in, no gap (probe: inbound honoured, malformed replaced). `X-Correlation-ID`, both `CorrelationMiddleware` classes, `CorrelationId` and the scope key are deleted. Log field `correlationId` becomes `traceId` (the OpenTelemetry name). **ADR 0044** supersedes the correlation section of ADR 0041. |
| Trace context, web (and feature 015) | Hand-written `traceparent`; `@opentelemetry/api` + `@opentelemetry/core`; the full OpenTelemetry SDK with instrumentations; Node and undici built-ins (none) (R-009) | `@opentelemetry/api` 1.10.0 and `@opentelemetry/core` 3.0.0, no SDK, exporter or instrumentation | A maintained propagator validates and formats `traceparent` (probe) where a hand-written one would re-learn its edge cases. **Feature 015 builds on `@opentelemetry/api`**, adding the SDK and the HTTP and undici instrumentations; it deletes only the manual root context and explicit injection, so the id is not migrated twice. Recorded in ADR 0044. |

### P3: web retry and limiter

| Mechanism | What was checked | What was chosen | Why (ADR if hand-rolled) |
|-----------|------------------|-----------------|--------------------------|
| Retry, backoff, per-attempt timeout, `Retry-After` | undici retry interceptor 8.12.3; `p-retry` 8.0.1; `cockatiel` 4.0.0; `ky` 2.2.0 (R-010) | `ky`, with the 013 policy restated (reads `limit 2`, writes none, statuses 502, 503, 504, 200 and 500 ms delay, 5 s and 10 s timeouts; `Retry-After` honoured up to 2000 ms, and a longer value means no retry, through a `shouldRetry` veto because ky's `maxRetryAfter` only clamps) | Only ky honours `Retry-After` natively, has a per-attempt timeout and abort signal, and works on the global `fetch` the tests stub. undici needs a different fetch and real sockets; p-retry and cockatiel need a hand-written Retry-After parser. **ADR 0045** holds the comparison and the FR-027 amendment. Fallback: cockatiel, decided by the first red test. |
| Concurrency limiter | `p-limit` 7.3.3; cockatiel `bulkhead`; the 36-line hand-written limiter (R-010) | `p-limit` | Maintained, tiny, FIFO like the current one. In **ADR 0045**. |

### P4: evaluate or justify

| Mechanism | What was checked | What was chosen | Why (ADR if hand-rolled) |
|-----------|------------------|-----------------|--------------------------|
| Log redaction (RR-80) | `Microsoft.Extensions.Compliance.Redaction` with data classification and `[LogProperties]` (MIT, 10.11.0) against `LogScrubber` (R-011) | **Kept `LogScrubber`.** No package added. | Classification redacted the service's own parameters but leaked 7 of 7 third-party cases (Duende, Bearer, JWT, Mongo URI, query string, `code` and `state` keys, exception text), removed 0 scrubber lines and added 10 packages; the 013 redaction suites stay green unchanged. [ADR 0046](../../docs/adr/0046-log-redaction.md). |
| Console log formatter (RR-81 a) | `AddJsonConsole`, `AddSimpleConsole`; the OpenTelemetry logging exporter (feature 015) (R-012) | **Kept `RoleRadarConsoleFormatter`.** No package added. | None of `AddJsonConsole`, `AddSimpleConsole` or a Serilog formatter has a hook for scrubbing the final text, which is required. Against the real 27 formatter tests the built-ins fail 13 (`AddJsonConsole`) and 17 (`AddSimpleConsole`); a custom Serilog `ITextFormatter` passes all 17 output cases and is about 24 percent smaller, which the owner may overrule. Revisit when feature 015's OpenTelemetry log exporter lands. [ADR 0047](../../docs/adr/0047-console-formatter-and-validators.md). |
| Command and query validators (RR-81 b) | FluentValidation 12.x; .NET 10 minimal API validation (HTTP layer only); DataAnnotations (do not fit the shared Application layer) (R-012) | **Kept the hand-written validators.** No package added. | FluentValidation 12.2.0 reaches parity only through `Custom` with a prebuilt `ValidationFailure` (it substitutes `{PropertyName}`-style text in caller-supplied values) and saves about 5 percent of characters; ADR 0006 and 0009 gave no reason against a library, and none was adopted. [ADR 0047](../../docs/adr/0047-console-formatter-and-validators.md). |
| Backup archive format (RR-82) | `age` (scrypt passphrase, X25519), `mongodump` piped to `age`, `restic`, against `SecretStreamBackupArchiveService` (ADR 0007) (R-013) | **Kept the `.rrbak` format.** No format, script or package change. | `age` cannot restore an archive made by the current format (a conversion works only through the current decrypt code), needs a separate plugin binary for scripted passphrases, and releases plaintext before it finds a bad chunk (a straight pipe into `mongorestore` left 44,916 of 50,000 postings); `restic` is a repository, not an archive. Measured on fictional data in a scratch container; nothing ran against the real database. Any move to `age` is a separate decision for the owner. [ADR 0043](../../docs/adr/0043-backup-archive-format.md). |
| Licence audit script (RR-83 a) | `trivy fs --scanners license`, `pnpm licenses`, the Dependency Review action (R-014) | **Kept `licence-audit.mjs`.** | No tool reproduces the ADR 0032 allowlist with "unknown fails": Trivy omits a missing or empty licence field, `pnpm licenses` only lists, and Dependency Review documents that it will not fail on an unknown licence (tested on a fictional package tree outside the repository). [ADR 0048](../../docs/adr/0048-scripts-and-existing-tools.md). |
| Action pin verification (RR-83 b) | zizmor (`unpinned-uses`, `impostor-commit`, `ref-version-mismatch`) and Dependabot (R-014) | **Retired `verify-action-pins.ps1`.** CI runs zizmor in its own step with `GH_TOKEN: ${{ github.token }}`, and the main pre-commit step skips it. | zizmor requires hash pins without configuration, and with a token its online audits fail a nonexistent SHA (`impostor-commit`) and a wrong version comment (`ref-version-mismatch`), shown on deliberate violations. The missing-`# vX.Y.Z`-comment check is intentionally dropped (the owner's decision: the SHA is the pin). [ADR 0048](../../docs/adr/0048-scripts-and-existing-tools.md). |

Hand-rolled survivors by design: the replay guard only (ADR 0042, temporary). Any P4 item that is kept gets its ADR in the same stage.

## Delivery stages and gates

One review stop per story, in this order. Each story is shippable alone: P2 and P3 both edit `fetchWithPolicy`, but P3's `ky` hook injects whatever outbound header exists at the time, so either order works; the plan orders P2 first so the edit lands once.

Every gate repeats the same base checks, then the story's own:

- Tests written and shown failing first; then `pwsh ./scripts/test-fast.ps1`.
- `dotnet build RoleRadar.sln -c Release -v q --nologo -warnaserror`, run with Mongo unreachable (the review gate does this), with no warning or error from startup code.
- If any NuGet package changed: `dotnet restore RoleRadar.sln --force-evaluate`, all 12 `packages.lock.json` files committed in the diff, then `dotnet restore RoleRadar.sln --locked-mode` passes.
- If any npm package changed: `pnpm install` updated `pnpm-lock.yaml`, `pnpm install --frozen-lockfile` passes, licences are on the allowlist.
- If the web changed: `pnpm --filter web run typecheck`, `lint`, `test`, `build`, `test:e2e`, and `bash scripts/linux-web-check.sh` from Git Bash.
- `pre-commit run --all-files`, then `pwsh ./scripts/review-gate.ps1` once at the end, with numbers reported from its output.
- `git diff -- .github/dependabot.yml` is empty; no test project added; LF line endings; no real data.

| Gate | Story | Own checks |
|------|-------|------------|
| G1 | P1: one-time-code check | Existing sign-in contract tests and the 013 events 5001 to 5004 green; the window, replay (sequential and simultaneous), restart and activation scenarios pass; a pre-feature enrolled secret signs in (probe-based test, FR-018); RFC 6238 vectors green against Otp.NET and otpauth; no hand-written RFC 6238 or Base32 left (SC-003); the sign-in fallback branch fails closed (5002, no subject) or goes through the guard; test helpers take every code through `NextUnused`; RFC 6238 Appendix B vectors pass on the .NET and web sides; ADR 0042 written. |
| G2 | P2: problem documents and trace context | Contract tests for 400, 401, 403, 404, 409, 500, 503 (media type, members, headers); OpenAPI snapshot and `api-types.ts` regenerated with the AGENTS.md command; web e2e for inline 400, the 409 Toast, 401, 403, 404 (success, 400, 409, bad CSRF, 401); one trace id searched across web, API, Identity logs and the audit entry; old audit entries still listable; no `X-Correlation-ID` code left (SC-007); `docs/api/problems.md`; ADR 0044; ADR 0018 and 0041 marked superseded in part. |
| G3 | P3: retry and limiter | 013 `api-policy` and Insights tests green with ky and p-limit underneath; new `Retry-After` tests with fake timers; attempt counts per method and status; `request-limiter.server.ts` and the hand-written helpers gone; ADR 0045; the 013 spec FR-027 pointer present. |
| G4a to G4d | P4: each item on its own | Per item: either a swap with the existing area suites unchanged, or an ADR naming the standard; for RR-82 the spike's scratch-container evidence and no contact with a real database; for RR-83 the deliberate-violation evidence for each policy; ADRs 0043, 0046, 0047, 0048, each with a row in the "P4 outcomes" table in `docs/adr/README.md` (FR-050's one place). |

## Lessons from 013 built into the plan

- **Lock files**: the lock-file recipe and the locked-mode check are in every gate that touches a package (R-015). Stage P1 is the first to hit it (Otp.NET reaches Identity, the CLI and their test projects).
- **Mongo unreachable at build**: problem-details registration, the schema transformer and the exception handler are written to be silent at startup, and the gate's build step proves it.
- **Dependabot**: `.github/dependabot.yml` is never edited; new packages ride the weekly schedule.
- **013 amendment**: the FR-027 pointer is added to the 013 spec now (plan time), as the spec promised.

## Project Structure

### Documentation (this feature)

```text
specs/014-standards-first/
├── plan.md              # This file
├── research.md          # Phase 0: facts, probes, decisions
├── data-model.md        # Phase 1: used-code record, problem document, trace id
├── quickstart.md        # Phase 1: how to validate each story
├── contracts/
│   ├── problem-details.md
│   ├── trace-context.md
│   └── web-outbound-policy.md
├── checklists/requirements.md
└── tasks.md             # Phase 2 (/speckit-tasks, not created here)
```

### Source Code (repository root)

```text
src/
├── RoleRadar.Identity/        # P1: verifier + replay guard, migration, Otp.NET; P2: drop CorrelationMiddleware
├── RoleRadar.Cli/             # P1: demo totp via Otp.NET; P2: run id = trace id
├── RoleRadar.Api/             # P2: ProblemDetails, handler, OData filter, schema transformer; drop Envelopes/ and CorrelationMiddleware
├── RoleRadar.Application/     # P2: request context carries the trace id; P4: validators (spike)
├── RoleRadar.Observability/   # P2: trace id helper replaces CorrelationId, formatter reads Activity; P4: redaction, formatter
├── RoleRadar.Infrastructure/  # P4: backup (evaluation only, no change planned)
└── RoleRadar.Mcp.Stdio/       # P2: per-call id = trace id
web/
├── app/lib/                   # P2: trace-context.server.ts, mapApiError, observability.ts, request-context.server.ts, oidc.server.ts
│                              # P3: api-client.server.ts (ky), delete request-limiter.server.ts (p-limit)
└── tests/                     # unit, e2e (support/totp.ts via otpauth), tooling (RFC vectors)
tests/                         # existing Architecture, Unit, Contract, Integration projects only
contracts/                     # regenerated OpenAPI document and client types (P2)
docs/adr/                      # 0042 to 0048 as above; README index updated
docs/api/problems.md           # P2
scripts/                       # P4 outcome (ADR 0048): verify-action-pins.ps1 retired (zizmor with a token in CI); licence-audit.mjs kept
specs/013-operability/spec.md  # one-line FR-027 pointer (done at plan time)
```

**Structure Decision**: No new projects. The existing layers, hosts and test projects are edited in place; new code is limited to a small replay guard inside Identity, a trace-id helper in `RoleRadar.Observability`, and a web `trace-context.server.ts`.

## Complexity Tracking

No constitution violation to justify. Two things to watch, neither a violation: the web gains four runtime packages (two OpenTelemetry, ky, p-limit) in exchange for deleting the hand-written retry, limiter and correlation code, and the replay guard is the one hand-rolled survivor, temporary by ADR.
