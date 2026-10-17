# RoleRadar

RoleRadar is an agentic job search copilot and career memory system.

## Quickstart

For full end-to-end setup and validation instructions, see the [Quickstart Guide](specs/001-memory-replacement/quickstart.md).

### Backup & Restore

RoleRadar provides encrypted, streaming backups via libsodium (`IncrementalXChaCha20Poly1305` + `Argon2id`):

```powershell
# Backup database
pwsh ./scripts/backup.ps1 -PassphraseEnv ROLERADAR_BACKUP_PASSPHRASE

# Restore from backup (two-pass verification and restore)
pwsh ./scripts/restore.ps1 -ArchivePath <path-to-archive.rrbak> -PassphraseEnv ROLERADAR_BACKUP_PASSPHRASE
```

> [!NOTE]
> Backup is consistent only when no writes happen during the dump because `--oplog` cannot be replayed together with `--nsInclude`.
