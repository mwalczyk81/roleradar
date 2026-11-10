# API problem types

Every error the API returns, under `/api/v1` and `/odata`, is an [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) problem document with the media type `application/problem+json`. This page is the documentation each document's `type` points at: the `type` member of a problem is this page's URL plus an anchor named after the error code, for example `https://github.com/mwalczyk81/roleradar/blob/main/docs/api/problems.md#not_found`.

Decision record: [ADR 0044](../adr/0044-problem-details-and-trace-context.md). The base URL is one constant in the API (`ProblemTypes.TypeUriPrefix`), so it can move if the API is hosted. Read the machine-readable `errorCode` member to decide what to do, not `type`.

## Members

| Member | Meaning |
| --- | --- |
| `type` | This page plus `#<errorCode>`. |
| `title` | The standard text for the status. |
| `status` | The HTTP status. |
| `detail` | The human-readable message. Present on 400, 404, 409, 500 and 503. |
| `errorCode` | The stable machine-readable code, lower case. The web switches on it. |
| `traceId` | The W3C trace id of the request (32 lower-case hex characters). Search the web, API and identity logs for it, and the `request_correlation_id` of an audit entry written by the request. |
| `errors` | On a validation failure only: `{ "<field>": ["<issue>", ...] }`. |

A response never carries exception text, a stack trace, connection details or a command to run. The log holds those, with the same `traceId`.

## Codes

### validation_error

HTTP 400. The request was understood and rejected: a command or query failed validation, or an OData query was refused (an unknown or disallowed field, `$top` above 100, a bare `contains()` without `tolower()`). `detail` says why. `errors` names the field when there is one. Fix the request and send it again.

### unauthorized

HTTP 401. There is no bearer token, or the token is not valid or has expired. The `WWW-Authenticate` header says what the API expects. Sign in again.

### forbidden

HTTP 403. The token is valid but belongs to someone who is not the owner. There is nothing to retry.

### not_found

HTTP 404. The thing you named does not exist for the owner. A row that belongs to someone else is never revealed, so it looks the same.

### concurrency_conflict

HTTP 409. The row changed since you read it: the version token you sent is stale. Read the row again and apply the change to the new version.

### service_unavailable

HTTP 503. The data store did not answer. The response carries `Retry-After: 5`. Nothing needs restarting: the database driver reconnects when the store is back. See [Logs and health](../operations/logs-and-health.md).

### internal_error

HTTP 500. The request failed in a way the owner cannot fix from the request. The body is fixed text. Find the log record with the response's `traceId` to see what happened.

## Statuses with no code of ours

A status the platform produces on its own, such as 405 or 415, keeps the platform's default problem document and its `type`. Those have no `errorCode`.
