# Quickstart & Validation Guide: Feature 001 — Memory Replacement

**Feature**: `001-memory-replacement`
**Target Environment**: Windows 11 with PowerShell 7 (`pwsh`), .NET 10 SDK (`10.0.402` via `global.json`), Docker Desktop.

---

## Prerequisites

1. **.NET 10 SDK** (`dotnet --version` >= `10.0.402`, governed by `global.json` with `rollForward: "latestPatch"`)
2. **Docker Desktop** running with Linux containers enabled (`docker info`)
3. **PowerShell 7+** (`pwsh --version`)

---

## 1. Build & Run Full Automated Test Suite (Clean Clone)

All unit, architecture, contract, backup tamper/streaming, 5,000-note search benchmark, and Testcontainers replica-set integration tests run against ephemeral `mongo:8.0.34` containers without touching any local running MongoDB container (`Constitution Principle II`):

```powershell
dotnet restore RoleRadar.sln --locked-mode
dotnet format RoleRadar.sln --verify-no-changes
dotnet build RoleRadar.sln -c Release -warnaserror
dotnet test RoleRadar.sln -c Release --collect:"XPlat Code Coverage"
```

**Expected Outcome**:
- Zero compiler, analyzer, formatting, or `NuGetAudit` warnings.
- Architecture test (`RoleRadar.Tests.Architecture`) confirms:
  - Strict inward dependencies (`Domain` → `Application` → `Infrastructure` → `Hosts`).
  - Every `ICommandHandler<,>` registration in DI is wrapped by `TransactionAndAuditCommandDecorator<,>` (Constitution v1.1.0 Principle III).
  - No `Update*`, `Replace*`, or `Delete*` methods exist against `note_revisions` or `audit_log`, and no hard-delete method exists on `INoteRepository`.
- Contract & integration tests (`RoleRadar.Tests.Contract`, `RoleRadar.Tests.Integration`) provision an ephemeral `mongo:8.0.34` replica-set container via `Testcontainers.MongoDb` `4.16.0`, verify multi-document transaction rollback on injected faults, verify typed `Result<T>` outcomes for expected failures, verify all backup encryption tamper/truncation/reorder/two-pass restore cases, verify 5,000-note search p95 < 200 ms (`SC-003`), verify stdout protocol hygiene (`FR-020`), and verify >=85% line coverage across `RoleRadar.Domain` and `RoleRadar.Application`.

---

## 2. Start Local Persistent Data Services (`US3`)

Start the single-node MongoDB replica set (`rs0`, image `mongo:8.0.34`) defined in `deploy/docker-compose.yml`, bound strictly to `127.0.0.1:27017` with named volume `roleradar-mongo-data`. The replica set is initiated with member host `127.0.0.1:27017` (`rs.initiate({ _id: "rs0", members: [{ _id: 0, host: "127.0.0.1:27017" }] })`) so processes on the host machine can connect directly to `mongodb://127.0.0.1:27017/?replicaSet=rs0` without container-hostname resolution errors (`directConnection=true` is also supported for single-node host connections):

```powershell
pwsh ./scripts/start-services.ps1
```

**Expected Outcome**:
- `roleradar-mongo` container starts from `deploy/docker-compose.yml`, initializes replica set `rs0` with member host `127.0.0.1:27017`, passes healthcheck, and prints `RoleRadar MongoDB replica set (rs0) is healthy and ready.`
- Host processes (`roleradar` CLI, `RoleRadar.Mcp.Stdio`, and the host-to-compose integration test in `BackupRestoreWipeIntegrationTests`) connect cleanly from the host machine to `127.0.0.1:27017`.

---

## 3. Validate Idempotent Byte-Exact Markdown Import (`US2`, `SC-001`, `SC-002`)

Import the two fictional sample memory files (~46 KB and ~26 KB, marked `-text` in `.gitattributes` to preserve exact `CRLF` and UTF-8 BOM bytes) twice:

```powershell
dotnet run --project src/RoleRadar.Cli -- import `
  tests/fixtures/fictional-memory-primary.md `
  tests/fixtures/fictional-memory-archive.md `
  --area criteria `
  --default-date 2026-10-17 `
  --json

# Run a second time to verify idempotence:
dotnet run --project src/RoleRadar.Cli -- import `
  tests/fixtures/fictional-memory-primary.md `
  tests/fixtures/fictional-memory-archive.md `
  --area criteria `
  --default-date 2026-10-18 `
  --json
```

**Expected Outcome**:
- First run reports `createdCount > 0`, `updatedCount: 0`, `conflictCount: 0`, `orphanCount: 0`.
- Second run (even with a different `--default-date`) reports `createdCount: 0`, `updatedCount: 0`, `unchangedCount` equal to the first run's `createdCount` (because changed-since-import comparison uses the body hash only).
- Reassembling sections by `section_order_index` matches the source files byte-for-byte.

---

## 4. Validate MCP Stdio Server (`US1`, `US4`, `SC-004`, `SC-007`)

Publish and register the stdio MCP server ([contracts/mcp-tools.md](./contracts/mcp-tools.md)):

```powershell
dotnet publish src/RoleRadar.Mcp.Stdio -c Release -o "$env:LOCALAPPDATA\roleradar\mcp-stdio"
```

1. **Create, Append, Replace-Section, Search, Archive (`US1`)**:
   - Invoke `note_create`, `note_append` (no version token required; no `note_revisions` snapshot created), `note_replace_section` (requires `versionToken`; saves full prior body in `note_revisions`), `note_search`, `note_get`, and `note_archive`.
   - Restart the container (`docker compose -f deploy/docker-compose.yml restart`) and confirm `note_search` and `note_get` return the persisted notes.
2. **Offline Fast-Fail Check (`US4`, `SC-007`)**:
   - Stop the container (`docker compose -f deploy/docker-compose.yml stop`), invoke any MCP tool, and confirm it returns a structured offline error in under 5 seconds with instructions to run `pwsh ./scripts/start-services.ps1`.
   - Restart services (`pwsh ./scripts/start-services.ps1`).

---

## 5. Validate Streamed Encrypted Backup, Safe Wipe, and Two-Pass Restore (`US3`, `SC-006`)

> [!NOTE]
> Backup is consistent only when no writes happen during the dump because `--oplog` cannot be replayed together with `--nsInclude`.

```powershell
$env:ROLERADAR_BACKUP_PASSPHRASE = "fictional-test-passphrase-2026"
$backupDir = Join-Path $env:TEMP "roleradar-quickstart-backups"
New-Item -ItemType Directory -Force -Path $backupDir | Out-Null

# 1. Streamed encrypted backup via libsodium secretstream + Argon2id (no plaintext temp files written)
pwsh ./scripts/backup.ps1 -OutputDirectory $backupDir -PassphraseEnv ROLERADAR_BACKUP_PASSPHRASE

# 2. Verify wipe without confirmation is rejected
pwsh ./scripts/wipe-data.ps1
# Expected: exits non-zero without deleting data

# 3. Execute confirmed wipe
pwsh ./scripts/wipe-data.ps1 -Confirm "WIPE-ALL-ROLERADAR-DATA"

# 4. Two-pass restore from the encrypted archive (Pass 1 verifies entire stream; Pass 2 restores)
$latestBackup = Get-ChildItem $backupDir -Filter "*.rrbak" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
pwsh ./scripts/restore.ps1 -ArchivePath $latestBackup.FullName -PassphraseEnv ROLERADAR_BACKUP_PASSPHRASE
```

**Expected Outcome**:
- Backup writes a `.rrbak` archive encrypted with `Geralt` (`IncrementalXChaCha20Poly1305` + `Argon2id`) without touching `%TEMP%` with plaintext dump files.
- Unconfirmed wipe refuses to run; confirmed wipe empties `notes`, `note_revisions`, and `audit_log`.
- Two-pass restore verifies the full archive in Pass 1 before applying Pass 2 to `mongorestore`, restoring 100% of notes, revisions, and audit entries identically; attempting a second restore without `-ForceOverwrite` fails safely because the datastore is non-empty.
