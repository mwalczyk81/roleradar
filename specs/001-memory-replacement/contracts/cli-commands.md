# Contract: CLI Commands & PowerShell Operational Scripts (`RoleRadar.Cli`)

**Feature**: `001-memory-replacement`  
**Host**: `RoleRadar.Cli` (`System.CommandLine` `2.0.13` GA) + Windows PowerShell wrapper scripts under `scripts/`.

---

## 1. `roleradar import` → `ICommandHandler<ImportMarkdownCommand, ImportMarkdownSummaryDto>`

Imports one or more UTF-8 markdown files into `notes` by splitting on `#` and `##` headings while preserving 100% of original bytes verbatim.

### Synopsis

```powershell
dotnet run --project src/RoleRadar.Cli -- import <files...> [--area <label>] [--tags <t1,t2>] [--default-date <YYYY-MM-DD>] [--json]
```

### Options & Arguments (`System.CommandLine` `2.0.13`)

- `<files...>` (`Argument<string[]>` with `Arity = ArgumentArity.OneOrMore`): Paths to UTF-8 markdown files to import.
- `--area` (`Option<string>`): Area label assigned to imported notes (default `"imported"`).
- `--tags` (`Option<string[]>`): Tags applied to imported notes.
- `--default-date` (`Option<string?>`, format `YYYY-MM-DD`): Explicit default date for undated `[kind]` provenance tags or sections without inline provenance tags. When omitted, defaults to the UTC date of each source file's `LastWriteTimeUtc`. Recorded in `audit_log.resolved_default_date`.
- `--json` (`Option<bool>`): Emit structured JSON summary output.

### Behavioral Contract (`FR-009`–`FR-012`)

1. **Pre-Flight UTF-8 Validation**: Reads all input files with strict UTF-8 decoding (`throwOnInvalidBytes: true`) before writing any record. If any file contains invalid UTF-8 bytes, returns `ResultError(ErrorCode.InvalidUtf8)` and exits with code `2` identifying the offending file path.
2. **Heading Splitting & Byte-Exact Preservation**:
   - Splits on `#` and `##` headings outside fenced code blocks; `###` and deeper stay within the parent section.
   - Any preamble bytes before the first `#`/`##` heading (including UTF-8 BOM at byte 0) are stored at `section_order_index = 0` (`heading_path = "$preamble"`).
   - Preserves exact bytes (`CRLF`/`LF` line endings, BOM, trailing newline, empty headings). Concatenating all imported sections for a source file ordered by `section_order_index` reproduces the file byte-for-byte.
3. **Provenance Extraction**:
   - Matches `\[(stated|researched|inferred)(?:\s+(\d{4}-\d{2}-\d{2})(?:\s+([^\]]+?))?)?\]` anywhere in the section (including its heading).
   - Populates `Note.provenance` from the **first** tag in document order; undated tags use `--default-date` (or UTC file last-modified date). All tags remain verbatim in `body`.
4. **Idempotent Reconciliation (Body-Hash-Only Comparison)**:
   - Key: `"{source_file_id}#{heading_path}#{occurrence_index}"`.
   - **Unchanged** (`source_body_hash == note.last_imported_body_hash` and `note.current_body_hash == note.last_imported_body_hash`): Skips write; increments `unchangedCount`.
   - **Updated in Source, Unmodified in Store** (`source_body_hash != note.last_imported_body_hash` and `note.current_body_hash == note.last_imported_body_hash`): Saves full prior body in `note_revisions` (`"import-overwrite"`), updates note in place, records `audit_log` entry; increments `updatedCount`.
   - **Conflict — Edited Since Last Import** (`note.current_body_hash != note.last_imported_body_hash`): Returns `ErrorCode.ImportConflict` for that section without overwriting the note; skips and records in `conflicts[]` (`importKey`, `noteId`, `title`, `reason`).
   - **Orphaned — Missing or Renamed in Source**: Never deletes or modifies existing notes for `source_file_id` whose `import_key` is absent from the current file; records in `orphans[]` (`importKey`, `noteId`, `title`).

### Output Summary Schema (`--json`)

```json
{
  "filesProcessed": 2,
  "createdCount": 18,
  "updatedCount": 0,
  "unchangedCount": 0,
  "conflictCount": 0,
  "orphanCount": 0,
  "conflicts": [],
  "orphans": []
}
```

---

## 2. `roleradar backup` (and `scripts/backup.ps1`)

Creates a dated, compressed, passphrase-encrypted backup archive by streaming `mongodump --archive --gzip` output directly into libsodium `crypto_secretstream_xchacha20poly1305` (`Geralt.IncrementalXChaCha20Poly1305`) with `Geralt.Argon2id` key derivation (`FR-014`, [ADR-0007](../../../docs/adr/0007-backup-streaming-aead-encryption.md)).

### Synopsis

```powershell
pwsh ./scripts/backup.ps1 -OutputDirectory <path> [-PassphraseEnv <VAR_NAME>] [-PassphraseStdin]
# or directly:
dotnet run --project src/RoleRadar.Cli -- backup --output-dir <path> [--passphrase-env <VAR_NAME>] [--passphrase-stdin]
```

### Behavioral Contract

1. **Passphrase Input Security**: Accepts passphrase **only** via:
   - `--passphrase-env <VAR_NAME>` (reads from the named environment variable),
   - `--passphrase-stdin` (reads a single line from `stdin`), or
   - Interactive masked prompt when running in an interactive terminal.
   - Any attempt to pass `--passphrase` or `-p` with a literal value on the command line is **rejected immediately with exit code `2`**.
2. **Zero Plaintext Temp Files & Streaming AEAD**: Streams `mongodump --archive --gzip` stdout in 64 KiB chunks directly through `Geralt.Argon2id` + `Geralt.IncrementalXChaCha20Poly1305` (with header authenticated as associated data and `ChunkFlag.Final` on the terminal chunk) into `roleradar-backup-YYYYMMDD-HHMMSSZ.rrbak` in `--output-dir`. Writes zero plaintext files to `--output-dir` or `%TEMP%`.

---

## 3. `roleradar restore` (and `scripts/restore.ps1`)

Restores notes, revisions, and audit logs from an encrypted `.rrbak` archive using a **two-pass** verification and restore flow (`FR-015`, [ADR-0007](../../../docs/adr/0007-backup-streaming-aead-encryption.md)).

### Synopsis

```powershell
pwsh ./scripts/restore.ps1 -ArchivePath <file.rrbak> [-PassphraseEnv <VAR_NAME>] [-PassphraseStdin] [-ForceOverwrite]
# or directly:
dotnet run --project src/RoleRadar.Cli -- restore --archive <file.rrbak> [--passphrase-env <VAR_NAME>] [--passphrase-stdin] [--force-overwrite]
```

### Behavioral Contract

1. **Non-Empty Datastore Guard**: Before decrypting into `mongorestore`, checks whether `notes`, `note_revisions`, or `audit_log` contain any documents. If non-empty and `--force-overwrite` (`-ForceOverwrite`) is not supplied, refuses to run with exit code `3` and instructs the user to run `pwsh ./scripts/wipe-data.ps1` first or pass `--force-overwrite`.
2. **Two-Pass Restore Guarantee & Pre-KDF Header Bounds**:
   - **Pre-KDF & Pre-Allocation Bounds Check**: Before invoking `Argon2id.DeriveKey` or allocating any chunk buffer, validates `Magic == "RRBK"`, `FormatVersion == 1`, `Argon2Iterations` in `[3, 10]`, `Argon2MemoryKiB` in `[65536, 1048576]` (`<= 1 GiB`), and each chunk's `CiphertextLength` in `[17, 1048576]` (`<= 1 MiB`), rejecting out-of-range headers or chunk prefixes immediately with a typed error (exit code `4`) without allocating attacker-chosen memory.
   - **Pass 1 (Verify-Only)**: Decrypts and authenticates the entire `.rrbak` archive stream (header associated data, every chunk's Poly1305 tag, chunk order, and terminal `ChunkFlag.Final` tag) with output discarded. If the passphrase is wrong or the archive is tampered with, truncated, or has dropped/reordered chunks, Pass 1 fails immediately with exit code `4` before `mongorestore` is ever started.
   - **Pass 2 (Apply)**: Rewinds the archive file stream and decrypts a second time directly into `mongorestore --archive --gzip` stdin. Nothing is applied to the datastore unless Pass 1 succeeded completely.

---

## 4. `roleradar wipe` (and `scripts/wipe-data.ps1`)

Destructively wipes all records in `notes`, `note_revisions`, and `audit_log` (`FR-016`, Constitution Principle VII).

### Synopsis

```powershell
pwsh ./scripts/wipe-data.ps1 [-Confirm "WIPE-ALL-ROLERADAR-DATA"]
# or directly:
dotnet run --project src/RoleRadar.Cli -- wipe [--confirm WIPE-ALL-ROLERADAR-DATA]
```

### Behavioral Contract

- If invoked without `--confirm WIPE-ALL-ROLERADAR-DATA` (and without typing `WIPE-ALL-ROLERADAR-DATA` at an interactive prompt), refuses to run, exits with code `2`, and leaves all collections untouched.

---

## 5. `scripts/start-services.ps1`

Starts the local MongoDB replica-set container (`deploy/docker-compose.yml`, `mongo:8.0.34`, bound to `127.0.0.1:27017`) with named persistent volume (`roleradar-mongo-data`), waits for replica-set initialization and health check (`rs.status()`), and verifies readiness (`FR-013`).

---

## 6. `roleradar status` (`StatusCommand`)

Probes the local MongoDB replica set (`rs0`) via `INoteRepository.CheckReadinessAsync` (`MongoReadinessProbe`) within a strict `<5 second` timeout budget (`FR-019`, `SC-007`).

### Synopsis

```powershell
dotnet run --project src/RoleRadar.Cli -- status [--json]
```

### Options

- `--json` (`Option<bool>`): Emit structured JSON readiness output (`isReady`, `endpoint`, `replicaSet`, `latencyMs`, `remediationCommand`).

### Exit Codes & Behavioral Contract

- **Exit Code `0` (Ready)**: Replica set `rs0` is reachable and ready for multi-document transactions. Prints readiness confirmation (or JSON payload when `--json` is supplied).
- **Exit Code `5` (Offline / Unreachable)**: Fails fast in `<5 seconds` (`ErrorCode.DatastoreOffline`) and prints the actionable remediation message instructing the user to run `pwsh ./scripts/start-services.ps1` (or `docker compose -f deploy/docker-compose.yml up -d`).
