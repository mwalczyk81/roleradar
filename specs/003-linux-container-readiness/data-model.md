# Data Model & Schema Specifications: Linux Container Readiness

**Feature**: `003-linux-container-readiness`  
**Date**: 2026-10-18  
**Status**: Completed (Phase 1)

> [!NOTE]
> Feature 003 is purely packaging, CI, and test-infrastructure work. No domain entities, application command/query handlers, or MongoDB production collection schemas are created or modified. The data structures below define test and governance artifacts.

---

## 1. Hosted Database Capability Matrix Schema

The capability matrix records the empirical validation of candidate MongoDB database offerings (e.g., Azure Cosmos DB for MongoDB vCore, MongoDB Atlas on Azure, self-hosted containerized replica sets) executed by the external compatibility test runner.

### 1.1 JSON Schema

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "title": "HostedDatabaseCapabilityMatrix",
  "type": "object",
  "required": [
    "timestamp",
    "engineVersion",
    "summary",
    "capabilities",
    "testResults"
  ],
  "properties": {
    "timestamp": {
      "type": "string",
      "format": "date-time",
      "description": "UTC timestamp when the validation suite completed."
    },
    "engineVersion": {
      "type": "string",
      "description": "MongoDB server engine version reported by buildInfo."
    },
    "summary": {
      "type": "object",
      "required": ["totalTests", "passed", "failed", "allRequiredPassed"],
      "properties": {
        "totalTests": { "type": "integer" },
        "passed": { "type": "integer" },
        "failed": { "type": "integer" },
        "allRequiredPassed": { "type": "boolean" }
      }
    },
    "capabilities": {
      "type": "object",
      "required": [
        "crossCollectionTransactions",
        "partialUniqueIndexes",
        "textSearch",
        "toolsBackupRestore"
      ],
      "properties": {
        "crossCollectionTransactions": {
          "type": "object",
          "required": ["supported", "details"],
          "properties": {
            "supported": { "type": "boolean" },
            "maxTransactionTimeSec": { "type": "number" },
            "details": { "type": "string" }
          }
        },
        "partialUniqueIndexes": {
          "type": "object",
          "required": ["supported", "details"],
          "properties": {
            "supported": { "type": "boolean" },
            "details": { "type": "string" }
          }
        },
        "textSearch": {
          "type": "object",
          "required": ["supported", "details"],
          "properties": {
            "supported": { "type": "boolean" },
            "details": { "type": "string" }
          }
        },
        "toolsBackupRestore": {
          "type": "object",
          "required": ["supported", "details"],
          "properties": {
            "supported": { "type": "boolean" },
            "details": { "type": "string" }
          }
        }
      }
    },
    "testResults": {
      "type": "array",
      "items": {
        "type": "object",
        "required": ["testName", "category", "passed", "durationMs"],
        "properties": {
          "testName": { "type": "string" },
          "category": { "type": "string", "enum": ["Transactions", "Indexes", "TextSearch", "Tools"] },
          "passed": { "type": "boolean" },
          "durationMs": { "type": "number" },
          "errorMessage": { "type": ["string", "null"] }
        }
      }
    }
  }
}
```

### 1.2 Sanitization Constraint
Under no circumstances may the capability matrix include:
- Passwords or user authentication tokens
- Hostnames or IP addresses of the tested cluster
- Connection strings or query parameter strings

---

## 2. External Database Safety Guard Descriptor

Defines the structure and decision model executed by `ExternalMongoSafetyGuard` prior to opening connections or mutating any test database on an external MongoDB service.

### 2.1 Guard State Machine & Error Codes

```mermaid
stateDiagram-v2
    [*] --> CheckOptIn
    CheckOptIn --> CheckTls: Opt-in present (ROLERADAR_EXTERNAL_TEST_OPT_IN=1)
    CheckOptIn --> AbortOptInMissing: Opt-in missing

    CheckTls --> InspectDatabases: Loopback OR (Remote AND TLS enabled)
    CheckTls --> AbortTlsMissing: Remote host without TLS

    InspectDatabases --> AuthorizeExecution: Server has only (system DBs OR rrtest_* DBs)
    InspectDatabases --> AbortForeignDatabases: Non-system database without rrtest_ prefix found

    AuthorizeExecution --> [*]
    AbortOptInMissing --> [*]
    AbortTlsMissing --> [*]
    AbortForeignDatabases --> [*]
```

### 2.2 Error Codes & Descriptions

| Guard Error Code | Condition | Action |
| :--- | :--- | :--- |
| `OptInNotSet` | `ROLERADAR_EXTERNAL_TEST_OPT_IN != "1"` | Abort test run; instruct developer to set opt-in variable explicitly. |
| `TlsRequiredForNonLoopback` | Target host is not `127.0.0.1`, `localhost`, or `::1`, and connection string lacks `tls=true` or `ssl=true`. | Abort test run; reject plaintext remote connections. |
| `ForeignDatabaseDetected` | Target MongoDB instance contains one or more non-system databases that do not start with `rrtest_`. | Abort test run; log names of invalid database prefixes (sanitized) to prevent running against non-test instances. |

---

## 3. Vulnerability Allowlist Schema

Governs container image security scanning exceptions in CI (`.trivy-allowlist.yaml`).

### 3.1 YAML Schema Example

```yaml
# RoleRadar Container Vulnerability Allowlist
# Enforces expiration dates and architectural justification for unfixed CVEs.
allowlist:
  - id: "CVE-2026-XXXX"
    component: "libssl3"
    package_version: "3.0.13-0ubuntu3.4"
    severity: "CRITICAL"
    has_upstream_fix: false
    justification: "Base Ubuntu Noble slice has no available vendor patch; network listeners are disabled in container."
    expires_on: "2026-11-30"
    reviewed_by: "mwalczyk81"
```

### 3.2 Evaluation Invariants
1. If a critical vulnerability has an available fix (`has_upstream_fix: true`), it **cannot** be allowlisted and fails the build.
2. If `current_date > expires_on`, the allowlist rule is expired and the build fails.
3. Every entry must have non-empty `justification` and `reviewed_by` fields.
