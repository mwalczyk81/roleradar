# Contract: MCP Stdio Tools (`RoleRadar.Mcp.Stdio`) — Feature 002

**Feature**: `002-job-search-tracking`  
**Host**: `RoleRadar.Mcp.Stdio` (`ModelContextProtocol` `2.2.0`, `Microsoft.Extensions.Hosting` `10.0.13`, zero network listeners, all logs routed to `stderr`).  
**Tool Cap**: Exactly 6 new tools exposed (12 total across Feature 001 and Feature 002, strictly obeying the <=12 limit and Constitution Principle V).  
**Architecture Rules**: Declarative descriptions only without imperative LLM manipulation instructions (`CON-010`). All write operations go through `TransactionAndAuditCommandDecorator` inside MongoDB multi-document transactions. Zero hard delete tools (`CON-004`).

---

## Tool 1: `posting_record`

- **Purpose**: Creates or updates a job posting with tiered deduplication (tiers A–D), duplicate collapsing, signals, notes links, and duplicate pointers.
- **Handler Mapping**: `ICommandHandler<RecordPostingCommand, PostingRecordResultDto>`
- **Description**: `"Creates or updates a tracked job posting with company, title, URL, ATS identifiers, compensation, location, culture risk level, screening note links, and signals, enforcing tiered deduplication (tiers A-D)."`
- **Input Parameters**:
  - `companyId` (`Guid`, required): Target company ID.
  - `title` (`string`, required, 1..200 chars): Job posting title.
  - `url` (`string`, optional, URI format): Primary job posting URL (normalized automatically).
  - `sourceKind` (`string`, required): `"ats"` or `"aggregator"`.
  - `atsTenant` (`string`, optional, 1..100 chars): ATS tenant identifier (e.g., `"acmecorp"`, `"octanecorp"`).
  - `requisitionId` (`string`, optional, 1..100 chars): Requisition/job ID within the ATS.
  - `location` (`string`, optional, max 200 chars): Geographic location or office name.
  - `remoteType` (`string`, required): `"remote"`, `"hybrid"`, or `"onsite"`.
  - `compensationMin` (`decimal`, optional): Minimum base compensation.
  - `compensationMax` (`decimal`, optional): Maximum base compensation.
  - `compensationText` (`string`, optional, max 200 chars): Verbatim compensation snippet or band notes.
  - `cultureLevel` (`string`, optional, default `"unknown"`): `"stable"`, `"moderate"`, `"high_risk"`, or `"unknown"`.
  - `postedDate` (`string`, optional, YYYY-MM-DD): Date the role was posted.
  - `resurfacePolicy` (`string`, optional, default `"if_reopened"`): `"never"` or `"if_reopened"`.
  - `noteId` (`Guid`, optional): Linked screening rationale note ID.
  - `duplicateOf` (`Guid`, optional): Pointer to canonical posting if this is a known duplicate.
  - `signals` (`array of objects`, optional): Array of signal objects (`kind`: string 1..64 chars, `detail`: string max 500 chars).
- **Behavior & Deduplication Tier Handling**:
  - **Tier A** (`atsTenant` + `requisitionId`): Matches existing posting.
  - **Tier B** (`normalizedUrl`): Matches existing posting.
  - **Tier C** (`companyId` + `requisitionId` when no tenant): Matches existing posting.
  - **Tier D** (`companyId` + `normalizedTitle`): If candidate postings found, returns typed `PossibleDuplicate` result with candidate list and dispositions. Does NOT mutate data or auto-merge.
  - **Match on Active Posting** (`lead`, `scored`, `applied`, `unscored`): Returns `AlreadyTracked` with current state; updates `last_verified_at_utc` in place without writing an audit entry if content is unchanged.
  - **Match on Decided Posting** (`screened_out`, `closed`, `rejected`):
    - If `resurfacePolicy == "never"`: Returns `AlreadyDecided` with prior reason and `canReopen: false`.
    - If `resurfacePolicy == "if_reopened"`: Returns `AlreadyDecided` with prior reason and `canReopen: true`. Does NOT update `last_verified_at_utc` or write an audit entry.
  - **New Posting**: Created in `lead` disposition. Emits audit entry.
  - **Duplicate Chain Collapsing**: If `duplicateOf` points to posting B, which points to posting C, collapses `duplicateOf` directly to C. Self-referencing duplicate returns `ValidationError`.
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
- **Description**: `"Transitions a posting between dispositions (lead, scored, unscored, applied, screened_out, closed, rejected) or reopens a decided posting, validating against the transition matrix."`
- **Input Parameters**:
  - `postingId` (`Guid`, required): Unique identifier of the posting.
  - `targetDisposition` (`string`, required): `"lead"`, `"scored"`, `"unscored"`, `"applied"`, `"screened_out"`, `"closed"`, or `"rejected"`.
  - `reason` (`string`, optional, max 500 chars): Explanation for the disposition change. Required when transitioning to `screened_out`, `closed`, or `rejected`.
  - `criterionId` (`Guid`, optional): Associated screening criterion ID justifying the transition.
  - `overrideReason` (`string`, optional, max 500 chars): Required reason if explicitly reopening a `rejected` posting.
  - `versionToken` (`Guid`, required): Optimistic concurrency token.
- **Behavior**:
  - Evaluates requested transition against pure domain transition matrix.
  - Invalid transitions reject immediately with `ErrorCode.InvalidTransition`.
  - `screened_out` or `closed` -> `lead` requires `resurfacePolicy == "if_reopened"`.
  - `rejected` -> `lead` requires explicit non-empty `overrideReason`.
  - Updates posting version token and disposition in Mongo inside transaction, recording `audit_log` entry with prior and new disposition.
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

- **Purpose**: Creates or updates a company profile, category tags, ownership type, career check runbook, and career sources.
- **Handler Mapping**: `ICommandHandler<RecordCompanyCommand, CompanyRecordResultDto>`
- **Description**: `"Creates or updates a tracked company profile with ownership type, category tags, career check instructions, and career sources (ATS endpoints)."`
- **Input Parameters**:
  - `id` (`Guid`, optional): Company ID (generated if omitted).
  - `name` (`string`, required, 1..100 chars): Unique company name (case-insensitive deduplication).
  - `description` (`string`, optional, max 300 chars): Bounded summary of industry, ownership context, and headquarters.
  - `ownershipType` (`string`, required): `"public"`, `"private"`, `"pe_backed"`, `"vc_backed"`, or `"other"`.
  - `headquarters` (`string`, optional, max 100 chars): Primary headquarters location.
  - `status` (`string`, required): `"target"`, `"watch"`, `"avoid"`, or `"neutral"`.
  - `categoryTags` (`array of strings`, optional, max 16 tags): Industry and category tags.
  - `careerSources` (`array of objects`, optional): List of career sources:
    - `id` (`Guid`, optional): Career source ID.
    - `atsType` (`string`, required): `"workday"`, `"greenhouse"`, `"ashby"`, `"lever"`, `"avature"`, `"oracle"`, `"ukg"`, or `"other"`.
    - `careersUrl` (`string`, required, URI): Entry URL for job search.
    - `apiEndpoint` (`string`, optional, URI): Optional backend job board JSON/API URL.
    - `checkIntervalDays` (`int`, optional, default 7): Cadence for checking postings.
    - `checkInstructions` (`string`, optional, max 1000 chars): Steps and filters for navigating the portal.
  - `versionToken` (`Guid`, optional): Required if updating an existing company.
- **Behavior**:
  - Company names are unique per owner (case-insensitive collation / unique index).
  - If existing name found without matching ID, returns `ErrorCode.DuplicateCompanyName`.
  - Atomically upserts company profile and career source sub-documents.
  - Emits `audit_log` entry.
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

- **Purpose**: Creates an application, records timeline events (interviews, communications, outcomes), and supports atomic reopen-and-apply override for decided roles.
- **Handler Mapping**: `ICommandHandler<RecordApplicationCommand, ApplicationRecordResultDto>`
- **Description**: `"Creates or updates a job application, appends timeline events, records resume/portfolio variants, and supports atomic reopen-and-apply override for decided postings."`
- **Input Parameters**:
  - `postingId` (`Guid`, required): Posting being applied to.
  - `action` (`string`, required): `"create"` or `"append_event"`.
  - `resumeVariant` (`string`, optional, max 100 chars): Resume filename or variant identifier.
  - `coverLetterVariant` (`string`, optional, max 100 chars): Cover letter variant identifier.
  - `portfolioVariant` (`string`, optional, max 100 chars): Portfolio version or link identifier.
  - `overrideDecided` (`bool`, optional, default false): Flag required to apply to a `screened_out` or `closed` posting.
  - `overrideReason` (`string`, optional, max 500 chars): Justification required when `overrideDecided == true`.
  - `event` (`object`, optional): Timeline event payload (required if `action == "append_event"`):
    - `eventType` (`string`, required): `"submitted"`, `"screening"`, `"interview"`, `"offer"`, `"rejected"`, `"withdrawn"`, or `"follow_up"`.
    - `date` (`string`, required, YYYY-MM-DD): Event date.
    - `notes` (`string`, optional, max 1000 chars): Context or summary of discussion.
    - `nextFollowUpDate` (`string`, optional, YYYY-MM-DD): Reminder date.
- **Behavior**:
  - `action == "create"`:
    - Verifies posting exists.
    - If posting is in `lead`, `unscored`, or `scored`: transitions posting to `applied` and inserts `applications` record.
    - If posting is in `screened_out` or `closed`:
      - If `overrideDecided == false`: returns `ErrorCode.InvalidTransition` ("Cannot apply to decided posting without explicit override flag").
      - If `overrideDecided == true`: executes atomic reopen-and-apply. Sets posting to `applied`, logs override in `audit_log` with previous disposition and policy, and inserts application.
  - `action == "append_event"`:
    - Atomically appends event to `application_events` (append-only collection) and updates application's `last_activity_at_utc`.
    - If event outcome is terminal (`rejected`, `withdrawn`, `offer`), synchronizes posting disposition (`rejected` or `closed`).
- **Output JSON Schema**:
  ```json
  {
    "applicationId": "0199f2c0-0000-7000-8000-000000000201",
    "postingId": "0199f2c0-0000-7000-8000-000000000101",
    "currentStatus": "submitted",
    "overrideApplied": true,
    "eventCount": 1,
    "updatedAtUtc": "2026-10-17T20:15:00Z"
  }
  ```

---

## Tool 5: `contact_record`

- **Purpose**: Creates, updates, or redacts confidential professional contacts with zero personal data leakage into audit logs or status commands.
- **Handler Mapping**: `ICommandHandler<RecordContactCommand, ContactRecordResultDto>`
- **Description**: `"Creates, updates, or redacts confidential professional networking contacts associated with companies or applications with privacy audit guards."`
- **Input Parameters**:
  - `id` (`Guid`, optional): Contact ID (generated if omitted).
  - `companyId` (`Guid`, required): Associated company ID.
  - `applicationId` (`Guid`, optional): Associated application ID.
  - `name` (`string`, optional, 1..100 chars): Full contact name (required on create unless redacting).
  - `role` (`string`, optional, max 100 chars): Job title or organizational role.
  - `email` (`string`, optional, max 100 chars): Contact email address.
  - `phone` (`string`, optional, max 50 chars): Contact phone number.
  - `linkedinUrl` (`string`, optional, URI): LinkedIn profile URL.
  - `notes` (`string`, optional, max 1000 chars): Interaction notes.
  - `redact` (`bool`, optional, default false): When true, overwrites personal fields with redaction placeholders.
  - `versionToken` (`Guid`, optional): Concurrency token.
- **Privacy Audit Guarantee (`FR-024`, `FR-025`)**:
  - Audit summaries for contact writes contain **only** changed field names and IDs (e.g., `fieldNames: ["role", "notes"]`).
  - No names, emails, roles, phones, or notes are **ever** stored in `audit_log`, error messages, or logs.
  - `redact: true` sets name to `"[REDACTED]"`, clears email/phone/linkedin/notes, and flags `is_redacted: true`. Redacted contacts are excluded from search listings.
- **Output JSON Schema**:
  ```json
  {
    "contactId": "0199f2c0-0000-7000-8000-000000000301",
    "companyId": "0199f2c0-0000-7000-8000-000000000050",
    "isRedacted": false,
    "versionToken": "0199f2c0-0000-7000-8000-000000000302"
  }
  ```

---

## Tool 6: `tracker_query`

- **Purpose**: Unified read-only query tool with a mandatory discriminator parameter, covering postings, decided postings, companies, criteria, applications, contacts, and datastore integrity verification.
- **Handler Mapping**: `IQueryHandler<TrackerQueryRequest, TrackerQueryResultDto>`
- **Description**: `"Unified read-only query tool for job search tracking data (postings, decided_postings, companies, criteria, applications, contacts, or integrity_check)."`
- **Input Parameters**:
  - `entity` (`string`, required): Discriminator: `"postings"`, `"decided_postings"`, `"companies"`, `"criteria"`, `"applications"`, `"contacts"`, or `"integrity_check"`.
  - `companyId` (`Guid`, optional): Filter by company.
  - `disposition` (`string`, optional): Filter by posting disposition (`lead`, `applied`, etc.).
  - `effectiveDate` (`string`, optional, YYYY-MM-DD): For `entity == "criteria"`, retrieves active criteria at this historical date.
  - `includeStale` (`bool`, optional, default false): If true, returns companies overdue for checking based on `checkIntervalDays`.
  - `page` (`int`, optional, default 1): Page index (`>= 1`).
  - `pageSize` (`int`, optional, default 20): Bounded page size (`1..100`).
- **Behavior by Entity Discriminator**:
  - `"postings"`: Returns paged postings matching filters. Calculates `ghostLikelihood` on read dynamically (never persisted).
  - `"decided_postings"`: Returns compact list (`id`, `title`, `companyId`, `disposition`, `dispositionReason`, `resurfacePolicy`, `decidedAtUtc`) optimized for client screening caches.
  - `"companies"`: Returns tracked company profiles, career source URLs, and last-checked timestamps.
  - `"criteria"`: If `effectiveDate` is provided, filters criteria where `effective_from <= effectiveDate` and (`effective_to == null` or `effective_to > effectiveDate`).
  - `"applications"`: Returns active applications with latest timeline events.
  - `"contacts"`: Returns unredacted contacts for the specified company.
  - `"integrity_check"`: Validates cross-collection referential integrity (checks for dangling `company_id`, `posting_id`, `application_id`, or `duplicate_of` links across notes and tracking records).
- **Output JSON Schema**:
  ```json
  {
    "entity": "postings",
    "page": 1,
    "pageSize": 20,
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
    ],
    "integrityIssues": []
  }
  ```
