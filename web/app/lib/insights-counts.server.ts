/**
 * Runs the Insights count requests: count-only OData queries (`$top=0&$count=true`) built by the shared lens module.
 *
 * At most MAX_IN_FLIGHT requests run at once (the p-limit limit function can be shared with the stats request, which then counts
 * against the same bound), each is cut off after REQUEST_TIMEOUT_MS in all, and one failing request only
 * makes the numbers that depend on it "unavailable". A 401 or 403 from the API is handed to assertApiOk after all
 * requests have settled, so the owner is re-challenged once (never in a loop) or sent to /not-authorized.
 *
 * Each request goes through the outbound policy (fetchWithPolicy: a read is retried at most twice on a network error, a
 * timeout, 502, 503 or 504). REQUEST_TIMEOUT_MS is the bound over the whole of it, retries included: the controller below
 * cancels the attempt and any backoff, and a caller's abort is never retried. Without that bound three 5 s attempts could
 * keep the page waiting for 15 s.
 *
 * Requests per page load: 18 lens counts + 2 coverage checks = 20; the 90 day view adds the 2 all-time interview
 * counts (interviewed, applied) = 22. The drill-only lens (offers without an interview) is never counted: its number is
 * derived from two counts the page already has.
 */
import { fetchOData } from "./api-client.server.js";
import pLimit, { type LimitFunction } from "p-limit";
import { assertApiOk } from "./auth-guard.server.js";
import {
  COUNTED_LENS_KEYS,
  COVERAGE_KEYS,
  coverageCountQuery,
  coverageEntity,
  lensCountQuery,
  lensEntity,
  type CoverageKey,
  type LensContext,
  type LensEntity,
  type LensKey,
} from "./insights-lenses.js";
import type { CountValue, InsightsCounts } from "./insights-view.js";

export const MAX_IN_FLIGHT = 6;
export const REQUEST_TIMEOUT_MS = 5_000;

const ENTITY_PATH: Record<LensEntity, string> = {
  postings: "/odata/Postings",
  applications: "/odata/Applications",
};

interface CountTask {
  path: string;
  query: string;
  assign: (count: CountValue) => void;
}

export async function loadInsightsCounts(args: {
  request: Request;
  session: { accessToken: string; sessionId: string };
  ctx: LensContext;
  limit?: LimitFunction;
}): Promise<InsightsCounts> {
  const { request, session, ctx } = args;
  const limit = args.limit ?? pLimit(MAX_IN_FLIGHT);
  const counts: InsightsCounts = { lens: {}, coverage: {} };
  const tasks: CountTask[] = [];

  for (const key of COUNTED_LENS_KEYS) {
    tasks.push({
      path: ENTITY_PATH[lensEntity(key)],
      query: lensCountQuery(key, ctx),
      assign: (count) => {
        counts.lens[key as LensKey] = count;
      },
    });
  }

  for (const key of COVERAGE_KEYS) {
    tasks.push({
      path: ENTITY_PATH[coverageEntity(key)],
      query: coverageCountQuery(key, ctx),
      assign: (count) => {
        counts.coverage[key as CoverageKey] = count;
      },
    });
  }

  if (ctx.window !== "all") {
    // The all-time interview rate is always counted over all time, whatever window is active.
    const allTime: LensContext = { window: "all", asOf: ctx.asOf };
    counts.allTime = { interviewed: null, applied: null };
    tasks.push(
      {
        path: ENTITY_PATH.applications,
        query: lensCountQuery("interviewed", allTime),
        assign: (count) => {
          if (counts.allTime) counts.allTime.interviewed = count;
        },
      },
      {
        path: ENTITY_PATH.applications,
        query: lensCountQuery("applied", allTime),
        assign: (count) => {
          if (counts.allTime) counts.allTime.applied = count;
        },
      }
    );
  }

  let authFailure: { status: number } | undefined;

  const runners = tasks.map((task) => limit(async () => {
    const controller = new AbortController();
    let timer: ReturnType<typeof setTimeout> | undefined;
    const timeout = new Promise<null>((resolve) => {
      timer = setTimeout(() => {
        controller.abort();
        resolve(null);
      }, REQUEST_TIMEOUT_MS);
    });

    try {
      const result = await Promise.race([
        fetchOData<unknown>(task.path, task.query, session.accessToken, controller.signal),
        timeout,
      ]);

      if (result === null) {
        task.assign(null);
        return;
      }

      const status = result.response?.status;
      if (status === 401 || status === 403) {
        authFailure ??= { status };
        task.assign(null);
        return;
      }

      const total = result.data?.["@odata.count"];
      task.assign(!result.error && typeof total === "number" ? total : null);
    } catch {
      // A network error or an aborted request: only the numbers that depend on it become unavailable.
      task.assign(null);
    } finally {
      if (timer !== undefined) {
        clearTimeout(timer);
      }
    }
  }));

  await Promise.all(runners);

  if (authFailure) {
    await assertApiOk(authFailure, request, session.sessionId);
  }

  return counts;
}
