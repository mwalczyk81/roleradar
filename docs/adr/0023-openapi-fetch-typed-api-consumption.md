# ADR 0023: Strongly-Typed API Consumption via openapi-fetch and Committed OpenAPI Types

- **Status**: Accepted
- **Date**: 2026-10-31
- **Feature**: `005b-management-ui`

## Context and Problem Statement

Feature 005a generated a committed OpenAPI specification (`contracts/openapi/roleradar-api.json`) and corresponding strongly typed TypeScript declarations (`contracts/client/api-types.ts`) via `openapi-typescript` (version `7.13.0`).

Requirement FR-004 mandates that all communication with the 005a REST API be conducted server-side using the generated typed client. Hand-written untyped `fetch` calls are strictly forbidden.

In addition:

1. The API exposes a standard error envelope (`ErrorEnvelope`: `{ ok: false, errorCode: string, message: string, details?: ErrorFieldDetail[] }`). The UI client must map these errors to typed form errors.
2. The REST API exposes no batch lookup endpoint. On the `/applications` list, the DTO returns only `companyId` and `postingId` without names. The web server must resolve names without creating an uncontrolled N+1 network cascade.
3. The API binds to an internal loopback/container interface and has CORS disabled. Browser client code must never invoke the API directly.

How should the web application server interact with the REST API?

## Decision Drivers

- **Zero Untyped Network Calls**: Compile-time TypeScript verification for every API path, HTTP method, query parameter, request body, and response status.
- **Contract Drift Protection**: Align directly with `contracts/client/api-types.ts` generated from `roleradar-api.json`.
- **Lightweight Server Footprint**: Avoid heavyweight SDK generator runtimes or bloated codegen frameworks.
- **Robust Error Envelope Handling**: Standardize transformation of 400 validation, 404 missing, 409 conflict, and 403 forbidden responses.
- **Bounded Request-Scoped Lookups**: Resolve application foreign key names predictably and concurrently.

## Decision Outcome

Chosen design: **`openapi-fetch` (v0.17.0) Server-Side Client Module + Typed Error Envelope Mapper + Deduplicated Request-Scoped Name Resolver**.

### 1. Server-Only Client Wrapper

All API calls are isolated in `web/app/lib/api-client.server.ts`:

```typescript
import createClient from 'openapi-fetch';
import type { paths } from '../../../contracts/client/api-types';

export function createApiClient(accessToken: string) {
  const baseUrl = process.env.ROLERADAR_API_BASE_URL ?? 'https://api:5002';

  return createClient<paths>({
    baseUrl,
    headers: {
      Authorization: `Bearer ${accessToken}`,
      'Content-Type': 'application/json',
    },
  });
}
```

### 2. Standard Error Mapping (`toUiError`)

The 005a REST API returns `ErrorEnvelope` for error responses:

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

export function mapApiError(status: number, error: unknown): UiError {
  const envelope = error as {
    ok?: boolean;
    errorCode?: string;
    message?: string;
    details?: Array<{ field: string; issue: string }>;
  };

  const fieldErrors: Record<string, string> = {};
  if (Array.isArray(envelope?.details)) {
    for (const d of envelope.details) {
      if (d.field && d.issue) {
        fieldErrors[d.field] = d.issue;
      }
    }
  }

  return {
    status,
    errorCode: envelope?.errorCode ?? 'UNKNOWN_ERROR',
    message: envelope?.message ?? 'An unexpected error occurred.',
    fieldErrors,
    isConflict: status === 409,
    isNotFound: status === 404,
    isForbidden: status === 403,
  };
}
```

### 3. Application List Context Resolution (Bounded Concurrent N+1 Resolution)

To populate readable posting and company names on `/applications`:

1. The route loader fetches the page of applications (`GET /api/v1/applications?limit=25&offset=0`).
2. It extracts unique non-null `postingId`s and `companyId`s across the returned slice (maximum 25 each).
3. It performs deduplicated, concurrent lookups via `Promise.all`:

   ```typescript
   export async function resolveApplicationContext(
     client: ReturnType<typeof createApiClient>,
     applications: ApplicationSummaryDto[]
   ): Promise<{ companies: Map<string, string>; postings: Map<string, string> }> {
     const uniquePostingIds = [...new Set(applications.map(a => a.postingId).filter(Boolean))];
     const uniqueCompanyIds = [...new Set(applications.map(a => a.companyId).filter(Boolean))];

     const [postingsRes, companiesRes] = await Promise.all([
       Promise.all(uniquePostingIds.map(id => client.GET('/api/v1/postings/{id}', { params: { path: { id } } }))),
       Promise.all(uniqueCompanyIds.map(id => client.GET('/api/v1/companies/{id}', { params: { path: { id } } }))),
     ]);

     const postingsMap = new Map<string, string>();
     for (const res of postingsRes) {
       if (res.data) postingsMap.set(res.data.id, res.data.title);
     }

     const companiesMap = new Map<string, string>();
     for (const res of companiesRes) {
       if (res.data) companiesMap.set(res.data.id, res.data.name);
     }

     return { postings: postingsMap, companies: companiesMap };
   }
   ```

4. Lookups are bounded strictly by page size (`limit <= 100`, default 25), execute over local loopback/container networks with sub-millisecond latency, and are cached for the duration of the request.

## Consequences

### Positive

- Strict compile-time validation: if the backend OpenAPI contract alters a route, parameter type, or payload, TypeScript fails the build during `pnpm build`.
- Zero manual type duplication.
- Server actions cleanly intercept 409 conflicts and 400 validation errors to render inline UI alerts.

### Negative / Trade-offs

- Missing batch lookup endpoint in 005a API requires multiple individual GET requests per page load on `/applications`. Documented as an API gap.
