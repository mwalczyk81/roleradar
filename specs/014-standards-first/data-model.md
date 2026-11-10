# Data Model: Standards-first cleanup (014)

One new stored entity, two changed values, and the wire shapes that replace the old envelope. Nothing is added to MongoDB.

## Used authenticator code (new, Identity store, P1)

The memory that a one-time code was accepted, so the same code is rejected a second time (spec FR-015, [research R-003](research.md)).

| Field | Type | Notes |
| --- | --- | --- |
| `UserId` | Guid | The Identity user (the single owner). Foreign key to `AspNetUsers`, cascade on delete. |
| `OwnerId` | Guid | Required, equal to the account's user id. Carried because Principle IX requires `owner_id` on every table we create. Framework-owned ASP.NET Identity tables are exempt (we do not create them and cannot add columns without forking the schema). |
| `CodeHash` | string (64 hex) | HMAC-SHA-256 of the submitted code, keyed by the user's authenticator key. Holds no code in readable form. A key rotation makes old rows unmatchable, which is correct. |
| `AcceptedAtUtc` | DateTimeOffset (UTC) | When the code was accepted. |
| `ExpiresAtUtc` | DateTimeOffset (UTC) | `AcceptedAtUtc` plus 5 minutes. A code is valid for at most 150 seconds after acceptance, so 5 minutes covers it with margin. |

**Identity**: primary key (`UserId`, `CodeHash`). The primary key is the unique constraint: two simultaneous attempts both pass the provider and race on the insert, and exactly one insert succeeds. The loser is a replay.

**Lifecycle**: inserted only after the provider has accepted a code. Rows with `ExpiresAtUtc` in the past are removed by the next insert (no background job). Never updated. The table is created by a new EF Core migration applied at Identity startup (ADR 0015's mechanism).

**Not stored**: the code, the authenticator key, the submitted username. The table is never logged.

**Rules**:
- An attempt is a replay when the provider accepts the code and the insert conflicts. The page then treats it exactly like a wrong code: `AccessFailedAsync`, event 5004, the lockout check.
- Enrolment activation goes through the same guard, so the code that activated the account cannot sign in immediately (spec edge case).
- A rejected code (provider says no) is not recorded.

## Second-factor secret (unchanged)

`AspNetUserTokens` row, provider `[AspNetUserStore]`, name `AuthenticatorKey`, Base32. Same value, same format. New enrolments get their key from `UserManager.GenerateNewAuthenticatorKey()` instead of `OwnerProvisioningService.GenerateBase32Secret`. The enrolment URI is built by Otp.NET `OtpUri` in the same `otpauth://totp/RoleRadar:<user>?secret=...&issuer=RoleRadar` form.

## Trace id (replaces the correlation id, P2)

| Aspect | Value |
| --- | --- |
| Form | 32 lowercase hexadecimal characters, not all zero (W3C Trace Context `trace-id`). |
| Source | `Activity.Current.TraceId` in the API and Identity; `ActivityTraceId.CreateRandom()` for a CLI run or an MCP call; `@opentelemetry/api` context in the web. |
| Where it appears | Log records (field `traceId`), the problem document (`traceId`), `request_correlation_id` on new audit entries, the CLI run id, the MCP per-call id, and the outbound `traceparent` header. |
| Where it does not appear | A response header. |

## Audit entry (value change only, P2)

| Field | Before | After |
| --- | --- | --- |
| `request_correlation_id` | The `X-Correlation-ID` value (8 to 64 characters, UUIDv7 text when generated) | The 32-character trace id for new entries. Old entries keep their stored values and stay listable. |
| `details.after.correlation_id` | ADR 0033 link between a posting event and the application event it caused | Unchanged. A different concept. |

The field name is not renamed: entries are append-only, and a rename would mean migrating stored data. The naming debt is recorded in ADR 0044.

## Problem document (replaces `ErrorEnvelope`, P2)

Full contract in [contracts/problem-details.md](contracts/problem-details.md).

| Old member | New member |
| --- | --- |
| `ok: false` | the HTTP status, and `status` in the body |
| `errorCode` | `errorCode` (extension member) and `type` (documentation URL built from it) |
| `message` | `detail` (and a fixed `title` per status) |
| `details[{ field, issue }]` | `errors`: `{ "<field>": ["<issue>", ...] }` (400 validation only) |
| `correlationId` | `traceId` (the bare trace id, not `Activity.Id`) |

## Web outbound policy (P3)

No stored data. The values are in [contracts/web-outbound-policy.md](contracts/web-outbound-policy.md).

## Decision record (ADR)

Seven records, listed in the plan: 0042 (P1), 0043 (backup), 0044 (P2), 0045 (P3), 0046 (redaction), 0047 (console formatter and validators), 0048 (scripts). Each names the standard compared. `docs/adr/README.md` is updated in the same stage.
