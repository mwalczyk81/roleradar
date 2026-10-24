# Implementation Plan: Feature 004 Job-Search Tracker Import

**Branch**: `004-tracker-import` | **Date**: 2026-10-24 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/004-tracker-import/spec.md`

## Summary

Feature 004 implements a CLI-only importer (`roleradar tracker-import <bundle-dir>`) that loads historical job-search tracker bundles into RoleRadar's structured collections.
Key technical highlights:
- **Zero Persistent Writes in Preview**: Defaults to dry-run mode using an ephemeral scratch database (`roleradar_scratch_<guid>`) cloned from the target database and dropped in a `finally` block, ensuring identical handler/decorator execution, full per-row isolation, and zero persistent side-effects on target state.
- **Strict Ingestion Pipeline**: (0) seed compensation criteria (`comp_preferred_floor`, `comp_high_priority_bar`); (1) `company-aliases.csv`; (2) `watchlist-companies.csv`; (3) `postings.json` (auto-creating neutral companies, staging `applied` postings as `scored`); (4) `appendix-rows.csv` (non-duplicates before duplicates, criteria created on first use); (5) `applications.csv` (applications transitioning postings to `applied`, chronology events, standalone notes in `job-search`).
- **Date Rollback & Stated Notes**: Month-first dates infer their year from `--default-date` with automatic rollback to the preceding year if the date is after `--default-date`. Leading `~` and trailing parentheticals are parsed into ISO dates with notes preserved.
- **Audit Logging**: Tracker import writes emit `import_*` operation names with actor `cli-import-tracker`. Standalone notes reuse `import_create` (`ImportCreate`). Zero interactive operation names are emitted.
- **Privacy & Safety First**: Terminal output displays exclusively aggregate outcome counts and row numbers. Contact creation is strictly manual via `roleradar contact add` (zero auto-created contacts). The `--report <path>` option rejects any path inside a git working tree.
- **Zero New Dependencies**: Hand-rolled RFC 4180 CSV parser implemented in ~120 lines of first-party C#; no external CSV packages introduced.

## Technical Context

**Language/Version**: C# / .NET 10 (`<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`).  
**Primary Dependencies**: `MongoDB.Driver` (3.12.0), `System.CommandLine` (2.0.13), `Microsoft.Extensions.*` (10.0.13), `Geralt` (4.4.0).  
**Storage**: MongoDB 8.0 replica set with standard GUID representation and UTC timestamps.  
**Testing**: xUnit v3 (4.0.2), `Testcontainers.MongoDb` (4.16.0), `FsCheck` (3.4.0), `NetArchTest.Rules` (1.3.2), `coverlet.collector` (10.1.0).  
**Target Platform**: Linux (containerized `roleradar-cli`) and Windows 11 host.  
**Project Type**: CLI tool invoking Application command handlers in-process.  
**Performance Goals**: Import ~200 rows and verify within <5 seconds.  
**Constraints**: MCP tool ceiling stays at 12 (0 new MCP tools). Minimum 85% coverage across Domain and Application layers. Zero real/private data in repository fixtures.

## Constitution Check

*GATE: Verified before planning and re-checked after Phase 1 design.*

- **Principle I (Privacy First)**: **PASS**. All test fixtures and sample schemas use strictly fictional company names, job titles, salary amounts, and contacts. The external real-name denylist hook passes. `--report` refuses any path within a git work tree. Terminal output displays counts and row numbers only.
- **Principle II (Test-First Development)**: **PASS**. Tests run against isolated Testcontainers fixtures (`IsolatedContainerMongoFixture`), never touching local developer databases. Dev-stack guard is preserved. Test-first TDD methodology enforced.
- **Principle III (Test-Enforced Layering)**: **PASS**. Inward dependencies `Domain <- Application <- Infrastructure <- Cli`. CLI invokes `ICommandHandler` in-process. Every command handler registration is wrapped by `TransactionAndAuditCommandDecorator`.
- **Principle IV (Named Seams Only)**: **PASS**. Handlers implement `ICommandHandler<TCommand, TResponse>` returning typed `Result<T>`. No mediator libraries or generic repositories introduced. CSV parser is hand-rolled in first-party code (ADR 0014).
- **Principle V (Agent Safety & Auditability)**: **PASS**. All imported text is treated as untrusted, length-limited input. No new MCP tools added. Every write produces an append-only audit entry with `import_*` operations and actor `cli-import-tracker`. Zero hard deletes.
- **Principle VI (stdio Protocol Hygiene)**: **PASS**. MCP stdio host is untouched. CLI stdout displays counts; stderr handles logging.
- **Principle VII (Data Durability & Safe Recovery)**: **PASS**. Backup/restore round-trip test verifies all imported collections and audit logs restore cleanly.
- **Principle VIII (Recorded Decisions & Pinned Dependencies)**: **PASS**. Architectural decisions recorded as MADR ADRs (ADR 0013 and ADR 0014). Zero new unpinned dependencies.
- **Principle IX (Baseline Technical Conventions)**: **PASS**. .NET 10, UTC timestamps, `Guid.CreateVersion7()`, `owner_id` scoping on every record.

## Project Structure

### Documentation (this feature)

```text
specs/004-tracker-import/
├── plan.md              # This implementation plan
├── research.md          # Technical research & ADR summaries
├── data-model.md        # File schemas, DTOs, and mapping definitions
├── quickstart.md        # Verification and quickstart guide
├── contracts/
│   ├── tracker-import-cli.md        # CLI syntax, options, and output protocol
│   └── execution-report-schema.md   # Structure of external report file
└── checklists/
    └── requirements.md  # Specification quality checklist
```

### Architectural Decisions

- [`docs/adr/0013-tracker-import-dry-run-scratch-database.md`](../../docs/adr/0013-tracker-import-dry-run-scratch-database.md) (Ephemeral scratch database for dry-run preview)
- [`docs/adr/0014-csv-parsing-strategy.md`](../../docs/adr/0014-csv-parsing-strategy.md) (Hand-rolled RFC 4180 CSV parser vs. external library)

### Source Code Changes

```text
src/
├── RoleRadar.Domain/
│   ├── Audit/
│   │   └── AuditOperation.cs        # Add import_* operation constants and update IsValid allowlist
│   └── Tracking/
│       └── DateInferenceRule.cs     # Partial date rollback and approximation parsing
├── RoleRadar.Application/
│   └── Tracking/
│       └── Import/
│           ├── Commands/
│           │   ├── TrackerImportCommand.cs        # Main import command record
│           │   └── TrackerImportCommandHandler.cs # Pipeline orchestrator invoking existing handlers
│           ├── DTOs/
│           │   └── TrackerImportSummaryDto.cs     # Summary statistics and row error DTOs
│           └── Pipeline/
│               ├── AliasResolutionStep.cs
│               ├── WatchlistCompanyStep.cs
│               ├── PostingsJsonStep.cs
│               ├── AppendixRowsStep.cs
│               └── ApplicationsStep.cs
├── RoleRadar.Infrastructure/
│   ├── Csv/
│   │   └── Rfc4180CsvReader.cs                    # Hand-rolled RFC 4180 CSV streaming reader (kept in Infrastructure)
│   └── Mongo/
│       └── MongoScratchDatabaseManager.cs         # Scratch database creation ($merge cloning, index init, regex guard, orphan cleanup)
└── RoleRadar.Cli/
    └── Commands/
        └── TrackerImportCommand.cs                # Top-level 'tracker-import' command registration

tests/
├── RoleRadar.Domain.Tests/
│   ├── Tracking/
│   │   └── DateInferenceRuleTests.cs              # Rollback, impossible dates, ~ and parentheticals
│   └── Audit/
│       └── AuditOperationTests.cs                 # Allowlist and import_* operation verification
├── RoleRadar.Application.Tests/
│   └── Tracking/
│       └── TrackerImportPipelineTests.cs          # Pipeline step sequencing, staging, and criteria seeding
├── RoleRadar.Infrastructure.Tests/
│   ├── Csv/
│   │   └── Rfc4180CsvReaderTests.cs               # RFC 4180 compliance, UTF-8 BOM, CRLF, LF, quoted newlines, embedded quotes, ragged rows, line-number tracking
│   ├── Mongo/
│   │   └── MongoScratchDatabaseManagerTests.cs    # $merge cloning, index init, orphan cleanup (>1h old), regex guard, failed dry-run target invariance
│   └── Architecture/
│       └── LayeringAndAppendOnlyArchitectureTests.cs # Verify Domain has 0 CSV types; all writes produce allowed audit ops
└── RoleRadar.Cli.Tests/
    ├── TrackerImportCliTests.cs                   # Dry-run parity, idempotency, dedup enforcement, failed/killed invariance, work-tree report refusal
    ├── Snapshot/
    │   └── CliHelpSnapshotTests.cs                # Verify tracker-import registered in root CLI help
    └── Integration/
        └── TrackerImportContainerTests.cs         # Container test against read-only bundle mount
```

## Phase Overview

- **Phase 0: Research & Foundation (Complete)**
  - ADR 0013 accepted (Scratch database with $merge cloning, index initialization, strict regex guard, orphan cleanup).
  - ADR 0014 accepted (Hand-rolled RFC 4180 parser in Infrastructure, keeping Domain pure).
  - Research completed on partial date rollbacks, container mounts, and test fixtures.
- **Phase 1: Design & Contracts (Complete)**
  - `data-model.md`, `contracts/`, and `quickstart.md` created.
- **Phase 2: Implementation (Follows in `/speckit-tasks`)**
  - Task 1: Infrastructure RFC 4180 CSV parser (UTF-8 BOM, CRLF/LF, quoted newlines, embedded quotes, ragged rows, line tracking) and Domain date inference rule with comprehensive unit tests.
  - Task 2: Domain AuditOperation additions & architecture test updates (verifying Domain has zero file-format/CSV code).
  - Task 3: Infrastructure scratch database manager (per-collection `$merge` cloning, host index initialization, strict name check `^roleradar_scratch_[0-9a-f]{32}$`, orphan cleanup for >1h old databases, and target invariance tests).
  - Task 4: Application tracker import command handler, pipeline steps, unclaimed `applied` role handling, and criteria seeding.
  - Task 5: Application execution report generator with git work-tree guard.
  - Task 6: CLI top-level `tracker-import` command and help snapshot tests.
  - Task 7: Integration test suite: fictional bundle dry-run equals apply, dedup enforcement during dry-run, idempotent re-run against already-imported DB, failed/killed dry-run target invariance, backup/restore, verify, and container read-only execution.
  - Task 8: Coverage (Domain+Application >=85%) and denylist privacy verification.
