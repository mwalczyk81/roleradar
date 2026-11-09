import createClient, { type Client } from "openapi-fetch";
import type { paths } from "../../../contracts/client/api-types.js";
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

/** The fetch every web-to-API call goes through. Reads globalThis.fetch per call so tests can stub it. */
export function apiFetch(request: Request): Promise<Response> {
  withCorrelationHeader(request.headers);
  return globalThis.fetch(request);
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
