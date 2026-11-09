# Backup & Recovery Workflow

RoleRadar provides encrypted backup and restore of the database: a streamed libsodium `secretstream` (XChaCha20-Poly1305) archive with an `Argon2id` key derivation, through `Geralt` ([ADR 0007](../adr/0007-backup-streaming-aead-encryption.md)). Passphrases are never accepted as command-line arguments.

## Creating a Backup

```powershell
# Using the helper script (with environment passphrase)
$env:BACKUP_PW = "YourSecurePassphraseHere"
pwsh ./scripts/backup.ps1 ./backup.rrbak -PassphraseEnv BACKUP_PW

# Or interactively (prompts securely for passphrase)
pwsh ./scripts/backup.ps1 ./backup.rrbak

# Or using the containerized CLI
pwsh ./scripts/roleradar-docker.ps1 backup /data/backup/backup.rrbak --passphrase-env BACKUP_PW
```

## Restoring from a Backup

1. **Standard Safe Restore (Empty Target)**:
   By default, `restore` checks target database collections and refuses to overwrite existing data:

   ```powershell
   pwsh ./scripts/restore.ps1 ./backup.rrbak -PassphraseEnv BACKUP_PW
   ```

2. **Force Overwrite Restore (`--force-overwrite`)**:
   If restoring over existing collections, `--force-overwrite` runs `mongorestore --drop`. To protect developer data, restoring into the default database `roleradar` requires explicit confirmation via `ROLERADAR_CONFIRM_OVERWRITE_DEFAULT=1`:

   ```powershell
   $env:ROLERADAR_CONFIRM_OVERWRITE_DEFAULT = "1"
   pwsh ./scripts/restore.ps1 ./backup.rrbak -PassphraseEnv BACKUP_PW -ForceOverwrite
   ```

## Container Bind-Mount Permissions & Non-Matching Host Users

RoleRadar container images specify an unprivileged non-root user (`USER 1654:1654`). On Linux and macOS hosts, `scripts/roleradar-docker.ps1` and the test harness dynamically pass `--user "$(id -u):$(id -g)"` whenever a host directory is bind-mounted writable (`:rw`), such as the backup destination (`/data/backup`). This ensures files written to the host match the host user's UID and GID so that runner-owned directories (even with restricted `0700` permissions) can receive backups and be cleanly managed or deleted by host processes without permission errors. On Windows and Docker Desktop, user translation is handled transparently.
