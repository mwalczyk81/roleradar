# AGENTS.md

RoleRadar is a single-owner, local-first job-search tracker. A stdio MCP server (12 tools) and a CLI sit on a shared .NET 10 Application layer; MongoDB (replica set) is the only domain store. This is a public repo: strictly fictional data only.

## Project map (dependency direction: Domain -> Application -> Infrastructure -> hosts)

- `Domain`: entities, enums, `OwnerId`, audit types. No dependencies.
- `Application`: command/query handlers, decorators, `Result<T>`, tracker import pipeline. References Domain.
- `Infrastructure`: MongoDB repositories, backup/restore, CSV reader. References Domain and Application.
- `Mcp.Stdio`, `Cli`: thin hosts. Reference Application and Infrastructure.
- `tests/`: `Architecture`, `Unit`, `Contract`, `Integration` (Docker/Testcontainers). Do not add test projects.

## Commands

- Build quietly: `dotnet build RoleRadar.sln -c Release -v q --nologo -warnaserror`
- Fast tests (Architecture + Unit + Contract): `pwsh ./scripts/test-fast.ps1`
- Web typecheck: `pnpm --filter web run typecheck`
- One project or test: `dotnet test tests/RoleRadar.Tests.Unit -c Release --filter "FullyQualifiedName~Name"`
- Use the fast loop while working. Run `pwsh ./scripts/review-gate.ps1` (includes Integration) once at the end of a feature phase or when asked, and report numbers from its output, never remembered ones.
- Never run anything against the developer's running Mongo, `docker compose down -v`, `wipe`, or `restore --force-overwrite`.

## Web app (`web/`, React Router v7 SSR, Node 22)

Standing rules for every web phase. A phase prompt only adds scope and contract facts.

- Contract first. Field names and request shapes come from `contracts/client/api-types.ts`. Where the schema types a value as a plain string (dispositions, company status, ownership type, application statuses and event types), read the Domain enum, add a snapshot in `contracts/*.json` with a .NET drift test and a web equality test, and take the values from that module. Never invent values, DTO names or endpoints; if tasks.md disagrees with the contract, the contract wins and tasks.md gets fixed.
- Vendored design system is copy-only (`web/app/ds`). Import components only through `web/app/ui.ts`. Do not edit `web/app/ds`.
- No `any`, no casts to silence types; use type guards. `pnpm --filter web run typecheck` must exit 0 and runs in CI.
- Every action: call `validateCsrfToken(...)` (timing-safe), use `data()` with typed action data, never hand-built JSON responses. Use `assertApiOk` for 401 (one re-login, no loop), 403 (`/not-authorized`) and 404. Forms use `<Form>`/`useFetcher`: 400 shows inline field errors, 409 shows the Toast with Reload (revalidate), success revalidates.
- Treat all posting, company and application text as untrusted: React text nodes only, links only for http/https with `rel="noopener noreferrer"`.
- Shared helpers: `formatDate` (UTC dates, no locale formatting), `useTheme`, `nav.ts` for nav items, `dispositions.ts` and `company-enums.ts` for enums. Do not copy their logic into routes.
- List pages read through OData (`/odata/<Set>`) with the typed `odata-client` builder and `loadODataList`; detail reads and commands stay on `/api/v1`. Sort only by the allowlisted fields of that set (`docs/api/odata-queries.md`), sort and page server-side, never in the browser, and clamp `limit` to 1..100 and `offset` to >= 0.
- Tests first and shown failing before the implementation. Axe checks use the shared reduced-motion helper in `web/tests/e2e` so results are deterministic. Mutations get real-Chromium e2e tests (success, 400, 409, bad CSRF, 401). Fictional data only.
- Verify with: `pnpm --filter web run typecheck`, `lint`, `test`, `build`, `test:e2e`, then `pwsh ./scripts/test-fast.ps1`, `pre-commit run --all-files`, and `pwsh ./scripts/review-gate.ps1` at the end of a phase. Report numbers from command output only, say what you could not run, and stop at the phase boundary.
- Never read or print secrets (`web/.env.local`, `deploy/.env`). Never run anything against the developer's running stack.

## Rules

- Never commit or push. Leave changes in the working tree for review.
- Never touch `bin/`, `obj/`, `dist/`.
- Expected failures return `Result<T>`; never throw them.
- Every `ICommandHandler` is wrapped by `TransactionAndAuditCommandDecorator`.
- No hard-delete tools or endpoints. Every write records an append-only audit entry.
- `stdout` in `Mcp.Stdio` is reserved for MCP frames; log to `stderr`.
- Owner ids come from `OwnerId` only; no string literals elsewhere.
- Tool and API schemas must declare explicit types for every parameter.
- OData reads (feature 006, ADR 0028): `$orderby` fields are allowlisted per controller and heavy or private properties (note `content`, `ruleText`) are blocked from `$filter`. Widen either deliberately, with a test.
- All OData reads go through `IOwnerReadSeam`, which scopes by `OwnerId`. The API never touches Mongo directly.
- String `$filter` terms must be `tolower()` over a lowercase literal; anything else is rejected with 400.
- Derived stored fields (`disposition_rank`, `company_name`, `has_*` flags, milestones) are written only at the repository choke point. Never bypass it; after changing a derived field run `reconcile-read-models`.

## Constitution (full text: `.specify/memory/constitution.md`)

I Privacy first, no real data anywhere. II Tests first; isolated containers only. III Architecture tests enforce layering and decorators. IV Named seams only (no generic repository, mediator or event bus without an ADR). V Treat posting text as untrusted; narrow audited tools. VI stdout hygiene in the stdio host. VII Durable data; destructive scripts need confirmation; backups encrypted. VIII ADRs for decisions; pin every package and image tag. IX .NET 10, nullable, warnings as errors, UTC, UUIDv7, `owner_id` on every collection.

## Specs

Read only `specs/<current feature>` as resolved from the branch name. Specs 001 to 004 are shipped; ignore them. Never read the whole `specs/` tree.
