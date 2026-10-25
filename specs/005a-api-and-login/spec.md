# Feature Specification: 005a REST API and Duende Identity Host

**Feature Branch**: `005a-api-and-login`

**Created**: 2026-10-25

**Status**: Draft

**Input**: User description: "Feature 005a-api-and-login: an authenticated REST API over the existing Application layer, plus the Duende identity host that logs the single owner in. No UI (that is 005b). No new MCP tools; the stdio MCP server keeps working unchanged. Context: RoleRadar is a single-owner job-search tracker with MongoDB as the only domain store (ADR-0001), an Application layer of command/query handlers, 12 stdio MCP tools, and real data now stored under owner id 'local-owner'. Contacts remain CLI-only and are never exposed by the API. Stories: P1 Identity host (RoleRadar.Identity), P1 Strict token validation in the API (RoleRadar.Api), P1 REST API for what the UI needs, P2 OpenAPI document generated at build, committed contract snapshot, and a generated typed client for 005b, P2 Container images and compose services for identity and api following 003 conventions, P3 IdP-agnostic standard OIDC integration suite. Rules: tokens/secrets never appear in logs/errors/repo; untrusted text is data; audit entries match MCP tool equivalents with actor 'api-user'; fictional fixtures only; MCP tool schemas/behavior unchanged."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Dedicated Identity Host for Single Owner Authentication (Priority: P1)

As the single owner and operator of RoleRadar, I want a dedicated identity service that authenticates me and issues cryptographically verifiable access tokens, so that subsequent web UI sessions and automated jobs can securely access RoleRadar data without exposing master credentials or bypassing authentication.

**Why this priority**: Without an identity provider issuing valid OpenID Connect / OAuth2 tokens, no authenticated API access is possible for web clients or automated jobs.

**Independent Test**: Can be tested independently by starting the identity host with test credentials, performing an authorization-code flow with PKCE for confidential interactive clients, performing a client-credentials flow for headless service clients, and verifying that cryptographically signed tokens containing the owner identity are issued.

**Acceptance Scenarios**:

1. **Given** a configured confidential client for interactive web access, **When** the single owner initiates login using an authorization code flow with PKCE, **Then** the identity host authenticates the owner, verifies the exact redirect URI, and issues an ID token and access token signed by the host's active signing key.
2. **Given** a configured headless service client with narrow scopes, **When** the client authenticates using client credentials and valid client secrets, **Then** the identity host issues an access token scoped exclusively to the authorized service operations.
3. **Given** any login attempt with invalid credentials, mismatched redirect URIs, or omitted PKCE code verifiers, **When** submitted to the identity host, **Then** the request is rejected with a standard OAuth2 error and no token is issued.
4. **Given** the identity host operational state (signing keys, persistent grants), **When** inspected in storage, **Then** operational state is stored in an independent datastore isolated from the primary domain database.

---

### User Story 2 - Strict Token Validation & Owner-Scoping Resource Server (Priority: P1)

As the system owner, I want the API resource server to strictly validate every inbound token against standard OpenID Connect discovery and enforce an allowlist of authorized subject identifiers, so that unauthorized tokens, forged signatures, expired credentials, or unrecognized callers are rejected immediately before any domain handler is invoked.

**Why this priority**: Strict zero-trust token validation guarantees that all API operations map securely to the system's verified owner identifier and prevents any unauthorized access or privilege escalation.

**Independent Test**: Can be tested independently by issuing requests with missing tokens, expired tokens, tokens from untrusted issuers, tokens with unauthorized algorithms, tokens with mismatched audiences, tokens with non-allowlisted subject identifiers, and valid allowlisted owner tokens, asserting appropriate 401 Unauthorized or 403 Forbidden HTTP responses.

**Acceptance Scenarios**:

1. **Given** an API request without a bearer token, with an expired token, with an untrusted signing signature, or with an algorithm not on the approved allowlist (RS256, ES256), **When** submitted to any secure endpoint, **Then** the API returns HTTP 401 Unauthorized with standard `WWW-Authenticate` headers and invokes no domain handlers.
2. **Given** a validly signed token whose audience does not exactly match the configured API resource identifier or whose issuer does not match the configured authority, **When** received by the API, **Then** the request is rejected with HTTP 401 Unauthorized.
3. **Given** a validly signed token whose subject identifier (`sub`) is not present in the owner allowlist, **When** received by the API, **Then** the request is rejected with HTTP 403 Forbidden.
4. **Given** a validly signed token with an allowlisted subject identifier, **When** dispatched to any domain query or command, **Then** the caller context is automatically mapped to the default system owner, and all retrieved and modified domain data belongs to that owner.
5. **Given** an unauthenticated request to the designated health check endpoint, **When** invoked, **Then** the API returns HTTP 200 OK with health status without requiring credentials.

---

### User Story 3 - Comprehensive REST API for Application Tracking Capabilities (Priority: P1)

As a frontend user or headless operator, I want standard, predictable REST endpoints for tracking job postings, company research, applications, evaluation criteria, notes, and search runs, so that all tracking operations can be performed over HTTP with optimistic concurrency, bounded pagination, and clear error envelopes.

**Why this priority**: Delivers the complete server-side capabilities required by the future web user interface and headless integrations, parity-matched with existing MCP tools.

**Independent Test**: Can be tested by exercising each endpoint over HTTP (filtering postings, transitioning statuses, updating companies with omitted fields untouched, recording applications, managing criteria, querying notes and search runs) and asserting bounded pagination, concurrency conflict detection (HTTP 409), and consistent error envelopes.

**Acceptance Scenarios**:

1. **Given** an authenticated request to list job postings with optional filters (disposition, company, resurface policy, score threshold, text query) and paging parameters, **When** executed, **Then** the API returns a paged collection bounded to the configured default limit (50) and enforced ceiling (200), including total counts and pagination metadata.
2. **Given** an authenticated request for a specific posting, **When** queried by identifier, **Then** the API returns detailed posting state, attached signals, and the linked assessment note content as plain unrendered string data.
3. **Given** an authenticated request to transition a posting disposition with a reason, criterion identifier, and concurrency version token, **When** the version token matches current state, **Then** the transition succeeds and records an append-only audit entry with the `api-user` actor name; **When** the version token is stale, **Then** the API returns HTTP 409 Conflict.
4. **Given** an authenticated request to update an existing company supplying only career source definitions, **When** processed, **Then** all omitted company fields (status, ownership type, description, headquarters, category tags) remain unchanged.
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

As a DevOps maintainer, I want minimal, hardened Linux container images and Docker Compose service definitions for both the identity host and the API service, following existing container conventions (non-root execution, read-only root filesystems, digest-pinned base images, health checks), so that the entire backend stack can run securely in containerized environments.

**Why this priority**: Guarantees production container standards and reproducibility across local development, CI test environments, and future cloud container hosting.

**Independent Test**: Can be tested by building both container images, executing them with `docker run --read-only --tmpfs /tmp` under non-root users, launching the stack via Docker Compose, and verifying that health probes report healthy status without storing secrets in images.

**Acceptance Scenarios**:

1. **Given** container images for the identity host and API service, **When** built and inspected, **Then** images use digest-pinned base images, execute under a designated non-privileged UID/GID, and contain zero embedded credentials or secrets.
2. **Given** the running containers executed with read-only root filesystems (`--read-only`), **When** started with temporary directories mounted as tmpfs, **Then** both services initialize, bind their configured HTTP ports, log diagnostics strictly to stderr or structured console, and handle requests without file system permission failures.
3. **Given** a Docker Compose environment defining the database, identity host, and API services, **When** launched with environment-provided secrets, **Then** health check probes report all services healthy and interconnected.

---

### User Story 6 - IdP-Agnostic Standard OIDC Test Suite (Priority: P3)

As an architect, I want an end-to-end integration test suite that communicates with the identity provider and API using strictly standard OpenID Connect discovery documents and standard claims, so that the underlying identity provider implementation can be swapped in the future (e.g., to Keycloak or an external IdP) without modifying the API resource server or integration tests.

**Why this priority**: Preserves architectural agility and prevents tight coupling between the resource server and the chosen identity provider implementation.

**Independent Test**: Can be tested by executing the end-to-end integration test suite against standard discovery endpoints (`.well-known/openid-configuration`), authenticating via standard token endpoints, and asserting API functionality using standard token formats without vendor-specific claims or extensions.

**Acceptance Scenarios**:

1. **Given** the integration test harness, **When** configuring API authentication, **Then** the harness consumes only standard OpenID Connect metadata (JWKS endpoint, token endpoint, issuer URL) obtained from discovery.
2. **Given** an authentication flow executed against the identity provider, **When** tokens are inspected, **Then** only standard claims (`iss`, `sub`, `aud`, `exp`, `iat`) are required by the API resource server.
3. **Given** the full end-to-end test cycle, **When** executed against the identity host and API, **Then** all test scenarios pass using standard HTTP protocol calls.

---

### Edge Cases

- **Token Expiration During Request**: When a token expires while a request is in flight or arriving at the gateway, the API rejects the request with HTTP 401 and an expired token indicator, requiring client token refresh.
- **Clock Skew**: When tokens are issued with timestamps slightly ahead of or behind the API server's clock, a small tolerance window (e.g., up to 60 seconds) prevents false rejections while rejecting materially early or late tokens.
- **Key Rotation**: When the identity provider rotates its signing keys, the API automatically fetches the updated JWKS key set without requiring service restarts.
- **Malformed or Incomplete OIDC Metadata**: If the identity authority's discovery endpoint is unreachable or returns malformed metadata on startup, the API fails fast during initialization with clear diagnostic logs to stderr.
- **Concurrent Posting Updates**: If two callers attempt to update or transition the same job posting simultaneously, the first request succeeds and increments the version token; the second request fails with HTTP 409 Conflict.
- **Paging Boundary Violations**: If a client requests a page size less than 1 or exceeding 200, the API clamps the limit to the valid boundary (or rejects with HTTP 400 Validation Error) to protect server performance.
- **Untrusted Content with Script or Markup**: When job postings or notes contain HTML, markdown, or scripts, the API treats all free-form fields as plain JSON strings without sanitization or HTML rendering, preventing injection vulnerabilities.
- **Contacts Isolation**: Any attempt to access personal contacts via the API returns HTTP 404 Not Found, as contacts are strictly CLI-only and intentionally omitted from the API surface.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST provide a dedicated identity host that implements standard OpenID Connect (OIDC) and OAuth 2.0 endpoints (discovery, authorization, token, and JWKS).
- **FR-002**: Identity host MUST support an interactive authorization code flow with PKCE (RFC 7636) for confidential web application clients with exact redirect URI matching and client secrets.
- **FR-003**: Identity host MUST support a client credentials grant flow for automated, headless service clients with restricted scopes.
- **FR-004**: Identity host MUST restrict user authentication exclusively to the designated single system owner.
- **FR-005**: Identity host operational state (cryptographic signing keys and persistent grants) MUST be stored in an isolated datastore independent from the primary domain database.
- **FR-006**: API resource server MUST validate inbound bearer tokens using standard OIDC discovery, verifying digital signatures via the authority's JWKS endpoint.
- **FR-007**: API resource server MUST enforce exact issuer matching against the configured identity authority.
- **FR-008**: API resource server MUST require that the token audience (`aud`) exactly matches the configured API resource identifier.
- **FR-009**: API resource server MUST enforce a cryptographic algorithm allowlist limited to approved asymmetric algorithms (RS256 and ES256).
- **FR-010**: API resource server MUST enforce token expiration (`exp`) with a configurable small clock skew.
- **FR-011**: API resource server MUST verify that the token subject identifier (`sub`) exists in the configured owner allowlist, returning HTTP 403 Forbidden for any valid token with an unrecognized subject.
- **FR-012**: API resource server MUST map allowlisted subject tokens to the system default owner context, ensuring all domain queries and commands execute under the default owner.
- **FR-013**: System MUST provide an unauthenticated health check endpoint returning service health status.
- **FR-014**: API MUST expose endpoints to query job postings with bounded pagination (default 50, maximum 200 items per page) and filtering by disposition, company identifier, resurface policy, score threshold, and free text search.
- **FR-015**: API MUST expose endpoints to retrieve individual posting details, including associated signals and linked assessment notes.
- **FR-016**: API MUST expose endpoints to transition posting disposition, accepting an explicit reason, optional criterion identifier, and concurrency version token.
- **FR-017**: API MUST expose endpoints to list companies, retrieve company details including configured career sources, and update company attributes.
- **FR-018**: When updating a company via the API, any omitted (null or unsupplied) fields MUST remain unchanged in the underlying company record.
- **FR-019**: API MUST expose endpoints to list job applications, retrieve application details with historical timeline events, and record new applications and timeline events.
- **FR-020**: API MUST expose endpoints to list evaluation criteria and query criteria effective at a specified date.
- **FR-021**: API MUST expose endpoints to retrieve individual notes and execute text searches across notes.
- **FR-022**: API MUST expose endpoints to list past search run executions and their summary outcomes.
- **FR-023**: API MUST NOT expose any endpoints for accessing, querying, or modifying personal contacts.
- **FR-024**: API MUST NOT expose any hard-delete endpoints for postings, companies, applications, or notes.
- **FR-025**: API MUST enforce optimistic concurrency on write operations using version tokens, returning HTTP 409 Conflict when a version token mismatch is detected.
- **FR-026**: Every write operation executed through the API MUST record an append-only audit entry identical in structure to MCP tool operations, specifying `api-user` as the actor name.
- **FR-027**: API MUST return all free text (notes, postings, reasons, culture text) as plain JSON string data without server-side rendering or interpretation.
- **FR-028**: API MUST return a consistent, structured error envelope for all client errors (validation, not found, concurrency conflict) without including internal stack traces or database driver details.
- **FR-029**: System MUST automatically generate an OpenAPI v3 specification document representing the complete API contract during the build process.
- **FR-030**: System MUST verify the generated OpenAPI specification against a committed contract snapshot in version control to detect unauthorized contract modifications.
- **FR-031**: System MUST generate a strongly-typed API client from the verified OpenAPI contract.
- **FR-032**: System MUST provide container image definitions for the identity host and API service that execute under a non-root user and support read-only root filesystems.
- **FR-033**: System MUST NOT store any passwords, client secrets, tokens, or private keys within source code, container images, or build logs.
- **FR-034**: System MUST provide an end-to-end integration test suite verifying standard OIDC authentication and API execution without dependency on vendor-specific OIDC extensions.

### Key Entities *(include if feature involves data)*

- **OwnerSecurityProfile**: Represents the security configuration for the system owner, including allowlisted subject identifiers, assigned scopes, and role claims.
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
- **SC-006**: Generated OpenAPI contract exactly matches the committed snapshot, with zero undocumented schema differences detected during build verification.
- **SC-007**: Identity host and API container images build successfully, pass container vulnerability scans with zero critical findings, and initialize under read-only root filesystems in under 4.0 seconds.
- **SC-008**: Code coverage across Domain and Application layers remains at or above 85% with all unit, architecture, contract, and integration tests passing.
- **SC-009**: The end-to-end OIDC integration suite executes to completion against standard discovery and token endpoints without using vendor-specific claims or APIs.

## Assumptions

- **Single-Owner Deployment**: RoleRadar remains a single-owner application; multi-tenancy and multi-user permissions are out of scope.
- **Separate Web UI Milestone**: User interface development and frontend tooling are deferred to Feature 005b; this specification covers the backend API and identity services only.
- **MCP Server Continuity**: The existing 12 Model Context Protocol stdio tools and their schemas remain completely unaffected and continue to execute in parallel against the Application layer.
- **Contacts Isolation**: Contacts data remains exclusively managed via the command-line interface and is not exposed through any HTTP API endpoint.
- **Operational Datastore Selection**: Operational state for the identity host (signing keys and grants) will be determined via an Architecture Decision Record (ADR) during the planning phase, evaluating an embedded SQLite file versus an isolated containerized relational store.
- **Licensing Compliance**: Duende IdentityServer Community Edition is used for local single-owner development within allowable community thresholds, with licensing rationale documented in a dedicated ADR.
- **Local Network Boundary**: Hosted public cloud networking, reverse proxy configuration, and TLS termination at public ingress are deferred to future hosting features.
