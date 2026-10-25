# REST API Endpoints Contract

**Feature**: `005a-api-and-login` | **Date**: 2026-10-25

All endpoints require HTTPS and a valid Bearer token from an allowlisted owner subject, except `/health`.

Base URI: `/api/v1`

---

## 1. System Endpoints

### `GET /health`
- **Auth**: Anonymous (internal network probe)
- **Status**: 200 OK
- **Response**: `{"status": "Healthy"}`

---

## 2. Job Postings Endpoints

### `GET /api/v1/postings`
- **Description**: Query paged postings with optional filters.
- **Query Parameters**:
  - `disposition` (optional string): Filter by disposition (e.g. `tracked`, `screened_out`, `closed`).
  - `companyId` (optional UUID): Filter by company identifier.
  - `resurfacePolicy` (optional string): Filter by resurface policy.
  - `minScore` (optional integer): Minimum score threshold.
  - `q` (optional string): Text search query.
  - `limit` (optional integer, default 50, max 200): Page limit.
  - `offset` (optional integer, default 0): Item offset.
- **Status**: 200 OK
- **Response**: `PagedResult<PostingListItemDto>`

### `GET /api/v1/postings/{id}`
- **Description**: Retrieve detailed posting state including signals and linked assessment note summary.
- **Path Parameters**: `id` (UUID).
- **Status**:
  - 200 OK: `PostingDetailDto`
  - 404 Not Found: `ErrorEnvelope`

### `POST /api/v1/postings/{id}/transition`
- **Description**: Transition posting disposition with optimistic concurrency check.
- **Path Parameters**: `id` (UUID).
- **Request Body**: `TransitionPostingRequestDto`
  - `toDisposition` (string, required)
  - `reason` (string, required)
  - `criterionId` (UUID, optional)
  - `versionToken` (string, required)
- **Status**:
  - 200 OK: Updated `PostingDetailDto`
  - 400 Bad Request: Validation failure (`ErrorEnvelope`)
  - 404 Not Found: Posting not found (`ErrorEnvelope`)
  - 409 Conflict: Stale versionToken (`ErrorEnvelope` with `errorCode: "concurrency_conflict"`)
- **Audit**: Emits append-only audit entry with operation `posting_transition` and actor `api-user`.

---

## 3. Companies Endpoints

### `GET /api/v1/companies`
- **Description**: List companies with paging.
- **Query Parameters**: `limit` (default 50, max 200), `offset` (default 0).
- **Status**: 200 OK: `PagedResult<CompanyListItemDto>`

### `GET /api/v1/companies/{id}`
- **Description**: Retrieve company detail including career site sources.
- **Path Parameters**: `id` (UUID).
- **Status**:
  - 200 OK: `CompanyDetailDto`
  - 404 Not Found: `ErrorEnvelope`

### `PATCH /api/v1/companies/{id}`
- **Description**: Update company attributes and/or career sources. Omitted/null fields remain unchanged.
- **Path Parameters**: `id` (UUID).
- **Request Body**: `UpdateCompanyRequestDto`
- **Status**:
  - 200 OK: Updated `CompanyDetailDto`
  - 400 Bad Request: `ErrorEnvelope`
  - 404 Not Found: `ErrorEnvelope`
- **Audit**: Emits append-only audit entry with operation `company_update` and actor `api-user`.

---

## 4. Applications and Events Endpoints

### `GET /api/v1/applications`
- **Description**: List job applications with paging.
- **Query Parameters**: `status` (optional string), `limit` (default 50, max 200), `offset` (default 0).
- **Status**: 200 OK: `PagedResult<ApplicationListItemDto>`

### `GET /api/v1/applications/{id}`
- **Description**: Retrieve application details with historical timeline events.
- **Path Parameters**: `id` (UUID).
- **Status**:
  - 200 OK: `ApplicationDetailDto`
  - 404 Not Found: `ErrorEnvelope`

### `POST /api/v1/applications`
- **Description**: Record a new job application.
- **Request Body**: `RecordApplicationRequestDto`
- **Status**:
  - 201 Created: `ApplicationDetailDto`
  - 400 Bad Request: `ErrorEnvelope`
- **Audit**: Emits append-only audit entry with operation `application_create` and actor `api-user`.

### `POST /api/v1/applications/{id}/events`
- **Description**: Append a timeline event (interview, offer, rejection) to an existing application.
- **Path Parameters**: `id` (UUID).
- **Request Body**: `AppendApplicationEventRequestDto`
- **Status**:
  - 200 OK: Updated `ApplicationDetailDto`
  - 400 Bad Request: `ErrorEnvelope`
  - 404 Not Found: `ErrorEnvelope`
- **Audit**: Emits append-only audit entry with operation `application_event_append` and actor `api-user`.

---

## 5. Criteria, Notes, and Search Runs Endpoints

### `GET /api/v1/criteria`
- **Description**: List evaluation criteria.
- **Query Parameters**: `effectiveDate` (optional UTC date-time string).
- **Status**: 200 OK: Array of `CriterionDto`

### `GET /api/v1/notes/{id}`
- **Description**: Retrieve note by identifier.
- **Path Parameters**: `id` (UUID).
- **Status**:
  - 200 OK: `NoteDto`
  - 404 Not Found: `ErrorEnvelope`

### `GET /api/v1/notes`
- **Description**: Search notes.
- **Query Parameters**: `q` (string, required), `limit` (default 50, max 200), `offset` (default 0).
- **Status**: 200 OK: `PagedResult<NoteDto>`

### `GET /api/v1/search-runs`
- **Description**: List past automated search run summaries.
- **Query Parameters**: `limit` (default 50, max 200), `offset` (default 0).
- **Status**: 200 OK: `PagedResult<SearchRunDto>`
