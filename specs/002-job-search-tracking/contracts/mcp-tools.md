# Contract: MCP Stdio Tools (`RoleRadar.Mcp.Stdio`) — Feature 002

**Feature**: `002-job-search-tracking`  
**Host**: `RoleRadar.Mcp.Stdio` (`ModelContextProtocol` `2.2.0`, `Microsoft.Extensions.Hosting` `10.0.13`, zero network listeners, all logs routed to `stderr`).  
**Tool Cap**: Exactly 6 new tools exposed (12 total across Feature 001 and Feature 002, strictly obeying the <=12 limit and Constitution Principle V).  
**Architecture Rules**: Declarative descriptions only without imperative LLM manipulation instructions (`CON-010`). All write operations go through `TransactionAndAuditCommandDecorator` inside MongoDB multi-document transactions. Zero hard delete tools (`CON-004`). Contacts management and referential integrity verification are strictly CLI-only (`FR-028`, `FR-033`, `FR-036`); no MCP tool, parameter, or description references contact data.

---

## Functional Requirements Traceability Matrix

Every spec-mandated requirement for Feature 002 is mapped to its satisfying MCP tool, CLI command, or automated architecture test:

| Spec Requirement | Feature Area | Satisfying Interface / Mechanism | Traceability Status |
| :--- | :--- | :--- | :---: |
| **FR-001** | Posting attributes (IDs, URLs, comp, culture, dates, signals) | `posting_record` | **PASS** |
| **FR-002** | Disposition values (`lead`, `scored`, `applied`, `rejected`, etc.) | `posting_record`, `posting_transition`, `application_record` | **PASS** |
| **FR-003** | Resurface policy (`never`, `if_reopened`) | `posting_record`, `posting_transition` | **PASS** |
| **FR-004** | Deduplication tiers A, B, C, D (computed keys + advisory) | `posting_record` | **PASS** |
| **FR-005** | In-place identifier upgrade from URL to ATS IDs | `posting_record` | **PASS** |
| **FR-006** | URL normalization pipeline & tracking parameter stripping | Domain `UrlNormalizer` via `posting_record` | **PASS** |
| **FR-007** | Re-encounter decided posting (no-op lookup, returns reason/policy) | `posting_record` | **PASS** |
| **FR-008** | Re-encounter active posting (audited bump across UTC days, same-day no-op) | `posting_record` | **PASS** |
| **FR-009** | Zero-change resubmission same-day no-op without audit entry | `posting_record` | **PASS** |
| **FR-010** | Duplicate chain collapsing upon write (no self-reference) | `posting_record` | **PASS** |
| **FR-011** | External narrative note ID linkage (`screening_note_id`) | `posting_record` | **PASS** |
| **FR-012** | Input length bounds & untrusted text validation | `posting_record` | **PASS** |
| **FR-012a** | Ingestion creation dispositions (import any vs MCP lead) | `posting_record` (MCP defaults `lead`), Import handler | **PASS** |
| **FR-013** | Company profile & full ownership enum | `company_record` | **PASS** |
| **FR-014** | Career sources (tenant, slug, fetch_method, notes) | `company_record` | **PASS** |
| **FR-015** | Query companies and career sources | `tracker_query (entity: "companies")` | **PASS** |
| **FR-016** | Criteria attributes & temporal timestamps | `tracker_log (action: "criterion_revision")` | **PASS** |
| **FR-017** | Immutable criteria versioning (close active, insert new) | `tracker_log (action: "criterion_revision")` | **PASS** |
| **FR-018** | Point-in-time criteria evaluation by UTC date | `tracker_query (entity: "criteria", effectiveDate: "...")` | **PASS** |
| **FR-019** | Disposition reason citing criterion ID | `posting_record`, `posting_transition` | **PASS** |
| **FR-020** | Application record with numeric salary & referral contact ID | `application_record` | **PASS** |
| **FR-021** | Raw resume text forbidden; variants/pointers only | `application_record` | **PASS** |
| **FR-022** | Disposition transition matrix (no `applied` via transition) | `posting_transition` (and `application_record` for `applied`) | **PASS** |
| **FR-023** | Reopen-and-apply override flag as 1 audited transaction | `application_record (overrideDecided: true)` | **PASS** |
| **FR-024** | Synchronize posting disposition with application outcomes | `application_record` | **PASS** |
| **FR-025** | Append-only application events timeline (8 event types, offer non-terminal) | `application_record (action: "append_event")` | **PASS** |
| **FR-026** | Posting signals (6 types, observed_at, deadline_date) | `posting_record (signals: [...])` | **PASS** |
| **FR-027** | Dynamic ghost-likelihood on-read (never persisted) | `tracker_query (entity: "postings")` | **PASS** |
| **FR-028** | CLI-only contacts management (`roleradar contact add`) | CLI command `roleradar contact add` | **PASS** |
| **FR-029** | Strict contact isolation from general queries & MCP | CLI-only architecture guard / Architecture tests | **PASS** |
| **FR-030** | Zero contact names in logs, diagnostics, status, or MCP | Architecture tests & CLI implementation | **PASS** |
| **FR-031** | Contact audit summaries contain field names and IDs only | `ContactAuditContentSummary` in `RoleRadar.Domain` | **PASS** |
| **FR-032** | Redact contact operation (`roleradar contact redact`) | CLI command `roleradar contact redact` | **PASS** |
| **FR-033** | Single-entity contact lookup by ID only via CLI | CLI command `roleradar contact get <id>` | **PASS** |
| **FR-034** | Backward-compatible note links (`company_id`, etc.) | Domain `Note` entity + Partial indexes | **PASS** |
| **FR-035** | Search run execution logging linked to report note | `tracker_log (action: "search_run")` | **PASS** |
| **FR-036** | Referential integrity verification across collections | CLI command `roleradar verify` | **PASS** |
| **FR-037** | Integrity check read-only invariant | CLI command `roleradar verify` | **PASS** |
| **FR-038** | Paged, bounded query tools (default 50, max 200) | `tracker_query` | **PASS** |
| **FR-039** | Compact decided-postings query | `tracker_query (entity: "decided_postings")` | **PASS** |
| **FR-040** | Exactly 6 new tools exposed (12 total ceiling) | `RoleRadar.Mcp.Stdio` tools catalog | **PASS** |
| **FR-040a** | 100% operational write coverage via MCP or CLI | All write operations mapped above | **PASS** |
| **FR-041** | `owner_id` on every document | MongoDB document schemas & repositories | **PASS** |
| **FR-042** | Audited transactions for all writes | `TransactionAndAuditCommandDecorator` | **PASS** |
| **FR-043** | Zero hard deletes across all repositories and tools | Architecture reflection tests | **PASS** |
| **FR-044** | Explicit typed operational failures (`ErrorCode`) | `Result<T>` and `ErrorCode` enum | **PASS** |
| **FR-045** | 100% coverage by encrypted backup/restore pipeline | `scripts/backup.ps1` & `scripts/restore.ps1` | **PASS** |
| **FR-046** | Strictly fictional data in all fixtures and tests | Denylist checks and test fixtures | **PASS** |

---

## Tool 1: `posting_record`

- **Purpose**: Creates or updates a job posting with tiered deduplication (Tiers A–D), duplicate collapsing, signals, notes links, culture summary, score, and resurface policy.
- **Handler Mapping**: `ICommandHandler<RecordPostingCommand, PostingRecordResultDto>`
- **Description**: `"Creates or updates a tracked job posting with company, title, URL, ATS identifiers, compensation, location, score, culture rating, screening note link, signals, and resurface policy, enforcing tiered deduplication (tiers A-D)."`
- **Input Parameters**:
  - `companyId` (`Guid`, required): Target company ID.
  - `title` (`string`, required, 1..200 chars): Job posting title.
  - `url` (`string`, optional, max 1000 chars): Primary job posting URL (normalized automatically).
  - `sourceKind` (`string`, required): `"ats"` or `"aggregator"`.
  - `atsTenant` (`string`, optional, max 100 chars): ATS tenant identifier (e.g., `"acmesoftware"`).
  - `requisitionId` (`string`, optional, max 100 chars): Requisition/job ID within the ATS.
  - `location` (`string`, optional, max 150 chars): Geographic location or office name.
  - `remoteType` (`string`, required): `"remote"`, `"hybrid"`, `"onsite"`, or `"unknown"`.
  - `compensationMin` (`decimal`, optional): Minimum base compensation.
  - `compensationMax` (`decimal`, optional): Maximum base compensation.
  - `compensationCurrency` (`string`, optional, default `"USD"`, 3 chars): Currency code.
  - `compensationText` (`string`, optional, max 300 chars): Verbatim compensation snippet or band notes.
  - `postedDate` (`string`, optional, YYYY-MM-DD): Date the role was posted.
  - `score` (`decimal`, optional, 0.0..10.0): Evaluated fit score.
  - `cultureLevel` (`string`, optional, default `"unknown"`): `"stable"`, `"moderate"`, `"high_risk"`, or `"unknown"`.
  - `cultureSummary` (`string`, optional, max 500 chars): Narrative culture observations.
  - `dispositionReason` (`string`, optional, max 500 chars): Rationale for the disposition.
  - `dispositionCriterionId` (`Guid`, optional): Associated screening criterion ID.
  - `resurfacePolicy` (`string`, optional, default `"if_reopened"`): `"never"` or `"if_reopened"`.
  - `screeningNoteId` (`Guid`, optional): Linked screening rationale note ID.
  - `duplicateOfId` (`Guid`, optional): Pointer to canonical posting if this is a duplicate.
  - `signals` (`array of objects`, optional): Array of observed signal objects:
    - `signalType` (`string`, required): `"repost"`, `"no_salary"`, `"parallel_posting"`, `"gone"`, `"phantom"`, or `"deadline"`.
    - `evidence` (`string`, required, max 500 chars): Observed evidence text.
    - `observedAt` (`string`, optional, ISO 8601 UTC timestamp): Timestamp of observation.
    - `deadlineDate` (`string`, optional, YYYY-MM-DD): Target deadline date (when `signalType == "deadline"`).
- **Behavior & Deduplication Tier Handling**:
  - **Tier A** (`atsTenant` + `requisitionId` -> `dedup_key_a`): Evaluates partial unique index.
  - **Tier B** (`normalizedUrl` -> `dedup_key_b`): Evaluates partial unique index.
  - **Tier C** (`companyId` + `requisitionId` -> `dedup_key_c` when `atsTenant` is missing): Evaluates partial unique index.
  - **Tier D** (`companyId` + `normalizedTitle`): Queries non-unique advisory index. If candidate postings found, returns typed `PossibleDuplicate` result with candidates list and dispositions. Does NOT mutate data or auto-merge.
  - **Match on Active Posting** (`lead`, `scored`, `applied`, `unscored`):
    - If re-encountered on a different UTC date: updates `last_verified_at` as a real write, generating an audit entry with a one-field summary (`last_verified_at`). Returns `AlreadyTracked`.
    - If identical to stored state on the same UTC day: returns `AlreadyTracked` with zero writes and zero audit entries.
  - **Match on Decided Posting** (`screened_out`, `closed`, `rejected`):
    - If `resurfacePolicy == "never"`: Returns `AlreadyDecided` with prior reason and `canReopen: false`.
    - If `resurfacePolicy == "if_reopened"`: Returns `AlreadyDecided` with prior reason and `canReopen: true`. Does NOT update `last_verified_at` or write an audit entry.
  - **New Posting**: Created defaulting to `lead` disposition. Emits audit entry.
  - **Duplicate Chain Collapsing**: If `duplicateOfId` points to posting B, which points to posting C, collapses `duplicateOfId` directly to C. Self-referencing duplicate returns `ValidationError`.
- **Output JSON Schema**:
  ```json
  {
    "status": "created | already_tracked | already_decided | possible_duplicate",
    "posting": {
      "id": "0199f2c0-0000-7000-8000-000000000101",
      "companyId": "0199f2c0-0000-7000-8000-000000000050",
      "title": "Staff Backend Engineer",
      "normalizedTitle": "staff backend engineer",
      "disposition": "lead",
      "resurfacePolicy": "if_reopened",
      "versionToken": "0199f2c0-0000-7000-8000-000000000102"
    },
    "priorReason": null,
    "canReopen": null,
    "candidates": []
  }
  ```

---

## Tool 2: `posting_transition`

- **Purpose**: Executes valid state transitions on job postings, recording disposition reasons, referenced criteria, or explicit reopenings.
- **Handler Mapping**: `ICommandHandler<TransitionPostingCommand, PostingTransitionResultDto>`
- **Description**: `"Transitions a posting between dispositions (lead, scored, unscored, screened_out, closed, rejected) or reopens a decided posting, validating against the pure domain transition matrix. Note: target 'applied' is disallowed (applications must be created via application_record)."`
- **Input Parameters**:
  - `postingId` (`Guid`, required): Unique identifier of the posting.
  - `targetDisposition` (`string`, required): Permitted values: `"lead"`, `"scored"`, `"unscored"`, `"screened_out"`, `"closed"`, or `"rejected"`. (`"applied"` is strictly forbidden and rejected with `InvalidTransition`).
  - `reason` (`string`, optional, max 500 chars): Explanation for the disposition change. Required when transitioning to `screened_out`, `closed`, or `rejected`.
  - `criterionId` (`Guid`, optional): Associated screening criterion ID justifying the transition.
  - `resurfacePolicy` (`string`, optional): Updated resurface policy (`"never"` or `"if_reopened"`).
  - `overrideReason` (`string`, optional, max 500 chars): Required reason if explicitly reopening a `rejected` posting.
  - `versionToken` (`Guid`, required): Optimistic concurrency token.
- **Behavior**:
  - Evaluates requested transition against pure domain transition matrix.
  - `targetDisposition == "applied"` rejects immediately with `ErrorCode.InvalidTransition` ("applied is reachable only via application creation").
  - `screened_out` or `closed` -> `lead` requires `resurfacePolicy == "if_reopened"`.
  - `rejected` -> `lead` requires explicit non-empty `overrideReason`.
  - Atomically updates posting version token and disposition in Mongo inside transaction, recording `audit_log` entry.
- **Output JSON Schema**:
  ```json
  {
    "postingId": "0199f2c0-0000-7000-8000-000000000101",
    "previousDisposition": "lead",
    "currentDisposition": "screened_out",
    "dispositionReason": "Below compensation threshold",
    "criterionId": "0199f2c0-0000-7000-8000-000000000010",
    "versionToken": "0199f2c0-0000-7000-8000-000000000103",
    "updatedAtUtc": "2026-10-17T20:00:00Z"
  }
  ```

---

## Tool 3: `company_record`

- **Purpose**: Creates or updates a company profile, category tags, ownership type, and career sources.
- **Handler Mapping**: `ICommandHandler<RecordCompanyCommand, CompanyRecordResultDto>`
- **Description**: `"Creates or updates a tracked company profile with ownership type, category tags, description, and career sources (ATS endpoints)."`
- **Input Parameters**:
  - `id` (`Guid`, optional): Company ID (generated if omitted).
  - `name` (`string`, required, 1..150 chars): Unique company name (case-insensitive deduplication).
  - `description` (`string`, optional, max 300 chars): Bounded summary of industry, ownership context, and headquarters.
  - `parentCompany` (`string`, optional, max 150 chars): Parent company name.
  - `ownershipType` (`string`, required): Full spec enum: `"public"`, `"private"`, `"pe_backed"`, `"vc_backed"`, `"mutual"`, `"subsidiary"`, `"nonprofit"`, `"government"`, or `"unknown"`.
  - `headquarters` (`string`, optional, max 150 chars): Primary headquarters location.
  - `status` (`string`, required): `"target"`, `"watch"`, `"avoid"`, or `"neutral"`.
  - `categoryTags` (`array of strings`, optional, max 20 tags, each max 50 chars): Industry and category tags.
  - `careerSources` (`array of objects`, optional): List of career sources:
    - `id` (`Guid`, optional): Career source ID.
    - `atsType` (`string`, required): `"Workday"`, `"Greenhouse"`, `"Ashby"`, `"Lever"`, `"Avature"`, `"Oracle"`, `"UKG"`, or `"Custom"`.
    - `tenant` (`string`, optional, max 100 chars): ATS tenant identifier.
    - `slug` (`string`, optional, max 150 chars): Portal URL slug.
    - `fetchMethod` (`string`, required): `"api"`, `"browser"`, or `"blocked"`.
    - `notes` (`string`, optional, max 500 chars): Navigation and operational notes.
    - `versionToken` (`Guid`, optional): Version token if updating career source.
  - `versionToken` (`Guid`, optional): Required if updating an existing company.
- **Behavior**:
  - Company names are unique per owner (unique index on `{ owner_id, normalized_name }`).
  - Career sources enforce unique index on `{ owner_id, company_id, ats_type, tenant, slug }`.
  - Atomically records company and career sources in one audited transaction.
- **Output JSON Schema**:
  ```json
  {
    "company": {
      "id": "0199f2c0-0000-7000-8000-000000000050",
      "name": "Acme Corp",
      "description": "Enterprise cloud security platform; Series D.",
      "ownershipType": "vc_backed",
      "status": "target",
      "versionToken": "0199f2c0-0000-7000-8000-000000000051",
      "careerSourceCount": 1
    }
  }
  ```

---

## Tool 4: `application_record`

- **Purpose**: Creates an application, records timeline events, and supports atomic reopen-and-apply override for decided roles.
- **Handler Mapping**: `ICommandHandler<RecordApplicationCommand, ApplicationRecordResultDto>`
- **Description**: `"Creates or updates a job application, appends timeline events, records resume variant pointers and desired salary, and supports atomic reopen-and-apply override for decided postings."`
- **Input Parameters**:
  - `postingId` (`Guid`, required): Posting being applied to.
  - `action` (`string`, required): `"create"` or `"append_event"`.
  - `submittedDate` (`string`, optional, YYYY-MM-DD): Submission date (required for `"create"`).
  - `desiredSalaryMin` (`decimal`, optional): Desired minimum base salary.
  - `desiredSalaryMax` (`decimal`, optional): Desired maximum base salary.
  - `desiredSalaryCurrency` (`string`, optional, default `"USD"`, 3 chars): Currency code.
  - `targetStartDate` (`string`, optional, YYYY-MM-DD): Target start date.
  - `resumeVariant` (`string`, optional): `"tailored"`, `"as_is"`, or `"ic_framed"` (required for `"create"`).
  - `resumePointer` (`string`, optional, max 300 chars): External path/identifier (file content is strictly forbidden).
  - `referralContactId` (`Guid`, optional): Optional referral contact ID.
  - `confirmationCode` (`string`, optional, max 100 chars): Confirmation reference code.
  - `overrideDecided` (`bool`, optional, default false): Flag required to apply to a `screened_out` or `closed` posting.
  - `overrideReason` (`string`, optional, max 500 chars): Justification required when `overrideDecided == true`.
  - `event` (`object`, optional): Timeline event payload (required if `action == "append_event"`):
    - `eventType` (`string`, required): Full spec enum: `"applied"`, `"follow_up"`, `"interview"`, `"offer"`, `"offer_accepted"`, `"offer_declined"`, `"rejected"`, or `"withdrawn"`.
    - `eventDate` (`string`, required, YYYY-MM-DD): Event date.
    - `eventSource` (`string`, optional, max 100 chars): Originating channel or portal.
    - `notes` (`string`, optional, max 500 chars): Discussion summary.
- **Behavior**:
  - `action == "create"`:
    - If posting is in `lead`, `unscored`, or `scored`: transitions posting to `applied` and inserts `applications` record.
    - If posting is in `screened_out` or `closed`:
      - If `overrideDecided == false`: returns `ErrorCode.InvalidTransition`.
      - If `overrideDecided == true`: executes atomic reopen-and-apply. Sets posting to `applied`, logs override in `audit_log` with previous disposition and policy, and inserts application.
  - `action == "append_event"`:
    - Appends event to `application_events` (append-only collection).
    - If `eventType == "offer"`: non-terminal; posting stays in `applied`.
    - If `eventType == "rejected"`: synchronizes posting disposition to `rejected`.
    - If `eventType == "offer_accepted"`, `"offer_declined"`, or `"withdrawn"`: synchronizes posting disposition to `closed` with event notes as reason.
- **Output JSON Schema**:
  ```json
  {
    "applicationId": "0199f2c0-0000-7000-8000-000000000201",
    "postingId": "0199f2c0-0000-7000-8000-000000000101",
    "currentOutcome": "applied",
    "overrideApplied": true,
    "eventCount": 1,
    "updatedAtUtc": "2026-10-17T20:15:00Z"
  }
  ```

---

## Tool 5: `tracker_log`

- **Purpose**: Appends immutable operational records for screening criteria revisions and search run execution summaries.
- **Handler Mapping**: `ICommandHandler<TrackerLogCommand, TrackerLogResultDto>`
- **Description**: `"Appends immutable operational tracking records: creates/revises screening criteria (closing prior version) or logs search run execution batches linked to report notes."`
- **Input Parameters**:
  - `action` (`string`, required): `"criterion_revision"` or `"search_run"`.
  - **Criterion Revision Parameters** (when `action == "criterion_revision"`):
    - `key` (`string`, required, max 100 chars): Screening criterion key.
    - `category` (`string`, required): `"hard_skip"`, `"weight"`, or `"preference"`.
    - `value` (`string`, required, max 500 chars): Rule specification text or threshold.
    - `provenance` (`string`, required): `"stated"` or `"inferred"`.
    - `effectiveFrom` (`string`, required, ISO 8601 UTC timestamp): Timestamp when rule becomes active.
    - `versionToken` (`Guid`, optional): Current version token of the active criterion being superseded.
  - **Search Run Parameters** (when `action == "search_run"`):
    - `startedAt` (`string`, required, ISO 8601 UTC timestamp): Start time of the search sweep.
    - `newPostingsCount` (`int`, required, >= 0): Count of newly discovered postings.
    - `reportNoteId` (`Guid`, optional): Linked narrative report note ID.
    - `notes` (`string`, optional, max 500 chars): Summary notes.
- **Behavior**:
  - `"criterion_revision"`:
    - Atomically locates current active version of `key`, verifies `versionToken`, sets its `effective_to = effectiveFrom` and `is_active = false`.
    - Inserts new criterion version with `is_active = true`, `effective_from = effectiveFrom`, `effective_to = null`.
    - Generates `audit_log` entry.
  - `"search_run"`:
    - Appends record to `search_runs` collection and logs audit entry.
- **Output JSON Schema**:
  ```json
  {
    "action": "criterion_revision",
    "recordId": "0199f2c0-0000-7000-8000-000000000010",
    "status": "success",
    "updatedAtUtc": "2026-10-17T20:30:00Z"
  }
  ```

---

## Tool 6: `tracker_query`

- **Purpose**: Unified read-only query tool with a mandatory discriminator parameter, covering postings, decided postings, explicit posting deduplication key lookup, companies, criteria, and applications. (No contact data reachable).
- **Handler Mapping**: `IQueryHandler<TrackerQueryRequest, TrackerQueryResultDto>`
- **Description**: `"Unified read-only query tool for job search tracking data (postings, decided_postings, posting_dedup_lookup, companies, criteria, applications) with bounded paging (default 50, max 200)."`
- **Input Parameters**:
  - `entity` (`string`, required): Discriminator: `"postings"`, `"decided_postings"`, `"posting_dedup_lookup"`, `"companies"`, `"criteria"`, or `"applications"`.
  - `companyId` (`Guid`, optional): Filter by company.
  - `disposition` (`string`, optional): Filter by posting disposition (`lead`, `applied`, etc.).
  - `effectiveDate` (`string`, optional, YYYY-MM-DD): For `entity == "criteria"`, retrieves active criteria at this historical date.
  - `includeStale` (`bool`, optional, default false): If true, returns companies overdue for checking.
  - `dedupKey` (`string`, optional): For `entity == "posting_dedup_lookup"`, lookup value for Tier A (`tenant|requisition`), Tier B (`normalizedUrl`), or Tier C (`companyId|requisition`).
  - `page` (`int`, optional, default 1): Page index (`>= 1`).
  - `pageSize` (`int`, optional, default 50): Bounded page size (`1..200`; default 50).
- **Behavior by Entity Discriminator**:
  - `"postings"`: Returns paged postings matching filters. Calculates `ghostLikelihood` dynamically on read (never persisted).
  - `"decided_postings"`: Returns compact list (`id`, `title`, `companyId`, `disposition`, `dispositionReason`, `resurfacePolicy`, `decidedAtUtc`) optimized for screening caches.
  - `"posting_dedup_lookup"`: Read-only single posting lookup by exact Tier A, B, or C key without mutating data.
  - `"companies"`: Returns tracked company profiles, career source endpoints, and last-checked timestamps.
  - `"criteria"`: If `effectiveDate` is provided, filters criteria where `effective_from <= effectiveDate` and (`effective_to == null` or `effective_to > effectiveDate`).
  - `"applications"`: Returns active applications with latest timeline events.
  - Note: Any attempt to query contacts via `tracker_query` returns `ErrorCode.ValidationError` ("Contacts are accessible exclusively via CLI").
- **Output JSON Schema**:
  ```json
  {
    "entity": "postings",
    "page": 1,
    "pageSize": 50,
    "totalCount": 142,
    "items": [
      {
        "id": "0199f2c0-0000-7000-8000-000000000101",
        "title": "Principal Architect",
        "companyName": "Acme Corp",
        "disposition": "applied",
        "ghostLikelihood": "low",
        "lastVerifiedAtUtc": "2026-10-17T18:00:00Z"
      }
    ]
  }
  ```
