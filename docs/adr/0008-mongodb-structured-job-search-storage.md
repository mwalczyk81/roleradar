# ADR 0008: MongoDB Replica Set Only for Structured Job-Search Storage

- **Status**: Accepted
- **Date**: 2026-10-17
- **Feature**: `002-job-search-tracking`

## Context and Problem Statement

Feature 002 introduces structured tracking for job postings, companies, career sources, screening criteria, applications, application timeline events, posting signals, private contacts, and search runs. A common enterprise pattern introduces a relational database (such as PostgreSQL) for structured business entities while keeping MongoDB for unstructured documents (notes).

In RoleRadar, the data volume for the single owner is modest (hundreds of companies, thousands of postings over years). At the same time, operations frequently require cross-entity atomic consistency:

- Ingesting an application must transition a posting's disposition to `applied` and append to `audit_log`.
- An application submission with an override flag for a `screened_out` or `closed` posting must atomically execute reopen-and-apply in a single transaction.
- Backup and restore procedures must protect the entire database state with authenticated streaming encryption and verifiable non-empty checks.

How should persistence for structured tracking entities be implemented in Feature 002 to guarantee transactional consistency, durability, and operational simplicity?

## Decision Drivers

- **Transactional Atomicity**: State changes across postings, applications, events, and audit logs must commit together in a single ACID transaction (`FR-024`, `FR-042`).
- **Unified Disaster Recovery**: All entities must be covered by the established audited streaming encryption backup (`Geralt` Argon2id + XChaCha20-Poly1305 `mongodump`), wipe, and restore pipeline (`FR-045`).
- **Scale & Performance**: Sub-100ms p95 posting lookup by deduplication key and sub-200ms p95 search across 5,000 postings (`SC-006`).
- **Architectural Simplicity (Constitution Principle IV)**: Avoid introducing a second database engine (PostgreSQL), dual connection management, distributed transactions (2PC), or complex outbox relay workers for a single-owner system.

## Considered Options

1. **MongoDB Replica Set Only (Same database `roleradar`, same replica set `rs0`)**: Store structured entities in dedicated collections (`companies`, `career_sources`, `postings`, `posting_signals`, `criteria`, `applications`, `application_events`, `contacts`, `search_runs`), each carrying `owner_id`.
2. **Polyglot Persistence (PostgreSQL for structured entities, MongoDB for narrative notes)**: Introduce PostgreSQL for relational tables and maintain MongoDB for notes.
3. **Migrate Entire Solution to PostgreSQL**: Migrate narrative notes, revisions, and audit logs from MongoDB to PostgreSQL.

## Decision Outcome

Chosen option: **Option 1 — MongoDB Replica Set Only**.

All structured entities live in dedicated collections within the existing `roleradar` database on the single-node replica set `rs0`.

### Rationale

1. **Single Transaction Boundary**: MongoDB's multi-document ACID transactions (`IClientSessionHandle.WithTransactionAsync`) natively span all collections within the database. Cross-entity workflows (such as application creation updating posting disposition and logging audit records) commit atomically in a single network round-trip.
2. **Single Encrypted Backup Stream**: `mongodump --archive --gzip` dumps all collections across notes, structured tracking, and audit logs into a single authenticated encrypted stream, preserving referential integrity at rest and during restore.
3. **Index Optimization**: BSON compound and partial unique indexes provide strict deduplication enforcement (Tiers A, B, and C) and sub-10ms lookup latencies on multi-thousand document collections.
4. **Zero Infrastructure Bloat**: No additional database container, connection pools, or ORMs (e.g. EF Core) are required.

### Consequences

- **Good**: 100% atomic write consistency across structured entities and audit entries.
- **Good**: Zero additional third-party dependencies or container services needed in `deploy/docker-compose.yml`.
- **Good**: Existing encrypted backup, wipe, and restore scripts cover all new entities without structural changes.
- **Bad / Trade-off**: Foreign key referential integrity is enforced at the domain/application layer and verified by the integrity verification query rather than by relational database foreign key constraints.
