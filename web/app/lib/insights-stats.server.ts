/**
 * Loads the Insights stats (GET /api/v1/insights/stats) with the generated, typed client.
 *
 * The request goes through the outbound policy (a GET is retried at most twice on a 502, 503, 504, a timeout or a network
 * error) and is cut off after REQUEST_TIMEOUT_MS in all, retries included, because the controller below cancels the
 * attempt and any backoff. It shares the concurrency limiter with the counts, so it starts with
 * the first wave of counts and the total in flight never exceeds the bound. A failure of any kind (an error status, a
 * network error, a timeout) only makes the trend and median tiles "unavailable": this function never throws. A 401 or
 * 403 is reported as `authFailure` so the loader can hand it to `assertApiOk` once, after the counts have settled.
 */
import { createApiClient } from "./api-client.server.js";
import { REQUEST_TIMEOUT_MS } from "./insights-counts.server.js";
import type { LensContext } from "./insights-lenses.js";
import { buildStatsView, statsWindow, UNAVAILABLE_STATS, type StatsView } from "./insights-stats.js";
import { createLimiter, type Limiter } from "./request-limiter.server.js";

export interface InsightsStatsResult {
  state: "ok" | "unavailable";
  view: StatsView;
  authFailure?: { status: number };
}

function unavailable(authFailure?: { status: number }): InsightsStatsResult {
  return authFailure
    ? { state: "unavailable", view: UNAVAILABLE_STATS, authFailure }
    : { state: "unavailable", view: UNAVAILABLE_STATS };
}

export async function loadInsightsStats(args: {
  request: Request;
  session: { accessToken: string; sessionId: string };
  ctx: LensContext;
  limiter?: Limiter;
}): Promise<InsightsStatsResult> {
  const { session, ctx } = args;
  const limiter = args.limiter ?? createLimiter(1);

  return limiter.run(async () => {
    const controller = new AbortController();
    let timer: ReturnType<typeof setTimeout> | undefined;
    const timeout = new Promise<null>((resolve) => {
      timer = setTimeout(() => {
        controller.abort();
        resolve(null);
      }, REQUEST_TIMEOUT_MS);
    });

    try {
      const api = createApiClient(session.accessToken);
      const result = await Promise.race([
        api.GET("/api/v1/insights/stats", {
          params: { query: { window: statsWindow(ctx), asOf: ctx.asOf } },
          signal: controller.signal,
        }),
        timeout,
      ]);

      if (result === null) {
        return unavailable();
      }

      const status = result.response?.status;
      if (status === 401 || status === 403) {
        return unavailable({ status });
      }

      if (result.error || !result.data) {
        return unavailable();
      }

      return { state: "ok", view: buildStatsView(result.data) };
    } catch {
      // A network error or an aborted request: only the trend and median tiles become unavailable.
      return unavailable();
    } finally {
      if (timer !== undefined) {
        clearTimeout(timer);
      }
    }
  });
}
