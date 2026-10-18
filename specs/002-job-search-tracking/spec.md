# Feature Specification: Structured Job-Search Tracking

**Feature Branch**: `002-job-search-tracking`

**Created**: 2026-10-17

**Status**: Draft

**Input**: User description: "Feature 002: structured job-search tracking for a single owner. This builds on feature 001 (notes store, audit log, stdio MCP server, encrypted backup) and must reuse its patterns: owner_id on every record, audit entry on every write inside the same transaction, no hard deletes, typed results, and the 8-tool MCP ceiling (6 tools exist today, so at most 6 new tools, 12 total, still zero delete tools)."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Postings with Dispositions and Deduplication (Priority: P1)

As the job-search owner, I want to record and update job postings with full tracking metadata (company, title, URL, source kind, ATS tenant/requisition ID, location, remote model, compensation, lifecycle dates, score, culture rating, disposition, reason, and resurface policy) so that the search system prevents duplicate entries across aggregators, remembers past screening decisions, and prevents already-decided postings from repeatedly consuming review time.

**Why this priority**: Postings and their screening dispositions are the core system of record. Without reliable deduplication and memory of prior rejections or screening decisions, every weekly search run wastes significant effort re-evaluating the same roles across multiple job boards and aggregators.

**Independent Test**: Can be fully tested by submitting identical postings via different aggregator URLs and ATS identifiers, verifying that exactly one record is maintained, and asserting that submitting a previously screened-out role returns an "already decided" status with its prior rationale without modifying the original record.

**Acceptance Scenarios**:

1. **Given** a new job posting with an ATS tenant identifier and requisition ID, **When** the owner submits the posting for the first time, **Then** the posting is recorded with disposition `lead` (or provided initial disposition) and an audit entry is generated.
2. **Given** an existing posting with an ATS tenant and requisition ID, **When** an aggregator submits the same posting with a different external URL but matching tenant and requisition ID, **Then** the existing record is matched rather than creating a duplicate, updating verification dates if active.
3. **Given** an existing posting lacking ATS identifiers, **When** a posting is submitted with a matching normalized URL (canonical scheme, host, and path stripped of tracking parameters), **Then** the system recognizes it as a duplicate and updates the existing record.
4. **Given** a posting previously screened out or closed with resurface policy `never`, **When** the posting is re-encountered in a subsequent search, **Then** the system immediately reports the role as "already decided", returns the prior disposition reason, and does not alter the historical disposition.
5. **Given** a posting closed with resurface policy `if_reopened`, **When** the posting is re-encountered while still closed, **Then** it remains closed until an explicit reopening action is executed to transition it back to `lead`.
6. **Given** a posting that references a related or duplicate posting, **When** stored, **Then** the record maintains a parent reference link without nesting full records.
7. **Given** a posting with detailed screening rationale, **When** saved, **Then** the long narrative rationale is stored in an independent note and referenced by note identifier rather than embedded as unbounded text in the posting record.

---

### User Story 2 - Companies and Career Sources (Priority: P1)

As the job-search owner, I want to track companies (name, parent organization, ownership type, headquarters, category tags, status, and last checked outcome) along with one or more career sources (ATS platform, tenant, slug, fetch method, and access notes) so that search workflows know exactly where and how to search for openings and which companies to actively prioritize or avoid.

**Why this priority**: Efficient search automation depends on knowing how each company hosts its openings (e.g., Workday API, Greenhouse board, or browser-only portal) and knowing the owner's strategic interest level (target, watch, avoid, neutral).

**Independent Test**: Can be tested by creating a company profile with multiple career sources, querying the company's active sources, updating company tracking status (e.g., to `avoid`), and verifying that subsequent source queries correctly reflect the status and guidance.

**Acceptance Scenarios**:

1. **Given** a target employer, **When** the owner registers the company with category tags and ownership type, **Then** the company profile is stored and queryable by name, parent organization, or status.
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

**Independent Test**: Can be tested by creating an application linked to an existing posting, appending chronological interaction events (e.g., applied -> interview -> offer), verifying the timeline ordering, and ensuring resume variant pointers are recorded without storing file contents.

**Acceptance Scenarios**:

1. **Given** an active posting with disposition `scored` or `lead`, **When** the owner submits an application, **Then** an application record is created linking to the posting, recording the resume variant classification (tailored, as_is, or ic_framed) and external pointer.
2. **Given** an active application, **When** new interaction milestones occur (follow-up, screening interview, technical round, offer, withdrawal), **Then** chronological events with dates, sources, and notes are appended to the application's timeline.
3. **Given** an application outcome change (e.g., offer accepted or rejected), **When** updated, **Then** the posting disposition synchronizes with the outcome (e.g., transitioning to `closed` or `rejected`) inside an audited transaction.

---

### User Story 5 - Posting Signals and Ghost Likelihood (Priority: P2)

As the job-search owner, I want to attach observed signals to job postings (such as repost indicators, missing salary disclosures, parallel multi-location listings, disappeared listings, phantom openings, or explicit deadlines) with observed dates and evidence so that the system computes a ghost-role likelihood dynamically upon read without persisting stale calculated scores.

**Why this priority**: Job boards frequently list phantom jobs, evergreen reposts, and expired openings. Calculating ghost likelihood dynamically on read ensures that scoring algorithms can be refined or updated without running expensive data migration passes over stored records.

**Independent Test**: Can be tested by recording multiple signals against a test posting, reading the posting to verify that the ghost likelihood score is calculated on the fly according to current signal weights, and asserting that no ghost likelihood field is persisted in the underlying datastore.

**Acceptance Scenarios**:

1. **Given** a tracked job posting, **When** the owner or automated check observes a signal (e.g., listing reposted or compensation omitted), **Then** the signal is appended with an observation date, signal type, and evidence summary.
2. **Given** a posting with multiple observed signals, **When** the posting details are retrieved, **Then** the system computes and returns a dynamic ghost likelihood assessment alongside the raw signal details.
3. **Given** a stored posting record, **When** inspecting stored database documents or backups, **Then** verify that ghost likelihood is completely absent from persisted storage.

---

### User Story 6 - Confidential Contacts Isolation (Priority: P2)

As the job-search owner, I want to record third-party contacts (recruiters, hiring managers, and referral connections) in a strictly confidential store that is isolated from default searches, error messages, status summaries, and log outputs so that personal contact data is never exposed during routine operations or automated agent searches.

**Why this priority**: Personal contact details represent third-party personal data. Under privacy-first principles, this sensitive information must never leak into general search results, logs, or diagnostic outputs, but must remain accessible when explicitly requested for a specific application or company.

**Independent Test**: Can be tested by creating a contact record, running standard general search operations, inspecting captured logs and status outputs to verify zero contact details appear, and verifying that the contact is returned only when queried through an explicit contact reference tool.

**Acceptance Scenarios**:

1. **Given** a professional contact associated with a company or application, **When** recorded, **Then** the contact is saved with name, role, organization, and relationship type.
2. **Given** general search queries for postings, companies, or criteria, **When** results are returned, **Then** contact details are completely omitted from the output.
3. **Given** an error condition or routine log entry involving an application with a contact reference, **When** logged or reported, **Then** the contact's name and details are redacted or omitted.
4. **Given** a specific inquiry requesting a contact by reference ID, **When** executed via the dedicated contact query tool, **Then** the contact details are returned to the authorized owner.

---

### User Story 7 - Search Runs and Referential Integrity Verification (Priority: P3)

As the job-search owner, I want to record periodic search run executions (start time, count of newly identified postings, and reference to a run narrative note) and execute referential integrity checks so that I can detect dangling references across companies, postings, applications, and notes before data inconsistencies compound.

**Why this priority**: Automated searches run on a schedule and generate narrative summary notes alongside structured records. A lightweight integrity verification tool guarantees that the relational graph (postings pointing to companies, applications pointing to postings, notes referencing entities) remains intact.

**Independent Test**: Can be tested by recording a search run with a linked report note, intentionally introducing a dangling entity reference in a test scenario, running the integrity check, and asserting that the check flags the exact dangling reference without throwing unhandled errors.

**Acceptance Scenarios**:

1. **Given** the completion of an automated or manual search batch, **When** the owner logs the run, **Then** a search run record is created capturing execution timestamp, count of new postings discovered, and the identifier of the narrative run report note.
2. **Given** a database state with potential inconsistencies, **When** the owner triggers an integrity verification check, **Then** the system scans and reports any postings referencing non-existent companies, applications referencing non-existent postings, or parent references pointing to missing parent postings.
3. **Given** an integrity verification check that detects anomalies, **When** reporting results, **Then** the output lists typed discrepancy summaries without modifying or deleting any underlying records.

---

### Edge Cases

- **Missing ATS Identifiers**: When a posting originates from a company site or job board lacking an ATS requisition ID, the system must fallback to normalizing the URL (lowercasing host, standardizing path, stripping UTM and tracking query parameters).
- **Simultaneous Aggregator Ingestion**: If two aggregators present the same opening simultaneously with different titles (e.g., "Senior .NET Engineer" vs "Sr. Software Engineer - .NET"), the ATS tenant and requisition ID take precedence to ensure deduplication.
- **Untrusted Posting Descriptions**: Posting descriptions may contain prompt injection attempts or malicious formatting; the system must enforce strict input size ceilings, treat text as opaque data, and never evaluate or interpret instructions contained within posting bodies.
- **Closed Role Resurfacing**: When a posting marked `screened_out` or `closed` with policy `never` is encountered again, it must not update its disposition or overwrite notes; it must return a typed `AlreadyDecided` response.
- **Reopened Role with `if_reopened`**: A posting marked `if_reopened` must remain in its inactive state when scanned unless an explicit reopening command is issued by the caller.
- **Long Screening Rationales**: Any screening rationale exceeding brief summary length must be saved as an independent narrative note and linked via note ID, preserving database index performance and document size boundaries.
- **Circular Parent Posting References**: A posting cannot reference itself or create a circular dependency loop in parent-child relationships; attempts to link circular parents must return a validation error.
- **Concurrent Updates to Postings**: Optimistic concurrency tokens or transactional version checks must ensure conflicting concurrent updates to a posting's disposition or metadata are rejected cleanly.

## Requirements *(mandatory)*

### Functional Requirements

#### Postings & Deduplication
- **FR-001**: System MUST record job postings with attributes: owner identifier, company reference, job title, original posting URL, source kind (ATS or aggregator), ATS tenant, requisition ID, physical location, remote work type, compensation (minimum, maximum, currency, free-text description), posted date, first-seen date, last-verified date, closed date, score (numeric), culture risk level (stable, moderate, high-risk, unknown), disposition, disposition reason, resurface policy, and optional parent posting reference.
- **FR-002**: System MUST enforce valid disposition values: `lead`, `scored`, `applied`, `rejected`, `screened_out`, `closed`, and `unscored`.
- **FR-003**: System MUST enforce valid resurface policy values: `never` and `if_reopened`.
- **FR-004**: System MUST deduplicate incoming postings using a primary composite key of `(ats_tenant, requisition_id)` when both are present, falling back to canonical `normalized_url` when either identifier is absent.
- **FR-005**: System MUST normalize URLs by standardizing schemes, lowercasing domain names, removing default ports, trailing slashes, and removing common tracking/marketing query parameters (e.g., `utm_*`, `ref`, `gh_src`).
- **FR-006**: When an existing posting with disposition `screened_out` or `closed` and policy `never` is submitted, System MUST return an `AlreadyDecided` status containing the original disposition and reason, and MUST NOT alter the existing record.
- **FR-007**: When a posting with policy `if_reopened` is submitted, System MUST NOT transition the posting back to `lead` automatically; it MUST require an explicit reopening command.
- **FR-008**: System MUST allow postings to reference an external narrative note ID for long-form screening rationale rather than embedding arbitrary prose in the posting record.
- **FR-009**: System MUST enforce maximum length bounds on all text fields (e.g., title, URL, location, compensation text, disposition reason) and treat all external posting text as untrusted data.

#### Companies & Career Sources
- **FR-010**: System MUST track companies with attributes: owner identifier, company name, parent company name, ownership type (public, private, VC-backed, non-profit, government), headquarters location, category tags, tracking status (`target`, `watch`, `avoid`, `neutral`), last checked timestamp, and last check result summary.
- **FR-011**: System MUST support associating one or more career sources with each company, containing: source ATS type (Workday, Greenhouse, Ashby, Lever, Avature, Oracle, UKG, or custom), tenant identifier, URL slug, fetch method (`api`, `browser`, `blocked`), and operational notes.
- **FR-012**: System MUST allow querying companies and their career sources filtered by status, category tags, or pending check date to guide search automation.

#### Criteria with Temporal History
- **FR-013**: System MUST maintain screening criteria with attributes: owner identifier, criterion key, category (`hard_skip`, `weight`, `preference`), criterion value, effective-from timestamp, effective-to timestamp (optional/null for active), provenance (`stated` or `inferred`), and active status flag.
- **FR-014**: System MUST treat criteria revisions as immutable append-only operations: updating a criterion MUST close the current version by setting its `effective_to` timestamp and insert a new revision record marked active.
- **FR-015**: System MUST support point-in-time criteria evaluation, returning the exact set of active criteria that were effective on any user-specified UTC date.
- **FR-016**: System MUST allow posting disposition reasons to reference the specific criterion key or revision that motivated the disposition.

#### Applications & Interaction Timeline
- **FR-017**: System MUST record applications linked to a specific posting, containing: owner identifier, posting reference, submitted date, desired salary range, target start date, resume variant flag (`tailored`, `as_is`, `ic_framed`), external resume pointer reference, referral contact reference ID, confirmation code/reference, and overall outcome.
- **FR-018**: System MUST forbid storing raw resume or cover letter document text within the database; only variant metadata and external pointers may be persisted.
- **FR-019**: System MUST maintain an append-only timeline of application events for each application, capturing: event timestamp, event type (`applied`, `follow_up`, `rejected`, `interview`, `offer`, `withdrawn`), event source, and descriptive notes.
- **FR-020**: Updating an application outcome MUST synchronize the parent posting's disposition within the same transaction.

#### Posting Signals & Dynamic Assessment
- **FR-021**: System MUST record signals observed against postings, capturing: posting reference, signal type (`repost`, `no_salary`, `parallel_posting`, `gone`, `phantom`, `deadline`), evidence description, and observation timestamp.
- **FR-022**: System MUST dynamically compute a ghost likelihood rating when a posting is retrieved, combining active signals using deterministic rules, and MUST NOT persist this calculated rating to storage.

#### Confidential Contacts Isolation
- **FR-023**: System MUST store third-party contact records with: owner identifier, name, current role, company affiliation, and relationship category (recruiter, referral, hiring manager, peer).
- **FR-024**: System MUST strictly isolate contacts from general search results: default queries for postings, companies, or criteria MUST NEVER return contact personal data.
- **FR-025**: System MUST ensure that contact names and personal information never appear in system logs, diagnostics, status command output, or error messages.
- **FR-026**: System MUST restrict contact retrieval to dedicated, explicit single-entity lookup operations referencing the specific contact identifier.

#### Search Runs & Integrity
- **FR-027**: System MUST record search run executions with: owner identifier, start timestamp, count of newly identified postings, and a link to the corresponding run report note ID.
- **FR-028**: System MUST provide an integrity verification check that detects and reports orphaned or dangling references: postings referencing non-existent companies, applications referencing non-existent postings, parent posting links referencing non-existent records, and notes linked to missing entities.
- **FR-029**: The integrity verification check MUST be read-only and MUST NOT delete or alter any data.

#### Cross-Cutting Architectural & Safety Constraints
- **FR-030**: Every record across all entities MUST include an `owner_id` attribute.
- **FR-031**: Every write operation (create, update, transition) across all entities MUST write an append-only audit entry in the same transaction capturing actor, command name, and before/after summaries.
- **FR-032**: System MUST NOT provide any hard-delete operations or tools; removal of entities is represented exclusively through inactive, closed, superseded, or archived states.
- **FR-033**: Expected operational failures (e.g., entity not found, duplicate already decided, concurrency conflict, invalid state transition) MUST be returned as typed result objects rather than throwing unhandled exceptions.
- **FR-034**: All new entities MUST be completely covered by the existing encrypted backup, wipe, and restore mechanisms, including restore verification guards.
- **FR-035**: All test suites, seed fixtures, and documentation MUST use strictly fictional data with zero real names or organizations.
- **FR-036**: The Model Context Protocol (MCP) server interface MUST expose at most 6 new tools (maintaining a strict ceiling of at most 12 total tools across Features 001 and 002) with zero delete tools.

### Key Entities *(include if feature involves data)*

- **Posting**: The core opportunity record containing company link, role title, normalized URL, ATS identifiers (tenant, requisition ID), compensation bounds, remote status, score, culture rating, disposition, disposition reason, resurface policy, and parent link.
- **Company**: An employer organization tracking name, parent company, ownership type, category tags, headquarters, tracking status (`target`, `watch`, `avoid`, `neutral`), and recent verification history.
- **CareerSource**: Specific careers portal configuration for a company, defining the ATS platform, tenant name, URL slug, fetch method (`api`, `browser`, `blocked`), and access guidance.
- **Criterion**: An immutable screening rule with key, category (`hard_skip`, `weight`, `preference`), value, validity timeframe (effective-from and effective-to), and stated or inferred provenance.
- **Application**: A formal submission record linked to a posting, containing submission dates, compensation targets, resume variant classification and pointer, referral contact reference, confirmation code, and outcome.
- **ApplicationEvent**: A chronological interaction milestone on an application timeline (applied, interview round, follow-up, offer, withdrawal).
- **PostingSignal**: An observable quality or risk signal (repost, omitted salary, phantom listing) tied to a posting with evidence and timestamp.
- **Contact**: A private third-party professional contact (recruiter, referral) kept isolated from general queries and diagnostics.
- **SearchRun**: An execution record of a search batch capturing timestamp, new postings discovered, and reference to a run narrative note.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Submitting the exact same job posting 100 times across three different aggregator URL formats results in exactly one stored posting record.
- **SC-002**: Re-evaluating a previously screened-out posting with policy `never` returns an `AlreadyDecided` response with the original rationale in a single lookup operation without modifying the datastore.
- **SC-003**: Point-in-time criteria queries return the 100% accurate, active criteria set for any historical timestamp across multiple revisions and superseded rules.
- **SC-004**: 100% of state-modifying write operations generate exactly one corresponding append-only audit entry within the same transaction.
- **SC-005**: Architecture and contract tests verify that zero hard-delete operations or tools exist across the entire codebase.
- **SC-006**: Lookup of a posting by deduplication key completes in under 100 milliseconds (p95) on a dataset of 5,000 postings; searching postings by company or disposition completes in under 200 milliseconds (p95).
- **SC-007**: Automated scans of logs, error payloads, diagnostic status outputs, and general search results detect zero third-party contact names or personal details.
- **SC-008**: Performing an encrypted backup followed by a wipe and restore operation yields 100% identical entity counts across all new entities (postings, companies, sources, criteria, applications, events, signals, contacts, search runs).
- **SC-009**: The total number of tools exposed by the Model Context Protocol (MCP) server does not exceed 12 tools, with zero delete tools.

## Assumptions

- **Single-Owner Scope**: The system continues to operate for a single authorized owner, using a consistent owner identifier across all records and transactions.
- **External Scraping Architecture**: Fetching and parsing HTML from careers sites is executed by external scheduled jobs or agents; this feature provides the structured storage, criteria, and retrieval API, not the web crawler.
- **Scoring Ingestion**: Posting scores and culture ratings are computed and supplied by the agent or user; this feature stores the inputs and computes dynamic signals like ghost-likelihood.
- **Resume File Storage**: Resume binaries and cover letters are stored in the user's local filesystem or document store; this tracker stores only the variant taxonomy and external path/pointer.
- **Legacy Export Compatibility**: The data model accommodates the structure of the 78 legacy postings and ~100 companies, including free-text compensation descriptions and parent-child posting hierarchies.
- **Database Engine**: Relational structured tracking builds upon the local datastore ecosystem established in the project without introducing unapproved libraries or network listeners.
- **URL Canonicalization Rules**: Standard URL normalization strips common marketing tracking query parameters (`utm_*`, `ref`, `source`, `gh_src`) while preserving semantic query parameters required by custom career portals.
- **Fictional Data Mandate**: All test suites, seed fixtures, and sample data remain strictly fictional to uphold repository privacy standards.
