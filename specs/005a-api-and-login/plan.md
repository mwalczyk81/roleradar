# Implementation Plan: 005a REST API and Duende Identity Host

**Branch**: `005a-api-and-login` | **Date**: 2026-10-25 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/005a-api-and-login/spec.md`

## Summary

Feature 005a delivers an authenticated REST API (`RoleRadar.Api`) over the existing `Application` layer, backed by an isolated OpenID Connect / OAuth 2.0 identity host (`RoleRadar.Identity`) using Duende IdentityServer under Community Edition for single-owner authentication.

Key architectural deliverables:
1. **Dedicated Identity Host (`RoleRadar.Identity`)**:
   - Duende IdentityServer 8.0.10 + ASP.NET Core Identity 10.0.14 backed by an isolated SQLite operational store (`Microsoft.EntityFrameworkCore.Sqlite` 10.0.14).
   - Single-owner authentication via password + TOTP 2FA.
   - Interactive CLI provisioning: `roleradar identity create-owner` displays the TOTP secret and terminal QR code once; zero credentials in files, config, or logs.
   - Automatic cryptographic key management with rotation in the SQLite datastore.
   - 15-minute account lockout after 5 failed attempts; ASP.NET Core 10 rate limiting on login and token endpoints.
   - Token lifetimes: 10m access tokens, 30d rotating refresh tokens with reuse detection, 5m client credentials, 60s authorization codes.
   - Identity store is intentionally excluded from RoleRadar backups (ADR-0015).
   - **Duende Community Edition Compliance & Packaging Constraint (ADR-0016)**:
     - Terms cited from official pages retrieved on October 25, 2026: [Community Edition](https://duendesoftware.com/products/communityedition) and [License Terms](https://duendesoftware.com/license).
     - Eligibility: for-profit under $1M projected annual gross revenue and under $3M capital access; non-profits under $1M budget; personal projects.
     - No license key required for evaluation, development, test environments, or personal projects (an informational startup notice is emitted).
     - A license key is required via an application for production use.
     - Redistribution is excluded: "software you ship to third parties" requires a commercial redistribution license.
     - **Hard Packaging Constraint**: The `roleradar-identity` container image and any package bundling Duende binaries **MUST NEVER be published to a public registry (e.g. Docker Hub, GHCR) or attached to public release assets**; CI builds the image strictly for tests and vulnerability scanning and discards it.
     - The maintainer will verify licensing terms again prior to any public cloud hosting.
2. **Strict Token Validation Resource Server (`RoleRadar.Api`)**:
   - Plain OIDC resource server using `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.14 (zero Duende types).
   - Validates tokens via standard OIDC discovery, exact issuer matching, exact audience matching (`roleradar.api`), algorithm allowlist (RS256, ES256), and small clock skew.
   - Validated `sub` claim must exist in `Owner:AllowedSubjects` (otherwise 403 Forbidden); allowlisted subject maps dynamically to `OwnerId.DefaultValue` ("local-owner") via `HttpUserOwnerIdProvider`.
   - Zero CORS: browser cross-origin requests are rejected (server-to-server only).
3. **Comprehensive REST Endpoints**:
   - ASP.NET Core 10 Minimal APIs with route groups for postings, transitions, companies, applications, timeline events, criteria, notes, and search runs.
   - Thin delivery adapters calling existing `Application` handlers directly in-process; zero Mongo driver types in `RoleRadar.Api`.
   - Bounded pagination (default 50, max 200 items).
   - Optimistic concurrency on transitions via `versionToken`, mapping stale tokens to HTTP 409 Conflict.
   - Audit logging emits append-only entries matching MCP tool operation names (`posting_transition`, `company_update`, etc.) with actor `api-user`.
4. **Build-Time OpenAPI & Typed Client**:
   - Built-in `Microsoft.AspNetCore.OpenApi` 10.0.14 + `Microsoft.Extensions.ApiDescription.Server` 10.0.14 generating `contracts/openapi/roleradar-api.json` with explicit scalar types for all parameters and properties.
   - Committed contract snapshot test in `RoleRadar.Tests.Contract` fails on unreviewed schema drift.
   - Typed TypeScript client generated via `openapi-typescript` for Feature 005b.
5. **Container Packaging, HTTPS Everywhere & Self-Contained Health Probing (ADR-0020)**:
   - Hardened chiseled container images (`mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled`) following Feature 003 conventions (non-root UID, read-only rootfs, tmpfs `/tmp`, digest-pinned bases).
   - HTTPS enforced everywhere via mounted local development certificate (`/https/cert.pfx`).
   - Because chiseled images have no shell and no curl utility, each service implements a **self-contained probe mode** via a `--healthcheck` CLI flag in `Program.cs` that requests `http://127.0.0.1:8080/health` and exits 0 on success, 1 on failure.
   - Exec-form healthchecks in Dockerfile and Compose (`CMD ["/app/RoleRadar.Identity", "--healthcheck"]` and `CMD ["/app/RoleRadar.Api", "--healthcheck"]`).
   - Launched-host test asserts the container reports `healthy` under the supported read-only run mode (`docker run --read-only`).
6. **Provider-Neutral Standard OIDC Test Suite**:
   - End-to-end integration test suite exercising discovery, token issuance, and API operations using strictly standard OIDC claims against `IsolatedContainerMongoFixture` and an ephemeral test identity host; dev Mongo is never touched.

---

## Technical Context

**Language/Version**: C# 13 / .NET 10 (`global.json` pinned to SDK `10.0.402`, `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`).

**Primary Dependencies**:
- `Duende.IdentityServer` version `8.0.10` (restricted to `RoleRadar.Identity` only).
- `Duende.IdentityServer.AspNetIdentity` version `8.0.10`.
- `Duende.IdentityServer.EntityFramework` version `8.0.10`.
- `Microsoft.AspNetCore.Authentication.JwtBearer` version `10.0.14`.
- `Microsoft.AspNetCore.OpenApi` version `10.0.14`.
- `Microsoft.Extensions.ApiDescription.Server` version `10.0.14`.
- `Microsoft.EntityFrameworkCore.Sqlite` version `10.0.14`.
- `Microsoft.AspNetCore.Identity.EntityFrameworkCore` version `10.0.14`.
- Base images: `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled` pinned by multi-arch digest.

**Storage**:
- Domain Store: MongoDB 8.0.34 replica set (unchanged from ADR-0001 / ADR-0008).
- Identity Operational Store: SQLite file volume (`/data/identity/identity.db`) excluded from domain backups.

**Testing Architecture (No New Test Projects)**:
- **Zero new test projects are added**. All test coverage is distributed across the existing four test projects:
  1. `tests/RoleRadar.Tests.Unit`: Unit tests for token validation logic, owner resolution mapping, DTO validation, CLI create-owner interactive parsing, and lockout/rate-limiting calculations.
  2. `tests/RoleRadar.Tests.Architecture`: Architecture tests enforcing inward dependencies, verifying that only `RoleRadar.Identity` references Duende, ensuring zero MongoDB driver types in `RoleRadar.Api`, and confirming zero hardcoded owner string literals outside `Domain/Common/OwnerId.cs`.
  3. `tests/RoleRadar.Tests.Contract`: Contract tests for every REST endpoint (status codes, error envelopes, bounded pagination, 409 concurrency conflicts, omitted company fields), the committed OpenAPI specification snapshot diff test, and launched-host container `--healthcheck` readiness tests.
  4. `tests/RoleRadar.Tests.Integration`: End-to-end tests exercising the provider-neutral OIDC authentication flow against an ephemeral test identity host and `IsolatedContainerMongoFixture` (never touching dev Mongo), validating API vs. MCP data visibility parity and audit entry creation with actor `api-user`.

**Target Platform**: Linux (`linux/amd64` and `linux/arm64`), Windows (`x86_64`), macOS (`arm64`).

**Project Type**: REST API service, OIDC identity host, CLI management commands, container packaging.

**Performance Goals**:
- Token validation latency < 2ms per API request (cached JWKS key set).
- API paged list response < 20ms p95 for 50 items.
- Container cold start to healthy < 4.0 seconds under read-only rootfs.

**Constraints**:
- Read-only root filesystem (`--read-only`) with writable `/tmp` on tmpfs and `/data/identity` volume.
- Zero secrets or credentials in git, images, compose files, or logs.
- Zero Mongo driver types in `RoleRadar.Api`.
- Strict architecture tests enforcing project references.
- Contacts endpoints are strictly forbidden (CLI-only).
- Zero hard-delete endpoints.
- Zero public distribution of the `roleradar-identity` container image or packages bundling Duende.

---

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Requirement | Status | Architecture Justification |
| :--- | :--- | :---: | :--- |
| **I. Privacy First** | No real job/personal data in repo, fixtures, logs, or image layers. Denylist check. | **PASS** | Fictional fixtures only. Secrets supplied via environment or mounted files. CLI interactive setup reads password with masking and renders QR code once to terminal. Denylist hook verified on all new files. |
| **II. Test-First Development** | Tests written before implementation. Ephemeral Testcontainers. Minimum coverage gate (>=85%). | **PASS** | Tests in `RoleRadar.Tests.Integration` use `IsolatedContainerMongoFixture` and ephemeral test identity host; dev Mongo never touched. Domain+Application coverage gate (>=85%) strictly maintained across existing four test projects. |
| **III. Test-Enforced Layering** | Strict inward dependencies. Command handlers wrapped in transaction & audit decorators. | **PASS** | Endpoints delegate directly to `Application` handlers. Architecture tests verify: (1) `RoleRadar.Api` depends only on `Application` and `Domain`; (2) no MongoDB driver types in `RoleRadar.Api`; (3) all command handlers wrapped by transaction & audit decorators. |
| **IV. Named Seams Only** | Aggregate-level repositories, plain command/query handlers, typed failure results. IdP seam. | **PASS** | NetArchTest verifies ONLY `RoleRadar.Identity` references Duende packages. `RoleRadar.Api` is a plain OIDC resource server using standard `JwtBearer`. Switching IdP touches 0 lines of Api, Domain, or Application. |
| **V. Agent Safety & Auditability** | No hard delete endpoints. Untrusted text treated as data. Append-only audit logging. | **PASS** | Zero hard-delete endpoints in API. Posting/note text returned as plain JSON strings. Every write records an append-only audit entry with actor `api-user` and identical MCP tool operation names. |
| **VI. stdio Protocol Hygiene** | stdio host reserved for protocol frames. No listeners on stdio. | **PASS** | Stdio MCP host remains completely untouched and continues running without network listeners. |
| **VII. Data Durability & Recovery** | Persistent volumes. Destructive wipe isolated. Encrypted backups. | **PASS** | Domain MongoDB storage unchanged. Identity SQLite file is stored on an independent volume and explicitly excluded from domain backups per specification (sessions invalidated, recreated via CLI). |
| **VIII. Recorded Decisions** | Decisions recorded in MADRs under `docs/adr/`. Pinned dependencies. | **PASS** | ADR-0015 through ADR-0020 recorded in MADR format. Package versions live-queried and pinned: Duende 8.0.10, JwtBearer 10.0.14, EF Core SQLite 10.0.14. |
| **IX. Technical Conventions** | .NET 10, C#, UTC timestamps, Guid.CreateVersion7 ids, `owner_id`. | **PASS** | Pinned .NET 10 SDK, UTC timestamps, UUIDv7 identifiers, owner allowlist dynamically mapped to `OwnerId.DefaultValue` ("local-owner") without string literals. |

---

## Project Structure

### Documentation (this feature)

```text
specs/005a-api-and-login/
├── plan.md              # Implementation plan (this file)
├── research.md          # Phase 0 technical research & decisions
├── data-model.md        # Phase 1 SQLite schema & API transport DTO models
├── quickstart.md        # Phase 1 local setup & validation guide
├── checklists/
│   └── requirements.md  # Specification quality checklist
└── contracts/           # Phase 1 interface & execution contracts
    ├── api-endpoints-contract.md
    ├── identity-endpoints-contract.md
    ├── security-and-token-contract.md
    └── openapi-snapshot-contract.md
```

### Source Code Changes (Repository Layout)

```text
contracts/
├── openapi/
│   └── roleradar-api.json         # Build-generated OpenAPI v3.1 specification
└── client/
    └── api-types.ts               # Generated typed TypeScript client for 005b

src/
├── RoleRadar.Domain/              # Unchanged domain logic
├── RoleRadar.Application/         # Unchanged command/query handlers
├── RoleRadar.Infrastructure/      # Unchanged MongoDB persistence
├── RoleRadar.Mcp.Stdio/           # Unchanged stdio MCP host
├── RoleRadar.Cli/                 # Extended with `identity create-owner` command
│   └── Commands/
│       └── Identity/
│           └── CreateOwnerCommand.cs
├── RoleRadar.Identity/            # NEW: Dedicated Duende OIDC Identity host
│   ├── Configuration/
│   │   ├── ClientsConfiguration.cs
│   │   └── IdentityServerConfig.cs
│   ├── Data/
│   │   └── IdentityAppDbContext.cs
│   ├── Pages/Account/
│   │   └── Login.cshtml / Login.cshtml.cs
│   ├── Services/
│   │   └── TotpAuthenticationService.cs
│   └── Program.cs                 # Supports `--healthcheck` self-contained probe
└── RoleRadar.Api/                 # NEW: Plain OIDC REST resource server
    ├── Authentication/
    │   ├── ApiOwnerIdProvider.cs
    │   └── OwnerSecurityOptions.cs
    ├── Endpoints/
    │   ├── PostingsEndpoints.cs
    │   ├── CompaniesEndpoints.cs
    │   ├── ApplicationsEndpoints.cs
    │   ├── CriteriaEndpoints.cs
    │   ├── NotesEndpoints.cs
    │   └── SearchRunsEndpoints.cs
    ├── Envelopes/
    │   ├── PagedResult.cs
    │   └── ErrorEnvelope.cs
    └── Program.cs                 # Supports `--healthcheck` self-contained probe

tests/ (Zero new test projects; tests added to existing four projects)
├── RoleRadar.Tests.Unit/          # Token validation logic, owner mapping, DTO tests
├── RoleRadar.Tests.Architecture/  # Project dependencies, Duende seam, zero Mongo in Api
├── RoleRadar.Tests.Contract/      # Endpoint contracts, 409 conflicts, OpenAPI snapshot, healthcheck probe tests
└── RoleRadar.Tests.Integration/   # Testcontainers Mongo + test IdP end-to-end suite

deploy/
├── Dockerfile.identity            # Hardened chiseled identity host image with exec-form healthcheck
├── Dockerfile.api                 # Hardened chiseled REST API image with exec-form healthcheck
└── docker-compose.apps.yml        # Compose overlay adding identity and api services with exec healthchecks
```

---

## Architecture Decision Records (ADRs)

The following six ADRs govern Feature 005a implementation:

1. [ADR-0015: Identity Operational Store Selection — SQLite File Volume vs. PostgreSQL Container](../../docs/adr/0015-identity-operational-store-selection.md)
2. [ADR-0016: Duende IdentityServer Community Edition Licensing and IdP Fallback Strategy](../../docs/adr/0016-duende-identityserver-community-edition-and-fallback.md)
3. [ADR-0017: Single-Owner Credential Handling and Authentication Security Controls](../../docs/adr/0017-owner-credential-and-security-controls.md)
4. [ADR-0018: REST API Style, Error Envelope, Paging, and Owner Mapping](../../docs/adr/0018-rest-api-style-and-owner-mapping.md)
5. [ADR-0019: OpenAPI Contract Generation, Committed Snapshot Verification, and Typed Client Generation](../../docs/adr/0019-openapi-generation-and-typed-client.md)
6. [ADR-0020: Transport Security, Local Compose Certificates, and Self-Contained Health Probing](../../docs/adr/0020-https-transport-and-local-compose-certificates.md)

---

## Complexity Tracking

No constitution violations exist. All gates pass without requiring complexity exceptions.
