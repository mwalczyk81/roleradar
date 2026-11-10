# Data Model: Operability (013)

Only one stored shape changes: the audit entry gains one optional field and the collection gains one index. Everything else here is a runtime shape (log records, health answers, query results) defined in the contracts.

## AuditLogEntry (Domain, collection `audit_log`) (changed)

| Field (C# / BSON) | Change | Rule |
| --- | --- | --- |
| `Actor` / `actor` | value source changes | API writes: the authenticated `sub`. CLI writes: `cli-user`, `cli-import`, `cli-import-tracker`, `demo-seed`. MCP writes: `mcp-stdio`. Never `api-user`, `agent`, `system` or empty for new entries. Existing entries are not rewritten (append-only). |
| `RequestCorrelationId` / `request_correlation_id` | **new, optional** | Set at the two audit choke points from `IRequestContext.CorrelationId`. Matches the correlation id rule in [contracts/http-correlation-and-errors.md](contracts/http-correlation-and-errors.md). Absent on older entries and when no context exists. |
| `Details.After["correlation_id"]` | unchanged | Still the posting-to-application event link (ADR 0033). Never used for request correlation. |

Invariants kept: append-only (`AppendOnlyMongoCollection`), `owner_id` on every entry, UTC `occurred_at`, UUIDv7 `_id`. No update or delete path is added.

### New index

`ix_audit_log_entity`: `(owner_id: 1, entity_type: 1, entity_id: 1, occurred_at: -1, _id: -1)`, created at startup in `MongoBootstrapAndIndexes` with the existing two. The existing `ix_audit_log_chronological` serves unfiltered and actor- or time-filtered listing.

## IRequestContext (Application port) (new)

| Member | Type | Set by |
| --- | --- | --- |
| `CorrelationId` | `string?` | API: correlation middleware, per request. CLI: one run id per process. MCP: one id per tool call. Tests: explicit. |

Scoped. Read only by the Infrastructure audit choke points and the logging scope setup. Recorded in ADR 0041 (Principle IV).

## ListAuditEntriesQuery / AuditListPage (Application) (new)

**Query** (`IQuery<AuditListPage>`):

| Field | Type | Validation |
| --- | --- | --- |
| `OwnerId` | `OwnerId` | required; CLI passes `OwnerId.Default` |
| `EntityType` | `string?` | in the known entity-type set |
| `EntityId` | `string?` | 1 to 128 characters |
| `Actor` | `string?` | 1 to 256 characters |
| `FromUtc`, `ToUtc` | `DateTimeOffset?` | UTC; `FromUtc < ToUtc` when both set |
| `Limit` | `int` | 1 to 100, default 25 |
| `Cursor` | `string?` | decodes to `(occurred_at, _id)`; malformed is `ValidationError` |
| `IncludeChangedFields` | `bool` | default false |

Validation failures return `Result` with `ErrorCode.ValidationError`; an unreachable store returns `DatastoreOffline`. Nothing is thrown for expected failures.

**Result** `AuditListPage`: `Items` (list of `AuditListItem`), `NextCursor` (`string?`).

**`AuditListItem`**: `OccurredAtUtc`, `EntityType`, `EntityId`, `Operation`, `Actor`, `RequestCorrelationId?`, `ChangedFields?` (names only). There is deliberately no field for `AppendedText`, before or after values, or content summaries.

## IAuditLogReader (Application storage port) (new)

`Task<Result<AuditListPage>> ListAsync(ListAuditEntriesQuery query, CancellationToken ct)`. Implemented by `MongoAuditLogReader` (Infrastructure). Always filters by `owner_id`; sorts `(occurred_at desc, _id desc)`; fetches `Limit + 1` to compute `NextCursor`; projects only the listed fields. Gets its own Contract test suite (Principle II): owner isolation, ordering, keyset paging stability while new entries are appended, every filter, and the absence of excluded fields.

## Correlation id (runtime value)

8 to 64 characters, `^[A-Za-z0-9][A-Za-z0-9._-]{7,63}$`. Lives in: the `X-Correlation-ID` header, the web `AsyncLocalStorage` store (`{ nonce, correlationId }`), the .NET logging scope, `IRequestContext`, the error envelope's `correlationId`, log records' `correlationId`, and audit `request_correlation_id`.

## Log record and security event (runtime value)

Defined in [contracts/log-records.md](contracts/log-records.md). Not stored by the application; retained only by the capped container logs (10 MB × 3 files per container).

## Health report (runtime value)

Defined in [contracts/health.md](contracts/health.md): `live` (no dependencies) and `ready` (data store reachable within 2 s), per service.
