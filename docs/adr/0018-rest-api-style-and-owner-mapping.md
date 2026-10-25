# ADR 0018: REST API Style, Error Envelope, Paging, and Owner Mapping

- **Status**: Accepted
- **Date**: 2026-10-25
- **Feature**: `005a-api-and-login`

## Context and Problem Statement

Feature 005a delivers the REST API (`RoleRadar.Api`) serving data to the future web management user interface (Feature 005b) and headless clients.

Per Constitution Principle III (Test-Enforced Layering), delivery adapters (MCP tools, CLI commands, REST endpoints) must invoke `Application` command and query handlers directly in-process rather than reimplementing business logic or executing raw database queries.

Key design questions include:

1. **API Style**: Minimal APIs vs. MVC Controllers.
2. **Error Envelope**: How failures (validation, concurrency conflicts, not found) are returned to callers.
3. **Paging Bounds**: How list endpoints constrain result sets to maintain predictable memory and network overhead.
4. **Owner Resolution**: How validated JWT `sub` claims map securely to domain entities without introducing hardcoded string literals across the codebase.
5. **Audit Traceability**: How API-driven writes produce append-only audit records matching existing MCP tool standards.

## Decision Drivers

- **Constitution Principle III (Test-Enforced Layering)**: Endpoints must be thin delivery adapters delegating immediately to `ICommandHandler` and `IQueryHandler`.
- **Zero Raw Driver Types**: No MongoDB driver types (BsonDocument, IMongoDatabase) may appear in `RoleRadar.Api`.
- **Owner Consistency**: All stored data uses `OwnerId.DefaultValue` ("local-owner"). The architecture test verifying zero "default-owner" or "local-owner" string literals outside `Domain/Common/OwnerId.cs` must remain green.
- **Audit Parity**: Write operations must emit audit entries with the exact same operation names as MCP tools, differentiated solely by the actor identity (`api-user`).

## Decision Outcome

Chosen design: **ASP.NET Core 10 Minimal APIs + Route Groups + Shared Owner Provider + Bounded Pagination Envelope**.

### 1. Endpoint Architecture: Minimal APIs with Route Groups

Minimal APIs are selected over MVC Controllers:

- Lower overhead, faster startup, and reduced memory footprint.
- Native integration with ASP.NET Core 10 OpenAPI type generation (`WithOpenApi()`).
- Modular organization through typed route extension classes:
  - `PostingsEndpoints.MapPostingsEndpoints(app)`
  - `CompaniesEndpoints.MapCompaniesEndpoints(app)`
  - `ApplicationsEndpoints.MapApplicationsEndpoints(app)`
  - `CriteriaEndpoints.MapCriteriaEndpoints(app)`
  - `NotesEndpoints.MapNotesEndpoints(app)`
  - `SearchRunsEndpoints.MapSearchRunsEndpoints(app)`

Each endpoint delegates directly to an injected `ICommandHandler<TCommand, TResult>` or `IQueryHandler<TQuery, TResult>`.

### 2. Error Envelope

All error responses adhere to a consistent JSON envelope schema:

```json
{
  "ok": false,
  "errorCode": "validation_error",
  "message": "Specific error explanation",
  "details": [
    {
      "field": "disposition",
      "issue": "Invalid disposition value 'unknown_status'"
    }
  ]
}
```

- **Validation Failures**: HTTP 400 Bad Request with `errorCode: "validation_error"`.
- **Concurrency Conflicts**: When a command handler returns a version mismatch failure (`ExpectedFailure`), the endpoint translates this to HTTP 409 Conflict with `errorCode: "concurrency_conflict"`.
- **Resource Not Found**: HTTP 404 Not Found with `errorCode: "not_found"`.
- **Unauthorized / Forbidden**: HTTP 401 Unauthorized / HTTP 403 Forbidden with standard `WWW-Authenticate` headers.

### 3. Bounded Paging

List endpoints (`GET /api/v1/postings`, `GET /api/v1/companies`, `GET /api/v1/applications`) accept `limit` and `offset` query parameters:

- **Default Limit**: 50 items.
- **Maximum Limit**: 200 items (enforced; requests specifying `limit > 200` are clamped to 200 or rejected with HTTP 400).
- **Paged Envelope**:

  ```json
  {
    "items": [...],
    "totalCount": 232,
    "limit": 50,
    "offset": 0
  }
  ```

### 4. Owner Mapping via Injected `IOwnerIdProvider`

To comply with the architecture test forbidding string literals for owner IDs outside `Domain/Common/OwnerId.cs`:

1. An `IOwnerIdProvider` implementation (`HttpUserOwnerIdProvider`) inspects the incoming `ClaimsPrincipal`.
2. Validates that the token's `sub` claim exists in the configured `Owner:AllowedSubjects` collection.
3. If valid, resolves the owner identifier dynamically via `OwnerId.DefaultValue`.
4. If invalid or missing, authentication/authorization fails with HTTP 403 Forbidden before handler invocation.

Zero hardcoded string literals are used, guaranteeing that imported data from CLI/MCP is visible through the API for the same owner.

### 5. Audit Logging Parity

All commands dispatched from `RoleRadar.Api` set `Actor = "api-user"`. The underlying command handlers (wrapped by the transaction-and-audit decorator per Constitution Principle III) record append-only audit entries using the established operation names:

- `posting_transition`
- `company_update`
- `application_create`
- `application_event_append`

## Consequences

- **Good**: Perfect behavioural and audit parity between REST endpoints and MCP tools.
- **Good**: Strict separation of concerns; zero Mongo types in API layer.
- **Good**: Paging bounds prevent unbounded query memory consumption.
- **Good**: Architecture tests guarantee clean layer boundaries and zero owner literal duplication.
