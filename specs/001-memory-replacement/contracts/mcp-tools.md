# Contract: MCP Stdio Tools (`RoleRadar.Mcp.Stdio`)

**Feature**: `001-memory-replacement`  
**Transport**: `stdio` only (`ModelContextProtocol` `2.2.0`, zero network listeners, all logs routed to `stderr`).  
**Tool Cap**: 6 tools exposed (strictly within the <=8 tool cap in `FR-018` and Constitution Principle V).

---

## Protocol & Readiness Error Envelope

- Every tool checks datastore readiness before executing. If the MongoDB replica set is unreachable or not started, the tool fails within **5 seconds** (`FR-019`) and returns a structured tool error message:
  ```text
  RoleRadar data services are not running or unreachable at mongodb://127.0.0.1:27017/?replicaSet=rs0.
  Start them from the repository root with:
    pwsh ./scripts/start-services.ps1
  (or: docker compose up -d)
  ```
- No tool exists to hard-delete a note or alter/delete revisions or audit logs (`FR-018`).

---

## Tool 1: `note_create`

- **Description**: `"Creates a durable markdown note with title, body, tags, area label, and provenance marker (stated, researched, or inferred with YYYY-MM-DD date)."`
- **Input Parameters**:
  - `title` (`string`, required): Non-empty title of the note.
  - `body` (`string`, required): Markdown body (supports >100 KB up to 1 MB).
  - `area` (`string`, required): Area label (e.g., `"criteria"`, `"runbook"`, `"preferences"`, `"general"`).
  - `tags` (`string[]`, optional): List of tag labels.
  - `provenanceKind` (`string`, required): One of `"stated"`, `"researched"`, or `"inferred"`.
  - `provenanceDate` (`string`, required): ISO date `"YYYY-MM-DD"`.
  - `provenanceQualifier` (`string`, optional): Optional provenance qualifier text.
- **Output Payload (JSON)**:
  ```json
  {
    "id": "0199f2c0-0000-7000-8000-000000000001",
    "title": "Target Role Criteria",
    "area": "criteria",
    "tags": ["remote", "dotnet"],
    "provenance": {
      "kind": "stated",
      "date": "2026-10-17",
      "qualifier": null
    },
    "status": "active",
    "versionToken": "0199f2c0-0000-7000-8000-000000000002",
    "bodyHash": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
    "createdAtUtc": "2026-10-17T13:30:00Z",
    "updatedAtUtc": "2026-10-17T13:30:00Z"
  }
  ```

---

## Tool 2: `note_get`

- **Description**: `"Reads a single note in full by its unique id, including archived notes, returning its markdown body, metadata, and current version token."`
- **Input Parameters**:
  - `id` (`Guid`, required): Unique identifier of the note.
- **Output Payload (JSON)**:
  - Full note object including `id`, `title`, `body`, `area`, `tags`, `provenance`, `status`, `versionToken`, `bodyHash`, `revisionCount`, `createdAtUtc`, and `updatedAtUtc`.
  - Returns a clear not-found error if `id` does not exist.

---

## Tool 3: `note_append`

- **Description**: `"Atomically appends markdown text to the end of an existing note on the server side without requiring a version token."`
- **Input Parameters**:
  - `id` (`Guid`, required): Unique identifier of the note.
  - `textToAppend` (`string`, required): Markdown text to append to the existing body.
- **Behavior**:
  - Executes atomically on the server side (`FR-004`).
  - Does **not** create a `NoteRevision` snapshot (`FR-008`).
  - Records `appended_text` and `resulting_version_token` in `audit_log` (`FR-017`).
- **Output Payload (JSON)**:
  - Updated note metadata (`id`, `versionToken`, `bodyHash`, `byteLength`, `updatedAtUtc`).

---

## Tool 4: `note_replace_section`

- **Description**: `"Replaces the markdown content under a specific heading in an existing note. Requires the current versionToken and saves the full prior body in revision history."`
- **Input Parameters**:
  - `id` (`Guid`, required): Unique identifier of the note.
  - `heading` (`string`, required): Heading title or heading path to replace (e.g., `"## Compensation"` or `"Compensation"`).
  - `newSectionBody` (`string`, required): Replacement markdown content for that section.
  - `versionToken` (`Guid`, required): Current concurrency `versionToken` of the note.
- **Behavior**:
  - If `versionToken` does not match the stored note's `version_token`, rejects the edit with a retryable conflict error containing the current `versionToken` (`FR-005`).
  - If `heading` is not found or matches multiple sections ambiguously, rejects the edit without modifying the note and lists the available headings in the error message (`FR-005`).
  - Saves the full prior note body in `note_revisions` and records `note_revision_id` in `audit_log`.
- **Output Payload (JSON)**:
  - Updated note metadata (`id`, `versionToken`, `revisionId`, `revisionCount`, `bodyHash`, `updatedAtUtc`).

---

## Tool 5: `note_search`

- **Description**: `"Searches notes by case-insensitive text query (supporting stemmed whole words and quoted phrases), tag filter, and area label. Excludes archived notes by default."`
- **Input Parameters**:
  - `query` (`string`, optional): Free-text search query (supports quoted phrases e.g. `"staff engineer"`).
  - `tags` (`string[]`, optional): Tag filters combined with logical `AND`.
  - `area` (`string`, optional): Area label filter combined with logical `AND`.
  - `includeArchived` (`bool`, optional, default `false`): Whether to include archived notes.
  - `limit` (`int`, optional, default `20`, max `100`): Maximum results to return.
- **Behavior**:
  - Orders results by relevance descending (when `query` is non-empty), then `updatedAtUtc` descending, then `id` ascending (`FR-006`).
- **Output Payload (JSON)**:
  - Array of matching summaries (`id`, `title`, `area`, `tags`, `provenance`, `status`, `versionToken`, `snippet`, `score`, `updatedAtUtc`).

---

## Tool 6: `note_archive`

- **Description**: `"Archives a note so it is excluded from default searches while preserving its full text and revision history intact."`
- **Input Parameters**:
  - `id` (`Guid`, required): Unique identifier of the note to archive.
- **Behavior**:
  - Marks `status = "archived"` (idempotent if already archived), preserves `body` and `note_revisions`, and records an `audit_log` entry (`FR-017`, `FR-018`).
- **Output Payload (JSON)**:
  - Updated note metadata (`id`, `status`: `"archived"`, `versionToken`, `updatedAtUtc`).
