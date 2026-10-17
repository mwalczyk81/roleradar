# Architecture Decision Records (MADR Index)

All significant architectural and library decisions for RoleRadar are recorded here in Markdown Architectural Decision Record (MADR) format per Constitution Principle VIII.

| ADR | Title | Status | Feature |
|-----|-------|--------|---------|
| [ADR-0001](./0001-mongo-only-for-feature-001.md) | MongoDB Replica Set Only for Feature 001 (Multi-Document Atomicity) | Accepted | `001-memory-replacement` |
| [ADR-0002](./0002-inote-repository-single-write-port.md) | `INoteRepository` as the Aggregate-Level Transactional Write Port | Accepted | `001-memory-replacement` |
| [ADR-0003](./0003-stdio-first-mcp.md) | Stdio-First MCP Host Without Network Listeners | Accepted | `001-memory-replacement` |
| [ADR-0004](./0004-verified-mcp-sdk-usage.md) | Live-Verified NuGet Package & Docker Image Pins and API Signatures | Accepted | `001-memory-replacement` |
| [ADR-0005](./0005-revision-history-policy.md) | Revision History Policy — Full Snapshots Only for Destructive Edits | Accepted | `001-memory-replacement` |
| [ADR-0006](./0006-handlers-with-decorators-no-mediator.md) | Plain Command & Query Handlers with Explicit Decorators Instead of a Mediator Library | Accepted | `001-memory-replacement` |
| [ADR-0007](./0007-backup-streaming-aead-encryption.md) | Streamed Backup Authenticated Encryption (`Geralt` libsodium `secretstream` + `Argon2id`) and Two-Pass Restore | Accepted | `001-memory-replacement` |
