import createClient, { type Client } from "openapi-fetch";
import type { components, paths } from "../../../contracts/client/api-types.js";
import { log } from "./log.server.js";
import {
  SERVICE_UNAVAILABLE_MESSAGE,
  attemptTimeoutMs,
  backoffMs,
  shouldRetry,
  type AttemptFailure,
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

/** Rejects when `signal` aborts, even if the underlying fetch ignores it, so a stalled socket cannot hold the caller. */
function abortable<T>(promise: Promise<T>, signal: AbortSignal): Promise<T> {
  return new Promise<T>((resolve, reject) => {
    const onAbort = () => reject(signal.reason);
    if (signal.aborted) {
      onAbort();
      return;
    }
    signal.addEventListener("abort", onAbort, { once: true });
    promise.then(resolve, reject).finally(() => signal.removeEventListener("abort", onAbort));
  });
}

function sleep(ms: number, signal: AbortSignal): Promise<void> {
  return new Promise<void>((resolve, reject) => {
    const onAbort = () => {
      clearTimeout(timer);
      reject(signal.reason);
    };
    const timer = setTimeout(() => {
      signal.removeEventListener("abort", onAbort);
      resolve();
    }, ms);
    if (signal.aborted) {
      onAbort();
      return;
    }
    signal.addEventListener("abort", onAbort, { once: true });
  });
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

/**
 * The outbound policy for every web-to-API call (feature 013 US3, FR-026, FR-027). Reads (GET, HEAD) are attempted at most
 * three times, on a network error, a 5 s per-attempt timeout, 502, 503 or 504, with a 200 ms and then a 500 ms backoff. A
 * write is sent exactly once, with a 10 s timeout, and never retried. 400, 401, 404, 409 and 500 come straight back for the
 * caller (and assertApiOk) to handle. A caller's abort is never retried and propagates. Every attempt carries the same
 * traceparent. The timeout is a plain timer, not AbortSignal.timeout, so tests can drive it with fake timers.
 *
 * The per-attempt timeout bounds each attempt, not the whole call: a caller that needs one overall bound across the
 * retries (the Insights loaders: 5 s) passes a signal of its own, which cancels the attempt and any backoff.
 */
export async function fetchWithPolicy(request: Request): Promise<Response> {
  withTraceparent(request.headers);
  const method = request.method.toUpperCase();
  const caller = request.signal;
  const timeoutMs = attemptTimeoutMs(method);

  for (let attempt = 1; ; attempt += 1) {
    caller.throwIfAborted();

    const timeout = new AbortController();
    const timer = setTimeout(() => timeout.abort(new DOMException("The attempt timed out.", "TimeoutError")), timeoutMs);
    const signal = AbortSignal.any([caller, timeout.signal]);

    let response: Response;
    try {
      response = await abortable(globalThis.fetch(new Request(request, { signal })), signal);
    } catch (error) {
      clearTimeout(timer);
      const failure: AttemptFailure = timeout.signal.aborted ? "timeout" : "network";
      if (caller.aborted || !shouldRetry(method, failure, attempt)) {
        throw error;
      }
      await sleep(backoffMs(attempt), caller);
      continue;
    }
    clearTimeout(timer);

    if (!shouldRetry(method, response.status, attempt)) {
      if (response.status === 503) {
        await logServiceUnavailable(request, response, attempt);
      }
      return response;
    }

    await response.body?.cancel().catch(() => undefined);
    await sleep(backoffMs(attempt), caller);
  }
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
