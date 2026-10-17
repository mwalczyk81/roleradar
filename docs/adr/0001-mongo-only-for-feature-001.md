# ADR 0001: MongoDB Replica Set Only for Feature 001 (Multi-Document Atomicity)

- **Status**: Accepted
- **Date**: 2026-10-17
- **Feature**: `001-memory-replacement`

## Context and Problem Statement

RoleRadar's overall architecture uses PostgreSQL for relational entities (companies, postings, applications) and MongoDB for narrative markdown notes. In Feature 001 (`001-memory-replacement`), only narrative notes, note revisions, and write audit log entries are in scope. Every write operation (`CreateNote`, `AppendToNote`, `ReplaceNoteSection`, `ArchiveNote`, and `ImportMarkdown` overwrite) must atomically persist the note change, the prior-body revision (when applicable), and the append-only audit log entry so a crash or mid-operation failure can never leave an unaudited note change or an orphaned revision.

How should persistence be structured in Feature 001 to guarantee atomic multi-record writes without premature infrastructure complexity?

## Decision Drivers

- **Transactional Atomicity**: A single write unit must commit `notes`, `note_revisions`, and `audit_log` together or roll back completely (`FR-008`, `FR-017`).
- **Scope Discipline**: Companies, postings, applications, and criteria tables are explicitly out of scope for Feature 001.
- **Simplicity (Constitution Principle IV)**: Avoid distributed transactions (`2PC`) or cross-datastore outbox workers when all Feature 001 records belong to the narrative note domain.

## Considered Options

1. **Single-node MongoDB replica set (`rs0`, `mongo:8.0.34`) storing `notes`, `note_revisions`, and `audit_log` (MongoDB-only in Feature 001)**.
2. **Split `notes` in MongoDB and `audit_log` in PostgreSQL in Feature 001**.
3. **Standalone MongoDB instance without replica set**.

## Decision Outcome

Chosen option: **Option 1 — Single-node MongoDB replica set (`rs0`, image `mongo:8.0.34`) storing `notes`, `note_revisions`, and `audit_log`**, because MongoDB multi-document ACID transactions (`IClientSessionHandle.WithTransactionAsync`) require a replica set topology and guarantee all-or-nothing writes across `notes`, `note_revisions`, and `audit_log` within a single database connection.

### Consequences

- **Good**: Every write unit commits `notes`, `note_revisions`, and `audit_log` atomically in a single ACID transaction, verified by a fault-injection test.
- **Good**: `deploy/docker-compose.yml`, backup/restore streaming (`mongodump --archive --gzip`), and Testcontainers setup only need a single container (`mongo:8.0.34`) in Feature 001.
- **Bad / Trade-off**: `deploy/docker-compose.yml` and Testcontainers must initialize the single-node replica set (`rs.initiate()`) before transactions can execute.
