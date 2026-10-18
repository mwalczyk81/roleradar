# Implementation Plan: Structured Job Search Tracking

**Branch**: `002-job-search-tracking` | **Date**: 2026-10-17 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/002-job-search-tracking/spec.md` (aligned with **Constitution v1.1.0**)

## Summary

Feature 002 introduces structured job-search tracking for a single owner, turning weekly search context, company ATS intelligence, screening decisions, and application pipelines into durable, queryable data. It prevents re-screening of previously decided postings, captures custom career portal check instructions, records applications and timeline events with an atomic reopen-and-apply override, revisions screening criteria over time, and protects confidential professional contacts with strict CLI-only isolation and privacy audit guards.

Technically, Feature 002 builds directly on Feature 001's architectural foundation without inventing parallel mechanisms:
- **Storage & Binary GUIDs**: MongoDB only, using the same replica set (`rs0`) and database (`roleradar`) as Feature 001 ([ADR-0008](../../docs/adr/0008-mongodb-structured-job-search-storage.md)). All `_id` fields, entity references (`company_id`, `posting_id`, etc.), note links, and optimistic `version_token` fields are stored and serialized as binary subtype 4 GUIDs (`GuidRepresentation.Standard`) matching Feature 001.
- **Architectural Patterns**: Pure inward layering (`Domain` → `Application` → `Infrastructure` → `Mcp.Stdio` / `Cli`). Hand-written `ICommandHandler<TCommand, TResponse>` and `IQueryHandler<TQuery, TResponse>` wrapped transparently by `TransactionAndAuditCommandDecorator` inside multi-document ACID transactions, `AppendOnlyMongoCollection<T>` for append-only streams, typed `Result<T>` and `ErrorCode` without unhandled exceptions, and `ConnectionStringRedactor` ([ADR-0009](../../docs/adr/0009-pattern-reuse-and-typed-results-for-feature-002.md)).
- **Zero Hard Deletes**: No delete methods on any repository, no delete/drop CLI commands, and no delete tools exposed via MCP. Enforced by architecture reflection tests ([ADR-0010](../../docs/adr/0010-immutable-auditability-and-zero-hard-deletes.md)).
- **Deduplication Tiers & Parallel-Submit Safety**: Multi-tiered deduplication using computed keys: Tier A (`dedup_key_a = tenant|requisition`), Tier B (`dedup_key_b = normalized_url`), Tier C (`dedup_key_c = companyId|requisition` when no tenant), each backed by a partial unique index (`$type: "string"`), and Tier D advisory non-unique index (`company_id` + `normalized_title`) returning typed `PossibleDuplicate` without auto-merging. Parallel submits catching duplicate key violations (`E11000`) re-read the winner inside the transaction to return `AlreadyDecided` or `AlreadyTracked`.
- **Posting Record Ingest vs Update Mode**: Ingest/dedup mode checks Tiers A–D. On duplicate match, stored fields other than `last_verified_at` are never changed; differing supplied values return in a `differences` list. Explicit update mode (`postingId` + `versionToken`) mutates only supplied fields, records changed field names in audit log, and rejects stale tokens with `StaleVersionToken`.
- **Career Sources Upsert & is_active**: Career sources are upserted by `id` or composite `(ats_type, tenant, slug)` with an `is_active` flag (default `true`); omission never deletes sources. Retired sources are excluded from queries unless `includeRetired` is true.
- **Resurface Policy & Decided Timestamp**: Default `resurface_policy` is `"never"`. Transitions to `screened_out`, `closed`, or `rejected` set `decided_at` (UTC). Stored policy controls re-transition back to `lead`.
- **Terminal Application Events**: Appending events to applications whose posting is already `rejected` or `closed` appends the event and audits it without mutating the posting disposition.
- **Point-in-Time Criteria & Deactivation**: `criterion_revision` enforces strict `effectiveFrom` sequencing and `versionToken` validation. `criterion_deactivate` closes active criteria without replacement. Search runs enforce unique `(owner_id, started_at)` returning `already_logged` on duplicate.
- **MCP Surface Ceiling**: Exactly 6 new tools exposed on `RoleRadar.Mcp.Stdio` (`posting_record`, `posting_transition`, `company_record`, `application_record`, `tracker_log`, `tracker_query`), bringing the total tool count to 12 (within the <=12 constitutional cap). 100% write coverage across MCP and CLI is verified by a requirement traceability matrix.
- **Contact Privacy**: Contact records isolated in `contacts` and managed exclusively via CLI (`roleradar contact add|get|redact`). No contact data is reachable through any MCP tool, log, error, or diagnostic output. Audit summaries contain strictly changed field names and IDs.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`, SDK `10.0.402` via `global.json` with `rollForward: "latestPatch"`, `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, Central Package Management with lock files).

**Primary Dependencies (Live-Verified on NuGet.org)**:
- `ModelContextProtocol` `2.2.0`
- `Microsoft.Extensions.Hosting` `10.0.13`
- `MongoDB.Driver` `3.12.0`
- `System.CommandLine` `2.0.13`
- `FsCheck.Xunit` `3.1.0` (for property-based idempotence testing of URL and title normalizers)

**Storage**: Single-node MongoDB 8.0 replica set (`rs0`, image `mongo:8.0.34`) bound to `127.0.0.1:27017` in `deploy/docker-compose.yml`. Single transaction boundary, `owner_id` on every document, binary subtype 4 GUIDs, partial unique indexes enforcing deduplication tiers A, B, and C.

**Testing**:
- `xunit.v3` `4.0.2` + `Microsoft.NET.Test.Sdk` `18.11.0`
- `Testcontainers.MongoDb` `4.16.0` (ephemeral `mongo:8.0.34` replica set containers)
- `NetArchTest.Rules` `1.3.2` (inward layering + decorator registration + zero delete methods + zero contact data references on MCP tools)
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
- Contacts management and referential integrity verification are strictly CLI-only.
- Zero hard deletes anywhere across CLI, MCP, and repository layers.
- Ghost likelihood dynamically computed on read; never persisted in datastore or backups.
- Strictly fictional data in all test fixtures, seed data, and unit tests.

**Scale/Scope**: Single-owner tracking, ~100–500 companies, ~1,000–10,000 postings, point-in-time criteria historical versions, referential integrity verification across collections.

## Constitution Check (v1.1.0)

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle (Constitution v1.1.0) | Pre-Research Check | Post-Design Re-Check | Evidence / Enforcement |
|---------------------------------|--------------------|----------------------|------------------------|
| **I. Privacy First (NON-NEGOTIABLE)** | PASS | PASS | Contacts CLI-only; audit summaries record field names only; `redact: true` support; zero contact data in logs, status, MCP, or search; denylist check and fictional data in tests. |
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
├── research.md                      # Phase 0 store decision, normalizers, rules, legacy fit check, and MCP grouping
├── data-model.md                    # Phase 1 MongoDB collections, schemas, indexes, and state machine
├── quickstart.md                    # Phase 1 runnable step-by-step verification guide
├── contracts/
│   ├── mcp-tools.md                 # 6 new MCP stdio tools specification + FR Traceability Matrix
│   └── cli-commands.md              # roleradar contact, roleradar verify & status extensions
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
│   │   ├── CareerSource.cs          # ATS portal endpoint, tenant, slug, fetch_method, is_active, notes
│   │   ├── Posting.cs               # Posting aggregate, computed dedup keys, score, culture, resurface policy, decided_at
│   │   ├── PostingDisposition.cs    # Disposition enum & pure table-driven transition matrix
│   │   ├── PostingSignal.cs         # Positive/negative/deadline signal item
│   │   ├── UrlNormalizer.cs         # Pure URL normalizer stripping tracking parameters
│   │   ├── TitleNormalizer.cs       # Pure title normalizer (lowercase, punctuation, whitespace)
│   │   ├── GhostLikelihoodRule.cs   # Dynamic on-read ghost calculator (never persisted)
│   │   ├── Criterion.cs             # Versioned screening criteria with effective dates and version token
│   │   ├── Application.cs           # Job application aggregate, numeric salary range, referral_contact_id
│   │   ├── ApplicationEvent.cs      # Timeline events (8 event types, offer non-terminal)
│   │   └── Contact.cs               # CLI-only contact with privacy redaction support
│   ├── Notes/                       # Note aggregate (extended with binary GUID company_id, posting_id, application_id)
│   └── Audit/                       # AuditLogEntry, ContactAuditContentSummary
├── RoleRadar.Application/
│   ├── Abstractions/                # IPostingRepository, ICompanyRepository, ICriterionRepository, IApplicationRepository, IContactRepository, ISearchRunRepository, ITrackingTransactionContext, ICommandHandler, IQueryHandler, Result<T>, ErrorCode
│   ├── Tracking/
│   │   ├── Commands/                # RecordPosting, TransitionPosting, RecordCompany, RecordApplication, TrackerLog, ContactCommands
│   │   └── Queries/                 # TrackerQueryHandler (postings, decided, posting_dedup_lookup, companies, criteria, applications)
│   ├── Integrity/                   # Cross-collection referential integrity verification engine
│   └── DependencyInjection.cs       # Registers handlers and wraps commands with TransactionAndAuditCommandDecorator
├── RoleRadar.Infrastructure/
│   ├── Mongo/
│   │   ├── Repositories/            # Mongo implementations of IPostingRepository, ICompanyRepository, etc.
│   │   ├── TrackingIndexInitializer.cs# Partial unique indexes for dedup_key_a/b/c & active criteria & career_sources & search_runs
│   │   └── TrackingMongoOptions.cs  # Collection names and configuration
├── RoleRadar.Mcp.Stdio/
│   └── Tools/
│       ├── NoteMcpTools.cs          # Existing 6 tools (note_create, note_get, etc.)
│       └── TrackingMcpTools.cs      # 6 new tools (posting_record, posting_transition, company_record, application_record, tracker_log, tracker_query)
└── RoleRadar.Cli/
    └── Commands/
        ├── ContactCommand.cs        # roleradar contact add|get|redact commands
        ├── VerifyCommand.cs         # roleradar verify referential integrity check
        └── StatusCommand.cs         # Extended roleradar status command reporting tracking aggregates

tests/
├── RoleRadar.Tests.Unit/
│   ├── Tracking/
│   │   ├── UrlNormalizerPropertyTests.cs      # FsCheck property tests (idempotency, parameter stripping)
│   │   ├── TitleNormalizerTests.cs            # Case, punctuation, spacing normalizer tests
│   │   ├── DispositionMatrixTests.cs          # Exhaustive table-driven transition matrix unit tests
│   │   ├── GhostLikelihoodTests.cs            # Dynamic rule tests & persistence scan assert
│   │   ├── CriterionVersionValidationTests.cs # EffectiveFrom sequencing & version token validation
│   │   └── ContactPrivacyTests.cs             # Verifies audit summaries contain field names only
├── RoleRadar.Tests.Architecture/
│   ├── LayeringTests.cs                       # NetArchTest inward dependency verification
│   ├── NoDeleteMethodsRuleTests.cs            # Reflection test: no delete/remove methods on repositories
│   ├── McpToolSurfaceTests.cs                 # Asserts tool cap <= 12 and zero delete tools
│   ├── McpZeroContactExposureTests.cs         # Asserts no MCP tool/parameter/description references contact data
│   └── TrackingArchitectureTests.cs           # Living architecture test file: tool counts, no-delete, contact isolation, decorator coverage
├── RoleRadar.Tests.Contract/
│   ├── TrackingRepositoryContractTests.cs     # MongoDB replica set contract tests
│   ├── PartialUniqueIndexTests.cs             # Tier A/B/C dedup key collisions and parallel race handling
│   ├── PostingRecordExplicitUpdateTests.cs    # Explicit update mode: partial mutation, versionToken, StaleVersionToken
│   ├── PostingRecordDifferencesListTests.cs   # Ingest mode dedup match: differences list, no field mutation
│   ├── CareerSourceUpsertTests.cs             # Upsert by id / composite key, is_active flag retirement/reactivation
│   ├── CriterionDeactivateTests.cs            # Deactivate active criterion without replacement, audit summary
│   ├── SearchRunDuplicateTests.cs             # Unique index on (owner_id, started_at), returns already_logged
│   └── DecidedPostingEventNonMutationTests.cs # Append event to decided/terminal posting preserves disposition
└── RoleRadar.Tests.Integration/
    ├── Smoke/
    │   └── StartupSmokeIntegrationTests.cs    # Process-based startup smoke test launching CLI and MCP DLLs with default config
    ├── Tracking/
    │   ├── DeduplicationIntegrationTests.cs   # Parallel duplicate submission test
    │   ├── ReopenAndApplyOverrideTests.cs     # Atomic reopen-and-apply transaction test
    │   ├── CriteriaPointInTimeTests.cs        # Historical effective date query tests
    │   ├── ContactCliPrivacyTests.cs          # Redaction, audit summary verification, and no-leak test
    │   ├── IntegrityVerificationTests.cs      # Dangling pointer detection tests
    │   └── Benchmark5000PostingsTests.cs      # 5,000-posting dataset latency benchmark (<100ms/<200ms)
    └── Backup/
        ├── TrackingBackupRestoreRoundTripTests.cs # Round-trip across all 9 collections & restore non-empty guard
        └── Legacy001RestoreCompatibilityTests.cs  # Restores 001-format archive into 002 build, idempotent indexes, pre-002 notes link deserialization
```

**Structure Decision**: Maintains the clean 5-project solution under `src/` and 4 test projects under `tests/`, expanding `Domain`, `Application`, and `Infrastructure` with tracking aggregates, computed deduplication keys, and queries while reusing the existing MongoDB infrastructure, transaction decorator, and streaming backup pipeline.

## Complexity Tracking

> No Constitution Check violations exist. All architectural decisions are recorded in `docs/adr/0008`–`0010`.
