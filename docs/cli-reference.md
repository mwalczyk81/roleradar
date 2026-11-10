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

## `roleradar audit list`

Reads the append-only audit log for the owner, newest first. It is read-only: it writes nothing to any collection, including `audit_log`. The CLI is the only way to read the log; there is no MCP tool and no API endpoint for it (ADR 0041).

- `--entity-type <type>`: one of `note`, `company`, `posting`, `application`, `application_event`, `contact`, `criterion`, `search_run`.
- `--entity-id <id>`: exact match.
- `--actor <actor>`: exact match, for example an owner subject, `cli-user` or `mcp-stdio`.
- `--from <time>`, `--to <time>`: ISO 8601 with `Z` or an offset, for example `2026-11-09T14:03:22Z`. `--from` is inclusive, `--to` is exclusive, and `--from` must be earlier than `--to`. A time with no `Z` or offset is rejected.
- `--limit <n>`: 1 to 100, default 25. A value out of range is an error, not clamped.
- `--cursor <token>`: the token printed after `next:` on the previous page.
- `--fields`: also print the names of the changed fields, never their values.
- `--json`: emit `{ "ok": true, "data": { "items": [...], "nextCursor": "..." } }`. Each item has `occurredAt`, `entityType`, `entityId`, `operation`, `actor` and `requestCorrelationId`, plus `changedFields` with `--fields`.

Text output is one line per entry, followed by `next: <cursor>` when more entries exist:

```text
2026-11-09T14:03:22.418Z  posting  0197...b5a  posting_transition  01970f3e-...  req=01970f40-...
```

The columns are time (UTC), entity type, entity id, operation, actor and `req=` with the request correlation id (`-` when the entry has none). With `--fields` the line ends with `fields=a,b`. An empty value prints as `-`. Every control character in a printed value, including ESC, CR and LF, is replaced with `?`, so a stored value cannot move the cursor or start a second line. `--json` keeps the stored value and lets JSON escape it.

Appended note text, before and after values and content summaries are never read from the store, so no mode can print them.

| Exit | Meaning | `--json` `error.code` |
| --- | --- | --- |
| 0 | Success, including an empty page | none |
| 2 | An option is invalid (limit, time, cursor, entity type, entity id or actor) | `validation_error` |
| 1 | The data store is unreachable, or another failure. The message never carries a connection-string credential | `service_unavailable` or `internal_error` |

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
