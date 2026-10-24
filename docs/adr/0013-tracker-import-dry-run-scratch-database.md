# ADR 0013: Tracker Import Dry-Run Mechanism — Ephemeral Scratch Database vs. Outer Rolled-Back Transaction

- **Status**: Accepted
- **Date**: 2026-10-24
- **Feature**: `004-tracker-import`

## Context and Problem Statement

Feature 004 introduces `roleradar tracker-import`, which imports previous job-search trackers into RoleRadar's structured collections.
The command defaults to dry-run mode and requires `--apply` to commit persistent writes.
The dry-run mode has stringent invariants:

1. **Identical pipeline & handlers**: Must execute the exact same command handlers (`ICommandHandler`) and cross-cutting decorators (transaction, audit, validation, logging) as `--apply`.
2. **Later stages observe earlier writes**: Ingestion occurs in stages (companies -> postings -> appendix rows -> applications). Later stages must observe records staged by earlier stages (e.g. applications resolving against newly ingested postings; duplicate appendix rows linking `duplicate_of_id`).
3. **Per-row error isolation**: A validation or mapping failure on a single row must be isolated, reported with its row number, and must not abort the remainder of the batch.
4. **Zero persistent side-effects**: Must guarantee zero persistent writes on the target database (target collection counts and `audit_log` remain completely unchanged).
5. **Exact count parity**: Dry-run counts of created, updated, and skipped entities must exactly equal apply counts.

Which mechanism should be used to isolate dry-run execution: an **ephemeral scratch database** or an **ambient outer rolled-back transaction**?

## Considered Options

1. **Option A: Ephemeral Scratch Database**
   Before executing the pipeline in dry-run mode, the importer clones/initializes a temporary scratch database (e.g., `roleradar_scratch_<uuid>`) on the same MongoDB instance with existing target collections. The handler DI scope executes against this scratch database. At the end of the run (in a `finally` block), the scratch database is dropped (`client.DropDatabaseAsync(scratchDbName)`).
2. **Option B: Ambient Outer Transaction with Final Abort/Rollback**
   The importer opens an ambient MongoDB multi-document transaction session before running the pipeline. Handlers join this ambient transaction. At the conclusion of dry-run, the outer transaction is explicitly aborted/rolled back rather than committed.

## Comparison & Decision Drivers

| Factor | Option A: Ephemeral Scratch Database | Option B: Ambient Rolled-Back Transaction |
| :--- | :--- | :--- |
| **Handler Code Invariance** | **100% Unchanged**: Handlers and `TransactionAndAuditCommandDecorator` execute their standard per-operation transactions against the scratch database without knowing they are running in dry-run mode. | **Complex / Invasive**: Requires modifying transaction decorators or repositories to support non-committing ambient scopes and bypassing `session.WithTransactionAsync` completion semantics. |
| **Per-Row Error Isolation** | **Native**: If row $N$ fails a domain or database constraint, that row's transaction fails independently, while rows $N+1 \dots M$ execute and commit normally in the scratch database. | **Broken**: In MongoDB (including 8.0), transactions do not support savepoints/nested transactions. Any write error or unhandled abort inside a multi-document transaction marks the entire transaction aborted. Subsequent operations fail with `Transaction has been aborted`. |
| **Multi-Document Limits** | **No Limits**: Each handler executes an independent transaction. Operations are not constrained by MongoDB's 60-second `transactionLifetimeLimitSeconds` or transaction log buffer limits. | **Risk of Timeout**: Importing hundreds of rows in a single continuous multi-document transaction risks exceeding `transactionLifetimeLimitSeconds` (default 60s) or transaction memory thresholds. |
| **Later-Stage Visibility** | **Native**: Queries executed by subsequent stages (`IQueryHandler` / repository reads) see all previously committed records in the scratch database. | **Supported**: Reads within the same transaction session see uncommitted writes via read concern snapshot. |
| **Zero Side-Effects on Target** | **Guaranteed**: Target database is never touched by write operations; scratch database is dropped in a `finally` block. | **Guaranteed**: Rolled back transaction leaves no persistent state on the target database. |
| **Parity with `--apply`** | **Exact**: Because the execution environment, transaction boundaries, and isolation per row are 100% identical to production, dry-run counts are guaranteed to match apply counts. | **Divergent**: Error handling and transaction boundaries differ from `--apply`. |

## Decision Outcome

Chosen option: **Option A: Ephemeral Scratch Database**.

### Rationale

1. **Preserves Existing Handlers Unchanged**: RoleRadar's architecture enforces that every write goes through `ICommandHandler` wrapped by `TransactionAndAuditCommandDecorator`. With Option A, every handler executes its normal transaction and audit log logic against the scratch database. No handler or decorator needs special branching flags or conditional commit logic.
2. **True Per-Row Error Isolation**: MongoDB multi-document transactions do not support savepoints. Under Option B, a single row failure in a batch of 150 rows would abort the outer transaction, preventing any subsequent rows from being evaluated. Under Option A, each row runs in its own transaction within the scratch database; a bad row fails and is isolated without poisoning subsequent rows.
3. **Avoids Multi-Document Transaction Timeouts**: Bundles with ~200+ entities and cross-collection lookups running in a single ambient transaction could exceed MongoDB's `transactionLifetimeLimitSeconds`. Option A eliminates this risk.
4. **Guaranteed Cleanup**: The scratch database name is deterministically scoped with a GUID (`roleradar_scratch_<guid>`). A `finally` block drops the scratch database on completion or failure. In addition, `roleradar wipe` and startup routines can garbage-collect orphaned `roleradar_scratch_*` databases if a process was hard-killed (`SIGKILL`).

## Implementation Details

- When `roleradar tracker-import` runs without `--apply`:
  1. Determine the target database name (e.g. `roleradar`).
  2. Generate scratch database name: `roleradar_scratch_{Guid.NewGuid():N}`.
  3. Clone existing collection documents from target database to scratch database (or create empty collections if target is empty).
  4. Build the DI service provider with `options.DatabaseName = scratchDbName`.
  5. Execute the 5-stage import pipeline using standard `ICommandHandler` invocations.
  6. Collect aggregate statistics, row errors, and report data.
  7. In a `finally` block, invoke `IMongoClient.DropDatabaseAsync(scratchDbName)`.
  8. Assert and report target database collection counts and audit logs remain 100% unchanged.
