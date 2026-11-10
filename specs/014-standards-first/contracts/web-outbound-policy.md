# Contract: Web outbound retry, timeout and concurrency

Applies to every web-to-API call (`apiFetch`) and the Insights loaders. Keeps the behavior of feature 013 US3 with maintained libraries underneath (`ky`, `p-limit`). Decision: research R-010. This amends 013 FR-027; 013 FR-026 and FR-028 still apply.

## Policy

| Aspect | Reads (GET, HEAD) | Writes (everything else) |
| --- | --- | --- |
| Attempts | At most 3 (the first call and at most 2 retries) | 1, never retried |
| Retried on | A network error, a per-attempt timeout, 502, 503, 504 | not applicable |
| Not retried | 400, 401, 403, 404, 409, 500, any other status; a caller abort | all |
| Per-attempt timeout | 5 s | 10 s |
| Backoff between attempts | 200 ms then 500 ms | not applicable |
| `Retry-After` | Honoured on a 503, as seconds or an HTTP date, when it asks for no more than the bound of 2000 ms (`RETRY_AFTER_BOUND_MS`). A larger value means **no retry**: the 503 is returned at once (spec edge case, FR-042). The API sends `Retry-After: 5`, so a real 503 from the API is not retried by the web and goes to the error boundary at once. A 503 without the header, a network error, a timeout, 502 and 504 still retry. | not applicable |
| Caller abort | Cancels the attempt and any wait and propagates. The Insights loaders still pass a 5 s signal of their own as the overall bound. | same |
| Trace context | The same `traceparent` on every attempt | same |

## Mapping to the libraries

| Policy item | Library setting |
| --- | --- |
| Attempts | `ky` `retry.limit` 2 for reads, 0 for writes |
| Methods | `retry.methods` `["get", "head"]` (ky's default also retries `put`, `delete`, `options` and `trace`) |
| Statuses | `retry.statusCodes` `[502, 503, 504]` (ky's default also retries 408, 413, 429 and 500) |
| Timeouts as failures | `retry.retryOnTimeout` true |
| Backoff | `retry.delay` returning 200 then 500 |
| `Retry-After` | ky's `afterStatusCodes` default (413, 429, 503) for the delay, plus a `retry.shouldRetry` veto that returns false when the header asks for more than `RETRY_AFTER_BOUND_MS`. ky's `maxRetryAfter` alone is not enough: a probe on 2026-11-10 showed it **clamps** the wait (3 attempts, 4 s for `Retry-After: 5` with a 2000 ms cap) instead of refusing the retry, and the `shouldRetry` veto gives 1 attempt. The first red test of the story re-asserts this against the pinned version; if it differs, this row is corrected to match the test. |
| Per-attempt timeout | `timeout` 5000 for reads, 10000 for writes |
| Status codes come back to the caller | ky throws `HTTPError` so that it can retry on status; the wrapper catches it and returns `error.response`. `throwHttpErrors: false` must not be used: ky then does not retry on status (probe). |
| Attempt count for the 503 warn record | A `beforeRetry` hook counts retries |
| Tests stub `globalThis.fetch` per call | `fetch: (input, init) => globalThis.fetch(input, init)` passed to ky |
| Concurrency | `p-limit(limit)` replaces `createLimiter`: the Insights counts keep `MAX_IN_FLIGHT`, the stats read keeps 1 |

Writing the policy with a different library (the fallback in the plan) keeps this table's left column unchanged.

## Tests (written first)

- The 013 `api-policy` tests and the Insights tests stay green with the libraries underneath (FR-043).
- First red test: a read answered 503 with `Retry-After: 5` makes exactly 1 attempt and returns the 503 at once (settles how the pinned ky treats a value above its `maxRetryAfter`).
- New with fake timers: a 503 with `Retry-After: 1` waits about 1 s and then succeeds; an HTTP-date `Retry-After` within the bound is honoured; one beyond the bound is not retried.
- A 503 with no `Retry-After` still retries as before (the 013 `api-policy` tests keep passing).
- Attempt counts: a read makes at most 3 attempts in total, a write 1, and 401, 403 and 404 make 1.
- A caller abort during a backoff wait rejects promptly and is not retried.
- No file under `web/app` still defines a retry loop, `sleep`, `abortable` or a limiter (SC-009).
