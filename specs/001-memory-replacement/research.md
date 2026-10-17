# Phase 0 Research: Feature 001 — Memory Replacement

**Feature**: `001-memory-replacement`  
**Date**: 2026-10-17  
**Spec**: [spec.md](./spec.md)

## 1. Datastore Selection & Multi-Document Atomicity for Feature 001 (ADR-0001)

- **Decision**: Use MongoDB (`mongo:8.0.4` pinned container image) configured as a single-node replica set (`rs0`) as the sole datastore for Feature 001 (`notes`, `note_revisions`, and `audit_log` collections), deferring PostgreSQL until Feature 002 (structured companies, postings, and applications).
- **Rationale**:
  - Feature 001 deals exclusively with narrative markdown notes, their destructive-edit revisions, and their write audit log.
  - Every write operation (`create`, `append`, `replace-section`, `archive`, and `import`) must atomically persist the note mutation, the prior-body revision (when applicable), and the `audit_log` entry so a crash or fault can never leave an unaudited write or a revision out of sync with the note.
  - Keeping all three collections inside a single MongoDB replica set allows standard multi-document ACID transactions (`IClientSessionHandle.WithTransactionAsync`) across `notes`, `note_revisions`, and `audit_log` without distributed two-phase commit (`2PC`) or cross-database outbox complexity.
- **Alternatives Considered**:
  - *Split Notes in MongoDB and Audit Log in PostgreSQL in Feature 001*: Rejected because coordinating atomic writes across MongoDB and PostgreSQL would require a distributed transaction or outbox processor before any relational entities exist.
  - *Standalone MongoDB instance (no replica set)*: Rejected because MongoDB requires a replica set topology (`--replSet rs0`) to support multi-document transactions.

---

## 2. Single Write Port: `INoteStore` (ADR-0002)

- **Decision**: Define a single storage port interface, `INoteStore`, in `RoleRadar.Application` that encapsulates all note reads, searches, and transactional write units (updating `notes`, appending `note_revisions` when applicable, and appending `audit_log` entries in one atomic call). Introduce no other storage port or repository interfaces in Feature 001.
- **Rationale**:
  - Directly satisfies Constitution Principle IV (*Named Seams Only*), which explicitly authorizes `INoteStore` for narrative storage isolation and forbids generic repositories or unapproved seams.
  - Modelling each write command on `INoteStore` as a complete unit of work (accepting the note mutation intent plus audit metadata and executing inside a single storage transaction) makes it impossible for application code to mutate a note without recording its audit entry.
- **Alternatives Considered**:
  - *Separate `INoteRepository`, `INoteRevisionRepository`, and `IAuditLogRepository` plus `IUnitOfWork`*: Rejected because it adds three extra abstractions not named in Principle IV and shifts transaction session types into the `Application` layer.

---

## 3. MCP Transport & SDK Selection (ADR-0003 & ADR-0004)

- **Decision**: Use the official `ModelContextProtocol` NuGet package pinned at `2.2.0` (verified in `spikes/StdioEcho/StdioEcho.csproj`) with `Microsoft.Extensions.Hosting` `10.0.0`, running exclusively over `stdio` transport (`WithStdioServerTransport()`) and exposing at most 8 tools.
- **Rationale**:
  - `ModelContextProtocol` `2.2.0` was validated in the `spikes/StdioEcho` spike on .NET 10 using:
    - `builder.Logging.ClearProviders();`
    - `builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);`
    - `builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<...>();`
    - `[McpServerToolType]` and `[McpServerTool(Name = "..."), Description("...")]`
  - Satisfies Constitution Principle VI (*stdio Protocol Hygiene*): `stdout` carries exclusively JSON-RPC protocol frames, all logs route to `stderr`, and zero network sockets/listeners are opened.
  - Capping the agent surface at 6 focused tools (well under the 8-tool ceiling: `note_create`, `note_get`, `note_append`, `note_replace_section`, `note_search`, `note_archive`) keeps context token overhead minimal and omits any permanent delete capability (Principle V).
- **Alternatives Considered**:
  - *HTTP/SSE MCP listener on localhost*: Rejected for Feature 001 because Principle VI and the spec forbid network listeners in Phase 1; authentication (Duende IdentityServer) arrives in Feature 004.
  - *Custom JSON-RPC stdin/stdout parser*: Rejected because the official `ModelContextProtocol` `2.2.0` SDK handles framing, tool schema generation, and cancellation cleanly.

---

## 4. Revision History & Concurrency Policy (ADR-0005)

- **Decision**:
  - Record a full prior-body `NoteRevision` document **only** on text-destroying writes (`replace-section` and `import-overwrite`).
  - Execute `append` as an atomic server-side operation (no caller version token required) that records only the appended text and resulting version token in `audit_log` without creating a `NoteRevision` snapshot.
  - Require the caller's `VersionToken` on `replace-section` and reject stale tokens with a retryable conflict error inside the transaction.
- **Rationale**:
  - Because `append` only concatenates to the end of the existing body, the prior body is strictly a prefix of the new body. Storing full-body snapshots on every append to a 100 KB+ note would cause $O(N^2)$ storage growth across $N$ appends.
  - By contrast, `replace-section` and `import-overwrite` overwrite existing text, so preserving the full prior body in `note_revisions` (referenced by `revision_id` in `audit_log`) guarantees zero text loss.
- **Alternatives Considered**:
  - *Full-body revision snapshot on every write including `append`*: Rejected due to quadratic storage amplification on frequent appends to large notes.
  - *Line-diff patches for all revisions*: Rejected because storing full prior bodies on infrequent destructive edits is simpler, faster to inspect/restore, and immune to diff reconstruction bugs.

---

## 5. Search Indexing & Query Semantics

- **Decision**:
  - Create a MongoDB compound text index on `title` (weight 10) and `body` (weight 1) with `default_language: "english"` (providing case-insensitive, whole-word matching with basic Porter stemming and quoted exact-phrase support via `$text: { $search: query, $caseSensitive: false, $diacriticSensitive: false }`), combined with equality/set filters on `owner_id`, `status`, `area`, and `tags` (`$all` for ANDed tag filtering).
  - Sort results by `{ score: { $meta: "textScore" }, updated_at: -1, _id: 1 }` when a text query is present, or `{ updated_at: -1, _id: 1 }` when filtering by tags/area only.
- **Rationale**:
  - MongoDB's built-in English text index natively supports case-insensitive whole-word stemming and double-quoted exact phrases (`"..."`), and executes in single-digit milliseconds over 5,000 notes—well below the 200 ms p95 requirement (`SC-003`).
- **Alternatives Considered**:
  - *In-memory regex scanning*: Rejected because regex scans do not provide stemming or relevance scoring and degrade on 5,000+ large notes.
  - *External search engine (Elasticsearch / Meilisearch)*: Rejected as unnecessary operational complexity for 5,000 notes.

---

## 6. Byte-Exact Markdown Import & Provenance Grammar

- **Decision**:
  - Validate input file bytes using `new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)` before any database write.
  - Parse markdown files by scanning lines while preserving exact raw slices (including UTF-8 BOM at byte 0, original `CRLF` or `LF` line sequences, and trailing newline) and ignoring `#` lines inside fenced code blocks (`` ``` `` or `~~~`).
  - Split on `#` (level 1) and `##` (level 2) headings (`###` and deeper remain inside the parent section body). Any bytes before the first `#`/`##` heading (such as UTF-8 BOM, frontmatter, or preamble text) are stored in section order index `0` with heading path `""` (or `$preamble`) so concatenating all sections for a file ordered by `section_order_index` reproduces the source file byte-for-byte.
  - Compute `import_key = "{normalized_source_file_id}#{heading_path}#{sibling_occurrence_index}"` and `body_hash = SHA-256(raw_section_bytes)`.
  - Parse inline provenance tags matching `\[(stated|researched|inferred)(?:\s+(\d{4}-\d{2}-\d{2})(?:\s+([^\]]+?))?)?\]` anywhere in the section (including the heading). Use the first match in document order; if undated (`[kind]`), use `--default-date` if supplied on the CLI or else the UTC date of `File.GetLastWriteTimeUtc(path)`. Record the resolved default date in the import `audit_log` entry.
- **Rationale**:
  - Guarantees 100% byte-for-byte round-trip fidelity (`SC-001`), deterministic idempotence (`SC-002`), conflict preservation when `current_body_hash != last_imported_body_hash`, and orphan reporting when previous `import_key` values are absent from the source file.
- **Alternatives Considered**:
  - *AST re-serialization via Markdig*: Rejected because AST round-tripping normalizes whitespace, list markers, and line endings, violating byte-exact preservation.

---

## 7. Streamed Encrypted Backups & Safe Lifecycle Scripts

- **Decision**:
  - Stream `docker exec roleradar-mongo mongodump --archive --gzip --db roleradar` directly over process `stdout` into an in-process authenticated AES-256-GCM (PBKDF2-SHA256, 600,000 iterations, random 16-byte salt + 12-byte nonce, chunked AEAD stream) encryption pipeline in `RoleRadar.Cli backup` so no plaintext dump file ever touches the filesystem or `%TEMP%`.
  - Read the passphrase exclusively from `--passphrase-env <VAR>`, `--passphrase-stdin`, or an interactive masked console prompt; reject any `--passphrase` value argument on the command line.
  - Provide PowerShell operational wrapper scripts under `scripts/` (`start-services.ps1`, `backup.ps1`, `restore.ps1`, `wipe-data.ps1`) that invoke `docker compose` and `RoleRadar.Cli`.
- **Rationale**:
  - Streaming directly from `mongodump --archive` stdout into .NET's `Rfc2898DeriveBytes` + `AesGcm` chunked stream requires zero external encryption binaries (`age`/`7z`) to be pre-installed on Windows while guaranteeing authenticated encryption and zero temporary plaintext files (`SC-006`).
- **Alternatives Considered**:
  - *Dumping to a temp file then encrypting*: Strictly forbidden by `FR-014` and `SC-006`.
  - *Requiring external `age.exe` binary on PATH*: Rejected as the primary mechanism because a built-in .NET 10 `AesGcm` streaming format works out of the box from a clean clone without extra binary dependencies.

---

## 8. Pinned Dependencies & Quality Gates

- **Decision**: Use Central Package Management (`Directory.Packages.props`) and strict build settings (`Directory.Build.props` with `TargetFramework=net10.0`, `Nullable=enable`, `TreatWarningsAsErrors=true`, `AnalysisLevel=latest-recommended`) with the following pinned versions:
  - `ModelContextProtocol`: `2.2.0`
  - `Microsoft.Extensions.Hosting`: `10.0.0`
  - `MongoDB.Driver`: `3.1.0`
  - `System.CommandLine`: `2.0.0-beta4.22272.1`
  - `xunit`: `2.9.3`
  - `xunit.runner.visualstudio`: `3.0.2`
  - `Microsoft.NET.Test.Sdk`: `17.13.0`
  - `Testcontainers.MongoDb`: `4.3.0`
  - `NetArchTest.Rules`: `1.3.2`
  - `coverlet.collector`: `6.0.4`
  - Docker image: `mongo:8.0.4`
  - Minimum coverage gate: **85% line coverage** across `RoleRadar.Domain` and `RoleRadar.Application` enforced in CI.
- **Rationale**: Satisfies Constitution Principles II, III, VIII, and IX.
