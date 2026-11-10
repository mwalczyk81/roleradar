# Contract: `roleradar audit list`

Read-only and owner-scoped. Decision: research R-013. Served by `ListAuditEntriesQuery` through `IAuditLogReader`. No MCP tool and no API endpoint.

## Synopsis

```text
roleradar audit list [--entity-type <type>] [--entity-id <id>] [--actor <actor>]
                     [--from <utc>] [--to <utc>] [--limit <n>] [--cursor <token>]
                     [--fields] [--json]
```

| Option | Rule |
| --- | --- |
| `--entity-type` | one of the entity types written to `audit_log`; an unknown value is a validation error |
| `--entity-id` | exact match |
| `--actor` | exact match, for example a subject, `cli-user`, `mcp-stdio` |
| `--from`, `--to` | ISO 8601 with `Z` or an offset, normalized to UTC; `--from` inclusive, `--to` exclusive; `--from` must be earlier than `--to` |
| `--limit` | 1 to 100, default 25; out of range is a validation error, not silently clamped |
| `--cursor` | opaque token from a previous page's `nextCursor`; a malformed token is a validation error |
| `--fields` | also print changed field names (never values) |
| `--json` | machine output in the same envelope style as `contact get --json` |

Owner: `OwnerId.Default`, as for every other CLI read.

## Text output

One line per entry, newest first, followed by `next: <cursor>` when more entries exist:

```text
2026-11-09T14:03:22.418Z  posting  0197...b5a  transition  01970f3e-...  req=01970f40-...
```

Columns: `occurredAt` (UTC), `entityType`, `entityId`, `operation`, `actor`, `requestCorrelationId` (or `-`). Control characters in any printed value are replaced with `?`.

## JSON output

```json
{
  "ok": true,
  "data": {
    "items": [
      {
        "occurredAt": "2026-11-09T14:03:22.418Z",
        "entityType": "posting",
        "entityId": "0197...",
        "operation": "transition",
        "actor": "01970f3e-...",
        "requestCorrelationId": "01970f40-...",
        "changedFields": ["disposition"]
      }
    ],
    "nextCursor": "opaque-token"
  }
}
```

`changedFields` appears only with `--fields`. Never present in any mode: `appendedText`, before and after values, content summaries, note bodies.

## Exit codes and errors

| Exit | When | `error.code` |
| --- | --- | --- |
| 0 | success, including an empty page | none |
| 2 | validation failure (option rules above) | `validation_error` |
| 1 | data store unreachable, or any other failure | `service_unavailable` or `internal_error`; the message carries no connection-string credential |

Running the command writes nothing to any collection, including `audit_log`.
