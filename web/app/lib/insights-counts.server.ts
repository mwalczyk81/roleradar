/**
 * Runs the Insights count requests: count-only OData queries (`$top=0&$count=true`) built by the shared lens module.
 *
 * At most MAX_IN_FLIGHT requests run at once, each is cut off after REQUEST_TIMEOUT_MS, and one failing request only
 * makes the numbers that depend on it "unavailable". A 401 or 403 from the API is handed to assertApiOk after all
 * requests have settled, so the owner is re-challenged once (never in a loop) or sent to /not-authorized.
 *
 * Requests per page load: 18 lens counts + 2 coverage checks = 20; the 90 day view adds the 2 all-time interview
 * counts (interviewed, applied) = 22. The drill-only lens (offers without an interview) is never counted: its number is
 * derived from two counts the page already has.
 */
import { fetchOData } from "./api-client.server.js";
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

/** Runs the tasks with at most `limit` in flight; every task settles (a task never throws). */
async function runBounded(tasks: Array<() => Promise<void>>, limit: number): Promise<void> {
  let next = 0;
  const worker = async () => {
    while (next < tasks.length) {
      const task = tasks[next++];
      await task();
    }
  };
  await Promise.all(Array.from({ length: Math.min(limit, tasks.length) }, worker));
}

export async function loadInsightsCounts(args: {
  request: Request;
  session: { accessToken: string; sessionId: string };
  ctx: LensContext;
}): Promise<InsightsCounts> {
  const { request, session, ctx } = args;
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

  const runners = tasks.map((task) => async () => {
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
  });

  await runBounded(runners, MAX_IN_FLIGHT);

  if (authFailure) {
    await assertApiOk(authFailure, request, session.sessionId);
  }

  return counts;
}
