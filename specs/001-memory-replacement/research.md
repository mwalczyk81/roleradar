# Phase 0 Research: Feature 001 — Memory Replacement (Constitution v1.1.0)

**Feature**: `001-memory-replacement`
**Date**: 2026-10-17
**Spec**: [spec.md](./spec.md)
**Constitution**: v1.1.0

---

## 1. Datastore Selection & Multi-Document Atomicity for Feature 001 ([ADR-0001](../../docs/adr/0001-mongo-only-for-feature-001.md))

- **Decision**: Use MongoDB (`mongo:8.0.34`, verified on Docker Hub as the latest `8.0.x` patch tag on 2026-10-17) configured as a single-node replica set (`rs0`) bound to `127.0.0.1:27017` under `deploy/docker-compose.yml` as the sole datastore for Feature 001 (`notes`, `note_revisions`, and `audit_log` collections), deferring PostgreSQL until Feature 002.
- **Rationale**:
  - Feature 001 manages narrative markdown notes, their destructive-edit revisions, and their write audit log.
  - Every write operation (`CreateNote`, `AppendToNote`, `ReplaceNoteSection`, `ArchiveNote`, and `ImportMarkdown`) must atomically persist the note mutation, the prior-body revision (for `ReplaceNoteSection` and `ImportMarkdown` overwrite), and the `audit_log` entry so a crash or fault can never leave an unaudited write or an out-of-sync revision.
  - Keeping all three collections inside a single MongoDB replica set enables standard multi-document ACID transactions (`IClientSessionHandle.WithTransactionAsync`) across `notes`, `note_revisions`, and `audit_log` without distributed two-phase commit (`2PC`) or cross-database outbox complexity.
- **Alternatives Considered**:
  - *Split Notes in MongoDB and Audit Log in PostgreSQL in Feature 001*: Rejected because coordinating atomic writes across MongoDB and PostgreSQL would require distributed transactions or an outbox worker before any relational entities exist.
  - *Standalone MongoDB instance (no replica set)*: Rejected because MongoDB requires a replica set topology (`--replSet rs0`) to support multi-document transactions.

---

## 2. Aggregate-Level Repository Port: `INoteRepository` ([ADR-0002](../../docs/adr/0002-inote-repository-single-write-port.md))

- **Decision**: Define a single aggregate-level repository port, `INoteRepository`, in `RoleRadar.Application.Abstractions` whose methods express domain use cases covering a `Note`, its `NoteRevision` snapshots, and the `AuditLogEntry` written in the same transaction. Introduce no generic `IRepository<T>` over the database driver.
- **Rationale**:
  - Directly satisfies Constitution v1.1.0 Principle IV (*Named Seams Only*), which adopts the repository pattern at the aggregate level (`INoteRepository`) and forbids generic `IRepository<T>` without an ADR.
  - Works in concert with the `TransactionAndAuditCommandDecorator<TCommand, TResponse>` so that every command handler execution runs inside an atomic transaction and records its append-only `AuditLogEntry` in the same commit unit.
- **Alternatives Considered**:
  - *Generic `IRepository<Note>`, `IRepository<NoteRevision>`, `IRepository<AuditLogEntry>`*: Explicitly forbidden by Constitution Principle IV because it leaks table-level CRUD semantics into `Application` and breaks aggregate transactional cohesion.

---

## 3. Command & Query Handlers with Explicit Decorators Instead of a Mediator ([ADR-0006](../../docs/adr/0006-handlers-with-decorators-no-mediator.md))

- **Decision**: Implement each application use case as a plain handler class implementing `ICommandHandler<TCommand, Result<TResponse>>` or `IQueryHandler<TQuery, Result<TResponse>>`, injected directly into the `RoleRadar.Mcp.Stdio` tools and `RoleRadar.Cli` commands:
  - **Command Handlers (5)**: `CreateNoteCommandHandler`, `AppendToNoteCommandHandler`, `ReplaceNoteSectionCommandHandler`, `ArchiveNoteCommandHandler`, `ImportMarkdownCommandHandler`.
  - **Query Handlers (2)**: `GetNoteQueryHandler`, `SearchNotesQueryHandler`.
  - **Decorators**: `TransactionAndAuditCommandDecorator<TCommand, TResponse>` wraps every `ICommandHandler<,>` registration (validating inputs, opening the atomic unit-of-work transaction, and enforcing audit entry recording); `LoggingDecorator` wraps both command and query handlers (routing logs to `stderr`).
  - **Typed Results**: Expected domain/application failures (`NotFound`, `StaleVersionToken`, `AmbiguousHeading`, `SectionNotFound`, `ImportConflict`, `InvalidUtf8`, `DatastoreOffline`) are returned via a discriminated `Result<T>` / `Error` type rather than thrown as exceptions.
  - **Architecture Enforcement**: A dedicated test in `RoleRadar.Tests.Architecture` resolves the DI `IServiceProvider` / `IServiceCollection` descriptors and proves that every registered `ICommandHandler<,>` has `TransactionAndAuditCommandDecorator<,>` in its pipeline (Constitution v1.1.0 Principle III).
- **Rationale**:
  - Keeps control flow explicit, allocation-light, and trivially navigable in Visual Studio ("Go To Implementation" jumps straight to the handler).
  - Avoids third-party mediator licensing risk: per secondary sources, `MediatR` 13+ transitioned to a dual-license model (Reciprocal Public License `RPL-1.5` or paid commercial license, to be verified before any future adoption), which is incompatible with RoleRadar's permissive `MIT` license (`Constitution Principle IV`).
- **Alternatives Considered**:
  - *MediatR*: Rejected per Constitution Principle IV due to `RPL-1.5` / commercial licensing incompatibility with an MIT portfolio repository and unnecessary indirection for 7 handlers.
  - *Monolithic `NoteService` / `MarkdownImportService` classes*: Replaced by single-responsibility command/query handlers so cross-cutting transaction, audit, validation, and logging decorators apply uniformly per command.

---

## 4. MCP Transport & Live-Verified NuGet / Docker Versions ([ADR-0003](../../docs/adr/0003-stdio-first-mcp.md) & [ADR-0004](../../docs/adr/0004-verified-mcp-sdk-usage.md))

- **Decision**: Query `api.nuget.org` and `hub.docker.com` live at plan time (2026-10-17) and pin the exact current stable releases in `Directory.Packages.props`, `global.json`, and `deploy/docker-compose.yml`:
  - **.NET SDK (`global.json`)**: `10.0.402` (`rollForward: "latestPatch"`)
  - **Docker Image**: `mongo:8.0.34` (latest `8.0.x` patch tag on Docker Hub)
  - **`ModelContextProtocol`**: `2.2.0`
  - **`Microsoft.Extensions.Hosting`**: `10.0.13`
  - **`MongoDB.Driver`**: `3.12.0`
  - **`System.CommandLine`**: `2.0.13` (stable 2.0.x GA release; replaces the stale 2022 beta `2.0.0-beta4.22272.1` and uses the GA `Command.Add`, `Command.SetAction`, `ParseResult.GetValue`, and `Parse(args).InvokeAsync()` APIs)
  - **`Geralt`**: `4.4.0` (with transitive `libsodium` `1.0.22`)
  - **`xunit.v3`**: `4.0.2` (evaluated and adopted over legacy `xunit` `2.9.3`; natively targets `net8.0`+ with `Microsoft.Testing.Platform` v2 support)
  - **`xunit.runner.visualstudio`**: `4.0.1`
  - **`Microsoft.NET.Test.Sdk`**: `18.11.0`
  - **`Testcontainers.MongoDb`**: `4.16.0` (natively targets `net10.0`; exposes `new MongoDbBuilder("mongo:8.0.34").WithReplicaSet("rs0").Build()`)
  - **`NetArchTest.Rules`**: `1.3.2`
  - **`coverlet.collector`**: `10.1.0`
- **Rationale**:
  - Eliminates stale remembered versions and 2022 pre-release betas while satisfying Constitution Principle VIII (*Pinned Dependencies*) and enabling `RestorePackagesWithLockFile` (`packages.lock.json`) + `NuGetAudit` as errors in CI.

---

## 5. Revision History & Concurrency Policy ([ADR-0005](../../docs/adr/0005-revision-history-policy.md))

- **Decision**:
  - Store a full prior-body `NoteRevision` document **only** on text-destroying writes (`ReplaceNoteSection` and `ImportMarkdown` overwrite).
  - Execute `AppendToNote` as an atomic server-side update (no caller version token required) that records only `appended_text` and `resulting_version_token` in `audit_log` without creating a `NoteRevision` snapshot.
  - Require the caller's `VersionToken` on `ReplaceNoteSection` and return a typed `Result` failure (`ErrorCode.StaleVersionToken`) inside the transaction when stale.
- **Rationale**:
  - Prevents $O(N^2)$ quadratic storage growth across repeated appends to 100 KB+ notes while preserving 100% of overwritten text on destructive edits.

---

## 6. Streamed Authenticated Backup Encryption & Two-Pass Restore ([ADR-0007](../../docs/adr/0007-backup-streaming-aead-encryption.md))

- **Decision**:
  - **Live Evaluation of .NET libsodium Candidates on NuGet.org (2026-10-17)**:
    1. **`Geralt` (`4.4.0`) — SELECTED**: Actively maintained (2026 release), MIT licensed, stable release on NuGet.org (`net8.0` target, compatible with `net10.0`), backed by official `libsodium` `1.0.22`. Reflection inspection of `Geralt.dll` confirmed first-class span-based APIs for both **Argon2id** (`Geralt.Argon2id.DeriveKey(Span<byte> outputKeyingMaterial, ReadOnlySpan<byte> password, ReadOnlySpan<byte> salt, int iterations, int memorySize)`) and **libsodium `crypto_secretstream_xchacha20poly1305`** (`Geralt.IncrementalXChaCha20Poly1305` with `KeySize = 32`, `HeaderSize = 24`, `TagSize = 17`, `ChunkFlag.Message = 0`, `ChunkFlag.Boundary = 1`, `ChunkFlag.Rekey = 2`, `ChunkFlag.Final = 3`, `EncryptChunk`, and `DecryptChunk` with `associatedData` support).
    2. **`NSec.Cryptography` (`26.4.0`) — Rejected**: Stable and maintained (`net9.0`), includes `Argon2id` and one-shot `XChaCha20Poly1305`, but assembly inspection confirmed it does **not** expose `crypto_secretstream_xchacha20poly1305` / incremental AEAD streaming.
    3. **`Sodium.Core` (`1.4.1`) — Rejected**: Exposes `PasswordHash` and one-shot `SecretAeadXChaCha20Poly1305`, but does **not** expose `crypto_secretstream_xchacha20poly1305`.
    4. **`LibSodium.Net` (`0.16.0-alpha`) — Rejected**: No stable release exists on NuGet.org (alpha pre-releases only).
  - **Archive Format & Streaming Pipeline**:
    - Header (`48 bytes`): Magic `"RRBK"` (4B) + Format Version `0x01` (1B) + Flags `0x00` (1B) + Argon2id Iterations (4B LE) + Argon2id Memory KiB (4B LE) + 16-byte random Salt + 24-byte `IncrementalXChaCha20Poly1305` Header (total 54 bytes).
    - Key Derivation: `Geralt.Argon2id.DeriveKey` derives a 32-byte key from the passphrase + 16-byte salt (default: `iterations = 4`, `memorySize = 262144` KiB = 256 MiB).
    - Chunked Stream: Streams `mongodump --archive --gzip` stdout in 64 KiB chunks through `IncrementalXChaCha20Poly1305.EncryptChunk`, passing the binary file header as `associatedData` on chunk `0` and setting `ChunkFlag.Final` (`3`) on the terminal chunk. Because `crypto_secretstream_xchacha20poly1305` increments its internal nonce per chunk and authenticates `ChunkFlag.Final`, wrong passphrase, bit flips, truncated streams, dropped chunks, and reordered chunks are all cryptographically rejected.
    - **Tool Source & Invocation Model**:
      - Default to `docker compose exec -T mongo mongodump` and `docker compose exec -T mongo mongorestore` so tool binaries match the server version (`mongo:8.0.34`) without requiring database tools to be installed on the host machine.
      - Make invocation configurable via `BackupOptions.DumpCommand` and `BackupOptions.RestoreCommand` so both production compose services and integration Testcontainers can supply explicit container or host invocations.
    - **Oplog Consistency Evaluation & Scoped Database Restore**:
      - Evaluated `mongodump --oplog` on single-node replica set `rs0`. Full-instance dumps with `--oplog` capture point-in-time consistent state across all collections (`notes`, `note_revisions`, and `audit_log`).
      - In `mongorestore` (verified on `mongo:8.0.34`), `--oplogReplay` conflicts with namespace inclusion options: passing `--oplogReplay` together with `--nsInclude` causes `mongorestore` to immediately abort with `Failed: cannot use --oplogReplay with includes specified`.
      - To guarantee that restoring into a specific database never drops or overwrites unrelated databases on the same MongoDB instance, `RestoreCommand` scopes restoration using `--nsInclude "<db>.*"` and omits `--oplogReplay`. If a user-provided or custom restore command specifies both flags, `SecretStreamBackupArchiveService` automatically strips `--oplogReplay` so that `mongorestore` executes cleanly without cross-database interference.
  - **Two-Pass Restore**:
    - **Pass 1 (Verify-Only)**: Streams the entire `.rrbak` archive file through `IncrementalXChaCha20Poly1305.DecryptChunk`, verifying header associated data, every chunk's Poly1305 tag, sequence integrity, and the terminal `ChunkFlag.Final` tag while discarding plaintext output in memory. Single `FileStream` with `FileShare.Read` is maintained across both passes and rewound (`Position = 0`) before Pass 2.
    - **Pass 2 (Apply)**: Only after Pass 1 succeeds 100%, decrypts directly into `mongorestore --archive --gzip` stdin from the rewound file stream, enforcing chunk length sanity bounds. A corrupted or truncated archive is guaranteed to leave the datastore completely untouched.

---

## 7. Repository Standards, CI/CD, and Community Governance

- **Decision**:
  - Layout: `src/`, `tests/`, `docs/adr/`, `scripts/`, `deploy/` (`deploy/docker-compose.yml`), `.github/`, and `RoleRadar.sln` at the repository root.
  - `global.json` pinning `.NET SDK 10.0.402` (`rollForward: "latestPatch"`).
  - `.gitattributes` enforcing `* text=auto eol=lf`, `*.ps1 text eol=crlf`, and `tests/**/fixtures/** -text` so Git never mutates `CRLF` or UTF-8 BOM bytes in test fixtures.
  - NuGet lock files (`RestorePackagesWithLockFile=true`) with locked-mode restore (`dotnet restore --locked-mode`) in CI and `NuGetAudit` (`TreatWarningsAsErrors=true`, `NuGetAuditMode=all`, `NuGetAuditLevel=low`).
  - GitHub Actions (`ci.yml`, `codeql.yml`, `dependency-review.yml`) with actions pinned by full 40-char commit SHA, least-privilege `permissions`, and concurrency groups; Dependabot for `nuget`, `docker`, and `github-actions` with grouped minor/patch updates.
  - Community & Governance files: `README.md` (with `<10 minute` quickstart and Mermaid architecture diagram), `docs/adr/README.md` (ADR index), `SECURITY.md`, `CONTRIBUTING.md`, `CODEOWNERS`, `.github/PULL_REQUEST_TEMPLATE.md`, `.github/ISSUE_TEMPLATE/`, and `docs/repo-settings.md`.
