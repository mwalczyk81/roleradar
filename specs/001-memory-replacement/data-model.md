# Data Model: Feature 001 — Memory Replacement (Constitution v1.1.0)

**Feature**: `001-memory-replacement`  
**Date**: 2026-10-17  
**Datastore**: MongoDB 8.0 (`mongo:8.0.34`), single-node replica set (`rs0`), `GuidRepresentation.Standard` configured globally once at process startup (`MongoDB.Driver` `3.12.0`).

---

## Global Invariants

1. **UUIDv7 Identifiers**: All primary keys (`_id`) and concurrency version tokens are generated with `Guid.CreateVersion7()`.
2. **Standard GUID Serialization**: `BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard))` is registered once at startup before any MongoDB operation.
3. **UTC Timestamps**: All timestamps (`created_at`, `updated_at`, `occurred_at`) are generated, persisted, and serialized in UTC (`DateTimeOffset` / UTC `DateTime`). Provenance dates (`provenance.date`, `resolved_default_date`) are stored as ISO-8601 UTC date strings (`YYYY-MM-DD` / `DateOnly`).
4. **Owner Scoping**: Every document in every collection (`notes`, `note_revisions`, `audit_log`) carries a non-empty `owner_id` field from creation (`FR-002`, Constitution Principle IX). In Feature 001 (single-owner local mode), `owner_id` defaults to `"local-owner"` (configurable via `ROLERADAR_OWNER_ID`).
5. **Append-Only Enforcement**: Application and Infrastructure layers expose **no** update or delete methods against `note_revisions` or `audit_log`, and **no** hard-delete method against `notes` (outside the confirmation-guarded CLI `wipe` command).
6. **Typed Failure Results (`Result<T>`)**: Expected domain and concurrency failures (`NotFound`, `StaleVersionToken`, `SectionNotFound`, `AmbiguousHeading`, `ImportConflict`, `InvalidUtf8`, `DatastoreOffline`) are modeled as value-type `Result<T>` / `ResultError` outcomes returned from `INoteRepository` and command/query handlers, never thrown as control-flow exceptions (Constitution Principle IV).

---

## 1. Collection: `notes`

Represents the aggregate root of each durable job-search note (`RoleRadar.Domain.Notes.Note`).

### Document Schema

| Field | BSON / C# Type | Nullability | Description |
|-------|----------------|-------------|-------------|
| `_id` | `BinData(4, UUID)` / `Guid` | Required | Primary identifier generated via `Guid.CreateVersion7()`. |
| `owner_id` | `string` | Required | Owner identifier (default `"local-owner"`). |
| `title` | `string` | Required | Note title (non-empty; bare headings during import receive deterministic placeholder e.g. `"(Untitled Heading #1)"`). |
| `body` | `string` | Required | Exact markdown body text (preserving original line endings `CRLF`/`LF`, UTF-8 BOM on preamble, and trailing newline; up to >=1 MB). |
| `current_body_hash` | `string` | Required | Lowercase hex SHA-256 of the UTF-8 bytes of `body`. |
| `tags` | `string[]` | Required | Normalized (trimmed, lowercase, deduplicated) tags. Empty array `[]` when untagged. |
| `area` | `string` | Required | Normalized area label (e.g., `"criteria"`, `"runbook"`, `"preferences"`, `"general"`). |
| `provenance` | `ProvenanceMarker` | Required | Embedded document: `{ kind: "stated" \| "researched" \| "inferred", date: "YYYY-MM-DD", qualifier: string? }`. |
| `import_metadata` | `NoteImportMetadata?` | Nullable | Populated for notes created/managed by markdown import; `null` for agent-created notes. |
| `status` | `string` (`NoteStatus`) | Required | `"active"` or `"archived"`. |
| `version_token` | `BinData(4, UUID)` / `Guid` | Required | Concurrency token (`Guid.CreateVersion7()`), rotated on every write. |
| `revision_count` | `int32` / `int` | Required | Count of destructive-edit revisions in `note_revisions` for this note (`0` initially; incremented only on `ReplaceNoteSection` and `ImportMarkdown` overwrite, never on `AppendToNote`). |
| `created_at` | `DateTime` (UTC) | Required | Creation UTC timestamp. |
| `updated_at` | `DateTime` (UTC) | Required | Last modification UTC timestamp. |

### Embedded Value Object: `NoteImportMetadata` (`import_metadata`)

| Field | BSON / C# Type | Nullability | Description |
|-------|----------------|-------------|-------------|
| `import_key` | `string` | Required | Deterministic composite key: `"{source_file_id}#{heading_path}#{occurrence_index}"`. |
| `source_file_id` | `string` | Required | Normalized source file identifier (relative path or file name). |
| `heading_path` | `string` | Required | Hierarchical heading path (e.g., `"$preamble"` or `"# H1 Title / ## H2 Section"`). |
| `occurrence_index` | `int32` / `int` | Required | Zero-based occurrence index among identical sibling headings under the same parent. |
| `section_order_index` | `int32` / `int` | Required | Zero-based sequential order of this section in the source file for byte-exact reassembly. |
| `last_imported_body_hash` | `string` | Required | Lowercase hex SHA-256 of the UTF-8 bytes of `body` at the time of the last import write. Compared against `current_body_hash` (excluding `provenance.date`) to detect post-import edits. |

### Indexes on `notes`

1. **Unique Import Key Index**:
   - Keys: `{ owner_id: 1, "import_metadata.import_key": 1 }`
   - Options: `unique: true`, `partialFilterExpression: { "import_metadata.import_key": { $type: "string" } }`, `name: "ux_notes_owner_import_key"`
2. **Full-Text Search & Filter Index**:
   - Keys: `{ title: "text", body: "text", owner_id: 1, status: 1, area: 1 }`
   - Options: `weights: { title: 10, body: 1 }`, `default_language: "english"`, `name: "ix_notes_fulltext"`
3. **Filter & Default Sort Index**:
   - Keys: `{ owner_id: 1, status: 1, area: 1, tags: 1, updated_at: -1, _id: 1 }`
   - Options: `name: "ix_notes_owner_status_area_tags_updated"`
4. **Source File Reassembly Index**:
   - Keys: `{ owner_id: 1, "import_metadata.source_file_id": 1, "import_metadata.section_order_index": 1 }`
   - Options: `name: "ix_notes_import_reassembly"`

---

## 2. Collection: `note_revisions` (Append-Only)

Part of the `Note` aggregate persisted via `INoteRepository`. Stores full prior-body snapshots exclusively when a text-destroying write (`ReplaceNoteSection` or `ImportMarkdown` overwrite) occurs (`RoleRadar.Domain.Notes.NoteRevision`). Never written on `AppendToNote`, `CreateNote`, or `ArchiveNote`.

### Document Schema

| Field | BSON / C# Type | Nullability | Description |
|-------|----------------|-------------|-------------|
| `_id` | `BinData(4, UUID)` / `Guid` | Required | Revision identifier generated via `Guid.CreateVersion7()`. |
| `owner_id` | `string` | Required | Owner identifier matching the parent note. |
| `note_id` | `BinData(4, UUID)` / `Guid` | Required | Foreign reference to `notes._id`. |
| `revision_number` | `int32` / `int` | Required | 1-based monotonic revision sequence number for `note_id`. |
| `reason` | `string` | Required | `"replace-section"` or `"import-overwrite"`. |
| `prior_body` | `string` | Required | Full exact markdown body of the note immediately prior to the destructive write. |
| `prior_body_hash` | `string` | Required | Lowercase hex SHA-256 of the UTF-8 bytes of `prior_body`. |
| `prior_version_token` | `BinData(4, UUID)` / `Guid` | Required | The note's `version_token` immediately prior to the write. |
| `created_at` | `DateTime` (UTC) | Required | UTC timestamp when this revision was recorded. |

### Indexes on `note_revisions`

1. **Unique Note Revision Sequence Index**:
   - Keys: `{ owner_id: 1, note_id: 1, revision_number: 1 }`
   - Options: `unique: true`, `name: "ux_note_revisions_note_revnum"`

---

## 3. Collection: `audit_log` (Append-Only)

Records every write operation (`note_create`, `note_append`, `note_replace_section`, `note_archive`, `import_create`, `import_overwrite`) inside the same multi-document transaction as the note mutation (`RoleRadar.Domain.Audit.AuditLogEntry`), coordinated by `TransactionAndAuditCommandDecorator` and `INoteRepository`.

### Document Schema

| Field | BSON / C# Type | Nullability | Description |
|-------|----------------|-------------|-------------|
| `_id` | `BinData(4, UUID)` / `Guid` | Required | Audit entry identifier generated via `Guid.CreateVersion7()`. |
| `owner_id` | `string` | Required | Owner identifier. |
| `actor` | `string` | Required | Actor identity (e.g., `"mcp-stdio"`, `"cli-import"`). |
| `operation` | `string` | Required | Operation name: `"note_create"`, `"note_append"`, `"note_replace_section"`, `"note_archive"`, `"import_create"`, or `"import_overwrite"`. |
| `target_note_id` | `BinData(4, UUID)` / `Guid` | Required | Target `notes._id`. |
| `note_revision_id` | `BinData(4, UUID)` / `Guid?` | Nullable | Populated **only** on `note_replace_section` and `import_overwrite` (references `note_revisions._id`); `null` on `note_create`, `note_append`, `note_archive`, and `import_create`. |
| `appended_text` | `string?` | Nullable | Populated **only** on `note_append` (stores the exact text appended); `null` for all other operations. |
| `resulting_version_token` | `BinData(4, UUID)` / `Guid` | Required | The note's new `version_token` after the write completes. |
| `resolved_default_date` | `string?` (`YYYY-MM-DD`) | Nullable | Populated on `import_create` and `import_overwrite` with the effective default provenance date (from `--default-date` or UTC file last-modified date); `null` otherwise. |
| `occurred_at` | `DateTime` (UTC) | Required | UTC timestamp of the write transaction. |
| `before_summary` | `AuditContentSummary?` | Nullable | `null` on `note_create` / `import_create`; otherwise `{ body_hash: string, byte_length: int, status: string, excerpt: string }`. |
| `after_summary` | `AuditContentSummary` | Required | `{ body_hash: string, byte_length: int, status: string, excerpt: string }` (first <=200 chars). |

### Indexes on `audit_log`

1. **Note Audit Trail Index**:
   - Keys: `{ owner_id: 1, target_note_id: 1, occurred_at: 1, _id: 1 }`
   - Options: `name: "ix_audit_log_target_note"`
2. **Chronological Audit Index**:
   - Keys: `{ owner_id: 1, occurred_at: -1, _id: 1 }`
   - Options: `name: "ix_audit_log_chronological"`
