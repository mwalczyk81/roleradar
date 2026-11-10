import ky, { isHTTPError, isNetworkError } from "ky";
import createClient, { type Client } from "openapi-fetch";
import type { components, paths } from "../../../contracts/client/api-types.js";
import { log } from "./log.server.js";
import {
  READ_RETRY_LIMIT,
  RETRYABLE_STATUSES,
  RETRY_AFTER_BOUND_MS,
  RETRY_DELAYS_MS,
  SERVICE_UNAVAILABLE_MESSAGE,
  attemptTimeoutMs,
  isReadMethod,
  retryAfterMs,
} from "./observability.js";
import { withTraceparent } from "./trace-context.server.js";

export interface UiError {
  errorCode: string;
  message: string;
  fieldErrors: Record<string, string>;
  isConflict: boolean;
  isNotFound: boolean;
  isForbidden: boolean;
  traceId?: string;
}

/** An RFC 9457 problem document as the API sends it (generated types; the validation form adds `errors`). */
type ProblemDocument = components["schemas"]["HttpValidationProblemDetails"];

function isProblemDocument(value: unknown): value is ProblemDocument {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function fieldErrorsOf(errors: unknown): Record<string, string> {
  const result: Record<string, string> = {};
  if (typeof errors !== "object" || errors === null || Array.isArray(errors)) {
    return result;
  }
  for (const [field, issues] of Object.entries(errors)) {
    if (!Array.isArray(issues)) {
      continue;
    }
    const texts = issues.filter((issue): issue is string => typeof issue === "string" && issue !== "");
    if (field && texts.length > 0) {
      result[field] = texts.join(" ");
    }
  }
  return result;
}

/**
 * Maps an API error body (an RFC 9457 problem document, feature 014) to what the routes show. Field errors come from
 * `errors` (issues joined with a space), the code is the lower-case `errorCode` (never `type`), the message is `detail`,
 * and `traceId` is carried through for logging. The flags come from the status or from the lower-case code.
 */
export function mapApiError(error: unknown, status?: number): UiError {
  const problem = isProblemDocument(error) ? error : undefined;

  const errorCode = typeof problem?.errorCode === "string" && problem.errorCode !== "" ? problem.errorCode : status ? `HTTP_${status}` : "UNKNOWN_ERROR";
  const message = typeof problem?.detail === "string" && problem.detail !== "" ? problem.detail : "An unexpected API error occurred.";
  const traceId = typeof problem?.traceId === "string" && problem.traceId !== "" ? problem.traceId : undefined;

  return {
    errorCode,
    message,
    fieldErrors: fieldErrorsOf(problem?.errors),
    isConflict: status === 409 || errorCode === "concurrency_conflict",
    isNotFound: status === 404 || errorCode === "not_found",
    isForbidden: status === 403 || errorCode === "forbidden",
    traceId,
  };
}

export function getApiBaseUrl(): string {
  return process.env.ROLERADAR_API_BASE_URL ?? "https://localhost:5002";
}

/** The traceId of the API's problem document, or undefined when the body is not one (a proxy's page, an empty body). */
async function problemTraceId(response: Response): Promise<string | undefined> {
  try {
    const body: unknown = await response.clone().json();
    return mapApiError(body, response.status).traceId;
  } catch {
    return undefined;
  }
}

/**
 * One warn record when the API finally answers 503 (after any retries), carrying the traceId the API's problem document
 * names (otherwise the request's own trace id) so the owner can find its side of the request. The document's text and the
 * query string are never logged.
 */
async function logServiceUnavailable(request: Request, response: Response, attempts: number): Promise<void> {
  log.warn("The API answered 503: its data store is unavailable", {
    event: "ApiServiceUnavailable",
    traceId: await problemTraceId(response),
    props: { method: request.method.toUpperCase(), path: new URL(request.url).pathname, status: 503, attempts },
  });
}

/** True when the error is a 503 whose `Retry-After` asks for more than the bound: the web then does not retry. */
function asksForTooLongAWait(error: Error): boolean {
  if (!isHTTPError(error) || error.response.status !== 503) {
    return false;
  }
  const waitMs = retryAfterMs(error.response.headers.get("Retry-After"), Date.now());
  return waitMs !== undefined && waitMs > RETRY_AFTER_BOUND_MS;
}

/**
 * The outbound policy for every web-to-API call (feature 013 US3, FR-026; the retry loop is `ky`'s since feature 014 G3,
 * ADR 0045, contracts/web-outbound-policy.md). Reads (GET, HEAD) are attempted at most three times, on a network error, a
 * 5 s per-attempt timeout, 502, 503 or 504, with a 200 ms and then a 500 ms backoff. A write is sent exactly once, with a
 * 10 s timeout, and never retried. 400, 401, 403, 404, 409 and 500 come straight back for the caller (and assertApiOk) to
 * handle. A caller's abort is never retried and propagates. Every attempt carries the same traceparent.
 *
 * A 503 is retried after the wait its `Retry-After` names (ky reads the header) when that is at most
 * RETRY_AFTER_BOUND_MS; a longer wait means no retry and the 503 comes back at once. ky's own `maxRetryAfter` would clamp
 * the wait and still retry (pinned by a test), hence the `shouldRetry` veto.
 *
 * ky throws an `HTTPError` for a status it may retry (502, 503, 504); once it gives up, that response is returned, so callers
 * see every status as before. `throwHttpErrors: false` must not be used: ky then does not retry on status.
 *
 * The per-attempt timeout bounds each attempt, not the whole call: a caller that needs one overall bound across the
 * retries (the Insights loaders: 5 s) passes a signal of its own, which cancels the attempt and any backoff.
 */
export async function fetchWithPolicy(request: Request): Promise<Response> {
  withTraceparent(request.headers);
  request.signal.throwIfAborted();
  const method = request.method.toUpperCase();
  let attempts = 1;
  // ky reads the body of the response it turns into an HTTPError (error.data), which would leave the caller nothing to
  // read, so the last retryable response is kept as a clone and that is what comes back once ky gives up.
  let lastRetryable: Response | undefined;

  let response: Response;
  try {
    response = await ky(request, {
      retry: {
        limit: isReadMethod(method) ? READ_RETRY_LIMIT : 0,
        methods: ["get", "head"],
        statusCodes: [...RETRYABLE_STATUSES],
        retryOnTimeout: true,
        delay: (attemptCount) => RETRY_DELAYS_MS[Math.min(Math.max(attemptCount, 1), RETRY_DELAYS_MS.length) - 1],
        maxRetryAfter: RETRY_AFTER_BOUND_MS,
        shouldRetry: ({ error }) => (asksForTooLongAWait(error) ? false : undefined),
      },
      timeout: attemptTimeoutMs(method),
      signal: request.signal,
      // Only the retryable statuses become an HTTPError; every other status comes straight back with its body unread.
      throwHttpErrors: (status) => RETRYABLE_STATUSES.includes(status),
      hooks: {
        afterResponse: [
          ({ response: attempt }) => {
            if (RETRYABLE_STATUSES.includes(attempt.status)) {
              void lastRetryable?.body?.cancel().catch(() => undefined);
              lastRetryable = attempt.clone();
            }
          },
        ],
        beforeRetry: [
          ({ retryCount }) => {
            attempts = retryCount + 1;
          },
        ],
      },
      // Read per call, so a test can stub globalThis.fetch.
      fetch: (input, init) => globalThis.fetch(input, init),
    });
  } catch (error) {
    if (isHTTPError(error)) {
      response = lastRetryable ?? error.response;
    } else if (isNetworkError(error) && error.cause !== undefined) {
      // ky wraps the failed fetch; the callers (and the 013 contract) see the error fetch itself threw.
      throw error.cause;
    } else {
      throw error;
    }
  }

  if (response.status === 503) {
    await logServiceUnavailable(request, response, attempts);
  }
  return response;
}

/** The fetch every web-to-API call goes through, under the outbound policy. Reads globalThis.fetch per call so tests can stub it. */
export function apiFetch(request: Request): Promise<Response> {
  return fetchWithPolicy(request);
}

export function createApiClient(accessToken?: string): Client<paths> {
  const baseUrl = getApiBaseUrl();
  return createClient<paths>({
    baseUrl,
    fetch: apiFetch,
    headers: accessToken
      ? {
          Authorization: `Bearer ${accessToken}`,
        }
      : undefined,
  });
}

export interface ODataFetchResult<T> {
  data?: {
    value: T[];
    "@odata.count"?: number;
  };
  error?: {
    message: string;
    status: number;
  };
  response: Response;
}

export interface ODataPayload<T> {
  value: T[];
  "@odata.count"?: number;
}

export function isODataPayload<T>(val: unknown): val is ODataPayload<T> {
  if (typeof val !== "object" || val === null) {
    return false;
  }
  const candidate = val as Record<string, unknown>;
  if (!Array.isArray(candidate.value)) {
    return false;
  }
  if (candidate["@odata.count"] !== undefined && typeof candidate["@odata.count"] !== "number") {
    return false;
  }
  return true;
}

export async function fetchOData<T>(
  path: string,
  queryString: string,
  accessToken?: string,
  signal?: AbortSignal
): Promise<ODataFetchResult<T>> {
  const baseUrl = getApiBaseUrl();
  const url = `${baseUrl}${path}${queryString ? `?${queryString}` : ""}`;

  const headers = new Headers({ Accept: "application/json" });
  if (accessToken) {
    headers.set("Authorization", `Bearer ${accessToken}`);
  }

  const response = await apiFetch(new Request(url, { method: "GET", headers, signal }));

  if (!response.ok) {
    if (response.status === 503) {
      // The API's data store did not answer. The owner gets the web's own friendly text, never the envelope's.
      return { error: { message: SERVICE_UNAVAILABLE_MESSAGE, status: 503 }, response };
    }

    let errorMsg = `OData request failed with status ${response.status}`;
    try {
      const errJson: unknown = await response.json();
      if (isProblemDocument(errJson) && typeof errJson.detail === "string" && errJson.detail !== "") {
        errorMsg = errJson.detail;
      }
    } catch {
      // ignore json parse error on non-json error responses
    }
    return {
      error: {
        message: errorMsg,
        status: response.status,
      },
      response,
    };
  }

  const rawJson: unknown = await response.json();
  if (!isODataPayload<T>(rawJson)) {
    return {
      error: {
        message: "Invalid OData response structure: expected 'value' array.",
        status: 500,
      },
      response,
    };
  }

  return {
    data: rawJson,
    response,
  };
}
