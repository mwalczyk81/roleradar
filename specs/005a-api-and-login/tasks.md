# Tasks: 005a REST API and Duende Identity Host

**Input**: Design documents from `specs/005a-api-and-login/` (`plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/`, `quickstart.md`).

**Prerequisites**: `plan.md` (required), `spec.md` (required for user stories), `research.md`, `data-model.md`, `contracts/`.

**Tests**: Tests are MANDATORY per Constitution Principle II (Test-First Development). For every user story, test tasks MUST come first, be marked as expected to fail, and precede all implementation tasks.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story. All test tasks are distributed strictly across the four existing test projects (`RoleRadar.Tests.Unit`, `RoleRadar.Tests.Architecture`, `RoleRadar.Tests.Contract`, `RoleRadar.Tests.Integration`); no new test projects are created.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g. US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Projects & Architecture Seams)

**Purpose**: Project initialization, central package management, and architecture seam enforcement.

- [X] T001 Register dependencies centrally in Directory.Packages.props: Duende.IdentityServer 8.0.10, Duende.IdentityServer.AspNetIdentity 8.0.10, Duende.IdentityServer.EntityFramework 8.0.10, Microsoft.AspNetCore.Authentication.JwtBearer 10.0.14, Microsoft.AspNetCore.OpenApi 10.0.14, Microsoft.Extensions.ApiDescription.Server 10.0.14, Microsoft.EntityFrameworkCore.Sqlite 10.0.14, Microsoft.AspNetCore.Identity.EntityFrameworkCore 10.0.14
- [X] T002 [P] Create identity host project in src/RoleRadar.Identity/RoleRadar.Identity.csproj referencing Duende packages, EF Core SQLite, and ASP.NET Core Identity with Nullable and TreatWarningsAsErrors enabled
- [X] T003 [P] Create REST API project in src/RoleRadar.Api/RoleRadar.Api.csproj referencing RoleRadar.Application, RoleRadar.Domain, Microsoft.AspNetCore.Authentication.JwtBearer, Microsoft.AspNetCore.OpenApi, and Microsoft.Extensions.ApiDescription.Server (zero Duende, zero Mongo driver)
- [X] T004 Add RoleRadar.Identity and RoleRadar.Api to the solution in RoleRadar.slnx
- [X] T005 Add architecture tests in tests/RoleRadar.Tests.Architecture/LayerDependencyArchitectureTests.cs asserting: only RoleRadar.Identity references Duende packages; RoleRadar.Api references RoleRadar.Infrastructure for composition only with zero dependencies on MongoDB.* driver types in Api types; zero hardcoded owner ID string literals exist outside Domain/Common/OwnerId.cs
- [X] T006 Measured Checkpoint: Run scripts/test-fast.ps1 and verify the solution compiles and all architecture seam tests pass

---

## Phase 2: Foundational (Identity Store & Dynamic Owner Resolution)

**Purpose**: Core infrastructure that MUST be complete before user stories can execute.

**⚠️ CRITICAL**: Blocks all user stories.

- [X] T007 [P] Implement IdentityAppDbContext in src/RoleRadar.Identity/Data/IdentityAppDbContext.cs inheriting from IdentityDbContext<IdentityUser<Guid>, IdentityRole<Guid>, Guid> and implementing Duende IPersistedGrantDbContext and IConfigurationDbContext
- [X] T008 [P] Implement EF Core SQLite initial migrations in src/RoleRadar.Identity/Data/Migrations/ for AspNetUsers, AspNetUserTokens, PersistedGrants, and Keys tables
- [X] T009 [P] Implement OwnerSecurityOptions in src/RoleRadar.Api/Authentication/OwnerSecurityOptions.cs configuring AllowedSubjects string array
- [X] T010 Implement HttpUserOwnerIdProvider in src/RoleRadar.Api/Authentication/HttpUserOwnerIdProvider.cs implementing Domain.Common.IOwnerIdProvider, validating the token sub claim against AllowedSubjects and dynamically resolving OwnerId.DefaultValue without string literals
- [X] T011 [P] Unit tests for HttpUserOwnerIdProvider in tests/RoleRadar.Tests.Unit/Authentication/HttpUserOwnerIdProviderTests.cs asserting allowlisted subject returns OwnerId.DefaultValue and non-allowlisted subject throws a security exception
- [X] T012 Measured Checkpoint: Run scripts/test-fast.ps1 and verify foundational database and owner resolution tests pass

---

## Phase 3: User Story 1 - Dedicated Identity Host for Single Owner Authentication (Priority: P1) 🎯 MVP

**Goal**: Dedicated Duende IdentityServer host authenticating the single owner via password + TOTP 2FA, CLI owner creation, rate limiting, lockout, and token issuance with rotating refresh tokens and reuse detection.

**Independent Test**: Provision an owner via CLI, perform authorization code with PKCE and client credentials grant, verify token lifetimes, and assert that 5 consecutive failed logins trigger a 15-minute lockout.

### Tests for User Story 1 (MANDATORY - Test-First per Principle II) ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [X] T013 [P] [US1] Unit test for owner provisioning logic and PBKDF2 password hashing in tests/RoleRadar.Tests.Unit/Identity/OwnerProvisioningTests.cs (expected to fail before implementation)
- [X] T014 [P] [US1] Unit test for 15-minute lockout after 5 consecutive failures and rate-limiting policy in tests/RoleRadar.Tests.Unit/Identity/LockoutAndRateLimitingTests.cs (expected to fail before implementation)
- [X] T015 [P] [US1] Contract test for client credentials grant and token lifetimes (5m CC, 10m access, 60s auth code) in tests/RoleRadar.Tests.Contract/Identity/IdentityTokenIssuanceContractTests.cs (expected to fail before implementation)
- [X] T016 [P] [US1] Contract test for refresh token rotation, one-time use, and reuse detection in tests/RoleRadar.Tests.Contract/Identity/RefreshTokenReuseContractTests.cs (expected to fail before implementation)

### Implementation for User Story 1

- [X] T017 [US1] Implement interactive identity create-owner command in src/RoleRadar.Cli/Commands/Identity/CreateOwnerCommand.cs with masked password prompt, TOTP secret generation, single-display ASCII terminal QR code, and zero credentials in files/logs
- [X] T018 [US1] Configure Duende IdentityServer in src/RoleRadar.Identity/Configuration/IdentityServerConfig.cs with confidential web client (PKCE, secret, exact redirect URIs, 10m access, 30d rotating refresh token), headless client (5m access, narrow scopes), and automatic key management with rotation
- [X] T019 [US1] Implement login endpoint and Razor page in src/RoleRadar.Identity/Pages/Account/Login.cshtml and Login.cshtml.cs with password verification, TOTP challenge, 5-failure lockout, and rate limiting
- [X] T020 [US1] Wire startup, services, rate limiter (AddRateLimiter), and EF Core SQLite migration in src/RoleRadar.Identity/Program.cs
- [X] T021 [US1] Measured Checkpoint: Run scripts/test-fast.ps1 plus User Story 1 tests and report measured test counts

---

## Phase 4: User Story 2 - Strict Token Validation & Owner-Scoping Resource Server (Priority: P1)

**Goal**: Zero-trust token validation resource server in RoleRadar.Api validating discovery metadata, issuer, audience, algorithm allowlist (RS256/ES256), expiration, owner sub allowlist, and zero-CORS rejection.

**Independent Test**: Issue HTTP requests with missing, expired, wrong-issuer, wrong-audience, wrong-algorithm, and non-allowlisted subject tokens, asserting expected 401 Unauthorized, 403 Forbidden, and rejected CORS responses.

### Tests for User Story 2 (MANDATORY - Test-First per Principle II) ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [X] T022 [P] [US2] Contract test for token validation matrix in tests/RoleRadar.Tests.Contract/Authentication/TokenValidationMatrixContractTests.cs: wrong issuer (401), wrong audience (401), unauthorized algorithm (401), expired token (401), missing token (401), non-allowlisted subject (403), valid allowlisted subject (200) (expected to fail before implementation)
- [X] T023 [P] [US2] Contract test for browser CORS rejection in tests/RoleRadar.Tests.Contract/Authentication/CorsRejectionContractTests.cs asserting request with Origin header receives no Access-Control-Allow-Origin header (expected to fail before implementation)

### Implementation for User Story 2

- [X] T024 [US2] Configure JwtBearer authentication in src/RoleRadar.Api/Program.cs with standard OIDC authority discovery, exact issuer validation, exact audience validation (roleradar.api), algorithm allowlist (RS256, ES256), small clock skew (max 60s), and owner subject allowlist authorization policy
- [X] T025 [US2] Configure zero-CORS policy and map anonymous /health endpoint on internal loopback in src/RoleRadar.Api/Program.cs
- [X] T026 [US2] Measured Checkpoint: Run scripts/test-fast.ps1 plus User Story 2 tests and report measured test counts

---

## Phase 5: User Story 3 - Comprehensive REST API for Application Tracking Capabilities (Priority: P1)

**Goal**: Full REST API endpoints for postings, companies, applications, criteria, notes, and search runs, delegating to existing Application handlers with bounded paging, optimistic concurrency, and audit parity.

**Independent Test**: Exercise every endpoint over HTTPS; assert bounded paging (50 default, 200 max), 409 conflict on stale versionToken, omitted company fields preserved on update, and audit records created with MCP operation names and actor api-user.

### Tests for User Story 3 (MANDATORY - Test-First per Principle II) ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [X] T027 [P] [US3] Contract tests for Postings endpoints (GET /api/v1/postings, GET /api/v1/postings/{id}, POST /api/v1/postings/{id}/transition) including bounded pagination (50 default, 200 max) and 409 concurrency conflict on stale versionToken in tests/RoleRadar.Tests.Contract/Api/PostingsEndpointsContractTests.cs (expected to fail before implementation)
- [X] T028 [P] [US3] Contract tests for Companies endpoints (GET /api/v1/companies, GET /api/v1/companies/{id}, PATCH /api/v1/companies/{id}) verifying omitted fields remain unchanged in tests/RoleRadar.Tests.Contract/Api/CompaniesEndpointsContractTests.cs (expected to fail before implementation)
- [X] T029 [P] [US3] Contract tests for Applications endpoints (GET /api/v1/applications, GET /api/v1/applications/{id}, POST /api/v1/applications, POST /api/v1/applications/{id}/events) in tests/RoleRadar.Tests.Contract/Api/ApplicationsEndpointsContractTests.cs (expected to fail before implementation)
- [X] T030 [P] [US3] Contract tests for Criteria, Notes, and SearchRuns endpoints in tests/RoleRadar.Tests.Contract/Api/AncillaryEndpointsContractTests.cs (expected to fail before implementation)
- [X] T031 [P] [US3] Integration test for API vs. MCP data visibility parity and audit entry operation name/actor (api-user) verification in tests/RoleRadar.Tests.Integration/Api/ApiMcpParityIntegrationTests.cs using real-shaped fixtures and IsolatedContainerMongoFixture (expected to fail before implementation)

### Implementation for User Story 3

- [X] T032 [P] [US3] Implement transport DTOs and envelopes in src/RoleRadar.Api/Envelopes/ and src/RoleRadar.Api/Models/ per data-model.md
- [X] T033 [US3] Implement Postings endpoints route group in src/RoleRadar.Api/Endpoints/PostingsEndpoints.cs delegating to Application handlers with api-user actor and mapping stale version token to HTTP 409
- [X] T034 [US3] Implement Companies endpoints route group in src/RoleRadar.Api/Endpoints/CompaniesEndpoints.cs delegating to Application handlers with omitted fields preserved on patch
- [X] T035 [US3] Implement Applications and Events endpoints route group in src/RoleRadar.Api/Endpoints/ApplicationsEndpoints.cs delegating to Application handlers with api-user actor
- [X] T036 [US3] Implement Criteria, Notes, and SearchRuns endpoints route groups in src/RoleRadar.Api/Endpoints/AncillaryEndpoints.cs delegating to Application query handlers
- [X] T037 [US3] Measured Checkpoint: Run scripts/test-fast.ps1 plus User Story 3 tests and report measured test counts

---

## Phase 6: User Story 4 - Build-Time OpenAPI Contract Snapshot and Typed Client Generation (Priority: P2)

**Goal**: Build-time OpenAPI v3.1 specification generation with explicit scalar types, committed contract snapshot verification test, and typed TypeScript client generation for Feature 005b.

**Independent Test**: Build the API project, verify that contracts/openapi/roleradar-api.json matches the committed snapshot, and generate TypeScript types.

### Tests for User Story 4 (MANDATORY - Test-First per Principle II) ⚠️

- [ ] T038 [US4] Contract snapshot test in tests/RoleRadar.Tests.Contract/OpenApi/ApiOpenApiContractSnapshotTests.cs asserting generated roleradar-api.json exactly matches the committed reference snapshot (expected to fail before generation)

### Implementation for User Story 4

- [ ] T039 [P] [US4] Configure build-time OpenAPI document generation in src/RoleRadar.Api/RoleRadar.Api.csproj targeting contracts/openapi/roleradar-api.json with explicit scalar types for all parameters, properties, and enums
- [ ] T040 [P] [US4] Implement OpenAPI schema and operation transformers in src/RoleRadar.Api/OpenApi/ExplicitTypeSchemaTransformers.cs ensuring zero untyped scalar parameters and explicit nullability
- [ ] T041 [US4] Add npm script and client generation configuration in contracts/package.json invoking openapi-typescript to produce contracts/client/api-types.ts
- [ ] T042 [US4] Measured Checkpoint: Build solution to generate OpenAPI specification, verify contract snapshot test passes, and report measured metrics

---

## Phase 7: User Story 5 - Hardened Linux Container Images & Compose Services (Priority: P2)

**Goal**: Hardened, chiseled container images and Docker Compose service definitions for Identity and API with self-contained probe mode (--healthcheck), HTTPS local cert mounting, and CI packaging constraints.

**Independent Test**: Build container images, launch them in read-only mode (docker run --read-only --tmpfs /tmp), and verify that docker inspect reports healthy within the start period.

### Tests for User Story 5 (MANDATORY - Test-First per Principle II) ⚠️

- [ ] T043 [US5] Contract/launched-host test in tests/RoleRadar.Tests.Contract/Containers/ContainerHealthCheckLaunchedHostTests.cs asserting identity and api containers report healthy in docker inspect under supported read-only mode (expected to fail before implementation)

### Implementation for User Story 5

- [ ] T044 [P] [US5] Implement self-contained probe mode (--healthcheck) in src/RoleRadar.Identity/Program.cs querying http://127.0.0.1:8080/health and exiting 0 on success, 1 on failure
- [ ] T045 [P] [US5] Implement self-contained probe mode (--healthcheck) in src/RoleRadar.Api/Program.cs querying http://127.0.0.1:8080/health and exiting 0 on success, 1 on failure
- [ ] T046 [P] [US5] Create hardened chiseled deploy/Dockerfile.identity following Feature 003 conventions (non-root, read-only rootfs, digest-pinned base, exec-form HEALTHCHECK CMD ["/app/RoleRadar.Identity", "--healthcheck"])
- [ ] T047 [P] [US5] Create hardened chiseled deploy/Dockerfile.api following Feature 003 conventions (non-root, read-only rootfs, digest-pinned base, exec-form HEALTHCHECK CMD ["/app/RoleRadar.Api", "--healthcheck"])
- [ ] T048 [US5] Add identity and api service definitions to deploy/docker-compose.apps.yml with mounted certificate /https/cert.pfx, persistent volume identity-data, environment configuration, and exec-form healthchecks
- [ ] T049 [US5] Update CI workflow .github/workflows/ci.yml with hadolint, Trivy vulnerability scan, and SBOM generation for both new images, enforcing that the identity image is never published or pushed to any external registry
- [ ] T050 [US5] Measured Checkpoint: Run container build and launched-host health check tests and report measured metrics

---

## Phase 8: User Story 6 - IdP-Agnostic Standard OIDC Test Suite (Priority: P3)

**Goal**: Provider-neutral end-to-end integration test suite communicating with the identity host and API using strictly standard OIDC discovery metadata, token endpoints, and standard JWT claims.

**Independent Test**: Execute the full end-to-end integration suite against an ephemeral test identity host and IsolatedContainerMongoFixture; verify all standard OIDC flows pass without vendor-specific claims.

### Tests for User Story 6 (MANDATORY - Test-First per Principle II) ⚠️

- [ ] T051 [US6] Implement provider-neutral OIDC integration test suite in tests/RoleRadar.Tests.Integration/Oidc/IdpAgnosticOidcIntegrationTests.cs using discovery metadata, standard token endpoint requests, and standard JWT claims against an ephemeral test identity host and IsolatedContainerMongoFixture (expected to fail before implementation)
- [ ] T052 [US6] Measured Checkpoint: Run IdpAgnosticOidcIntegrationTests and report measured test counts

---

## Phase 9: Polish, Cross-Cutting & Real-Data Rehearsal

**Purpose**: Final quality gates, documentation, and operator real-data rehearsal procedure.

- [ ] T053 [P] Update documentation in docs/adr/README.md and feature design artifacts in specs/005a-api-and-login/
- [ ] T054 Run scripts/review-gate.ps1 to assert all unit, architecture, contract, and integration tests pass across the four existing test projects, Domain+Application coverage is >=85%, dev-stack canary remains untouched, and container images build cleanly
- [ ] T055 Document operator real-data rehearsal procedure in specs/005a-api-and-login/quickstart.md for execution by Matt against a throwaway database restored from backup via ROLERADAR_MONGO_DATABASE override

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: Can start immediately.
- **Foundational (Phase 2)**: Depends on Phase 1. Blocks all user stories.
- **User Story 1 (Phase 3)**: Depends on Phase 2. Delivers identity host and owner login.
- **User Story 2 (Phase 4)**: Depends on Phase 3 (needs token issuance from US1). Delivers token validation and owner mapping.
- **User Story 3 (Phase 5)**: Depends on Phase 4 (needs token validation middleware from US2). Delivers REST API endpoints.
- **User Story 4 (Phase 6)**: Depends on Phase 5 (needs endpoints for OpenAPI generation). Delivers OpenAPI contract and typed client.
- **User Story 5 (Phase 7)**: Depends on Phase 3 and Phase 5 (needs service binaries for packaging). Delivers container images and compose services.
- **User Story 6 (Phase 8)**: Depends on Phase 3 and Phase 5. Delivers provider-neutral OIDC integration tests.
- **Polish (Phase 9)**: Depends on all prior phases.

### Within Each User Story

- Tests are MANDATORY, written first, and expected to fail before implementation tasks begin.
- Models and DTOs before endpoints.
- Measured checkpoint task at the end of each user story phase.

---

## Parallel Execution Opportunities

- Phase 1: T002 and T003 can be implemented in parallel.
- Phase 2: T007, T008, T009 can be implemented in parallel.
- Phase 3: Tests T013, T014, T015, T016 can be written in parallel.
- Phase 4: Tests T022, T023 can be written in parallel.
- Phase 5: Tests T027, T028, T029, T030, T031 can be written in parallel. DTOs T032 can be implemented in parallel.
- Phase 7: Healthcheck probes T044, T045, and Dockerfiles T046, T047 can be created in parallel.

---

## Implementation Strategy

### MVP First (User Story 1 & User Story 2)

1. Complete Phase 1: Setup (seams and references).
2. Complete Phase 2: Foundational (identity store and owner resolution).
3. Complete Phase 3: User Story 1 (Identity host and owner login) → Validate independently.
4. Complete Phase 4: User Story 2 (Token validation) → Validate independently.
5. Complete Phase 5: User Story 3 (REST API) → Complete core backend capability.
6. Complete Phases 6–8: OpenAPI, containers, and OIDC integration suite.
7. Execute Phase 9: review-gate and operator rehearsal.
