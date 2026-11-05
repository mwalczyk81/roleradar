# Quickstart: validating 008

Everything below uses fictional data and isolated containers. Never run against the developer's Mongo.

## Prerequisites

- Docker running (Integration tests use Testcontainers).
- `pnpm install` done for the web app.

## Per phase

| Phase | Commands | Expected |
| --- | --- | --- |
| 1 | `dotnet build RoleRadar.sln -c Release -v q --nologo -warnaserror` then `pwsh ./scripts/test-fast.ps1` | Green. New handler tests were shown failing first. |
| 2 | `dotnet test tests/RoleRadar.Tests.Integration -c Release --filter "FullyQualifiedName~PostingRejectionSync|FullyQualifiedName~ApiMcpParity"` | Atomicity, idempotency and host parity tests pass. |
| 3 | `pwsh ./scripts/test-fast.ps1`, `dotnet test tests/RoleRadar.Tests.Integration -c Release --filter "FullyQualifiedName~Insights"`, `pnpm --filter web run test` | Snapshot, drift and equality tests agree; Insights figures unchanged with note events. |
| 4 | `pnpm --filter web run typecheck`, `lint`, `test`, `build`, `test:e2e`; `bash scripts/linux-web-check.sh`; `pwsh ./scripts/review-gate.ps1` | All green; report numbers from output only. |

## Scenarios to see working (fictional seed)

1. Reject an applied posting (reason, no date): the application shows one rejected event dated today (UTC) with source `posting_transition`, outcome rejected, and the Insights rejection count rises by one.
2. Repeat the rejection path by appending a manual rejected event afterwards: no loop, no second posting-driven event.
3. Reject with `eventDate` set to an earlier valid date: the event carries that date. A malformed date returns 400 and nothing changes.
4. Application with outcome `closed` (withdrawn), posting rejected: transition succeeds, application unchanged, `application_closed` warning shown on the page and in the MCP JSON.
5. Reopen a rejected posting whose application is rejected: posting becomes lead, application stays rejected, `application_still_rejected` warning shown.
6. Add a note event to an old no-response application: still ghosted, Insights unchanged, application page shows "Note".
7. Posting with no application, and a `closed` transition: behave exactly as before.

## Rollback evidence

An integration test forces a failure on the application write and asserts the posting is unchanged and no audit entry exists.
