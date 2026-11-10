# Documentation index

Everything under `docs/`, grouped by what you want to do. The [root README](../README.md) is the front door; [CONTRIBUTING.md](../CONTRIBUTING.md) covers working on the code.

## Try it

- [demo.md](demo.md): the one-command demo over fictional data, its sign-in, teardown, the guard that keeps it apart from a real stack, and how the README pictures are regenerated.
- [images/](images/manifest.json): the README pictures, listed in `images/manifest.json` with the hero image and the fictional records they show.

Start the demo:

```bash
docker compose -p roleradar-demo -f deploy/demo/docker-compose.yml up --build --wait
```

## Use it

- [mcp-clients.md](mcp-clients.md): registering the MCP server with Claude Code, Claude Desktop or a scheduled task.
- [mcp-tools.md](mcp-tools.md): the twelve MCP tools.
- [cli-reference.md](cli-reference.md): every CLI command and option.
- [insights.md](insights.md): the Insights page, its windows, drill-downs and Markdown notes.
- [api/odata-queries.md](api/odata-queries.md): the OData read API, its allowlisted sort fields and filter rules.
- [integrations/excel-powerbi.md](integrations/excel-powerbi.md): reading the API from Excel or Power BI (prospective).

## Run it

- [operations/backup-restore.md](operations/backup-restore.md): encrypted backup and the two-pass restore.
- [operations/reconcile-read-models.md](operations/reconcile-read-models.md): backfilling the derived read-model fields, owner only.
- [operations/logs-and-health.md](operations/logs-and-health.md): reading the logs (compose, text mode, the opt-in viewer), following a request by correlation id, log levels and caps, the health endpoints, what a 503 means, and `roleradar audit list`.

## Decisions and governance

- [adr/README.md](adr/README.md): the index of architecture decision records.
- [CHANGELOG.md](../CHANGELOG.md): what changed in each release.
- [constitution-changelog.md](constitution-changelog.md): changes to the project constitution.
- [repo-settings.md](repo-settings.md): branch ruleset and repository settings.
- [threat-model.md](threat-model.md): what is trusted, what is defended, the demo's exposure, and what is not claimed.
- [pre-announce.md](pre-announce.md): the local secret, name and picture checks to run before announcing the repository.
- [denylist.example.txt](denylist.example.txt): the format of the private local list the privacy hooks scan for.

## Work on it

The fast test loop:

```powershell
pwsh ./scripts/test-fast.ps1
```

The review gate, before a pull request:

```powershell
pwsh ./scripts/review-gate.ps1
```
