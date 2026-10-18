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
| **FR-019** | Disposition reason citing criterion ID | `posting_transition` (and import path) | **PASS** |
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

- **Purpose**: Creates, ingests, or explicitly updates a job posting with tiered deduplication (Tiers A–D), duplicate collapsing, signals, notes links, culture summary, score, and resurface policy.
- **Handler Mapping**: `ICommandHandler<RecordPostingCommand, PostingRecordResultDto>`
- **Description**: `"Creates, ingests, or explicitly updates a tracked job posting with company, title, URL, ATS identifiers, compensation, location, score, culture rating, screening note link, signals, and resurface policy, enforcing tiered deduplication (tiers A-D)."`
- **Input Parameters**:
  - `postingId` (`Guid`, optional): When provided with `versionToken`, activates explicit update mode on the existing posting.
  - `versionToken` (`Guid`, optional): Concurrency token required when `postingId` is provided for update mode.
  - `companyId` (`Guid`, required for create/ingest; optional for update): Target company ID. Validates that the company exists in `companies`; returns `ErrorCode.ValidationError` if the company is not found.
  - `title` (`string`, required for create/ingest; optional for update, 1..200 chars): Job posting title.
  - `url` (`string`, optional, max 1000 chars): Primary job posting URL (normalized automatically).
  - `sourceKind` (`string`, required for create/ingest; optional for update): `"ats"` or `"aggregator"`.
  - `atsTenant` (`string`, optional, max 100 chars): ATS tenant identifier (e.g., `"acmesoftware"`).
  - `requisitionId` (`string`, optional, max 100 chars): Requisition/job ID within the ATS.
  - `location` (`string`, optional, max 150 chars): Geographic location or office name.
  - `remoteType` (`string`, optional, default `"unknown"`): `"remote"`, `"hybrid"`, `"onsite"`, or `"unknown"`.
  - `compensationMin` (`decimal`, optional): Minimum base compensation.
  - `compensationMax` (`decimal`, optional): Maximum base compensation.
  - `compensationCurrency` (`string`, optional, default `"USD"`, 3 chars): Currency code.
  - `compensationText` (`string`, optional, max 300 chars): Verbatim compensation snippet or band notes.
  - `postedDate` (`string`, optional, YYYY-MM-DD): Date the role was posted.
  - `score` (`decimal`, optional, 0.0..10.0): Evaluated fit score.
  - `cultureLevel` (`string`, optional, default `"unknown"`): `"stable"`, `"moderate"`, `"high_risk"`, or `"unknown"`.
  - `cultureSummary` (`string`, optional, max 500 chars): Narrative culture observations.
  - `resurfacePolicy` (`string`, optional, default `"never"`): Resurface policy (`"never"` or `"if_reopened"`). Default is `"never"`.
  - `screeningNoteId` (`Guid`, optional): Linked screening rationale note ID.
  - `duplicateOfId` (`Guid`, optional): Pointer to canonical posting if this is a duplicate.
  - `signals` (`array of objects`, optional): Array of observed signal objects:
    - `signalType` (`string`, required): `"repost"`, `"no_salary"`, `"parallel_posting"`, `"gone"`, `"phantom"`, or `"deadline"`.
    - `evidence` (`string`, required, max 500 chars): Observed evidence text.
    - `observedAt` (`string`, optional, ISO 8601 UTC timestamp): Timestamp of observation.
    - `deadlineDate` (`string`, optional, YYYY-MM-DD): Target deadline date (when `signalType == "deadline"`).
- **Behavior & Modes**:
  - **Explicit Update Mode** (`postingId` provided):
    - Requires matching `versionToken`; if `versionToken` does not match stored document, returns typed `ErrorCode.StaleVersionToken`.
    - Modifies **only** the supplied fields; unsupplied fields remain unchanged.
    - Audit entry records the list of changed field names and the posting ID in its summary.
  - **Ingest / Deduplication Match Mode** (no `postingId` provided):
    - Evaluates Tier A (`atsTenant` + `requisitionId`), Tier B (`normalizedUrl`), and Tier C (`companyId` + `requisitionId`).
    - On a dedup match (Tiers A–C): **fields other than `last_verified_at` are NEVER changed**. If supplied field values differ from stored ones (e.g. different compensation or remote type), the system does NOT mutate them; instead, it returns them in a `differences` list (`field`, `storedValue`, `suppliedValue`).
    - `already_tracked`, `already_decided`, and `possible_duplicate` are returned as SUCCESSFUL (`Result.Success`) outcomes with a status field, ensuring the transaction decorator commits any modifications:
      - If active (`lead`, `scored`, `applied`, `unscored`) and re-encountered on a different UTC date: updates `last_verified_at` as a real write, generating an audit entry with a one-field summary (`last_verified_at`), and returns `status: "already_tracked"` (commits).
      - If re-encountered on the same UTC day with identical state: returns `status: "already_tracked"` with zero writes and zero audit entries.
      - If decided (`screened_out`, `closed`, `rejected`): returns `status: "already_decided"` with `canReopen` indicator and prior reason; mutates nothing.
      - Tier D (`companyId` + `normalizedTitle`): Queries non-unique advisory index. Returns `status: "possible_duplicate"` with candidate list of dispositions and reasons; never mutates records.
  - **New Posting Creation Mode**:
    - Created defaulting to `lead` disposition. Emits audit entry and returns `status: "created"`.
  - **Duplicate Chain Collapsing**: If `duplicateOfId` points to posting B, which points to posting C, collapses `duplicateOfId` directly to C. Self-referencing duplicate returns `ErrorCode.ValidationError`.
- **Output JSON Schema**:
  ```json
  {
    "status": "created | updated | already_tracked | already_decided | possible_duplicate",
    "posting": {
      "id": "0199f2c0-0000-7000-8000-000000000101",
      "companyId": "0199f2c0-0000-7000-8000-000000000050",
      "title": "Staff Backend Engineer",
      "normalizedTitle": "staff backend engineer",
      "disposition": "lead",
      "resurfacePolicy": "never",
      "versionToken": "0199f2c0-0000-7000-8000-000000000102"
    },
    "priorReason": null,
    "canReopen": null,
    "candidates": [],
    "differences": []
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
  - `resurfacePolicy` (`string`, optional, default `"never"`): Sets the resurface policy at decision time (`screened_out` or `closed`). Default is `"never"`.
  - `overrideReason` (`string`, optional, max 500 chars): Required reason if explicitly reopening a `rejected` posting.
  - `versionToken` (`Guid`, required): Optimistic concurrency token.
- **Behavior**:
  - Evaluates requested transition against pure domain transition matrix.
  - `targetDisposition == "applied"` rejects immediately with `ErrorCode.InvalidTransition` ("applied is reachable only via application creation").
  - `screened_out` or `closed` -> `lead` evaluates the **stored** `resurface_policy` on the posting (must equal `"if_reopened"`; if `"never"`, rejects with `ErrorCode.InvalidTransition`).
  - Transitioning into `screened_out`, `closed`, or `rejected` sets `decided_at` to current UTC timestamp and sets `resurface_policy` to supplied `resurfacePolicy` (default `"never"`).
  - `rejected` -> `lead` requires explicit non-empty `overrideReason`.
  - Atomically updates posting version token, disposition, `decided_at`, and `resurface_policy` in Mongo inside transaction, recording `audit_log` entry.
- **Output JSON Schema**:
  ```json
  {
    "postingId": "0199f2c0-0000-7000-8000-000000000101",
    "previousDisposition": "lead",
    "currentDisposition": "screened_out",
    "dispositionReason": "Below compensation threshold",
    "criterionId": "0199f2c0-0000-7000-8000-000000000010",
    "resurfacePolicy": "never",
    "decidedAtUtc": "2026-10-17T20:00:00Z",
    "versionToken": "0199f2c0-0000-7000-8000-000000000103",
    "updatedAtUtc": "2026-10-17T20:00:00Z"
  }
  ```

---

## Tool 3: `company_record`

- **Purpose**: Creates or updates a company profile, category tags, ownership type, and career sources (upsert by id or by ats_type, tenant, slug with explicit active/retired state).
- **Handler Mapping**: `ICommandHandler<RecordCompanyCommand, CompanyRecordResultDto>`
- **Description**: `"Creates or updates a tracked company profile with ownership type, category tags, description, and career sources (ATS endpoints) with upsert by id or natural key without accidental source deletion."`
- **Input Parameters**:
  - `id` (`Guid`, optional): Company ID (generated if omitted).
  - `name` (`string`, required, 1..150 chars): Unique company name (case-insensitive deduplication).
  - `description` (`string`, optional, max 300 chars): Bounded summary of industry, ownership context, and headquarters.
  - `parentCompany` (`string`, optional, max 150 chars): Parent company name.
  - `ownershipType` (`string`, required): Full spec enum: `"public"`, `"private"`, `"pe_backed"`, `"vc_backed"`, `"mutual"`, `"subsidiary"`, `"nonprofit"`, `"government"`, or `"unknown"`.
  - `headquarters` (`string`, optional, max 150 chars): Primary headquarters location.
  - `status` (`string`, required): `"target"`, `"watch"`, `"avoid"`, or `"neutral"`.
  - `categoryTags` (`array of strings`, optional, max 20 tags, each max 50 chars): Industry and category tags.
  - `careerSources` (`array of objects`, optional): List of career sources to upsert:
    - `id` (`Guid`, optional): Career source ID (if provided, upserts by ID).
    - `atsType` (`string`, required): `"Workday"`, `"Greenhouse"`, `"Ashby"`, `"Lever"`, `"Avature"`, `"Oracle"`, `"UKG"`, or `"Custom"`.
    - `tenant` (`string`, optional, max 100 chars): ATS tenant identifier.
    - `slug` (`string`, optional, max 150 chars): Portal URL slug.
    - `fetchMethod` (`string`, required): `"api"`, `"browser"`, or `"blocked"`.
    - `notes` (`string`, optional, max 500 chars): Navigation and operational notes.
    - `isActive` (`bool`, optional, default true): Explicitly activate or retire this career source.
    - `versionToken` (`Guid`, optional): Version token if updating career source.
  - `versionToken` (`Guid`, optional): Required if updating an existing company.
- **Behavior**:
  - Company names are unique per owner (unique index on `{ owner_id, normalized_name }`).
  - Career sources are upserted matching on `id` (if supplied) or the composite key `(atsType, tenant, slug)`. **Omission of existing career sources from the `careerSources` array NEVER removes or deletes them**.
  - Setting `isActive: false` retires a career source; retired sources are excluded from `tracker_query` results unless `includeRetired == true`.
  - Atomically records company and career source upserts in one audited transaction.
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
    - **Behavior on already decided postings**: If the parent posting is already in `rejected` or `closed`, the event is still appended to the application's timeline, and the posting disposition remains unchanged (`rejected` or `closed`).
    - Otherwise, synchronizes posting disposition:
      - `eventType == "offer"`: non-terminal; posting stays in `applied`.
      - `eventType == "rejected"`: synchronizes posting disposition to `rejected`.
      - `eventType == "offer_accepted"`, `"offer_declined"`, or `"withdrawn"`: synchronizes posting disposition to `closed` with event notes as reason.
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

- **Purpose**: Appends immutable operational records for screening criteria revisions/deactivations and search run execution summaries.
- **Handler Mapping**: `ICommandHandler<TrackerLogCommand, TrackerLogResultDto>`
- **Description**: `"Appends immutable operational tracking records: creates/revises/deactivates screening criteria or logs search run execution batches linked to report notes."`
- **Input Parameters**:
  - `action` (`string`, required): `"criterion_revision"`, `"criterion_deactivate"`, or `"search_run"`.
  - **Criterion Parameters** (when `action == "criterion_revision"` or `"criterion_deactivate"`):
    - `key` (`string`, required, max 100 chars): Screening criterion key.
    - `category` (`string`, optional): `"hard_skip"`, `"weight"`, or `"preference"` (required for revision).
    - `value` (`string`, optional, max 500 chars): Rule specification text or threshold (required for revision).
    - `provenance` (`string`, optional): `"stated"` or `"inferred"` (required for revision).
    - `effectiveFrom` (`string`, optional, ISO 8601 UTC timestamp): Timestamp when rule becomes active (required for revision; MUST be later than active version's `effective_from`).
    - `versionToken` (`Guid`, optional): Current version token of the active criterion being superseded or deactivated. (Mandatory if active criterion exists; omission returns `ErrorCode.StaleVersionToken`).
  - **Search Run Parameters** (when `action == "search_run"`):
    - `startedAt` (`string`, required, ISO 8601 UTC timestamp): Start time of the search sweep.
    - `newPostingsCount` (`int`, required, >= 0): Count of newly discovered postings.
    - `reportNoteId` (`Guid`, optional): Linked narrative report note ID.
    - `notes` (`string`, optional, max 500 chars): Summary notes.
- **Behavior**:
  - `"criterion_revision"`:
    - Locates active version of `key`. If exists and `versionToken` is null or mismatched, returns `ErrorCode.StaleVersionToken`.
    - Validates that `effectiveFrom` is strictly later than the active version's `effective_from`; returns `ErrorCode.ValidationError` otherwise.
    - Atomically sets active version's `effective_to = effectiveFrom` and `is_active = false`.
    - Inserts new criterion version with `is_active = true`, `effective_from = effectiveFrom`, `effective_to = null`. Emits `audit_log` entry.
  - `"criterion_deactivate"`:
    - Locates active version of `key`. If `versionToken` is null or mismatched, returns `ErrorCode.StaleVersionToken`.
    - Closes active version by setting `effective_to = UtcNow` and `is_active = false`. No replacement version is inserted. Emits `audit_log` entry.
  - `"search_run"`:
    - Enforces unique index on `(owner_id, started_at)`.
    - If a run with identical `(owner_id, startedAt)` exists, returns the existing record with `status: "already_logged"` without throwing or duplicating. Emits audit entry on new inserts.
- **Output JSON Schema**:
  ```json
  {
    "action": "criterion_revision | criterion_deactivate | search_run",
    "recordId": "0199f2c0-0000-7000-8000-000000000010",
    "status": "success | already_logged",
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
  - `postingId` (`Guid`, optional): Single posting lookup for `entity == "postings"`.
  - `companyId` (`Guid`, optional): Filter by company.
  - `disposition` (`string`, optional): Filter by posting disposition (`lead`, `applied`, etc.).
  - `status` (`string`, optional): For `entity == "companies"`, filter by tracking status (`target`, `watch`, `avoid`, `neutral`).
  - `tag` (`string`, optional): For `entity == "companies"`, filter by category tag.
  - `name` (`string`, optional): For `entity == "companies"`, filter by name prefix/substring.
  - `includeRetired` (`bool`, optional, default false): For `entity == "companies"`, whether to include retired career sources (`is_active: false`).
  - `includeStale` (`bool`, optional, default false): If true, returns companies overdue for checking based on `last_checked_at` and `staleAfterDays`.
  - `staleAfterDays` (`int`, optional, default 7): Threshold days used with `includeStale`.
  - `effectiveDate` (`string`, optional, YYYY-MM-DD): For `entity == "criteria"`, retrieves active criteria at this historical date.
  - `dedupKey` (`string`, optional): For `entity == "posting_dedup_lookup"`, lookup value for Tier A (`tenant|requisition`), Tier B (`normalizedUrl`), or Tier C (`companyId|requisition`).
  - `page` (`int`, optional, default 1): Page index (`>= 1`).
  - `pageSize` (`int`, optional, default 50): Bounded page size (`1..200`; default 50) enforced across all entities.
- **Behavior by Entity Discriminator**:
  - `"postings"`: If `postingId` is provided, returns the single posting. Otherwise returns paged postings matching filters. Calculates `ghostLikelihood` dynamically on read (never persisted).
  - `"decided_postings"`: Returns compact list (`id`, `title`, `companyId`, `disposition`, `dispositionReason`, `resurfacePolicy`, `decidedAtUtc`) ordered by `decided_at` descending.
  - `"posting_dedup_lookup"`: Read-only single posting lookup by exact Tier A, B, or C key without mutating data.
  - `"companies"`: Returns tracked company profiles, active career source endpoints (or retired if `includeRetired == true`), and last-checked timestamps. Filters by `status`, `tag`, `name`, or `includeStale` (comparing `last_checked_at` against `staleAfterDays`).
  - `"criteria"`: If `effectiveDate` is provided, filters criteria where `effective_from <= effectiveDate` and (`effective_to == null` or `effective_to > effectiveDate`).
  - `"applications"`: Returns active applications with latest timeline events.
  - Note: Any attempt to query contacts via `tracker_query` returns `ErrorCode.ValidationError` ("Contacts are accessible exclusively via CLI").
- **Output JSON Schema**:
  ```json
  {
    "entity": "postings | decided_postings | posting_dedup_lookup | companies | criteria | applications",
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
