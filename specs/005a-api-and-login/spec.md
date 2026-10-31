# Feature Specification: 005a REST API and Duende Identity Host

**Feature Branch**: `005a-api-and-login`

**Created**: 2026-10-25

**Status**: Draft

**Input**: User description: "Feature 005a-api-and-login: an authenticated REST API over the existing Application layer, plus the Duende identity host that logs the single owner in. No UI (that is 005b). No new MCP tools; the stdio MCP server keeps working unchanged. Context: RoleRadar is a single-owner job-search tracker with MongoDB as the only domain store (ADR-0001), an Application layer of command/query handlers, 12 stdio MCP tools, and real data now stored under owner id 'local-owner'. Contacts remain CLI-only and are never exposed by the API. Stories: P1 Identity host (RoleRadar.Identity), P1 Strict token validation in the API (RoleRadar.Api), P1 REST API for what the UI needs, P2 OpenAPI document generated at build, committed contract snapshot, and a generated typed client for 005b, P2 Container images and compose services for identity and api following 003 conventions, P3 IdP-agnostic standard OIDC integration suite. Rules: tokens/secrets never appear in logs/errors/repo; untrusted text is data; audit entries match MCP tool equivalents with actor 'api-user'; fictional fixtures only; MCP tool schemas/behavior unchanged."

## Clarifications

### Session 2026-10-25

- Q: How is the single owner account provisioned and authenticated on the identity host? → A: A single owner account is provisioned in the identity store via CLI (`roleradar identity create-owner`) prompting for password and displaying TOTP secret/QR once (no credentials stored in repo, files, or logs). Passwords use framework default hashing; account locks for 15 minutes after 5 consecutive failed attempts; login and token endpoints enforce rate limiting.
- Q: What are the explicit token, code, and session lifetimes? → A: Access tokens: 10 minutes; refresh tokens: rotating one-time use with reuse detection and 30-day absolute lifetime; client credentials tokens: 5 minutes with narrow scopes; authorization codes: 60 seconds.
- Q: What transport and CORS policies govern the identity host and REST API? → A: HTTPS everywhere including local compose (using a locally trusted development certificate mounted into containers; no plain HTTP listener except the internal compose health check); fixed issuer URL from configuration; zero CORS support (API is called server-to-server by the web application; cross-origin browser requests are rejected).
- Q: How are cryptographic signing keys managed and backed up? → A: Duende automatic key management with rotation in the isolated identity store (not Mongo). The identity store is intentionally excluded from RoleRadar backups; losing it only invalidates active sessions and the owner account can be recreated with the CLI command (documented in the quickstart).
- Q: What naming and actor convention applies to API audit records? → A: API write operations record audit entries using the exact same operation names as the equivalent MCP tools (`posting_transition`, `company_update`, `application_create`, `application_event_append`), with the distinct actor name `api-user`.
- Q: What real-data safety policy governs testing and deployment? → A: Tests run against `IsolatedContainerMongoFixture` and an ephemeral test identity host; the dev Mongo is never touched. Before any real deployment, a read-only rehearsal is executed against a throwaway database restored from backup via the `ROLERADAR_MONGO_DATABASE` environment override.
- Q: Are posting field edits (score, culture, compensation) or criteria/notes writes included in 005a? → A: No; 005a focuses on the endpoints required by the initial UI (dispositions, applications, and company career sources/fields). Posting field edits and criteria/notes writes are explicitly deferred to 005b and future follow-ups.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Dedicated Identity Host for Single Owner Authentication (Priority: P1)

As the single owner and operator of RoleRadar, I want a dedicated identity service that authenticates me using password plus TOTP second factor and issues cryptographically verifiable access tokens, so that subsequent web UI sessions and automated jobs can securely access RoleRadar data without exposing master credentials or bypassing authentication.

**Why this priority**: Without an identity provider issuing valid OpenID Connect / OAuth2 tokens, no authenticated API access is possible for web clients or automated jobs.

**Independent Test**: Can be tested independently by provisioning an owner via the CLI command `roleradar identity create-owner`, performing an authorization-code flow with PKCE and TOTP second-factor challenge for confidential interactive clients, performing a client-credentials flow for headless service clients, verifying token lifetimes (10m access, rotating refresh, 5m CC, 60s code), and verifying rate limiting and lockout behavior (15-minute lockout after 5 consecutive failures).

**Acceptance Scenarios**:

1. **Given** a configured confidential client for interactive web access, **When** the single owner initiates login using an authorization code flow with PKCE, **Then** the identity host authenticates the owner password, prompts for the TOTP second-factor code, verifies the exact redirect URI, and issues an ID token and a 10-minute access token alongside a rotating refresh token with one-time use and reuse detection (30-day absolute lifetime).
2. **Given** a configured headless service client with narrow scopes, **When** the client authenticates using client credentials and valid client secrets, **Then** the identity host issues a 5-minute access token scoped exclusively to the authorized service operations.
3. **Given** 5 consecutive failed login attempts on the owner account, **When** a 6th login attempt is made, **Then** the identity host locks the account for 15 minutes and rejects the attempt.
4. **Given** rapid automated traffic targeting login or token issuance endpoints, **When** exceeding rate limits, **Then** the identity host throttles the requests with HTTP 429 Too Many Requests.
5. **Given** the identity host operational state (signing keys, persistent grants, user credentials), **When** inspected in storage, **Then** operational state is stored in an independent datastore isolated from the primary domain database, using Duende automatic key management with rotation.

---

### User Story 2 - Strict Token Validation & Owner-Scoping Resource Server (Priority: P1)

As the system owner, I want the API resource server to strictly validate every inbound token over HTTPS against standard OpenID Connect discovery and enforce an allowlist of authorized subject identifiers without CORS, so that unauthorized tokens, forged signatures, expired credentials, or unrecognized callers are rejected immediately before any domain handler is invoked.

**Why this priority**: Strict zero-trust token validation guarantees that all API operations map securely to the system's verified owner identifier and prevents any unauthorized access or privilege escalation.

**Independent Test**: Can be tested independently by issuing requests with missing tokens, expired tokens, tokens from untrusted issuers, tokens with unauthorized algorithms, tokens with mismatched audiences, tokens with non-allowlisted subject identifiers, valid allowlisted owner tokens, and browser cross-origin requests, asserting appropriate 401 Unauthorized, 403 Forbidden, or rejected CORS responses.

**Acceptance Scenarios**:

1. **Given** an API request without a bearer token, with an expired token, with an untrusted signing signature, or with an algorithm not on the approved allowlist (RS256, ES256), **When** submitted to any secure endpoint, **Then** the API returns HTTP 401 Unauthorized with standard `WWW-Authenticate` headers and invokes no domain handlers.
2. **Given** a validly signed token whose audience does not exactly match the configured API resource identifier or whose issuer does not match the configured fixed authority URL, **When** received by the API, **Then** the request is rejected with HTTP 401 Unauthorized.
3. **Given** a validly signed token whose subject identifier (`sub`) is not present in the owner allowlist, **When** received by the API, **Then** the request is rejected with HTTP 403 Forbidden.
4. **Given** a validly signed token with an allowlisted subject identifier, **When** dispatched to any domain query or command, **Then** the caller context is automatically mapped to the default system owner, and all retrieved and modified domain data belongs to that owner.
5. **Given** a browser cross-origin request (`Origin` header present), **When** submitted to the API, **Then** the request is rejected without CORS headers, as the API is exclusively accessible server-to-server.
6. **Given** an unauthenticated request to the designated health check endpoint inside the local network, **When** invoked, **Then** the API returns HTTP 200 OK with health status without requiring credentials.

---

### User Story 3 - Comprehensive REST API for Application Tracking Capabilities (Priority: P1)

As a frontend user or headless operator, I want standard, predictable REST endpoints for tracking job postings, company research, applications, evaluation criteria, notes, and search runs, so that tracking operations can be performed over HTTPS with optimistic concurrency, bounded pagination, consistent error envelopes, and audit entry parity.

**Why this priority**: Delivers the complete server-side capabilities required by the initial web user interface and headless integrations, parity-matched with existing MCP tools.

**Independent Test**: Can be tested by exercising each endpoint over HTTPS (filtering postings, transitioning statuses, updating companies with omitted fields untouched, recording applications, querying criteria, notes, and search runs) and asserting bounded pagination, concurrency conflict detection (HTTP 409), consistent error envelopes, and audit records with identical MCP operation names and actor `api-user`.

**Acceptance Scenarios**:

1. **Given** an authenticated request to list job postings with optional filters (disposition, company, resurface policy, score threshold, text query) and paging parameters, **When** executed, **Then** the API returns a paged collection bounded to the configured default limit (50) and enforced ceiling (200), including total counts and pagination metadata.
2. **Given** an authenticated request for a specific posting, **When** queried by identifier, **Then** the API returns detailed posting state, attached signals, and the linked assessment note content as plain unrendered string data.
3. **Given** an authenticated request to transition a posting disposition with a reason, criterion identifier, and concurrency version token, **When** the version token matches current state, **Then** the transition succeeds and records an append-only audit entry with the `posting_transition` operation name and `api-user` actor; **When** the version token is stale, **Then** the API returns HTTP 409 Conflict.
4. **Given** an authenticated request to update an existing company supplying only career source definitions, **When** processed, **Then** all omitted company fields (status, ownership type, description, headquarters, category tags) remain unchanged, and an audit entry with `company_update` and actor `api-user` is recorded.
5. **Given** an authenticated request to view companies, applications, timeline events, criteria definitions, notes, or search runs, **When** queried, **Then** the API returns the requested records matching the authenticated owner.
6. **Given** any invalid request payload or domain validation failure, **When** processed, **Then** the API returns a structured, consistent error envelope containing specific error codes and field issues, without leaking internal stack traces or database errors.

---

### User Story 4 - Build-Time OpenAPI Contract Snapshot and Typed Client Generation (Priority: P2)

As a frontend and API developer, I want a build-time generated OpenAPI specification document committed to the repository along with a generated typed client, so that client applications have compile-time type safety and any breaking API changes are detected immediately in CI.

**Why this priority**: Eliminates drift between API endpoints and client applications, preventing manual schema synchronization bugs in upcoming UI development.

**Independent Test**: Can be tested by running the build verification task to confirm that the generated OpenAPI schema matches the committed contract snapshot and that the typed client compiles against the schema.

**Acceptance Scenarios**:

1. **Given** the API project build pipeline, **When** compiled, **Then** an OpenAPI v3 specification is generated automatically representing all endpoints, request models, response models, and security schemes.
2. **Given** the generated OpenAPI document, **When** compared against the committed contract snapshot in version control, **Then** any undocumented or breaking schema modification fails the contract gate.
3. **Given** the committed OpenAPI contract, **When** the client generation task runs, **Then** strongly-typed client interfaces and models are generated for consumer integration.

---

### User Story 5 - Hardened Linux Container Images & Compose Services (Priority: P2)

As a DevOps maintainer, I want minimal, hardened Linux container images and Docker Compose service definitions for both the identity host and the API service, following existing container conventions (non-root execution, read-only root filesystems, digest-pinned base images, health checks, HTTPS with local development certificates), so that the entire backend stack can run securely in containerized environments.

**Why this priority**: Guarantees production container standards and reproducibility across local development, CI test environments, and future cloud container hosting.

**Independent Test**: Can be tested by building both container images, executing them with `docker run --read-only --tmpfs /tmp` under non-root users, launching the stack via Docker Compose with mounted local development certificates and environment secrets, and verifying that health probes report healthy status without storing secrets in images.

**Acceptance Scenarios**:

1. **Given** container images for the identity host and API service, **When** built and inspected, **Then** images use digest-pinned base images, execute under a designated non-privileged UID/GID, and contain zero embedded credentials or secrets.
2. **Given** the running containers executed with read-only root filesystems (`--read-only`), **When** started with temporary directories mounted as tmpfs and local development certificates mounted for HTTPS, **Then** both services initialize, bind their configured HTTPS ports, log diagnostics strictly to stderr or structured console, and handle requests without file system permission failures.
3. **Given** a Docker Compose environment defining the database, identity host, and API services, **When** launched with environment-provided secrets and mounted certificates, **Then** health check probes report all services healthy and interconnected.

---

### User Story 6 - IdP-Agnostic Standard OIDC Test Suite (Priority: P3)

As an architect, I want an end-to-end integration test suite that communicates with the identity provider and API using strictly standard OpenID Connect discovery documents and standard claims, so that the underlying identity provider implementation can be swapped in the future (e.g., to Keycloak or an external IdP) without modifying the API resource server or integration tests.

**Why this priority**: Preserves architectural agility and prevents tight coupling between the resource server and the chosen identity provider implementation.

**Independent Test**: Can be tested by executing the end-to-end integration test suite against standard discovery endpoints (`.well-known/openid-configuration`), authenticating via standard token endpoints, and asserting API functionality using standard token formats without vendor-specific claims or extensions.

**Acceptance Scenarios**:

1. **Given** the integration test harness, **When** configuring API authentication, **Then** the harness consumes only standard OpenID Connect metadata (JWKS endpoint, token endpoint, issuer URL) obtained from discovery.
2. **Given** an authentication flow executed against the identity provider, **When** tokens are inspected, **Then** only standard claims (`iss`, `sub`, `aud`, `exp`, `iat`) are required by the API resource server.
3. **Given** the full end-to-end test cycle, **When** executed against the identity host and API using `IsolatedContainerMongoFixture` and a test identity instance, **Then** all test scenarios pass using standard HTTP protocol calls without touching the developer Mongo database.

---

### Edge Cases

- **Token Expiration During Request**: When a token expires while a request is in flight or arriving at the gateway, the API rejects the request with HTTP 401 and an expired token indicator, requiring client token refresh.
- **Refresh Token Reuse Detection**: If a previously consumed refresh token is presented again, the identity host immediately revokes the entire token family, forcing re-authentication.
- **Brute Force Lockout**: After 5 consecutive failed login attempts on the owner account, authentication is locked for 15 minutes.
- **Clock Skew**: When tokens are issued with timestamps slightly ahead of or behind the API server's clock, a small tolerance window (up to 60 seconds) prevents false rejections while rejecting materially early or late tokens.
- **Key Rotation**: When the identity provider rotates its signing keys via automatic key management, the API automatically fetches the updated JWKS key set without requiring service restarts.
- **Malformed or Incomplete OIDC Metadata**: If the identity authority's discovery endpoint is unreachable or returns malformed metadata on startup, the API fails fast during initialization with clear diagnostic logs to stderr.
- **Concurrent Posting Updates**: If two callers attempt to update or transition the same job posting simultaneously, the first request succeeds and increments the version token; the second request fails with HTTP 409 Conflict.
- **Paging Boundary Violations**: If a client requests a page size less than 1 or exceeding 200, the API clamps the limit to the valid boundary (or rejects with HTTP 400 Validation Error) to protect server performance.
- **Untrusted Content with Script or Markup**: When job postings or notes contain HTML, markdown, or scripts, the API treats all free-form fields as plain JSON strings without sanitization or HTML rendering, preventing injection vulnerabilities.
- **Contacts Isolation**: Any attempt to access personal contacts via the API returns HTTP 404 Not Found, as contacts are strictly CLI-only and intentionally omitted from the API surface.
- **Cross-Origin Browser Requests**: Inbound requests containing an `Origin` header are rejected without CORS headers, preventing unauthorized browser-based script access.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST provide a dedicated identity host that implements standard OpenID Connect (OIDC) and OAuth 2.0 endpoints (discovery, authorization, token, and JWKS).
- **FR-002**: Identity host MUST support an interactive authorization code flow with PKCE (RFC 7636) for confidential web application clients with exact redirect URI matching and client secrets.
- **FR-003**: Identity host MUST support a client credentials grant flow for automated, headless service clients with restricted scopes and a 5-minute token lifetime.
- **FR-004**: Identity host MUST restrict user authentication exclusively to the single system owner, authenticated via password plus TOTP second factor.
- **FR-005**: System MUST provide a command-line interface tool (`roleradar identity create-owner`) to provision the owner account interactively, prompting for password and displaying the TOTP secret/QR code once, without writing credentials to repository files, configuration files, or log streams.
- **FR-006**: Identity host MUST hash owner passwords using framework default secure password hashing, enforce an account lockout of 15 minutes after 5 consecutive failed login attempts, and apply rate limiting to login and token endpoints.
- **FR-007**: Identity host MUST issue access tokens with a 10-minute lifetime, authorization codes with a 60-second lifetime, and rotating refresh tokens with one-time use, reuse detection, and a 30-day absolute expiration.
- **FR-008**: Identity host operational state (cryptographic signing keys, persistent grants, user credentials) MUST be stored in an isolated datastore independent from the primary domain database, using Duende automatic key management with rotation.
- **FR-009**: API resource server and identity host MUST enforce HTTPS transport across all endpoints (including local Docker Compose via mounted development certificates), prohibiting unencrypted HTTP listeners with the sole exception of the internal container health check endpoint.
- **FR-010**: API resource server MUST configure a fixed identity issuer URL and validate inbound bearer tokens using standard OIDC discovery, verifying digital signatures via the authority's JWKS endpoint.
- **FR-011**: API resource server MUST enforce exact issuer matching against the configured identity authority.
- **FR-012**: API resource server MUST require that the token audience (`aud`) exactly matches the configured API resource identifier.
- **FR-013**: API resource server MUST enforce a cryptographic algorithm allowlist limited to approved asymmetric algorithms (RS256 and ES256).
- **FR-014**: API resource server MUST enforce token expiration (`exp`) with a configurable small clock skew.
- **FR-015**: API resource server MUST verify that the token subject identifier (`sub`) exists in the configured owner allowlist, returning HTTP 403 Forbidden for any valid token with an unrecognized subject.
- **FR-016**: API resource server MUST map allowlisted subject tokens to the system default owner context, ensuring all domain queries and commands execute under the default owner.
- **FR-017**: API resource server MUST reject all cross-origin requests (CORS disabled), supporting only server-to-server HTTP invocations.
- **FR-018**: System MUST provide an unauthenticated health check endpoint returning service health status.
- **FR-019**: API MUST expose endpoints to query job postings with bounded pagination (default 50, maximum 200 items per page) and filtering by disposition, company identifier, resurface policy, score threshold, and free text search.
- **FR-020**: API MUST expose endpoints to retrieve individual posting details, including associated signals and linked assessment notes.
- **FR-021**: API MUST expose endpoints to transition posting disposition, accepting an explicit reason, optional criterion identifier, and concurrency version token.
- **FR-022**: API MUST expose endpoints to list companies, retrieve company details including configured career sources, and update company attributes.
- **FR-023**: When updating a company via the API, any omitted (null or unsupplied) fields MUST remain unchanged in the underlying company record.
- **FR-024**: API MUST expose endpoints to list job applications, retrieve application details with historical timeline events, and record new applications and timeline events.
- **FR-025**: API MUST expose endpoints to list evaluation criteria and query criteria effective at a specified date.
- **FR-026**: API MUST expose endpoints to retrieve individual notes and execute text searches across notes.
- **FR-027**: API MUST expose endpoints to list past search run executions and their summary outcomes.
- **FR-028**: API MUST NOT expose any endpoints for accessing, querying, or modifying personal contacts.
- **FR-029**: API MUST NOT expose any hard-delete endpoints for postings, companies, applications, or notes.
- **FR-030**: API MUST enforce optimistic concurrency on write operations using version tokens, returning HTTP 409 Conflict when a version token mismatch is detected.
- **FR-031**: Every write operation executed through the API MUST record an append-only audit entry using the identical operation name as the equivalent MCP tool (`posting_transition`, `company_update`, `application_create`, `application_event_append`), specifying `api-user` as the actor name.
- **FR-032**: API MUST return all free text (notes, postings, reasons, culture text) as plain JSON string data without server-side rendering or interpretation.
- **FR-033**: API MUST return a consistent, structured error envelope for all client errors (validation, not found, concurrency conflict) without including internal stack traces or database driver details.
- **FR-034**: System MUST automatically generate an OpenAPI v3 specification document representing the complete API contract during the build process.
- **FR-035**: System MUST verify the generated OpenAPI specification against a committed contract snapshot in version control to detect unauthorized contract modifications.
- **FR-036**: System MUST generate a strongly-typed API client from the verified OpenAPI contract.
- **FR-037**: System MUST provide container image definitions for the identity host and API service that execute under a non-root user and support read-only root filesystems.
- **FR-038**: System MUST NOT store any passwords, client secrets, tokens, or private keys within source code, container images, or build logs.
- **FR-039**: System MUST provide an end-to-end integration test suite verifying standard OIDC authentication and API execution without dependency on vendor-specific OIDC extensions.

### Key Entities *(include if feature involves data)*

- **OwnerSecurityProfile**: Represents the security configuration for the system owner, including allowlisted subject identifiers, assigned scopes, password hash, and TOTP secret configuration.
- **IdentityClientConfiguration**: Represents registered client applications (such as the web application or headless automation), defining client identifiers, allowed grant types (authorization code with PKCE, client credentials), secret hashes, exact redirect URIs, and permitted scopes.
- **PostingRecord**: Represents tracked job postings, including title, requisition, company reference, location, compensation text, disposition, resurface policy, score, version token, and attached signals.
- **CompanyProfile**: Represents employer organizations, including name, description, headquarters, ownership classification, status, career site sources, and category tags.
- **ApplicationTracker**: Represents an active or completed job application, associated posting references, submission dates, current status, and chronological timeline events.
- **EvaluationCriteria**: Represents scoring criteria rules, weights, descriptions, and temporal effectiveness dates used for posting assessments.
- **AssessmentNote**: Represents free-form analysis, interview records, or research narratives linked to postings or companies.
- **SearchRunRecord**: Represents the execution metadata and outcome summary of an automated search run.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of API endpoints requiring authentication reject unauthenticated, expired, or invalid tokens with HTTP 401 Unauthorized, verified by dedicated automated tests.
- **SC-002**: 100% of valid tokens with subject identifiers not on the owner allowlist are rejected with HTTP 403 Forbidden, verified by automated tests.
- **SC-003**: 100% of data imported or recorded via CLI or MCP tools is immediately accessible through the API for the matching owner without data translation discrepancies.
- **SC-004**: All list endpoints enforce bounded pagination, returning a maximum of 200 records per request regardless of client query parameters.
- **SC-005**: 100% of write endpoints reject conflicting concurrent updates with HTTP 409 Conflict when presented with stale version tokens.
- **SC-006**: 100% of API write operations produce audit entries whose operation names match the corresponding MCP tool names with actor `api-user`.
- **SC-007**: Generated OpenAPI contract exactly matches the committed snapshot, with zero undocumented schema differences detected during build verification.
- **SC-008**: Identity host and API container images build successfully, pass container vulnerability scans with zero critical findings, and initialize under read-only root filesystems with HTTPS in under 4.0 seconds.
- **SC-009**: Code coverage across Domain and Application layers remains at or above 85% with all unit, architecture, contract, and integration tests passing.
- **SC-010**: The end-to-end OIDC integration suite executes to completion against standard discovery and token endpoints without using vendor-specific claims or APIs.

## Assumptions

- **Single-Owner Deployment**: RoleRadar remains a single-owner application; multi-tenancy and multi-user permissions are out of scope.
- **Identity Store Backup Exclusion**: The identity operational datastore is intentionally excluded from RoleRadar backups; losing it invalidates active sessions without compromising domain data, and the single owner can be re-provisioned via `roleradar identity create-owner`.
- **Scope Boundaries for 005a**: Posting field edits (score, culture text, compensation details) and criteria/notes write operations are not in Feature 005a scope; they are tracked as follow-up items for Feature 005b and beyond.
- **Separate Web UI Milestone**: User interface development and frontend tooling are deferred to Feature 005b; this specification covers the backend API and identity services only.
- **MCP Server Continuity**: The existing 12 Model Context Protocol stdio tools and their schemas remain completely unaffected and continue to execute in parallel against the Application layer.
- **Contacts Isolation**: Contacts data remains exclusively managed via the command-line interface and is not exposed through any HTTP API endpoint.
- **Operational Datastore Selection**: Operational state for the identity host (signing keys and grants) will be determined via an Architecture Decision Record (ADR) during the planning phase, evaluating an embedded SQLite file versus an isolated containerized relational store.
- **Licensing Compliance**: Duende IdentityServer Community Edition is used for local single-owner development within allowable community thresholds, with licensing rationale documented in a dedicated ADR.
- **Real-Data Rehearsal Requirement**: Before any real use, a read-only rehearsal against a throwaway database restored from backup (`ROLERADAR_MONGO_DATABASE` override) is required.
