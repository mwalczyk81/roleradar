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

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Store, Update, and Retrieve Searchable Notes (Priority: P1)

As the sole user working through an AI agent, I want my agent to create, read, append to, update sections of, and search structured markdown notes with tags, area labels, and provenance markers so that job-search knowledge is stored durably outside size-capped flat memory files and remains retrievable across sessions and system restarts.

**Why this priority**: This is the core capability that replaces the 48 KB memory file ceiling. Without durable note creation, targeted updates, and fast search/retrieval by the agent, the system cannot function as an external memory store.

**Independent Test**: Can be fully tested by having the agent create a note with title, markdown body, tags, area label, and provenance marker, appending content and replacing a heading section, restarting both the agent session and the local data services (simulating a machine restart), searching for the note by text and by tag/area, and reading the full note back by its identifier.

**Acceptance Scenarios**:

1. **Given** the local data services are running, **When** the agent creates a note with a title, markdown body, one or more tags, an area label (e.g., `criteria` or `runbook`), and a provenance marker (`stated`, `researched`, or `inferred` with a date), **Then** the system stores the note, assigns it a unique identifier, and returns the created note metadata.
2. **Given** an existing note identifier, **When** the agent requests the note by identifier, **Then** the system returns the complete note including its exact markdown body, title, tags, area label, provenance marker and date, and timestamps.
3. **Given** an existing note, **When** the agent appends markdown content to the note, **Then** the new content is added to the end of the existing body without altering prior text and the note's updated timestamp is refreshed.
4. **Given** an existing note containing markdown headings, **When** the agent requests a section replacement targeting a specific heading, **Then** only the content under that heading section is replaced while all other sections of the note remain intact.
5. **Given** multiple stored notes across different areas and tags, **When** the agent searches by free-text query, tag filter, area label filter, or a combination thereof, **Then** the system returns matching active notes ranked or ordered deterministically.
6. **Given** notes have been created and updated, **When** the agent session ends and the local environment or computer is restarted, **Then** all previously stored notes remain intact and searchable.

---

### User Story 2 - Import Existing Markdown Memory Files Idempotently (Priority: P1)

As the sole user migrating off legacy agent memory files, I want a command that imports one or more markdown files by splitting them on headings into individual notes while preserving every character of original text verbatim and extracting inline provenance markers, without creating duplicates if run repeatedly.

**Why this priority**: Immediate adoption depends on migrating the existing ~46 KB and ~26 KB markdown memory files without manual copy-pasting, data loss, or duplicate pollution.

**Independent Test**: Can be fully tested by running the import command twice against a fictional two-file markdown sample (sized ~46 KB and ~26 KB with inline `[stated YYYY-MM-DD]` markers) and verifying that the note count is identical after both runs and reassembling the imported notes yields 100% verbatim text fidelity.

**Acceptance Scenarios**:

1. **Given** one or more valid UTF-8 markdown files organized by headings, **When** the user runs the import command, **Then** each split heading section is stored as a separate note whose body preserves the original markdown text verbatim with zero character loss.
2. **Given** a markdown section containing an inline provenance marker formatted as `[stated YYYY-MM-DD]` (or `[researched YYYY-MM-DD]` / `[inferred YYYY-MM-DD]`), **When** the section is imported, **Then** the note's structured provenance kind and date are populated from that marker while keeping the original text verbatim.
3. **Given** a markdown section that contains multiple inline provenance markers or none at all, **When** the section is imported, **Then** the note uses the most recent inline provenance marker found (or defaults to an `inferred` / import-dated provenance marker when none is present) while preserving all inline markers verbatim in the body text.
4. **Given** a set of markdown files has already been imported, **When** the user runs the import command a second time on the exact same files, **Then** no duplicate notes are created and total note count remains unchanged.
5. **Given** a previously imported markdown file is edited under one heading and re-imported, **When** the user runs the import command, **Then** the corresponding note is updated in place (or reconciled deterministically) rather than duplicating unchanged sections.

---

### User Story 3 - Manage Durable Data Storage, Encrypted Backups, and Safe Resets (Priority: P2)

As the sole user operating a local-first system, I want single-command lifecycle controls to start persistent data services, export dated compressed passphrase-encrypted backups to a folder of my choice, restore from those backups, and intentionally wipe data only with explicit confirmation.

**Why this priority**: Because the system is local-first and stores the sole copy of curated job-search knowledge, data durability across container/service restarts and encrypted off-machine backups are essential before relying on it daily.

**Independent Test**: Can be fully tested by starting data services, importing fictional notes, writing an encrypted backup to a chosen directory, wiping all data via the confirmation-guarded reset command, restoring from the backup file with the passphrase, and verifying all notes and audit entries match the pre-wipe state.

**Acceptance Scenarios**:

1. **Given** a clean local machine setup, **When** the user runs the start-services command, **Then** the data services start with named persistent storage volumes attached.
2. **Given** stored notes and audit history exist, **When** the user runs the backup command with a target output folder and passphrase, **Then** the system creates a dated, compressed, passphrase-encrypted backup archive in that folder and leaves no unencrypted temporary dump files behind.
3. **Given** a valid encrypted backup archive and an empty datastore, **When** the user runs the restore command with the matching passphrase, **Then** all notes and audit logs from the backup are restored completely.
4. **Given** stored data exists, **When** the user invokes the wipe command without explicit confirmation, **Then** the command refuses to run, exits with a non-zero status, and leaves all data untouched.
5. **Given** stored data exists, **When** the user invokes the wipe command with explicit confirmation, **Then** all note and audit data are removed cleanly.

---

### User Story 4 - Enforce Auditability, Archive-Only Deletion, and Clear Service Readiness Errors (Priority: P2)

As the sole user delegating note management to an AI agent, I want every write operation audited in an append-only log, permanent deletion forbidden to the agent in favor of reversible archiving, and fast actionable errors when data services are offline.

**Why this priority**: AI agents process untrusted external text and can make unintended edits; append-only audit trails and archive-only deletion bound the blast radius of mistakes, while fast readiness checks prevent hung agent sessions.

**Independent Test**: Can be fully tested by executing create, append, section-replace, import, and archive operations, verifying that each operation creates an immutable audit entry with before/after summaries, confirming archived notes are hidden from default search, and stopping the data services to verify an agent tool call immediately returns a helpful start-service instruction.

**Acceptance Scenarios**:

1. **Given** any write operation (create, append, section replace, archive, or import), **When** the operation completes, **Then** an append-only audit entry is recorded containing the actor identity, operation name, target note identifier, UTC timestamp, and before/after content summaries.
2. **Given** an active note, **When** the agent archives the note, **Then** the note is marked archived, remains retrievable when explicitly requested (or by ID), and is excluded from default search results.
3. **Given** the agent's available operations, **When** inspecting or invoking agent tools, **Then** no operation exists that permanently deletes a note or modifies/deletes existing audit log entries.
4. **Given** the local data services are stopped or unreachable, **When** the agent invokes any note operation, **Then** the operation fails within a short timeout (under 5 seconds) and returns a clear diagnostic message stating the data services are not running and providing the exact command to start them.

---

### Edge Cases

- **Very long note bodies (over 100 KB)**: The system MUST accept, store, retrieve, append to, and full-text search individual notes exceeding 100 KB (up to at least 1 MB per note) without silent truncation or indexing failure.
- **Duplicate titles**: Multiple notes MAY share the same title (for example, identical sub-headings across different imported files or areas). Each note is uniquely identified by its system ID; during import, uniqueness is keyed deterministically by source file identity and heading path so duplicate titles in different contexts do not collide.
- **Empty headings**: During markdown import, a heading that contains no body text before the next heading (or end of file) MUST be handled deterministically without failing the import—either preserved as a structural note with an empty body or attached as parent heading context—so that re-assembling imported notes preserves 100% of the source text verbatim; bare heading markers with no title text (e.g., `## `) MUST receive a deterministic placeholder title while retaining the exact raw text in the body.
- **Non-UTF-8 input**: If an input file passed to the import command contains invalid UTF-8 byte sequences, the command MUST fail before writing partial records from that file and report the file path and encoding error clearly.
- **Concurrent appends from two agent sessions**: When two agent sessions attempt to append to or update the same note concurrently, the system MUST prevent lost updates (either by serializing atomic appends or by enforcing optimistic concurrency checks that reject a stale write with a clear conflict message).
- **Wrong backup passphrase**: When restoring a backup with an incorrect passphrase, decryption/authentication MUST fail immediately with a clear error message before modifying any state in the target datastore.
- **Restore into a non-empty database**: When the user runs the restore command against a datastore that already contains notes or audit records, the command MUST refuse to overwrite existing data by default and instruct the user to either run the confirmation-guarded wipe command first or pass an explicit force/overwrite confirmation flag.
- **Target section missing or ambiguous during section replacement**: If the agent attempts to replace a heading section that does not exist or appears multiple times within the same note without disambiguation, the system MUST reject the edit without modifying the note and return a clear error listing the available headings.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST allow the AI agent to create a note containing a title, markdown body, zero or more tags, an area label (such as `criteria` or `runbook`), and a provenance marker consisting of a provenance kind (`stated`, `researched`, or `inferred`) and a date.
- **FR-002**: System MUST assign every created note a unique, time-ordered identifier, record creation and last-modified timestamps in UTC, and associate every record with an owner identifier.
- **FR-003**: System MUST allow the AI agent to retrieve a single note in full by its unique identifier, including archived notes.
- **FR-004**: System MUST allow the AI agent to append markdown text to an existing note without overwriting or truncating existing content.
- **FR-005**: System MUST allow the AI agent to replace the body of a specific markdown heading section within an existing note while leaving all other sections unchanged, and MUST reject the operation with a descriptive error if the target heading is not found or is ambiguous.
- **FR-006**: System MUST allow the AI agent to search notes by free-text query across titles and bodies, filterable by one or more tags and/or an area label, excluding archived notes by default while providing an option to include archived notes.
- **FR-007**: System MUST support storing, retrieving, updating, and searching note bodies over 100 KB (up to at least 1 MB per note) without truncation.
- **FR-008**: System MUST prevent lost updates when two agent sessions concurrently append to or modify the same note.
- **FR-009**: System MUST provide a command to import one or more UTF-8 markdown files by splitting them on headings into individual notes while preserving 100% of the original file text verbatim (including any preamble text before the first heading and empty heading blocks).
- **FR-010**: During markdown import, the system MUST parse inline provenance tags matching `[stated YYYY-MM-DD]`, `[researched YYYY-MM-DD]`, or `[inferred YYYY-MM-DD]` to populate the note's structured provenance metadata while leaving the inline tag intact in the note body.
- **FR-011**: The markdown import command MUST be idempotent: re-running the import on unchanged files MUST NOT create duplicate notes, and re-running on updated files MUST deterministically update changed sections based on source file and heading hierarchy.
- **FR-012**: The markdown import command MUST validate that all input files are valid UTF-8 prior to importing and MUST abort with a clear error message if any file contains invalid UTF-8 sequences.
- **FR-013**: System MUST provide a single documented command to start the local data services configured with named persistent storage volumes so data survives container restarts and host reboots.
- **FR-014**: System MUST provide a single documented command to create a dated, compressed, passphrase-encrypted backup archive of all stored data (notes and audit logs) in a user-specified destination directory, leaving no unencrypted intermediate artifacts on disk.
- **FR-015**: System MUST provide a single documented command to restore all notes and audit logs from an encrypted backup archive using the user's passphrase, failing immediately without modifying existing data if the passphrase is wrong or the archive is corrupted, and refusing to restore into a non-empty datastore unless explicit overwrite confirmation is provided.
- **FR-016**: System MUST provide a separate, explicitly named command to wipe all stored data that refuses to run unless the user supplies explicit confirmation.
- **FR-017**: System MUST record an append-only audit log entry for every write operation (note creation, append, section replacement, archive, and markdown import) capturing the actor identity, operation name, target note identifier, UTC timestamp, and before/after summaries.
- **FR-018**: System MUST NOT expose any agent tool for permanently deleting notes or modifying/deleting audit log records; the agent MAY only archive notes.
- **FR-019**: When the local data services are stopped or unreachable, any agent tool invocation or CLI command requiring data access MUST fail fast (within 5 seconds) with a clear error message stating that data services are offline and showing the exact command to start them.
- **FR-020**: All agent communication in this feature MUST operate exclusively over standard input/output without opening any network listener on the host machine, and all diagnostic logging MUST be routed away from standard output so protocol frames are never corrupted.
- **FR-021**: All automated tests, sample files, and documentation in the repository MUST use strictly fictional data and MUST NEVER contain real job-search notes, company opinions, or personal contact details.

### Key Entities *(include if feature involves data)*

- **Note**: A durable unit of job-search knowledge owned by the user. Attributes include: Unique Identifier, Owner Identifier, Title, Markdown Body, Tags (collection of normalized labels), Area Label (category such as `criteria`, `runbook`, `preferences`, `general`), Provenance (Provenance Kind: `stated`, `researched`, or `inferred`, plus Provenance Date), Import Source Key (optional deterministic key from source file and heading path for idempotent imports), Status (`active` or `archived`), Version/Concurrency Token, Created Timestamp (UTC), and Updated Timestamp (UTC).
- **AuditLogEntry**: An immutable, append-only record of a state-changing operation. Attributes include: Unique Identifier, Owner Identifier, Actor Identity (e.g., agent session or import command), Operation Name, Target Note Identifier, Occurred Timestamp (UTC), Before Summary (hash, byte length, and brief excerpt or metadata snapshot prior to change), and After Summary (hash, byte length, and brief excerpt or metadata snapshot after change).
- **BackupArchive**: A portable, dated, compressed, passphrase-encrypted snapshot file containing the complete set of Notes and AuditLogEntries at a point in time, along with format/version metadata for safe restoration.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Importing a two-file markdown memory sample of approximately 46 KB and 26 KB completes with 100% verbatim text preservation (zero characters lost or altered when comparing source text to imported note bodies) and 100% accuracy in extracting inline `[stated YYYY-MM-DD]` provenance markers.
- **SC-002**: Re-running the markdown import command against already-imported files produces zero duplicate notes (total note count before and after the second run is identical).
- **SC-003**: Searching across a dataset of 5,000 stored notes (by free text, tag, or area filter) returns results in under 200 milliseconds at the 95th percentile on the local machine.
- **SC-004**: Notes exceeding 100 KB in body size are stored, modified via append and section replacement, searched, and retrieved by identifier with 100% data integrity.
- **SC-005**: 100% of write operations performed by the agent or import command generate a corresponding immutable audit log entry containing actor, operation, target note ID, timestamp, and before/after summaries.
- **SC-006**: A complete disaster-recovery cycle (creating a passphrase-encrypted backup, executing a confirmed wipe to zero records, and restoring from the backup) restores 100% of notes and audit entries identically.
- **SC-007**: When data services are stopped, 100% of agent operations return an actionable start-service error message in under 5 seconds rather than hanging.
- **SC-008**: All four user stories and verification checks pass end-to-end from a clean repository clone using only the documented commands and fictional test fixtures.

## Assumptions

- **Single-Owner Local Operation**: Because authentication is out of scope for this feature, all records created by the local agent and CLI commands use a configured or default single-owner identifier (e.g., `owner_id` defaulted for the local owner) so that the data model satisfies multi-tenant/owner-scoped invariants from day one without requiring login flows.
- **Markdown Heading Splitting**: By default, the markdown import command splits files on top-level/section headings (such as `#` and `##` headings, or configurable heading depth) while keeping deeper sub-headings (`###` and below) inside the parent note body so that cohesive sections remain readable units and can later be targeted by section replacement. Any text preceding the first split heading (including YAML frontmatter or preamble paragraphs) is captured in a preamble note or attached to the file's root note so zero bytes are lost.
- **Inline Provenance Precedence**: When a markdown section being imported contains an explicit `[stated YYYY-MM-DD]`, `[researched YYYY-MM-DD]`, or `[inferred YYYY-MM-DD]` tag, that tag populates the note's provenance kind and date. If none appears in the section, the import command assigns a default provenance (such as `stated` or `inferred` with the file's date or import date, configurable via CLI flag).
- **Out of Scope Boundaries**: Structured tracking for companies, job postings, applications, and criteria tables, as well as authentication, web user interfaces, network listeners, and cloud hosting, are deferred to subsequent features.
