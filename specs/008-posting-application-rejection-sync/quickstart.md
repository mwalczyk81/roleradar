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
| 4 | `pnpm --filter web run typecheck`, `lint`, `test`, `build`, `test:e2e`; `bash scripts/linux-web-check.sh`; `pwsh ./scripts/review-gate.ps1 -IncludeE2E` (from Git Bash) | All green; report numbers from output only. The review gate's Integration step uses the local `roleradar-mcp:test` image: rebuild it (`docker build --target mcp -t roleradar-mcp:test .`) if the container MCP schema test reports a stale description. |

## Scenarios to see working (fictional seed)

1. Reject an applied posting that has an application (reason, date left at its default): the posting detail carries a non-null `applicationId`, the dialog says "This also records the rejection on the application." and offers "Event Date (Optional)" prefilled with today (UTC). After submit the application shows one rejected event dated today with the line "Source: posting_transition", outcome rejected, and the Insights rejection count rises by one. A posting with no application has `applicationId: null`; its dialog shows neither the sentence nor the date field.
2. Repeat the rejection path by appending a manual rejected event afterwards: no loop, no second posting-driven event.
3. Reject with `eventDate` set to an earlier valid date (the "Event Date (Optional)" field on the page, or the `eventDate` parameter on MCP and API): the event carries that date. A malformed date returns 400 with a field error named `eventDate`, shown inline on the date field, and nothing changes.
4. Application with outcome `closed` (withdrawn), posting rejected: transition succeeds, application unchanged, `application_closed` warning shown on the page (the API's message text in a warning banner above the posting summary, never chosen by code) and in the MCP JSON.
5. Reopen a rejected posting whose application is rejected: posting becomes lead (an override reason is required), application stays rejected, and the `application_still_rejected` warning message is shown on the page after the dialog closes and in the MCP JSON.
6. Add a note event to an old no-response application: still ghosted, Insights unchanged, application page shows "Note" and each event's source.
7. Posting with no application, and a `closed` transition: behave exactly as before.

## Rollback evidence

An integration test forces a failure on the application write and asserts the posting is unchanged and no audit entry exists.
