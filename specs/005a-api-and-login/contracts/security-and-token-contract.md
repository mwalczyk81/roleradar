# Security & Token Validation Contract

**Feature**: `005a-api-and-login` | **Date**: 2026-10-25

This document establishes the zero-trust token validation rules enforced by `RoleRadar.Api` using `Microsoft.AspNetCore.Authentication.JwtBearer` (version `10.0.14`).

---

## 1. Token Validation Policy Matrix

Every request to an authenticated API endpoint must present a JWT in the `Authorization: Bearer <token>` header. The resource server validates the token against the following strict invariants:

| Validation Rule | Invariant / Expected Shape | Failure Outcome | HTTP Status |
| :--- | :--- | :--- | :---: |
| **Presence** | Header `Authorization: Bearer <token>` supplied | Missing token | 401 Unauthorized |
| **Signature & JWKS** | Valid asymmetric cryptographic signature verified against the authority's JWKS endpoint | Untrusted / modified signature | 401 Unauthorized |
| **Algorithm Allowlist** | Token header `alg` MUST be `RS256` or `ES256` (symmetric algorithms like HS256 and `none` rejected) | Unauthorized algorithm | 401 Unauthorized |
| **Issuer (`iss`)** | Token `iss` claim MUST exactly equal configured `Authentication:Authority` URL | Mismatched issuer | 401 Unauthorized |
| **Audience (`aud`)** | Token `aud` claim MUST equal configured `Authentication:Audience` (`roleradar.api`) | Mismatched audience | 401 Unauthorized |
| **Expiration (`exp`)** | Current UTC time must be less than `exp` + allowed clock skew (max 60 seconds) | Expired token | 401 Unauthorized |
| **Subject Allowlist (`sub`)** | Token `sub` claim MUST be present in configured `Owner:AllowedSubjects` array | Unknown subject identifier | 403 Forbidden |
| **Owner Mapping** | Allowlisted subject token dynamically mapped to `OwnerId.DefaultValue` | N/A | 200 OK (Data Scoped) |

---

## 2. Dynamic Owner Resolution Contract

1. The API registers an `IOwnerIdProvider` implementation: `HttpUserOwnerIdProvider`.
2. For each authenticated HTTP request:
   - Retrieves the `ClaimsPrincipal` from `HttpContext.User`.
   - Reads the `sub` (or `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier`) claim.
   - Verifies against `IOptions<OwnerSecurityOptions>`:
     ```csharp
     if (!_options.AllowedSubjects.Contains(subClaim, StringComparer.Ordinal))
     {
         throw new SecurityException("Subject identifier not allowlisted.");
     }
     ```
   - Returns `OwnerId.DefaultValue` ("local-owner") for all domain operations.
3. This guarantees that:
   - All API queries read data matching the owner ID stored by CLI and MCP tools.
   - All API commands write data scoped to that same owner.
   - The architecture test prohibiting hardcoded owner string literals outside `Domain/Common/OwnerId.cs` remains green.

---

## 3. Cross-Origin Resource Sharing (CORS) Rejection Policy

- `RoleRadar.Api` registers zero CORS middleware.
- When an inbound request includes an `Origin` HTTP header (such as from a browser-hosted web page), the server processes the request without sending `Access-Control-Allow-Origin` or related preflight response headers.
- Standard browser security policies block the calling script from reading the response.

---

## 4. Audit Log Invariant

For every write command dispatched through `RoleRadar.Api`:
- Injected `IActorContext` returns `"api-user"`.
- Command handlers execute inside the transaction-and-audit decorator.
- Append-only audit entry in `audit_log` records:
  - `actor_name`: `"api-user"`
  - `operation_name`: identical to MCP tool equivalent (`posting_transition`, `company_update`, `application_create`, `application_event_append`)
  - `timestamp_utc`: current UTC timestamp
  - `owner_id`: `OwnerId.DefaultValue`
