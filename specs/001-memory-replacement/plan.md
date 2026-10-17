# Implementation Plan: Memory Replacement

**Branch**: `001-memory-replacement` | **Date**: 2026-10-17 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/001-memory-replacement/spec.md` (aligned with **Constitution v1.1.0**)

## Summary

Feature 001 replaces flat, size-capped (~48 KB) AI agent memory files with a local-first, durable, searchable note store accessed via a `stdio` Model Context Protocol (MCP) server (`RoleRadar.Mcp.Stdio`) and a companion CLI (`RoleRadar.Cli`).

Technically, the solution is built on **.NET 10 / C#** (`global.json` pinned to SDK `10.0.402` with `rollForward: "latestPatch"`) using strict inward layering (`Domain` → `Application` → `Infrastructure` → `Mcp.Stdio` / `Cli`). Per **Constitution v1.1.0**:
- Persistence uses a single aggregate-level repository port, `INoteRepository` ([ADR-0002](../../docs/adr/0002-inote-repository-single-write-port.md), [contracts/note-repository-port.md](./contracts/note-repository-port.md)), backed by a **single-node MongoDB 8.0 replica set (`rs0`, image `mongo:8.0.34`)** in `deploy/docker-compose.yml` ([ADR-0001](../../docs/adr/0001-mongo-only-for-feature-001.md)). No generic `IRepository<T>` is introduced.
- Every application use case is implemented as a plain hand-written handler (`CreateNote`, `AppendToNote`, `ReplaceNoteSection`, `ArchiveNote`, `ImportMarkdown` commands; `GetNote`, `SearchNotes` queries) injected directly into the MCP and CLI adapters without a mediator library ([ADR-0006](../../docs/adr/0006-handlers-with-decorators-no-mediator.md)).
- `TransactionAndAuditCommandDecorator<TCommand, TResponse>` wraps every command handler registration (applying input validation, opening the multi-document ACID transaction across `notes`, `note_revisions`, and `audit_log`, and recording the append-only audit entry), while `LoggingDecorator` wraps both commands and queries. An automated test in `RoleRadar.Tests.Architecture` proves every `ICommandHandler<,>` registration is decorated.
- Expected failures (`NotFound`, `StaleVersionToken`, `SectionNotFound`, `AmbiguousHeading`, `ImportConflict`, `InvalidUtf8`, `DatastoreOffline`) are returned as typed `Result<T>` values, never thrown as exceptions.
- Full prior-body `NoteRevision` snapshots are stored only on text-destroying writes (`ReplaceNoteSection` and `ImportMarkdown` overwrite), whereas `AppendToNote` executes atomically server-side without a version token and records only `appended_text` and `resulting_version_token` in `audit_log` ([ADR-0005](../../docs/adr/0005-revision-history-policy.md)).
- Streamed backups pipe `mongodump --archive --gzip` directly through libsodium `crypto_secretstream_xchacha20poly1305` (`Geralt` `4.4.0` `IncrementalXChaCha20Poly1305` + `Argon2id`, with header authenticated as associated data and `ChunkFlag.Final` on the terminal chunk), and restore uses a **two-pass protocol** (Pass 1 authenticates and decrypts the entire stream with output discarded; Pass 2 decrypts into `mongorestore` only after Pass 1 succeeds) ([ADR-0007](../../docs/adr/0007-backup-streaming-aead-encryption.md)).

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`, SDK `10.0.402` via `global.json` with `rollForward: "latestPatch"`, `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, Central Package Management with lock files `RestorePackagesWithLockFile=true` and `NuGetAudit` warnings treated as errors).

**Primary Dependencies (Live-Verified on NuGet.org on 2026-10-17 — [ADR-0004](../../docs/adr/0004-verified-mcp-sdk-usage.md))**:
- `ModelContextProtocol` `2.2.0`
- `Microsoft.Extensions.Hosting` `10.0.13`
- `MongoDB.Driver` `3.12.0`
- `System.CommandLine` `2.0.13` (stable 2.0.x GA release)
- `Geralt` `4.4.0` (MIT-licensed .NET 8+ libsodium wrapper for `IncrementalXChaCha20Poly1305` and `Argon2id`, backed by `libsodium` `1.0.22`)

**Storage**: MongoDB `mongo:8.0.34` (latest `8.0.x` patch tag verified on Docker Hub) running as a single-node replica set (`rs0`) in `deploy/docker-compose.yml` with named persistent volume `roleradar-mongo-data`, health check, `restart: unless-stopped`, bound exclusively to `127.0.0.1:27017`. Collections: `notes`, `note_revisions`, `audit_log`. `GuidRepresentation.Standard` configured globally once; IDs and version tokens generated with `Guid.CreateVersion7()`.

**Testing (Live-Verified on NuGet.org on 2026-10-17)**:
- `xunit.v3` `4.0.2` + `xunit.runner.visualstudio` `4.0.1` + `Microsoft.NET.Test.Sdk` `18.11.0`
- `Testcontainers.MongoDb` `4.16.0` (ephemeral `mongo:8.0.34` replica-set containers via `WithReplicaSet("rs0")`; never touches developer's local container)
- `NetArchTest.Rules` `1.3.2` (inward dependency rules + command handler decorator registration verification + reflection check forbidding update/delete methods on `note_revisions` and `audit_log`)
- `coverlet.collector` `10.1.0` (**>=85% line coverage gate** across `RoleRadar.Domain` and `RoleRadar.Application`)

**Target Platform**: Windows 11 with PowerShell 7 (`pwsh`) as primary developer/operator environment; Ubuntu runner in GitHub Actions CI with Linux containers (`mongo:8.0.34`).

**Project Type**: Local `stdio` MCP server (`RoleRadar.Mcp.Stdio`) + CLI tool (`RoleRadar.Cli`) + layered class libraries (`RoleRadar.Domain`, `RoleRadar.Application`, `RoleRadar.Infrastructure`).

**Performance Goals**:
- Search across 5,000 fictional notes completes in **<200 ms at p95** (`SC-003`), verified by an automated benchmark test.
- Offline datastore readiness probe fails fast in **<5 seconds** (`SC-007`) returning typed `ErrorCode.DatastoreOffline` with the exact start command (`pwsh ./scripts/start-services.ps1`).
- Streamed backup encryption/decryption processes multi-megabyte archives in bounded memory (`<= 256 KiB` chunk buffers) without buffering the full archive.

**Constraints**:
- Stdio-only MCP transport; zero network listeners; all logging routed to `stderr` (`FR-020`, [ADR-0003](../../docs/adr/0003-stdio-first-mcp.md)).
- Agent-facing tool set capped at `<= 8` tools (6 tools defined: `note_create`, `note_get`, `note_append`, `note_replace_section`, `note_search`, `note_archive`).
- Backup passphrase accepted only via interactive prompt, environment variable, or `stdin` (never CLI arg); backup streams `mongodump` directly into `Geralt.IncrementalXChaCha20Poly1305` encryption with zero plaintext temp files; restore is strictly two-pass ([ADR-0007](../../docs/adr/0007-backup-streaming-aead-encryption.md)).
- Strictly fictional data in all repo files, tests, and fixtures (`FR-021`).

**Scale/Scope**: Single-owner local store (`owner_id` on every collection), individual note bodies >100 KB (up to >=1 MB), 5,000+ notes search benchmark.

## Constitution Check (v1.1.0)

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle (Constitution v1.1.0) | Pre-Research Check | Post-Design Re-Check | Evidence / Enforcement |
|---------------------------------|--------------------|----------------------|------------------------|
| **I. Privacy First (NON-NEGOTIABLE)** | PASS | PASS | All fixtures (`tests/fixtures/fictional-memory-*.md`) and tests use strictly fictional data. `.gitignore`, pre-commit hook (`scripts/pre-commit.ps1` with `gitleaks`, path/pattern guard, and external denylist check at `%LOCALAPPDATA%\roleradar\private\denylist.txt`), and GitHub Actions CI (`gitleaks` + path guard without needing the private denylist) are enforced. Backup writes zero plaintext temp files (verified by temp-dir inspection test). |
| **II. Test-First Development (NON-NEGOTIABLE)** | PASS | PASS | Test projects (`Unit`, `Contract`, `Integration`, `Architecture`) use `xunit.v3` `4.0.2` and `Testcontainers.MongoDb` `4.16.0` with replica set; dedicated `INoteRepository` contract test suite ([contracts/note-repository-port.md](./contracts/note-repository-port.md)) includes fault-injection rollback test; >=85% line coverage gate enforced on `Domain` and `Application` in CI. |
| **III. Test-Enforced Layering** | PASS | PASS | `Domain` has zero external dependencies; `Application` depends only on `Domain`; `Infrastructure` implements `INoteRepository` using `MongoDB.Driver`; `Mcp.Stdio` and `Cli` invoke `ICommandHandler`/`IQueryHandler` directly in-process. `RoleRadar.Tests.Architecture` verifies both one-way inward dependencies **and** that every `ICommandHandler<,>` registration is wrapped by `TransactionAndAuditCommandDecorator<,>`. |
| **IV. Named Seams Only** | PASS | PASS | `INoteRepository` is the sole aggregate-level storage port ([ADR-0002](../../docs/adr/0002-inote-repository-single-write-port.md)); no generic `IRepository<T>` exists. Plain hand-written handlers (`CreateNote`, `AppendToNote`, `ReplaceNoteSection`, `ArchiveNote`, `ImportMarkdown`, `GetNote`, `SearchNotes`) + explicit decorators are used without any mediator library ([ADR-0006](../../docs/adr/0006-handlers-with-decorators-no-mediator.md)). Expected failures return typed `Result<T>` values, never exceptions. |
| **V. Agent Safety & Auditability** | PASS | PASS | 6 narrow MCP tools (<=8 cap), no hard-delete tool (`note_archive` only), append-only `audit_log` and `note_revisions` verified by architecture/reflection test and transactional write decorator. |
| **VI. stdio Protocol Hygiene** | PASS | PASS | `RoleRadar.Mcp.Stdio` routes all logs to `stderr`, opens zero network listeners, and is verified by an automated stdout-hygiene test ([ADR-0003](../../docs/adr/0003-stdio-first-mcp.md)). |
| **VII. Data Durability & Safe Recovery** | PASS | PASS | `deploy/docker-compose.yml` uses named volume `roleradar-mongo-data`; `wipe` is isolated in `scripts/wipe-data.ps1` / `roleradar wipe` requiring explicit confirmation; backups stream directly into `Geralt` `IncrementalXChaCha20Poly1305` + `Argon2id` encryption with two-pass restore ([ADR-0007](../../docs/adr/0007-backup-streaming-aead-encryption.md)). |
| **VIII. Recorded Decisions & Pinned Dependencies** | PASS | PASS | ADRs `0001`–`0007` recorded under `docs/adr/` with index; all NuGet versions live-verified and pinned in `Directory.Packages.props` with lock files (`packages.lock.json`) and `NuGetAudit` as errors; Docker image pinned to `mongo:8.0.34`; GitHub Actions pinned by commit SHA; Dependabot configured for `nuget`, `docker`, and `github-actions`. |
| **IX. Baseline Technical Conventions** | PASS | PASS | `.NET 10` (`global.json` `10.0.402`), `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, UTC timestamps, `Guid.CreateVersion7()`, global `GuidRepresentation.Standard`, and `owner_id` on `notes`, `note_revisions`, and `audit_log`. |

**Gate Evaluation**: **PASS** (Zero violations).

## Project Structure

### Documentation (this feature)

```text
specs/001-memory-replacement/
├── plan.md                          # This file (/speckit-plan command output)
├── research.md                      # Phase 0 research and live version/crypto evaluations
├── data-model.md                    # Phase 1 MongoDB collections, schemas, and indexes
├── quickstart.md                    # Phase 1 runnable validation guide
├── contracts/
│   ├── note-repository-port.md      # INoteRepository, ICommandHandler/IQueryHandler, Decorator, and Result<T> contract
│   ├── mcp-tools.md                 # Stdio MCP tools contract (6 tools, <=8 cap)
│   └── cli-commands.md              # RoleRadar.Cli and PowerShell scripts contract
└── tasks.md                         # Phase 2 output (/speckit-tasks command)

docs/
├── repo-settings.md                 # GitHub ruleset, security, and branch protection checklist
└── adr/
    ├── README.md                    # ADR index
    ├── 0001-mongo-only-for-feature-001.md
    ├── 0002-inote-repository-single-write-port.md
    ├── 0003-stdio-first-mcp.md
    ├── 0004-verified-mcp-sdk-usage.md
    ├── 0005-revision-history-policy.md
    ├── 0006-handlers-with-decorators-no-mediator.md
    └── 0007-backup-streaming-aead-encryption.md
```

### Source Code (repository root)

```text
RoleRadar.sln
global.json                          # Pins .NET SDK 10.0.402 (rollForward: latestPatch)
Directory.Build.props                # net10.0, Nullable=enable, TreatWarningsAsErrors=true, RestorePackagesWithLockFile=true, NuGetAudit
Directory.Packages.props             # Central Package Management with live-verified pinned versions
.editorconfig                        # C# formatting and analyzer rules enforced in build
.gitattributes                       # * text=auto eol=lf, *.ps1 text eol=crlf, tests/**/fixtures/** -text
.gitleaks.toml                       # Secret and pattern scanning rules
README.md                            # Quickstart (<10m to first stored note) + Mermaid architecture diagram
SECURITY.md                          # Vulnerability reporting policy
CONTRIBUTING.md                      # Conventional Commits, TDD workflow, privacy guardrails
CODEOWNERS                           # Repository ownership definitions

deploy/
└── docker-compose.yml               # mongo:8.0.34 single-node replica set (rs0), 127.0.0.1:27017, named volume roleradar-mongo-data

.github/
├── dependabot.yml                   # nuget, docker, github-actions with grouped minor/patch updates
├── PULL_REQUEST_TEMPLATE.md
├── ISSUE_TEMPLATE/
│   ├── bug_report.yml
│   └── feature_request.yml
└── workflows/
    ├── ci.yml                       # SHA-pinned actions, locked restore, format check, build, Testcontainers tests, 85% coverage, gitleaks
    ├── codeql.yml                   # SHA-pinned CodeQL analysis for C#
    └── dependency-review.yml        # SHA-pinned dependency review on pull requests

scripts/
├── start-services.ps1               # Starts deploy/docker-compose.yml mongo replica set and waits for readiness
├── backup.ps1                       # Wrapper for streamed encrypted backup
├── restore.ps1                      # Wrapper for two-pass encrypted restore
├── wipe-data.ps1                    # Confirmation-guarded destructive wipe script
└── pre-commit.ps1                   # Local pre-commit guard (gitleaks + path rules + external denylist check)

src/
├── RoleRadar.Domain/
│   ├── Common/                      # Entity base, OwnerId, UtcClock, Sha256Hasher
│   ├── Notes/                       # Note, NoteRevision, ProvenanceMarker, NoteImportMetadata, NoteStatus, MarkdownSectionParser
│   └── Audit/                       # AuditLogEntry, AuditContentSummary, AuditOperation
├── RoleRadar.Application/
│   ├── Abstractions/                # INoteRepository, INoteTransactionContext, ICommandHandler, IQueryHandler, Result<T>, ErrorCode
│   ├── Decorators/                  # TransactionAndAuditCommandDecorator, LoggingCommandDecorator, LoggingQueryDecorator
│   ├── Notes/
│   │   ├── Commands/                # CreateNote, AppendToNote, ReplaceNoteSection, ArchiveNote commands + handlers
│   │   └── Queries/                 # GetNote, SearchNotes queries + handlers
│   ├── Import/
│   │   └── Commands/                # ImportMarkdownCommand + ImportMarkdownCommandHandler
│   └── DependencyInjection.cs       # Registers handlers and wraps all ICommandHandler<,> with TransactionAndAuditCommandDecorator + LoggingDecorator
├── RoleRadar.Infrastructure/
│   ├── Mongo/                       # MongoNoteRepository, MongoOptions, GuidSerializer bootstrap, index initializer, readiness probe
│   └── Backup/                      # SecretStreamBackupArchiveService (Geralt IncrementalXChaCha20Poly1305 + Argon2id + Two-Pass Restore)
├── RoleRadar.Mcp.Stdio/
│   ├── Program.cs                   # Stdio host bootstrap (stderr logging only)
│   └── Tools/
│       └── NoteMcpTools.cs          # 6 MCP tools injecting ICommandHandler / IQueryHandler directly
└── RoleRadar.Cli/
    ├── Program.cs                   # System.CommandLine 2.0.13 root and exit code handling
    └── Commands/                    # ImportCommand, BackupCommand, RestoreCommand, WipeCommand, StatusCommand

tests/
├── fixtures/                        # Marked -text in .gitattributes to preserve exact CRLF and UTF-8 BOM bytes
│   ├── fictional-memory-primary.md  # ~46 KB fictional markdown memory file with [stated/researched/inferred] tags
│   └── fictional-memory-archive.md  # ~26 KB fictional markdown memory file
├── RoleRadar.Tests.Unit/            # Domain & Application unit tests + Geralt secretstream KAT, wrong passphrase, bit flip, truncation, dropped/reordered chunk, and bounded-memory stream tests
├── RoleRadar.Tests.Architecture/    # NetArchTest layering rules + DI decorator verification test + reflection check forbidding update/delete on revisions and audit_log
├── RoleRadar.Tests.Contract/        # INoteRepository contract tests + fault-injection transaction rollback + typed Result<T> tests (Testcontainers replica set)
└── RoleRadar.Tests.Integration/     # End-to-end import idempotence/conflict/orphan tests, 5,000-note <200ms p95 search benchmark, corrupted-restore-leaves-db-untouched test, temp-dir inspection test, and MCP stdout-hygiene test
```

**Structure Decision**: 5-project `.NET 10` solution under `src/` (`Domain`, `Application`, `Infrastructure`, `Mcp.Stdio`, `Cli`), 4 test projects under `tests/` (`Unit`, `Architecture`, `Contract`, `Integration`), container deployment files under `deploy/`, PowerShell scripts under `scripts/`, and MADR architecture decision records under `docs/adr/`.

## Complexity Tracking

> No Constitution Check violations exist. All architectural decisions are recorded in `docs/adr/0001`–`0007`.
