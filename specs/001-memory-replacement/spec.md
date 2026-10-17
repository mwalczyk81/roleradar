# Feature Specification: Memory Replacement

**Feature Branch**: `001-memory-replacement`

**Created**: 2026-10-17

**Status**: Draft

**Input**: User description: "Feature: memory replacement. As the sole user, I want my AI agent to keep durable, searchable notes about my job search outside its own size-limited memory files, so I stop hitting the roughly 48 KB per-file memory cap.

User stories, in priority order:

US1 (P1): Store and retrieve notes. The agent can create a note (title, markdown body, tags, an area label such as "criteria" or "runbook", and a provenance marker of stated, researched or inferred with a date), read it back by id, append to an existing note, replace a section of a note, and search notes by text and by tag or area. Notes persist across agent sessions and across a computer restart. Independent test: create, restart everything, search, find the note.

US2 (P1): Bring in my existing memory files. A command imports one or more markdown files by splitting on headings into separate notes, preserving the original text verbatim, keeping provenance tags written as "[stated YYYY-MM-DD]" as the note's provenance, and being safe to re-run without creating duplicates. Independent test: import a fictional two-file sample twice, see the same note count.

US3 (P2): Durable data and backup. One command starts the data services with data on persistent volumes. One command writes a dated, compressed, passphrase-encrypted backup of everything to a folder I choose, and one command restores from a backup. A separate, clearly named command wipes all data and refuses to run without explicit confirmation. Independent test: backup, wipe, restore, same notes.

US4 (P2): Safe by construction. Every write by the agent or the import command is recorded in an audit log (who, which operation, which note, when, before and after summaries). The agent has no way to permanently delete a note; it can archive one, and archived notes are excluded from search by default. If the data services are not running, the agent receives a clear message saying how to start them rather than hanging.

Out of scope for this feature: companies, postings, applications, criteria tables, authentication, any web UI, any network listener, hosting.

Success criteria: the two markdown memory files (about 46 KB and 26 KB) import with zero text loss; search returns results in under 200 ms for 5,000 notes; all stories pass from a clean clone using only documented commands.

Edge cases: very long note bodies (over 100 KB), duplicate titles, empty headings, non-UTF-8 input, concurrent appends from two agent sessions, a backup passphrase that is wrong, a restore into a non-empty database."

## Clarifications

### Session 2026-10-17

- Q: How are prior note texts preserved across body-changing writes and archiving? → A: Body-changing writes (replace section, append, import overwrite) MUST preserve the full prior text in an append-only note revision history; audit entries reference the revision, and archive never loses text.
- Q: How should re-import handle a note that was edited after its last import? → A: Import MUST NOT overwrite a note changed since its last import (where current body hash differs from the last-imported hash); it skips that note and lists it as a conflict in the import summary.
- Q: How are imported sections keyed and ordered to guarantee exact byte-level file reassembly? → A: Import key = source file identity + heading path + occurrence index among identical sibling headings; store a section order index so files can be reassembled and preserve bytes exactly, including line endings, BOM, and trailing newline.
- Q: What happens during re-import when a previously imported section is removed or renamed in the source file? → A: Re-import never deletes; sections missing from the source or renamed stay as they are and are reported as orphaned in the import summary.
- Q: What is the exact grammar and precedence for inline provenance tags during import? → A: Provenance tag grammar is `[kind]`, `[kind YYYY-MM-DD]`, or `[kind YYYY-MM-DD qualifier words]` (`kind` in `stated`, `researched`, `inferred`) anywhere in a section including its heading; a section's structured provenance is the first tag in document order (undated tags use the file date), all tags stay verbatim in the body, and per-fact provenance extraction is deferred to a later feature.
- Q: How do append and section-replacement operations handle concurrency control? → A: Append is atomic and server-side (no version token needed); replace-section requires the caller's version token and rejects a stale token with a retryable conflict error.
- Q: How must the backup passphrase be supplied and how is plaintext data protected during backup? → A: The backup passphrase comes from an interactive prompt, an environment variable, or stdin (never a command-line argument); backup streams dump output into encryption so no plaintext file is written, and a test inspects temp locations.
- Q: What are the query matching, filtering, and ordering rules for note search? → A: Search is case-insensitive, whole-word with basic stemming, supports quoted phrases, ANDs tag and area filters, and orders results by relevance, then updated time, then id.
- Q: What are the constraints on the agent-facing tool surface and primary target platform? → A: The agent-facing tool set is capped at 8 tools, and Windows with PowerShell is the primary target.
- Q: Which canonical term must describe audit log and revision history persistence guarantees? → A: Use the term "append-only" (no update or delete path exists), not "immutable".

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Store, Update, and Retrieve Searchable Notes (Priority: P1)

As the sole user working through an AI agent, I want my agent to create, read, append to, update sections of, and search structured markdown notes with tags, area labels, and provenance markers so that job-search knowledge is stored durably outside size-capped flat memory files and remains retrievable across sessions and system restarts.

**Why this priority**: This is the core capability that replaces the 48 KB memory file ceiling. Without durable note creation, targeted updates, and fast search/retrieval by the agent, the system cannot function as an external memory store.

**Independent Test**: Can be fully tested by having the agent create a note with title, markdown body, tags, area label, and provenance marker, appending content and replacing a heading section, restarting both the agent session and the local data services (simulating a machine restart), searching for the note by text and by tag/area, and reading the full note back by its identifier.

**Acceptance Scenarios**:

1. **Given** the local data services are running, **When** the agent creates a note with a title, markdown body, one or more tags, an area label (e.g., `criteria` or `runbook`), and a provenance marker (`stated`, `researched`, or `inferred` with a date), **Then** the system stores the note, assigns it a unique identifier and version token, and returns the created note metadata.
2. **Given** an existing note identifier, **When** the agent requests the note by identifier, **Then** the system returns the complete note including its exact markdown body, title, tags, area label, provenance marker and date, version token, and timestamps.
3. **Given** an existing note, **When** the agent appends markdown content to the note (without needing a version token), **Then** the append is executed atomically server-side, the full prior text is saved in an append-only revision history, the new content is added to the end of the body without altering prior text, and the note's version token and updated timestamp are refreshed.
4. **Given** an existing note containing markdown headings and its current version token, **When** the agent requests a section replacement targeting a specific heading with that version token, **Then** the full prior text is saved in an append-only revision history, only the content under that heading section is replaced while all other sections remain intact, and a stale version token is rejected with a retryable conflict error.
5. **Given** multiple stored notes across different areas and tags, **When** the agent searches using a case-insensitive query (supporting whole-word matching with basic stemming and quoted phrases) along with optional tag and area label filters (combined with logical AND), **Then** the system returns matching active notes ordered by relevance descending, then updated timestamp descending, then note identifier.
6. **Given** notes have been created and updated, **When** the agent session ends and the local environment or computer is restarted, **Then** all previously stored notes and their revision histories remain intact and searchable.

---

### User Story 2 - Import Existing Markdown Memory Files Idempotently (Priority: P1)

As the sole user migrating off legacy agent memory files, I want a command that imports one or more markdown files by splitting them on headings into individual notes while preserving every byte of original text verbatim and extracting inline provenance markers, without creating duplicates, deleting removed sections, or overwriting post-import edits if run repeatedly.

**Why this priority**: Immediate adoption depends on migrating the existing ~46 KB and ~26 KB markdown memory files without manual copy-pasting, data loss, or duplicate pollution.

**Independent Test**: Can be fully tested by running the import command twice against a fictional two-file markdown sample (sized ~46 KB and ~26 KB with inline provenance markers) and verifying that the note count is identical after both runs and reassembling the imported notes by their section order index yields 100% byte-for-byte text fidelity.

**Acceptance Scenarios**:

1. **Given** one or more valid UTF-8 markdown files organized by headings, **When** the user runs the import command, **Then** each split heading section is stored as a separate note keyed by `source file identity + heading path + occurrence index among identical sibling headings` with a `section order index`, preserving the original bytes verbatim (including line endings, UTF-8 BOM, and trailing newline) so the source file can be reassembled byte-for-byte.
2. **Given** a markdown section containing one or more inline provenance markers matching `[kind]`, `[kind YYYY-MM-DD]`, or `[kind YYYY-MM-DD qualifier words]` (where `kind` is `stated`, `researched`, or `inferred`) anywhere in the section including its heading, **When** the section is imported, **Then** the note's structured provenance is populated from the first matching tag in document order (using the source file date when the tag is undated) while keeping all tags verbatim in the body text.
3. **Given** a set of markdown files has already been imported, **When** the user runs the import command a second time on the exact same files, **Then** no duplicate notes are created and total note count remains unchanged.
4. **Given** a previously imported markdown file is edited under one heading and re-imported, and the stored note's current body hash still matches its last-imported body hash, **When** the user runs the import command, **Then** the note is updated in place and its full prior text is recorded in the append-only revision history.
5. **Given** a previously imported note has been modified since its last import (its current body hash differs from its last-imported body hash), **When** the source file is re-imported with changes to that section, **Then** the import command skips that note without overwriting it and lists it as a conflict in the import summary.
6. **Given** a previously imported section is removed or its heading is renamed in the source markdown file, **When** the file is re-imported, **Then** the existing stored note is never deleted or altered and is reported as orphaned in the import summary.

---

### User Story 3 - Manage Durable Data Storage, Encrypted Backups, and Safe Resets (Priority: P2)

As the sole user operating a local-first system on Windows with PowerShell, I want single-command lifecycle controls to start persistent data services, export dated compressed passphrase-encrypted backups to a folder of my choice without ever writing plaintext dumps to disk, restore from those backups, and intentionally wipe data only with explicit confirmation.

**Why this priority**: Because the system is local-first and stores the sole copy of curated job-search knowledge, data durability across container/service restarts and encrypted off-machine backups are essential before relying on it daily.

**Independent Test**: Can be fully tested by starting data services, importing fictional notes, writing an encrypted backup to a chosen directory (while verifying no plaintext files touch temp directories), wiping all data via the confirmation-guarded reset command, restoring from the backup file with the passphrase, and verifying all notes, revisions, and audit entries match the pre-wipe state.

**Acceptance Scenarios**:

1. **Given** a clean local machine setup, **When** the user runs the start-services command, **Then** the data services start with named persistent storage volumes attached.
2. **Given** stored notes, revisions, and audit history exist, **When** the user runs the backup command with a target output folder and supplies a passphrase via interactive prompt, environment variable, or `stdin`, **Then** the system streams dump output directly through compression and encryption into a dated archive in that folder without writing any plaintext temporary file to disk, and rejects any attempt to pass the passphrase as a command-line argument.
3. **Given** a valid encrypted backup archive and an empty datastore, **When** the user runs the restore command with the matching passphrase (via prompt, environment variable, or `stdin`), **Then** all notes, note revisions, and audit logs from the backup are restored completely.
4. **Given** stored data exists, **When** the user invokes the wipe command without explicit confirmation, **Then** the command refuses to run, exits with a non-zero status, and leaves all data untouched.
5. **Given** stored data exists, **When** the user invokes the wipe command with explicit confirmation, **Then** all note, revision, and audit data are removed cleanly.

---

### User Story 4 - Enforce Auditability, Archive-Only Deletion, and Clear Service Readiness Errors (Priority: P2)

As the sole user delegating note management to an AI agent, I want every write operation audited in an append-only log that references the note revision history, permanent deletion forbidden to the agent in favor of reversible archiving that never loses text, the agent tool surface capped at 8 tools, and fast actionable errors when data services are offline.

**Why this priority**: AI agents process untrusted external text and can make unintended edits; append-only audit trails, full prior-text revisions, and archive-only deletion bound the blast radius of mistakes, while fast readiness checks prevent hung agent sessions.

**Independent Test**: Can be fully tested by executing create, append, section-replace, import, and archive operations, verifying that each operation creates an append-only audit entry with before/after summaries and a revision reference where prior text changed, confirming archived notes retain their full text and are hidden from default search, verifying no more than 8 agent tools are exposed, and stopping the data services to verify an agent tool call immediately returns a helpful start-service instruction.

**Acceptance Scenarios**:

1. **Given** any write operation (create, append, section replace, archive, or import), **When** the operation completes, **Then** an append-only audit entry is recorded containing the actor identity, operation name, target note identifier, referenced revision identifier (for body-changing writes), UTC timestamp, and before/after content summaries.
2. **Given** an active note, **When** the agent archives the note, **Then** the note is marked archived without losing any body text or revision history, remains retrievable when explicitly requested (or by ID), and is excluded from default search results.
3. **Given** the agent's available operations, **When** inspecting or invoking agent tools, **Then** the total number of agent-facing tools is at most 8, and no operation exists in application code that permanently deletes a note or updates/deletes existing note revisions or audit log entries.
4. **Given** the local data services are stopped or unreachable, **When** the agent invokes any note operation, **Then** the operation fails within a short timeout (under 5 seconds) and returns a clear diagnostic message stating the data services are not running and providing the exact command to start them.

---

### Edge Cases

- **Very long note bodies (over 100 KB)**: The system MUST accept, store, retrieve, append to, and full-text search individual notes exceeding 100 KB (up to at least 1 MB per note) without silent truncation or indexing failure.
- **Duplicate titles and identical sibling headings**: Multiple notes MAY share the same title. Each note has a unique system ID; during import, uniqueness is keyed deterministically by `source file identity + heading path + occurrence index among identical sibling headings`, and each imported note stores a `section order index` so duplicate titles in the same or different files never collide and files reassemble in exact order.
- **Empty headings**: During markdown import, a heading that contains no body text before the next heading (or end of file) MUST be preserved deterministically with its exact bytes and `section order index` so that reassembling imported notes reproduces 100% of the source file byte-for-byte; bare heading markers with no title text (e.g., `## `) MUST receive a deterministic placeholder title while retaining the exact raw bytes in the body.
- **Non-UTF-8 input**: If an input file passed to the import command contains invalid UTF-8 byte sequences, the command MUST fail before writing partial records from that file and report the file path and encoding error clearly.
- **Concurrent appends vs. concurrent section replacements from two agent sessions**: `append` is executed atomically server-side without requiring a version token so concurrent appends never overwrite each other. `replace-section` requires the caller's version token and rejects a stale version token with a retryable conflict error while leaving the note untouched.
- **Re-import conflict when a note was edited after import**: If a previously imported note's current body hash differs from its last-imported body hash, re-importing the source file MUST skip that note rather than overwriting it and MUST report it as a conflict in the import summary.
- **Re-import with removed or renamed headings**: Re-import MUST never delete stored notes; previously imported notes whose import key no longer appears in the source file remain unchanged and are listed as orphaned in the import summary.
- **Backup passphrase via CLI argument or wrong backup passphrase**: Supplying a passphrase as a command-line argument MUST be rejected immediately. When restoring a backup with an incorrect passphrase, decryption/authentication MUST fail immediately with a clear error message before modifying any state in the target datastore.
- **Restore into a non-empty database**: When the user runs the restore command against a datastore that already contains notes, revisions, or audit records, the command MUST refuse to overwrite existing data by default and instruct the user to either run the confirmation-guarded wipe command first or pass an explicit force/overwrite confirmation flag.
- **Target section missing or ambiguous during section replacement**: If the agent attempts to replace a heading section that does not exist or appears multiple times within the same note without disambiguation, the system MUST reject the edit without modifying the note and return a clear error listing the available headings.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST allow the AI agent to create a note containing a title, markdown body, zero or more tags, an area label (such as `criteria` or `runbook`), and a provenance marker consisting of a provenance kind (`stated`, `researched`, or `inferred`), a date, and optional qualifier words.
- **FR-002**: System MUST assign every created note a unique, time-ordered identifier and version token, record creation and last-modified timestamps in UTC, and associate every record with an owner identifier.
- **FR-003**: System MUST allow the AI agent to retrieve a single note in full by its unique identifier, including archived notes.
- **FR-004**: System MUST allow the AI agent to append markdown text to an existing note atomically on the server side without requiring a caller version token, preserving the full prior text in an append-only revision history before appending.
- **FR-005**: System MUST allow the AI agent to replace the body of a specific markdown heading section within an existing note only when the caller supplies the current version token—preserving the full prior text in an append-only revision history and leaving all other sections unchanged—and MUST reject the operation with a retryable conflict error if the version token is stale, or a descriptive error if the target heading is not found or is ambiguous.
- **FR-006**: System MUST allow the AI agent to search notes with case-insensitive matching, whole-word matching with basic stemming, and quoted exact-phrase support across titles and bodies, combining tag and area filters with logical AND, excluding archived notes by default (with an option to include them), and ordering results by relevance descending, then updated timestamp descending, then note identifier.
- **FR-007**: System MUST support storing, retrieving, updating, and searching note bodies over 100 KB (up to at least 1 MB per note) without truncation.
- **FR-008**: System MUST preserve the full prior markdown body in an append-only note revision history on every body-changing write (`append`, `replace section`, and `import overwrite`), and archiving a note MUST never lose body text or revision history.
- **FR-009**: System MUST provide a command to import one or more UTF-8 markdown files by splitting them on headings into individual notes keyed by `source file identity + heading path + occurrence index among identical sibling headings` and storing a `section order index`, preserving 100% of the original file bytes verbatim—including line endings (`CRLF`/`LF`), UTF-8 BOM, trailing newline, preamble text before the first heading, and empty heading blocks—so that source files can be reassembled byte-for-byte.
- **FR-010**: During markdown import, the system MUST recognize inline provenance tags matching `[kind]`, `[kind YYYY-MM-DD]`, or `[kind YYYY-MM-DD qualifier words]` (where `kind` is `stated`, `researched`, or `inferred`) anywhere in a section including its heading, set the note's structured provenance from the first matching tag in document order (using the source file date when the tag has no date), and keep all inline tags verbatim in the note body.
- **FR-011**: The markdown import command MUST be idempotent and non-destructive on re-runs:
  - Unchanged sections (where source hash matches last-imported hash) MUST NOT create duplicate notes or redundant revisions.
  - Changed source sections whose stored note still matches the last-imported body hash MUST update the note in place while saving the full prior text in the append-only revision history.
  - Stored notes whose current body hash differs from their last-imported body hash MUST NOT be overwritten; the import MUST skip them and report them as conflicts in the import summary.
  - Previously imported notes whose import key is missing from the source file (due to deletion or heading rename) MUST NOT be deleted or modified and MUST be reported as orphaned in the import summary.
- **FR-012**: The markdown import command MUST validate that all input files are valid UTF-8 prior to importing and MUST abort with a clear error message if any file contains invalid UTF-8 sequences.
- **FR-013**: System MUST provide a single documented command (targeting Windows with PowerShell as the primary environment) to start the local data services configured with named persistent storage volumes so data survives container restarts and host reboots.
- **FR-014**: System MUST provide a single documented command to create a dated, compressed, passphrase-encrypted backup archive of all stored data (notes, note revisions, and audit logs) in a user-specified destination directory, accepting the passphrase exclusively via interactive prompt, environment variable, or `stdin` (never a command-line argument) and streaming dump output directly into encryption so no plaintext temporary file is ever written to disk.
- **FR-015**: System MUST provide a single documented command to restore all notes, note revisions, and audit logs from an encrypted backup archive using the passphrase supplied via interactive prompt, environment variable, or `stdin`, failing immediately without modifying existing data if the passphrase is wrong or the archive is corrupted, and refusing to restore into a non-empty datastore unless explicit overwrite confirmation is provided.
- **FR-016**: System MUST provide a separate, explicitly named command to wipe all stored data that refuses to run unless the user supplies explicit confirmation.
- **FR-017**: System MUST record an append-only audit log entry (no update or delete path exists in application code) for every write operation (note creation, append, section replacement, archive, and markdown import) capturing the actor identity, operation name, target note identifier, referenced note revision identifier (for body-changing writes), UTC timestamp, and before/after summaries.
- **FR-018**: The agent-facing MCP tool set MUST be capped at a maximum of 8 tools and MUST NOT expose any tool for permanently deleting notes or modifying/deleting note revisions or audit log records; the agent MAY only archive notes.
- **FR-019**: When the local data services are stopped or unreachable, any agent tool invocation or CLI command requiring data access MUST fail fast (within 5 seconds) with a clear error message stating that data services are offline and showing the exact command to start them.
- **FR-020**: All agent communication in this feature MUST operate exclusively over standard input/output without opening any network listener on the host machine, and all diagnostic logging MUST be routed away from standard output so protocol frames are never corrupted.
- **FR-021**: All automated tests, sample files, and documentation in the repository MUST use strictly fictional data and MUST NEVER contain real job-search notes, company opinions, or personal contact details.

### Key Entities *(include if feature involves data)*

- **Note**: A durable unit of job-search knowledge owned by the user. Attributes include: Unique Identifier, Owner Identifier, Title, Markdown Body (preserving exact bytes/line endings), Current Body Hash, Tags (collection of normalized labels), Area Label (category such as `criteria`, `runbook`, `preferences`, `general`), Provenance (Provenance Kind: `stated`, `researched`, or `inferred`, Provenance Date, and optional Qualifier Words), Import Source Key (optional composite of `source file identity + heading path + occurrence index among identical sibling headings`), Section Order Index (optional integer order for exact file reassembly), Last-Imported Body Hash (optional hash recorded at last import to detect post-import edits), Status (`active` or `archived`), Version/Concurrency Token, Created Timestamp (UTC), and Updated Timestamp (UTC).
- **NoteRevision**: An append-only historical record (no update or delete path exists) preserving the full prior text of a Note whenever a body-changing write (`append`, `replace section`, or `import overwrite`) occurs. Attributes include: Unique Identifier, Owner Identifier, Note Identifier, Revision Number, Prior Markdown Body (exact full text prior to the write), Prior Body Hash, Prior Version Token, Created Timestamp (UTC).
- **AuditLogEntry**: An append-only record (no update or delete path exists) of a state-changing operation. Attributes include: Unique Identifier, Owner Identifier, Actor Identity (e.g., agent session or import command), Operation Name, Target Note Identifier, Note Revision Identifier (nullable; populated for all body-changing writes), Occurred Timestamp (UTC), Before Summary (hash, byte length, and brief excerpt or metadata snapshot prior to change), and After Summary (hash, byte length, and brief excerpt or metadata snapshot after change).
- **BackupArchive**: A portable, dated, compressed, passphrase-encrypted snapshot file produced by streaming database dump output directly into encryption (with zero plaintext temp files), containing the complete set of Notes, NoteRevisions, and AuditLogEntries at a point in time, along with format/version metadata for safe restoration.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Importing a two-file markdown memory sample of approximately 46 KB and 26 KB completes with 100% byte-for-byte text preservation (reassembling imported sections by `section order index` reproduces exact bytes including line endings, UTF-8 BOM, and trailing newline) and 100% accuracy in extracting the first inline `[kind]`, `[kind YYYY-MM-DD]`, or `[kind YYYY-MM-DD qualifier words]` tag in document order as structured provenance.
- **SC-002**: Re-running the markdown import command against already-imported files produces zero duplicate notes, skips any note edited since its last import (reporting it as a conflict), and preserves any note whose section was removed or renamed in the source file (reporting it as orphaned).
- **SC-003**: Searching across a dataset of 5,000 stored notes (using case-insensitive whole-word stemmed queries, quoted phrases, and ANDed tag/area filters, ordered by relevance then updated time then id) returns results in under 200 milliseconds at the 95th percentile on the local machine.
- **SC-004**: Notes exceeding 100 KB in body size are stored, modified via atomic server-side append and version-checked section replacement (with full prior text preserved in append-only note revisions), searched, and retrieved by identifier with 100% data integrity.
- **SC-005**: 100% of write operations performed by the agent or import command generate a corresponding append-only audit log entry containing actor, operation, target note ID, referenced revision ID (for body-changing writes), timestamp, and before/after summaries.
- **SC-006**: A complete disaster-recovery cycle (creating a streamed passphrase-encrypted backup while an automated test verifies zero plaintext files are written to temporary locations, executing a confirmed wipe to zero records, and restoring from the backup) restores 100% of notes, note revisions, and audit entries identically.
- **SC-007**: When data services are stopped, 100% of agent operations return an actionable start-service error message in under 5 seconds rather than hanging.
- **SC-008**: All four user stories and verification checks pass end-to-end on Windows with PowerShell from a clean repository clone using only the documented commands, at most 8 agent-facing tools, and strictly fictional test fixtures.

## Assumptions

- **Primary Target Environment**: Windows with PowerShell is the primary target environment for all developer workflows, operational scripts, and CLI commands, while container definitions remain standard.
- **Single-Owner Local Operation**: Because authentication is out of scope for this feature, all records created by the local agent and CLI commands use a configured or default single-owner identifier (e.g., `owner_id` defaulted for the local owner) so that the data model satisfies owner-scoped invariants from day one without requiring login flows.
- **Markdown Heading Splitting & Byte-Exact Reassembly**: By default, the markdown import command splits files on top-level/section headings while keeping deeper sub-headings inside the parent note body so that cohesive sections remain readable units and can later be targeted by section replacement. Any text preceding the first split heading (including UTF-8 BOM, YAML frontmatter, or preamble paragraphs) is captured in a preamble section at index 0 so concatenating all sections for a source file in `section order index` order reproduces the original file byte-for-byte.
- **Inline Provenance Precedence & Deferred Per-Fact Extraction**: When a markdown section being imported contains one or more tags matching `[kind]`, `[kind YYYY-MM-DD]`, or `[kind YYYY-MM-DD qualifier words]` (`kind` in `stated`, `researched`, `inferred`), the first tag in document order populates the note's structured provenance (with undated `[kind]` tags inheriting the source file's date). If no tag appears in the section, the import command assigns a default provenance (`inferred` with the file's date, or configurable via CLI flag). Fine-grained per-fact provenance extraction inside a section is deferred to a subsequent feature.
- **Out of Scope Boundaries**: Structured tracking for companies, job postings, applications, and criteria tables, as well as per-fact provenance extraction, authentication, web user interfaces, network listeners, and cloud hosting, are deferred to subsequent features.
