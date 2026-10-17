# Contract: MCP Stdio Tools (`RoleRadar.Mcp.Stdio`)

**Feature**: `001-memory-replacement`
**Transport**: `stdio` only (`ModelContextProtocol` `2.2.0`, `Microsoft.Extensions.Hosting` `10.0.13`, zero network listeners, all logs routed to `stderr`).
**Handler Injection**: Each tool injects its corresponding `ICommandHandler<TCommand, TResponse>` or `IQueryHandler<TQuery, TResponse>` directly (no mediator library, per Constitution v1.1.0 Principle IV and [ADR-0006](../../../docs/adr/0006-handlers-with-decorators-no-mediator.md)).
**Tool Cap**: 6 tools exposed (strictly within the <=8 tool cap in `FR-018` and Constitution Principle V).

---

## Protocol, Typed Result Mapping & Readiness Error Envelope

- Every tool invokes its injected handler and maps `Result<T>` into an MCP tool response. Expected failures (`NotFound`, `StaleVersionToken`, `SectionNotFound`, `AmbiguousHeading`, `ValidationError`, `DatastoreOffline`) are returned as structured MCP tool error payloads without throwing unhandled exceptions.
- If the MongoDB replica set is unreachable or not started, `CheckReadinessAsync` returns `ErrorCode.DatastoreOffline` within **5 seconds** (`FR-019`) with the exact start command:
  ```text
  RoleRadar data services are not running or unreachable at mongodb://127.0.0.1:27017/?replicaSet=rs0.
  Start them from the repository root with:
    pwsh ./scripts/start-services.ps1
  (or: docker compose -f deploy/docker-compose.yml up -d)
  ```
- No tool exists to hard-delete a note or alter/delete revisions or audit logs (`FR-018`).

---

## Tool 1: `note_create` → `ICommandHandler<CreateNoteCommand, NoteCreatedDto>`

- **Description**: `"Creates a durable markdown note with title (<=300 chars), body (<=8 MiB UTF-8), tags (<=32 tags, each <=64 chars), area label (<=64 chars), and provenance marker (stated, researched, or inferred with YYYY-MM-DD date)."`
- **Input Parameters**:
  - `title` (`string`, required): Non-empty title of the note (`1..300` characters).
  - `body` (`string`, required): Markdown body (`<= 8 MiB` / `8,388,608` UTF-8 bytes).
  - `area` (`string`, required): Area label (`1..64` characters, e.g., `"criteria"`, `"runbook"`, `"preferences"`, `"general"`).
  - `tags` (`string[]`, optional): List of tag labels (at most `32` tags, each `1..64` characters).
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

## Tool 2: `note_get` → `IQueryHandler<GetNoteQuery, NoteDetailDto>`

- **Description**: `"Reads a single note by its unique id, including archived notes, returning a character-paged slice of its markdown body (at most 200,000 characters per call by default), pagination metadata (totalChars, truncated, nextOffset), note metadata, and current version token."`
- **Input Parameters**:
  - `id` (`Guid`, required): Unique identifier of the note.
  - `offset` (`int`, optional, default `0`): Zero-based character offset into the note body (`>= 0`).
  - `maxChars` (`int`, optional, default `200000`): Maximum characters of `body` to return in this call (`1..200000`; default `200,000`).
- **Behavior**:
  - Returns at most `maxChars` (`200,000` by default) UTF-16 `char` units starting at `offset`.
  - **No split inside a UTF-16 surrogate pair**: If `offset` lands on a low surrogate preceded by a high surrogate, or if the end of the slice (`offset + take`) would split a high/low surrogate pair, the slice boundaries are adjusted so no page starts or ends inside a surrogate pair, and `nextOffset` points to the high surrogate of the unreturned pair.
  - **Offset at or beyond end**: If `offset >= totalChars`, returns an empty `body` (`""`), `totalChars`, `truncated: false`, and `nextOffset: null`.
- **Output Payload (JSON)**:
  - Note object including `id`, `title`, `body` (surrogate-safe paged slice), `totalChars`, `truncated` (`bool`), `nextOffset` (`int?`, `null` when not truncated), `area`, `tags`, `provenance`, `status`, `versionToken`, `bodyHash`, `revisionCount`, `createdAtUtc`, and `updatedAtUtc`.
  - Maps `ErrorCode.NotFound` to a structured not-found tool response and `ErrorCode.ValidationError` if `offset < 0` or `maxChars` is outside `1..200,000`.

---

## Tool 3: `note_append` → `ICommandHandler<AppendToNoteCommand, NoteUpdatedDto>`

- **Description**: `"Atomically appends markdown text (<=8 MiB UTF-8, resulting body <=8 MiB UTF-8) to the end of an existing note inside a snapshot transaction without requiring a version token."`
- **Input Parameters**:
  - `id` (`Guid`, required): Unique identifier of the note.
  - `textToAppend` (`string`, required): Markdown text to append to the existing body (`<= 8 MiB` UTF-8 bytes; resulting note body `<= 8 MiB` UTF-8 bytes).
- **Behavior**:
  - Executes atomically inside a snapshot transaction (`read`, `append`, `hash`, `update`) with transient-error retry (`FR-004`).
  - Rejects with `ErrorCode.ValidationError` if `textToAppend` or the resulting body exceeds `8 MiB` (`8,388,608` UTF-8 bytes).
  - Does **not** create a `NoteRevision` snapshot (`FR-008`).
  - Records `appended_text` and `resulting_version_token` in `audit_log` (`FR-017`).
- **Output Payload (JSON)**:
  - Updated note metadata (`id`, `versionToken`, `bodyHash`, `byteLength`, `updatedAtUtc`).

---

## Tool 4: `note_replace_section` → `ICommandHandler<ReplaceNoteSectionCommand, NoteSectionReplacedDto>`

- **Description**: `"Replaces the markdown content under a specific heading in an existing note (replacement and resulting body each <=8 MiB UTF-8). Requires the current versionToken and saves the full prior body in revision history."`
- **Input Parameters**:
  - `id` (`Guid`, required): Unique identifier of the note.
  - `heading` (`string`, required): Heading title or heading path to replace (e.g., `"## Compensation"` or `"Compensation"`).
  - `newSectionBody` (`string`, required): Replacement markdown content for that section (`<= 8 MiB` UTF-8 bytes; resulting note body `<= 8 MiB` UTF-8 bytes).
  - `versionToken` (`Guid`, required): Current concurrency `versionToken` of the note.
- **Behavior**:
  - Rejects with `ErrorCode.ValidationError` if `newSectionBody` or the resulting note body exceeds `8 MiB` (`8,388,608` UTF-8 bytes).
  - If `versionToken` does not match the stored note's `version_token`, returns typed `ErrorCode.StaleVersionToken` with the current `versionToken` (`FR-005`).
  - If `heading` is not found or matches multiple sections ambiguously, returns typed `ErrorCode.SectionNotFound` or `ErrorCode.AmbiguousHeading` listing the available headings without modifying the note (`FR-005`).
  - Saves the full prior note body in `note_revisions` and records `note_revision_id` in `audit_log`.
- **Output Payload (JSON)**:
  - Updated note metadata (`id`, `versionToken`, `revisionId`, `revisionCount`, `bodyHash`, `updatedAtUtc`).

---

## Tool 5: `note_search` → `IQueryHandler<SearchNotesQuery, NoteSearchResponseDto>`

- **Description**: `"Searches notes by case-insensitive text query (supporting stemmed whole words where multi-word queries rank notes by relevance rather than requiring every term, quoted phrases that match exactly, and no prefix matching), tag filter, and area label. Excludes archived notes by default."`
- **Input Parameters**:
  - `query` (`string`, optional): Free-text search query (multi-word queries rank notes by relevance rather than requiring every term; quoted phrases e.g. `"staff engineer"` match exactly; prefix matching is not supported).
  - `tags` (`string[]`, optional): Tag filters combined with logical `AND`.
  - `area` (`string`, optional): Area label filter combined with logical `AND`.
  - `includeArchived` (`bool`, optional, default `false`): Whether to include archived notes.
  - `limit` (`int`, optional, default `20`, max `100`): Maximum results to return.
- **Behavior**:
  - Orders results by relevance descending (when `query` is non-empty), then `updatedAtUtc` descending, then `id` ascending (`FR-006`).
- **Output Payload (JSON)**:
  - Array of matching summaries (`id`, `title`, `area`, `tags`, `provenance`, `status`, `versionToken`, `snippet`, `score`, `updatedAtUtc`).

---

## Tool 6: `note_archive` → `ICommandHandler<ArchiveNoteCommand, NoteArchivedDto>`

- **Description**: `"Archives a note so it is excluded from default searches while preserving its full text and revision history intact."`
- **Input Parameters**:
  - `id` (`Guid`, required): Unique identifier of the note to archive.
- **Behavior**:
  - Marks `status = "archived"` (idempotent if already archived), preserves `body` and `note_revisions`, and records an `audit_log` entry (`FR-017`, `FR-018`).
- **Output Payload (JSON)**:
  - Updated note metadata (`id`, `status`: `"archived"`, `versionToken`, `updatedAtUtc`).
