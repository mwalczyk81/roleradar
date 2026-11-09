# CLI Reference

The demo commands (`roleradar demo seed`, `credentials`, `totp`) run only inside the demo stack and are described in [demo.md](demo.md).

RoleRadar includes the `roleradar` command-line utility (`src/RoleRadar.Cli`):

## `roleradar status`

Checks datastore readiness, round-trip ping latency, and cluster configuration.

- `--json`: Emit machine-readable JSON status output.

## `roleradar import <files...>`

Imports markdown documents into memory, preserving exact headings, sections, code blocks, and tags.

- `--dry-run`: Parse and reconcile notes against database without committing writes or audit entries.
- `--verbose`, `-v`: Output verbose diagnostic reconciliation logs to standard error.
- `--json`: Output full reconciliation summaries as JSON.
- `--default-date <YYYY-MM-DD>`: Fallback date for undated headings and tags.
- `--source-id <id>`: Custom provenance identifier for imported documents.
- `--area <area>`: Target area category (default: `imported`).

## `roleradar backup [destination]`

Creates a compressed, streaming passphrase-encrypted archive (`.rrbak`) of the database.

- `[destination]`: Target directory or file path (defaults to current directory).
- `--passphrase-env <VAR>`: Read encryption passphrase from the specified environment variable.
- `--passphrase-stdin`: Read encryption passphrase from standard input.

> [!NOTE]
> CLI arguments like `--passphrase <value>` are explicitly forbidden for security.
> **Backup Consistency Note**: Backups are guaranteed consistent only when no writes occur during the dump because MongoDB's `--oplog` flag cannot be replayed together with namespace filtering (`--nsInclude`).

## `roleradar reconcile-read-models`

Backfills the derived, denormalised fields the OData read models rely on (postings, applications, criteria). It is a dry run unless `--confirm RECONCILE-READ-MODELS` is passed. Only the owner runs it on real data, in the order given in [docs/operations/reconcile-read-models.md](operations/reconcile-read-models.md); automated agents and CI never do.

## Running on the real stack (Windows)

The bare `roleradar ...` commands above assume the CLI is on your path and can reach Mongo. On the Docker Compose stack use these forms from PowerShell at the repository root, after rebuilding the image (`docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.apps.yml --profile apps build cli`):

```powershell
# Backup: runs the built Release CLI on the host, which dumps through `docker compose exec mongo mongodump`.
pwsh ./scripts/backup.ps1 <destination>

# Reconcile: runs in the CLI image on the compose network, where the hostname "mongo" resolves.
docker run --rm --network deploy_default -e "ROLERADAR_MONGO_CONNECTION_STRING=mongodb://mongo:27017/?directConnection=true" -e ROLERADAR_MONGO_DATABASE=roleradar roleradar-cli:local reconcile-read-models [--confirm RECONCILE-READ-MODELS]
```

`scripts/roleradar-docker.ps1` builds the same container command. It adds `--network <name>` only when `ROLERADAR_DOCKER_NETWORK` is set (for example `deploy_default`), and uses `ROLERADAR_CLI_IMAGE` for the image (default `roleradar-cli:test`). Full run order and safety rules: [docs/operations/reconcile-read-models.md](operations/reconcile-read-models.md).

## `roleradar restore <archive-path>`

Performs a strict two-pass verification and restore of an encrypted archive. Pass 1 validates the header, authentication tags, and archive magic numbers. Pass 2 restores data to MongoDB.

- `--passphrase-env <VAR>`: Read decryption passphrase from the specified environment variable.
- `--passphrase-stdin`: Read decryption passphrase from standard input.
- `--force-overwrite`: Required if the target database contains existing collections or notes.

## `roleradar wipe`

Resets and drops all notes, revisions, and audit collections in the database.

- `--confirm WIPE-ALL-ROLERADAR-DATA`: Mandatory safety token to confirm destruction.
