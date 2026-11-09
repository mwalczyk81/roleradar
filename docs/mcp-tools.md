# MCP Tool Surface

How to register the server with a client is in [mcp-clients.md](mcp-clients.md).

RoleRadar exposes exactly 12 high-level memory tools across notes and job search tracking with **zero hard-delete tools**:

| Tool Name | Purpose |
| :--- | :--- |
| `note_create` | Creates a durable markdown note with title, body, area label, tags, and provenance marker. |
| `note_get` | Reads a single note by its unique id, including archived notes, returning character-paged body, pagination metadata, and version token. |
| `note_append` | Atomically appends markdown text to the end of an existing note inside a snapshot transaction without requiring a version token. |
| `note_replace_section` | Replaces the markdown content under a specific heading in an existing note under optimistic concurrency control, preserving prior body in revision history. |
| `note_search` | Searches notes by plain text query across title, body, and tags, with optional filtering by area, tags, provenance, and archived status. |
| `note_archive` | Archives a note under optimistic concurrency control, hiding it from default search while preserving all content and revision history. |
| `company_record` | Creates a new company target with initial research status, or updates status and research metadata of an existing company. |
| `posting_record` | Records a newly discovered job posting, deduping against company+title, canonical URL, and requisition ID. |
| `posting_transition` | Evaluates and applies state transitions to an active posting under optimistic concurrency control, supporting screening out, closing, or advancing. |
| `tracker_query` | Queries the job search pipeline across postings, applications, and companies with multi-field filtering, state filtering, and cursor pagination. |
| `tracker_log` | Records operational screening criteria revisions and search run execution batches linked to report notes. |
| `application_record` | Creates or updates a job application, appends timeline events, records resume variant pointers and desired salary, and supports atomic reopen-and-apply override for decided postings. |
