# Quickstart & Verification Guide: Feature 004 Job-Search Tracker Import

## Prerequisites

1. MongoDB replica set running (or Testcontainers fixture for automated testing).
2. Environment variables set:
   ```powershell
   $env:ROLERADAR_MONGO_CONNECTION_STRING="mongodb://127.0.0.1:27017/?replicaSet=rs0"
   $env:ROLERADAR_MONGO_DATABASE="roleradar_verify"
   ```

## Scenario 1: Dry-Run Preview

Run dry-run on a test bundle:

```powershell
dotnet run --project src/RoleRadar.Cli -- tracker-import ./fixtures/fictional-bundle --default-date 2026-10-24 --report C:/Temp/tracker-report.txt
```

**Expected Results**:
- Console shows aggregate counts only (zero company names, job titles, or salary amounts).
- `C:/Temp/tracker-report.txt` is created with detailed row listings.
- No database records or audit entries are committed.

## Scenario 2: Apply Ingestion

Execute the import with `--apply`:

```powershell
dotnet run --project src/RoleRadar.Cli -- tracker-import ./fixtures/fictional-bundle --default-date 2026-10-24 --apply --report C:/Temp/tracker-report.txt
```

**Expected Results**:
- Summary counts match the dry-run counts 100%.
- Database collections (`companies`, `postings`, `applications`, `criteria`, `notes`) and `audit_log` contain imported records.
- All audit log entries carry actor `cli-import-tracker` and `import_*` operations.

## Scenario 3: Idempotent Re-Run

Re-execute the identical command with `--apply`:

```powershell
dotnet run --project src/RoleRadar.Cli -- tracker-import ./fixtures/fictional-bundle --default-date 2026-10-24 --apply
```

**Expected Results**:
- Console reports 0 created, 0 updated, and explicitly confirms an idempotent no-op.
- Zero new audit log entries are generated.

## Scenario 4: Integrity Verification

Run the referential integrity check:

```powershell
dotnet run --project src/RoleRadar.Cli -- verify
```

**Expected Results**:
- Exits with exit code `0` (clean).
- All posting company links and application posting links resolve cleanly.

## Scenario 5: Containerized Execution (Hardened Read-Only)

Run the import from the hardened container image:

```bash
docker run --rm \
  -v /absolute/path/to/fixtures/fictional-bundle:/bundle:ro \
  -v /tmp/reports:/reports:rw \
  -e ROLERADAR_MONGO_CONNECTION_STRING="mongodb://host.docker.internal:27017/?replicaSet=rs0" \
  -e ROLERADAR_MONGO_DATABASE="roleradar_verify" \
  roleradar-cli:local \
  tracker-import /bundle --default-date 2026-10-24 --report /reports/report.txt
```

**Expected Results**:
- Container runs successfully against the read-only mounted bundle directory.
- Report is written to `/reports/report.txt`.

## Scenario 6: Backup & Restore Round-Trip

Verify that imported data survives a full encrypted backup and restore:

```powershell
# Ensure $env:ROLERADAR_BACKUP_PASSPHRASE is configured in environment
dotnet run --project src/RoleRadar.Cli -- backup --out C:/Temp/backup.bin
dotnet run --project src/RoleRadar.Cli -- restore --in C:/Temp/backup.bin --confirm-destructive-overwrite
dotnet run --project src/RoleRadar.Cli -- verify
```

**Expected Results**:
- Backup and restore complete successfully with code 0.
- `roleradar verify` confirms referential integrity is preserved after restoration.
