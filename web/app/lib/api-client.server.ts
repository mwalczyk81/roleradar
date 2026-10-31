import createClient, { type Client } from "openapi-fetch";
import type { paths } from "../../../contracts/client/api-types.js";

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

export function createApiClient(accessToken?: string): Client<paths> {
  const baseUrl = getApiBaseUrl();
  return createClient<paths>({
    baseUrl,
    headers: accessToken
      ? {
          Authorization: `Bearer ${accessToken}`,
        }
      : undefined,
  });
}
