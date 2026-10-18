# Contract: External Database Safety Guard & Compatibility Matrix

**Feature**: `003-linux-container-readiness`  
**Date**: 2026-10-18  
**Status**: Approved

## 1. Safety Guard Contract (`ExternalMongoSafetyGuard`)

The safety guard intercepts the contract test runner whenever an external connection string is configured.

### 1.1 Trigger Configuration
- **Opt-In Switch**: Environment variable `ROLERADAR_EXTERNAL_TEST_OPT_IN=1`. If missing or not equal to `1`, the test runner uses local Testcontainers.
- **Connection URI**: Environment variable `ROLERADAR_TEST_MONGODB_URI`.

### 1.2 Validation Sequence

```text
Step 1: Check Opt-In
  IF ROLERADAR_EXTERNAL_TEST_OPT_IN != "1" -> THROW OptInMissingException

Step 2: Check TLS on Non-Loopback
  IF host NOT IN ["127.0.0.1", "localhost", "::1"] AND uri NOT CONTAINS ("tls=true" OR "ssl=true")
    -> THROW TlsRequiredException

Step 3: Check Existing Database Names
  FETCH all database names via client.ListDatabaseNamesAsync()
  FILTER out system databases: ["admin", "config", "local"]
  IF any remaining database does NOT start with "rrtest_"
    -> THROW ForeignDatabaseDetectedException (naming invalid databases)

Step 4: Register and Track Test Databases
  GENERATE database name: $"rrtest_{Guid.CreateVersion7():N}"
  REGISTER database in static ConcurrentBag<string> TrackedTestDatabases

Step 5: Guaranteed Cleanup
  ON test fixture completion, test failure, or AppDomain/ProcessExit/SIGTERM:
    FOR EACH db in TrackedTestDatabases:
      DROP database db via client.DropDatabaseAsync(db)
```

---

## 2. Capability Matrix Output Format

The test runner outputs both a JSON file (`TestResults/database-capability-matrix.json`) and a Markdown report (`TestResults/database-capability-matrix.md`):

### 2.1 Markdown Report Template

```markdown
# Database Compatibility Checklist: [Engine Name / Candidate]

- **Date**: 2026-10-18T16:00:00Z
- **Reported Engine Version**: MongoDB 8.0.x (or Cosmos vCore / Atlas)
- **Result**: [PASS / FAIL]

## Invariant Verification

| Invariant | Test Fixture | Status | Failure / Limitation Details |
| :--- | :--- | :---: | :--- |
| Cross-Collection Transactions | `MongoTransactionContractTests` | PASS | Multi-document commits & rollbacks verified across 12 collections |
| Partial Unique Indexes | `DeduplicationIndexContractTests` | PASS | Partial filter expressions on `normalized_url`, `dedup_key_a/b/c` verified |
| Text Search Indexes | `NoteSearchContractTests` | PASS | Full-text stemming and phrase search verified |
| Database Tools Backup | `TrackingBackupSafetyTests` | PASS | `mongodump` archive dump stream completed with zero errors |
| Database Tools Restore | `TrackingBackupSafetyTests` | PASS | `mongorestore` two-pass reload verified with exact document counts |
```
