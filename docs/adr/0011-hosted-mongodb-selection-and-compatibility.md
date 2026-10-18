# ADR 0011: Hosted MongoDB Database Evaluation & Compatibility Checklist

- **Status**: Proposed
- **Date**: 2026-10-18
- **Feature**: `003-linux-container-readiness`

## Context and Problem Statement

RoleRadar is preparing for Linux container readiness in cloud environments (such as Azure). While the application currently runs against a local single-node replica set in Docker, hosting future HTTP services (Feature 005 API/web, Feature 006 hosted MCP over HTTP) requires a reliable, managed, or self-hosted database service.

RoleRadar's data layer has several strict architectural invariants:

1. **Multi-Document Cross-Collection Transactions**: Every state-modifying write command is wrapped in a multi-document transaction committing both the entity mutation and its append-only audit entry atomically across distinct collections (`postings`, `audit_log`, `companies`, etc.).
2. **Partial Unique Indexes**: Postings deduplication relies on partial filter expressions (`dedup_key_a`, `dedup_key_b`, `dedup_key_c`) where keys exist only when specific identifiers are present. Criteria require a partial unique index on `(owner_id, key)` where `active == true`.
3. **Text Search Indexes**: Notes retrieval relies on full-text stemming, tokenization, and phrase search indexes across `(title, body, tags)`.
4. **Database Tools Backup & Restore**: Administrative backup and restore rely on bundled `mongodump` and `mongorestore` binary tools (MongoDB 8.0.x baseline) operating via archive streams.

Which hosted MongoDB option should RoleRadar target, and what compatibility risks exist?

## Considered Options

1. **MongoDB Atlas on Azure**: Managed MongoDB natively provided by MongoDB Inc., hosted within Azure regions.
2. **Azure Cosmos DB for MongoDB (vCore Architecture)**: Managed PostgreSQL-backed MongoDB API service running on Azure.
3. **Self-Hosted Containerized MongoDB Replica Set on Azure VM or Persistent Storage (Azure Files / Managed Disks)**.

## Compatibility Matrix & Feature Comparison

| Capability / Metric | MongoDB Atlas on Azure | Azure Cosmos DB for MongoDB vCore | Self-Hosted MongoDB Container |
| :--- | :--- | :--- | :--- |
| **Engine Reality** | 100% Real MongoDB (8.0.x) | Translation layer over Azure Citus / Postgres | 100% Real MongoDB (8.0.x) |
| **Cross-Collection Transactions** | **Fully Supported** (Standard multi-document transactions) | **Supported** (Subject to 30-second lifetime cap; distributed edge cases require verification) | **Fully Supported** (Standard replica set) |
| **Partial Unique Indexes** | **Fully Supported** (Standard partialFilterExpression) | **Partial/Uncertain** (Unique index builds require exclusive table locks; partial filter syntax compatibility requires live verification) | **Fully Supported** |
| **Text Search ($text)** | **Fully Supported** (Standard text indexes) | **Supported** (Full text search supported via inverted index extension, syntax nuances exist) | **Fully Supported** |
| **Database Tools Compatibility** | **100% Compatible** (`mongodump` / `mongorestore` 8.0.x) | **High Compatibility** (Supports mongodump/restore, but subtle metadata flags may fail) | **100% Compatible** |
| **Pricing / Free Tier** | Shared M0 Free Tier (512 MB storage, shared RAM, no SLA; regional limits apply) | Minimum tier starts at ~0.25 vCore / ~$30–$40/mo (no perpetual free tier) | Azure VM B1s / B2s (~$10–$20/mo) or Container Apps volume |
| **Operational Overhead** | Near Zero (Fully managed backups, patches, monitoring) | Near Zero (Managed by Azure) | High (Manual OS updates, volume snapshots, replica-set health) |

## Decision Outcome

Recommended candidate: **MongoDB Atlas on Azure (Shared Tier / M0 initially, M10 if scaled)**, validated empirically using RoleRadar's new **External Database Compatibility Test Suite**.

### Rationale

- RoleRadar's complex multi-collection transactional decorators and partial unique indexes work with 100% fidelity on real MongoDB engines without semantic translation bugs.
- Atlas on Azure provides local network peering / private endpoints to Azure workloads while providing the lowest operational overhead.
- Cosmos DB vCore is a strong secondary candidate within pure Azure environments, but its 30-second transaction duration cap and unique index locking behavior introduce architectural risk.

### Open Items (Resolved Empirically via Compatibility Mode)

The following compatibility questions cannot be definitively closed by documentation alone and will be verified by pointing `RoleRadar.Tests.Contract` with `ROLERADAR_EXTERNAL_TEST_OPT_IN=1` at candidate endpoints:

1. Verify whether Cosmos DB vCore supports partial unique index expressions matching `{"dedup_key_a": {"$exists": true}}` without syntax rejection.
2. Confirm that `mongodump` and `mongorestore` 8.0.x archive streaming with `--gzip` completes against Cosmos DB vCore without namespace metadata warnings.
3. Verify that Atlas M0 free tier does not throttle concurrent burst connections during the 8-process parallel search run test.
