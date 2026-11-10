import createClient, { type Client } from "openapi-fetch";
import type { paths } from "../../../contracts/client/api-types.js";
import { log } from "./log.server.js";
import {
  SERVICE_UNAVAILABLE_MESSAGE,
  acceptCorrelationId,
  attemptTimeoutMs,
  backoffMs,
  shouldRetry,
  type AttemptFailure,
} from "./observability.js";
import { getRequestContext } from "./request-context.server.js";

export interface UiError {
  errorCode: string;
  message: string;
  fieldErrors: Record<string, string>;
  isConflict: boolean;
  isNotFound: boolean;
  isForbidden: boolean;
}

export function mapApiError(error: unknown, status?: number): UiError {
  const envelope = error as {
    ok?: boolean;
    errorCode?: string;
    message?: string;
    details?: Array<{ field?: string; message?: string }>;
  } | null | undefined;

  const fieldErrors: Record<string, string> = {};
  if (envelope?.details && Array.isArray(envelope.details)) {
    for (const d of envelope.details) {
      if (d.field && d.message) {
        fieldErrors[d.field] = d.message;
      }
    }
  }

  const errorCode = envelope?.errorCode ?? (status ? `HTTP_${status}` : "UNKNOWN_ERROR");
  const message = envelope?.message ?? "An unexpected API error occurred.";

  return {
    errorCode,
    message,
    fieldErrors,
    isConflict: status === 409 || errorCode === "CONCURRENCY_CONFLICT",
    isNotFound: status === 404 || errorCode === "NOT_FOUND",
    isForbidden: status === 403 || errorCode === "FORBIDDEN",
  };
}

export function getApiBaseUrl(): string {
  return process.env.ROLERADAR_API_BASE_URL ?? "https://localhost:5002";
}

export const CORRELATION_HEADER = "X-Correlation-ID";

/** Adds the current request's X-Correlation-ID (feature 013) to outbound headers. Outside a request, adds nothing. */
export function withCorrelationHeader(headers: Headers): Headers {
  const correlationId = getRequestContext()?.correlationId;
  if (correlationId) {
    headers.set(CORRELATION_HEADER, correlationId);
  }
  return headers;
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

async function envelopeCorrelationId(response: Response): Promise<string | undefined> {
  try {
    const body: unknown = await response.clone().json();
    if (typeof body === "object" && body !== null && "correlationId" in body && typeof body.correlationId === "string") {
      const fromEnvelope = acceptCorrelationId(body.correlationId);
      if (fromEnvelope) {
        return fromEnvelope;
      }
    }
  } catch {
    // Not a JSON envelope (a proxy's page, an empty body): fall back to the header.
  }
  return acceptCorrelationId(response.headers.get(CORRELATION_HEADER));
}

/**
 * One warn record when the API finally answers 503 (after any retries), carrying the correlation id the API's envelope
 * names so the owner can find its side of the request. The envelope's text and the query string are never logged.
 */
async function logServiceUnavailable(request: Request, response: Response, attempts: number): Promise<void> {
  log.warn("The API answered 503: its data store is unavailable", {
    event: "ApiServiceUnavailable",
    correlationId: await envelopeCorrelationId(response),
    props: { method: request.method.toUpperCase(), path: new URL(request.url).pathname, status: 503, attempts },
  });
}

/**
 * The outbound policy for every web-to-API call (feature 013 US3, FR-026, FR-027). Reads (GET, HEAD) are attempted at most
 * three times, on a network error, a 5 s per-attempt timeout, 502, 503 or 504, with a 200 ms and then a 500 ms backoff. A
 * write is sent exactly once, with a 10 s timeout, and never retried. 400, 401, 404, 409 and 500 come straight back for the
 * caller (and assertApiOk) to handle. A caller's abort is never retried and propagates. Every attempt carries the same
 * X-Correlation-ID. The timeout is a plain timer, not AbortSignal.timeout, so tests can drive it with fake timers.
 *
 * The per-attempt timeout bounds each attempt, not the whole call: a caller that needs one overall bound across the
 * retries (the Insights loaders: 5 s) passes a signal of its own, which cancels the attempt and any backoff.
 */
export async function fetchWithPolicy(request: Request): Promise<Response> {
  withCorrelationHeader(request.headers);
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
      const errJson = await response.json();
      if (errJson?.message) {
        errorMsg = errJson.message;
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
