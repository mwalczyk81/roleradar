/**
 * The Insights view model: rates, sample flags, the two single-cohort funnels, the offers-without-interview note and
 * the backfill gate. Pure: it takes counts in and returns what the page renders, with no I/O.
 *
 * Every percentage carries its numerator and denominator ("x of y"). A denominator under 10 is a small sample, a zero
 * denominator is "no data", a failed request is "unavailable", and anything that depends on a derived flag that
 * reconcile-read-models has not yet written is "needs backfill": never a guessed number.
 */
import {
  COVERAGE_KEYS,
  FUNNELS,
  LENS_LABELS,
  lensListHref,
  type CoverageKey,
  type FunnelId,
  type LensContext,
  type LensKey,
} from "./insights-lenses.js";

export type CountValue = number | null;

export interface InsightsCounts {
  /** `@odata.count` per lens for the active window; null when that request failed; missing when not requested. */
  lens: Partial<Record<LensKey, CountValue>>;
  /** Windowed rows that carry the derived flag, per coverage check; null when that request failed. */
  coverage: Partial<Record<CoverageKey, CountValue>>;
  /** All-time interviewed and applied counts, used only when the active window is not already all time. */
  allTime?: { interviewed: CountValue; applied: CountValue };
}

export type ItemState = "ok" | "needs_backfill" | "unavailable";
export type RateState = "ok" | "small_sample" | "no_data" | "needs_backfill" | "unavailable";
export type BackfillState = "ok" | "needed" | "unknown";

export const SMALL_SAMPLE_BELOW = 10;
export const BACKFILL_COMMAND = "roleradar reconcile-read-models";
export const BACKFILL_HELP =
  "Run a dry run first, then confirm with --confirm RECONCILE-READ-MODELS (see docs/operations/reconcile-read-models.md).";

export function formatPercent(fraction: number): string {
  if (fraction === 0) {
    return "0%";
  }
  const percent = fraction * 100;
  return fraction < 0.1 ? `${percent.toFixed(1)}%` : `${Math.round(percent)}%`;
}

const POSTING_FLAG_LENSES: ReadonlySet<LensKey> = new Set<LensKey>(["reviewed", "applied-to", "screened-out", "expired"]);
const APPLICATION_FLAG_LENSES: ReadonlySet<LensKey> = new Set<LensKey>([
  "offers",
  "offers-interviewed",
  "rejected",
  "rejected-before-interview",
  "rejected-after-interview",
]);

export interface RateView {
  id: string;
  label: string;
  /** What the rate means, shown as help text. */
  help: string;
  numeratorKey: LensKey;
  denominatorKey: LensKey;
  numerator: CountValue;
  denominator: CountValue;
  /** A fraction between 0 and 1, or null when there is nothing honest to show. */
  value: number | null;
  state: RateState;
  /** "25%", "No data", "Needs backfill" or "Unavailable". */
  display: string;
  /** "4 of 18", "0 of 0", or empty when no numbers may be shown. */
  of: string;
  numeratorHref: string;
  denominatorHref: string;
  /** True for the all-time interview rate, which is always counted over all time. */
  allTime?: boolean;
}

export interface StageView {
  key: LensKey;
  label: string;
  value: CountValue;
  state: ItemState;
  href: string;
  /** The previous stage's count, for the funnel's "% of previous stage"; absent on the first stage. */
  denominator?: number;
}

export interface FunnelView {
  id: FunnelId;
  title: string;
  /** Which date the cohort is counted by. */
  basis: string;
  stages: StageView[];
  /** Set instead of drawing an inverted funnel when the counts disagree. */
  error?: string;
}

export interface CountItem {
  count: CountValue;
  state: ItemState;
  href: string;
}

export interface InsightsView {
  ctx: LensContext;
  backfill: { postings: BackfillState; applications: BackfillState; command: string; help: string };
  funnels: [FunnelView, FunnelView];
  /** Offers logged without an interview event: offers minus offers-interviewed. */
  offersWithoutInterview: CountItem;
  rates: RateView[];
  open: CountItem & { of: CountValue };
  rejectionSplit: { before: CountItem; after: CountItem };
}

function backfillState(flagged: CountValue | undefined, cohort: CountValue | undefined): BackfillState {
  if (typeof flagged !== "number" || typeof cohort !== "number") {
    return "unknown";
  }
  return flagged === cohort ? "ok" : "needed";
}

function gateFor(key: LensKey, backfill: { postings: BackfillState; applications: BackfillState }): ItemState {
  const state = POSTING_FLAG_LENSES.has(key)
    ? backfill.postings
    : APPLICATION_FLAG_LENSES.has(key)
      ? backfill.applications
      : "ok";
  return state === "ok" ? "ok" : state === "needed" ? "needs_backfill" : "unavailable";
}

export function buildInsightsView(counts: InsightsCounts, ctx: LensContext): InsightsView {
  const backfillStates = {
    postings: backfillState(counts.coverage["coverage-postings"], counts.lens.leads),
    applications: backfillState(counts.coverage["coverage-applications"], counts.lens.applied),
  };
  const allTimeCtx: LensContext = { window: "all", asOf: ctx.asOf };

  const countOf = (key: LensKey): CountValue => counts.lens[key] ?? null;
  const itemState = (key: LensKey): ItemState => {
    const gate = gateFor(key, backfillStates);
    if (gate !== "ok") {
      return gate;
    }
    return countOf(key) === null ? "unavailable" : "ok";
  };
  const item = (key: LensKey): CountItem => {
    const state = itemState(key);
    return { count: state === "ok" ? countOf(key) : null, state, href: lensListHref(key, ctx) };
  };

  // ---- rates ----
  const makeRate = (
    id: string,
    label: string,
    help: string,
    numeratorKey: LensKey,
    denominatorKey: LensKey,
    options: { rateCtx?: LensContext; numerator?: CountValue; denominator?: CountValue; allTime?: boolean } = {}
  ): RateView => {
    const rateCtx = options.rateCtx ?? ctx;
    const numerator = options.numerator !== undefined ? options.numerator : countOf(numeratorKey);
    const denominator = options.denominator !== undefined ? options.denominator : countOf(denominatorKey);
    const gates = [gateFor(numeratorKey, backfillStates), gateFor(denominatorKey, backfillStates)];

    let state: RateState;
    if (gates.includes("needs_backfill")) {
      state = "needs_backfill";
    } else if (gates.includes("unavailable") || numerator === null || denominator === null) {
      state = "unavailable";
    } else if (denominator === 0) {
      state = "no_data";
    } else if (numerator > denominator) {
      // By construction a rate's numerator is a subset of its denominator; disagreement means the counts cannot be trusted.
      state = "unavailable";
    } else {
      state = denominator < SMALL_SAMPLE_BELOW ? "small_sample" : "ok";
    }

    const shown = state === "ok" || state === "small_sample";
    const value = shown && numerator !== null && denominator !== null ? numerator / denominator : null;
    const display =
      value !== null
        ? formatPercent(value)
        : state === "no_data"
          ? "No data"
          : state === "needs_backfill"
            ? "Needs backfill"
            : "Unavailable";
    const of = shown ? `${numerator} of ${denominator}` : state === "no_data" ? "0 of 0" : "";

    return {
      id,
      label,
      help,
      numeratorKey,
      denominatorKey,
      numerator: shown || state === "no_data" ? numerator : null,
      denominator: shown || state === "no_data" ? denominator : null,
      value,
      state,
      display,
      of,
      numeratorHref: lensListHref(numeratorKey, rateCtx),
      denominatorHref: lensListHref(denominatorKey, rateCtx),
      ...(options.allTime ? { allTime: true } : {}),
    };
  };

  const allTimeInterviewed = ctx.window === "all" ? countOf("interviewed") : (counts.allTime?.interviewed ?? null);
  const allTimeApplied = ctx.window === "all" ? countOf("applied") : (counts.allTime?.applied ?? null);

  const rates: RateView[] = [
    makeRate(
      "application-rate",
      "Application rate",
      "Reviewed postings first seen in the window that have an application, out of reviewed postings first seen in the window. One cohort, so never above 100%.",
      "applied-to",
      "reviewed"
    ),
    makeRate(
      "screen-out-rate",
      "Screen-out rate",
      "Screened-out postings out of reviewed postings, over the same cohort as the application rate.",
      "screened-out",
      "reviewed"
    ),
    makeRate(
      "interview-rate",
      "Interview rate",
      "Applications with at least one interview event, out of applications at least 14 days old (the mature cohort).",
      "mature-interviewed",
      "mature"
    ),
    makeRate(
      "interview-rate-all-time",
      "Interview rate, all time",
      "Applications with at least one interview event, out of all applications, over all time. Lower confidence: it includes applications too recent to have heard back.",
      "interviewed",
      "applied",
      { rateCtx: allTimeCtx, numerator: allTimeInterviewed, denominator: allTimeApplied, allTime: true }
    ),
    makeRate(
      "rejection-rate",
      "Rejection rate",
      "Applications with a rejected event, out of all applications in the window; split below into rejected before any interview and after one.",
      "rejected",
      "applied"
    ),
    makeRate(
      "offer-rate-interviews",
      "Offer rate, of interviews",
      "Applications with both an offer event and an interview event, out of applications with an interview.",
      "offers-interviewed",
      "interviewed"
    ),
    makeRate(
      "offer-rate-applications",
      "Offer rate, of applications",
      "Applications with an offer event, out of all applications in the window.",
      "offers",
      "applied"
    ),
    makeRate(
      "ghost-rate",
      "Ghost rate",
      "Applications still applied, with no response event, older than 30 days, out of applications at least 30 days old.",
      "ghosted",
      "ghost-eligible"
    ),
  ];

  // ---- funnels ----
  const buildFunnel = (id: FunnelId, title: string, basis: string): FunnelView => {
    const keys: readonly LensKey[] = FUNNELS[id];
    const stages: StageView[] = keys.map((key) => {
      const state = itemState(key);
      return { key, label: LENS_LABELS[key], value: state === "ok" ? countOf(key) : null, state, href: lensListHref(key, ctx) };
    });

    for (let i = 1; i < stages.length; i++) {
      const previous = stages[i - 1];
      const current = stages[i];
      if (previous.state === "ok" && current.state === "ok" && previous.value !== null && current.value !== null && current.value > previous.value) {
        return {
          id,
          title,
          basis,
          stages: [],
          error: `${current.label} (${current.value}) is higher than ${previous.label} (${previous.value}), so the counts disagree and this funnel is not drawn.`,
        };
      }
    }

    return {
      id,
      title,
      basis,
      stages: stages.map((stage, i) => {
        const previous = i > 0 ? stages[i - 1] : undefined;
        return previous && previous.state === "ok" && previous.value !== null ? { ...stage, denominator: previous.value } : stage;
      }),
    };
  };

  const funnels: [FunnelView, FunnelView] = [
    buildFunnel("postings", "Postings first seen in window", "Counted by the date RoleRadar first recorded each posting."),
    buildFunnel("applications", "Applications applied in window", "Counted by application date."),
  ];

  // ---- offers logged without an interview ----
  const offersState = itemState("offers");
  const offersInterviewedState = itemState("offers-interviewed");
  const offers = countOf("offers");
  const offersInterviewed = countOf("offers-interviewed");
  const withoutInterviewState: ItemState =
    offersState === "needs_backfill" || offersInterviewedState === "needs_backfill"
      ? "needs_backfill"
      : offersState === "ok" && offersInterviewedState === "ok"
        ? "ok"
        : "unavailable";
  const withoutInterview =
    withoutInterviewState === "ok" && offers !== null && offersInterviewed !== null && offers >= offersInterviewed ? offers - offersInterviewed : null;

  return {
    ctx,
    backfill: { ...backfillStates, command: BACKFILL_COMMAND, help: BACKFILL_HELP },
    funnels,
    offersWithoutInterview: {
      count: withoutInterview,
      state: withoutInterview === null && withoutInterviewState === "ok" ? "unavailable" : withoutInterviewState,
      href: lensListHref("offers-without-interview", ctx),
    },
    rates,
    open: { ...item("open"), of: countOf("applied") },
    rejectionSplit: { before: item("rejected-before-interview"), after: item("rejected-after-interview") },
  };
}

// COVERAGE_KEYS is re-exported for the loader and tests that enumerate the checks.
export { COVERAGE_KEYS };
