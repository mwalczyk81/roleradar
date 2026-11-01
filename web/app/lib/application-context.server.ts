import type { createApiClient } from "./api-client.server.js";

export type ApiClient = ReturnType<typeof createApiClient>;

export interface PostingContextSummary {
  id: string;
  title: string;
  companyName?: string;
  location?: string | null;
  disposition?: string;
  isUnavailable?: boolean;
}

export interface CompanyContextSummary {
  id: string;
  name: string;
  status?: string;
  isUnavailable?: boolean;
}

export interface ApplicationContextCache {
  postings: Map<string, PostingContextSummary>;
  companies: Map<string, CompanyContextSummary>;
}

export function createApplicationContextCache(): ApplicationContextCache {
  return {
    postings: new Map(),
    companies: new Map(),
  };
}

/**
 * Concurrency-bounded parallel mapper.
 * Ensures at most `limit` asynchronous tasks are in flight simultaneously.
 */
async function mapConcurrent<T, R>(
  items: readonly T[],
  limit: number,
  fn: (item: T) => Promise<R>
): Promise<R[]> {
  if (items.length === 0) return [];
  const results: R[] = new Array(items.length);
  let nextIndex = 0;

  const worker = async () => {
    while (nextIndex < items.length) {
      const index = nextIndex++;
      results[index] = await fn(items[index]);
    }
  };

  const poolSize = Math.max(1, Math.min(limit, items.length));
  const workers = Array.from({ length: poolSize }, () => worker());
  await Promise.all(workers);
  return results;
}

export interface ResolveContextOptions {
  concurrency?: number;
  cache?: ApplicationContextCache;
}

/**
 * Resolves posting titles and company names for a slice of applications.
 * Guarantees:
 * 1. Only unique posting and company IDs are queried (deduplicated).
 * 2. In-flight requests are bounded by concurrency limit (default 5).
 * 3. Tolerates lookup failures (404/500/network) with fallback summaries instead of failing the page.
 * 4. Supports per-request caching across calls.
 */
export async function resolveApplicationContext(
  client: ApiClient,
  items: readonly { postingId: string; companyId: string }[],
  options?: ResolveContextOptions
): Promise<{
  postingMap: Record<string, PostingContextSummary>;
  companyMap: Record<string, CompanyContextSummary>;
}> {
  const concurrency = Math.max(1, options?.concurrency ?? 5);
  const cache = options?.cache ?? createApplicationContextCache();

  // 1. Deduplicate IDs from the page slice
  const uniquePostingIds = Array.from(
    new Set(items.map((i) => i.postingId).filter((id): id is string => Boolean(id && id.trim())))
  );

  const uniqueCompanyIds = Array.from(
    new Set(items.map((i) => i.companyId).filter((id): id is string => Boolean(id && id.trim())))
  );

  // 2. Identify uncached IDs
  const postingsToFetch = uniquePostingIds.filter((id) => !cache.postings.has(id));
  const companiesToFetch = uniqueCompanyIds.filter((id) => !cache.companies.has(id));

  // 3. Fetch postings with bounded concurrency and failure tolerance
  await mapConcurrent(postingsToFetch, concurrency, async (id) => {
    try {
      const res = await client.GET("/api/v1/postings/{id}", {
        params: { path: { id } },
      });

      if (res.data && !res.error) {
        const p = res.data;
        const summary: PostingContextSummary = {
          id: p.id,
          title: p.title || `Posting ${id.slice(0, 8)}`,
          companyName: p.companyName,
          location: p.location,
          disposition: p.disposition,
        };
        cache.postings.set(id, summary);
      } else {
        cache.postings.set(id, {
          id,
          title: `Unavailable (${id.slice(0, 8)}...)`,
          companyName: "Unavailable",
          isUnavailable: true,
        });
      }
    } catch {
      cache.postings.set(id, {
        id,
        title: `Unavailable (${id.slice(0, 8)}...)`,
        companyName: "Unavailable",
        isUnavailable: true,
      });
    }
  });

  // 4. Fetch companies with bounded concurrency and failure tolerance
  await mapConcurrent(companiesToFetch, concurrency, async (id) => {
    try {
      const res = await client.GET("/api/v1/companies/{id}", {
        params: { path: { id } },
      });

      if (res.data && !res.error) {
        const c = res.data;
        const summary: CompanyContextSummary = {
          id: c.id,
          name: c.name || `Company ${id.slice(0, 8)}`,
          status: c.status,
        };
        cache.companies.set(id, summary);
      } else {
        cache.companies.set(id, {
          id,
          name: `Unavailable (${id.slice(0, 8)}...)`,
          isUnavailable: true,
        });
      }
    } catch {
      cache.companies.set(id, {
        id,
        name: `Unavailable (${id.slice(0, 8)}...)`,
        isUnavailable: true,
      });
    }
  });

  // 5. Construct result lookup objects for the requested IDs
  const postingMap: Record<string, PostingContextSummary> = {};
  for (const id of uniquePostingIds) {
    const summary = cache.postings.get(id);
    if (summary) {
      postingMap[id] = summary;
    }
  }

  const companyMap: Record<string, CompanyContextSummary> = {};
  for (const id of uniqueCompanyIds) {
    const summary = cache.companies.get(id);
    if (summary) {
      companyMap[id] = summary;
    }
  }

  return { postingMap, companyMap };
}
