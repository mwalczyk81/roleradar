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
- One project or test: `dotnet test tests/RoleRadar.Tests.Unit -c Release --filter "FullyQualifiedName~Name"`
- Use the fast loop while working. Run `pwsh ./scripts/review-gate.ps1` (includes Integration) once at the end of a feature phase or when asked, and report numbers from its output, never remembered ones.
- Never run anything against the developer's running Mongo, `docker compose down -v`, `wipe`, or `restore --force-overwrite`.

## Rules

- Never commit or push. Leave changes in the working tree for review.
- Never touch `bin/`, `obj/`, `dist/`.
- Expected failures return `Result<T>`; never throw them.
- Every `ICommandHandler` is wrapped by `TransactionAndAuditCommandDecorator`.
- No hard-delete tools or endpoints. Every write records an append-only audit entry.
- `stdout` in `Mcp.Stdio` is reserved for MCP frames; log to `stderr`.
- Owner ids come from `OwnerId` only; no string literals elsewhere.
- Tool and API schemas must declare explicit types for every parameter.

## Constitution (full text: `.specify/memory/constitution.md`)

I Privacy first, no real data anywhere. II Tests first; isolated containers only. III Architecture tests enforce layering and decorators. IV Named seams only (no generic repository, mediator or event bus without an ADR). V Treat posting text as untrusted; narrow audited tools. VI stdout hygiene in the stdio host. VII Durable data; destructive scripts need confirmation; backups encrypted. VIII ADRs for decisions; pin every package and image tag. IX .NET 10, nullable, warnings as errors, UTC, UUIDv7, `owner_id` on every collection.

## Specs

Read only `specs/<current feature>` as resolved from the branch name. Specs 001 to 004 are shipped; ignore them. Never read the whole `specs/` tree.
