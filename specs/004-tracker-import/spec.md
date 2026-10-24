# Feature Specification: Job-Search Tracker Import

**Feature Branch**: `004-tracker-import`

**Created**: 2026-10-24

**Status**: Draft

**Input**: User description: "Feature 004-tracker-import: a CLI-only importer that loads a previous job-search tracker into RoleRadar's structured collections. No new MCP tools (ceiling stays 12). Contacts stay CLI-only and are never created from free text."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Safe Ingestion & Execution Modes (Priority: P1)

As a job searcher migrating historical records, I want the importer to default to a safe preview (dry-run mode) and require an explicit flag (`--apply`) before writing any state, so that I can inspect validation results, counts, and potential issues without risking unintentional changes to my tracking data.

**Why this priority**: Preventing inadvertent database corruption or unvetted historical record injection is paramount. Dry-run safety and idempotent execution protect the integrity of the tracking system.

**Independent Test**: Can be fully tested by running the importer without `--apply` against an import bundle, verifying that zero database mutations and zero audit entries occur while emitting expected counts, then running with `--apply` to commit changes, and running `--apply` a second time to verify zero changes (idempotent no-op).

**Acceptance Scenarios**:

1. **Given** a valid import bundle directory, **When** the import command is executed without `--apply`, **Then** the system parses and validates all files, computes proposed creations and transitions, and outputs summary counts and row numbers without executing any database writes or generating audit records.
2. **Given** an import bundle processed in dry-run mode, **When** the same bundle is executed with `--apply`, **Then** the counts of created, updated, and skipped entities exactly match the counts calculated in the dry-run execution.
3. **Given** an import bundle successfully applied to the database, **When** the import command is re-executed with `--apply` against the same bundle without modifications, **Then** the system detects that all records are already reconciled, commits zero database modifications, writes zero audit entries, and explicitly reports that zero changes were made.
4. **Given** an import bundle where one or more rows lack explicit date fields, **When** executed without a `--default-date` argument, **Then** the command refuses to run and reports a validation error requiring `--default-date`; **When** `--default-date` is supplied, **Then** all undated rows use that date and the summary output reports the exact number of defaulted rows.
5. **Given** an import bundle containing a row with invalid or unparsable values, **When** executed, **Then** the failure is isolated to that specific row, the row number and validation error are recorded in the error report, and processing continues for all remaining rows without aborting the batch.

---

### User Story 2 - Companies, Watchlist Statuses, and Aliases (Priority: P1)

As a job searcher, I want company records created, matched, and updated with watchlist preferences (watch or avoid) and parent organization summaries from the bundle, so that existing and new employer profiles accurately reflect historical research and strategic prioritization.

**Why this priority**: Postings and applications link to companies. Accurate company resolution—without guessing or fuzzy matching—establishes the structural foundation for all subsequent tracking entities.

**Independent Test**: Can be tested by providing `watchlist-companies.csv`, optional `company-aliases.csv`, and company fields from `postings.json`, verifying that canonical names are resolved, watchlist statuses (`watch` or `avoid`) are set, unmatched aliases are reported, and parent text is appended to company summaries.

**Acceptance Scenarios**:

1. **Given** a row in `watchlist-companies.csv` with company name, proposed status (`watch` or `avoid`), descriptive text, and a blank decision, **When** imported with `--apply`, **Then** the company record is created or updated with the specified status, the text is appended to the company description or summary, and an audit entry is generated.
2. **Given** a row in `watchlist-companies.csv` with decision set to `"skip"`, **When** imported, **Then** the row is ignored and reported as skipped by user decision.
3. **Given** a row in `watchlist-companies.csv` with a decision value other than blank or `"skip"`, **When** imported, **Then** the row is treated as a validation failure naming the row number.
4. **Given** a row with non-empty `corrected_notes`, **When** imported, **Then** the corrected notes content replaces the row's original descriptive text.
5. **Given** an optional `company-aliases.csv` file mapping aliases to canonical company names, **When** postings or appendix rows reference an alias, **Then** the system resolves the record to the canonical company; **When** a name cannot be resolved, **Then** the system reports the unmatched name and never makes fuzzy or probabilistic guesses.
6. **Given** a posting with a `parent` field containing descriptive organization text, **When** imported, **Then** the text is stored as company summary/background text rather than creating a hierarchical posting link.

---

### User Story 3 - Postings and Appendix Rows Ingestion (Priority: P1)

As a job searcher, I want historical postings from `postings.json` and legacy search outcomes from `appendix-rows.csv` imported through the standard posting lifecycle handlers, so that deduplication tiers, screening reasons, and the disposition state machine are strictly enforced.

**Why this priority**: Postings represent the core tracking artifact. Routing them through standard handlers guarantees that duplicate detection, screening rules, culture assessments, and audit logs maintain identical integrity whether entered interactively or via batch import.

**Independent Test**: Can be tested by importing postings with various identifiers (requisition ID, URL, company + title), verifying tiered deduplication, asserting that proposed dispositions map onto permitted posting states, and confirming that invalid state transitions are rejected and reported without crashing the batch.

**Acceptance Scenarios**:

1. **Given** postings in `postings.json`, **When** imported, **Then** `tier` is dropped; `compHigh` maps to `comp_high_priority`; `cultureLevel` maps to the culture level enum (`stable`, `moderate`, `high_risk`, `unknown`) defaulting unmapped values to `unknown` with a reported warning count; `culture` text maps to `culture_summary` (truncated to length limit with count reported); and `ghost` flags map to an existing ghost signal (`phantom`) or a screening note line without adding new signal types.
2. **Given** rows in `appendix-rows.csv` without requisition ID or URL, **When** imported, **Then** the importer matches existing postings using company name and normalized title.
3. **Given** an appendix row with `proposed_disposition` marked `dismissed`, **When** imported, **Then** the posting is created or updated in disposition `screened_out` with resurface policy `never`.
4. **Given** an appendix row identified as a duplicate where the target posting exists, **When** imported, **Then** it links `duplicate_of_id` to the target posting; **When** the target posting does not exist, **Then** it imports as `screened_out` with reason `"duplicate"`.
5. **Given** an appendix row whose proposed disposition requires a transition forbidden by the posting state machine, **When** imported, **Then** the importer rejects the invalid transition, leaves the existing posting intact, and logs a validation error naming the row.
6. **Given** an imported posting write, **When** committed under `--apply`, **Then** the operation generates exactly one audit entry with an import-specific operation name and per-row isolation.

---

### User Story 4 - Screening Criteria Creation & Provenance (Priority: P2)

As a job searcher, I want primary screening reasons (`reason_primary`) automatically registered as screening criteria and referenced by disposition decisions, along with predefined seed compensation criteria, so that historical screening decisions are permanently linked to auditable rules.

**Why this priority**: Preserving the rationale behind screening rejections requires traceable criteria. Automatically capturing distinct screening reasons as criteria ensures every screened-out role points to a structured criterion.

**Independent Test**: Can be tested by running the importer against appendix rows with novel `reason_primary` values, verifying that criteria are created on first use with inferred provenance, asserting that disposition reasons reference the new criterion ID, and confirming seed compensation criteria are initialized with stated provenance.

**Acceptance Scenarios**:

1. **Given** the importer initializes criteria, **When** executed, **Then** seed criteria `comp_preferred_floor=200000` and `comp_high_priority_bar=220000` are created if not already present, with provenance recorded as `stated`.
2. **Given** an appendix row with a non-empty `reason_primary`, **When** the criterion does not already exist, **Then** a new criterion is registered on first use with provenance `inferred` and effective date set to the row's date (or default date).
3. **Given** an appendix row with `reason_primary`, **When** the posting is created or transitioned to `screened_out`, **Then** the posting's `disposition_criterion_id` references the created or existing criterion.

---

### User Story 5 - Applications, Interaction Events, and Standalone Notes (Priority: P2)

As a job searcher, I want historical application submissions, outcomes, and job-search notes imported from `applications.csv`, so that my application history and interview timelines are preserved without creating unauthorized contacts or losing contextual flags.

**Why this priority**: Applications track real submissions and their outcomes (applied, rejected, referral, fit, resume variant). Capturing them accurately completes the job-search timeline while strictly adhering to privacy and contact rules.

**Independent Test**: Can be tested by importing `applications.csv` containing both `application` and `standalone_note` kinds, asserting that applications transition postings to `applied`, rejection events append to timelines, flagged dates record notes, standalone notes land in the `job-search` area, and mentioned contacts are reported for manual addition rather than auto-created.

**Acceptance Scenarios**:

1. **Given** a row in `applications.csv` with `kind: application`, **When** imported with `--apply`, **Then** the parent posting transitions to `applied`, an application record is created capturing applied date, desired salary, referral indicator, fit rating, and resume variant (`tailored`, `as_is`, `ic_framed`).
2. **Given** an application row with `rejected: true` and `rejected_date_md`, **When** imported, **Then** a `rejected` event is appended to the application's timeline and the posting transitions to `rejected`.
3. **Given** an application row flagged with `review_flags` containing `date_is_stated_not_submitted`, **When** imported, **Then** the application retains the stated date as applied date, and the flag and explanation are recorded in the event/application notes.
4. **Given** an application row with `kind: standalone_note`, **When** imported, **Then** a note is created in area `job-search` containing the row's text and metadata.
5. **Given** rows in `applications.csv` flagged `contains_people` or containing text in `people_detected`, **When** imported, **Then** zero contacts are created automatically, and the detected names and row numbers are listed in the execution report for manual entry via `roleradar contact add`.

---

### User Story 6 - Execution Reporting & Privacy Guard (Priority: P3)

As a job searcher, I want execution summaries presented strictly as aggregate outcome counts and row numbers, with an optional detailed report export that refuses to write inside a git repository, so that confidential employer names and personal details never leak to terminal displays or git-tracked files.

**Why this priority**: RoleRadar is a public portfolio repository backing a private search. Terminal outputs and git work trees must never be contaminated with company opinions, compensation numbers, or contact names.

**Independent Test**: Can be tested by running an import with console output and inspecting stdout/stderr for zero company names or free text, requesting `--report <path>` targeting a path inside the git work tree and asserting rejection, and requesting `--report <path>` outside the git work tree and verifying successful file creation with detailed row mappings.

**Acceptance Scenarios**:

1. **Given** an import run (dry-run or apply), **When** outputting to the console, **Then** the output displays exclusively aggregate counts (created, updated, skipped, failed, defaulted dates) and row numbers for warnings/errors, and contains ZERO company names, job titles, or imported free text.
2. **Given** an import command invoked with `--report <path>` where `<path>` resolves inside the current git working tree, **When** evaluated, **Then** the importer immediately refuses the operation with a clear error explaining that detailed reports cannot be written inside a git work tree.
3. **Given** an import command invoked with `--report <path>` pointing to a valid location outside the git work tree, **When** executed, **Then** a detailed UTF-8 report is written containing comprehensive row-by-row mapping outcomes, warning reasons, and manual contact action lists.

---

### Edge Cases

- **Missing Requisition ID and Missing URL**: Postings and appendix rows lacking both identifiers are matched strictly on canonical company name plus normalized title. If multiple existing postings match the same company and title, the row is flagged as ambiguous and rejected rather than arbitrarily merged.
- **Malformed or Incomplete CSV/JSON**: Files with syntax errors, unexpected headers, or malformed JSON payloads fail upfront validation with an informative error naming the file and syntax issue before any database transactions begin.
- **Unmapped Culture Levels**: Values for `cultureLevel` in `postings.json` that do not match known enum names fall back to `unknown` and increment the reported unmapped culture level counter.
- **Oversized Text Fields**: Free text exceeding schema limits (e.g., culture summaries exceeding 500 characters, company descriptions exceeding 300 characters) are safely truncated to the maximum allowed length, and a truncation count is included in the summary report.
- **Untrusted Scripting or Prompt Injection**: All input text is treated as inert user data. No text is executed, interpreted as code, or evaluated as agent prompts.
- **Zero-Byte or Empty Bundle Directory**: An empty directory or missing mandatory files produces a clear error listing the missing expected files.
- **Read-Only Bundle Filesystem**: The importer operates with read-only file permissions on the import bundle directory and never attempts to modify, rename, or delete bundle files.

## Input Schemas & Fictional Fixtures *(mandatory)*

All examples below use strictly FICTIONAL names, titles, and figures in compliance with Constitution Principle I.

### 1. `postings.json`

Object keyed by slug representing previously tracked job postings.

```json
{
  "acme-lead-eng": {
    "added": "2026-03-01",
    "comp": "$190,000 - $215,000",
    "compHigh": false,
    "company": "Acme Widgets Ltd",
    "culture": "Collaborative engineering environment with quarterly hackathons and flexible core hours.",
    "cultureLevel": "stable",
    "first": "2026-02-28",
    "ghost": false,
    "href": "https://careers.acmewidgets.example.com/jobs/101",
    "loc": "Remote - US",
    "note": "Initial screening note regarding distributed architecture background.",
    "order": 1,
    "parent": "Acme Global Holdings - Industrial component manufacturing subsidiary.",
    "posted": "2026-02-25",
    "score": 8.5,
    "status": "lead",
    "tier": "tier-1",
    "title": "Lead Software Engineer"
  },
  "globex-staff-arch": {
    "added": "2026-03-05",
    "comp": "$225,000+",
    "compHigh": true,
    "company": "Globex Innovations",
    "culture": "Rapid turnaround delivery pace with high ownership expectations.",
    "cultureLevel": "moderate",
    "first": "2026-03-04",
    "ghost": true,
    "href": "https://jobs.globex.example.org/p/7749",
    "loc": "Hybrid - New York, NY",
    "note": "Role reposted three times in six months.",
    "order": 2,
    "parent": "Globex Research Ventures",
    "posted": "2026-03-01",
    "score": 7.0,
    "status": "scored",
    "tier": "tier-2",
    "title": "Staff Cloud Architect"
  }
}
```

- **Field Rules**:
  - `tier`: Dropped during import (not retained in domain model).
  - `parent`: Company descriptor text, appended to company summary.
  - `compHigh`: Mapped to `comp_high_priority` boolean.
  - `cultureLevel`: Mapped to `CultureLevel` enum (`stable`, `moderate`, `high_risk`, `unknown`).
  - `culture`: Mapped to `culture_summary` (truncated to 500 characters).
  - `ghost`: Mapped to `SignalType.Phantom` posting signal.

### 2. `appendix-rows.csv`

Legacy screening and triage outcomes. Most rows lack requisition IDs and URLs.

```csv
source,proposed_disposition,company,title,req,comp_detected,comp_is_estimate,remote_mentioned,details_raw,added_md,reason_primary,reason_all,proposed_resurface,company_also_in_db,review_flags,reason_text,decision,corrected_notes
aggregator,screened_out,Initech Systems,Senior Platform Engineer,,180000,false,true,Discovered on board,2026-03-02,tech_stack_mismatch,legacy_stack;low_comp,never,false,,Requires legacy COBOL maintenance,,
referral,applied,Soylent Corporation,Backend Systems Developer,REQ-404,210000,false,true,Direct submission,2026-03-10,,,,true,,Submitted via internal portal,,
direct,dismissed,Hooli Cloud,Principal DevOps Engineer,,240000,true,false,Career portal check,2026-03-12,on_site_mandate,on_site_mandate,never,false,,Requires 5 days onsite,skip,Skipped per review
portal,duplicate,Umbrella Corp,Distributed Systems Engineer,,195000,false,true,Aggregator repost,2026-03-15,duplicate,duplicate,never,true,,Matches previously evaluated posting,,Updated notes for duplicate role
```

- **Field Rules**:
  - `proposed_disposition`: Mapped to `PostingDisposition`. `dismissed` maps to `screened_out` with policy `never`. `duplicate` maps to `duplicate_of_id` link or `screened_out` with reason "duplicate".
  - `decision`: Blank = accept. `"skip"` = skip row. Any other string = validation failure.
  - `corrected_notes`: If non-empty, replaces `reason_text` or note content.
  - `reason_primary`: Auto-registered as `Criterion` on first use with provenance `inferred`.

### 3. `applications.csv`

Application history and interaction milestones.

```csv
kind,company,title,req,applied_on,rejected,rejected_date_md,desired_salary,referral,fit,resume_variant,people_detected,review_flags,raw,decision,corrected_notes
application,Soylent Corporation,Backend Systems Developer,REQ-404,2026-03-10,false,,215000,true,strong_fit,tailored,Jane Doe (Hiring Mgr),contains_people,Applied via referral,,
application,Initech Systems,Site Reliability Engineer,,2026-02-15,true,2026-03-01,195000,false,moderate_fit,as_is,,date_is_stated_not_submitted,Formal confirmation dated 2026-02-20,,Corrected date notes
standalone_note,Massive Dynamic,General Inquiry,,2026-03-08,,,,,,,,,Informational interview notes regarding robotics division,
```

- **Field Rules**:
  - `kind`: `application` creates application and transitions posting to `applied`. `standalone_note` creates a note in area `job-search`.
  - `date_is_stated_not_submitted`: Keeps stated date and records the flag in event notes.
  - `people_detected` / `contains_people`: Reported in manual contact action list; NO contact created.

### 4. `watchlist-companies.csv`

Employer strategic preferences and background.

```csv
company,proposed_status,text,decision,corrected_notes
Acme Widgets Ltd,watch,Leading manufacturing automation provider expanding software group.,,
Cyberdyne Systems,avoid,Strict mandatory return-to-office and recorded organizational turbulence.,,
Tyrell Corporation,watch,Autonomous robotics research and development.,skip,
```

- **Field Rules**:
  - `proposed_status`: `watch` or `avoid` maps to `CompanyStatus`.
  - `decision`: Blank = accept; `"skip"` = skip row; other = validation error.
  - `corrected_notes`: Replaces `text` when present.

### 5. `company-aliases.csv` (Optional)

Maps informal or historical company names to canonical records without guessing.

```csv
alias,canonical
Acme Widgets,Acme Widgets Ltd
Globex,Globex Innovations
Cyberdyne,Cyberdyne Systems
```

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST provide a dedicated CLI command `roleradar import tracker <bundle-directory>` for importing historical tracker bundles.
- **FR-002**: System MUST default to a dry-run execution mode with zero database writes and zero audit logs unless the `--apply` flag is explicitly provided.
- **FR-003**: System MUST require the `--default-date` parameter whenever one or more imported rows lack an explicit date, and MUST report the count of records that used the default date.
- **FR-004**: System MUST perform all write operations through existing Application command handlers (`ICommandHandler`), applying standard transaction and audit decorators.
- **FR-005**: System MUST record one append-only audit entry per write with an import-specific operation name.
- **FR-006**: System MUST enforce per-row failure isolation such that a validation or mapping failure on a single row is recorded as an error and does not terminate the import process for remaining valid rows.
- **FR-007**: System MUST support the `decision` column in CSV files: blank accepts the proposed action; `"skip"` ignores the row and records it as skipped; any other value triggers a validation error identifying the row number.
- **FR-008**: System MUST replace a row's original descriptive or reason notes with the content of `corrected_notes` whenever `corrected_notes` is non-empty.
- **FR-009**: System MUST match postings lacking a requisition ID and URL using the canonical company name and normalized title.
- **FR-010**: System MUST guarantee idempotent re-runs: re-running `--apply` against an identical bundle MUST produce zero changes, zero audit entries, and report zero modifications.
- **FR-011**: System MUST map `proposed_disposition` onto existing `PostingDisposition` states and reject any transition that violates the disposition state machine without bypassing validation.
- **FR-012**: System MUST map `dismissed` dispositions to `screened_out` with resurface policy `never`.
- **FR-013**: System MUST resolve duplicate rows against existing postings, linking `duplicate_of_id` if the target posting is found, or importing as `screened_out` with reason `"duplicate"` if no target posting exists.
- **FR-014**: System MUST automatically create new screening criteria on first use from `reason_primary` values with inferred provenance (`inferred`), and link the posting's `disposition_criterion_id` to the criterion.
- **FR-015**: System MUST seed predefined criteria `comp_preferred_floor=200000` and `comp_high_priority_bar=220000` with provenance `stated` if they do not exist.
- **FR-016**: System MUST map `compHigh` to `comp_high_priority` boolean and ignore `tier` in `postings.json`.
- **FR-017**: System MUST map `cultureLevel` to `CultureLevel` enum, falling back to `unknown` for unrecognized values and reporting the count of unmapped values.
- **FR-018**: System MUST store `culture` text as `culture_summary`, safely truncating to 500 characters and reporting the truncation count.
- **FR-019**: System MUST map `ghost: true` to a `SignalType.Phantom` posting signal.
- **FR-020**: System MUST store `parent` text from `postings.json` as company description/summary text rather than a posting relationship.
- **FR-021**: System MUST treat all imported text as untrusted data, enforcing character length limits and storing content without script or query evaluation.
- **FR-022**: System MUST list all rows flagged `contains_people` or containing `people_detected` text in the execution report for manual entry via `roleradar contact add`, and MUST NEVER create contact records automatically.
- **FR-023**: System MUST import rows in `applications.csv` with `kind: standalone_note` as notes in the `job-search` area.
- **FR-024**: System MUST record the `date_is_stated_not_submitted` flag in event/application notes while preserving the stated date on the application.
- **FR-025**: System MUST emit console output consisting exclusively of outcome counts and row numbers, never printing company names, job titles, or imported text.
- **FR-026**: System MUST support an optional `--report <path>` flag to write a detailed report, and MUST refuse execution if `<path>` resides inside a git working tree.
- **FR-027**: System MUST NOT add any new MCP tools, keeping the total MCP tool ceiling at 12.
- **FR-028**: System MUST resolve aliases from `company-aliases.csv` using exact matching, and MUST report all unmatched company names without applying fuzzy or heuristic matching.

### Key Entities

- **ImportBundle**: The source directory containing up to five files (`postings.json`, `appendix-rows.csv`, `applications.csv`, `watchlist-companies.csv`, and optional `company-aliases.csv`).
- **Company**: Employer entity tracked with canonical name, description (including legacy parent and watchlist text), status (`target`, `watch`, `avoid`, `neutral`), and category tags.
- **Posting**: Individual job role with title, company link, location, compensation, culture ratings, signals (`phantom`), disposition, and duplicate link.
- **Application**: Submission record capturing application date, desired salary, referral flag, fit rating, resume variant, and chronology of events (`applied`, `rejected`).
- **Criterion**: Decision standard with key, category, value, provenance (`stated` or `inferred`), and effective date, cited by posting screening dispositions.
- **ImportSummary**: Aggregated execution results containing counts of created, updated, skipped, and failed entities, defaulted dates, unmapped values, truncated fields, and row error references.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Fictional test fixtures cover 100% of defined schema mappings, valid transitions, and error paths.
- **SC-002**: Dry-run summary counts (created, updated, skipped) exactly equal apply summary counts when executed against the same bundle.
- **SC-003**: Executing `--apply` a second time against an identical bundle results in 0 created records, 0 updated records, 0 audit entries, and explicitly reports an idempotent no-op.
- **SC-004**: The system integrity check (`roleradar verify`) exits with status code 0 (clean) after importing the bundle.
- **SC-005**: Executing database backup followed by restore round-trips 100% of the imported data without loss or corruption.
- **SC-006**: The importer runs successfully from the `roleradar-cli` container image against a read-only mounted import bundle directory.
- **SC-007**: Combined code coverage across `Domain` and `Application` test suites remains at or above 85%.
- **SC-008**: The repository privacy denylist pre-commit hook passes with zero detections of private or real-name data.
- **SC-009**: Console output contains zero occurrences of company names, job titles, salary figures, or free text across all run modes.
- **SC-010**: Invoking `--report` with any path inside the git work tree fails immediately with exit code 1 and writes no files.

## Assumptions

- The import bundle files are encoded in standard UTF-8.
- The existing `ICommandHandler` pipeline and audit infrastructure from Features 001 and 002 are sufficient to support all import writes.
- The `roleradar contact add` command remains the sole authorized mechanism for creating contact records.
- Date fields in CSV and JSON follow ISO-8601 (`YYYY-MM-DD`) or standard month-day format (`added_md`) resolvable with the explicit `--default-date` year.
