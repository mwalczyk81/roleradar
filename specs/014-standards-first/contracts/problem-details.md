# Contract: API errors as RFC 9457 problem documents

Applies to every error response of the API under `/api/v1`, `/odata` and the failures raised by authentication and authorization. Replaces the error envelope of ADR 0018 section 2 and the 013 contract `specs/013-operability/contracts/http-correlation-and-errors.md` (its error-envelope half; its correlation half is replaced by [trace-context.md](trace-context.md)). Decisions: research R-005, R-006, R-007.

## Media type and members

Every error response is `application/problem+json`.

| Member | Type | Present | Notes |
| --- | --- | --- | --- |
| `type` | string (URI) | always | A documentation URL in the repository built from the error code: `https://github.com/mwalczyk81/roleradar/blob/main/docs/api/problems.md#<errorCode>`, one anchor per code in `docs/api/problems.md`. The base is one constant (`ProblemTypes.TypeUriPrefix`). The web does not switch on `type`; it switches on `errorCode`. A status with no code of ours (for example 405) keeps the platform default. |
| `title` | string | always | Fixed text per status (the platform's). |
| `status` | integer | always | Equals the HTTP status. |
| `detail` | string | 400, 404, 409, 500, 503 | The human-readable message. Fixed text for 500 and 503. |
| `errorCode` | string | always except platform-default statuses | The machine-readable code the web switches on. Lower case, as the server sends it today. |
| `traceId` | string | always | The bare W3C trace id (32 hex), see [trace-context.md](trace-context.md). Not `Activity.Id`: the platform default is overridden. |
| `errors` | object of string arrays | 400 validation only | `{ "<field>": ["<issue>", ...] }`, the platform's `ValidationProblemDetails` shape. |

No `instance` member is written.

## Status and code mapping

| Situation | Status | `errorCode` | `detail` | Headers |
| --- | --- | --- | --- | --- |
| Validation failure (command or query) | 400 | `validation_error` | The validation message | |
| OData query error (`ODataException`) | 400 | `validation_error` | The parser message, as today | |
| Not authenticated | 401 | `unauthorized` | none | `WWW-Authenticate` as the platform sets it |
| Not authorized | 403 | `forbidden` | none | |
| Not found | 404 | `not_found` | The not-found message | |
| Stale version token | 409 | `concurrency_conflict` | The conflict message | |
| Data store unreachable | 503 | `service_unavailable` | `The data store is not reachable. Try again shortly.` | `Retry-After: 5` |
| Any other unhandled exception | 500 | `internal_error` | `The request failed unexpectedly. Try again, and check the logs if it keeps happening.` | |

- 503 arrives within 5 seconds of the request (013 FR-024) and the data store recovery bound (013 FR-025) is unchanged.
- 500 and 503 bodies never contain exception text, a stack trace, connection details or a remediation command. Those go to the scrubbed log only, with the trace id.
- A 401 or 403 with an empty body written by the authentication or authorization middleware is turned into the same document by the status-code pages. A test covers a missing or invalid bearer token and an authorization failure.
- A client that closes the request before it finishes is still absorbed quietly and gets no body (013 `ApiExceptionHandler` behavior).

## Old to new

| Envelope member | Problem document |
| --- | --- |
| `ok: false` | HTTP status, `status` |
| `errorCode` | `errorCode`, and `type` built from it |
| `message` | `detail` |
| `details[{ field, issue }]` | `errors` |
| `correlationId` | `traceId` |

## Web behavior

- 400: the web reads `errors` and shows inline field errors, as today.
- 409: the Toast with Reload (revalidate), as today.
- 401: one re-login, no loop. 403: `/not-authorized`. 404: the not-found handling. 503: the existing error boundary and the one warn record carrying `traceId`.
- `mapApiError` is rewritten from the generated types. It reads `errors` (the old code read `details[].message` where the contract said `issue`) and compares lower-case `errorCode` values (the old code compared upper-case literals). Tests for both are written first.
- The CLI and MCP do not read HTTP errors: they call the Application layer and keep their `{ ok, data }` result shape.

## OpenAPI and the generated client

- Each operation declares `application/problem+json` responses (`ProducesProblem`, `ProducesValidationProblem`), and the OData routes are covered by the same transformer.
- A schema transformer gives `ProblemDetails` the typed members `errorCode` and `traceId`, so `contracts/client/api-types.ts` carries them and the web needs no cast.
- Regenerate with the AGENTS.md commands: `dotnet build src/RoleRadar.Api`, then `npx --yes openapi-typescript@7.10.1 contracts/openapi/roleradar-api.json -o contracts/client/api-types.ts`.
- `EnumContractRegistry` is not involved (`errorCode` is a string).
- The transformer and the handler write nothing at warning level or above during the build-time start of the API with Mongo unreachable.

## Contract tests (written first)

For each of 400, 401, 403, 404, 409, 500 and 503: media type, the members above, `traceId` equal to the trace id of the request (and 32 hex), headers (`Retry-After` on 503 only), and no old-envelope member (`ok`, `message`, `details`, `correlationId`). Plus an OpenAPI snapshot test and the web end-to-end tests named in the plan's G2.
