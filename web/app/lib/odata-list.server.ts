import { assertApiOk } from "./auth-guard.server.js";
import { fetchOData } from "./api-client.server.js";
import type { FilterExpression } from "./odata-client.js";

export const DEFAULT_PAGE_SIZE = 25;
export const MAX_PAGE_SIZE = 100;

export interface PagingParams {
  limit: number;
  offset: number;
}

/** limit is clamped to 1..100 (default 25) and offset to >= 0; anything non-numeric falls back to the defaults. */
export function parsePaging(url: URL): PagingParams {
  const rawLimit = Number(url.searchParams.get("limit"));
  const rawOffset = Number(url.searchParams.get("offset"));

  const limit =
    Number.isNaN(rawLimit) || rawLimit <= 0
      ? DEFAULT_PAGE_SIZE
      : Math.min(MAX_PAGE_SIZE, Math.max(1, Math.floor(rawLimit)));
  const offset = Number.isNaN(rawOffset) || rawOffset < 0 ? 0 : Math.max(0, Math.floor(rawOffset));

  return { limit, offset };
}

export interface SortParams<F extends string> {
  sort: F;
  direction: "asc" | "desc";
}

export interface DefaultSort<F extends string> {
  field: F;
  direction: "asc" | "desc";
}

/**
 * Reads ?sort= and ?direction= against an allowlist. A sort field that is not on the list is ignored and the
 * default order is used (the field never reaches the API). Direction defaults to desc unless it is exactly "asc".
 */
export function parseSort<F extends string>(
  url: URL,
  allowed: readonly F[],
  fallback: DefaultSort<F>
): SortParams<F> {
  const raw = url.searchParams.get("sort");
  const match = raw === null ? undefined : allowed.find((field) => field === raw);
  if (match === undefined) {
    return { sort: fallback.field, direction: fallback.direction };
  }
  return { sort: match, direction: url.searchParams.get("direction") === "asc" ? "asc" : "desc" };
}

/** Combines filter parts with AND; undefined when there is nothing to filter. */
export function andFilters(parts: FilterExpression[]): FilterExpression | undefined {
  if (parts.length === 0) {
    return undefined;
  }
  return parts.reduce((acc, part) => ({ type: "binary", operator: "and", left: acc, right: part }));
}

/** Case-insensitive "contains" over one or more fields (OR-ed); undefined for a blank term. */
export function containsAny(fields: readonly string[], term: string | null | undefined): FilterExpression | undefined {
  const trimmed = term?.trim();
  if (!trimmed) {
    return undefined;
  }
  const parts = fields.map<FilterExpression>((field) => ({ type: "function", name: "contains", field, value: trimmed }));
  return parts.reduce((acc, part) => ({ type: "binary", operator: "or", left: acc, right: part }));
}

export interface ODataListPage<T> {
  items: T[];
  totalCount: number;
}

/**
 * Runs one OData list query for a loader: 401 re-challenges once, 403 goes to /not-authorized,
 * and every other failure becomes a Response carrying the API status and message.
 */
export async function loadODataList<T>(args: {
  path: string;
  queryString: string;
  request: Request;
  session: { accessToken: string; sessionId: string };
  failureMessage: string;
}): Promise<ODataListPage<T>> {
  const { data, error, response } = await fetchOData<T>(args.path, args.queryString, args.session.accessToken);

  await assertApiOk(response, args.request, args.session.sessionId);

  if (error || !data) {
    throw new Response(error?.message || args.failureMessage, { status: response.status || 500 });
  }

  const items = data.value ?? [];
  return { items, totalCount: data["@odata.count"] ?? items.length };
}
