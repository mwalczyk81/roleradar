# Research & Technical Decisions: Feature 004 Job-Search Tracker Import

**Feature**: `004-tracker-import`  
**Date**: 2026-10-24  
**Status**: Completed

## 1. Dry-Run Isolation Mechanism

### Context & Challenge
The importer must execute all ingestion steps in dry-run mode by default, providing exact validation, dedup resolution, and staging counts without mutating the persistent database or writing audit log entries. When `--apply` is subsequently executed, the resulting counts must match dry-run counts 100%.

Key constraints:
- Later stages (applications, duplicate rows) must observe earlier staged writes (postings, non-duplicate rows).
- Handlers (`ICommandHandler`) and their `TransactionAndAuditCommandDecorator` must remain unchanged.
- Per-row error isolation must be preserved: a failure on row $N$ must be isolated to that row without aborting the batch.
- Collection document counts and `audit_log` in the target database must be completely untouched.

### Options Evaluated
1. **Option A: Ephemeral Scratch Database**:
   Clone/initialize a temporary database `roleradar_scratch_{guid}` on the target MongoDB instance, point the DI container to it, execute the pipeline through standard handlers, collect counts, and drop the database in a `finally` block.
2. **Option B: Ambient Outer Transaction with Abort**:
   Open a single MongoDB session, start a transaction, run all handlers inside it, and call `AbortTransactionAsync` at the end.

### Decision
**Option A: Ephemeral Scratch Database** (recorded in [ADR 0013](../../docs/adr/0013-tracker-import-dry-run-scratch-database.md)).

### Rationale
- MongoDB multi-document transactions do not support savepoints/nested transactions. If any row fails a unique index or database constraint under Option B, the entire transaction is irreversibly marked aborted, breaking per-row error isolation.
- Option A preserves handlers and transaction decorators 100% unchanged. Each handler executes its normal transaction in the scratch database.
- Multi-document transactions in MongoDB have lifetime limits (default 60 seconds). A large bundle could exceed this timeout. Option A has no transaction duration limits.
- Target database remains 100% untouched.

---

## 2. CSV Parsing Strategy

### Context & Challenge
The bundle contains four CSV files with up to 18 columns, containing quoted text, embedded commas, and multiline descriptions. We evaluated whether to introduce a NuGet package (`CsvHelper` or `Sylvan.Data.Csv`) or implement a hand-rolled RFC 4180 parser.

### Options Evaluated
1. **Option A: Hand-Rolled RFC 4180 Parser (`Rfc4180CsvReader`)**:
   Implement a first-party streaming parser in `RoleRadar.Domain.Tracking` using .NET BCL primitives (`ReadOnlySpan<char>`, `StreamReader`).
2. **Option B: `CsvHelper` (v33.1.0)**:
   External NuGet dependency.
3. **Option C: `Sylvan.Data.Csv` (v1.4.4)**:
   Lightweight external NuGet dependency.

### Decision
**Option A: Hand-Rolled RFC 4180 Parser** (recorded in [ADR 0014](../../docs/adr/0014-csv-parsing-strategy.md)).

### Rationale
- Zero third-party dependencies aligns with Constitution Principle IV ("Named Seams Only") and keeps dependencies pinned.
- Standard RFC 4180 is straightforward to implement (~120 lines) and supports custom row numbering (critical for accurate error isolation and reporting).
- Character length limits and untrusted data checks (Principle V) are applied during tokenization.

---

## 3. Date Rollback & Stated Date Inference

### Context & Challenge
Historical trackers frequently omit years from date columns (e.g., `added_md` as `11/15` or `03-02`), or include non-standard notations like `~2026-01-15` or `2026-04-01 (stated date, may differ from submit date)`.

### Resolution
- **Rollback Rule**: When parsing month-first dates (`M/D`, `MM/DD`, `M-D`), derive the year from `--default-date`. If the resulting date is later than `--default-date`, roll back to the preceding calendar year (e.g., `11/15` with `--default-date 2026-10-24` resolves to `2025-11-15`).
- **Impossible Dates**: Dates like `02/30` are flagged as missing, recorded in the warning report, and fall back to `--default-date`.
- **Approximations & Notes**: Leading `~` or trailing parentheticals are parsed into the ISO date while recording the approximation or stated-date note in event/application notes without failing the row.
- **Reporting**: Summary output categorizes dates into explicit counts: *inferred*, *defaulted*, and *flagged*.

---

## 4. Audit Operation Names & Actor Identity

### Context & Challenge
Per Constitution Principle V and Architecture Tests, all writes must produce append-only audit entries. Writes from the tracker importer must be clearly distinguishable from interactive MCP writes and markdown note imports.

### Resolution
- **Actor**: `cli-import-tracker` for all writes executed by the tracker importer.
- **Operation Names**:
  - Postings: `import_posting_create`, `import_posting_transition`, `import_posting_signal_append`
  - Companies: `import_company_create`, `import_company_update`
  - Applications: `import_application_create`, `import_application_event_append`
  - Criteria: `import_criterion_revision`
  - Standalone Notes: Reuses existing `import_create` (`ImportCreate`) with actor `cli-import-tracker`.
- **Enforcement**: Architecture tests verify that the `AuditOperation` constants and `IsValid` allowlist include all `import_*` operations. Integration tests assert that 0 interactive operation names are emitted during tracker import.

---

## 5. Container Execution & Read-Only Mounting

### Context & Challenge
The importer must run inside the hardened Linux container image (`roleradar-cli`) with the bundle mounted read-only (`:ro`) and an optional `--report` output written to an external writable mount (`:rw`).

### Resolution
- In docker/podman invocation:
  ```bash
  docker run --rm \
    -v /host/path/to/bundle:/bundle:ro \
    -v /host/path/to/reports:/reports:rw \
    -e ROLERADAR_MONGO_CONNECTION_STRING="mongodb://..." \
    roleradar-cli:local \
    tracker-import /bundle --default-date 2026-10-24 --report /reports/import-report.txt
  ```
- The CLI checks that the bundle directory is readable, never opens write streams against the bundle path, and verifies that `--report <path>` does not reside inside a git working tree.

---

## 6. Test Isolation & Dev-Stack Guard

### Context & Challenge
Tests must verify dry-run, apply, error paths, and backup/restore without touching local developer databases.

### Resolution
- All integration and CLI tests use `IsolatedContainerMongoFixture` (backed by Testcontainers).
- The existing dev-stack guard checks `ROLERADAR_MONGO_DATABASE` and connection string parameters to abort immediately if a test attempts to connect to a default local MongoDB instance.
- Real production apply is executed manually by the user only, after a fresh encrypted backup.
