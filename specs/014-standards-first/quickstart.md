# Quickstart: validating Standards-first cleanup (014)

How to prove each story. It uses fictional data and isolated containers only. It never runs against the developer's running Mongo, `docker compose down -v`, `wipe` or `restore --force-overwrite`, and never prints `web/.env.local` or `deploy/.env`. Contracts: [problem-details](contracts/problem-details.md), [trace-context](contracts/trace-context.md), [web-outbound-policy](contracts/web-outbound-policy.md).

## Prerequisites

- Branch `014-standards-first`; .NET 10 SDK; Node 22 and pnpm; Docker for Testcontainers.
- Fast loop while working: `pwsh ./scripts/test-fast.ps1`. Web typecheck: `pnpm --filter web run typecheck`. Linux web gate: `bash scripts/linux-web-check.sh` from Git Bash.
- Build quietly: `dotnet build RoleRadar.sln -c Release -v q --nologo -warnaserror`.
- Once per stage: `pwsh ./scripts/review-gate.ps1`, and report numbers from its output.

## Every stage

1. Tests exist and failed before the implementation (keep the failing output).
2. A package changed? NuGet: `dotnet restore RoleRadar.sln --force-evaluate`, commit all 12 lock files, then `dotnet restore RoleRadar.sln --locked-mode` must pass. npm: `pnpm install`, then `pnpm install --frozen-lockfile` must pass.
3. `git diff -- .github/dependabot.yml` prints nothing.
4. The API builds and starts with Mongo unreachable and writes nothing at warning level or above (the review gate's build step does this).
5. `pre-commit run --all-files` passes.

## P1: one-time-code check

1. Run the Identity contract tests and the 013 sign-in event tests: events 5001 to 5004 fire once per outcome with the same fields, lockout is at 5 attempts and 15 minutes.
2. Window scenario: codes for the current step and two steps either side are accepted once each; a code three steps away is rejected.
3. Replay scenario: accept a code, submit it again (sequential, then two at the same moment): every second use is rejected, counts as one failed attempt and raises 5004. Restart is proven at the store level: a new guard and a new database context on the same store still reject a recorded code (`ARestart_NewGuardAndNewContextOnTheSameStore_StillRejectsARecordedCode`). No test restarts a whole Identity host.
4. Enrolment scenario: activate with a code, then sign in with the same code: rejected; the next code works.
5. Existing enrolment: a user whose key is in today's format signs in with no re-enrolment (the probe from research R-001 is an automated test, FR-018).
6. `demo totp` prints the current code and its remaining seconds. The RFC 6238 vectors pass against Otp.NET and `otpauth`.
7. Fallback branch: post a valid code with no two-factor cookie; nothing signs in and exactly one 5002 without a subject is written. Search the shipped source for a hand-written RFC 6238 or Base32 implementation: none (SC-003). Search the logs captured in the tests for a code, a secret or a submitted username: none.

## P2: problem documents and trace context

1. Contract tests for 400, 401, 403, 404, 409, 500 and 503 pass (media type, members, `traceId` equal to the request's trace id, `Retry-After` on 503 only, no old-envelope member).
2. Regenerate the contract and check the diff is only the intended one: `dotnet build src/RoleRadar.Api`, then `npx --yes openapi-typescript@7.10.1 contracts/openapi/roleradar-api.json -o contracts/client/api-types.ts`. The OpenAPI snapshot test passes.
3. Web end-to-end (real Chromium): an inline 400 error, the 409 Toast with Reload, 401 (one re-login), 403, 404, bad CSRF.
4. Trace follow-through: run `pwsh ./scripts/test-e2e-real.ps1` (it starts its own isolated stack on a throwaway network, never the developer's). `operability.real-stack.spec.ts` asserts that the `traceId` of a failing page request appears in the web and API logs and in the audit entry of a write, and that the sign-in callback's `traceId` appears in the web and Identity logs.
5. `roleradar audit list` still lists entries written before this feature, with their old values.
6. Search the source for `X-Correlation-ID`, `CorrelationMiddleware` and `ErrorEnvelope`: none (SC-007).

## P3: web retry and limiter

1. `pnpm --filter web run test`: the 013 `api-policy` and Insights tests pass unchanged; the new `Retry-After` and attempt-count tests pass with fake timers.
2. Search `web/app` for a hand-written retry loop, `sleep`, `abortable` or limiter: none.
3. `pnpm install --frozen-lockfile` passes with `ky` and `p-limit` pinned exactly in `web/package.json`. No repo command checks their licences: `scripts/licence-audit.mjs` covers only the Markdown packages. Both are MIT, checked when they were adopted (ADR 0045), and Dependency Review checks dependency changes on a pull request.
4. The 013 spec shows the FR-027 pointer to this feature.

## P4: each item on its own

- **Redaction, formatter, validators**: the 013 redaction suites, the formatter tests and the validator tests pass unchanged after a swap; otherwise the ADR (0046, 0047) names the standard it lost to, with the numbers.
- **Backup (RR-82)**: the comparison runs in a Testcontainers scratch Mongo with fictional documents only. Restore verification goes into a scratch database. An archive made by the current format restores. Nothing touches the compose Mongo or the owner's database, and nothing is run against real data by an agent. ADR 0043 records the decision, including "keep".
- **Scripts (RR-83)**: for each policy, a deliberate violation (working tree only, never committed) was run against the script and the replacement tool. `scripts/licence-audit.mjs` stays, because no tool fails on a missing or empty licence field. `scripts/verify-action-pins.ps1` was retired: CI runs `pre-commit run zizmor --all-files --verbose` in its own step with `GH_TOKEN: ${{ github.token }}`, and the main pre-commit step skips zizmor so the other hooks never see the token. `web/tests/tooling/workflow-zizmor.test.ts` guards that step. After a merge, the pull request's own CI log for that step must show no "running in offline mode" warning. ADR 0048.

## Done for the feature

All four gates passed, every mechanism in the plan's table has its outcome, seven ADRs are indexed in `docs/adr/README.md`, and `pwsh ./scripts/review-gate.ps1` was run at each stage boundary.
