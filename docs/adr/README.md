# Architecture Decision Records (MADR Index)

All significant architectural and library decisions for RoleRadar are recorded here in Markdown Architectural Decision Record (MADR) format per Constitution Principle VIII.

| ADR | Title | Status | Feature |
| ----- | ------- | -------- | --------- |
| [ADR-0001](./0001-mongo-only-for-feature-001.md) | MongoDB Replica Set Only for Feature 001 (Multi-Document Atomicity) | Accepted | `001-memory-replacement` |
| [ADR-0002](./0002-inote-repository-single-write-port.md) | `INoteRepository` as the Aggregate-Level Transactional Write Port | Accepted | `001-memory-replacement` |
| [ADR-0003](./0003-stdio-first-mcp.md) | Stdio-First MCP Host Without Network Listeners | Accepted | `001-memory-replacement` |
| [ADR-0004](./0004-verified-mcp-sdk-usage.md) | Live-Verified NuGet Package & Docker Image Pins and API Signatures | Accepted | `001-memory-replacement` |
| [ADR-0005](./0005-revision-history-policy.md) | Revision History Policy — Full Snapshots Only for Destructive Edits | Accepted | `001-memory-replacement` |
| [ADR-0006](./0006-handlers-with-decorators-no-mediator.md) | Plain Command & Query Handlers with Explicit Decorators Instead of a Mediator Library | Accepted | `001-memory-replacement` |
| [ADR-0007](./0007-backup-streaming-aead-encryption.md) | Streamed Backup Authenticated Encryption (`Geralt` libsodium `secretstream` + `Argon2id`) and Two-Pass Restore | Accepted | `001-memory-replacement` |
| [ADR-0008](./0008-mongodb-structured-job-search-storage.md) | MongoDB Replica Set Only for Structured Job-Search Storage | Accepted | `002-job-search-tracking` |
| [ADR-0009](./0009-pattern-reuse-and-typed-results-for-feature-002.md) | Architectural Pattern Reuse and Typed Operational Failures for Feature 002 | Accepted | `002-job-search-tracking` |
| [ADR-0010](./0010-immutable-auditability-and-zero-hard-deletes.md) | Immutable Auditability, Zero Hard Deletes, and Contact Privacy Isolation | Accepted | `002-job-search-tracking` |
| [ADR-0011](./0011-hosted-mongodb-selection-and-compatibility.md) | Hosted MongoDB Database Evaluation & Compatibility Checklist | Proposed | `003-linux-container-readiness` |
| [ADR-0012](./0012-azure-container-runtime-target.md) | Azure Container Runtime Target — Container Apps vs. AKS | Proposed | `003-linux-container-readiness` |
| [ADR-0013](./0013-tracker-import-dry-run-scratch-database.md) | Tracker Import Dry-Run Mechanism — Ephemeral Scratch Database vs. Outer Rolled-Back Transaction | Accepted | `004-tracker-import` |
| [ADR-0014](./0014-csv-parsing-strategy.md) | CSV Parsing Strategy — Hand-Rolled RFC 4180 Parser vs. External Library | Accepted | `004-tracker-import` |
| [ADR-0015](./0015-identity-operational-store-selection.md) | Identity Operational Store Selection — SQLite File Volume vs. PostgreSQL Container | Accepted | `005a-api-and-login` |
| [ADR-0016](./0016-duende-identityserver-community-edition-and-fallback.md) | Duende IdentityServer Community Edition Licensing and IdP Fallback Strategy | Accepted | `005a-api-and-login` |
| [ADR-0017](./0017-owner-credential-and-security-controls.md) | Single-Owner Credential Handling and Authentication Security Controls | Accepted | `005a-api-and-login` |
| [ADR-0018](./0018-rest-api-style-and-owner-mapping.md) | REST API Style, Error Envelope, Paging, and Owner Mapping | Accepted | `005a-api-and-login` |
| [ADR-0019](./0019-openapi-generation-and-typed-client.md) | OpenAPI Contract Generation, Committed Snapshot Verification, and Typed Client Generation | Accepted | `005a-api-and-login` |
| [ADR-0020](./0020-https-transport-and-local-compose-certificates.md) | Transport Security, Local Compose Certificates, and Internal Health Probing | Accepted | `005a-api-and-login` |
