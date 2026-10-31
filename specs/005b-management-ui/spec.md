# Feature Specification: 005b-management-ui

**Feature Branch**: `005b-management-ui`

**Created**: 2026-10-31

**Status**: Draft

**Input**: User description: "Feature 005b-management-ui: a server-rendered web app where the single owner manages the job search through the existing 005a REST API. It is a confidential OIDC client of the 005a Identity host and holds all tokens server-side. No new domain behavior, no new MCP tools, no new API endpoints (gaps are listed in the spec, not filled here)."

## Clarifications

### Session 2026-10-31

- Q: How are target dispositions presented and validated for posting transitions given that target `applied` is forbidden and no allowed-transitions endpoint exists? → A: Target `applied` is disallowed via `posting_transition` (becoming `applied` occurs strictly by recording an application). The UI presents all general non-applied target disposition options in the transition dialog, submits the required `criterionId` (nullable) alongside optional `resurfacePolicy` and `overrideReason`, and maps backend API validation/matrix rejection errors (`InvalidTransition`, 400) directly into the UI.
- Q: How does the postings table handle sorting given that the 005a API query parameters do not include a sort parameter? → A: Postings table sorting is explicitly page-local only (sorting the currently fetched page of rows) and clearly labelled as page-local; server-side sorting is recorded as an API gap.
- Q: What query parameters and filters are available for postings list? → A: Postings filters are strictly the query parameters exposed by `GET /api/v1/postings`: `disposition`, `companyId`, `resurfacePolicy`, `minScore`, `q`, `limit`, and `offset`. All references to generic "status" or "company" are replaced by these real parameters.
- Q: How does the companies list handle search or filtering given that the 005a API exposes no filter parameters for companies? → A: The companies list view provides pagination only without search or filter controls; server-side search and filtering for companies is recorded as an API gap.
- Q: What are the exact entity fields for Screening Criteria and Search Runs exposed by the API? → A: Criteria entities expose `id`, `name`, `weight`, `ruleText`, `effectiveFrom`, `effectiveTo`, and `isActive` (category and provenance are removed). Search runs expose `id`, `runDate`, `companiesSearched`, `postingsFound`, `newPostings`, `errorsCount`, and `summary` (duration and status are removed).
- Q: What fields are displayed in the Posting Detail view, and to which fields does untrusted plain-text rendering apply? → A: Posting detail displays only the fields returned by `PostingDetailDto` (`id`, `companyId`, `companyName`, `title`, `requisitionId`, `location`, `disposition`, `resurfacePolicy`, `score`, `versionToken`, `createdAt`, `updatedAt`, `url`, `compensation` [with `currency`, `rawText`, `hasSalary`, `compHighPriority`], `signals` [with `signalType`, `signalValue`, `detectedAt`], and `assessmentNote` [with `id`, `summary`, `updatedAt`]). Untrusted plain-text rendering applies to all text fields returned by DTOs: `title`, `location`, `compensation.rawText`, `assessmentNote.summary`, signal values, note content/headings, event notes, and search run summaries.
- Q: How does the Applications list display company and posting context given that the API has no batch lookup endpoint? → A: The API has no batch lookup endpoint. The web server resolves names by issuing one `GET /api/v1/postings/{id}` and one `GET /api/v1/companies/{id}` per distinct posting and company on the page, bounded by the page size, deduplicated across the slice, executed concurrently, and cached for the request.
- Q: How can notes be searched? → A: Notes are searched strictly by text query (`q`), matching the OpenAPI query parameter; tag search is not supported by the API.
- Q: How is audit logging verified in automated Playwright end-to-end testing given that no audit API endpoint exists? → A: The Playwright test executes a direct query against the test's isolated ephemeral MongoDB `audit_log` collection to assert that the state-changing transition write generated the expected append-only audit record.
- Q: What server-side session architecture is used and what is the persistence/restart policy? → A: The application uses a cryptographically signed cookie carrying only an opaque session identifier mapping to a server-side memory/file-backed session store holding the encrypted OIDC token set (satisfying FR-005). Sessions are bounded to the web container lifecycle; container restarts require the owner to re-authenticate (acceptable for local single-owner operation). Tokens never reach the browser.
- Q: What happens if a session expires while the owner is editing a form? → A: Unsaved form input is lost upon session expiration, and the owner is returned to the same route after re-authentication with a notification (no draft persistence).
- Q: What is the scope of the SC-001 3-minute performance budget? → A: The 3-minute budget measures workflow completion time after the stack is already running (excluding container image builds and service startup).
- Q: How is test coverage verified for the new frontend code? → A: A dedicated success criterion is established for web application test coverage (component, loader, and utility suites via Vitest), with the exact percentage target to be specified in the implementation plan.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Secure Authentication & Session Management (Priority: P1)

The single owner accesses the management web application to monitor and drive their job search. If unauthenticated, the application securely redirects them to the 005a Duende Identity host login page (supporting username, password, and TOTP 2FA). Upon successful login, the owner is redirected back through an authorization code flow with PKCE, a signed cookie carrying only an opaque session id is established in the browser, and the user lands on the postings list. The web server manages tokens entirely on the backend using a server-side session store mapping from the opaque session id, handling silent token refreshes (with single-flight concurrency gating so overlapping user actions never trigger duplicate token rotation requests). When the session expires or is terminated, the owner is cleanly logged out locally and at Identity. If the web container restarts, re-login is required. If the backend API returns an authorization failure (HTTP 403), the application displays a clear "Not Authorized" screen rather than entering a redirect loop. If the session expires while the owner is drafting or editing a form, unsaved form values are lost upon redirecting to login, and the user returns to the target route after authentication.

**Why this priority**: Without secure authentication and token lifecycle handling, the owner cannot access or interact with any job-search data, and security invariants (no browser tokens, confidential OIDC client) cannot be satisfied.

**Independent Test**: Can be tested independently by navigating to the application root `/`, completing login via Identity with TOTP, verifying the owner session cookie is created with `HttpOnly`, `Secure`, and `SameSite` flags and carries only an opaque session id, confirming access to protected routes, verifying single-flight token rotation, and testing sign out.

**Acceptance Scenarios**:

1. **Given** an unauthenticated visitor, **When** they request any protected route (e.g., `/postings`), **Then** the server initiates an OIDC Authorization Code grant with PKCE and redirects the browser to Identity's authorization endpoint (`https://identity:5001/connect/authorize`).
2. **Given** a user successfully authenticated via Identity, **When** Identity redirects back to the web application callback with a valid authorization code, **Then** the web server exchanges the code for tokens, stores tokens strictly in a server-side session store, sets a minimal `HttpOnly`, `Secure`, `SameSite=Lax` session cookie carrying only an opaque session ID, and redirects the owner to `/postings`.
3. **Given** an authenticated session whose access token is expired or expiring, **When** multiple concurrent requests arrive at the web server, **Then** exactly one single-flight refresh request is executed against Identity's token endpoint (`grant_type=refresh_token`), updated tokens are persisted server-side, and all concurrent requests proceed without multiple token rotations or session invalidation.
4. **Given** an authenticated user whose token's `sub` claim is not authorized by the backend API (resulting in HTTP 403), **When** the server proxy or loader receives the 403 response, **Then** the application displays a friendly, non-looping "Not Authorized" error view explaining that the user subject is not permitted.
5. **Given** an active authenticated session, **When** the owner triggers sign out, **Then** the server-side session is cleared, the session cookie is expired, and an OIDC end-session redirect is performed against Identity.
6. **Given** an owner actively editing a form whose session expires before submission, **When** the form is submitted, **Then** the user is redirected to Identity to re-authenticate, unsaved input is discarded, and the user returns to the form route after logging in.

---

### User Story 2 - Browse, Search, and Inspect Job Postings (Priority: P1)

The owner needs a clear, responsive view of active and historical job opportunities. Navigating to the postings list displays a paged data table equipped with a search input and filter chips constrained strictly to query parameters supported by the 005a REST API snapshot (`disposition`, `companyId`, `resurfacePolicy`, `minScore`, and text query `q`, plus pagination `limit` and `offset`; sorting is page-local and labelled as such). Selecting a posting navigates to its detail view showing the exact fields returned by `PostingDetailDto`: disposition, company ID and name, title, requisition ID, location, URL, score, compensation (`currency`, `rawText`, `hasSalary`, `compHighPriority`), signals (`signalType`, `signalValue`, `detectedAt`), and assessment note summary (`id`, `summary`, `updatedAt`). Because external posting data may contain arbitrary or hostile content, all text fields (including title, location, compensation rawText, assessment note summary, and signals) are strictly rendered as untrusted plain text—never evaluated as HTML or raw markdown.

**Why this priority**: Reviewing job postings is the core daily workflow of the job search. Displaying untrusted text securely as plain text fulfills Core Principle V (Agent Safety & Untrusted Posting Hygiene).

**Independent Test**: Can be tested independently by seeding postings in MongoDB, logging in, filtering through the UI toolbar with real API filter parameters (`disposition`, `companyId`, `resurfacePolicy`, `minScore`, `q`), verifying table pagination and counts against the API, clicking into a posting detail view, and confirming malicious HTML/markdown strings in any text field render as harmless plain text.

**Acceptance Scenarios**:

1. **Given** an authenticated owner on `/postings`, **When** the page loads, **Then** the web server calls `GET /api/v1/postings` using the typed API client with active query parameters and renders the postings in a Design System `DataTable` with current counts and pagination controls.
2. **Given** an owner viewing the postings table, **When** interacting with table column sorting, **Then** sorting is executed strictly on the currently loaded page rows and clearly labelled as page-local sorting.
3. **Given** an owner applying search terms or filter chips (`disposition`, `companyId`, `resurfacePolicy`, `minScore`, or `q`), **When** the filter state updates, **Then** the application triggers a filtered server query matching supported OpenAPI parameters and updates the table without client-side data leaks.
4. **Given** a job posting containing HTML tags (`<script>`, `<img onerror=...>`) or markdown formatting in its title, location, compensation rawText, assessment note summary, or signals, **When** viewed in the table or on `/postings/{id}`, **Then** the content is rendered purely as escaped plain text without DOM injection or markdown formatting.
5. **Given** a valid posting ID, **When** the owner navigates to `/postings/{id}`, **Then** the system displays the full details including current disposition, company name, location, compensation details, match scores, signals, assessment note, and current version token.

---

### User Story 3 - Change Dispositions with Conflict Handling (Priority: P1)

When the owner decides to advance, screen out, or close an opportunity, they update its disposition through the UI. The UI presents target disposition options (excluding `applied`, which is unreachable via transition per `DispositionMatrix`). The dialog prompts for a reason, an optional criterion selection (`criterionId` is required in the payload but nullable), and optional `resurfacePolicy` or `overrideReason`. Because the API exposes no dynamic allowed-transitions endpoint, the UI presents all non-applied target disposition options and submits the request to `POST /api/v1/postings/{id}/transition` with the current `versionToken`. If the transition violates the domain matrix (e.g., transitioning from an ineligible state), the API returns HTTP 400 (`InvalidTransition`), and the UI maps this error clearly to the user. If another client updated the posting concurrently, the API returns HTTP 409 Conflict (`concurrency_conflict`); the UI captures this conflict, notifies the owner via a Toast/Banner with the fresh state, and offers a reload action without losing intent.

**Why this priority**: Driving workflow transitions is essential for active tracking. Proper optimistic concurrency conflict handling (409) and error mapping ensures data integrity across concurrent UI and MCP operations.

**Independent Test**: Can be tested independently by selecting a posting, submitting a valid disposition transition, confirming the API write succeeds, and verifying that injecting a stale version token triggers a 409 conflict notification with a reload action.

**Acceptance Scenarios**:

1. **Given** an owner viewing a posting detail or action menu, **When** they select a target disposition (excluding `applied`), provide a reason, specify `criterionId` (null if not based on a criterion), and submit, **Then** the server issues `POST /api/v1/postings/{id}/transition` with the current `versionToken` and updates the view upon success.
2. **Given** a selected transition that violates the domain disposition transition matrix, **When** the API responds with HTTP 400 (`InvalidTransition`), **Then** the UI displays an inline validation message explaining that the transition is not allowed from the current disposition.
3. **Given** an attempted transition with an outdated `versionToken`, **When** the API responds with HTTP 409 Conflict (`concurrency_conflict`), **Then** the application displays an inline conflict warning showing that the posting was modified elsewhere, prevents data corruption, and provides an action to reload the latest posting state.
4. **Given** an invalid transition payload (e.g., missing required reason), **When** the API returns HTTP 400 with standard error envelope details, **Then** the form maps the errors directly to the corresponding input fields.

---

### User Story 4 - Company Profile Management (Priority: P2)

The owner tracks target companies and their operational details. The owner can browse a paged list of companies (without search or filter, as the API does not support them), view company details, and edit permitted attributes (description, headquarters, status, categoryTags, careerSources) via `PATCH /api/v1/companies/{id}`. Updates use optimistic concurrency version tokens and standard field error mapping.

**Why this priority**: Maintaining company context directly supports intelligent application tracking and screening criteria evaluations.

**Independent Test**: Can be tested by navigating to `/companies`, selecting a company, updating its description or headquarters via form submission, verifying the API PATCH call, and confirming optimistic concurrency on stale updates.

**Acceptance Scenarios**:

1. **Given** an owner on `/companies`, **When** requesting the list, **Then** the server renders a paged table of companies retrieved via `GET /api/v1/companies` (with pagination controls only; no search or filter bar).
2. **Given** an owner editing a company at `/companies/{id}`, **When** modifying editable fields and submitting the form, **Then** the server issues `PATCH /api/v1/companies/{id}` with the appropriate payload and version token.
3. **Given** a concurrent modification to the company, **When** the API returns HTTP 409, **Then** the UI alerts the owner to reload and reapply modifications.

---

### User Story 5 - Application Lifecycle Tracking (Priority: P2)

The owner creates and tracks formal job applications. Becoming `applied` occurs exclusively by creating an application for a posting via `POST /api/v1/applications` (which atomically updates the posting's disposition to `applied`). From an eligible posting, the owner specifies submission dates, resume variants, and desired compensation. The applications list displays paged applications; because the API returns only `companyId` and `postingId` without names and has no batch lookup endpoint, the web server resolves names with one GET per distinct posting and company on the page, bounded by the page size, deduplicated across the slice, executed concurrently, and cached for the request. For an existing application, the owner can append timeline events.

**Why this priority**: Tracking application milestones allows the owner to monitor active interview funnels while respecting the strict invariant that `applied` status is granted only through formal application creation.

**Independent Test**: Can be tested by creating an application from a posting, verifying `POST /api/v1/applications` transitions the posting to `applied`, inspecting the resulting application detail, and appending an event via `POST /api/v1/applications/{id}/events`.

**Acceptance Scenarios**:

1. **Given** a posting in an applicable disposition, **When** the owner chooses "Create Application", **Then** the UI provides a form for submission date, tailored resume variant, and target compensation, submitting via `POST /api/v1/applications` and transitioning the posting to `applied`.
2. **Given** an owner navigating to `/applications`, **When** the list loads, **Then** the web server resolves company and posting names with one GET per distinct posting and company on the page (bounded by page size, deduplicated, concurrent, and request-cached), rendering the applications list with readable names.
3. **Given** an existing application on `/applications/{id}`, **When** the owner submits a new timeline event, **Then** the server posts to `/api/v1/applications/{id}/events` and appends the event to the timeline view upon success.

---

### User Story 6 - Read-Only Operational Views: Criteria, Notes & Search Runs (Priority: P3)

The owner can inspect read-only views for active and inactive screening criteria (showing `name`, `weight`, `ruleText`, `effectiveFrom`, `effectiveTo`, `isActive`), search narrative notes by text query `q` (reading paged markdown content in read-only mode), and review automated search run batches (showing `runDate`, `companiesSearched`, `postingsFound`, `newPostings`, `errorsCount`, `summary`). Contact management, note authoring/editing, criteria creation, and raw import operations are deliberately excluded from this UI.

**Why this priority**: Provides comprehensive visibility into background automation and decision rules while keeping operational boundaries narrow and secure.

**Independent Test**: Can be tested by loading `/criteria`, `/notes` (searching by text query `q` and opening a note), and `/search-runs`, verifying that data renders read-only with the exact fields exposed by the API and without any mutating controls.

**Acceptance Scenarios**:

1. **Given** an owner navigating to `/criteria`, **When** the view loads, **Then** screening criteria are displayed showing `name`, `weight`, `ruleText`, `effectiveFrom`, `effectiveTo`, and `isActive`.
2. **Given** an owner navigating to `/notes`, **When** querying notes by text query `q`, **Then** matching notes are returned via `GET /api/v1/notes` and displayed in read-only mode.
3. **Given** an owner on `/search-runs`, **When** viewing historical executions, **Then** past search runs are listed showing `runDate`, `companiesSearched`, `postingsFound`, `newPostings`, `errorsCount`, and `summary`.
4. **Given** any view in the management UI, **When** inspecting available actions, **Then** contacts, note editing, criteria editing, import runs, and search run triggers are completely absent.

---

### Edge Cases

- **Session Expiration during Form Edit**: If a session expires while an owner is drafting a disposition reason or editing a company, submitting the form redirects to Identity for re-authentication. Unsaved form values are not persisted and are lost; upon successful re-login, the user returns to the form route with an informative banner.
- **Direct Browser API Attempts**: Because the REST API does not enable CORS and is bound to internal network/loopback interfaces, any direct browser `fetch` or script invocation fails immediately. All calls are routed through server loaders/actions.
- **Hostile Script Tags in Postings and Notes**: If an imported posting, note, or signal contains content such as `<script>alert('pwn')</script>` or markdown images, the text is sanitized and rendered strictly as text nodes in the DOM.
- **Concurrent Single-Flight Token Refresh**: When 10 concurrent requests arrive for an expired access token, exactly 1 token exchange call is made to Identity. The other 9 requests await the result and use the refreshed token without rotating tokens multiple times.
- **Identity Host Offline**: If the Identity host or API is unreachable, the web application renders a clear "Service Temporarily Unavailable" screen conforming to Design System EmptyState components (`web/app/ds/components/src/feedback/EmptyState.jsx`), detailing connectivity remediation without exposing internal stack traces.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The web application MUST be server-rendered using React Router v7 in framework mode.
- **FR-002**: The web application MUST act as a confidential OpenID Connect client (`roleradar-web`) of the 005a Identity host, utilizing the Authorization Code grant with PKCE (`S256`).
- **FR-003**: The browser client MUST NEVER receive or store access tokens, refresh tokens, or ID tokens. Tokens MUST be stored and managed exclusively on the web application server.
- **FR-004**: All communication with the 005a REST API MUST be conducted server-side using the generated typed client (`contracts/client/api-types.ts` and OpenAPI contract `contracts/openapi/roleradar-api.json`). Hand-written untyped fetch calls to the API are forbidden.
- **FR-005**: The web application session cookie MUST be configured with `HttpOnly`, `Secure`, and `SameSite=Lax` (or stricter), containing only an opaque, cryptographically signed session identifier mapping to a server-side session store. Sessions survive until explicit logout or web container restart (re-login on container restart is accepted).
- **FR-006**: Every state-changing HTTP route (POST, PATCH, PUT, DELETE) MUST enforce CSRF protection.
- **FR-007**: Standard security headers including Content Security Policy (CSP), `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, and `Referrer-Policy: strict-origin-when-cross-origin` MUST be served on all web responses.
- **FR-008**: The OIDC redirect URIs and post-logout URIs for the web application MUST be configurable and registered on the Identity host client configuration, replacing the prior API port 5002 default.
- **FR-009**: Token refresh MUST be single-flight: concurrent requests arriving with an expired or expiring access token MUST queue behind a single refresh request to Identity so duplicate refresh requests are never dispatched.
- **FR-010**: When the REST API responds with HTTP 403 Forbidden, the application MUST render a dedicated, non-looping "Not Authorized" view explaining that the authenticated subject is not on the API allowlist.
- **FR-011**: Job postings list (`/postings`) MUST provide server-side paging and filtering strictly bounded by parameters supported in `contracts/openapi/roleradar-api.json` (`disposition`, `companyId`, `resurfacePolicy`, `minScore`, `q`, `limit`, `offset`). Any column sorting in the table MUST be executed strictly on the currently loaded page rows and clearly labelled as page-local sorting.
- **FR-012**: All untrusted text fields returned by API DTOs (including posting titles, locations, compensation `rawText`, assessment note `summary`, signals, note bodies/headings, application event notes, and search run summaries) MUST be treated as untrusted and rendered strictly as escaped plain text—never parsed or rendered as HTML, rich text, or markdown.
- **FR-013**: Disposition transitions MUST issue `POST /api/v1/postings/{id}/transition` with `toDisposition`, `reason`, `criterionId` (nullable UUID), `versionToken`, and optional `resurfacePolicy` and `overrideReason`. Target `applied` MUST NOT be permitted via transition (applications are created via `/applications`). The UI MUST offer all general non-applied target disposition options and map API matrix rejections (`InvalidTransition`, 400) to user-facing error messages.
- **FR-014**: When a disposition transition or company update fails with HTTP 409 Conflict (`concurrency_conflict`), the UI MUST display a clear conflict message with the current state and provide an action to reload the latest entity state.
- **FR-015**: API error envelopes (validation errors, domain rejections) MUST map directly to user-facing form field error messages.
- **FR-016**: The UI MUST support viewing and updating company profiles via `PATCH /api/v1/companies/{id}`. The companies list (`/companies`) MUST provide pagination only, without search or filter controls.
- **FR-017**: The UI MUST support creating applications from eligible postings (transitioning the posting to `applied`) and appending chronological timeline events to existing applications. Because the API has no batch lookup endpoint, the web server MUST resolve posting and company names with one GET per distinct posting and company on the page, bounded by the page size, deduplicated, executed concurrently, and cached for the request.
- **FR-018**: The UI MUST provide read-only views for screening criteria (`/criteria` showing `name`, `weight`, `ruleText`, `effectiveFrom`, `effectiveTo`, `isActive`), narrative notes search and read (`/notes` supporting text search `q` only), and search run history (`/search-runs` showing `runDate`, `companiesSearched`, `postingsFound`, `newPostings`, `errorsCount`, `summary`).
- **FR-019**: Contacts, manual note creation/editing, criteria creation/editing, import processing, and search run triggering MUST NOT be exposed in the UI (these remain CLI/MCP only).
- **FR-020**: All UI components MUST be constructed using Matt's Design System tokens and components, documented in `web/app/ds` with an up-to-date `SYNC.md`.
- **FR-021**: The UI MUST use semantic `--ds-*` design tokens exclusively. No hardcoded hex colors or raw pixel sizes are allowed outside `web/app/ds`.
- **FR-022**: The UI MUST support both light and dark themes with zero theme flicker (FOUC) on page load, using system preferences with manual toggle.
- **FR-023**: Every interactive element MUST display visible focus indicators, satisfy WCAG AA contrast ratios, support complete keyboard navigation, and respect `prefers-reduced-motion`.
- **FR-024**: The project orchestration MUST use Nx with the first-party `@nx/dotnet` plugin (Nx 22 or later) managing both .NET and web projects from the root `package.json` with a pinned pnpm lockfile.
- **FR-025**: The web application container image MUST be pinned by digest, run as an unprivileged non-root user with a read-only root filesystem and tmpfs `/tmp`, expose a loopback health check, and bind ports strictly to `127.0.0.1`.
- **FR-026**: An automated architecture check MUST verify that the web application depends only on standard OpenID Connect protocols and libraries and never references Duende IdentityServer packages.
- **FR-027**: Automated tests MUST include Vitest component tests with axe accessibility checks on every route in both themes, route loader tests against a stub API generated from OpenAPI, and an end-to-end Playwright smoke test executing sign-in via Identity, posting list, transition, and audit verification via direct query to the test's isolated ephemeral MongoDB `audit_log` collection using fictional data.
- **FR-028**: An automated test MUST assert that no tokens (access, refresh, or ID tokens) appear in any response body, cookie value, or browser storage mechanism.

### Key Entities

- **User Session**: Server-side session record mapping from an opaque signed cookie identifier to the encrypted OIDC token set and expiration metadata. Held in a server-side store and scoped to the container lifecycle.
- **Job Posting**: Representation of an opportunity matching `PostingDetailDto`, containing `id`, `companyId`, `companyName`, `title`, `requisitionId`, `location`, `disposition`, `resurfacePolicy`, `score`, `versionToken`, `createdAt`, `updatedAt`, `url`, `compensation` (`currency`, `rawText`, `hasSalary`, `compHighPriority`), `signals` (`signalType`, `signalValue`, `detectedAt`), and `assessmentNote` (`id`, `summary`, `updatedAt`).
- **Company**: Tracked employer entity matching `CompanyDetailDto`, containing `id`, `name`, `status`, `ownershipType`, `description`, `headquarters`, `categoryTags`, `careerSources`, and `versionToken`.
- **Application**: Application record matching `ApplicationDetailDto`, containing `id`, `postingId`, `companyId`, `status`, `appliedAt`, and `events` (`eventType`, `eventDate`, `notes`).
- **Screening Criterion**: Criterion record matching `CriterionDto`, containing `id`, `name`, `weight`, `ruleText`, `effectiveFrom`, `effectiveTo`, and `isActive`.
- **Search Run**: Discovery run execution batch matching `SearchRunDto`, containing `id`, `runDate`, `companiesSearched`, `postingsFound`, `newPostings`, `errorsCount`, and `summary`.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Once the stack is running, the owner can complete the full end-to-end workflow (sign in via Identity, find a posting, change its disposition with a reason, and sign out) in under 3 minutes using the keyboard alone (stack startup and image build times excluded).
- **SC-002**: Automated accessibility testing with `axe-core` achieves zero serious and zero critical violations across all application routes in both light and dark themes.
- **SC-003**: 100% of untrusted text fields (including posting titles, locations, compensation rawText, assessment note summaries, signals, note bodies, and event notes) are rendered as plain text, with zero unescaped HTML or markdown formatting rendered in the DOM.
- **SC-004**: In an optimistic concurrency conflict scenario (HTTP 409), 100% of attempted conflicting writes display a recoverable notification and permit reloading without loss of user intent.
- **SC-005**: In token leakage tests across all HTTP endpoints, cookies, and client-side storage, exactly zero access tokens, refresh tokens, or ID tokens are exposed to the browser.
- **SC-006**: Domain and Application test coverage remains at or above the required 85% gate across all CI runs.
- **SC-007**: Web application automated test suites (Vitest component, route loader, and utility tests) meet the coverage target established in the implementation plan.
- **SC-008**: OpenAPI schema drift check passes with zero uncommitted differences between the backend API models and the generated client types.

---

## Assumptions & API Gaps

### API Gaps (Explicitly Excluded from Scope)
- **Server-Side Postings Sorting**: `GET /api/v1/postings` provides no sort parameter; sorting in the UI is explicitly page-local only.
- **Company Search & Filtering**: `GET /api/v1/companies` provides no search or filter query parameters; the companies list is paged only.
- **Allowed Transitions Endpoint**: The API exposes no discovery endpoint for valid target transitions; the UI presents all non-applied targets and maps domain matrix rejections from `POST /api/v1/postings/{id}/transition`.
- **Audit Log Query Endpoint**: The API exposes no read endpoint for audit entries; verification in end-to-end tests reads directly from the test's MongoDB `audit_log` collection.
- **No Batch Lookup Endpoint for Application Names**: `GET /api/v1/applications` returns IDs only (`postingId`, `companyId`); the API provides no batch lookup endpoint. The web server resolves names via concurrent, request-cached individual lookups bounded by page size.
- **Notes Tag Search**: `GET /api/v1/notes` supports text query search (`q`) only; search by tag is not supported by the API.

### Assumptions
- **Single Owner Context**: The management UI is single-tenant and designed solely for the job seeker (owner). Multi-user management, tenant isolation, and granular role-based permissions are not required.
- **No Direct Browser API Calls**: The 005a API does not enable Cross-Origin Resource Sharing (CORS) and does not need to, because all API calls are proxied or executed directly by the server-side web application runtime.
- **Session Lifecycle**: Server sessions are memory/file-backed on the web host mapping from a signed cookie carrying an opaque session id. Sessions surviving container restarts is not required; re-login upon container restart is accepted.
- **Draft Loss on Expiry**: If a session expires while editing a form, draft form values are lost upon re-authentication redirect; no offline draft storage is maintained.
- **Fictional Data Only**: All test fixtures, component stories, screenshots, and Playwright verification scripts use synthetic, fictional companies and roles per Constitution Principle I.
- **Network Boundaries**: Both the Identity host (`https://identity:5001`), the REST API (`https://api:5002`), and the web app (`https://localhost:5003` or containerized equivalent) communicate over internal container networks or secure loopback interfaces.
