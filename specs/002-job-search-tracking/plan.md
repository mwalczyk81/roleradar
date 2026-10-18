# Implementation Plan: Structured Job Search Tracking

**Branch**: `002-job-search-tracking` | **Date**: 2026-10-17 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/002-job-search-tracking/spec.md` (aligned with **Constitution v1.1.0**)

## Summary

Feature 002 introduces structured job-search tracking for a single owner, turning weekly search context, company ATS intelligence, screening decisions, and application pipelines into durable, queryable data. It prevents re-screening of previously decided postings, captures custom career portal check instructions, records applications and timeline events with an atomic reopen-and-apply override, revisions screening criteria over time, and protects confidential professional contacts with strict privacy audit guards.

Technically, Feature 002 builds directly on Feature 001's architectural foundation without inventing parallel mechanisms:
- **Storage**: MongoDB only, using the same replica set (`rs0`) and database (`roleradar`) as Feature 001 ([ADR-0008](../../docs/adr/0008-mongodb-structured-job-search-storage.md)). Structured entities live in 9 dedicated collections: `companies`, `career_sources`, `postings`, `posting_signals`, `criteria`, `applications`, `application_events`, `contacts`, and `search_runs`.
- **Architectural Patterns**: Pure inward layering (`Domain` → `Application` → `Infrastructure` → `Mcp.Stdio` / `Cli`). Hand-written `ICommandHandler<TCommand, TResponse>` and `IQueryHandler<TQuery, TResponse>` wrapped transparently by `TransactionAndAuditCommandDecorator` inside multi-document ACID transactions, `AppendOnlyMongoCollection<T>` for append-only streams, typed `Result<T>` and `ErrorCode` without unhandled exceptions, and `ConnectionStringRedactor` ([ADR-0009](../../docs/adr/0009-pattern-reuse-and-typed-results-for-feature-002.md)).
- **Zero Hard Deletes**: No delete methods on any repository, no delete/drop CLI commands, and no delete tools exposed via MCP. Enforced by architecture reflection tests ([ADR-0010](../../docs/adr/0010-immutable-auditability-and-zero-hard-deletes.md)).
- **Deduplication**: Multi-tiered deduplication: Tier A (`ats_tenant` + `requisition_id`), Tier B (`normalized_url`), Tier C (`company_id` + `requisition_id`), and Tier D advisory match (`company_id` + `normalized_title`) returning typed `PossibleDuplicate` without auto-merging. Handled safely under concurrency via MongoDB partial unique indexes and transactional retry/re-read semantics.
- **MCP Surface Ceiling**: Exactly 6 new tools exposed on `RoleRadar.Mcp.Stdio` (`posting_record`, `posting_transition`, `company_record`, `application_record`, `contact_record`, `tracker_query`), bringing the total tool count to 12 (within the <=12 constitutional cap).
- **Contact Privacy**: Contact records isolated in `contacts`. Audit summaries contain strictly changed field names and IDs (never names, emails, roles, or notes). Explicit `redact: true` operation and zero personal data leakage into CLI status or logs.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`, SDK `10.0.402` via `global.json` with `rollForward: "latestPatch"`, `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, Central Package Management with lock files).

**Primary Dependencies (Live-Verified on NuGet.org)**:
- `ModelContextProtocol` `2.2.0`
- `Microsoft.Extensions.Hosting` `10.0.13`
- `MongoDB.Driver` `3.12.0`
- `System.CommandLine` `2.0.13`
- `FsCheck.Xunit` `3.1.0` (for property-based idempotence testing of URL and title normalizers)

**Storage**: Single-node MongoDB 8.0 replica set (`rs0`, image `mongo:8.0.34`) bound to `127.0.0.1:27017` in `deploy/docker-compose.yml`. Single transaction boundary, `owner_id` on every document, partial unique indexes enforcing deduplication tiers A, B, and C.

**Testing**:
- `xunit.v3` `4.0.2` + `Microsoft.NET.Test.Sdk` `18.11.0`
- `Testcontainers.MongoDb` `4.16.0` (ephemeral `mongo:8.0.34` replica set containers)
- `NetArchTest.Rules` `1.3.2` (inward layering + decorator registration + zero delete methods verification)
- `FsCheck.Xunit` `3.1.0` (property-based URL normalization tests)
- `coverlet.collector` `10.1.0` (>=85% line coverage gate across Domain and Application)

**Target Platform**: Windows 11 with PowerShell 7 (`pwsh`) developer workstation; GitHub Actions CI (`windows-latest` unit/arch/scripts + `ubuntu-latest` contract/integration with containers).

**Project Type**: Stdio MCP server (`RoleRadar.Mcp.Stdio`) + CLI tool (`RoleRadar.Cli`) + layered libraries (`Domain`, `Application`, `Infrastructure`).

**Performance Goals**:
- P95 query lookup latency < 100 ms across a benchmark dataset of 5,000 postings.
- P95 search / filter query latency < 200 ms across 5,000 postings.
- Re-encounter check for decided postings < 50 ms.

**Constraints**:
- Single MongoDB datastore; zero secondary databases.
- Agent-facing tool set capped at exactly 12 tools (6 existing + 6 new).
- Zero hard deletes anywhere across CLI, MCP, and repository layers.
- Ghost likelihood dynamically computed on read; never persisted in datastore or backups.
- Strictly fictional data in all test fixtures, seed data, and unit tests.

**Scale/Scope**: Single-owner tracking, ~100–500 companies, ~1,000–10,000 postings, point-in-time criteria historical versions, referential integrity verification across collections.

## Constitution Check (v1.1.0)

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle (Constitution v1.1.0) | Pre-Research Check | Post-Design Re-Check | Evidence / Enforcement |
|---------------------------------|--------------------|----------------------|------------------------|
| **I. Privacy First (NON-NEGOTIABLE)** | PASS | PASS | Contacts stored in separate collection; audit summaries record field names only; `redact: true` support; zero contact data in logs, status, or search; denylist check and fictional data in tests. |
| **II. Test-First Development (NON-NEGOTIABLE)** | PASS | PASS | Comprehensive test matrix: property tests (FsCheck for URL normalizer), table-driven state transition tests, concurrency race tests with Testcontainers, >=85% line coverage gate in CI. |
| **III. Test-Enforced Layering** | PASS | PASS | Pure Domain with zero driver/framework dependencies; Application depends only on Domain; Mongo repositories isolated in Infrastructure; Mcp and Cli adapters call handlers. NetArchTest verifies layering. |
| **IV. Named Seams Only** | PASS | PASS | Dedicated aggregate repository ports (`ITrackingRepository` or entity-specific ports); no generic `IRepository<T>`; handlers invoked directly without MediatR; typed `Result<T>` and `ErrorCode`. |
| **V. Agent Safety & Auditability** | PASS | PASS | Exactly 6 new MCP tools (12 total, <=12 cap); zero delete tools; append-only audit log and event streams via `TransactionAndAuditCommandDecorator` and `AppendOnlyMongoCollection<T>`. |
| **VI. stdio Protocol Hygiene** | PASS | PASS | Stdio transport only; zero network listeners; all logs to stderr; clean stdout JSON-RPC verified by automated test. |
| **VII. Data Durability & Safe Recovery** | PASS | PASS | Single MongoDB instance covered 100% by existing `mongodump` / `mongorestore` streaming libsodium AEAD backup/restore pipeline; no split-brain data loss. |
| **VIII. Recorded Decisions & Pinned Dependencies** | PASS | PASS | ADRs 0008, 0009, 0010 recorded under `docs/adr/` with updated index; dependencies pinned in CPM (`Directory.Packages.props`). |
| **IX. Baseline Technical Conventions** | PASS | PASS | .NET 10, C# nullable enable, TreatWarningsAsErrors=true, UTC ISO 8601 timestamps, `Guid.CreateVersion7()`, `GuidRepresentation.Standard`, `owner_id` on every collection. |

**Gate Evaluation**: **PASS** (Zero violations).

## Project Structure

### Documentation (this feature)

```text
specs/002-job-search-tracking/
├── spec.md                          # Feature specification & user stories
├── research.md                      # Phase 0 store decision, normalizers, rules, and MCP grouping
├── data-model.md                    # Phase 1 MongoDB collections, schemas, indexes, and state machine
├── quickstart.md                    # Phase 1 runnable step-by-step verification guide
├── contracts/
│   ├── mcp-tools.md                 # 6 new MCP stdio tools specification (12 total cap)
│   └── cli-commands.md              # roleradar verify & roleradar status extensions
├── checklists/
│   └── requirements.md              # Requirements quality checklist (16/16 PASS)
└── tasks.md                         # Phase 2 output (/speckit-tasks command)

docs/
└── adr/
    ├── README.md                    # ADR index
    ├── 0008-mongodb-structured-job-search-storage.md
    ├── 0009-pattern-reuse-and-typed-results-for-feature-002.md
    └── 0010-immutable-auditability-and-zero-hard-deletes.md
```

### Source Code (repository root)

```text
src/
├── RoleRadar.Domain/
│   ├── Common/                      # Entity base, OwnerId, UtcClock, Sha256Hasher
│   ├── Tracking/                    # Pure domain models, normalizers, and rules
│   │   ├── Company.cs               # Company profile, ownership type, category tags
│   │   ├── CareerSource.cs          # ATS portal endpoint, check interval, check runbook
│   │   ├── Posting.cs               # Posting aggregate, disposition, resurface policy, version token
│   │   ├── PostingDisposition.cs    # Disposition enum & pure table-driven transition matrix
│   │   ├── PostingSignal.cs         # Positive/negative/neutral signal item
│   │   ├── UrlNormalizer.cs         # Pure URL normalizer stripping tracking parameters
│   │   ├── TitleNormalizer.cs       # Pure title normalizer (lowercase, punctuation, whitespace)
│   │   ├── GhostLikelihoodRule.cs   # Dynamic on-read ghost calculator (never persisted)
│   │   ├── Criterion.cs             # Versioned screening criteria with effective dates
│   │   ├── Application.cs           # Job application aggregate, resume/portfolio variants
│   │   ├── ApplicationEvent.cs      # Timeline events (submitted, screening, interview, offer)
│   │   └── Contact.cs               # Confidential contact with privacy redaction support
│   ├── Notes/                       # Note aggregate (extended with company_id, posting_id, application_id)
│   └── Audit/                       # AuditLogEntry, ContactAuditContentSummary
├── RoleRadar.Application/
│   ├── Abstractions/                # ITrackingRepository, ICommandHandler, IQueryHandler, Result<T>, ErrorCode
│   ├── Tracking/
│   │   ├── Commands/                # RecordPosting, TransitionPosting, RecordCompany, RecordApplication, RecordContact
│   │   └── Queries/                 # TrackerQueryHandler (postings, decided, companies, criteria, contacts, integrity)
│   ├── Integrity/                   # Cross-collection referential integrity verification engine
│   └── DependencyInjection.cs       # Registers handlers and wraps commands with TransactionAndAuditCommandDecorator
├── RoleRadar.Infrastructure/
│   ├── Mongo/
│   │   ├── MongoTrackingRepository.cs # MongoDB implementation of ITrackingRepository
│   │   ├── TrackingIndexInitializer.cs# Partial unique indexes for dedup tiers A/B/C & tier D index
│   │   └── TrackingMongoOptions.cs  # Collection names and configuration
├── RoleRadar.Mcp.Stdio/
│   └── Tools/
│       ├── NoteMcpTools.cs          # Existing 6 tools (note_create, note_get, etc.)
│       └── TrackingMcpTools.cs      # 6 new tools (posting_record, posting_transition, company_record, application_record, contact_record, tracker_query)
└── RoleRadar.Cli/
    └── Commands/
        ├── VerifyCommand.cs         # roleradar verify referential integrity check
        └── StatusCommand.cs         # Extended roleradar status command reporting tracking aggregates

tests/
├── RoleRadar.Tests.Unit/
│   ├── Tracking/
│   │   ├── UrlNormalizerPropertyTests.cs # FsCheck property tests (idempotency, parameter stripping)
│   │   ├── TitleNormalizerTests.cs       # Case, punctuation, spacing normalizer tests
│   │   ├── DispositionMatrixTests.cs     # Exhaustive table-driven transition matrix unit tests
│   │   ├── GhostLikelihoodTests.cs       # Dynamic rule tests & persistence scan assert
│   │   └── ContactPrivacyTests.cs        # Verifies audit summaries contain field names only
├── RoleRadar.Tests.Architecture/
│   ├── LayeringTests.cs                  # NetArchTest inward dependency verification
│   ├── NoDeleteMethodsRuleTests.cs       # Reflection test: no delete/remove methods on repositories
│   └── McpToolSurfaceTests.cs            # Asserts tool cap <= 12 and zero delete tools
├── RoleRadar.Tests.Contract/
│   ├── TrackingRepositoryContractTests.cs# MongoDB replica set contract tests
│   └── PartialUniqueIndexTests.cs        # Tier A/B/C partial unique index collision and race handling
└── RoleRadar.Tests.Integration/
    ├── DeduplicationIntegrationTests.cs  # Parallel duplicate submission test
    ├── ReopenAndApplyOverrideTests.cs    # Atomic reopen-and-apply transaction test
    ├── CriteriaPointInTimeTests.cs       # Historical effective date query tests
    ├── IntegrityVerificationTests.cs     # Dangling pointer detection tests
    ├── Benchmark5000PostingsTests.cs     # 5,000-posting dataset latency benchmark (<100ms/<200ms)
    └── BackupRestoreTrackingTests.cs     # Backup and restore round-trip across all 9 collections
```

**Structure Decision**: Maintains the clean 5-project solution under `src/` and 4 test projects under `tests/`, expanding `Domain`, `Application`, and `Infrastructure` with tracking aggregates and queries while reusing the existing MongoDB infrastructure, transaction decorator, and streaming backup pipeline.

## Complexity Tracking

> No Constitution Check violations exist. All architectural decisions are recorded in `docs/adr/0008`–`0010`.
