# Technical Research & Architecture Decisions: 005a REST API and Duende Identity Host

**Feature**: `005a-api-and-login` | **Date**: 2026-10-25

## Executive Summary

Feature 005a introduces an authenticated REST API (`RoleRadar.Api`) over the existing Application layer, backed by an isolated OpenID Connect / OAuth 2.0 identity host (`RoleRadar.Identity`) using Duende IdentityServer for single-owner authentication.

All design decisions adhere strictly to Constitution Principles I–IX, especially:
- **Principle I (Privacy First)**: Zero credentials, private tokens, or license keys in source control, container images, or logs.
- **Principle III (Test-Enforced Layering)**: Endpoints act as thin delivery adapters calling existing `Application` command and query handlers directly in-process; no Mongo driver types in the API project.
- **Principle IV (Named Seams Only)**: Only `RoleRadar.Identity` references Duende packages; `RoleRadar.Api` is a plain OIDC resource server using standard `Microsoft.AspNetCore.Authentication.JwtBearer`.
- **Principle V (Agent Safety & Auditability)**: Zero hard-delete endpoints; append-only audit entries match MCP tool operations with actor `api-user`.

---

## 1. Identity Operational Store Selection

- **Decision**: SQLite file volume via Entity Framework Core (`Microsoft.EntityFrameworkCore.Sqlite` version `10.0.14`).
- **Rationale**:
  - RoleRadar domain data is stored strictly in MongoDB (ADR-0001).
  - The identity store holds only one owner account, transient grants, and automatic signing keys.
  - Per specification, the identity datastore is **excluded from RoleRadar domain backups**: losing it invalidates active sessions without compromising domain data, and the single owner is easily re-provisioned via CLI.
  - SQLite requires zero extra container processes, ports, or network listeners in Docker Compose, maintaining local-first simplicity.
- **Alternatives Considered**:
  - *PostgreSQL Container*: Rejected due to unnecessary resource overhead (~150MB memory) and operational complexity for a single-owner system.
  - *MongoDB Store for Identity*: Rejected to prevent coupling identity lifecycle with domain schema backups.
- **Governing ADR**: [ADR-0015](../../docs/adr/0015-identity-operational-store-selection.md).

---

## 2. Duende IdentityServer Community Edition & Fallback Strategy

- **Decision**: Adopt `Duende.IdentityServer` version `8.0.10` under the free Community Edition license for personal projects, isolated strictly behind an architecture boundary.
- **Rationale**:
  - RoleRadar is a personal project by an individual maintainer and meets Community Edition criteria (<$1M annual gross revenue).
  - No license key is required for development/testing; production keys (if applicable) are passed strictly via environment variables.
  - The fallback IdP is **OpenIddict** (Apache 2.0).
  - An automated NetArchTest architecture test ensures that only `RoleRadar.Identity` references Duende packages.
  - An IdP-agnostic end-to-end integration test suite executes strictly against standard discovery (`.well-known/openid-configuration`), standard token endpoints, and standard JWT claims.
- **Alternatives Considered**:
  - *Direct OpenIddict implementation*: Duende was selected for this milestone to provide mature out-of-the-box key management and PKCE workflows, while retaining zero-cost swapability.
- **Governing ADR**: [ADR-0016](../../docs/adr/0016-duende-identityserver-community-edition-and-fallback.md).

---

## 3. Owner Credential Handling, 2FA, and Security Controls

- **Decision**: ASP.NET Core Identity (`Microsoft.AspNetCore.Identity.EntityFrameworkCore` version `10.0.14`) with password + TOTP 2FA, interactive CLI setup, lockout, and built-in rate limiting.
- **Rationale**:
  - Single owner is provisioned via CLI command: `roleradar identity create-owner`. Prompts for password interactively and prints the TOTP secret and terminal QR code once.
  - Passwords hashed with framework default PBKDF2/HMAC-SHA512.
  - Account lockout after 5 consecutive failed attempts for 15 minutes (`LockoutOptions`).
  - Rate limiting via ASP.NET Core 10 `AddRateLimiter` (10 requests/minute fixed window per IP on login and token endpoints).
  - Token lifetimes:
    - Access token: 10 minutes
    - Refresh token: 30 days absolute, rotating, one-time use with reuse detection
    - Client credentials token: 5 minutes
    - Authorization code: 60 seconds
- **Governing ADR**: [ADR-0017](../../docs/adr/0017-owner-credential-and-security-controls.md).

---

## 4. REST API Style, Error Envelope, and Owner Mapping

- **Decision**: ASP.NET Core 10 Minimal APIs with route groups, consistent error envelope, bounded paging, and dynamic owner resolution via `IOwnerIdProvider`.
- **Rationale**:
  - Minimal APIs offer minimal memory overhead and native integration with build-time OpenAPI.
  - Concurrency conflicts on posting transitions translate to HTTP 409 Conflict.
  - Bounded pagination: default 50 items, max 200 items.
  - Dynamic owner mapping: validated `sub` claim is checked against `Owner:AllowedSubjects` and mapped to `OwnerId.DefaultValue` via `HttpUserOwnerIdProvider`. Zero string literals for owner IDs outside `Domain/Common/OwnerId.cs`.
  - Audit logging records exact MCP operation names with actor `api-user`.
- **Governing ADR**: [ADR-0018](../../docs/adr/0018-rest-api-style-and-owner-mapping.md).

---

## 5. OpenAPI Generation, Committed Contract Snapshot, and Client Generation

- **Decision**: Built-in .NET 10 OpenAPI (`Microsoft.AspNetCore.OpenApi` version `10.0.14`) + `Microsoft.Extensions.ApiDescription.Server` (version `10.0.14`) + Contract Snapshot Test + `openapi-typescript`.
- **Rationale**:
  - Build-time generation creates `contracts/openapi/roleradar-api.json` without runtime reflection.
  - Explicit scalar types declared on every parameter and property to prevent client deserialization issues.
  - Committed contract snapshot test in `RoleRadar.Tests.Contract` fails on unreviewed schema changes.
  - `openapi-typescript` generates strongly typed TypeScript definitions for Feature 005b.
- **Governing ADR**: [ADR-0019](../../docs/adr/0019-openapi-generation-and-typed-client.md).

---

## 6. Transport Security, Local Certificates, and Health Checks

- **Decision**: HTTPS everywhere via mounted development certificates, fixed issuer URL, internal health probe listener, and zero CORS.
- **Rationale**:
  - Eliminates unencrypted token transmissions across all environments.
  - Multi-binding Kestrel configuration binds HTTPS externally and an anonymous plain HTTP probe internally on `http://localhost:8080/health`.
  - CORS is completely disabled (rejected) as the API is strictly called server-to-server.
- **Governing ADR**: [ADR-0020](../../docs/adr/0020-https-transport-and-local-compose-certificates.md).
