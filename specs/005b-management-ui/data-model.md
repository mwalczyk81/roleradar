# Data Model: 005b-management-ui

**Feature**: `005b-management-ui`  
**Date**: 2026-10-31  
**Status**: Completed  

This document details the server-side session entities, the consumed REST API data models, state transition rules, and safety invariants governing untrusted text rendering.

---

## 1. Web Application Server Entities

### `UserSession` (Server-Side Session Store)

The `UserSession` entity exists exclusively in the web application server's memory (`Map<string, EncryptedSessionRecord>`). It is never transmitted across the network or stored client-side.

```typescript
export interface UserSession {
  /** High-entropy cryptographically random session identifier (UUIDv7 or 32 random bytes) */
  sessionId: string;

  /** OIDC authenticated subject claim ('sub') matching the single owner */
  ownerSub: string;

  /** Encrypted OAuth 2.0 Access Token */
  accessToken: string;

  /** Encrypted OAuth 2.0 Refresh Token */
  refreshToken: string;

  /** Optional raw OpenID Connect ID Token for RP-Initiated Logout hint */
  idToken?: string;

  /** Unix epoch timestamp (milliseconds) when the access token expires */
  expiresAt: number;

  /** Unix epoch timestamp (milliseconds) when the session was created */
  createdAt: number;

  /** Cryptographically random synchronizer token for CSRF defense on state-changing actions */
  csrfToken: string;
}

export interface EncryptedSessionRecord {
  /** AES-256-GCM ciphertext of JSON-serialized session tokens (base64) */
  ciphertext: string;

  /** 96-bit (12-byte) initialization vector (base64) */
  iv: string;

  /** 128-bit (16-byte) authentication tag (base64) */
  tag: string;

  /** Expiration timestamp for store eviction */
  expiresAt: number;
}
```

#### Lifecycle & Validation Rules:
1. **Creation**: Created upon successful authorization code exchange at `/auth/callback`. A new `sessionId` is minted, old session cookies are rotated, and tokens are encrypted with `SESSION_ENCRYPTION_KEY`.
2. **Access Token Refresh**: If `expiresAt - now < 60_000`, a single-flight background refresh executes via `refreshTokenGrant`.
3. **Destruction**: Cleared immediately on `/auth/logout` or when an invalid/expired refresh token is rejected by Identity.
4. **Scoping**: Bounded to the container lifecycle. In-memory data is purged on restart; re-login is required.

---

## 2. API Domain Entities (Mapped from OpenAPI Contract)

### `PostingDetail` (matches `PostingDetailDto`)

Represents a single job opportunity retrieved from `GET /api/v1/postings/{id}`.

```typescript
export interface PostingDetail {
  id: string; // UUIDv7
  companyId: string; // UUIDv7
  companyName: string;
  title: string;
  requisitionId: string | null;
  location: string | null;
  disposition: PostingDisposition;
  resurfacePolicy: string | null;
  score: number;
  versionToken: string;
  createdAt: string; // ISO 8601 UTC
  updatedAt: string; // ISO 8601 UTC
  url: string | null;
  compensation: CompensationInfo | null;
  signals: PostingSignal[];
  assessmentNote: AssessmentNoteSummary | null;
}

export type PostingDisposition =
  | 'identified'
  | 'screened_in'
  | 'screened_out'
  | 'applied'
  | 'interviewing'
  | 'offer'
  | 'rejected'
  | 'withdrawn'
  | 'archived';

export interface CompensationInfo {
  currency: string;
  rawText: string;
  hasSalary: boolean;
  compHighPriority: boolean;
}

export interface PostingSignal {
  signalType: string;
  signalValue: string;
  detectedAt: string; // ISO 8601 UTC
}

export interface AssessmentNoteSummary {
  id: string; // UUIDv7
  summary: string;
  updatedAt: string; // ISO 8601 UTC
}
```

### `CompanyDetail` (matches `CompanyDetailDto`)

Represents employer context retrieved from `GET /api/v1/companies/{id}`.

```typescript
export interface CompanyDetail {
  id: string; // UUIDv7
  name: string;
  status: string | null;
  ownershipType: string | null;
  description: string | null;
  headquarters: string | null;
  categoryTags: string[] | null;
  careerSources: CareerSourceDetail[] | null;
  versionToken: string | null;
}

export interface CareerSourceDetail {
  id: string;
  sourceType: string;
  atsProvider: string | null;
  atsTenant: string | null;
  sourceUrl: string | null;
  fetchMethod: string;
  active: boolean;
  versionToken: string | null;
}
```

### `ApplicationDetail` & `ApplicationSummary` (matches `ApplicationDetailDto` / `ApplicationSummaryDto`)

Tracks formal job applications retrieved from `GET /api/v1/applications/{id}` and `GET /api/v1/applications`.

```typescript
export interface ApplicationSummary {
  id: string; // UUIDv7
  postingId: string; // UUIDv7
  companyId: string; // UUIDv7
  status: string;
  appliedAt: string; // ISO 8601 UTC
  // Augmented by web server loader context:
  postingTitle?: string;
  companyName?: string;
}

export interface ApplicationDetail extends ApplicationSummary {
  events: ApplicationTimelineEvent[];
}

export interface ApplicationTimelineEvent {
  eventType: string;
  eventDate: string; // ISO 8601 UTC
  notes: string | null;
}
```

### `ScreeningCriterion` (matches `CriterionDto`)

Read-only screening rule retrieved from `GET /api/v1/criteria`.

```typescript
export interface ScreeningCriterion {
  id: string; // UUIDv7
  name: string;
  weight: number;
  ruleText: string;
  effectiveFrom: string; // ISO 8601 UTC
  effectiveTo: string | null; // ISO 8601 UTC
  isActive: boolean;
}
```

### `SearchRun` (matches `SearchRunDto`)

Read-only automated ingestion batch retrieved from `GET /api/v1/search-runs`.

```typescript
export interface SearchRun {
  id: string; // UUIDv7
  runDate: string; // ISO 8601 UTC
  companiesSearched: number;
  postingsFound: number;
  newPostings: number;
  errorsCount: number;
  summary: string | null;
}
```

---

## 3. State Mutation Payloads & Transitions

### Posting Disposition Transitions

- **Endpoint**: `POST /api/v1/postings/{id}/transition`
- **Request Body**:
  ```typescript
  export interface TransitionPostingRequestDto {
    toDisposition: string;
    reason: string;
    criterionId: string | null; // UUIDv7, required field but nullable
    versionToken: string;
    resurfacePolicy?: string | null;
    overrideReason?: string | null;
  }
  ```
- **Domain Matrix Rules (`DispositionMatrix`)**:
  - Target `applied` is **forbidden** via transition. Reaching `applied` occurs strictly by creating an application via `POST /api/v1/applications`.
  - The UI presents all non-applied dispositions (`screened_in`, `screened_out`, `interviewing`, `offer`, `rejected`, `withdrawn`, `archived`).
  - If a transition is disallowed from the posting's current state, the API returns HTTP 400 (`InvalidTransition`), which is mapped to an inline alert.
  - If `versionToken` does not match the stored version, the API returns HTTP 409 Conflict (`concurrency_conflict`), triggering a reload prompt.

### Application Creation

- **Endpoint**: `POST /api/v1/applications`
- **Side Effect**: Atomically updates the target posting's disposition to `applied`.
- **Request Body**:
  ```typescript
  export interface CreateApplicationRequestDto {
    postingId: string; // UUIDv7
    appliedDate: string; // ISO 8601 UTC
    tailoredResumeVariant?: string | null;
    targetCompensation?: string | null;
  }
  ```

### Timeline Event Append

- **Endpoint**: `POST /api/v1/applications/{id}/events`
- **Request Body**:
  ```typescript
  export interface AppendApplicationEventRequestDto {
    eventType: string;
    eventDate: string; // ISO 8601 UTC
    notes?: string | null;
  }
  ```

### Company Profile Update

- **Endpoint**: `PATCH /api/v1/companies/{id}`
- **Request Body**:
  ```typescript
  export interface UpdateCompanyRequestDto {
    name?: string | null;
    status?: string | null;
    ownershipType?: string | null;
    description?: string | null;
    headquarters?: string | null;
    categoryTags?: string[] | null;
    careerSources?: UpdateCareerSourceRequestDto[] | null;
    versionToken?: string | null;
  }
  ```

---

## 4. Error Mapping & Concurrency Handling

### API Error Envelope (`ErrorEnvelope`)

```typescript
export interface ErrorEnvelope {
  ok: boolean; // false
  errorCode: string;
  message: string;
  details?: ErrorFieldDetail[] | null;
}

export interface ErrorFieldDetail {
  field: string;
  issue: string;
}
```

### UI Error Representation (`UiError`)

```typescript
export interface UiError {
  status: number;
  errorCode: string;
  message: string;
  fieldErrors: Record<string, string>;
  isConflict: boolean;
  isNotFound: boolean;
  isForbidden: boolean;
}
```

---

## 5. Security & Untrusted Text Isolation

To satisfy Core Principle V, all string fields originating from external postings, assessment summaries, notes, or third-party web content are categorized as **Untrusted Plain Text**.

| Field | Source | Display Treatment | Security Invariant |
|-------|--------|-------------------|-------------------|
| `PostingDetail.title` | External Job Board | Plain Text Leaf Node | Escaped by React; no HTML/markdown parsing |
| `PostingDetail.location` | External Job Board | Plain Text Leaf Node | Escaped by React |
| `CompensationInfo.rawText` | External Job Board | Plain Text Leaf Node | Escaped by React |
| `AssessmentNoteSummary.summary` | AI Agent Note | Plain Text Leaf Node | Escaped by React |
| `PostingSignal.signalValue` | Heuristic / AI | Plain Text Leaf Node | Escaped by React |
| `CompanyDetail.description` | External / User | Plain Text Leaf Node | Escaped by React |
| `ApplicationTimelineEvent.notes` | User / Agent | Plain Text Leaf Node | Escaped by React |
| `ScreeningCriterion.ruleText` | User Criterion | Plain Text Leaf Node | Escaped by React |
| `SearchRun.summary` | Ingestion Script | Plain Text Leaf Node | Escaped by React |

**Rule**: Any use of `dangerouslySetInnerHTML`, `innerHTML`, `v-html`, or markdown AST-to-HTML transformers on these fields is strictly prohibited and enforced via automated ESLint checks.
