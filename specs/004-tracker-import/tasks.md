# Tasks: Feature 004 Job-Search Tracker Import

**Input**: Design documents from `specs/004-tracker-import/` (`plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/`, `quickstart.md`)  
**Prerequisites**: `plan.md` and `spec.md` approved; ADR 0013 and ADR 0014 accepted.  
**Tests**: MANDATORY per Constitution Principle II (Test-First Development). For every story, test tasks come first, are marked as expected to fail, and precede implementation tasks. Ephemeral Testcontainers fixtures (`IsolatedContainerMongoFixture`) are used exclusively; no tests ever connect to dev Mongo or import real data.

---

## Phase 1: Setup & Foundational Audit & Ingestion Primitives

**Purpose**: Establish audit operation allowlists, CSV parsing, date inference rules, and Domain purity before database work.

- [ ] T001 [P] Architecture tests for AuditOperation allowlist in `tests/RoleRadar.Tests.Architecture/LayeringAndAppendOnlyArchitectureTests.cs` (expected to fail before constants are added)
- [ ] T002 [P] Define `import_*` constants (`import_posting_create`, `import_posting_transition`, `import_posting_signal_append`, `import_company_create`, `import_company_update`, `import_application_create`, `import_application_event_append`, `import_criterion_revision`) and update `IsValid` in `src/RoleRadar.Domain/Audit/AuditOperation.cs`
- [ ] T003 [P] Unit tests for RFC 4180 CSV parsing in `tests/RoleRadar.Infrastructure.Tests/Csv/Rfc4180CsvReaderTests.cs` covering UTF-8 BOM, CRLF, LF, quoted newlines, embedded quotes `""`, ragged rows, and line-number tracking (expected to fail before implementation)
- [ ] T004 [P] Implement hand-rolled RFC 4180 streaming reader in `src/RoleRadar.Infrastructure/Csv/Rfc4180CsvReader.cs` keeping `RoleRadar.Domain` free of file-format code
- [ ] T005 [P] Unit tests for partial date rollback, impossible dates, `~` prefixes, and parentheticals in `tests/RoleRadar.Domain.Tests/Tracking/DateInferenceRuleTests.cs` (expected to fail before implementation)
- [ ] T006 [P] Implement date inference and rollback rules (`DateInferenceRule`) in `src/RoleRadar.Domain/Tracking/DateInferenceRule.cs`
- [ ] T007 Architecture test in `tests/RoleRadar.Tests.Architecture/LayeringAndAppendOnlyArchitectureTests.cs` verifying `RoleRadar.Domain` contains zero file-format or CSV types

---

## Phase 2: Foundational Scratch Database Guard & Isolation

**Purpose**: Guard routines and isolation manager ensuring target database is never dropped and scratch databases are safely scoped and cleaned.

**⚠️ CRITICAL**: T008 and T009 (scratch database guard and tests) MUST be implemented and pass before any code that can drop a database is written!

- [ ] T008 [P] Unit tests for scratch database name guard in `tests/RoleRadar.Infrastructure.Tests/Mongo/MongoScratchDatabaseGuardTests.cs` (expected to fail before implementation) asserting strict name regex `^roleradar_scratch_[0-9a-f]{32}$`, refusal of target database name, and refusal of databases created <1 hour ago
- [ ] T009 [P] Implement `MongoScratchDatabaseGuard` in `src/RoleRadar.Infrastructure/Mongo/MongoScratchDatabaseGuard.cs` with validation regex `^roleradar_scratch_[0-9a-f]{32}$`, target database equality check, and 1-hour age filter
- [ ] T010 Integration tests for scratch database lifecycle in `tests/RoleRadar.Infrastructure.Tests/Mongo/MongoScratchDatabaseManagerTests.cs` (expected to fail before implementation) verifying per-collection `$merge` cloning, host index initialization (`EnsureIndexesOnceAsync`), `finally`-block drop, orphan cleanup of >1h old databases, and target collection count and `audit_log` invariance on error or kill
- [ ] T011 Implement `MongoScratchDatabaseManager` in `src/RoleRadar.Infrastructure/Mongo/MongoScratchDatabaseManager.cs` implementing `$merge` collection cloning, index initialization, guarded drop, and startup orphan cleanup

---

## Phase 3: User Story 1 - Safe Ingestion & Execution Modes (Priority: P1) 🎯 MVP

**Goal**: Establish the `roleradar tracker-import` CLI command, dry-run preview default, mandatory `--default-date` check, per-row failure isolation, and idempotent re-run guarantees.

**Independent Test**: Run `roleradar tracker-import` against a fictional bundle without `--apply`; assert 0 persistent writes and 0 audit entries. Re-run with `--apply`; assert counts match dry-run. Re-run with `--apply` a second time; assert 0 created and idempotent no-op confirmation.

### Tests for User Story 1 (MANDATORY - Test-First per Principle II) ⚠️

- [ ] T012 [P] [US1] Integration tests in `tests/RoleRadar.Cli.Tests/TrackerImportCliTests.cs` for dry-run preview default, `--apply` requirement, `--default-date` missing error naming file and affected row count, per-row error isolation, and second `--apply` idempotent no-op (expected to fail before implementation)

### Implementation for User Story 1

- [ ] T013 [P] [US1] Create command and summary DTOs in `src/RoleRadar.Application/Tracking/Import/DTOs/TrackerImportSummaryDto.cs` and `src/RoleRadar.Application/Tracking/Import/Commands/TrackerImportCommand.cs`
- [ ] T014 [US1] Implement `TrackerImportCommandHandler` in `src/RoleRadar.Application/Tracking/Import/Commands/TrackerImportCommandHandler.cs` orchestrating the multi-stage pipeline across target or scratch databases with per-row failure isolation
- [ ] T015 [US1] Register top-level CLI command `roleradar tracker-import <bundle-directory> [--default-date <YYYY-MM-DD>] [--apply] [--report <path>]` in `src/RoleRadar.Cli/Commands/TrackerImportCommand.cs` and connect in `src/RoleRadar.Cli/Program.cs`

---

## Phase 4: User Story 2 - Companies, Watchlist Statuses, and Aliases (Priority: P1)

**Goal**: Ingest `watchlist-companies.csv` and optional `company-aliases.csv`; resolve canonical company names, set `watch`/`avoid` statuses, and auto-create unlisted companies with status `neutral` and blank description.

**Independent Test**: Import bundle with aliases and watchlist companies; verify canonical resolution, preservation of `watch`/`avoid` statuses, auto-creation of unlisted companies as `neutral` with blank description, and listing of auto-created companies in `--report`.

### Tests for User Story 2 (MANDATORY - Test-First per Principle II) ⚠️

- [ ] T016 [P] [US2] Unit and integration tests in `tests/RoleRadar.Application.Tests/Tracking/CompanyImportStepTests.cs` for exact alias matching via `company-aliases.csv`, watchlist `watch` and `avoid` status assignment, unlisted company auto-creation as `neutral` with blank description (never `watch`), and decision `skip` (expected to fail before implementation)

### Implementation for User Story 2

- [ ] T017 [P] [US2] Implement `AliasResolutionStep` in `src/RoleRadar.Application/Tracking/Import/Pipeline/AliasResolutionStep.cs` loading `company-aliases.csv` into an exact match dictionary
- [ ] T018 [US2] Implement `WatchlistCompanyStep` in `src/RoleRadar.Application/Tracking/Import/Pipeline/WatchlistCompanyStep.cs` processing `watchlist-companies.csv` via `RecordCompanyCommand`, appending description text ("max 300 characters"), respecting `decision: skip`, and replacing text with `corrected_notes` if non-empty

---

## Phase 5: User Story 3 - Postings and Appendix Rows Ingestion (Priority: P1)

**Goal**: Ingest `postings.json` and `appendix-rows.csv` through standard posting handlers; drop `tier`, map `compHigh`, map `cultureLevel` with `unknown` fallback, map `ghost` to `phantom`, stage `applied` postings as `scored`, process non-duplicates before duplicates, and resolve `duplicate_of_id`.

**Independent Test**: Import postings and appendix rows; verify Tier A/B/C/D deduplication, duplicate linking to target posting ID, `dismissed` mapping to `screened_out` (`resurface_policy: never`), and per-row rejection of forbidden state transitions.

### Tests for User Story 3 (MANDATORY - Test-First per Principle II) ⚠️

- [ ] T019 [P] [US3] Unit and integration tests in `tests/RoleRadar.Application.Tests/Tracking/PostingImportStepTests.cs` for dropping `tier`, mapping `compHigh` to `comp_high_priority`, mapping `cultureLevel` with fallback to `unknown`, mapping `ghost: true` to `SignalType.Phantom`, staging status `applied` as `scored`, non-duplicates before duplicates in `appendix-rows.csv`, linking `duplicate_of_id`, and per-row error isolation on invalid transitions (expected to fail before implementation)

### Implementation for User Story 3

- [ ] T020 [P] [US3] Implement `PostingsJsonStep` in `src/RoleRadar.Application/Tracking/Import/Pipeline/PostingsJsonStep.cs` parsing `postings.json`, auto-creating neutral companies, appending `parent` text to company descriptions, clamping `score` to `0.0..10.0`, truncating `culture` text to 500 characters, and invoking `RecordPostingCommand` with actor `cli-import-tracker` and operation `import_posting_create`
- [ ] T021 [US3] Implement `AppendixRowsStep` in `src/RoleRadar.Application/Tracking/Import/Pipeline/AppendixRowsStep.cs` sorting non-duplicates before duplicates, matching by company + normalized title when req/URL missing, mapping `dismissed` to `screened_out` (`never`), linking `duplicate_of_id` if target exists (or `screened_out` reason "duplicate" if target missing), and emitting `import_posting_create` or `import_posting_transition`

---

## Phase 6: User Story 4 - Screening Criteria Creation & Provenance (Priority: P2)

**Goal**: Seed predefined compensation criteria (`comp_preferred_floor=200000`, `comp_high_priority_bar=220000`) with `stated` provenance and auto-register novel `reason_primary` values as criteria with `inferred` provenance linked to posting dispositions.

**Independent Test**: Run import against rows with new `reason_primary` values; assert criteria are created with provenance `inferred` and effective date, and postings link `disposition_criterion_id`.

### Tests for User Story 4 (MANDATORY - Test-First per Principle II) ⚠️

- [ ] T022 [P] [US4] Integration tests in `tests/RoleRadar.Application.Tests/Tracking/CriteriaImportStepTests.cs` verifying idempotent seeding of `comp_preferred_floor=200000` and `comp_high_priority_bar=220000` with provenance `stated`, auto-creation of `reason_primary` criteria with provenance `inferred`, and linking `disposition_criterion_id` (expected to fail before implementation)

### Implementation for User Story 4

- [ ] T023 [US4] Implement criteria seeding and first-use registration in `src/RoleRadar.Application/Tracking/Import/Pipeline/CriteriaSeedingStep.cs` emitting `import_criterion_revision` with actor `cli-import-tracker` and linking `disposition_criterion_id` during posting transitions

---

## Phase 7: User Story 5 - Applications, Interaction Events, and Standalone Notes (Priority: P2)

**Goal**: Ingest `applications.csv`; transition postings to `applied`, record rejection events, preserve `date_is_stated_not_submitted` flags in notes, leave unclaimed `applied` postings in `scored`, import `standalone_note` rows into `job-search`, and list detected people for manual entry.

**Independent Test**: Import applications; verify posting transition to `applied`, chronology event creation, unclaimed roles staying `scored`, standalone notes stored in `job-search`, and zero auto-created contacts.

### Tests for User Story 5 (MANDATORY - Test-First per Principle II) ⚠️

- [ ] T024 [P] [US5] Integration tests in `tests/RoleRadar.Application.Tests/Tracking/ApplicationsImportStepTests.cs` verifying application creation transitioning posting to `applied`, unclaimed `postings.json` roles with status `applied` staying in `scored`, rejection timeline event appending, `date_is_stated_not_submitted` notes recording, `standalone_note` import into area `job-search` with `import_create`, and zero auto-created contacts (expected to fail before implementation)

### Implementation for User Story 5

- [ ] T025 [US5] Implement `ApplicationsStep` in `src/RoleRadar.Application/Tracking/Import/Pipeline/ApplicationsStep.cs` invoking `CreateApplicationCommand` with actor `cli-import-tracker` and operation `import_application_create`, appending rejection events with `import_application_event_append`, creating standalone notes via `CreateNoteCommand` (`import_create`), tracking unclaimed `applied` postings, and extracting `people_detected` for manual reporting

---

## Phase 8: User Story 6 - Execution Reporting & Privacy Guard (Priority: P3)

**Goal**: Deliver console output with aggregate counts and row numbers only; provide `--report <path>` detailing row mappings while refusing any path inside a git work tree; add container and CLI help snapshot tests.

**Independent Test**: Execute with console output and verify 0 company names/titles/free text; invoke `--report` targeting inside git work tree and verify exit code 1; invoke `--report` targeting outside git work tree and verify detailed UTF-8 report; run container integration test against read-only bundle.

### Tests for User Story 6 (MANDATORY - Test-First per Principle II) ⚠️

- [ ] T026 [P] [US6] Unit and integration tests in `tests/RoleRadar.Cli.Tests/TrackerImportReportingTests.cs` asserting console stdout contains 0 company names, job titles, or salary figures across all runs; asserting `--report <path>` fails with exit code 1 if `<path>` is inside git work tree; and asserting external report contains auto-created company alias review list and unclaimed `applied` postings list (expected to fail before implementation)
- [ ] T027 [P] [US6] CLI help snapshot test in `tests/RoleRadar.Cli.Tests/Snapshot/CliHelpSnapshotTests.cs` verifying `tracker-import` is recorded in root CLI help with dry-run default documented (expected to fail before implementation)
- [ ] T028 [P] [US6] Container integration test in `tests/RoleRadar.Cli.Tests/Integration/TrackerImportContainerTests.cs` asserting `tracker-import` runs successfully from the `roleradar-cli` container image against a read-only mounted fictional bundle (`:ro`) writing `--report` to a separate writable mount (`:rw`) (expected to fail before implementation)

### Implementation for User Story 6

- [ ] T029 [P] [US6] Implement report generator and git work-tree detector in `src/RoleRadar.Application/Tracking/Import/Report/TrackerImportReportGenerator.cs` verifying destination path is outside git working tree and writing detailed row outcomes, auto-created companies, unclaimed applied postings, and manual contact instructions
- [ ] T030 [US6] Implement console formatter in `src/RoleRadar.Cli/Commands/TrackerImportCommand.cs` printing strictly aggregate counts and row numbers to stdout and diagnostic logs to stderr

---

## Phase 9: Polish, Review Gate & Verification

**Purpose**: End-to-end durability verification, coverage gate enforcement, and pre-commit review gates.

- [ ] T031 [P] Integration test in `tests/RoleRadar.Cli.Tests/Integration/TrackerImportBackupRestoreTests.cs` verifying database backup followed by restore round-trips 100% of imported tracker data
- [ ] T032 Integration test verifying `roleradar verify` exits with code 0 (clean) after tracker import
- [ ] T033 Execute `scripts/review-gate.ps1` and verify the dev-stack canary passes, Domain+Application test coverage remains >= 85%, and the pre-commit denylist hook passes with zero detections

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup & Foundational Audit (Phase 1)**: Can start immediately.
- **Foundational Scratch Guard & Manager (Phase 2)**: Depends on Phase 1. **T008/T009 MUST complete before T010/T011** (guard and validation regex land before any drop logic).
- **User Story 1 (Phase 3)**: Depends on Phase 2. Delivers MVP CLI preview and apply scaffolding.
- **User Story 2 (Phase 4)**: Depends on Phase 3. Ingests aliases and companies.
- **User Story 3 (Phase 5)**: Depends on Phase 4. Ingests postings and appendix rows (linking companies).
- **User Story 4 (Phase 6)**: Depends on Phase 5. Seeds criteria and links disposition reasons.
- **User Story 5 (Phase 7)**: Depends on Phase 5 and Phase 6. Ingests applications (linking postings and criteria).
- **User Story 6 (Phase 8)**: Depends on Phase 3-7. Implements console output privacy, git work-tree refusal, and container tests.
- **Polish & Review Gate (Phase 9)**: Depends on all stories complete.

### Parallel Opportunities

- **Phase 1**: T001, T002, T003, T004, T005, T006 can run in parallel across separate files.
- **Phase 2**: T008 (tests) and T009 (guard) must precede T010/T011.
- **Phase 8**: T026, T027, T028 can be written in parallel across CLI, snapshot, and container test suites.

---

## Parallel Example: Foundational Primitives

```bash
# Launch test-first primitives in parallel:
Task: "Unit tests for RFC 4180 CSV parsing in tests/RoleRadar.Infrastructure.Tests/Csv/Rfc4180CsvReaderTests.cs"
Task: "Unit tests for date inference and rollback in tests/RoleRadar.Domain.Tests/Tracking/DateInferenceRuleTests.cs"
Task: "Architecture tests for AuditOperation allowlist in tests/RoleRadar.Tests.Architecture/LayeringAndAppendOnlyArchitectureTests.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Scaffolding)

1. Complete Phase 1 (Audit allowlist, RFC 4180 CSV reader, date inference).
2. Complete Phase 2 (Scratch database guard with strict regex and age check, scratch manager).
3. Complete Phase 3 (US1 CLI command, dry-run preview default, `--apply` requirement).
4. **Checkpoint**: Run dry-run against empty/sample bundle; verify 0 persistent writes and 0 audit entries.

### Incremental Feature Delivery

1. Add US2 (Companies & aliases) -> verify company statuses and auto-creation of neutral companies.
2. Add US3 (Postings & appendix rows) -> verify deduplication tiers, duplicate links, and invalid transition handling.
3. Add US4 (Criteria seeding & reason_primary) -> verify criteria created on first use with inferred provenance.
4. Add US5 (Applications & timeline events) -> verify application creation, unclaimed roles staying scored, and notes in `job-search`.
5. Add US6 (Privacy reporting & containerization) -> verify console output privacy, git work-tree refusal, help snapshot, and container run.
6. Phase 9 (Review Gate) -> backup/restore roundtrip, verify clean check, run `scripts/review-gate.ps1`.
