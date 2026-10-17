# Implementation Plan: Memory Replacement

**Branch**: `001-memory-replacement` | **Date**: 2026-10-17 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/001-memory-replacement/spec.md`

## Summary

Feature 001 replaces flat, size-capped (~48 KB) AI agent memory files with a local-first, durable, searchable note store accessed via a `stdio` Model Context Protocol (MCP) server (`RoleRadar.Mcp.Stdio`) and a companion CLI (`RoleRadar.Cli`).

Technically, the solution is built on **.NET 10 / C#** with strict inward layering (`Domain` → `Application` → `Infrastructure` → `Mcp.Stdio` / `Cli`) and a single persistence seam, `INoteStore` ([ADR-0002](../../docs/adr/0002-inote-store-single-write-port.md)). Persistence in Feature 001 uses a **single-node MongoDB 8.0 replica set (`rs0`)** ([ADR-0001](../../docs/adr/0001-mongo-only-for-feature-001.md)) holding `notes`, `note_revisions`, and `audit_log` so every write (`create`, `append`, `replace-section`, `archive`, and `import`) commits across all three collections inside a single multi-document ACID transaction. Full prior-body revision snapshots are stored only on destructive edits (`replace-section` and `import-overwrite`), whereas `append` executes atomically server-side without a version token and records only the appended text and resulting version token in `audit_log` ([ADR-0005](../../docs/adr/0005-revision-history-policy.md)). The CLI provides byte-exact idempotent markdown import (`#` and `##` heading splits with inline provenance extraction), streamed compressed AES-256-GCM backups (`mongodump --archive --gzip` piped directly into encryption with zero plaintext temp files), safe restore, and confirmation-guarded wipe, wrapped by PowerShell scripts for Windows.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`, `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, Central Package Management via `Directory.Packages.props`).

**Primary Dependencies**:
- `ModelContextProtocol` `2.2.0` ([ADR-0004](../../docs/adr/0004-verified-mcp-sdk-usage.md))
- `Microsoft.Extensions.Hosting` `10.0.0`
- `MongoDB.Driver` `3.1.0`
- `System.CommandLine` `2.0.0-beta4.22272.1`

**Storage**: MongoDB `mongo:8.0.4` (pinned image tag) running as a single-node replica set (`rs0`) via Docker Compose with named persistent volume `roleradar-mongo-data`, health check, `restart: unless-stopped`, bound exclusively to `127.0.0.1:27017`. Collections: `notes`, `note_revisions`, `audit_log`. `GuidRepresentation.Standard` configured globally once; IDs and version tokens generated with `Guid.CreateVersion7()`.

**Testing**:
- `xunit` `2.9.3` + `xunit.runner.visualstudio` `3.0.2` + `Microsoft.NET.Test.Sdk` `17.13.0`
- `Testcontainers.MongoDb` `4.3.0` (ephemeral replica-set containers for contract/integration/benchmark tests; never touches developer's local container)
- `NetArchTest.Rules` `1.3.2` (inward dependency rules + reflection verification that no update/delete methods exist on `note_revisions` or `audit_log`)
- `coverlet.collector` `6.0.4` (**>=85% line coverage gate** across `RoleRadar.Domain` and `RoleRadar.Application`)

**Target Platform**: Windows 11 with PowerShell 7 (`pwsh`) as primary host environment; Linux container for MongoDB (`mongo:8.0.4`).

**Project Type**: Local `stdio` MCP server (`RoleRadar.Mcp.Stdio`) + CLI tool (`RoleRadar.Cli`) + layered class libraries (`RoleRadar.Domain`, `RoleRadar.Application`, `RoleRadar.Infrastructure`).

**Performance Goals**:
- Search across 5,000 fictional notes completes in **<200 ms at p95** (`SC-003`), verified by an automated benchmark test.
- Offline datastore readiness probe fails fast in **<5 seconds** (`SC-007`) with the exact start command (`pwsh ./scripts/start-services.ps1`).

**Constraints**:
- Stdio-only MCP transport; zero network listeners; all logging routed to `stderr` (`FR-020`, [ADR-0003](../../docs/adr/0003-stdio-first-mcp.md)).
- Agent-facing tool set capped at `<= 8` tools (6 tools defined: `note_create`, `note_get`, `note_append`, `note_replace_section`, `note_search`, `note_archive`).
- Backup passphrase accepted only via interactive prompt, environment variable, or `stdin` (never CLI arg); backup streams `mongodump` directly into encryption with zero plaintext temp files written to disk (`FR-014`).
- Strictly fictional data in all repo files, tests, and fixtures (`FR-021`).

**Scale/Scope**: Single-owner local store (`owner_id` on every collection), individual note bodies >100 KB (up to >=1 MB), 5,000+ notes search benchmark.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Pre-Research Check | Post-Design Re-Check | Evidence / Enforcement |
|-----------|--------------------|----------------------|------------------------|
| **I. Privacy First (NON-NEGOTIABLE)** | PASS | PASS | All fixtures (`tests/fixtures/fictional-memory-*.md`) and tests use strictly fictional data. `.gitignore`, pre-commit hook (`scripts/pre-commit.ps1` with `gitleaks`, path/pattern guard, and external denylist check at `%LOCALAPPDATA%\roleradar\private\denylist.txt`), and GitHub Actions CI (`gitleaks` + path guard without needing the private denylist) are included in the plan. |
| **II. Test-First Development (NON-NEGOTIABLE)** | PASS | PASS | Test projects (`Unit`, `Contract`, `Integration`, `Architecture`) use `Testcontainers.MongoDb` with replica set; dedicated `INoteStore` contract test suite ([contracts/note-store-port.md](./contracts/note-store-port.md)) includes fault-injection rollback test; >=85% line coverage gate enforced on `Domain` and `Application` in CI. |
| **III. Test-Enforced Layering** | PASS | PASS | `Domain` has zero external dependencies; `Application` depends only on `Domain`; `Infrastructure` implements `INoteStore` using `MongoDB.Driver`; `Mcp.Stdio` and `Cli` invoke `Application` services in-process. Verified by `RoleRadar.Tests.Architecture`. |
| **IV. Named Seams Only** | PASS | PASS | `INoteStore` is the sole storage port in Feature 001 ([ADR-0002](../../docs/adr/0002-inote-store-single-write-port.md)). No generic repositories, mediator libraries, or plugin frameworks are introduced. |
| **V. Agent Safety & Auditability** | PASS | PASS | 6 narrow MCP tools (<=8 cap), no hard-delete tool (`note_archive` only), append-only `audit_log` and `note_revisions` verified by architecture/reflection test and transactional write unit. |
| **VI. stdio Protocol Hygiene** | PASS | PASS | `RoleRadar.Mcp.Stdio` routes all logs to `stderr`, opens zero network listeners, and is verified by an automated stdout-hygiene test ([ADR-0003](../../docs/adr/0003-stdio-first-mcp.md)). |
| **VII. Data Durability & Safe Recovery** | PASS | PASS | `docker-compose.yml` uses named volume `roleradar-mongo-data`; `wipe` is isolated in `scripts/wipe-data.ps1` / `roleradar wipe` requiring explicit confirmation; backups stream directly into AES-256-GCM encryption. |
| **VIII. Recorded Decisions & Pinned Dependencies** | PASS | PASS | ADRs `0001`–`0005` recorded under `docs/adr/`; all NuGet versions pinned in `Directory.Packages.props`; Docker image pinned to `mongo:8.0.4`; Dependabot configured for `nuget`, `docker`, and `github-actions`. |
| **IX. Baseline Technical Conventions** | PASS | PASS | `.NET 10`, `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, UTC timestamps, `Guid.CreateVersion7()`, global `GuidRepresentation.Standard`, and `owner_id` on `notes`, `note_revisions`, and `audit_log`. |

**Gate Evaluation**: **PASS** (Zero violations).

## Project Structure

### Documentation (this feature)

```text
specs/001-memory-replacement/
├── plan.md                          # This file (/speckit-plan command output)
├── research.md                      # Phase 0 research and decisions
├── data-model.md                    # Phase 1 MongoDB collections, schemas, and indexes
├── quickstart.md                    # Phase 1 runnable validation guide
├── contracts/
│   ├── note-store-port.md           # INoteStore C# transactional port contract
│   ├── mcp-tools.md                 # Stdio MCP tools contract (6 tools, <=8 cap)
│   └── cli-commands.md              # RoleRadar.Cli and PowerShell scripts contract
└── tasks.md                         # Phase 2 output (/speckit-tasks command)

docs/adr/
├── 0001-mongo-only-for-feature-001.md
├── 0002-inote-store-single-write-port.md
├── 0003-stdio-first-mcp.md
├── 0004-verified-mcp-sdk-usage.md
└── 0005-revision-history-policy.md
```

### Source Code (repository root)

```text
RoleRadar.sln
Directory.Build.props                # net10.0, Nullable=enable, TreatWarningsAsErrors=true, analyzers
Directory.Packages.props             # Central Package Management with pinned versions
.editorconfig                        # Formatting and analyzer rules for dotnet format
docker-compose.yml                   # mongo:8.0.4 single-node replica set (rs0), 127.0.0.1:27017, named volume
.gitleaks.toml                       # Secret and pattern scanning rules
.github/
├── dependabot.yml                   # nuget, docker, github-actions updates
└── workflows/
    └── ci.yml                       # gitleaks, path guard, dotnet format --verify-no-changes, build, test, 85% coverage gate

scripts/
├── start-services.ps1               # Starts docker compose mongo replica set and waits for readiness
├── backup.ps1                       # Wrapper for streamed encrypted backup
├── restore.ps1                      # Wrapper for encrypted restore
├── wipe-data.ps1                    # Confirmation-guarded destructive wipe script
└── pre-commit.ps1                   # Local pre-commit guard (gitleaks + path rules + external denylist check)

src/
├── RoleRadar.Domain/
│   ├── Common/                      # Entity base, OwnerId, UtcClock, Guard helpers
│   ├── Notes/                       # Note, NoteRevision, ProvenanceMarker, NoteImportMetadata, NoteStatus, MarkdownSectionParser
│   └── Audit/                       # AuditLogEntry, AuditContentSummary, AuditOperation
├── RoleRadar.Application/
│   ├── Abstractions/                # INoteStore and transactional store command/result records
│   ├── Notes/                       # NoteService (create, get, append, replace-section, search, archive)
│   └── Import/                      # MarkdownImportService (UTF-8 check, heading split, provenance parse, conflict/orphan reconciliation)
├── RoleRadar.Infrastructure/
│   ├── Mongo/                       # MongoNoteStore, MongoOptions, GuidSerializer bootstrap, index initializer, readiness probe
│   └── Backup/                      # StreamedAesGcmBackupService (mongodump/mongorestore streaming + PBKDF2/AesGcm)
├── RoleRadar.Mcp.Stdio/
│   ├── Program.cs                   # Stdio host bootstrap (stderr logging only)
│   └── Tools/
│       └── NoteMcpTools.cs          # 6 MCP tools (note_create, note_get, note_append, note_replace_section, note_search, note_archive)
└── RoleRadar.Cli/
    ├── Program.cs                   # System.CommandLine root and exit code handling
    └── Commands/                    # ImportCommand, BackupCommand, RestoreCommand, WipeCommand, StatusCommand

tests/
├── fixtures/
│   ├── fictional-memory-primary.md  # ~46 KB fictional markdown memory file with [stated/researched/inferred] tags
│   └── fictional-memory-archive.md  # ~26 KB fictional markdown memory file
├── RoleRadar.Tests.Unit/            # Domain & Application unit tests (markdown splitter, section replacer, provenance parser, backup crypto stream)
├── RoleRadar.Tests.Architecture/    # NetArchTest layering rules + reflection check forbidding update/delete on revisions and audit_log
├── RoleRadar.Tests.Contract/        # INoteStore contract tests + fault-injection transaction rollback tests (Testcontainers replica set)
└── RoleRadar.Tests.Integration/     # End-to-end import idempotence/conflict/orphan tests, 5,000-note <200ms p95 search benchmark, backup temp-dir inspection test, and MCP stdout-hygiene test
```

**Structure Decision**: Clean 5-project `.NET 10` solution under `src/` (`Domain`, `Application`, `Infrastructure`, `Mcp.Stdio`, `Cli`) paired with 4 focused test projects under `tests/` (`Unit`, `Architecture`, `Contract`, `Integration`), operational PowerShell scripts under `scripts/`, and MADR architecture decision records under `docs/adr/`.

## Complexity Tracking

> No Constitution Check violations exist. All architectural decisions are recorded in `docs/adr/0001`–`0005`.
