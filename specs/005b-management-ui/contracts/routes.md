# Interface Contract: Route Loaders and Server Actions

**Feature**: `005b-management-ui`  
**Date**: 2026-10-31  
**Status**: Completed  

All routes in the web application execute in React Router v7 framework mode. Loaders and Actions execute exclusively on the Node server; they are protected by session authentication and server-side CSRF validation.

---

## 1. Authentication & System Routes

### `GET /auth/callback`
- **Purpose**: Handles OIDC Authorization Code exchange redirect from Identity host (`https://identity:5001`).
- **Query Parameters**:
  - `code`: string (authorization code)
  - `state`: string (anti-tamper state)
- **Behavior**:
  1. Validates `state` against the transient cookie/state.
  2. Executes `client.authorizationCodeGrant` with PKCE verifier.
  3. Mints a new cryptographically random session ID and rotates the session cookie.
  4. Encrypts tokens and persists them into the in-memory session store.
  5. Redirects the browser to `/postings`.

### `POST /auth/logout`
- **Purpose**: Destroys the local session and initiates OIDC RP-Initiated Logout.
- **Form Data**:
  - `_csrf`: string (valid session CSRF synchronizer token)
- **Behavior**:
  1. Validates `_csrf` against session.
  2. Retrieves `idToken` from session store (if available).
  3. Evicts session from in-memory store and expires the session cookie.
  4. Generates an end-session redirect URL pointing to Identity's logout endpoint with `post_logout_redirect_uri=https://localhost:5003/postings`.
  5. Returns a 302 redirect to Identity.

### `GET /health`
- **Purpose**: In-container loopback healthcheck endpoint.
- **Access**: Unauthenticated, internal loopback only.
- **Response**: `200 OK` (`{ status: "ok" }`).

---

## 2. Core Job-Search Routes

### `GET /`
- **Behavior**: 302 redirect to `/postings`.

### `GET /postings`
- **Loader Contract**:
  - **Query Parameters (constrained strictly to 005a API)**:
    - `disposition?: string`
    - `companyId?: string` (UUID)
    - `resurfacePolicy?: string`
    - `minScore?: number` (integer)
    - `q?: string` (search text)
    - `limit?: number` (default: 25, max: 100)
    - `offset?: number` (default: 0)
  - **Backend Call**: `GET /api/v1/postings`
  - **Returned Loader Data**:
    ```typescript
    interface PostingsLoaderData {
      postings: PostingSummaryDto[];
      totalCount: number;
      limit: number;
      offset: number;
      activeFilters: {
        disposition: string | null;
        companyId: string | null;
        resurfacePolicy: string | null;
        minScore: number | null;
        q: string | null;
      };
    }
    ```
- **Sorting Rule**: Column sorting in the UI table is strictly page-local (operating on the current `postings` slice) and clearly labelled as page-local.

### `GET /postings/:id`
- **Loader Contract**:
  - **Path Parameter**: `id` (UUID)
  - **Backend Call**: `GET /api/v1/postings/{id}`
  - **Returned Loader Data**: `PostingDetailDto`
  - **Error Handling**: 404 renders NotFound EmptyState; 403 renders NotAuthorized view.

### `POST /postings/:id` (Transition Action)
- **Action Contract**:
  - **Form Data**:
    - `_csrf`: string (required)
    - `toDisposition`: string (required; cannot be `applied`)
    - `reason`: string (required)
    - `criterionId`: string | "" (optional/nullable UUID)
    - `versionToken`: string (required)
    - `resurfacePolicy`: string | "" (optional)
    - `overrideReason`: string | "" (optional)
  - **Backend Call**: `POST /api/v1/postings/{id}/transition`
  - **Responses**:
    - `200 OK` / Redirect: Reloads posting detail on success.
    - `400 Bad Request`: Returns field validation errors or matrix rejection (`InvalidTransition`).
    - `409 Conflict`: Returns `{ isConflict: true, currentVersionToken: string, message: string }` for reload Toast/Banner.

---

## 3. Company Routes

### `GET /companies`
- **Loader Contract**:
  - **Query Parameters**:
    - `limit?: number` (default: 25, max: 100)
    - `offset?: number` (default: 0)
  - **Backend Call**: `GET /api/v1/companies` (no search/filter parameters supported)
  - **Returned Loader Data**:
    ```typescript
    interface CompaniesLoaderData {
      companies: CompanySummaryDto[];
      totalCount: number;
      limit: number;
      offset: number;
    }
    ```

### `GET /companies/:id`
- **Loader Contract**:
  - **Path Parameter**: `id` (UUID)
  - **Backend Call**: `GET /api/v1/companies/{id}`
  - **Returned Loader Data**: `CompanyDetailDto`

### `POST /companies/:id` (Update Action)
- **Action Contract**:
  - **Form Data**:
    - `_csrf`: string (required)
    - `name?: string`
    - `status?: string`
    - `ownershipType?: string`
    - `description?: string`
    - `headquarters?: string`
    - `categoryTags?: string` (comma-separated or JSON)
    - `versionToken?: string`
  - **Backend Call**: `PATCH /api/v1/companies/{id}`
  - **Responses**:
    - `200 OK` on success.
    - `400 Bad Request`: Returns field errors.
    - `409 Conflict`: Returns concurrency conflict prompt with reload action.

---

## 4. Application Routes

### `GET /applications`
- **Loader Contract**:
  - **Query Parameters**:
    - `limit?: number` (default: 25)
    - `offset?: number` (default: 0)
  - **Backend Call**: `GET /api/v1/applications`
  - **Context Resolution**: Bounded concurrent lookup for distinct `postingId`s and `companyId`s across page slice.
  - **Returned Loader Data**:
    ```typescript
    interface ApplicationsLoaderData {
      applications: Array<ApplicationSummaryDto & {
        postingTitle: string;
        companyName: string;
      }>;
      totalCount: number;
      limit: number;
      offset: number;
    }
    ```

### `GET /applications/:id`
- **Loader Contract**:
  - **Path Parameter**: `id` (UUID)
  - **Backend Call**: `GET /api/v1/applications/{id}`
  - **Returned Loader Data**: `ApplicationDetailDto`

### `POST /applications` (Create Application Action)
- **Action Contract**:
  - **Form Data**:
    - `_csrf`: string (required)
    - `postingId`: string (UUID, required)
    - `appliedDate`: string (ISO 8601 UTC date, required)
    - `tailoredResumeVariant?: string`
    - `targetCompensation?: string`
  - **Backend Call**: `POST /api/v1/applications`
  - **Side Effect**: Postings transition to `applied`.
  - **Response**: Redirects to `/applications/{id}`.

### `POST /applications/:id/events` (Append Event Action)
- **Action Contract**:
  - **Form Data**:
    - `_csrf`: string (required)
    - `eventType`: string (required)
    - `eventDate`: string (ISO 8601 UTC date, required)
    - `notes?: string`
  - **Backend Call**: `POST /api/v1/applications/{id}/events`
  - **Response**: 200 OK / Reload application detail.

---

## 5. Read-Only Operational Routes

### `GET /criteria`
- **Loader Contract**:
  - **Backend Call**: `GET /api/v1/criteria`
  - **Returned Loader Data**:
    ```typescript
    interface CriteriaLoaderData {
      criteria: CriterionDto[]; // name, weight, ruleText, effectiveFrom, effectiveTo, isActive
    }
    ```

### `GET /notes`
- **Loader Contract**:
  - **Query Parameters**:
    - `q?: string` (text query search only; no tag search)
    - `limit?: number`
    - `offset?: number`
  - **Backend Call**: `GET /api/v1/notes`
  - **Returned Loader Data**:
    ```typescript
    interface NotesLoaderData {
      notes: NoteSummaryDto[];
      totalCount: number;
      limit: number;
      offset: number;
      q: string | null;
    }
    ```

### `GET /notes/:id`
- **Loader Contract**:
  - **Path Parameter**: `id` (UUID)
  - **Backend Call**: `GET /api/v1/notes/{id}`
  - **Returned Loader Data**: `NoteDto` (rendered read-only with plain text safety)

### `GET /search-runs`
- **Loader Contract**:
  - **Query Parameters**: `limit?: number`, `offset?: number`
  - **Backend Call**: `GET /api/v1/search-runs`
  - **Returned Loader Data**:
    ```typescript
    interface SearchRunsLoaderData {
      searchRuns: SearchRunDto[]; // runDate, companiesSearched, postingsFound, newPostings, errorsCount, summary
      totalCount: number;
    }
    ```

---

## 6. Standard Error Boundaries & Views

1. **`Not Authorized` (`403 Forbidden`)**:
   - Rendered when the REST API returns 403 (owner `sub` not present in `Owner:AllowedSubjects`).
   - Displays clear message explaining that the user account does not have access to the RoleRadar store.
   - Prevents infinite redirect loops.
2. **`Service Unavailable` (`503` / Connection Refused)**:
   - Rendered when the Identity host or REST API is unreachable.
   - Conforms to Design System `EmptyState` primitive.
   - Displays remediation command: `pwsh ./scripts/start-services.ps1`.
3. **`Not Found` (`404`)**:
   - Rendered when an entity ID is missing.
   - Conforms to Design System `EmptyState` with a button to return to the list view.
