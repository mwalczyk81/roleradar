# Data Model: 005a REST API and Duende Identity Host

**Feature**: `005a-api-and-login` | **Date**: 2026-10-25

## Overview

Feature 005a establishes data models across two distinct storage and boundary contexts:
1. **Identity Operational Datastore (SQLite)**: User accounts, credentials, TOTP authenticators, signing keys, and active refresh token grants.
2. **REST API Transport Layer (JSON DTOs)**: Strongly-typed request payloads, response models, paging envelopes, and error representations mapping to domain entities.

The primary domain aggregates remain in MongoDB as defined in Feature 001 and Feature 002.

---

## 1. Identity Operational Datastore (SQLite Schema)

Managed via EF Core migrations in `RoleRadar.Identity` using `IdentityAppDbContext`.

### `AspNetUsers`
Represents the single owner account.

| Column | Type | Constraints | Description |
| :--- | :--- | :--- | :--- |
| `Id` | `TEXT (GUID)` | Primary Key | Time-ordered GUID (UUIDv7) identifier |
| `UserName` | `TEXT` | NOT NULL, Unique | Normalized username |
| `NormalizedUserName` | `TEXT` | NOT NULL, Unique | Uppercase normalized username |
| `PasswordHash` | `TEXT` | NOT NULL | PBKDF2 with HMAC-SHA512 password hash |
| `SecurityStamp` | `TEXT` | NOT NULL | Random token updated on credential change |
| `ConcurrencyStamp` | `TEXT` | NOT NULL | Optimistic concurrency token |
| `TwoFactorEnabled` | `INTEGER` | NOT NULL | Set to `1` (TOTP enabled) |
| `LockoutEnd` | `TEXT` | Nullable | UTC timestamp when lockout expires |
| `LockoutEnabled` | `INTEGER` | NOT NULL | Set to `1` (lockout active) |
| `AccessFailedCount` | `INTEGER` | NOT NULL | Consecutive failed attempts (resets on success) |

### `AspNetUserTokens`
Stores the TOTP authenticator key.

| Column | Type | Constraints | Description |
| :--- | :--- | :--- | :--- |
| `UserId` | `TEXT (GUID)` | FK to `AspNetUsers.Id` | Owner reference |
| `LoginProvider` | `TEXT` | Primary Key (Composite) | `"[AspNetUserStore]"` |
| `Name` | `TEXT` | Primary Key (Composite) | `"AuthenticatorKey"` |
| `Value` | `TEXT` | NOT NULL | Base32 encoded TOTP shared secret |

### `DeviceCodes`, `PersistedGrants`, `Keys`
Standard Duende IdentityServer operational tables:
- **`PersistedGrants`**: Stores rotating refresh tokens and authorization codes with `Key`, `Type`, `SubjectId`, `ClientId`, `CreationTime`, `Expiration`, `ConsumedTime`, and `Data`.
- **`Keys`**: Automatic key management table storing active and rotated cryptographic signing keys (RS256 / ES256).

---

## 2. API Transport Envelopes & Models (JSON)

### Paged Collection Envelope (`PagedResult<T>`)

```json
{
  "items": [],
  "totalCount": 232,
  "limit": 50,
  "offset": 0
}
```

- `items`: Array of strongly-typed items.
- `totalCount`: Total number of matching records across all pages (integer >= 0).
- `limit`: Page size limit (clamped between 1 and 200).
- `offset`: Zero-based item offset.

### Consistent Error Envelope (`ErrorEnvelope`)

```json
{
  "ok": false,
  "errorCode": "validation_error",
  "message": "The request payload failed validation.",
  "details": [
    {
      "field": "disposition",
      "issue": "Invalid disposition value."
    }
  ]
}
```

- `ok`: Always `false` for error responses.
- `errorCode`: String identifier matching domain/MCP error codes (`validation_error`, `concurrency_conflict`, `not_found`, `unauthorized`, `forbidden`).
- `message`: Sanitized human-readable error summary.
- `details`: Optional list of specific field-level validation errors.

---

## 3. Resource DTOs

### Postings

#### `PostingListItemDto`
```json
{
  "id": "0192e4a1-0000-7000-8000-000000000001",
  "companyId": "0192e4a0-0000-7000-8000-000000000001",
  "companyName": "Acme Corp",
  "title": "Staff Software Engineer",
  "requisitionId": "19505",
  "location": "Remote - US",
  "disposition": "screened_out",
  "resurfacePolicy": "if_reopened",
  "score": 85,
  "versionToken": "0192e4a1-0000-7000-8000-000000000002",
  "createdAt": "2026-10-25T12:00:00Z",
  "updatedAt": "2026-10-25T14:30:00Z"
}
```

#### `PostingDetailDto`
Extends `PostingListItemDto` with:
- `url`: Posting URL.
- `compensation`: Compensation object (`currency`, `rawText`, `hasSalary`, `compHighPriority`).
- `signals`: Array of `PostingSignalDto` (`signalType`, `evidence`, `detectedAt`).
- `assessmentNote`: Optional linked `AssessmentNoteSummaryDto` (`id`, `summary`, `updatedAt`).

#### `TransitionPostingRequestDto`
```json
{
  "toDisposition": "interviewing",
  "reason": "Passed technical screen with hiring manager",
  "criterionId": "0192e4a0-0000-7000-8000-000000000010",
  "versionToken": "0192e4a1-0000-7000-8000-000000000002"
}
```

### Companies

#### `CompanyListItemDto` / `CompanyDetailDto`
```json
{
  "id": "0192e4a0-0000-7000-8000-000000000001",
  "name": "Acme Corp",
  "status": "neutral",
  "ownershipType": "public",
  "description": "Enterprise cloud services",
  "headquarters": "Austin, TX",
  "categoryTags": ["saas", "infrastructure"],
  "careerSources": [
    {
      "sourceType": "ats",
      "atsProvider": "greenhouse",
      "atsTenant": "acme",
      "sourceUrl": "https://boards.greenhouse.io/acme",
      "fetchMethod": "api",
      "active": true
    }
  ]
}
```

#### `UpdateCompanyRequestDto`
All fields nullable/omissible; omitted fields remain unchanged on update.
```json
{
  "status": null,
  "ownershipType": null,
  "description": null,
  "headquarters": null,
  "categoryTags": null,
  "careerSources": [
    {
      "sourceType": "ats",
      "atsProvider": "greenhouse",
      "atsTenant": "acme",
      "sourceUrl": "https://boards.greenhouse.io/acme",
      "fetchMethod": "api",
      "active": true
    }
  ]
}
```

### Applications & Timeline Events

#### `ApplicationDetailDto`
```json
{
  "id": "0192e4a2-0000-7000-8000-000000000001",
  "postingId": "0192e4a1-0000-7000-8000-000000000001",
  "companyId": "0192e4a0-0000-7000-8000-000000000001",
  "status": "submitted",
  "appliedAt": "2026-10-20T10:00:00Z",
  "events": [
    {
      "eventType": "applied",
      "eventDate": "2026-10-20T10:00:00Z",
      "notes": "Submitted via company portal"
    }
  ]
}
```

### Criteria, Notes, and Search Runs

- **`CriterionDto`**: `id`, `name`, `weight`, `ruleText`, `effectiveFrom`, `effectiveTo`, `isActive`.
- **`NoteDto`**: `id`, `heading`, `content`, `tags`, `linkedEntityType`, `linkedEntityId`, `createdAt`, `updatedAt`.
- **`SearchRunDto`**: `id`, `runDate`, `companiesSearched`, `postingsFound`, `newPostings`, `errorsCount`, `summary`.
