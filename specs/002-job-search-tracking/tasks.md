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

- [x] T001 Setup shared Testcontainers Mongo fixture extensions and isolated Docker project helpers for contract and integration test suites in `tests/RoleRadar.Tests.Contract/Fixtures/SharedMongoFixture.cs` and `tests/RoleRadar.Tests.Integration/Fixtures/SharedMongoFixture.cs` ensuring tests reuse fixtures cleanly and never depend on the local developer compose stack
- [x] T002 [P] Create strictly fictional tracking seed fixtures (companies, career sources, postings with Tiers A-D variants, criteria, applications, timeline events, and contacts) in `tests/fixtures/fictional-tracking-dataset.json` (strictly fictional data per FR-046)
- [x] T003 [P] Implement automated requirements traceability matrix verification script `scripts/verify-traceability.ps1` that parses `specs/002-job-search-tracking/spec.md` to extract all 48 requirements (FR-001 through FR-046, FR-012a, FR-040a), verifies their presence in `specs/002-job-search-tracking/contracts/mcp-tools.md` with status PASS, and (after implementation) verifies that every tool name, CLI command, and test file named in the matrix exists in the code on disk

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core domain value types, `Company` and `CareerSource` domain entities and repository methods, ErrorCode additions, normalizers with property tests, living architecture tests, process-based startup smoke test, audit summary types, and MongoDB collections with partial unique indexes that MUST be complete before any user story can be implemented.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Foundational Tests (MANDATORY - Test-First per Principle II & III) ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [x] T004 [P] Write failing unit and FsCheck property tests for `UrlNormalizer` and `TitleNormalizer` (tracking parameter denylist stripping `utm_*`, `ref`, `gh_src`, etc., protocol/host canonicalization, trailing slash removal, case/punctuation/whitespace folding, and property-based idempotence `Normalize(Normalize(u)) == Normalize(u)`) in `tests/RoleRadar.Tests.Unit/Tracking/NormalizerPropertyAndUnitTests.cs` (expected to fail before implementation)
- [x] T005 [P] Write failing domain unit tests for tracking enums, domain primitives, `Company` and `CareerSource` invariants, `ContactAuditContentSummary` field-names-only privacy invariant, and typed `Result<T>` error codes in `tests/RoleRadar.Tests.Unit/Tracking/TrackingDomainAndAuditTests.cs` (expected to fail before implementation)
- [x] T006 [P] Write failing MongoDB bootstrap and index initializer contract tests using Testcontainers verifying partial unique indexes for `dedup_key_a`, `dedup_key_b`, `dedup_key_c`, active criteria `{ owner_id: 1, key: 1 }` where `is_active == true`, career sources `{ owner_id: 1, company_id: 1, ats_type: 1, tenant: 1, slug: 1 }`, and unique index on `search_runs { owner_id: 1, started_at: 1 }` in `tests/RoleRadar.Tests.Contract/TrackingIndexInitializerContractTests.cs` (expected to fail before implementation)
- [x] T007 [P] Create living architecture test file in `tests/RoleRadar.Tests.Architecture/TrackingArchitectureTests.cs` (asserting tool cap <= 12 and exactly 6 new once complete, no delete/remove repository methods or tool names, zero contact data on MCP, and `TransactionAndAuditCommandDecorator` registration for every new command handler)
- [x] T008 [P] Implement base process-based startup smoke test in `tests/RoleRadar.Tests.Integration/Smoke/StartupSmokeIntegrationTests.cs` that builds and launches the actual CLI and MCP DLLs as separate processes using default configuration (addressing the 001 lesson: tests must not build their own in-process service provider)

### Foundational Implementation

- [x] T009 [P] Update `ErrorCode` enum in `src/RoleRadar.Application/Abstractions/Result.cs` adding ONLY genuinely new code `InvalidTransition`, explicitly reusing existing `StaleVersionToken` for all optimistic concurrency, missing, or mismatched version token cases, and confirming that `AlreadyDecided`, `AlreadyTracked`, and `PossibleDuplicate` are handled as successful outcomes with a status discriminator (`status: "created" | "already_tracked" | "already_decided" | "possible_duplicate"`) rather than error codes (depends on T005 failing first)
- [x] T010 [P] Implement domain value types, enums, and primitives (`OwnershipType`, `CompanyStatus`, `CultureLevel`, `PostingDisposition`, `ResurfacePolicy`, `SourceKind`, `RemoteType`, `FetchMethod`, `CriterionCategory`, `ProvenanceKind`, `ResumeVariant`, `ApplicationOutcome`, `ApplicationEventType`, `SignalType`, `ContactRelationship`) in `src/RoleRadar.Domain/Tracking/TrackingEnums.cs` and `src/RoleRadar.Domain/Tracking/TrackingPrimitives.cs` (depends on T005 failing first)
- [x] T011 [P] Implement `Company` aggregate root and `CareerSource` entity in `src/RoleRadar.Domain/Tracking/Company.cs` and `src/RoleRadar.Domain/Tracking/CareerSource.cs` (including `is_active` lifecycle, bounded 300-char description, and ownership type enum) (depends on T005 failing first)
- [x] T012 [P] Implement pure `UrlNormalizer` with tracking parameter denylist (`utm_*`, `ref`, `ref_*`, `source`, `gh_src`, `src`, `source_id`, `fbclid`, `gclid`, `gclsrc`, `dclid`, `msclkid`, `twclid`, `trk`, `trackingId`, `tracking_id`, `li_fat_id`, `mc_cid`, `mc_eid`, `_hsenc`, `_hsmi`, `hsCtaTracking`, `igshid`, `yclid`, `mkt_tok`) and `TitleNormalizer` in `src/RoleRadar.Domain/Tracking/UrlNormalizer.cs` and `src/RoleRadar.Domain/Tracking/TitleNormalizer.cs` (depends on T004 failing first)
- [x] T013 [P] Implement typed audit summary domain records (`PostingAuditSummary`, `CompanyAuditSummary`, `CriterionAuditSummary`, `ApplicationAuditSummary`, and `ContactAuditContentSummary` strictly containing changed field names and record IDs without names, roles, or company text) in `src/RoleRadar.Domain/Audit/TrackingAuditSummaries.cs` (depends on T005 failing first)
- [x] T014 [P] Define aggregate-group repository ports (`IPostingRepository`, `ICompanyRepository`, `ICriterionRepository`, `IApplicationRepository`, `IContactRepository`, `ISearchRunRepository`) sharing `ITrackingTransactionContext` in `src/RoleRadar.Application/Abstractions/TrackingRepositories.cs`
- [x] T015 Implement MongoDB collections options (`companies`, `career_sources`, `postings`, `posting_signals`, `criteria`, `applications`, `application_events`, `contacts`, `search_runs`), index initializers, and Mongo implementations of `ICompanyRepository` and transaction context in `src/RoleRadar.Infrastructure/Mongo/TrackingMongoOptions.cs`, `src/RoleRadar.Infrastructure/Mongo/TrackingIndexInitializer.cs`, and `src/RoleRadar.Infrastructure/Mongo/Repositories/MongoCompanyRepository.cs` (depends on T006 failing first and T010, T011, T014)
- [x] T016 Register foundational tracking infrastructure, repositories, and readiness probes in `src/RoleRadar.Infrastructure/DependencyInjection.cs` and `src/RoleRadar.Application/DependencyInjection.cs`

**Checkpoint**: Foundation ready — normalizer property tests, domain tests, index initializer contract tests, living architecture tests, and startup smoke test compile and pass. User story implementation can begin.  
*Commit*: `feat(foundation): add tracking data types, company repo, indexes, normalizers, and aggregate-group repository ports`

---

## Phase 3: User Story 1 - Postings with Dispositions, Tiered Deduplication & Read Query (Priority: P1) 🎯 MVP

**Goal**: Record and update job postings with full tracking metadata, validating target company exists in `companies`, enforcing deduplication Tiers A through D, collapsing duplicate reference chains at write time, supporting explicit update mode with version tokens, returning differing supplied values in a `differences` list on dedup match, remembering screening decisions with resurface policies, and exposing read query discriminators for postings, compact decided postings, and key deduplication lookups.

**Independent Test**: Submit postings with varying URLs and ATS identifiers; verify target company existence validation (`ErrorCode.ValidationError` if company absent); verify Tier A, B, and C deduplication matches existing records; verify Tier D advisory matching returns `status: "possible_duplicate"` without mutating data; re-encounter decided postings and verify `status: "already_decided"` with `can_reopen` indicator; re-encounter active posting on different UTC day and verify `status: "already_tracked"` commits `last_verified_at` bump and audit entry; update postings via explicit update mode (`postingId` + `versionToken`) and verify partial field mutation; query `tracker_query` for `postings`, `decided_postings`, and `posting_dedup_lookup`.

### Tests for User Story 1 (MANDATORY - Test-First per Principle II) ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [x] T017 [P] [US1] Write failing contract test `PostingRecordContractTests.cs` for tool `posting_record` covering create (defaulting to `lead`), validating company exists in `companies` (`ErrorCode.ValidationError` if absent), Tier A match (`dedup_key_a`), Tier B match (`dedup_key_b`), Tier C match (`dedup_key_c`), Tier D advisory match returning `status: "possible_duplicate"` without record modification, in-place ATS identifier upgrade, duplicate reference chain collapsing, differences list return on non-matching values, re-encounter of decided postings returning `status: "already_decided"` with `can_reopen`, and re-encounter of active postings returning `status: "already_tracked"` (including test asserting that a different-UTC-day re-encounter commits `last_verified_at` bump and audit entry to Mongo while returning `already_tracked`) in `tests/RoleRadar.Tests.Contract/PostingRecordContractTests.cs` (expected to fail before implementation)
- [x] T018 [P] [US1] Write failing contract test `PostingTransitionContractTests.cs` for tool `posting_transition` covering valid transitions, rejection of `applied` target with `ErrorCode.InvalidTransition`, evaluation of stored `resurface_policy` on reopen to `lead`, and setting `decided_at` UTC timestamp upon transition to `screened_out`/`closed`/`rejected` in `tests/RoleRadar.Tests.Contract/PostingTransitionContractTests.cs` (expected to fail before implementation)
- [x] T019 [P] [US1] Write failing unit test `DispositionMatrixTests.cs` verifying the exhaustive table-driven disposition state transition matrix in `tests/RoleRadar.Tests.Unit/Tracking/DispositionMatrixTests.cs` (expected to fail before implementation)
- [x] T020 [P] [US1] Write failing contract test `PostingRecordExplicitUpdateTests.cs` covering explicit update mode (`postingId` + `versionToken`), validating company exists, mutating only supplied fields, rotating `version_token`, logging changed field names in audit log, and returning `ErrorCode.StaleVersionToken` on version token mismatch in `tests/RoleRadar.Tests.Contract/PostingRecordExplicitUpdateTests.cs` (expected to fail before implementation)
- [x] T021 [P] [US1] Write failing contract test `TrackerQueryPostingsContractTests.cs` for `tracker_query` discriminators `postings` (with `postingId` single lookup or paged list with `disposition` filter and dynamic ghost likelihood), `decided_postings` (compact list ordered by `decided_at` desc), and `posting_dedup_lookup` (read-only Tier A, B, C key lookup without mutation) with bounded paging (default 50, max 200) in `tests/RoleRadar.Tests.Contract/TrackerQueryPostingsContractTests.cs` (expected to fail before implementation)

### Implementation for User Story 1

- [x] T022 [P] [US1] Implement `Posting` aggregate root in `src/RoleRadar.Domain/Tracking/Posting.cs`, enforcing `data-model.md` constraints verbatim:
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
- [x] T023 [P] [US1] Implement `MongoPostingRepository` implementing `IPostingRepository` in `src/RoleRadar.Infrastructure/Mongo/Repositories/MongoPostingRepository.cs`
- [x] T024 [P] [US1] Implement `RecordPostingCommand`, `RecordPostingCommandHandler`, and DTOs in `src/RoleRadar.Application/Tracking/Commands/RecordPostingCommand.cs` (validating company exists via `ICompanyRepository`, handling Tiers A-D dedup, differences list, returning success status `already_tracked` committing different-day `last_verified_at` bump, and explicit update mode with `StaleVersionToken`) (depends on T017, T020 failing first and T022, T023)
- [x] T025 [P] [US1] Implement `TransitionPostingCommand`, `TransitionPostingCommandHandler`, and DTOs in `src/RoleRadar.Application/Tracking/Commands/TransitionPostingCommand.cs` (transition matrix, `InvalidTransition` on target `applied`, stored resurface policy, `decided_at`) (depends on T018, T019 failing first and T022, T023)
- [x] T026 [P] [US1] Implement `TrackerQueryHandler` for discriminators `postings`, `decided_postings`, and `posting_dedup_lookup` with uniform 50/200 bounded paging in `src/RoleRadar.Application/Tracking/Queries/TrackerQueryPostingsHandler.cs` (depends on T021 failing first and T022, T023)
- [x] T027 [US1] Implement MCP tools `posting_record`, `posting_transition`, and `tracker_query` (postings discriminators) in `src/RoleRadar.Mcp.Stdio/Tools/TrackingMcpTools.cs`, register handlers in `src/RoleRadar.Application/DependencyInjection.cs`, and extend `TrackingArchitectureTests.cs` and `StartupSmokeIntegrationTests.cs` to exercise posting tools (depends on T017, T018, T020, T021 failing first and T024, T025, T026)

**Checkpoint**: User Story 1 (MVP) is complete and independently verified by `PostingRecordContractTests`, `PostingTransitionContractTests`, `DispositionMatrixTests`, `PostingRecordExplicitUpdateTests`, and `TrackerQueryPostingsContractTests`.  
*Commit*: `feat(postings): implement posting_record, posting_transition, and tracker_query postings discriminators`

---

## Phase 4: User Story 2 - Companies and Career Sources (Priority: P1)

**Goal**: Expose the `company_record` MCP tool to create/update target employers (with bounded 300-char descriptions and ownership types) and upsert career sources (ATS configuration, fetch method, and `is_active` lifecycle), enabling automated searches to know how and where to check for postings.

**Independent Test**: Create a company with description and ownership type; upsert career sources by ID and composite key; update `isActive` to retire a source without hard deletion; verify query filters exclude retired sources unless `includeRetired` is true.

### Tests for User Story 2 (MANDATORY - Test-First per Principle II) ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [x] T028 [P] [US2] Write failing contract test `CompanyRecordContractTests.cs` for tool `company_record` covering company creation, description up to 300 chars, career sources upsert by `id` or composite `(ats_type, tenant, slug)`, `is_active` flag retirement and reactivation without deletion, and omission of sources leaving existing sources untouched in `tests/RoleRadar.Tests.Contract/CompanyRecordContractTests.cs` (expected to fail before implementation)

### Implementation for User Story 2

- [x] T029 [P] [US2] Implement `RecordCompanyCommand`, `RecordCompanyCommandHandler`, and DTOs in `src/RoleRadar.Application/Tracking/Commands/RecordCompanyCommand.cs` (upserting career sources by id or composite key, handling `isActive` input parameter) (depends on T028 failing first)
- [x] T030 [US2] Implement MCP tool `company_record` in `src/RoleRadar.Mcp.Stdio/Tools/TrackingMcpTools.cs`, register handler wrapped by `TransactionAndAuditCommandDecorator` in `src/RoleRadar.Application/DependencyInjection.cs`, and extend `TrackingArchitectureTests.cs` and `StartupSmokeIntegrationTests.cs` for company recording (depends on T028 failing first and T029)

**Checkpoint**: User Story 2 is complete and independently verified by `CompanyRecordContractTests`.  
*Commit*: `feat(companies): implement company_record with career source upsert and is_active lifecycle`

---

## Phase 5: User Story 3 - Criteria with Revision History (Priority: P1)

**Goal**: Maintain immutable, versioned screening criteria with strict `effectiveFrom` sequencing and version token concurrency control, support deactivating criteria without replacement, and enable point-in-time criteria evaluation.

**Independent Test**: Insert a criterion effective on date T1; supersede it with a revision on date T2; assert missing version token returns `ErrorCode.StaleVersionToken`; deactivate a criterion via `criterion_deactivate`; query criteria as of date T1 and date T2 to verify historical point-in-time resolution.

### Tests for User Story 3 (MANDATORY - Test-First per Principle II) ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [x] T031 [P] [US3] Write failing contract test `TrackerLogCriteriaContractTests.cs` for tool `tracker_log` covering action `criterion_revision` (strict `effectiveFrom` sequencing validation, closing active version, rotating version token), `ErrorCode.StaleVersionToken` on missing/mismatched version token, and action `criterion_deactivate` (closing active version without replacement) in `tests/RoleRadar.Tests.Contract/TrackerLogCriteriaContractTests.cs` (expected to fail before implementation)
- [x] T032 [P] [US3] Write failing unit tests for `Criterion` point-in-time validity and date sequencing in `tests/RoleRadar.Tests.Unit/Tracking/CriterionVersionValidationTests.cs` (expected to fail before implementation)

### Implementation for User Story 3

- [x] T033 [P] [US3] Implement `Criterion` aggregate root in `src/RoleRadar.Domain/Tracking/Criterion.cs` and `MongoCriterionRepository` implementing `ICriterionRepository` in `src/RoleRadar.Infrastructure/Mongo/Repositories/MongoCriterionRepository.cs`, enforcing `data-model.md` constraints verbatim:
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
- [x] T034 [P] [US3] Implement `LogCriterionRevisionCommand`, `DeactivateCriterionCommand`, and handlers in `src/RoleRadar.Application/Tracking/Commands/CriterionCommands.cs` (enforcing strict `effectiveFrom` validation, closing active record, requiring `versionToken` when active exists) (depends on T031, T032 failing first and T033)
- [x] T035 [US3] Implement `tracker_log` tool actions `criterion_revision` and `criterion_deactivate` in `src/RoleRadar.Mcp.Stdio/Tools/TrackingMcpTools.cs`, register handlers wrapped by `TransactionAndAuditCommandDecorator` in `src/RoleRadar.Application/DependencyInjection.cs`, and extend `TrackingArchitectureTests.cs` and `StartupSmokeIntegrationTests.cs` for criteria logging (depends on T031 failing first and T034)

**Checkpoint**: User Story 3 is complete and independently verified by `TrackerLogCriteriaContractTests` and `CriterionVersionValidationTests`.  
*Commit*: `feat(criteria): implement immutable criteria revisioning and deactivation in tracker_log`

---

## Phase 6: User Story 4 - Applications and Interaction Timeline (Priority: P2)

**Goal**: Track formal job applications with resume variant pointers and numeric salary ranges, support an atomic reopen-and-apply override for decided roles, synchronize posting dispositions on application events, and record timeline events (accepting events on terminal postings without mutating disposition).

**Independent Test**: Create an application from `lead` moving posting to `applied`; attempt to apply to `screened_out` posting without override (assert `ErrorCode.InvalidTransition`); apply with `overrideDecided: true` (assert atomic transition to `applied` with audit record); append timeline events (including `rejected` and non-terminal `offer`); append event to already closed posting and verify event appends without error or disposition change.

### Tests for User Story 4 (MANDATORY - Test-First per Principle II) ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [x] T036 [P] [US4] Write failing contract test `ApplicationRecordContractTests.cs` for tool `application_record` covering application creation from `lead`, `unscored`, and `scored` (moving posting to `applied`), atomic reopen-and-apply override on `screened_out`/`closed` posting in ONE audited transaction, rejection without override flag (`ErrorCode.InvalidTransition`), action `append_event` with 8 event types, and appending events to already decided/terminal postings without mutating disposition in `tests/RoleRadar.Tests.Contract/ApplicationRecordContractTests.cs` (expected to fail before implementation)
- [x] T037 [P] [US4] Write failing unit tests for `Application` and `ApplicationEvent` domain invariants, numeric salary validation (`min >= 0`, `max >= min`), resume variant pointers (forbidding raw resume text), and outcome synchronization in `tests/RoleRadar.Tests.Unit/Tracking/ApplicationDomainTests.cs` (expected to fail before implementation)

### Implementation for User Story 4

- [x] T038 [P] [US4] Implement `Application` aggregate root and `ApplicationEvent` entity in `src/RoleRadar.Domain/Tracking/Application.cs` and `src/RoleRadar.Domain/Tracking/ApplicationEvent.cs`, and `MongoApplicationRepository` implementing `IApplicationRepository` in `src/RoleRadar.Infrastructure/Mongo/Repositories/MongoApplicationRepository.cs`, enforcing `data-model.md` constraints verbatim:
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
- [x] T039 [P] [US4] Implement `RecordApplicationCommand`, `AppendApplicationEventCommand`, and handlers in `src/RoleRadar.Application/Tracking/Commands/ApplicationCommands.cs` (reopen-and-apply atomic override, disposition synchronization, accepting events on terminal postings) (depends on T036, T037 failing first and T038)
- [x] T040 [US4] Implement MCP tool `application_record` in `src/RoleRadar.Mcp.Stdio/Tools/TrackingMcpTools.cs`, register handlers wrapped by `TransactionAndAuditCommandDecorator` in `src/RoleRadar.Application/DependencyInjection.cs`, and extend `TrackingArchitectureTests.cs` and `StartupSmokeIntegrationTests.cs` for applications (depends on T036 failing first and T039)

**Checkpoint**: User Story 4 is complete and independently verified by `ApplicationRecordContractTests` and `ApplicationDomainTests`.  
*Commit*: `feat(applications): implement application_record with atomic reopen-and-apply and timeline events`

---

## Phase 7: User Story 5 - Posting Signals and Ghost Likelihood (Priority: P2)

**Goal**: Record observable risk/quality signals against job postings (including optional deadline dates) and dynamically compute ghost likelihood on read without persisting scores to storage.

**Independent Test**: Attach signals (`gone`, `repost`, `deadline`) to a posting; verify dynamic ghost score is calculated correctly on retrieval; assert via raw Mongo document inspection and decrypted backup archives that zero `ghost` or `ghost_likelihood` fields are persisted in the datastore.

### Tests for User Story 5 (MANDATORY - Test-First per Principle II) ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [ ] T041 [P] [US5] Write failing unit tests for `GhostLikelihoodRule` covering active signal weights (`gone` +50%, `phantom` +40%, `deadline` passed +30%, `repost` +25%, `parallel_posting` +20%, `no_salary` +15%, age >60d +20%, age >90d +35%, freshness <7d -15%), clamping 0..100, and categorization (Low 0..24, Moderate 25..49, High 50..74, Critical 75..100) in `tests/RoleRadar.Tests.Unit/Tracking/GhostLikelihoodRuleTests.cs` (expected to fail before implementation)
- [ ] T042 [P] [US5] Write failing contract test `PostingSignalContractTests.cs` verifying append of posting signals and optional `deadline_date` in `tests/RoleRadar.Tests.Contract/PostingSignalContractTests.cs` (expected to fail before implementation)
- [ ] T043 [P] [US5] Write failing integration test `GhostLikelihoodPersistenceScanIntegrationTests.cs` verifying ghost likelihood is dynamically computed on read, and asserting via raw BSON collection inspection and decrypted backup scan that zero `ghost` or `ghost_likelihood` fields are persisted in `tests/RoleRadar.Tests.Integration/Tracking/GhostLikelihoodPersistenceScanIntegrationTests.cs` (expected to fail before implementation)

### Implementation for User Story 5

- [ ] T044 [P] [US5] Implement `PostingSignal` entity in `src/RoleRadar.Domain/Tracking/PostingSignal.cs` with `signal_type` (`repost`, `no_salary`, `parallel_posting`, `gone`, `phantom`, `deadline`), `evidence` (mandatory, max 500 chars), `deadline_date` (optional date-only `YYYY-MM-DD`), and `observed_at` (UTC timestamp) (depends on T042 failing first)
- [ ] T045 [P] [US5] Implement `GhostLikelihoodRule` domain service in `src/RoleRadar.Domain/Tracking/GhostLikelihoodRule.cs` computing dynamic score strictly on read (depends on T041, T043 failing first and T044)
- [ ] T046 [US5] Integrate signal ingestion in `RecordPostingCommandHandler` and dynamic ghost assessment in `TrackerQueryPostingsHandler` in `src/RoleRadar.Application/Tracking/Commands/RecordPostingCommand.cs` and `src/RoleRadar.Application/Tracking/Queries/TrackerQueryPostingsHandler.cs` (depends on T042, T043 failing first and T044, T045)

**Checkpoint**: User Story 5 is complete and independently verified by `GhostLikelihoodRuleTests`, `PostingSignalContractTests`, and `GhostLikelihoodPersistenceScanIntegrationTests`.  
*Commit*: `feat(signals): implement posting signals and dynamic on-read ghost likelihood calculation`

---

## Phase 8: User Story 6 - Confidential Contacts Isolation (CLI-Only) (Priority: P2)

**Goal**: Manage professional contacts exclusively via CLI (`roleradar contact add|get|redact`), completely isolated from MCP, general queries, logs, and diagnostics, with privacy redaction and field-names-only audit summaries.

**Independent Test**: Add a contact via CLI; verify contact is retrievable solely by direct ID; verify MCP queries reject contacts; redact contact and verify personal fields are blanked while ID and audit history remain; run no-leak scanner across logs, diagnostics, status, and backups to verify zero contact details leak.

### Tests for User Story 6 (MANDATORY - Test-First per Principle II) ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [ ] T047 [P] [US6] Write failing contract test `ContactCliContractTests.cs` for CLI commands `roleradar contact add|get|redact` covering creation with name, role, company, relationship (`recruiter`, `referral`, `hiring_manager`, `peer`), retrieval strictly by contact ID, and `redact` blanking personal fields while preserving ID and auditing with field-names-only summary in `tests/RoleRadar.Tests.Contract/ContactCliContractTests.cs` (expected to fail before implementation)
- [ ] T048 [P] [US6] Write failing unit test `ContactPrivacyTests.cs` proving audit summaries contain exclusively changed field names and record IDs, and zero contact data is present in `ContactAuditContentSummary` in `tests/RoleRadar.Tests.Unit/Tracking/ContactPrivacyTests.cs` (expected to fail before implementation)
- [ ] T049 [P] [US6] Write failing architecture test `McpZeroContactExposureTests.cs` using NetArchTest asserting that no MCP tool class, method, parameter, or tool description references `Contact` types or exposes contact data in `tests/RoleRadar.Tests.Architecture/McpZeroContactExposureTests.cs` (expected to fail before implementation)
- [ ] T050 [P] [US6] Write failing integration test `ContactNoLeakScannerIntegrationTests.cs` creating a contact via CLI, triggering error conditions, status checks, search queries, and backup archive dumps, and scanning all outputs to assert zero leaks of personal contact information in `tests/RoleRadar.Tests.Integration/Tracking/ContactNoLeakScannerIntegrationTests.cs` (expected to fail before implementation)

### Implementation for User Story 6

- [ ] T051 [P] [US6] Implement `Contact` aggregate root in `src/RoleRadar.Domain/Tracking/Contact.cs` and `MongoContactRepository` implementing `IContactRepository` in `src/RoleRadar.Infrastructure/Mongo/Repositories/MongoContactRepository.cs`, enforcing `data-model.md` constraints verbatim:
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
- [ ] T052 [P] [US6] Implement `AddContactCommand`, `GetContactQuery`, `RedactContactCommand`, and handlers in `src/RoleRadar.Application/Tracking/Commands/ContactCommands.cs` and `src/RoleRadar.Application/Tracking/Queries/GetContactQuery.cs` (depends on T047, T048 failing first and T051)
- [ ] T053 [US6] Implement CLI command `roleradar contact add|get|redact` in `src/RoleRadar.Cli/Commands/ContactCommand.cs`, register handlers in `src/RoleRadar.Application/DependencyInjection.cs`, register CLI root in `src/RoleRadar.Cli/Program.cs`, and extend `TrackingArchitectureTests.cs` and `StartupSmokeIntegrationTests.cs` for contact commands (depends on T047, T049, T050 failing first and T052)

**Checkpoint**: User Story 6 is complete and independently verified by `ContactCliContractTests`, `ContactPrivacyTests`, `McpZeroContactExposureTests`, and `ContactNoLeakScannerIntegrationTests`.  
*Commit*: `feat(contacts): implement CLI-only confidential contact management with privacy redaction`

---

## Phase 9: User Story 7 - Search Runs and Referential Integrity Verification (Priority: P3)

**Goal**: Record search run execution milestones linked to report notes, support deduplication on `(owner_id, started_at)`, extend `Note` entities with backward-compatible links, and provide a read-only referential integrity verification CLI command (`roleradar verify`) that flags dangling pointers across all collections.

**Independent Test**: Log a search run via `tracker_log`; submit duplicate search run and assert `status: "already_logged"` without duplicate insert; create notes referencing entities; intentionally introduce a dangling company ID in a test posting; run `roleradar verify` and verify that the anomaly is reported cleanly without data modification.

### Tests for User Story 7 (MANDATORY - Test-First per Principle II) ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [ ] T054 [P] [US7] Write failing contract test `TrackerLogSearchRunContractTests.cs` for tool `tracker_log (action: "search_run")` covering logging start timestamp, new postings count, report note link, and deduplication returning `status: "already_logged"` on duplicate `(owner_id, started_at)` without write or audit entry in `tests/RoleRadar.Tests.Contract/TrackerLogSearchRunContractTests.cs` (expected to fail before implementation)
- [ ] T055 [P] [US7] Write failing contract test `VerifyCliContractTests.cs` for CLI command `roleradar verify` verifying read-only detection of dangling company references, dangling posting references, dangling `duplicate_of` links, and dangling note entity links in `tests/RoleRadar.Tests.Contract/VerifyCliContractTests.cs` (expected to fail before implementation)
- [ ] T056 [P] [US7] Write failing contract test `StatusCliContractTests.cs` verifying extended `roleradar status` reporting entity counts for companies, postings, criteria, applications, and contacts (with redacted counts and zero personal data) in `tests/RoleRadar.Tests.Contract/StatusCliContractTests.cs` (expected to fail before implementation)

### Implementation for User Story 7

- [ ] T057 [P] [US7] Extend `Note` aggregate root in `src/RoleRadar.Domain/Notes/Note.cs` with optional binary subtype 4 GUID link fields `company_id`, `posting_id`, `application_id`, and update MongoDB partial indexes in `src/RoleRadar.Infrastructure/Mongo/MongoBootstrapAndIndexes.cs` (backward-compatible)
- [ ] T058 [P] [US7] Implement `SearchRun` entity in `src/RoleRadar.Domain/Tracking/SearchRun.cs`, `MongoSearchRunRepository` implementing `ISearchRunRepository` in `src/RoleRadar.Infrastructure/Mongo/Repositories/MongoSearchRunRepository.cs`, and `LogSearchRunCommand` in `src/RoleRadar.Application/Tracking/Commands/LogSearchRunCommand.cs` (enforcing unique index on `owner_id, started_at` and returning `already_logged`) (depends on T054 failing first)
- [ ] T059 [P] [US7] Implement `VerifyIntegrityQuery` and `VerifyIntegrityQueryHandler` in `src/RoleRadar.Application/Integrity/VerifyIntegrityQuery.cs` performing read-only cross-collection scans (depends on T055 failing first and T057)
- [ ] T060 [US7] Implement CLI commands `roleradar verify` in `src/RoleRadar.Cli/Commands/VerifyCommand.cs` and extend `roleradar status` in `src/RoleRadar.Cli/Commands/StatusCommand.cs`, expose `action: "search_run"` on `tracker_log` in `src/RoleRadar.Mcp.Stdio/Tools/TrackingMcpTools.cs`, and extend `TrackingArchitectureTests.cs` and `StartupSmokeIntegrationTests.cs` for integrity and search run operations (depends on T054, T055, T056 failing first and T058, T059)

**Checkpoint**: User Story 7 is complete and independently verified by `TrackerLogSearchRunContractTests`, `VerifyCliContractTests`, and `StatusCliContractTests`.  
*Commit*: `feat(integrity): implement search run logging, roleradar verify integrity check, and status extensions`

---

## Phase 10: Unified Query Tool (`tracker_query`) & Multi-Entity Read Routing

**Goal**: Complete the unified read-only query tool `tracker_query` for `companies`, `criteria`, and `applications`, with filters, uniform bounded paging (`page`/`pageSize`, default 50, max 200), and point-in-time criteria evaluation.

**Independent Test**: Query `tracker_query` with `entity: "companies"` (filtering by status, tag, name, includeRetired, includeStale); query `entity: "criteria"` with `effectiveDate` to verify point-in-time rules; query `entity: "applications"`; assert querying `contacts` returns `ErrorCode.ValidationError`.

### Tests for Multi-Entity Query Tool (MANDATORY - Test-First per Principle II) ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [ ] T061 [P] Write failing contract test `TrackerQueryMultiEntityContractTests.cs` for tool `tracker_query` covering discriminators `companies` (filtering by `status`, `tag`, `name`, `includeRetired`, and `includeStale` with `staleAfterDays`), `criteria` (with `effectiveDate` point-in-time filtering), and `applications`, asserting uniform bounded paging (`page`, `pageSize`, default 50, max 200), and validation error when attempting to query `contacts` in `tests/RoleRadar.Tests.Contract/TrackerQueryMultiEntityContractTests.cs` (expected to fail before implementation)

### Implementation for Multi-Entity Query Tool

- [ ] T062 Extend `TrackerQueryHandler` in `src/RoleRadar.Application/Tracking/Queries/TrackerQueryHandler.cs` to route `companies`, `criteria`, and `applications` discriminators using `ICompanyRepository`, `ICriterionRepository`, and `IApplicationRepository` (depends on T061 failing first)
- [ ] T063 Update MCP tool `tracker_query` in `src/RoleRadar.Mcp.Stdio/Tools/TrackingMcpTools.cs` with the full entity discriminator schema, and extend `TrackingArchitectureTests.cs` and `StartupSmokeIntegrationTests.cs` for multi-entity queries (depends on T061 failing first and T062)

**Checkpoint**: Unified query tool is complete across all allowed entities.  
*Commit*: `feat(query): complete tracker_query multi-entity read routing and paging`

---

## Phase 11: Polish, Backup Compatibility, Security & Cross-Cutting Verification

**Purpose**: Execute backup compatibility tests across 001 archives, restore non-empty guards on all 9 collections, index initialization after wipe/restore, end-to-end architecture assertions, startup smoke tests, deduplication parallel race tests, 5,000-posting latency benchmarks, and backup/restore round-trip verification.

### Tests for Backup Compatibility & Guards (MANDATORY - Test-First per Principle II) ⚠️

- [ ] T064 [P] Write failing integration tests in `tests/RoleRadar.Tests.Integration/Backup/TrackingBackupSafetyAndCompatibilityTests.cs` verifying:
  - Restore non-empty guard blocks restore if ANY of the 9 new collections contains documents
  - Restored-count and wipe summaries report accurate document counts for all 9 collections
  - Tracking index initializer is invoked idempotently after wipe and after restore
  - Restoring a 001-format archive (only notes, note_revisions, audit_log) into the 002 build succeeds and initializes tracking collections cleanly
  - Idempotent index initialization executes safely on a database that already contains notes
  - Deserialization of pre-002 notes without new optional link fields (`company_id`, `posting_id`, `application_id`) deserializes nulls without schema errors (expected to fail before T065 implementation)

### Implementation for Backup Compatibility & Guards

- [ ] T065 [P] Extend `scripts/restore.ps1`, `scripts/wipe-data.ps1`, `scripts/backup.ps1`, and backup services in `src/RoleRadar.Infrastructure/Backup/` to enforce the non-empty guard across all 9 new collections (`companies`, `career_sources`, `postings`, `posting_signals`, `criteria`, `applications`, `application_events`, `contacts`, `search_runs`), output restored and wiped counts across all 9 collections, run `TrackingIndexInitializer` after wipe and restore, and maintain 100% backward compatibility with 001 archives (depends on T064 failing first)

### Cross-Cutting Verification

- [ ] T066 [P] Run final architecture completeness check in `tests/RoleRadar.Tests.Architecture/TrackingArchitectureTests.cs` asserting:
  - Total MCP tools count is exactly 12 (6 existing + 6 new)
  - Zero delete/remove methods across all 6 tracking repositories and CLI commands
  - Zero MCP tool names containing delete/remove
  - Zero MCP tools, parameters, or descriptions exposing or referencing contact data
  - 100% of command handlers in `RoleRadar.Application` are wrapped by `TransactionAndAuditCommandDecorator`
- [ ] T067 [P] Implement parallel-submit race integration tests in `tests/RoleRadar.Tests.Integration/Tracking/ParallelSubmitRaceIntegrationTests.cs` testing concurrent submissions for Tier A, Tier B, and Tier C collisions using Testcontainers
- [ ] T068 [P] Implement 5,000-posting performance benchmark tests in `tests/RoleRadar.Tests.Integration/Tracking/TrackingBenchmarkTests.cs` asserting p95 lookup latency < 100 ms and p95 search/filter latency < 200 ms across 5,000 postings
- [ ] T069 [P] Run backup, wipe, and restore round-trip integration tests in `tests/RoleRadar.Tests.Integration/Backup/TrackingBackupRestoreRoundTripTests.cs` asserting 100% data preservation across all 9 new collections
- [ ] T070 Run `scripts/verify-traceability.ps1` to verify all 48 requirements pass and that every tool name, CLI command, and test file named in the matrix exists in the code on disk; execute quickstart validation; and verify CI checks pass

**Checkpoint**: All cross-cutting architecture, performance, security, and backup tests pass.  
*Commit*: `test(cross-cutting): add architecture completeness check, parallel races, benchmarks, and backup compatibility verification`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately.
- **Foundational (Phase 2)**: Depends on Phase 1 — BLOCKS all user stories. Implements `Company` and `CareerSource` domain models and `ICompanyRepository` in MongoDB.
- **User Stories (Phase 3+)**: All depend on Foundational phase completion.
  - **US1 (Postings & Dedup, P1) 🎯 MVP**: Depends on Foundational. Validates that company exists in `companies` using `ICompanyRepository`. **No dependency on US2** (the ordering hazard is eliminated because company domain entities and repository methods reside in Foundational).
  - **US2 (Companies MCP Tool, P1)**: Depends on Foundational. Exposes `company_record` MCP tool.
  - **US3 (Criteria, P1)**: Depends on Foundational.
  - **US4 (Applications, P2)**: Depends on US1 (`Posting` aggregate) and optionally US6 (`referral_contact_id`).
  - **US5 (Signals, P2)**: Depends on US1 (`Posting` aggregate).
  - **US6 (Contacts, P2)**: Depends on Foundational (isolated CLI storage).
  - **US7 (Search Runs & Integrity, P3)**: Depends on US1, US2, US4 for cross-collection referential integrity verification.
- **Phase 10 (Multi-Entity Query Tool)**: Integrates read routing for `companies`, `criteria`, and `applications` (`postings` discriminators delivered in US1).
- **Phase 11 (Polish & Cross-Cutting)**: Depends on all user story phases being complete.

### Within Each User Story

- Tests are MANDATORY, come first, and MUST fail on assertions (stub types so everything compiles) before implementation begins.
- DB-enforced invariants (such as partial unique indexes, deduplication races, and concurrent process mutations) must be tested through shipped host processes (e.g. `RoleRadar.Mcp.Stdio` or `RoleRadar.Cli`), not merely in-process fixtures.
- Domain models and enums are implemented first.
- Command and query handlers implemented second.
- MCP tools and CLI commands implemented third, extending the living architecture test file and startup smoke test for each entity.
- Story complete and verified before moving to dependent stories.

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001–T003)
2. Complete Phase 2: Foundational (T004–T016, CRITICAL — blocks all stories, establishes data types, `Company` repo, and base architecture/smoke tests)
3. Complete Phase 3: User Story 1 (T017–T027: Postings with Dispositions, Tiered Deduplication, and `tracker_query` postings/decided/dedup-lookup discriminators)
4. **STOP and VALIDATE**: Test User Story 1 independently (`PostingRecordContractTests`, `PostingTransitionContractTests`, `PostingRecordExplicitUpdateTests`, `TrackerQueryPostingsContractTests`, and startup smoke test for postings)
5. Demo / verify core deduplication, screening memory, and query capabilities before proceeding to company management and applications.
