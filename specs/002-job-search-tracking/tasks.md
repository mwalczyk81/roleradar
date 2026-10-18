# Tasks: Structured Job-Search Tracking

**Input**: Design documents from `/specs/002-job-search-tracking/`
**Prerequisites**: [plan.md](./plan.md) (required), [spec.md](./spec.md) (required for user stories), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)
**Tests**: Tests are MANDATORY per Constitution Principle II (Test-First Development). For every user story, test tasks MUST come first, be marked as expected to fail, and precede all implementation tasks.
**Organization**: Tasks are grouped by user story (`US1`–`US7`) to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (`[US1]`, `[US2]`, `[US3]`, `[US4]`, `[US5]`, `[US6]`, `[US7]`)
- Every task includes exact repository-relative file paths and quotes field constraints from `data-model.md` verbatim where applicable.

---

## Phase 1: Setup (Shared Test Infrastructure, Verification Scripts & Solution Extensions)

**Purpose**: Establish shared Testcontainers Mongo fixture extensions, isolated compose helpers, fictional seed fixtures, and requirements traceability script.

- [ ] T001 Setup shared Testcontainers Mongo fixture extensions and isolated Docker project helpers for contract and integration test suites in `tests/RoleRadar.Tests.Contract/Fixtures/SharedMongoFixture.cs` and `tests/RoleRadar.Tests.Integration/Fixtures/SharedMongoFixture.cs` ensuring tests reuse fixtures cleanly and never depend on the local developer compose stack
- [ ] T002 [P] Create strictly fictional tracking seed fixtures (companies, career sources, postings with Tiers A-D variants, criteria, applications, timeline events, and contacts) in `tests/fixtures/fictional-tracking-dataset.json` (strictly fictional data per FR-046)
- [ ] T003 [P] Implement automated requirements traceability matrix verification script `scripts/verify-traceability.ps1` that parses `specs/002-job-search-tracking/spec.md` to extract all 48 requirements (FR-001 through FR-046, FR-012a, FR-040a), verifies their presence in `specs/002-job-search-tracking/contracts/mcp-tools.md` with status PASS, and asserts that every requirement maps to a concrete MCP tool, CLI command, or automated test

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core domain value types, ErrorCode additions, normalizers with property tests, audit summary types, and MongoDB collections with partial unique indexes that MUST be complete before any user story can be implemented.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Foundational Tests (MANDATORY - Test-First per Principle II & III) ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [ ] T004 [P] Write failing unit and FsCheck property tests for `UrlNormalizer` and `TitleNormalizer` (tracking parameter denylist stripping `utm_*`, `ref`, `gh_src`, etc., protocol/host canonicalization, trailing slash removal, case/punctuation/whitespace folding, and property-based idempotence `Normalize(Normalize(u)) == Normalize(u)`) in `tests/RoleRadar.Tests.Unit/Tracking/NormalizerPropertyAndUnitTests.cs` (expected to fail before implementation)
- [ ] T005 [P] Write failing domain unit tests for tracking enums, domain primitives, `ContactAuditContentSummary` field-names-only privacy invariant, and typed `Result<T>` error codes in `tests/RoleRadar.Tests.Unit/Tracking/TrackingDomainAndAuditTests.cs` (expected to fail before implementation)
- [ ] T006 [P] Write failing MongoDB bootstrap and index initializer contract tests using Testcontainers verifying partial unique indexes for `dedup_key_a`, `dedup_key_b`, `dedup_key_c`, active criteria `{ owner_id: 1, key: 1 }` where `is_active == true`, career sources `{ owner_id: 1, company_id: 1, ats_type: 1, tenant: 1, slug: 1 }`, and unique index on `search_runs { owner_id: 1, started_at: 1 }` in `tests/RoleRadar.Tests.Contract/TrackingIndexInitializerContractTests.cs` (expected to fail before implementation)

### Foundational Implementation

- [ ] T007 [P] Update `ErrorCode` enum in `src/RoleRadar.Application/Abstractions/Result.cs` adding genuinely new codes `InvalidTransition`, `AlreadyDecided`, `AlreadyTracked`, and `PossibleDuplicate`, explicitly reusing existing `StaleVersionToken` for all optimistic concurrency, missing, or mismatched version token cases (depends on T005 failing first)
- [ ] T008 [P] Implement domain value types, enums, and primitives (`OwnershipType`, `CompanyStatus`, `CultureLevel`, `PostingDisposition`, `ResurfacePolicy`, `SourceKind`, `RemoteType`, `FetchMethod`, `CriterionCategory`, `ProvenanceKind`, `ResumeVariant`, `ApplicationOutcome`, `ApplicationEventType`, `SignalType`, `ContactRelationship`) in `src/RoleRadar.Domain/Tracking/TrackingEnums.cs` and `src/RoleRadar.Domain/Tracking/TrackingPrimitives.cs` (depends on T005 failing first)
- [ ] T009 [P] Implement pure `UrlNormalizer` with tracking parameter denylist (`utm_*`, `ref`, `ref_*`, `source`, `gh_src`, `src`, `source_id`, `fbclid`, `gclid`, `gclsrc`, `dclid`, `msclkid`, `twclid`, `trk`, `trackingId`, `tracking_id`, `li_fat_id`, `mc_cid`, `mc_eid`, `_hsenc`, `_hsmi`, `hsCtaTracking`, `igshid`, `yclid`, `mkt_tok`) and `TitleNormalizer` in `src/RoleRadar.Domain/Tracking/UrlNormalizer.cs` and `src/RoleRadar.Domain/Tracking/TitleNormalizer.cs` (depends on T004 failing first)
- [ ] T010 [P] Implement typed audit summary domain records (`PostingAuditSummary`, `CompanyAuditSummary`, `CriterionAuditSummary`, `ApplicationAuditSummary`, and `ContactAuditContentSummary` strictly containing changed field names and record IDs without names, roles, or company text) in `src/RoleRadar.Domain/Audit/TrackingAuditSummaries.cs` (depends on T005 failing first)
- [ ] T011 [P] Implement `ITrackingRepository` port and transaction context abstraction in `src/RoleRadar.Application/Abstractions/ITrackingRepository.cs` and `src/RoleRadar.Application/Abstractions/ITrackingTransactionContext.cs`
- [ ] T012 Implement MongoDB global binary subtype 4 GUID mapping, collections options (`companies`, `career_sources`, `postings`, `posting_signals`, `criteria`, `applications`, `application_events`, `contacts`, `search_runs`), and idempotent index initialization in `src/RoleRadar.Infrastructure/Mongo/TrackingMongoOptions.cs`, `src/RoleRadar.Infrastructure/Mongo/TrackingIndexInitializer.cs`, and `src/RoleRadar.Infrastructure/Mongo/MongoTrackingRepository.cs` (depends on T006 failing first and T007, T008, T010, T011)
- [ ] T013 Register tracking repository, readiness probes, and command decorator bindings in `src/RoleRadar.Infrastructure/DependencyInjection.cs` and `src/RoleRadar.Application/DependencyInjection.cs`

**Checkpoint**: Foundation ready — normalizer property tests, domain tests, and index initializer contract tests compile and pass. User story implementation can begin.  
*Commit*: `feat(foundation): add tracking data types, indexes, normalizers, and repository abstractions`

---

## Phase 3: User Story 1 - Postings with Dispositions and Tiered Deduplication (Priority: P1) 🎯 MVP

**Goal**: Record and update job postings with full tracking metadata, enforcing deduplication Tiers A through D, collapsing duplicate reference chains at write time, supporting explicit update mode with version tokens, returning differing supplied values in a `differences` list on dedup match, and remembering screening decisions with resurface policies.

**Independent Test**: Submit postings with varying URLs and ATS identifiers; verify Tier A, B, and C deduplication matches existing records; verify Tier D advisory matching returns `PossibleDuplicate` without mutating data; re-encounter decided postings and verify `ErrorCode.AlreadyDecided` with `can_reopen` indicator; update postings via explicit update mode (`postingId` + `versionToken`) and verify partial field mutation.

### Tests for User Story 1 (MANDATORY - Test-First per Principle II) ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [ ] T014 [P] [US1] Write failing contract test `PostingRecordContractTests.cs` for tool `posting_record` covering create (defaulting to `lead`), Tier A match (`dedup_key_a`), Tier B match (`dedup_key_b`), Tier C match (`dedup_key_c`), Tier D advisory match returning `PossibleDuplicate` candidate list without record modification, in-place ATS identifier upgrade, duplicate reference chain collapsing, and differences list return on non-matching supplied values in `tests/RoleRadar.Tests.Contract/PostingRecordContractTests.cs` (expected to fail before implementation)
- [ ] T015 [P] [US1] Write failing contract test `PostingTransitionContractTests.cs` for tool `posting_transition` covering valid transitions, rejection of `applied` target with `ErrorCode.InvalidTransition`, re-encounter of `screened_out`/`closed`/`rejected` postings returning `ErrorCode.AlreadyDecided` with `can_reopen` indicator, and re-encounter of active postings returning `ErrorCode.AlreadyTracked` with audited `last_verified_at` bump across UTC days vs same-day zero-write no-op in `tests/RoleRadar.Tests.Contract/PostingTransitionContractTests.cs` (expected to fail before implementation)
- [ ] T016 [P] [US1] Write failing unit test `DispositionMatrixTests.cs` verifying the exhaustive table-driven disposition state transition matrix in `tests/RoleRadar.Tests.Unit/Tracking/DispositionMatrixTests.cs` (expected to fail before implementation)
- [ ] T017 [P] [US1] Write failing contract test `PostingRecordExplicitUpdateTests.cs` covering explicit update mode (`postingId` + `versionToken`), mutating only supplied fields, rotating `version_token`, logging changed field names in audit log, and returning `ErrorCode.StaleVersionToken` on version token mismatch in `tests/RoleRadar.Tests.Contract/PostingRecordExplicitUpdateTests.cs` (expected to fail before implementation)

### Implementation for User Story 1

- [ ] T018 [P] [US1] Implement `Posting` aggregate root in `src/RoleRadar.Domain/Tracking/Posting.cs`, enforcing `data-model.md` constraints verbatim:
  - `_id`: Required binary subtype 4 GUID
  - `owner_id`: Required string
  - `company_id`: Required binary GUID reference to `companies`
  - `title`: Required string, 1..200 chars
  - `normalized_title`: Computed lowercase alphanumeric-clean string
  - `original_url`: Optional string, max 1000 chars
  - `normalized_url`: Optional string, max 1000 chars (stripped of tracking params)
  - `source_kind`: Required string enum (`ats`, `aggregator`)
  - `ats_tenant`: Optional string, max 100 chars
  - `requisition_id`: Optional string, max 100 chars
  - `dedup_key_a`: Computed string, `"{ats_tenant}|{requisition_id}"` when both present; null otherwise
  - `dedup_key_b`: Computed string, `normalized_url` when present; null otherwise
  - `dedup_key_c`: Computed string, `"{company_id}|{requisition_id}"` when requisition_id is present but ats_tenant is absent/null; null otherwise
  - `location`: Optional string, max 150 chars
  - `remote_type`: String enum (`remote`, `hybrid`, `onsite`, `unknown`, default: `unknown`)
  - `compensation`: Subdocument with `min` (decimal?), `max` (decimal?), `currency` (string, 3 chars, default: `USD`), `text` (string, max 300 chars)
  - `posted_date`: Optional ISO 8601 date-only `YYYY-MM-DD` string
  - `first_seen_at` and `last_verified_at`: Required UTC timestamps
  - `closed_date`: Optional ISO 8601 date-only `YYYY-MM-DD` string
  - `score`: Optional decimal validated `0.0 <= score <= 10.0`
  - `culture_level`: String enum (`stable`, `moderate`, `high_risk`, `unknown`, default: `unknown`)
  - `culture_summary`: Optional string, max 500 chars
  - `disposition`: String enum (`lead`, `scored`, `applied`, `rejected`, `screened_out`, `closed`, `unscored`, default: `lead`)
  - `disposition_reason`: Optional string, max 500 chars
  - `disposition_criterion_id`: Optional binary subtype 4 GUID reference to `criteria`
  - `resurface_policy`: String enum (`never`, `if_reopened`, default: `never`)
  - `decided_at`: Optional UTC timestamp set on transition into `screened_out`, `closed`, or `rejected`
  - `duplicate_of_id`: Optional binary subtype 4 GUID (cannot equal `_id`; duplicate chains collapsed upon write)
  - `screening_note_id`: Optional binary subtype 4 GUID reference to `notes`
  - `version_token`: Required binary subtype 4 GUID rotated upon every write
- [ ] T019 [P] [US1] Implement `RecordPostingCommand`, `RecordPostingCommandHandler`, and DTOs in `src/RoleRadar.Application/Tracking/Commands/RecordPostingCommand.cs` handling Tiers A-D dedup, in-place identifier upgrade, duplicate chain collapsing, differences list return on non-matching values, same-day no-op vs different-day `last_verified_at` bump with audit entry, and explicit update mode (`postingId` + `versionToken`) (depends on T014, T017 failing first and T018)
- [ ] T020 [P] [US1] Implement `TransitionPostingCommand`, `TransitionPostingCommandHandler`, and DTOs in `src/RoleRadar.Application/Tracking/Commands/TransitionPostingCommand.cs` enforcing the transition matrix, rejecting target `applied` (`ErrorCode.InvalidTransition`), evaluating stored resurface policy, and setting `decided_at` (depends on T015, T016 failing first and T018)
- [ ] T021 [US1] Implement MCP tools `posting_record` and `posting_transition` in `src/RoleRadar.Mcp.Stdio/Tools/TrackingMcpTools.cs` and register handlers wrapped by `TransactionAndAuditCommandDecorator` in `src/RoleRadar.Application/DependencyInjection.cs` (depends on T014, T015, T017 failing first and T019, T020)

**Checkpoint**: User Story 1 (MVP) is complete and independently verified by `PostingRecordContractTests`, `PostingTransitionContractTests`, `DispositionMatrixTests`, and `PostingRecordExplicitUpdateTests`.  
*Commit*: `feat(postings): implement posting_record and posting_transition with tiered dedup and update mode`

---

## Phase 4: User Story 2 - Companies and Career Sources (Priority: P1)

**Goal**: Track target employers (with bounded 300-char descriptions and ownership types) and career sources (ATS configuration, fetch method, and `is_active` lifecycle), enabling automated searches to know how and where to check for postings.

**Independent Test**: Create a company with description and ownership type; upsert career sources by ID and composite key; update `isActive` to retire a source without hard deletion; verify query filters exclude retired sources unless `includeRetired` is true.

### Tests for User Story 2 (MANDATORY - Test-First per Principle II) ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [ ] T022 [P] [US2] Write failing contract test `CompanyRecordContractTests.cs` for tool `company_record` covering company creation, description up to 300 chars, career sources upsert by `id` or composite `(ats_type, tenant, slug)`, `is_active` flag retirement and reactivation without deletion, and omission of sources leaving existing sources untouched in `tests/RoleRadar.Tests.Contract/CompanyRecordContractTests.cs` (expected to fail before implementation)
- [ ] T023 [P] [US2] Write failing unit tests for `Company` and `CareerSource` invariants, ownership enum parsing, and bounded description limits in `tests/RoleRadar.Tests.Unit/Tracking/CompanyDomainTests.cs` (expected to fail before implementation)

### Implementation for User Story 2

- [ ] T024 [P] [US2] Implement `Company` aggregate root and `CareerSource` entity in `src/RoleRadar.Domain/Tracking/Company.cs` and `src/RoleRadar.Domain/Tracking/CareerSource.cs`, enforcing `data-model.md` constraints verbatim:
  - `Company.name`: Required string, 1..150 chars
  - `Company.normalized_name`: Computed lowercase trimmed string
  - `Company.description`: Optional string, max 300 chars (covers industry/ownership/HQ context)
  - `Company.parent_company`: Optional string, max 150 chars
  - `Company.ownership_type`: String enum (`public`, `private`, `pe_backed`, `vc_backed`, `mutual`, `subsidiary`, `nonprofit`, `government`, `unknown`)
  - `Company.headquarters`: Optional string, max 150 chars
  - `Company.category_tags`: Array of strings, max 20 tags, each max 50 chars
  - `Company.status`: String enum (`target`, `watch`, `avoid`, `neutral`, default: `neutral`)
  - `Company.last_checked_at`: Optional UTC timestamp
  - `Company.last_check_result`: Optional string, max 300 chars
  - `Company.version_token`: Required binary subtype 4 GUID
  - `CareerSource.ats_type`: String enum (`Workday`, `Greenhouse`, `Ashby`, `Lever`, `Avature`, `Oracle`, `UKG`, `Custom`)
  - `CareerSource.tenant`: Optional string, max 100 chars
  - `CareerSource.slug`: Optional string, max 150 chars
  - `CareerSource.fetch_method`: String enum (`api`, `browser`, `blocked`)
  - `CareerSource.notes`: Optional string, max 500 chars
  - `CareerSource.is_active`: Required boolean, default true
  - `CareerSource.version_token`: Required binary subtype 4 GUID
- [ ] T025 [P] [US2] Implement `RecordCompanyCommand`, `RecordCompanyCommandHandler`, and DTOs in `src/RoleRadar.Application/Tracking/Commands/RecordCompanyCommand.cs` (upserting career sources by id or composite key, handling `isActive` input parameter) (depends on T022 failing first and T024)
- [ ] T026 [US2] Implement MCP tool `company_record` in `src/RoleRadar.Mcp.Stdio/Tools/TrackingMcpTools.cs` and register handler wrapped by `TransactionAndAuditCommandDecorator` in `src/RoleRadar.Application/DependencyInjection.cs` (depends on T022 failing first and T025)

**Checkpoint**: User Story 2 is complete and independently verified by `CompanyRecordContractTests` and `CompanyDomainTests`.  
*Commit*: `feat(companies): implement company_record with career source upsert and is_active lifecycle`

---

## Phase 5: User Story 3 - Criteria with Revision History (Priority: P1)

**Goal**: Maintain immutable, versioned screening criteria with strict `effectiveFrom` sequencing and version token concurrency control, support deactivating criteria without replacement, and enable point-in-time criteria evaluation.

**Independent Test**: Insert a criterion effective on date T1; supersede it with a revision on date T2; assert missing version token returns `ErrorCode.StaleVersionToken`; deactivate a criterion via `criterion_deactivate`; query criteria as of date T1 and date T2 to verify historical point-in-time resolution.

### Tests for User Story 3 (MANDATORY - Test-First per Principle II) ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [ ] T027 [P] [US3] Write failing contract test `TrackerLogCriteriaContractTests.cs` for tool `tracker_log` covering action `criterion_revision` (strict `effectiveFrom` sequencing validation, closing active version, rotating version token), `ErrorCode.StaleVersionToken` on missing/mismatched version token, and action `criterion_deactivate` (closing active version without replacement) in `tests/RoleRadar.Tests.Contract/TrackerLogCriteriaContractTests.cs` (expected to fail before implementation)
- [ ] T028 [P] [US3] Write failing unit tests for `Criterion` point-in-time validity and date sequencing in `tests/RoleRadar.Tests.Unit/Tracking/CriterionVersionValidationTests.cs` (expected to fail before implementation)

### Implementation for User Story 3

- [ ] T029 [P] [US3] Implement `Criterion` aggregate root in `src/RoleRadar.Domain/Tracking/Criterion.cs`, enforcing `data-model.md` constraints verbatim:
  - `_id`: Required binary subtype 4 GUID
  - `owner_id`: Required string
  - `key`: Required string, max 100 chars
  - `category`: String enum (`hard_skip`, `weight`, `preference`)
  - `value`: Required string, max 500 chars
  - `effective_from`: Required UTC timestamp
  - `effective_to`: Optional UTC timestamp (null for active)
  - `provenance`: String enum (`stated`, `inferred`)
  - `is_active`: Required boolean
  - `version_token`: Required binary subtype 4 GUID
- [ ] T030 [P] [US3] Implement `LogCriterionRevisionCommand`, `DeactivateCriterionCommand`, and handlers in `src/RoleRadar.Application/Tracking/Commands/CriterionCommands.cs` (enforcing strict `effectiveFrom` validation, closing active record, requiring `versionToken` when active exists) (depends on T027, T028 failing first and T029)
- [ ] T031 [US3] Implement `tracker_log` tool actions `criterion_revision` and `criterion_deactivate` in `src/RoleRadar.Mcp.Stdio/Tools/TrackingMcpTools.cs` and register handlers wrapped by `TransactionAndAuditCommandDecorator` in `src/RoleRadar.Application/DependencyInjection.cs` (depends on T027 failing first and T030)

**Checkpoint**: User Story 3 is complete and independently verified by `TrackerLogCriteriaContractTests` and `CriterionVersionValidationTests`.  
*Commit*: `feat(criteria): implement immutable criteria revisioning and deactivation in tracker_log`

---

## Phase 6: User Story 4 - Applications and Interaction Timeline (Priority: P2)

**Goal**: Track formal job applications with resume variant pointers and numeric salary ranges, support an atomic reopen-and-apply override for decided roles, synchronize posting dispositions on application events, and record timeline events (accepting events on terminal postings without mutating disposition).

**Independent Test**: Create an application from `lead` moving posting to `applied`; attempt to apply to `screened_out` posting without override (assert `ErrorCode.InvalidTransition`); apply with `overrideDecided: true` (assert atomic transition to `applied` with audit record); append timeline events (including `rejected` and non-terminal `offer`); append event to already closed posting and verify event appends without error or disposition change.

### Tests for User Story 4 (MANDATORY - Test-First per Principle II) ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [ ] T032 [P] [US4] Write failing contract test `ApplicationRecordContractTests.cs` for tool `application_record` covering application creation from `lead`, `unscored`, and `scored` (moving posting to `applied`), atomic reopen-and-apply override on `screened_out`/`closed` posting in ONE audited transaction, rejection without override flag (`ErrorCode.InvalidTransition`), action `append_event` with 8 event types, and appending events to already decided/terminal postings without mutating disposition in `tests/RoleRadar.Tests.Contract/ApplicationRecordContractTests.cs` (expected to fail before implementation)
- [ ] T033 [P] [US4] Write failing unit tests for `Application` and `ApplicationEvent` domain invariants, numeric salary validation (`min >= 0`, `max >= min`), resume variant pointers (forbidding raw resume text), and outcome synchronization in `tests/RoleRadar.Tests.Unit/Tracking/ApplicationDomainTests.cs` (expected to fail before implementation)

### Implementation for User Story 4

- [ ] T034 [P] [US4] Implement `Application` aggregate root and `ApplicationEvent` entity in `src/RoleRadar.Domain/Tracking/Application.cs` and `src/RoleRadar.Domain/Tracking/ApplicationEvent.cs`, enforcing `data-model.md` constraints verbatim:
  - `Application._id`: Required binary subtype 4 GUID
  - `Application.owner_id`: Required string
  - `Application.posting_id`: Required binary subtype 4 GUID reference to `postings`
  - `Application.submitted_date`: Required date-only `YYYY-MM-DD` string
  - `Application.desired_salary`: Subdocument with `min` (decimal?, >= 0), `max` (decimal?, >= min), `currency` (string, 3 chars, default: `USD`)
  - `Application.target_start_date`: Optional date-only `YYYY-MM-DD` string
  - `Application.resume_variant`: Required string enum (`tailored`, `as_is`, `ic_framed`)
  - `Application.resume_pointer`: Optional string, max 300 chars (raw resume content strictly forbidden)
  - `Application.referral_contact_id`: Optional binary subtype 4 GUID reference to `contacts`
  - `Application.confirmation_code`: Optional string, max 100 chars
  - `Application.outcome`: String enum (`applied`, `rejected`, `closed`, default: `applied`)
  - `Application.outcome_reason`: Optional string, max 500 chars
  - `Application.version_token`: Required binary subtype 4 GUID
  - `ApplicationEvent._id`: Required binary subtype 4 GUID
  - `ApplicationEvent.owner_id`: Required string
  - `ApplicationEvent.application_id`: Required binary subtype 4 GUID reference to `applications`
  - `ApplicationEvent.event_date`: Required date-only `YYYY-MM-DD` string
  - `ApplicationEvent.event_type`: Required string enum (`applied`, `follow_up`, `interview`, `offer`, `offer_accepted`, `offer_declined`, `rejected`, `withdrawn`), with `offer` non-terminal
  - `ApplicationEvent.event_source`: Optional string, max 100 chars
  - `ApplicationEvent.notes`: Optional string, max 500 chars
- [ ] T035 [P] [US4] Implement `RecordApplicationCommand`, `AppendApplicationEventCommand`, and handlers in `src/RoleRadar.Application/Tracking/Commands/ApplicationCommands.cs` (reopen-and-apply atomic override, disposition synchronization, accepting events on terminal postings) (depends on T032, T033 failing first and T034)
- [ ] T036 [US4] Implement MCP tool `application_record` in `src/RoleRadar.Mcp.Stdio/Tools/TrackingMcpTools.cs` and register handlers wrapped by `TransactionAndAuditCommandDecorator` in `src/RoleRadar.Application/DependencyInjection.cs` (depends on T032 failing first and T035)

**Checkpoint**: User Story 4 is complete and independently verified by `ApplicationRecordContractTests` and `ApplicationDomainTests`.  
*Commit*: `feat(applications): implement application_record with atomic reopen-and-apply and timeline events`

---

## Phase 7: User Story 5 - Posting Signals and Ghost Likelihood (Priority: P2)

**Goal**: Record observable risk/quality signals against job postings (including optional deadline dates) and dynamically compute ghost likelihood on read without persisting scores to storage.

**Independent Test**: Attach signals (`gone`, `repost`, `deadline`) to a posting; verify dynamic ghost score is calculated correctly on retrieval; assert via raw Mongo document inspection and decrypted backup archives that zero `ghost` or `ghost_likelihood` fields are persisted in the datastore.

### Tests for User Story 5 (MANDATORY - Test-First per Principle II) ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [ ] T037 [P] [US5] Write failing unit tests for `GhostLikelihoodRule` covering active signal weights (`gone` +50%, `phantom` +40%, `deadline` passed +30%, `repost` +25%, `parallel_posting` +20%, `no_salary` +15%, age >60d +20%, age >90d +35%, freshness <7d -15%), clamping 0..100, and categorization (Low 0..24, Moderate 25..49, High 50..74, Critical 75..100) in `tests/RoleRadar.Tests.Unit/Tracking/GhostLikelihoodRuleTests.cs` (expected to fail before implementation)
- [ ] T038 [P] [US5] Write failing contract test `PostingSignalContractTests.cs` verifying append of posting signals and optional `deadline_date` in `tests/RoleRadar.Tests.Contract/PostingSignalContractTests.cs` (expected to fail before implementation)
- [ ] T039 [P] [US5] Write failing integration test `GhostLikelihoodPersistenceScanIntegrationTests.cs` verifying ghost likelihood is dynamically computed on read, and asserting via raw BSON collection inspection and decrypted backup scan that zero `ghost` or `ghost_likelihood` fields are persisted in `tests/RoleRadar.Tests.Integration/Tracking/GhostLikelihoodPersistenceScanIntegrationTests.cs` (expected to fail before implementation)

### Implementation for User Story 5

- [ ] T040 [P] [US5] Implement `PostingSignal` entity in `src/RoleRadar.Domain/Tracking/PostingSignal.cs` with `signal_type` (`repost`, `no_salary`, `parallel_posting`, `gone`, `phantom`, `deadline`), `evidence` (mandatory, max 500 chars), `deadline_date` (optional date-only `YYYY-MM-DD`), and `observed_at` (UTC timestamp) (depends on T038 failing first)
- [ ] T041 [P] [US5] Implement `GhostLikelihoodRule` domain service in `src/RoleRadar.Domain/Tracking/GhostLikelihoodRule.cs` computing dynamic score strictly on read (depends on T037, T039 failing first and T040)
- [ ] T042 [US5] Integrate signal ingestion in `RecordPostingCommandHandler` and dynamic ghost assessment in `TrackerQueryHandler` in `src/RoleRadar.Application/Tracking/Commands/RecordPostingCommand.cs` and `src/RoleRadar.Application/Tracking/Queries/TrackerQuery.cs` (depends on T038, T039 failing first and T040, T041)

**Checkpoint**: User Story 5 is complete and independently verified by `GhostLikelihoodRuleTests`, `PostingSignalContractTests`, and `GhostLikelihoodPersistenceScanIntegrationTests`.  
*Commit*: `feat(signals): implement posting signals and dynamic on-read ghost likelihood calculation`

---

## Phase 8: User Story 6 - Confidential Contacts Isolation (CLI-Only) (Priority: P2)

**Goal**: Manage professional contacts exclusively via CLI (`roleradar contact add|get|redact`), completely isolated from MCP, general queries, logs, and diagnostics, with privacy redaction and field-names-only audit summaries.

**Independent Test**: Add a contact via CLI; verify contact is retrievable solely by direct ID; verify MCP queries reject contacts; redact contact and verify personal fields are blanked while ID and audit history remain; run no-leak scanner across logs, diagnostics, status, and backups to verify zero contact details leak.

### Tests for User Story 6 (MANDATORY - Test-First per Principle II) ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [ ] T043 [P] [US6] Write failing contract test `ContactCliContractTests.cs` for CLI commands `roleradar contact add|get|redact` covering creation with name, role, company, relationship (`recruiter`, `referral`, `hiring_manager`, `peer`), retrieval strictly by contact ID, and `redact` blanking personal fields while preserving ID and auditing with field-names-only summary in `tests/RoleRadar.Tests.Contract/ContactCliContractTests.cs` (expected to fail before implementation)
- [ ] T044 [P] [US6] Write failing unit test `ContactPrivacyTests.cs` proving audit summaries contain exclusively changed field names and record IDs, and zero contact data is present in `ContactAuditContentSummary` in `tests/RoleRadar.Tests.Unit/Tracking/ContactPrivacyTests.cs` (expected to fail before implementation)
- [ ] T045 [P] [US6] Write failing architecture test `McpZeroContactExposureTests.cs` using NetArchTest asserting that no MCP tool class, method, parameter, or tool description references `Contact` types or exposes contact data in `tests/RoleRadar.Tests.Architecture/McpZeroContactExposureTests.cs` (expected to fail before implementation)
- [ ] T046 [P] [US6] Write failing integration test `ContactNoLeakScannerIntegrationTests.cs` creating a contact via CLI, triggering error conditions, status checks, search queries, and backup archive dumps, and scanning all outputs to assert zero leaks of personal contact information in `tests/RoleRadar.Tests.Integration/Tracking/ContactNoLeakScannerIntegrationTests.cs` (expected to fail before implementation)

### Implementation for User Story 6

- [ ] T047 [P] [US6] Implement `Contact` aggregate root in `src/RoleRadar.Domain/Tracking/Contact.cs`, enforcing `data-model.md` constraints verbatim:
  - `_id`: Required binary subtype 4 GUID
  - `owner_id`: Required string
  - `name`: Required string, 1..150 chars (set to `"[REDACTED]"` upon redaction)
  - `role`: Optional string, max 150 chars (null upon redaction)
  - `company_affiliation`: Optional string, max 150 chars (null upon redaction)
  - `company_id`: Optional binary subtype 4 GUID reference to `companies` (null upon redaction)
  - `relationship`: String enum (`recruiter`, `referral`, `hiring_manager`, `peer`, `redacted`)
  - `notes`: Optional string, max 500 chars (null upon redaction)
  - `is_redacted`: Boolean, default false (true upon redaction)
  - `version_token`: Required binary subtype 4 GUID
- [ ] T048 [P] [US6] Implement `AddContactCommand`, `GetContactQuery`, `RedactContactCommand`, and handlers in `src/RoleRadar.Application/Tracking/Commands/ContactCommands.cs` and `src/RoleRadar.Application/Tracking/Queries/GetContactQuery.cs` (depends on T043, T044 failing first and T047)
- [ ] T049 [US6] Implement CLI command `roleradar contact add|get|redact` in `src/RoleRadar.Cli/Commands/ContactCommand.cs` and register handlers in `src/RoleRadar.Application/DependencyInjection.cs` and CLI root in `src/RoleRadar.Cli/Program.cs` (depends on T043, T045, T046 failing first and T048)

**Checkpoint**: User Story 6 is complete and independently verified by `ContactCliContractTests`, `ContactPrivacyTests`, `McpZeroContactExposureTests`, and `ContactNoLeakScannerIntegrationTests`.  
*Commit*: `feat(contacts): implement CLI-only confidential contact management with privacy redaction`

---

## Phase 9: User Story 7 - Search Runs and Referential Integrity Verification (Priority: P3)

**Goal**: Record search run execution milestones linked to report notes, support deduplication on `(owner_id, started_at)`, extend `Note` entities with backward-compatible links, and provide a read-only referential integrity verification CLI command (`roleradar verify`) that flags dangling pointers across all collections.

**Independent Test**: Log a search run via `tracker_log`; submit duplicate search run and assert `status: "already_logged"` without duplicate insert; create notes referencing entities; intentionally introduce a dangling company ID in a test posting; run `roleradar verify` and verify that the anomaly is reported cleanly without data modification.

### Tests for User Story 7 (MANDATORY - Test-First per Principle II) ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [ ] T050 [P] [US7] Write failing contract test `TrackerLogSearchRunContractTests.cs` for tool `tracker_log (action: "search_run")` covering logging start timestamp, new postings count, report note link, and deduplication returning `status: "already_logged"` on duplicate `(owner_id, started_at)` without write or audit entry in `tests/RoleRadar.Tests.Contract/TrackerLogSearchRunContractTests.cs` (expected to fail before implementation)
- [ ] T051 [P] [US7] Write failing contract test `VerifyCliContractTests.cs` for CLI command `roleradar verify` verifying read-only detection of dangling company references, dangling posting references, dangling `duplicate_of` links, and dangling note entity links in `tests/RoleRadar.Tests.Contract/VerifyCliContractTests.cs` (expected to fail before implementation)
- [ ] T052 [P] [US7] Write failing contract test `StatusCliContractTests.cs` verifying extended `roleradar status` reporting entity counts for companies, postings, criteria, applications, and contacts (with redacted counts and zero personal data) in `tests/RoleRadar.Tests.Contract/StatusCliContractTests.cs` (expected to fail before implementation)

### Implementation for User Story 7

- [ ] T053 [P] [US7] Extend `Note` aggregate root in `src/RoleRadar.Domain/Notes/Note.cs` with optional binary subtype 4 GUID link fields `company_id`, `posting_id`, `application_id`, and update MongoDB partial indexes in `src/RoleRadar.Infrastructure/Mongo/MongoBootstrapAndIndexes.cs` (backward-compatible)
- [ ] T054 [P] [US7] Implement `SearchRun` entity and `LogSearchRunCommand` in `src/RoleRadar.Domain/Tracking/SearchRun.cs` and `src/RoleRadar.Application/Tracking/Commands/LogSearchRunCommand.cs` (enforcing unique index on `owner_id, started_at` and returning `already_logged`) (depends on T050 failing first)
- [ ] T055 [P] [US7] Implement `VerifyIntegrityQuery` and `VerifyIntegrityQueryHandler` in `src/RoleRadar.Application/Integrity/VerifyIntegrityQuery.cs` performing read-only cross-collection scans (depends on T051 failing first and T053)
- [ ] T056 [US7] Implement CLI commands `roleradar verify` in `src/RoleRadar.Cli/Commands/VerifyCommand.cs` and extend `roleradar status` in `src/RoleRadar.Cli/Commands/StatusCommand.cs`, and expose `action: "search_run"` on `tracker_log` in `src/RoleRadar.Mcp.Stdio/Tools/TrackingMcpTools.cs` (depends on T050, T051, T052 failing first and T054, T055)

**Checkpoint**: User Story 7 is complete and independently verified by `TrackerLogSearchRunContractTests`, `VerifyCliContractTests`, and `StatusCliContractTests`.  
*Commit*: `feat(integrity): implement search run logging, roleradar verify integrity check, and status extensions`

---

## Phase 10: Unified Query Tool (`tracker_query`) & Paging

**Goal**: Implement the unified read-only query tool `tracker_query` supporting all entity discriminators, uniform bounded paging (`page`/`pageSize`, default 50, max 200), compact decided-postings lookup, and read-only posting lookup by deduplication key.

**Independent Test**: Query each entity discriminator (`postings`, `decided_postings`, `posting_dedup_lookup`, `companies`, `criteria`, `applications`); verify paging bounds; verify filtering by status, tag, name, and stale days for companies; verify querying contacts via `tracker_query` returns a validation error.

### Tests for Unified Query Tool (MANDATORY - Test-First per Principle II) ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [ ] T057 [P] Write failing contract test `TrackerQueryContractTests.cs` for tool `tracker_query` covering discriminators (`postings`, `decided_postings`, `posting_dedup_lookup`, `companies`, `criteria`, `applications`), filters (`companyId`, `disposition`, `status`, `tag`, `name`, `includeRetired`, `includeStale` with `staleAfterDays`, `effectiveDate`, `postingId`), bounded uniform paging (`page`, `pageSize`, default 50, max 200), and validation error when attempting to query `contacts` in `tests/RoleRadar.Tests.Contract/TrackerQueryContractTests.cs` (expected to fail before implementation)

### Implementation for Unified Query Tool

- [ ] T058 Implement `TrackerQuery`, `TrackerQueryHandler`, and response DTOs in `src/RoleRadar.Application/Tracking/Queries/TrackerQuery.cs` (depends on T057 failing first)
- [ ] T059 Implement MCP tool `tracker_query` in `src/RoleRadar.Mcp.Stdio/Tools/TrackingMcpTools.cs` and register handler in `src/RoleRadar.Application/DependencyInjection.cs` (depends on T057 failing first and T058)

**Checkpoint**: Unified query tool is complete and verified by `TrackerQueryContractTests`.  
*Commit*: `feat(query): implement tracker_query with bounded paging and discriminator routing`

---

## Phase 11: Polish, Security & Cross-Cutting Verification

**Purpose**: Execute end-to-end architecture assertions, startup smoke tests, deduplication parallel race tests, 5,000-posting latency benchmarks, and backup/restore round-trip verification across all 9 new collections.

- [ ] T060 [P] Write failing architecture tests in `tests/RoleRadar.Tests.Architecture/TrackingArchitectureTests.cs` asserting:
  - Total MCP tools count <= 12 and exactly 6 new tools
  - No delete/remove methods on any new repository or CLI command
  - No tool name containing delete/remove
  - No MCP tool, parameter, or description referencing contact data
  - `TransactionAndAuditCommandDecorator` registration wraps every new `ICommandHandler<,>` implementation
- [ ] T061 [P] Implement startup smoke test in `tests/RoleRadar.Tests.Integration/Smoke/StartupSmokeIntegrationTests.cs` that builds and launches the actual CLI and MCP DLLs as separate processes using default configuration and exercises one new tool or command per entity (verifying startup composition without building an in-process service provider)
- [ ] T062 [P] Implement parallel-submit race integration tests in `tests/RoleRadar.Tests.Integration/Tracking/ParallelSubmitRaceIntegrationTests.cs` testing concurrent submissions for Tier A, Tier B, and Tier C collisions using Testcontainers
- [ ] T063 [P] Implement 5,000-posting performance benchmark tests in `tests/RoleRadar.Tests.Integration/Tracking/TrackingBenchmarkTests.cs` asserting p95 lookup latency < 100 ms and p95 search/filter latency < 200 ms across 5,000 postings
- [ ] T064 [P] Implement backup, wipe, and restore round-trip integration tests in `tests/RoleRadar.Tests.Integration/Backup/TrackingBackupRestoreRoundTripTests.cs` asserting 100% data preservation across all 9 new collections (`companies`, `career_sources`, `postings`, `posting_signals`, `criteria`, `applications`, `application_events`, `contacts`, `search_runs`), and verifying the restore non-empty database safety guard
- [ ] T065 Run `scripts/verify-traceability.ps1` and verify all 48 requirements pass, execute quickstart validation, and verify CI checks pass

**Checkpoint**: All cross-cutting architecture, performance, security, and backup tests pass.  
*Commit*: `test(cross-cutting): add architecture tests, startup smoke test, parallel submit races, benchmarks, and backup verification`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately.
- **Foundational (Phase 2)**: Depends on Phase 1 — BLOCKS all user stories.
- **User Stories (Phase 3+)**: All depend on Foundational phase completion.
  - US1 (Postings & Dedup, P1) and US2 (Companies & Sources, P1) can proceed in parallel once Foundation completes.
  - US3 (Criteria, P1) can proceed once Foundation completes.
  - US4 (Applications, P2) depends on US1 (`Posting` aggregate) and optionally US6 (`referral_contact_id`).
  - US5 (Signals, P2) depends on US1 (`Posting` aggregate).
  - US6 (Contacts, P2) can proceed once Foundation completes.
  - US7 (Search Runs & Integrity, P3) depends on US1, US2, US4 for referential integrity checks.
- **Phase 10 (Unified Query Tool)**: Integrates read routing for US1, US2, US3, US4, US5.
- **Phase 11 (Polish & Cross-Cutting)**: Depends on all user story phases being complete.

### Within Each User Story

- Tests are MANDATORY, come first, and MUST fail on assertions (stub types so everything compiles) before implementation begins.
- Domain models and enums are implemented first.
- Command and query handlers implemented second.
- MCP tools and CLI commands implemented third.
- Story complete and verified before moving to dependent stories.

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (CRITICAL — blocks all stories)
3. Complete Phase 3: User Story 1 (Postings with Dispositions & Tiered Deduplication)
4. **STOP and VALIDATE**: Test User Story 1 independently (`PostingRecordContractTests`, `PostingTransitionContractTests`, `PostingRecordExplicitUpdateTests`)
5. Demo / verify core deduplication and screening memory before proceeding to company tracking and applications.
