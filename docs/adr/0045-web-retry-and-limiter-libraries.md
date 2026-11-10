# ADR 0045: Web Retry, Timeout and Concurrency Limit From ky and p-limit

- **Status**: Accepted (2026-11-10)
- **Date**: 2026-11-10
- **Feature**: `014-standards-first` (story P3, Jira RR-79)

## Context and Problem Statement

Feature 013 gave the web server an outbound policy for every call to the API (013 US3, FR-026, SC-007): reads are attempted at most three times, writes once, with a per-attempt timeout and a 200 ms then 500 ms backoff. It was written by hand because 013 FR-027 said "without a retry policy library". The hand-written parts were `fetchWithPolicy`'s loop, `sleep`, `abortable`, the pure `shouldRetry` and `backoffMs`, and `createLimiter` (the Insights concurrency limit). Maintained libraries cover all of it, and Principle X (Standards First) asks for one unless a recorded reason says it does not fit. The one thing the hand-written code did not do is honour `Retry-After`, which the API sends on every 503.

## Decision Outcome

**`ky` 2.2.0 for retry, per-attempt timeout and `Retry-After`; `p-limit` 7.3.3 for the concurrency limit.** Both are MIT and need Node 22 or newer; the web image is `node:22-alpine`. `ky` has no dependencies; `p-limit` has one (`yocto-queue`, MIT). `fetchWithPolicy` in `web/app/lib/api-client.server.ts` is rewritten over ky and `request-limiter.server.ts` is deleted. `apiFetch` and `createApiClient` keep their signatures, ky is handed `fetch: (input, init) => globalThis.fetch(input, init)` so tests still stub `globalThis.fetch` per call, and a source scan test (SC-009) fails if a hand-written `sleep`, `abortable` or limiter returns to `web/app`.

### Compared

At the 013 policy (reads at most 3 attempts, 5 s per attempt, 200 and 500 ms backoff, writes once with 10 s, 400, 401, 403, 404, 409 and 500 returned at once, a caller abort not retried):

| Option | `Retry-After` | Timeout and abort | Fit with the code and tests |
| --- | --- | --- | --- |
| undici retry interceptor (`undici` 8.12.3) | Honoured by default | Per attempt through socket timeouts, not an abort signal | Defaults retry `PUT`, `DELETE` and 500 and must be restated. Works on dispatchers, so it needs undici's own `fetch` and real sockets, where the tests stub `globalThis.fetch` with fake timers. The largest change. |
| `p-retry` 8.0.1 | Not supported | `signal`, `shouldRetry`, `onFailedAttempt` | Retry only. The timeout and `Retry-After` stay hand-written. (9.0.0 was published 2026-11-02, too new.) |
| `cockatiel` 4.0.0 | Not built in; a `DelegateBackoff` would read the header with a parser of ours | `timeout` and `bulkhead` in the same package | Covers retry, timeout and limiter in one package, but adds a hand-written parser. Kept as the documented fallback. |
| **`ky` 2.2.0** | **Honoured** (`afterStatusCodes` 413, 429, 503; seconds or HTTP date) | Per-attempt `timeout`, abort signal, `retryOnTimeout` | Built on the global `fetch`, takes a `Request`, and its options cover the policy. |

`p-limit` was chosen over `cockatiel`'s bulkhead because it is the library the limiter reduces to (a function that wraps a task), has no policy surface to learn and works with the loaders unchanged apart from the call shape.

### The policy (contract: `specs/014-standards-first/contracts/web-outbound-policy.md`)

| Aspect | Setting |
| --- | --- |
| Reads (`GET`, `HEAD`) | `retry.limit` 2 (three attempts in all), `retry.methods` `["get", "head"]`, `retry.statusCodes` `[502, 503, 504]`, `retry.retryOnTimeout` true, `retry.delay` 200 then 500 ms, `timeout` 5000 |
| Writes | `retry.limit` 0, `timeout` 10000 |
| Not retried | 400, 401, 403, 404, 409, 500 and any other status; a caller abort |
| Trace context | The same `traceparent` on every attempt (ADR 0044) |
| Concurrency | `pLimit(limit)`; the Insights counts keep `MAX_IN_FLIGHT` (6) and the stats read keeps 1 |

ky's defaults also retry `PUT`, `DELETE`, `OPTIONS`, `TRACE`, 408, 413, 429 and 500, so those are stated rather than inherited.

### `Retry-After` and its consequence

A 503 that names a wait of at most `RETRY_AFTER_BOUND_MS` (2000 ms), as whole seconds or an HTTP date, is retried after that wait. **A longer value means no retry**: the 503 is returned at once (spec 014 edge case, FR-042).

**ky clamps, it does not refuse.** ky's own `maxRetryAfter` option caps the wait but still retries. The characterization test in `web/tests/unit/api-policy-retry-after.test.ts` (case b) pins this on 2.2.0: with `maxRetryAfter` 2000 and `Retry-After: 5`, ky makes 3 attempts, at 0, 2000 and 4000 ms. A `retry.shouldRetry` veto that returns false when a 503's `Retry-After` exceeds the bound gives 1 attempt and an immediate 503 (the first red test of the story). So the veto is the mechanism, and `maxRetryAfter` is kept only as a second line of defence. The test fails loudly if a later ky changes either behavior. The veto needs the header's value, so a small reader of our own (`retryAfterMs`, a few lines in `observability.ts`, unit-tested and mutation-tested) parses the two forms RFC 9110 section 10.2.3 allows; it exists only to compare against the bound, ky does the waiting.

**The consequence for the owner.** The API always sends `Retry-After: 5` on a 503, which is above the bound. So **a real 503 from the API is returned at once by the web and is not retried**; it goes to the error boundary (or the form's message) without the 0.7 s of backoff and two extra attempts 013 spent on it. A network error, a timeout, 502, 504 and a 503 that carries no `Retry-After` are still retried. The reasoning for the bound: honouring 5 s in full would put 10 s of waiting into two retries and break the Insights loaders' own 5 s bound. If the 013 behavior for a real 503 is wanted back, the options are to raise `RETRY_AFTER_BOUND_MS` to 5000 (the Insights loaders' 5 s signal then bounds them) or to lower the API's `Retry-After`. Either is a one-constant change and a line in this ADR.

### Things the library does that the wrapper has to absorb

- **ky throws on a retryable status and reads its body.** It needs an `HTTPError` to retry on status, and it consumes the response body to fill `error.data`, which would leave callers nothing to read (the problem document carries the `traceId` and `detail`). The wrapper therefore sets `throwHttpErrors` to a function that is true only for 502, 503 and 504, so every other status (400, 401, 403, 404, 409, 500) comes straight back with its body unread; and an `afterResponse` hook keeps a clone of the last retryable response, which is returned once ky gives up. `throwHttpErrors: false` must not be used: ky then does not retry on status.
- **ky wraps a failed fetch in its own `NetworkError`.** The wrapper rethrows its `cause`, the error `fetch` threw, so callers see what they saw in 013.
- **ky calls `fetch` once for a request whose signal is already aborted.** The wrapper checks the signal first, as the hand-written code did.
- **Attempt count for the 503 warn record** comes from a `beforeRetry` hook.

### Amendment of 013 FR-027

013 FR-027 ("without a retry policy library") is superseded by 014 FR-044: a maintained retry library is now allowed. 013 FR-026 (a per-attempt timeout; only safe reads retried, at most twice; writes never) and FR-028 (nothing leaves the machine) stand. The pointer is under FR-027 in `specs/013-operability/spec.md`.

## Consequences

- **Good:** the retry loop, backoff, timeout, abort handling and limiter are maintained code with their own test suites, and `Retry-After` is honoured, which the 013 code never did. `shouldRetry`, `backoffMs`, `sleep`, `abortable`, `createLimiter` and `request-limiter.server.ts` are gone. The 013 `api-policy` and Insights tests pass against the library-backed code with their assertions unchanged, apart from the unit tests of the deleted pure helpers.
- **Bad:** two runtime packages and one transitive (ky has none, p-limit has `yocto-queue`); a real 503 is no longer retried by the web; three behaviors of ky have to be absorbed in `fetchWithPolicy` (above), which is the kind of coupling a library brings and why the policy tests exercise `fetchWithPolicy` as a whole; `retryAfterMs` is a small hand-written reader that exists only because ky's option clamps.
- **Packages:** `ky` 2.2.0 and `p-limit` 7.3.3 in `web/package.json` `dependencies` (exact), `yocto-queue` through `p-limit`; no NuGet package.
- **Fallback not used:** `cockatiel` 4.0.0 with a Retry-After parser and its own ADR, for the case that a fake-timer or abort test showed ky could not keep the 013 tests green. It was not needed.
