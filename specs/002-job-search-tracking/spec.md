# Feature Specification: Structured Job-Search Tracking

**Feature Branch**: `002-job-search-tracking`

**Created**: 2026-10-17

**Status**: Draft

**Input**: User description: "Feature 002: structured job-search tracking for a single owner. This builds on feature 001 (notes store, audit log, stdio MCP server, encrypted backup) and must reuse its patterns: owner_id on every record, audit entry on every write inside the same transaction, no hard deletes, typed results, and the 8-tool MCP ceiling (6 tools exist today, so at most 6 new tools, 12 total, still zero delete tools)."

## Clarifications

### Session 2026-10-17

- Q: How are posting relationships and company background captured? → A: Replaced parent posting references with an optional single-valued `duplicate_of` link (self-duplicates and duplicate chains collapsed at write time). The legacy "parent" field is company description text; circular-parent logic was removed and a bounded company `description` field (max 300 characters) added.
- Q: How does posting deduplication operate across sparse legacy data? → A: Deduplication is tiered: (a) `ats_tenant + requisition_id`; (b) `normalized_url`; (c) `company + requisition_id` when no tenant; (d) `company + normalized title` returns a typed `PossibleDuplicate` advisory result listing candidate postings with dispositions and reasons without modifying or auto-merging records. Records first stored by URL only are upgraded in place when ATS identifiers are discovered.
- Q: How does re-encountering an existing posting behave across dispositions? → A: Returns the existing record and its status. `AlreadyDecided` applies to `screened_out`, `closed`, and `rejected` with `resurface_policy: never`. `applied`, `scored`, `lead`, and `unscored` return `AlreadyTracked` with their current state. `last_verified` is updated only for postings not in a decided or closed state. Resubmissions with zero changes write no audit entry.
- Q: What are the exact permitted disposition transitions and application outcome mappings? → A: Permitted transitions: `lead` -> `scored | unscored | screened_out | closed`; `unscored` -> `scored | screened_out | closed`; `scored` -> `applied | screened_out | closed`; `applied` -> `rejected | closed`; `screened_out` -> `lead` (explicit reopen when policy is `if_reopened`); `closed` -> `lead` (explicit reopen when policy is `if_reopened`); `rejected` -> terminal (explicit reopen with reason only). Invalid transitions return typed `InvalidTransition`. Application outcomes: `submitted` -> `applied`; `rejected` -> `rejected`; `withdrawn`, `declined_offer`, `accepted_offer` -> `closed` with reason. Applying to `screened_out` or `closed` requires an explicit override flag recorded in the audit entry.
- Q: What are the allowed company ownership types? → A: `public`, `private`, `pe_backed`, `vc_backed`, `mutual`, `subsidiary`, `nonprofit`, `government`, `unknown`.
- Q: How is contact privacy maintained across audits and deletions? → A: Audit before/after summaries for contact writes contain only field names changed and record IDs, never names, roles, or companies. A dedicated `redact` operation blanks personal fields while preserving the record ID and generating an audit entry.
- Q: How do notes associate with structured entities? → A: Notes may optionally link to a company, posting, or application by ID. Existing notes remain valid without links. The integrity check reports notes pointing to missing target entities.
- Q: What are the temporal formats, culture attributes, score bounds, and deadline fields for postings? → A: `culture_level` is an enum (`stable`, `moderate`, `high_risk`, `unknown`) plus optional `culture_summary` (max 500 chars). `score` is a number validated `0..10`. `posted_at` is optional. Date-only fields: `posted`, `closed`, `applied`, `deadline`. UTC timestamps: `first_seen`, `last_verified`, and system times. Posting signals of type `deadline` carry an optional `deadline` date.
- Q: How are list and search operations constrained for MCP and weekly batch performance? → A: All list/search tools are paged and bounded (default 50, max 200 items; response size cap). A compact decided-postings query returns only keys, dispositions, reasons, and resurface policy for fast screening.
- Q: What is the storage engine architectural assumption? → A: Structured storage uses the project's existing datastore ecosystem; the specific engine is decided during implementation planning.
- Q: What typed operational failures must the system explicitly define? → A: `NotFound`, `AlreadyDecided`, `AlreadyTracked`, `PossibleDuplicate`, `InvalidTransition`, `ConcurrencyConflict`, `ValidationError`.
- Q: What remains out of scope for Feature 002? → A: Importing legacy export data (Feature 003), scoring algorithms, career site scraping/crawling, UI, authentication, and hosted MCP.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Postings with Dispositions and Tiered Deduplication (Priority: P1)

As the job-search owner, I want to record and update job postings with full tracking metadata (company, title, URL, source kind, ATS tenant/requisition ID, location, remote model, compensation, lifecycle dates, score, culture rating, disposition, reason, resurface policy, and optional duplicate link) so that the search system prevents duplicate entries across aggregators, remembers past screening decisions, and prevents already-decided postings from repeatedly consuming review time.

**Why this priority**: Postings and their screening dispositions are the core system of record. Without reliable deduplication and memory of prior rejections or screening decisions, every weekly search run wastes significant effort re-evaluating the same roles across multiple job boards and aggregators.

**Independent Test**: Can be fully tested by submitting identical postings via different aggregator URLs and ATS identifiers, verifying that exactly one record is maintained, asserting that submitting a previously screened-out role returns a typed `AlreadyDecided` status with its prior rationale without modifying the original record, and verifying that fuzzy title matches return advisory `PossibleDuplicate` candidates without mutating records.

**Acceptance Scenarios**:

1. **Given** a new job posting with an ATS tenant identifier and requisition ID, **When** the owner submits the posting for the first time, **Then** the posting is recorded with disposition `lead` (or provided initial disposition) and an audit entry is generated.
2. **Given** an existing posting with an ATS tenant and requisition ID, **When** an aggregator submits the same posting with a different external URL but matching tenant and requisition ID (Tier A deduplication), **Then** the existing record is matched rather than creating a duplicate, and `last_verified` is updated only if the posting is not in a decided/closed state.
3. **Given** an existing posting lacking ATS identifiers, **When** a posting is submitted with a matching normalized URL (Tier B deduplication), **Then** the system matches the existing record and updates `last_verified` if active.
4. **Given** an existing posting lacking a tenant identifier, **When** a posting is submitted with matching company and requisition ID (Tier C deduplication), **Then** the system matches the existing record.
5. **Given** an existing posting matching on company and normalized title but lacking matching IDs or URL (Tier D deduplication), **When** submitted, **Then** the system returns a typed `PossibleDuplicate` advisory listing of candidate postings with their current dispositions and reasons, and NEVER auto-merges or modifies any record.
6. **Given** a posting originally recorded with URL only, **When** subsequent submissions provide valid ATS tenant and requisition identifiers, **Then** the existing record is upgraded in place with the new ATS identifiers rather than creating a duplicate record.
7. **Given** a posting previously screened out, closed, or rejected with resurface policy `never`, **When** the posting is re-encountered, **Then** the system returns a typed `AlreadyDecided` response with the existing record and prior reason without modifying the record or writing an audit entry.
8. **Given** an existing posting in disposition `lead`, `scored`, `applied`, or `unscored`, **When** the posting is re-encountered, **Then** the system returns a typed `AlreadyTracked` response with its current state, updating `last_verified` without creating duplicate records.
9. **Given** a resubmission of an existing posting where no field values or verification timestamps change, **When** processed, **Then** the operation is an idempotent no-op and generates zero audit entries.
10. **Given** a posting that duplicates another posting, **When** linking `duplicate_of`, **Then** the posting cannot reference itself or another posting that is already marked as a duplicate; any duplicate reference chains are collapsed to the root posting at write time.
11. **Given** a posting closed or screened out with resurface policy `if_reopened`, **When** re-encountered while still closed, **Then** it remains in its current state until an explicit reopen command is issued.
12. **Given** a posting with detailed screening rationale, **When** saved, **Then** the long narrative rationale is stored in an independent note and referenced by note identifier rather than embedded as unbounded text in the posting record.

---

### User Story 2 - Companies and Career Sources (Priority: P1)

As the job-search owner, I want to track companies (name, description, parent company, ownership type, headquarters, category tags, status, and last checked outcome) along with one or more career sources (ATS platform, tenant, slug, fetch method, and access notes) so that search workflows know exactly where and how to search for openings and which companies to actively prioritize or avoid.

**Why this priority**: Efficient search automation depends on knowing how each company hosts its openings (e.g., Workday API, Greenhouse board, or browser-only portal) and knowing the owner's strategic interest level (target, watch, avoid, neutral).

**Independent Test**: Can be tested by creating a company profile with description and ownership type, adding multiple career sources, querying the company's active sources, updating company tracking status (e.g., to `avoid`), and verifying that subsequent source queries correctly reflect the status and guidance.

**Acceptance Scenarios**:

1. **Given** a target employer, **When** the owner registers the company with description (up to 300 characters covering industry/background), category tags, and an ownership type from (`public`, `private`, `pe_backed`, `vc_backed`, `mutual`, `subsidiary`, `nonprofit`, `government`, `unknown`), **Then** the company profile is stored and queryable by name, parent organization, or status.
2. **Given** an existing company, **When** one or more career sources (specifying ATS platform, tenant, fetch method, and operational notes) are registered, **Then** the sources are linked to the company and retrievable for automated or manual checking.
3. **Given** a company marked with status `avoid`, **When** the search system queries for active targets, **Then** the company and its career sources are flagged to be skipped.
4. **Given** an update to a company's verification history, **When** the last-checked timestamp and outcome are updated, **Then** an audit entry records the check timestamp without overwriting historical audit history.

---

### User Story 3 - Criteria with Revision History (Priority: P1)

As the job-search owner, I want to define screening criteria (key, category like hard_skip, weight, or preference, value, effective-from date, and provenance) with immutable revision history so that screening rules evolve transparently and every disposition reason can cite the exact rule version that was active when the decision was made.

**Why this priority**: Screening rules shift over the course of a search. Overwriting criteria destroys the auditability of past rejections. Temporal criteria tracking guarantees that historical decisions remain reproducible and explainable.

**Independent Test**: Can be tested by creating a criterion, superseding it with a new revision, asserting that querying the criterion as of a prior date returns the original version, and verifying that a posting disposition reason can reference the specific criterion.

**Acceptance Scenarios**:

1. **Given** a screening standard (e.g., minimum compensation or forbidden tech stack), **When** the owner creates a criterion, **Then** it is stored with an effective-from timestamp, active status, and stated/inferred provenance.
2. **Given** an active criterion, **When** the owner revises its value or category, **Then** the previous version is marked superseded rather than overwritten, creating a new active revision with the updated effective date.
3. **Given** multiple historical revisions of a criterion, **When** the owner queries criteria active on a specific historical date, **Then** the system returns the exact criteria set effective on that target date.
4. **Given** a posting disposition decision, **When** a reason is recorded, **Then** it can link directly to the criterion key or version that triggered the disposition.

---

### User Story 4 - Applications and Interaction Timeline (Priority: P2)

As the job-search owner, I want to track formal job applications (submission date, desired compensation range, target start date, resume variant, referral contact reference, confirmation code, and outcome) along with an interaction timeline (applied, follow-up, interview stages, offer, rejected, withdrawn) so that I maintain a complete, verifiable log of all employer interactions without storing sensitive resume files inside the database.

**Why this priority**: Once a posting advances past screening, the owner requires clear state management for submissions and follow-ups. Keeping resume binary text out of scope protects database agility and security while still tracking the exact variant used.

**Independent Test**: Can be tested by creating an application linked to an existing posting, validating disposition transitions and outcome mappings, appending chronological interaction events (e.g., applied -> interview -> offer), verifying the timeline ordering, and ensuring resume variant pointers are recorded without storing file contents.

**Acceptance Scenarios**:

1. **Given** an active posting with disposition `scored` or `lead`, **When** the owner submits an application, **Then** an application record is created linking to the posting, recording the resume variant classification (`tailored`, `as_is`, or `ic_framed`) and external pointer.
2. **Given** a posting currently marked `screened_out` or `closed`, **When** the owner attempts to create an application without an override, **Then** the system rejects the operation with a typed `InvalidTransition` error; **When** submitted with an explicit override flag, **Then** the application is created, the posting transitions to `applied`, and the override is recorded in the audit entry.
3. **Given** an active posting, **When** disposition transitions are requested, **Then** the system enforces the permitted transition matrix:
   - `lead` -> `scored | unscored | screened_out | closed`
   - `unscored` -> `scored | screened_out | closed`
   - `scored` -> `applied | screened_out | closed`
   - `applied` -> `rejected | closed`
   - `screened_out` -> `lead` (only via explicit reopen when policy is `if_reopened`)
   - `closed` -> `lead` (only via explicit reopen when policy is `if_reopened`)
   - `rejected` -> terminal (reopen allowed only by explicit command with recorded reason)
   - Any other transition is rejected with a typed `InvalidTransition` error.
4. **Given** an active application, **When** new interaction milestones occur (follow-up, screening interview, technical round, offer, withdrawal), **Then** chronological events with date-only event dates, sources, and notes are appended to the application's timeline.
5. **Given** an application outcome update, **When** the outcome transitions, **Then** the parent posting disposition synchronizes within the same transaction:
   - `submitted` -> posting transitions to `applied`
   - `rejected` -> posting transitions to `rejected`
   - `withdrawn`, `declined_offer`, or `accepted_offer` -> posting transitions to `closed` with the reason recorded.

---

### User Story 5 - Posting Signals and Ghost Likelihood (Priority: P2)

As the job-search owner, I want to attach observed signals to job postings (such as repost indicators, missing salary disclosures, parallel multi-location listings, disappeared listings, phantom openings, or explicit deadlines) with observed dates and evidence so that the system computes a ghost-role likelihood dynamically upon read without persisting stale calculated scores.

**Why this priority**: Job boards frequently list phantom jobs, evergreen reposts, and expired openings. Calculating ghost likelihood dynamically on read ensures that scoring algorithms can be refined or updated without running expensive data migration passes over stored records.

**Independent Test**: Can be tested by recording multiple signals against a test posting (including deadline signals with optional target dates), reading the posting to verify that the ghost likelihood score is calculated on the fly according to current signal weights, and asserting that no ghost likelihood field is persisted in the underlying datastore.

**Acceptance Scenarios**:

1. **Given** a tracked job posting, **When** the owner or automated check observes a signal (e.g., listing reposted or compensation omitted), **Then** the signal is appended with an observation timestamp, signal type, and evidence summary.
2. **Given** a signal of type `deadline`, **When** recorded, **Then** the signal carries an optional date-only deadline date.
3. **Given** a posting with multiple observed signals, **When** the posting details are retrieved, **Then** the system computes and returns a dynamic ghost likelihood assessment alongside the raw signal details.
4. **Given** a stored posting record, **When** inspecting stored database documents or backups, **Then** verify that ghost likelihood is completely absent from persisted storage.

---

### User Story 6 - Confidential Contacts Isolation (Priority: P2)

As the job-search owner, I want to record third-party contacts (recruiters, hiring managers, and referral connections) in a strictly confidential store that is isolated from default searches, error messages, status summaries, and log outputs, and support a redact operation, so that personal contact data is never exposed during routine operations or automated agent searches.

**Why this priority**: Personal contact details represent third-party personal data. Under privacy-first principles, this sensitive information must never leak into general search results, logs, or diagnostic outputs, but must remain accessible when explicitly requested for a specific application or company.

**Independent Test**: Can be tested by creating a contact record, running standard general search operations, inspecting captured logs, status outputs, and audit summaries to verify zero contact personal details appear, verifying that contact write audit entries contain only record IDs and changed field names, and verifying that the `redact` operation blanks personal fields while retaining the audited record ID.

**Acceptance Scenarios**:

1. **Given** a professional contact associated with a company or application, **When** recorded, **Then** the contact is saved with name, role, organization, and relationship type.
2. **Given** general search queries for postings, companies, or criteria, **When** results are returned, **Then** contact details are completely omitted from the output.
3. **Given** an error condition, status command, or routine log entry involving a contact or contact reference, **When** logged or reported, **Then** the contact's name, role, and personal details are completely excluded.
4. **Given** any create or update write operation on a contact record, **When** the mandatory audit entry is written, **Then** the before and after summaries contain ONLY changed field names and record identifiers, NEVER names, roles, or company strings.
5. **Given** an existing contact that must be removed, **When** the owner invokes the `redact` operation, **Then** the system blanks all personal data fields (name, role, notes) while keeping the record ID in place, generating a compliant audit entry without violating the zero hard-delete constraint.
6. **Given** a specific inquiry requesting a contact by reference ID, **When** executed via the dedicated contact query tool, **Then** the contact details are returned to the authorized owner.

---

### User Story 7 - Search Runs and Referential Integrity Verification (Priority: P3)

As the job-search owner, I want to record periodic search run executions (start time, count of newly identified postings, and reference to a run narrative note) and execute referential integrity checks so that I can detect dangling references across companies, postings, applications, and notes before data inconsistencies compound.

**Why this priority**: Automated searches run on a schedule and generate narrative summary notes alongside structured records. A lightweight integrity verification tool guarantees that the relational graph (postings pointing to companies, applications pointing to postings, notes referencing entities) remains intact.

**Independent Test**: Can be tested by recording a search run with a linked report note, creating a note linked to a structured entity, intentionally introducing a dangling entity reference in a test scenario, running the integrity check, and asserting that the check flags the exact dangling reference without throwing unhandled errors.

**Acceptance Scenarios**:

1. **Given** the completion of an automated or manual search batch, **When** the owner logs the run, **Then** a search run record is created capturing execution timestamp, count of new postings discovered, and the identifier of the narrative run report note.
2. **Given** a narrative note, **When** authored or updated, **Then** the note may optionally declare a link to a company, posting, or application by ID; existing notes lacking links remain completely valid without requiring schema migration.
3. **Given** a database state with potential inconsistencies, **When** the owner triggers an integrity verification check, **Then** the system scans and reports any postings referencing non-existent companies, applications referencing non-existent postings, `duplicate_of` links referencing non-existent postings, or notes referencing non-existent entities.
4. **Given** an integrity verification check that detects anomalies, **When** reporting results, **Then** the output lists typed discrepancy summaries without modifying or deleting any underlying records.

---

### Edge Cases

- **Missing ATS Identifiers (Tier B & C Deduplication)**: When a posting originates from a source lacking an ATS tenant or requisition ID, the system falls back to normalized URL (Tier B) or company + requisition ID (Tier C).
- **Advisory Duplicate Matches (Tier D)**: When an incoming posting shares a company and normalized title with an existing record but lacks matching identifiers, the system returns a typed `PossibleDuplicate` advisory response listing candidate records and their dispositions. The system NEVER auto-merges or mutates any existing record under Tier D.
- **In-Place Identifier Upgrades**: If a posting was initially saved by normalized URL alone and a subsequent submission supplies ATS tenant and requisition ID, the system upgrades the existing record in place rather than creating a second record.
- **Duplicate Reference Chains**: A posting cannot be marked as `duplicate_of` itself or another duplicate. If posting A is marked duplicate of B, and B is already duplicate of C, writing A collapses the relationship so A is stored as `duplicate_of: C`.
- **Untrusted Posting Descriptions**: Posting descriptions may contain prompt injection attempts or malicious formatting; the system must enforce strict input size ceilings, treat text as opaque data, and never evaluate or interpret instructions contained within posting bodies.
- **Closed / Screened-Out Resubmissions**: When a posting marked `screened_out`, `closed`, or `rejected` with policy `never` is encountered again, it returns a typed `AlreadyDecided` response without updating the record or writing an audit entry.
- **Reopened Role with `if_reopened`**: A posting marked `if_reopened` remains closed or screened out when re-encountered unless an explicit reopening command is issued by the caller.
- **Active Postings Re-encountered**: Postings in `lead`, `scored`, `applied`, or `unscored` return `AlreadyTracked` with their current state. `last_verified` is updated only for active (non-decided/non-closed) postings.
- **Zero-Change Resubmissions**: If an incoming submission matches an existing posting and introduces no field changes or verification updates, the operation is an idempotent no-op and writes no audit log entry.
- **Long Screening Rationales**: Any screening rationale exceeding brief summary length must be saved as an independent narrative note and linked via note ID, preserving database index performance and document size boundaries.
- **Concurrent Updates to Postings**: Optimistic concurrency tokens or transactional version checks ensure conflicting concurrent updates to a posting's disposition or metadata are rejected cleanly with a typed `ConcurrencyConflict` error.

## Requirements *(mandatory)*

### Functional Requirements

#### Postings & Deduplication
- **FR-001**: System MUST record job postings with attributes: owner identifier, company reference, job title, original posting URL, source kind (ATS or aggregator), ATS tenant, requisition ID, physical location, remote work type, compensation (minimum, maximum, currency, free-text description), posted date (optional date-only), first-seen timestamp (UTC), last-verified timestamp (UTC), closed date (optional date-only), score (optional number validated between 0 and 10), culture risk level (`stable`, `moderate`, `high_risk`, `unknown`), optional culture summary (max 500 characters), disposition, disposition reason, resurface policy (`never`, `if_reopened`), and optional `duplicate_of` posting reference.
- **FR-002**: System MUST enforce valid disposition values: `lead`, `scored`, `applied`, `rejected`, `screened_out`, `closed`, and `unscored`.
- **FR-003**: System MUST enforce valid resurface policy values: `never` and `if_reopened`.
- **FR-004**: System MUST evaluate deduplication in strict tier priority:
  - **Tier A**: Match by composite key `(ats_tenant, requisition_id)` when both are present.
  - **Tier B**: Match by canonical `normalized_url` when ATS identifiers are absent.
  - **Tier C**: Match by `(company_id, requisition_id)` when tenant identifier is absent.
  - **Tier D (Advisory)**: When matching by `(company_id, normalized_title)` without matching IDs or URLs, System MUST return a typed `PossibleDuplicate` response containing candidate postings with their current dispositions and reasons, and MUST NEVER automatically merge or modify records.
- **FR-005**: When an existing posting recorded by URL subsequently receives matching ATS identifiers via Tier A or Tier C, System MUST upgrade the existing record in place with the newly discovered identifiers.
- **FR-006**: System MUST normalize URLs by standardizing schemes, lowercasing domain names, removing default ports, trailing slashes, and removing common tracking/marketing query parameters (e.g., `utm_*`, `ref`, `gh_src`).
- **FR-007**: When an existing posting with disposition `screened_out`, `closed`, or `rejected` and policy `never` is submitted, System MUST return a typed `AlreadyDecided` response containing the original disposition and reason, and MUST NOT alter the record or write an audit entry.
- **FR-008**: When an existing posting with disposition `lead`, `scored`, `applied`, or `unscored` is submitted, System MUST return a typed `AlreadyTracked` response with its current state, updating `last_verified` only for active postings.
- **FR-009**: Resubmission of an existing posting that produces zero changes to stored values MUST be an idempotent no-op and write no audit log entry.
- **FR-010**: System MUST enforce that a posting cannot be `duplicate_of` itself or of another posting marked as a duplicate, automatically collapsing duplicate reference chains to the root posting upon write.
- **FR-011**: System MUST allow postings to reference an external narrative note ID for long-form screening rationale rather than embedding arbitrary prose in the posting record.
- **FR-012**: System MUST enforce maximum length bounds on all text fields (e.g., title, URL, location, compensation text, disposition reason, culture summary) and treat all external posting text as untrusted data.

#### Companies & Career Sources
- **FR-013**: System MUST track companies with attributes: owner identifier, company name, bounded company description (max 300 characters covering industry, ownership, and headquarters notes), parent company name, ownership type (`public`, `private`, `pe_backed`, `vc_backed`, `mutual`, `subsidiary`, `nonprofit`, `government`, `unknown`), headquarters location, category tags, tracking status (`target`, `watch`, `avoid`, `neutral`), last checked timestamp (UTC), and last check result summary.
- **FR-014**: System MUST support associating one or more career sources with each company, containing: source ATS type (Workday, Greenhouse, Ashby, Lever, Avature, Oracle, UKG, or custom), tenant identifier, URL slug, fetch method (`api`, `browser`, `blocked`), and operational notes.
- **FR-015**: System MUST allow querying companies and their career sources filtered by status, category tags, or pending check date to guide search automation.

#### Criteria with Temporal History
- **FR-016**: System MUST maintain screening criteria with attributes: owner identifier, criterion key, category (`hard_skip`, `weight`, `preference`), criterion value, effective-from timestamp (UTC), effective-to timestamp (UTC, optional/null for active), provenance (`stated` or `inferred`), and active status flag.
- **FR-017**: System MUST treat criteria revisions as immutable append-only operations: updating a criterion MUST close the current version by setting its `effective_to` timestamp and insert a new revision record marked active.
- **FR-018**: System MUST support point-in-time criteria evaluation, returning the exact set of active criteria that were effective on any user-specified UTC date.
- **FR-019**: System MUST allow posting disposition reasons to reference the specific criterion key or revision that motivated the disposition.

#### Applications & State Transitions
- **FR-020**: System MUST record applications linked to a specific posting, containing: owner identifier, posting reference, submitted date (date-only), desired salary range, target start date (date-only), resume variant flag (`tailored`, `as_is`, `ic_framed`), external resume pointer reference, referral contact reference ID, confirmation code/reference, and overall outcome.
- **FR-021**: System MUST forbid storing raw resume or cover letter document text within the database; only variant metadata and external pointers may be persisted.
- **FR-022**: System MUST enforce the following valid disposition transitions:
  - `lead` -> `scored | unscored | screened_out | closed`
  - `unscored` -> `scored | screened_out | closed`
  - `scored` -> `applied | screened_out | closed`
  - `applied` -> `rejected | closed`
  - `screened_out` -> `lead` (only via explicit reopen when policy is `if_reopened`)
  - `closed` -> `lead` (only via explicit reopen when policy is `if_reopened`)
  - `rejected` -> terminal (reopen permitted only via explicit command with a recorded reason)
  - Any invalid transition attempt MUST return a typed `InvalidTransition` failure.
- **FR-023**: Creating an application for a posting in `screened_out` or `closed` status MUST require an explicit override flag; if omitted, System MUST return an `InvalidTransition` failure. If present, the override MUST be recorded in the audit entry.
- **FR-024**: System MUST synchronize posting dispositions with application outcomes in the same transaction:
  - Application submitted -> posting transitions to `applied`
  - Application rejected -> posting transitions to `rejected`
  - Application withdrawn, offer declined, or offer accepted -> posting transitions to `closed` with the outcome reason recorded.
- **FR-025**: System MUST maintain an append-only timeline of application events for each application, capturing: event date (date-only), event type (`applied`, `follow_up`, `rejected`, `interview`, `offer`, `withdrawn`), event source, and descriptive notes.

#### Posting Signals & Dynamic Assessment
- **FR-026**: System MUST record signals observed against postings, capturing: posting reference, signal type (`repost`, `no_salary`, `parallel_posting`, `gone`, `phantom`, `deadline`), evidence description, optional deadline date (date-only, for `deadline` signal types), and observation timestamp (UTC).
- **FR-027**: System MUST dynamically compute a ghost likelihood rating when a posting is retrieved, combining active signals using deterministic rules, and MUST NOT persist this calculated rating to storage.

#### Confidential Contacts Isolation & Redaction
- **FR-028**: System MUST store third-party contact records with: owner identifier, name, current role, company affiliation, and relationship category (recruiter, referral, hiring manager, peer).
- **FR-029**: System MUST strictly isolate contacts from general search results: default queries for postings, companies, or criteria MUST NEVER return contact personal data.
- **FR-030**: System MUST ensure that contact names and personal information never appear in system logs, diagnostics, status command output, or error messages.
- **FR-031**: For all write operations on contact records, System MUST record audit log before/after summaries containing ONLY changed field names and record identifiers, strictly excluding names, roles, or company text.
- **FR-032**: System MUST provide a dedicated `redact` operation for contacts that blanks all personal fields (name, role, affiliation) while preserving the record ID and recording an audit entry, upholding the zero hard-delete constraint.
- **FR-033**: System MUST restrict contact retrieval to dedicated, explicit single-entity lookup operations referencing the specific contact identifier.

#### Notes Association, Search Runs & Integrity
- **FR-034**: System MUST allow notes to optionally store a link to a company, posting, or application by ID; existing notes lacking links remain completely valid without migration.
- **FR-035**: System MUST record search run executions with: owner identifier, start timestamp (UTC), count of newly identified postings, and a link to the corresponding run report note ID.
- **FR-036**: System MUST provide an integrity verification check that detects and reports orphaned or dangling references: postings referencing non-existent companies, applications referencing non-existent postings, `duplicate_of` links referencing non-existent postings, and notes linked to non-existent entities.
- **FR-037**: The integrity verification check MUST be read-only and MUST NOT delete or alter any data.

#### Query Performance, Paging & Tool Ceilings
- **FR-038**: All listing and search tools MUST be paged and bounded (default page size: 50, maximum page size: 200 items, with response size caps matching note retrieval tools).
- **FR-039**: System MUST provide a compact "decided postings" listing tool returning only posting keys, dispositions, reasons, and resurface policies to allow rapid candidate screening without loading full posting records.
- **FR-040**: The Model Context Protocol (MCP) server interface MUST expose at most 6 new tools (maintaining a strict ceiling of at most 12 total tools across Features 001 and 002) with zero delete tools.

#### Cross-Cutting Architectural & Safety Constraints
- **FR-041**: Every record across all entities MUST include an `owner_id` attribute.
- **FR-042**: Every write operation (create, update, transition, redact) across all entities MUST write an append-only audit entry in the same transaction capturing actor, command name, and before/after summaries.
- **FR-043**: System MUST NOT provide any hard-delete operations or tools; removal or deactivation of entities is represented exclusively through inactive, closed, superseded, archived, or redacted states.
- **FR-044**: System MUST explicitly return expected operational failures as typed result objects:
  - `NotFound`: Requested entity does not exist.
  - `AlreadyDecided`: Posting already in a decided terminal state with resurface policy `never`.
  - `AlreadyTracked`: Posting is already actively tracked in the system.
  - `PossibleDuplicate`: Candidate postings identified by fuzzy title/company matching.
  - `InvalidTransition`: Disallowed state transition attempt.
  - `ConcurrencyConflict`: Optimistic concurrency version mismatch.
  - `ValidationError`: Input violates structural constraints or length bounds.
- **FR-045**: All new entities MUST be completely covered by the existing encrypted backup, wipe, and restore mechanisms, including restore verification guards.
- **FR-046**: All test suites, seed fixtures, and documentation MUST use strictly fictional data with zero real names or organizations.

### Key Entities *(include if feature involves data)*

- **Posting**: The core opportunity record containing company link, role title, normalized URL, ATS identifiers (tenant, requisition ID), compensation bounds, remote status, score (0..10), culture level (`stable`, `moderate`, `high_risk`, `unknown`), culture summary, disposition, disposition reason, resurface policy (`never`, `if_reopened`), optional `duplicate_of` link, date-only lifecycle dates (`posted`, `closed`), and UTC timestamps (`first_seen`, `last_verified`).
- **Company**: An employer organization tracking name, description (max 300 chars), parent company, ownership type (`public`, `private`, `pe_backed`, `vc_backed`, `mutual`, `subsidiary`, `nonprofit`, `government`, `unknown`), category tags, headquarters, tracking status (`target`, `watch`, `avoid`, `neutral`), and recent verification history.
- **CareerSource**: Specific careers portal configuration for a company, defining the ATS platform, tenant name, URL slug, fetch method (`api`, `browser`, `blocked`), and access guidance.
- **Criterion**: An immutable screening rule with key, category (`hard_skip`, `weight`, `preference`), value, validity timeframe (effective-from and effective-to UTC timestamps), and stated or inferred provenance.
- **Application**: A formal submission record linked to a posting, containing submission date (date-only), compensation targets, target start date (date-only), resume variant classification and pointer, referral contact reference, confirmation code, and outcome.
- **ApplicationEvent**: A chronological interaction milestone on an application timeline (applied, interview round, follow-up, offer, withdrawal) with a date-only event date.
- **PostingSignal**: An observable quality or risk signal (repost, omitted salary, phantom listing, deadline with optional date-only target) tied to a posting with evidence and UTC timestamp.
- **Contact**: A private third-party professional contact (recruiter, referral) kept isolated from general queries and diagnostics, supporting redaction.
- **SearchRun**: An execution record of a search batch capturing UTC timestamp, new postings discovered, and reference to a run narrative note.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Submitting the exact same job posting 100 times across three different aggregator URL formats results in exactly one stored posting record.
- **SC-002**: Re-evaluating a previously screened-out posting with policy `never` returns a typed `AlreadyDecided` response with the original rationale in a single lookup operation without modifying the datastore or writing an audit entry.
- **SC-003**: Point-in-time criteria queries return the 100% accurate, active criteria set for any historical timestamp across multiple revisions and superseded rules.
- **SC-004**: 100% of state-modifying write operations generate exactly one corresponding append-only audit entry within the same transaction.
- **SC-005**: Architecture and contract tests verify that zero hard-delete operations or tools exist across the entire codebase.
- **SC-006**: Lookup of a posting by deduplication key completes in under 100 milliseconds (p95) on a dataset of 5,000 postings; searching postings by company or disposition completes in under 200 milliseconds (p95).
- **SC-007**: Automated scans of logs, error payloads, diagnostic status outputs, and general search results detect zero third-party contact names or personal details.
- **SC-008**: Performing an encrypted backup followed by a wipe and restore operation yields 100% identical entity counts across all new entities (postings, companies, sources, criteria, applications, events, signals, contacts, search runs).
- **SC-009**: The total number of tools exposed by the Model Context Protocol (MCP) server does not exceed 12 tools (at most 6 new tools added to the existing 6), with zero delete tools.
- **SC-010**: Advisory duplicate screening (Tier D) accurately flags candidate postings by company and normalized title for 100% of ambiguous test cases without altering existing records.

## Assumptions

- **Single-Owner Scope**: The system continues to operate for a single authorized owner, using a consistent owner identifier across all records and transactions.
- **External Scraping Architecture**: Fetching and parsing HTML from careers sites is executed by external scheduled jobs or agents; this feature provides the structured storage, criteria, and retrieval API, not the web crawler.
- **Scoring Ingestion**: Posting scores and culture ratings are computed and supplied by the agent or user; this feature stores the inputs and computes dynamic signals like ghost-likelihood.
- **Resume File Storage**: Resume binaries and cover letters are stored in the user's local filesystem or document store; this tracker stores only the variant taxonomy and external path/pointer.
- **Legacy Export Compatibility**: The data model accommodates the structure of the 78 legacy postings, 149 appendix rows, and ~100 companies, including free-text compensation descriptions, bounded company descriptions, and tiered advisory deduplication for unindexed roles.
- **Storage Ecosystem**: Structured storage uses the project's existing datastore ecosystem; the specific storage engine is decided during implementation planning.
- **URL Canonicalization Rules**: Standard URL normalization strips common marketing tracking query parameters (`utm_*`, `ref`, `source`, `gh_src`) while preserving semantic query parameters required by custom career portals.
- **Fictional Data Mandate**: All test suites, seed fixtures, and sample data remain strictly fictional to uphold repository privacy standards.
- **Explicit Out of Scope**: Importing the legacy export (Feature 003), automated scoring algorithms, web scraping, user interface, authentication, and hosted MCP deployment remain strictly out of scope for Feature 002.
