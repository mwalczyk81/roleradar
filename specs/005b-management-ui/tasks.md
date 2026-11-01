---
description: "Task list for Feature 005b Management UI implementation"
---

# Tasks: 005b Management UI

**Input**: Design documents from `/specs/005b-management-ui/` (`spec.md`, `plan.md`, `research.md`, `data-model.md`, `contracts/`, `quickstart.md`)  
**Prerequisites**: Feature 005a REST API and Identity hosts, OpenAPI contract snapshot `contracts/openapi/roleradar-api.json`, generated client `contracts/client/api-types.ts`, and vendored design system in `web/app/ds` (kit version `1793458161-0e77`, copy-only).  
**Tests**: Tests are MANDATORY per Constitution Principle II (Test-First Development). For every user story, test tasks come first, are marked as expected to fail, and precede implementation tasks.  
**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.  

## Format: `- [ ] [TaskID] [P?] [Story?] Description with file path`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: User story identifier (`[US1]`, `[US2]`, `[US3]`, `[US4]`, `[US5]`, `[US6]`)
- Exact file paths included in all task descriptions

---

## Phase 1: Setup (Shared Infrastructure & Monorepo Configuration)

**Purpose**: Establish monorepo orchestration, web project structure, Vite bundler, TypeScript configuration, test harnesses, and CI automation.

- [x] T001 Initialize monorepo orchestration in root `package.json`, `nx.json`, and `pnpm-lock.yaml` using `@nx/dotnet` version `24.0.3` and `pnpm` version `10.4.0` with test caching disabled for `RoleRadar.Tests.Contract` and `RoleRadar.Tests.Integration`
- [x] T002 Update CI workflow in `.github/workflows/ci.yml` (depends on T001) so jobs run only what a change affects using `nx affected` for build, lint, and test with `nrwl/nx-set-shas` (pinned by commit SHA), running after `dotnet restore --locked-mode` and `pnpm install --frozen-lockfile`, keeping pre-commit, gitleaks, zizmor, OpenAPI drift check, 85% Domain and Application coverage gate, and image builds unconditional or path-gated, adding the web job to the existing Ubuntu runner, and ensuring web-only changes skip Testcontainers and docs-only changes run only pre-commit
- [x] T003 Scaffold web application project in `web/package.json` and `web/project.json` declaring dependencies (`react` 19.0.0, `react-dom` 19.0.0, `react-router` 7.18.5, `openid-client` 6.8.8, `openapi-fetch` 0.17.0, `lucide-react` 1.66.0, `cookie` 1.0.2)
- [x] T004 Configure TypeScript in `web/tsconfig.json` with `<Nullable>enable</Nullable>` equivalent compiler flags (`strict: true`, `noImplicitAny: true`, `allowJs: true`, `checkJs: false`) so consuming application code is strictly typed while vendored design system `.jsx` files are consumed via their `.d.ts` types
- [x] T005 [P] Configure Vite and React Router v7 framework mode in `web/vite.config.ts` and `web/app/routes.ts`
- [x] T006 [P] Configure Vitest 5.0.5 and React Testing Library in `web/vitest.config.ts` and `web/tests/setup.ts` with `@testing-library/react` 16.3.3 and `axe-core` 4.14.0 matchers
- [x] T007 [P] Configure ESLint / Stylelint rules in `web/eslint.config.js` banning `dangerouslySetInnerHTML` across `web/app/**` and forbidding raw hex colors or explicit `px` values outside `web/app/ds/**`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure and security plumbing that MUST be completed before ANY user story can be implemented.

**⚠️ CRITICAL**: No user story work can begin until this foundational phase is complete.

- [x] T008 Implement web host in-process TLS termination on port 5003 in `web/server.ts` reading development certificates from `deploy/certs/dev-cert.crt` and `dev-cert.key` and listening on `https://*:5003`
- [x] T009 [P] Implement per-response cryptographic CSP nonce generation (128-bit random base64) and standard security headers (`X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin`) in `web/server.ts`
- [x] T010 [P] Implement server-side AES-256-GCM token encryption and in-memory session store in `web/app/lib/session.server.ts` storing `sessionId`, `ownerSub`, `accessToken`, `refreshToken`, `idToken`, `expiresAt`, `createdAt`, `csrfToken`, with fail-closed validation of 32-byte `SESSION_ENCRYPTION_KEY` outside Development
- [x] T011 [P] Implement single-flight token refresh lock in `web/app/lib/session.server.ts` using an in-flight Promise registry (`inFlightRefreshes.get(sessionId)`) so concurrent requests await a single `refreshTokenGrant` execution without duplicate rotation against `RefreshTokenUsage=OneTimeOnly`
- [x] T012 [P] Implement OpenID Connect client wrapper in `web/app/lib/oidc.server.ts` using `openid-client` version `6.8.8` (executing `client.discovery` from `ROLERADAR_IDENTITY_ISSUER_URI`, PKCE `S256` generation, `client.authorizationCodeGrant`, `client.refreshTokenGrant`, and `client.buildEndSessionUrl`)
- [x] T013 [P] Implement synchronizer CSRF token generator and validator in `web/app/lib/csrf.server.ts`
- [x] T014 [P] Implement strongly-typed API client wrapper in `web/app/lib/api-client.server.ts` using `openapi-fetch` version `0.17.0` importing `paths` from `contracts/client/api-types.ts` and implementing `mapApiError` to map `ErrorEnvelope` (`{ ok: false, errorCode: string, message: string, details?: ErrorFieldDetail[] }`) to `UiError`
- [x] T015 Implement root HTML document shell and zero-FOUC theme initialization script in `web/app/root.tsx` rendering nonced `<script>` before paint that applies `data-theme` attribute to `<html>` from cookie or system preference, linking `tokens.css` and `components/bundle.css`
- [x] T016 [P] Update Identity client default redirect URIs in `src/RoleRadar.Identity/Configuration/IdentityServerConfig.cs` setting `Clients:Web:RedirectUri` default to `https://localhost:5003/auth/callback` and `Clients:Web:PostLogoutRedirectUri` default to `https://localhost:5003/auth/logout`
- [x] T017 [P] Create multi-stage web application Dockerfile in `deploy/Dockerfile.web` (using `node:22-alpine@sha256:fe4f6d74e20370acb04ad889077d14d0f64a9c8ea6a42bc60cddf1e844699914`, unprivileged `node:node` UID 1000, read-only rootfs, tmpfs `/tmp`, loopback healthcheck) and register `web` service in `deploy/docker-compose.apps.yml` binding port `127.0.0.1:5003:5003` with `NODE_EXTRA_CA_CERTS=/https/dev-cert.crt`

**Checkpoint**: Foundation ready — all user stories can now be implemented independently.

---

## Phase 3: User Story 1 - Secure Authentication & Session Management (Priority: P1) 🎯 MVP

**Goal**: Deliver unauthenticated redirect to Identity, PKCE authorization code exchange, server-held encrypted session cookie (`__Host-roleradar_session`), single-flight token refresh, friendly 403 Not Authorized page, and RP-initiated logout.

**Independent Test**: Navigate to `/postings` while unauthenticated; confirm redirect to Identity login (`https://localhost:5001/Account/Login`); complete login with owner credentials and TOTP; verify browser receives an `HttpOnly`, `Secure`, `SameSite=Lax` cookie with an opaque ID and zero tokens; verify 10 concurrent requests trigger exactly 1 token refresh; trigger logout and confirm session destruction at web host and Identity.

### Tests for User Story 1 (MANDATORY - Test-First per Principle II) ⚠️
> **NOTE: Write these tests FIRST and verify they FAIL before implementation.**

- [x] T018 [P] [US1] Unit test for AES-256-GCM encryption, fail-closed key validation, and single-flight token refresh concurrency in `web/tests/unit/session.server.test.ts` (expected to fail before implementation)
- [x] T019 [P] [US1] Unit and mock integration test for OIDC discovery, PKCE verification, and token exchange in `web/tests/unit/oidc.server.test.ts` (expected to fail before implementation)
- [x] T020 [P] [US1] Route loader and action tests for `/auth/callback` and `/auth/logout` asserting cookie flags and session rotation in `web/tests/routes/auth.test.ts` (expected to fail before implementation)
- [x] T021 [P] [US1] Component and axe accessibility tests for Not Authorized (`/not-authorized`) and Service Unavailable (`/service-unavailable`) in `web/tests/routes/error-views.test.ts` (expected to fail before implementation)

### Implementation for User Story 1

- [x] T022 [US1] Implement session authentication guard in `web/app/lib/auth-guard.server.ts` checking session validity, invoking single-flight refresh when `expiresAt - now < 60000`, and redirecting unauthenticated requests to Identity's authorization endpoint with PKCE `code_challenge`
- [x] T023 [US1] Implement OIDC authorization callback route in `web/app/routes/auth.callback.tsx` validating `state`, exchanging code via `client.authorizationCodeGrant`, generating new session ID, encrypting tokens into session store, setting `__Host-roleradar_session` cookie, and redirecting to `/postings`
- [x] T024 [US1] Implement OIDC sign-out route action in `web/app/routes/auth.logout.tsx` validating `_csrf`, clearing session store, expiring session cookie, and redirecting to Identity's end-session URL with `post_logout_redirect_uri=https://localhost:5003/auth/logout`
- [x] T025 [P] [US1] Implement Not Authorized view in `web/app/routes/not-authorized.tsx` rendering a clear explanation when the REST API returns 403 Forbidden without redirect looping
- [x] T026 [P] [US1] Implement Service Unavailable view in `web/app/routes/service-unavailable.tsx` conforming to Design System EmptyState primitives (`web/app/ds/components/src/feedback/EmptyState.jsx`) and displaying remediation command `pwsh ./scripts/start-services.ps1`
- [x] T027 [US1] Implement root route redirect in `web/app/routes/_index.tsx` routing authenticated visitors to `/postings`

**Checkpoint**: User Story 1 is functional and testable as an independent MVP increment.

---

## Phase 4: User Story 2 - Browse, Search, and Inspect Job Postings (Priority: P1)

**Goal**: Deliver paged postings table with search and filter chips strictly constrained to 005a API query parameters (`disposition`, `companyId`, `resurfacePolicy`, `minScore`, `q`, `limit`, `offset`), page-local column sorting, posting detail view with exact `PostingDetailDto` fields, and strict plain-text escaping for all untrusted text.

**Independent Test**: Seed postings in MongoDB; log in; filter postings table via query params (`disposition=scored`, `minScore=80`, `q=engineer`); verify pagination counts match API; sort a column and verify page-local sorting badge; open a posting containing `<script>alert('xss')</script>` in its title or compensation text and confirm it renders as plain text.

### Tests for User Story 2 (MANDATORY - Test-First per Principle II) ⚠️
> **NOTE: Write these tests FIRST and verify they FAIL before implementation.**

- [x] T028 [P] [US2] Route loader test for `GET /postings` in `web/tests/routes/postings.index.test.ts` verifying parameter mapping for `disposition`, `companyId`, `resurfacePolicy`, `minScore`, `q`, `limit`, and `offset` against stubbed API (expected to fail before implementation)
- [x] T029 [P] [US2] Component and axe accessibility tests for Postings DataTable and Toolbar in `web/tests/components/postings-table.test.ts` verifying page-local sorting and keyboard navigation in light and dark themes (expected to fail before implementation)
- [x] T030 [P] [US2] Untrusted text escaping security test in `web/tests/components/posting-detail-safety.test.ts` asserting HTML tags and markdown formatting in `title`, `location`, `compensation.rawText`, `assessmentNote.summary`, and `signals` render purely as plain text DOM leaf nodes (expected to fail before implementation)
- [x] T031 [P] [US2] Route loader test for `GET /postings/:id` in `web/tests/routes/postings.detail.test.ts` asserting exact `PostingDetailDto` field mapping (`id`, `companyId`, `companyName`, `title`, `requisitionId`, `location`, `disposition`, `resurfacePolicy`, `score`, `versionToken`, `createdAt`, `updatedAt`, `url`, `compensation`, `signals`, `assessmentNote`) (expected to fail before implementation)
- [x] T032 [P] [US2] Vitest test and typecheck in `web/tests/components/vendored-composites.test.ts` importing Part C composites from `web/app/ds/components/src` (`data/DataTable.jsx`, `data/Toolbar.jsx`, `layout/AppShell.jsx`, `feedback/EmptyState.jsx`, `feedback/Skeleton.jsx`, `feedback/Toast.jsx`, `feedback/ToastRegion.jsx`, `overlay/Menu.jsx`, `overlay/Tooltip.jsx`), rendering each with route props (`AppShell` with `navItems`, `linkComponent`, `title`, `theme`, `onThemeToggle`, `userLabel`, `signOut`, `headerActions`), and verifying typecheck through vendored `.d.ts` declaration files (expected to fail before implementation)

### Implementation for User Story 2

- [x] T033 [US2] Implement postings list route loader and page in `web/app/routes/postings._index.tsx` fetching `GET /api/v1/postings`, parsing query parameters (`disposition`, `companyId`, `resurfacePolicy`, `minScore`, `q`, `limit`, `offset`), and rendering `web/app/ds/components/src/layout/AppShell.jsx`, `web/app/ds/components/src/data/Toolbar.jsx`, and `web/app/ds/components/src/data/DataTable.jsx` with page-local sort
- [x] T034 [US2] Implement posting detail route loader and view in `web/app/routes/postings.$id.tsx` fetching `GET /api/v1/postings/{id}`, rendering all `PostingDetailDto` fields in `web/app/ds/components/src/layout/AppShell.jsx` with `web/app/ds/components/src/feedback/EmptyState.jsx` and `web/app/ds/components/src/feedback/Skeleton.jsx`, and ensuring all untrusted string fields are rendered as React plain text leaf nodes

**Checkpoint**: User Stories 1 and 2 deliver complete read access to job opportunities.

---

## Phase 5: User Story 3 - Change Dispositions with Conflict Handling (Priority: P1)

**Goal**: Deliver disposition transition dialog on postings (excluding `applied`), submission of required `reason`, nullable `criterionId`, optional `resurfacePolicy`, `overrideReason`, mapping 400 `InvalidTransition` matrix rejections, and handling 409 Concurrency Conflicts with a reload prompt.

**Independent Test**: Open a posting on `/postings/:id`; submit a transition to `scored` with a reason; verify `POST /api/v1/postings/{id}/transition` succeeds; verify target `applied` is not selectable; trigger a transition from an invalid state and confirm 400 matrix rejection maps inline; inject an outdated `versionToken` and verify 409 Conflict Toast appears with reload action.

### Tests for User Story 3 (MANDATORY - Test-First per Principle II) ⚠️
> **NOTE: Write these tests FIRST and verify they FAIL before implementation.**

- [x] T035 [P] [US3] Route action test for `POST /postings/:id` in `web/tests/routes/postings.transition.test.ts` verifying payload mapping (`toDisposition`, `reason`, `criterionId`, `versionToken`, `resurfacePolicy`, `overrideReason`), CSRF validation, 400 error mapping, and 409 conflict handling (expected to fail before implementation)
- [x] T036 [P] [US3] Component and axe accessibility tests for Transition dialog, Toast, ToastRegion, and Menu in `web/tests/components/transition-dialog.test.ts` in both light and dark themes (expected to fail before implementation)

### Implementation for User Story 3

- [x] T037 [US3] Implement disposition transition dialog in `web/app/components/TransitionPostingDialog.tsx` presenting valid target options per the disposition matrix (target `applied` is never offered), inputs for required `reason`, nullable `criterionId` (UUID string), optional `resurfacePolicy` (`never`, `if_reopened`), optional `overrideReason`, and hidden `_csrf` and `versionToken`
- [x] T038 [US3] Implement transition server action in `web/app/routes/postings.$id.tsx` issuing `POST /api/v1/postings/{id}/transition` with `TransitionPostingRequestDto`, capturing 400 `InvalidTransition` errors into form validation state, and capturing 409 Conflict responses
- [x] T039 [US3] Integrate 409 Concurrency Conflict notification and reload handler in `web/app/routes/postings.$id.tsx` and `web/app/routes/postings._index.tsx` using `web/app/ds/components/src/feedback/Toast.jsx` and `web/app/ds/components/src/feedback/ToastRegion.jsx` to alert the user when an entity was modified elsewhere and offer a single-click reload action

**Checkpoint**: User Stories 1, 2, and 3 complete all P1 features.

---

## Phase 6: User Story 4 - Company Profile Management (Priority: P2)

**Goal**: Deliver paged company list (pagination only; no search/filter bar per API gap), company detail inspection, and PATCH company profile editing with optimistic concurrency version tokens.

**Independent Test**: Navigate to `/companies`; verify paged list displays companies from `GET /api/v1/companies` with pagination controls only; navigate to `/companies/:id`; modify description and headquarters; submit form; verify `PATCH /api/v1/companies/{id}` updates the record; verify 409 conflict on stale update.

### Tests for User Story 4 (MANDATORY - Test-First per Principle II) ⚠️
> **NOTE: Write these tests FIRST and verify they FAIL before implementation.**

- [x] T040 [P] [US4] Route loader tests for `GET /companies` (asserting pagination only without search/filter parameters) and `GET /companies/:id` in `web/tests/routes/companies.loader.test.ts` (expected to fail before implementation)
- [x] T041 [P] [US4] Route action test for `PATCH /companies/:id` in `web/tests/routes/companies.action.test.ts` verifying `UpdateCompanyRequestDto` payload mapping (`name`, `status`, `ownershipType`, `description`, `headquarters`, `categoryTags`, `careerSources`, `versionToken`), CSRF validation, and 409 conflict handling (expected to fail before implementation)
- [x] T042 [P] [US4] Axe accessibility check for company list and edit forms in `web/tests/routes/companies.a11y.test.ts` (expected to fail before implementation)

### Implementation for User Story 4

- [x] T043 [US4] Implement companies list route loader and page in `web/app/routes/companies._index.tsx` calling `GET /api/v1/companies` with `limit` and `offset`, importing vendored components via `web/app/ui.ts` (`AppShell`, `DataTable`, etc.) with pagination controls only
- [x] T044 [US4] Implement company detail view and edit form in `web/app/routes/companies.$id.tsx` importing vendored components via `web/app/ui.ts` (`AppShell`, etc.), rendering all fields from `CompanyDetailDto`, providing editable form fields for `name`, `status`, `ownershipType`, `description`, `headquarters`, `categoryTags`, and `careerSources`, with hidden `_csrf` and `versionToken`
- [x] T045 [US4] Implement company update server action in `web/app/routes/companies.$id.tsx` validating CSRF, calling `PATCH /api/v1/companies/{id}` with `UpdateCompanyRequestDto`, mapping validation errors to form fields, and handling 409 concurrency conflicts with `web/app/ui.ts` (`Toast`, etc.)

**Checkpoint**: User Story 4 is functional and testable independently.

---

## Phase 7: User Story 5 - Application Lifecycle Tracking (Priority: P2)

**Goal**: Deliver formal application creation from posting (transitioning posting to `applied`), `/applications` list with bounded deduplicated concurrent name resolution, and timeline event append.

**Independent Test**: Select an eligible posting; click "Create Application"; submit application with submission date and compensation; verify `POST /api/v1/applications` succeeds and posting transitions to `applied`; navigate to `/applications`; verify posting and company names appear via deduplicated concurrent lookups; open `/applications/:id` and append a timeline event via `POST /api/v1/applications/{id}/events`.

### Tests for User Story 5 (MANDATORY - Test-First per Principle II) ⚠️
> **NOTE: Write these tests FIRST and verify they FAIL before implementation.**

- [x] T046 [P] [US5] Route loader test for `GET /applications` in `web/tests/routes/applications.loader.test.ts` asserting deduplicated concurrent lookups for distinct posting and company IDs bounded by page size with request caching (expected to fail before implementation)
- [x] T047 [P] [US5] Route action tests for `POST /applications` (`RecordApplicationRequestDto`: `postingId`, `submittedDate`, `resumeVariant`, `desiredSalaryMin`, `desiredSalaryMax`, `desiredSalaryCurrency`, `targetStartDate`, `resumePointer`, `confirmationCode`, `overrideDecided`, `overrideReason`) and `POST /applications/:id/events` (`AppendApplicationEventRequestDto`: `eventType`, `eventDate`, `notes`, `eventSource`) in `web/tests/routes/applications.action.test.ts` (expected to fail before implementation)
- [x] T048 [P] [US5] Axe accessibility check for applications list, detail, and timeline in `web/tests/routes/applications.a11y.test.ts` in both light and dark themes (expected to fail before implementation)

### Implementation for User Story 5

- [x] T049 [US5] Implement bounded deduplicated concurrent name resolution helper in `web/app/lib/application-context.server.ts` extracting unique `postingId`s and `companyId`s from current page slice, executing parallel `Promise.all` GET requests, and caching results for the request
- [x] T050 [US5] Implement applications list route loader and page in `web/app/routes/applications._index.tsx` calling `GET /api/v1/applications`, resolving names via `resolveApplicationContext`, and rendering `web/app/ds/components/src/layout/AppShell.jsx` and `web/app/ds/components/src/data/DataTable.jsx`
- [x] T051 [US5] Implement create application dialog component in `web/app/components/CreateApplicationDialog.tsx` prompting for `submittedDate` (YYYY-MM-DD), `resumeVariant` (`tailored`, `as_is`, `ic_framed`), optional `desiredSalaryMin`, `desiredSalaryMax`, `desiredSalaryCurrency`, optional `targetStartDate`, `resumePointer`, `confirmationCode`, showing `overrideDecided` and `overrideReason` when the posting requires an override (`screened_out`, `closed`), with hidden `_csrf` and `postingId`
- [x] T052 [US5] Implement application creation server action in `web/app/routes/postings.$id.tsx` issuing `POST /api/v1/applications` and revalidating the posting to applied
- [x] T053 [US5] Implement application detail route loader, timeline view, and append event action in `web/app/routes/applications.$id.tsx` rendering `ApplicationDetailDto` events in `web/app/ds/components/src/layout/AppShell.jsx` and handling `POST /api/v1/applications/{id}/events`

**Checkpoint**: User Story 5 is functional and testable independently.

---

## Phase 8: User Story 6 - Read-Only Operational Views: Criteria, Notes & Search Runs (Priority: P3)

**Goal**: Deliver read-only views for screening criteria (exact fields: `name`, `weight`, `ruleText`, `effectiveFrom`, `effectiveTo`, `isActive`), narrative notes (text search `q` only), and automated search run batches (`runDate`, `companiesSearched`, `postingsFound`, `newPostings`, `errorsCount`, `summary`), with mutation/contact controls excluded.

**Independent Test**: Navigate to `/criteria`; verify criteria table displays exact fields and has no edit/create buttons; navigate to `/notes`; enter search query `q=interview`; verify matching notes appear and note detail displays read-only plain text; navigate to `/search-runs`; verify historical batches are listed with exact fields.

### Tests for User Story 6 (MANDATORY - Test-First per Principle II) ⚠️
> **NOTE: Write these tests FIRST and verify they FAIL before implementation.**

- [x] T054 [P] [US6] Route loader and rendering tests for `/criteria`, `/notes`, `/notes/:id`, and `/search-runs` in `web/tests/routes/operational-views.test.ts` asserting exact DTO field displays and zero mutation controls (expected to fail before implementation)
- [x] T055 [P] [US6] Axe accessibility tests for all read-only operational views in both light and dark themes in `web/tests/routes/operational.a11y.test.ts` (expected to fail before implementation)

### Implementation for User Story 6

- [x] T056 [P] [US6] Implement criteria list route loader and page in `web/app/routes/criteria.tsx` calling `GET /api/v1/criteria` and rendering read-only `web/app/ds/components/src/data/DataTable.jsx` in `web/app/ds/components/src/layout/AppShell.jsx` showing `name`, `weight`, `ruleText`, `effectiveFrom`, `effectiveTo`, and `isActive`
- [x] T057 [P] [US6] Implement notes search list in `web/app/routes/notes._index.tsx` calling `GET /api/v1/notes` with text search query `q` only and rendering summary list using `web/app/ds/components/src/layout/AppShell.jsx` and `web/app/ds/components/src/data/DataTable.jsx`
- [x] T058 [P] [US6] Implement note detail view in `web/app/routes/notes.$id.tsx` calling `GET /api/v1/notes/{id}` and rendering note content as escaped plain text without edit controls in `web/app/ds/components/src/layout/AppShell.jsx`
- [x] T059 [P] [US6] Implement search runs history view in `web/app/routes/search-runs.tsx` calling `GET /api/v1/search-runs` and rendering table in `web/app/ds/components/src/layout/AppShell.jsx` showing `runDate`, `companiesSearched`, `postingsFound`, `newPostings`, `errorsCount`, and `summary`

**Checkpoint**: All user stories (P1, P2, P3) are implemented.

---

## Phase 9: Polish, Cross-Cutting Invariants & End-to-End Verification

**Purpose**: System-wide security verifications, architecture enforcement, CI affected workflow verification, Playwright smoke tests, and final gate checks.

- [x] T060 [P] Architecture verification test in `web/tests/architecture/dependency-rules.test.ts` asserting that `web/` packages do not import or reference Duende IdentityServer packages (FR-026)
- [x] T061 [P] Token leakage assertion test in `web/tests/architecture/token-leakage.test.ts` asserting that zero access, refresh, or ID tokens appear in any response body for every route (including error and /not-authorized pages), in Set-Cookie values, and in localStorage/sessionStorage/IndexedDB after a real Chromium login-free session built from the stubbed harness (FR-028, SC-005)
- [x] T062 [P] End-to-end real-stack Playwright test executing against real Identity, API, and Web container images and an isolated Mongo replica set on a throwaway network with unique run-id names, random free ports on 127.0.0.1, temporary volumes, runtime-generated secrets/certs, and full finally cleanup; automating owner login with TOTP, posting search and filter, disposition transition, application creation, company editing, sign-out, unauthenticated /postings re-challenge assertion, and direct audit_log MongoDB validation (FR-027, SC-001)
- [x] T063 [P] Verify web application code coverage via Vitest coverage provider with pinned dependencies asserting >= 80% line coverage and >= 75% branch coverage on `web/app/**` excluding `web/app/ds/**`, wired as an Nx target and CI web job step (SC-007)
- [x] T064 [P] Confirm CI affected workflow behavior and path-gating in `scripts/verify-ci-affected.ps1` against current `ci.yml` without rewriting the script
- [x] T065 Review and update `specs/005b-management-ui/quickstart.md` to be copy-paste correct for both PowerShell 5.1 and pwsh, verifying all commands and variables against repo and `deploy/docker-compose.apps.yml`, and listing human-required steps explicitly
- [x] T066 Run full build and test verification gate via `pnpm exec nx run-many -t lint test build`, web test verification list (`test:e2e --repeat-each=3`), real-stack E2E, and `pwsh ./scripts/review-gate.ps1` with `-IncludeE2E` flag

---

## Dependencies & Execution Order

### Phase Dependencies
- **Setup (Phase 1)**: No dependencies — executes first. T002 depends on T001.
- **Foundational (Phase 2)**: Depends on Phase 1 completion — **BLOCKS all user stories**.
- **User Story 1 (Phase 3 - P1) [MVP]**: Depends on Phase 2 completion.
- **User Story 2 (Phase 4 - P1)**: Depends on Phase 2 and US1 authentication guard.
- **User Story 3 (Phase 5 - P1)**: Depends on US2 posting detail views.
- **User Story 4 (Phase 6 - P2)**: Depends on Phase 2; can execute in parallel with US2/US3.
- **User Story 5 (Phase 7 - P2)**: Depends on US2 posting detail (for application creation).
- **User Story 6 (Phase 8 - P3)**: Depends on Phase 2; can execute in parallel with other stories.
- **Polish (Phase 9)**: Depends on all user stories being complete.

### User Story Dependencies Graph

```text
[Phase 1: Setup]
       │
       ▼
[Phase 2: Foundational (TLS, Sessions, OIDC, API Client, Theme, Container)]
       │
       ├──────────────────────────────────────────┬──────────────────────┐
       ▼                                          ▼                      ▼
[Phase 3: US1 Auth & Session (MVP)]      [Phase 6: US4 Companies]  [Phase 8: US6 Operational]
       │
       ▼
[Phase 4: US2 Postings Browse & Detail]
       │
       ├─────────────────────────┐
       ▼                         ▼
[Phase 5: US3 Transitions]  [Phase 7: US5 Applications]
       │                         │
       └───────────┬─────────────┘
                   ▼
         [Phase 9: Polish & E2E Verification]
```

---

## Parallel Example: User Story 1

```bash
# Launch all tests for User Story 1 in parallel FIRST (mandatory - verify they fail before implementing):
Task: "Unit test for AES-256-GCM encryption, fail-closed key validation, and single-flight token refresh concurrency in web/tests/unit/session.server.test.ts"
Task: "Unit and mock integration test for OIDC discovery, PKCE verification, and token exchange in web/tests/unit/oidc.server.test.ts"
Task: "Route loader and action tests for /auth/callback and /auth/logout in web/tests/routes/auth.test.ts"
Task: "Component and axe accessibility tests for Not Authorized and Service Unavailable in web/tests/routes/error-views.test.ts"

# Implement error views in parallel:
Task: "Implement Not Authorized view in web/app/routes/not-authorized.tsx"
Task: "Implement Service Unavailable view in web/app/routes/service-unavailable.tsx"
```

---

## Parallel Example: User Story 2

```bash
# Launch all tests for User Story 2 in parallel (mandatory - verify they fail before implementing):
Task: "Route loader test for GET /postings in web/tests/routes/postings.index.test.ts"
Task: "Component and axe accessibility tests for Postings DataTable and Toolbar in web/tests/components/postings-table.test.ts"
Task: "Untrusted text escaping security test in web/tests/components/posting-detail-safety.test.ts"
Task: "Route loader test for GET /postings/:id in web/tests/routes/postings.detail.test.ts"
Task: "Vitest test and typecheck in web/tests/components/vendored-composites.test.ts"

# Implement routes:
Task: "Implement postings list route loader and page in web/app/routes/postings._index.tsx"
Task: "Implement posting detail route loader and view in web/app/routes/postings.$id.tsx"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)
1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (blocking prerequisites)
3. Complete Phase 3: User Story 1
4. **VALIDATE MVP**: Verify unauthenticated redirect to Identity, TOTP sign-in, token storage isolation, single-flight token refresh, and sign-out.

### Incremental Delivery
1. **Increment 1 (MVP)**: Secure authentication and session lifecycle (US1).
2. **Increment 2 (Core Reading)**: Browse, search, filter, and inspect job postings with safe plain-text rendering (US2).
3. **Increment 3 (Core Mutations)**: Disposition transitions with optimistic concurrency conflict handling (US3).
4. **Increment 4 (Entity Lifecycle)**: Company profiles and application tracking with concurrent name resolution (US4, US5).
5. **Increment 5 (System Visibility)**: Operational read-only views for criteria, notes, and search runs (US6).
6. **Increment 6 (Hardened Polish)**: Architecture enforcement, token leakage assertion, CI affected verification, Playwright smoke tests, and Docker Compose stack verification (Phase 9).
