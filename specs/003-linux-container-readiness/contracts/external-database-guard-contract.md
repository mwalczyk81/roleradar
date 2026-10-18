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

Step 2: Parse URI via MongoDB C# Driver Parser (MongoUrl / MongoUrlBuilder)
  PARSE connection string using `new MongoUrl(connectionString)` (never substring checks).
  IDENTIFY hosts:
    Loopback hosts: "localhost", IPv6 "::1", and any IPv4 in 127.0.0.0/8 (e.g., 127.0.0.1, 127.0.0.2).
    Non-loopback hosts: Any remote hostname, IP outside 127.0.0.0/8, and "host.docker.internal".
  DETERMINE TLS status:
    - Scheme "mongodb+srv://" implies TLS (UseTls == true) unless explicitly disabled.
    - Explicit conflicting parameters (e.g. tls=false with ssl=true) -> REJECT as invalid.
    - IF any host is non-loopback AND (UseTls == false OR tls=false OR ssl=false):
        -> THROW TlsRequiredException (refusing plaintext remote connection).

Step 3: Check Existing Database Names & Cluster Privileges
  TRY:
    FETCH all database names via client.ListDatabaseNamesAsync()
  CATCH MongoCommandException / UnauthorizedException:
    -> THROW RedactedPermissionException:
       "External database guard failed: user lacks 'listDatabases' cluster privilege to verify database prefix safety. Provide an account with cluster monitor or listDatabases privileges, or use a dedicated test cluster."
  FILTER out system databases: ["admin", "config", "local"]
  IF any remaining database does NOT start with "rrtest_"
    -> THROW ForeignDatabaseDetectedException (naming invalid databases, sanitized)

Step 4: Register and Track Test Databases
  GENERATE database name: $"rrtest_{Guid.CreateVersion7():N}"
  REGISTER database in static ConcurrentBag<string> TrackedTestDatabases

Step 5: Guaranteed Cleanup
  ON test fixture completion, test failure, or AppDomain/ProcessExit/SIGTERM:
    FOR EACH db in TrackedTestDatabases:
      DROP database db via client.DropDatabaseAsync(db)
```

### 1.3 Safety Guard Unit-Test Case Matrix

The pure unit-test suite (`RoleRadar.Tests.Contract/ExternalDatabaseGuardUnitTests.cs`) verifies the following test cases:

| Test Case | Connection URI / Scenario | Host Classification | TLS Evaluation | Expected Result |
| :--- | :--- | :--- | :--- | :--- |
| `SrvImpliesTls` | `mongodb+srv://cluster0.example.mongodb.net/` | Remote (SRV) | Implied TLS (`true`) | **Allowed** |
| `SrvExplicitTlsDisabled` | `mongodb+srv://cluster0.example.mongodb.net/?tls=false` | Remote (SRV) | Explicit `tls=false` | **Rejected** (`TlsRequiredException`) |
| `DockerInternalTreatedAsNonLoopback` | `mongodb://host.docker.internal:27017/?tls=false` | Non-loopback | Explicit `tls=false` | **Rejected** (`TlsRequiredException`) |
| `Ipv4LoopbackSubnet127` | `mongodb://127.0.0.2:27017` | Loopback (`127.0.0.0/8`) | Plaintext | **Allowed** |
| `Ipv6Loopback` | `mongodb://[::1]:27017` | Loopback (`::1`) | Plaintext | **Allowed** |
| `LocalhostLoopback` | `mongodb://localhost:27017` | Loopback (`localhost`) | Plaintext | **Allowed** |
| `ConflictingTlsParameters` | `mongodb://remote-db.example.com:27017/?tls=false&ssl=true` | Remote | Invalid / Conflicting | **Rejected** (`ConflictingTlsParametersException`) |
| `RemoteWithExplicitTls` | `mongodb://remote-db.example.com:27017/?tls=true` | Remote | Explicit `tls=true` | **Allowed** |
| `RemotePlaintextRefused` | `mongodb://remote-db.example.com:27017` | Remote | Plaintext (default) | **Rejected** (`TlsRequiredException`) |
| `LacksListDatabasesPrivilege` | Remote cluster where user cannot execute `listDatabases` | Remote (TLS) | Valid TLS | **Rejected** with redacted permission message (no host or secret leaks) |
| `ForeignDatabasePresent` | Remote cluster containing `production_db` | Remote (TLS) | Valid TLS | **Rejected** (`ForeignDatabaseDetectedException`) |

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
