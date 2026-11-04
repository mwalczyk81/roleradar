/**
 * The Insights lens catalog (ADR 0029, specs/007-insights-dashboard/contracts/insights-lenses.md).
 *
 * A lens is a named filter. This module is the ONLY place that builds a count filter or a drill-down filter, so the
 * number on the dashboard and the number of rows in the list it links to come from the same function and agree by
 * construction. Values (dispositions, application status) come from dispositions.ts and application-enums.ts.
 *
 * Windows and cutoffs are whole UTC days. "Last 90 days" includes asOf: [asOf-89d, asOf]. Application age cutoffs are
 * expressed on appliedAt because ageInDays is derived and blocked from $filter:
 *   at least 14 days old    appliedAt <  asOf-13d 00:00Z
 *   at least 30 days old    appliedAt <  asOf-29d 00:00Z
 *   older than 30 days      appliedAt <  asOf-30d 00:00Z
 */
import { buildODataQuery, formatFilterExpression, toODataDateTime, type FilterExpression } from "./odata-client.js";
import { DISPOSITION } from "./dispositions.js";
import { APPLICATION_STATUS } from "./application-enums.js";

export type LensEntity = "postings" | "applications";
export type LensWindow = "90d" | "all";

export interface LensContext {
  window: LensWindow;
  /** yyyy-MM-dd, UTC. Pinned in every link so a dashboard and its lists agree across midnight. */
  asOf: string;
}

/** Lenses whose count the page requests: 18 count-only requests per load. */
export const COUNTED_LENS_KEYS = [
  "leads",
  "reviewed",
  "applied-to",
  "screened-out",
  "expired",
  "applied",
  "responded",
  "interviewed",
  "offers",
  "offers-interviewed",
  "mature",
  "mature-interviewed",
  "rejected",
  "rejected-before-interview",
  "rejected-after-interview",
  "ghost-eligible",
  "ghosted",
  "open",
] as const;

/**
 * Lenses that exist only so a number the page derives has its own drill-down. "Offers without an interview" is
 * offers minus offers-with-interview, two counts the page already has, so it costs no request; its list holds exactly
 * those rows (a partition of the offers lens), which the parity tests prove against the live API.
 */
export const DRILL_ONLY_LENS_KEYS = ["offers-without-interview"] as const;

export const LENS_KEYS = [...COUNTED_LENS_KEYS, ...DRILL_ONLY_LENS_KEYS] as const;

export type CountedLensKey = (typeof COUNTED_LENS_KEYS)[number];
export type LensKey = (typeof LENS_KEYS)[number];

export const COVERAGE_KEYS = ["coverage-postings", "coverage-applications"] as const;
export type CoverageKey = (typeof COVERAGE_KEYS)[number];

/** Each funnel is one cohort. Within a funnel every stage is a subset of the stage before it. */
export const FUNNELS = {
  postings: ["leads", "reviewed", "applied-to"],
  applications: ["applied", "responded", "interviewed", "offers-interviewed"],
} as const satisfies Record<string, readonly LensKey[]>;

export type FunnelId = keyof typeof FUNNELS;

export const LENS_LABELS: Record<LensKey, string> = {
  leads: "Leads",
  reviewed: "Reviewed",
  "applied-to": "Applied to",
  "screened-out": "Screened out",
  expired: "Expired",
  applied: "Applied",
  responded: "Responded",
  interviewed: "Interviewed",
  offers: "Offers",
  "offers-interviewed": "Offers",
  "offers-without-interview": "Offers without an interview",
  mature: "At least 14 days old",
  "mature-interviewed": "Interviewed, at least 14 days old",
  rejected: "Rejected",
  "rejected-before-interview": "Rejected before an interview",
  "rejected-after-interview": "Rejected after an interview",
  "ghost-eligible": "At least 30 days old",
  ghosted: "Ghosted",
  open: "Open",
};

const POSTING_LENSES: ReadonlySet<LensKey> = new Set<LensKey>(["leads", "reviewed", "applied-to", "screened-out", "expired"]);

export function isLensKey(value: unknown): value is LensKey {
  return typeof value === "string" && (LENS_KEYS as readonly string[]).includes(value);
}

export function lensEntity(key: LensKey): LensEntity {
  return POSTING_LENSES.has(key) ? "postings" : "applications";
}

// ---- dates (whole UTC days) ----

const DATE_ONLY = /^\d{4}-\d{2}-\d{2}$/;
const DAY_MS = 86_400_000;

function parseDay(value: string): Date | undefined {
  if (!DATE_ONLY.test(value)) {
    return undefined;
  }
  const parsed = new Date(`${value}T00:00:00Z`);
  if (Number.isNaN(parsed.getTime()) || parsed.toISOString().slice(0, 10) !== value) {
    return undefined;
  }
  return parsed;
}

function formatDay(date: Date): string {
  return date.toISOString().slice(0, 10);
}

function shiftDay(asOf: string, days: number): string {
  const base = parseDay(asOf);
  if (!base) {
    throw new Error(`Invalid asOf date: ${asOf}`);
  }
  return formatDay(new Date(base.getTime() + days * DAY_MS));
}

function literal(day: string) {
  const value = toODataDateTime(day);
  if (!value) {
    throw new Error(`Invalid date: ${day}`);
  }
  return value;
}

/** Today in UTC as yyyy-MM-dd. */
export function defaultAsOf(now: Date = new Date()): string {
  return formatDay(now);
}

// ---- filter construction ----

type Part = FilterExpression;

const cmp = (field: string, operator: "eq" | "ne" | "lt" | "ge", value: string | boolean | null | ReturnType<typeof literal>): Part => ({
  type: "comparison",
  operator,
  field,
  value,
});

function and(parts: Part[]): Part | undefined {
  if (parts.length === 0) {
    return undefined;
  }
  return parts.reduce((left, right) => ({ type: "binary", operator: "and", left, right }));
}

const or = (left: Part, right: Part): Part => ({ type: "binary", operator: "or", left, right });

const reviewed = (): Part =>
  or(
    { type: "binary", operator: "and", left: cmp("disposition", "ne", DISPOSITION.Lead), right: cmp("disposition", "ne", DISPOSITION.Unscored) },
    cmp("hasApplication", "eq", true)
  );

/** A lens's own clauses, ANDed together, before the window clause. */
function lensClauses(key: LensKey, ctx: LensContext): Part[] {
  const appliedBefore = (days: number): Part => cmp("appliedAt", "lt", literal(shiftDay(ctx.asOf, -days)));
  switch (key) {
    case "leads":
    case "applied":
      return [];
    case "reviewed":
      return [reviewed()];
    case "applied-to":
      return [reviewed(), cmp("hasApplication", "eq", true)];
    case "screened-out":
      return [reviewed(), cmp("disposition", "eq", DISPOSITION.ScreenedOut)];
    case "expired":
      return [cmp("disposition", "eq", DISPOSITION.Closed), cmp("hasApplication", "eq", false)];
    case "responded":
      return [cmp("firstResponseAt", "ne", null)];
    case "interviewed":
      return [cmp("hasInterview", "eq", true)];
    case "offers":
      return [cmp("hasOffer", "eq", true)];
    case "offers-interviewed":
      return [cmp("hasOffer", "eq", true), cmp("hasInterview", "eq", true)];
    case "offers-without-interview":
      return [cmp("hasOffer", "eq", true), cmp("hasInterview", "eq", false)];
    case "mature":
      return [appliedBefore(13)];
    case "mature-interviewed":
      return [appliedBefore(13), cmp("hasInterview", "eq", true)];
    case "rejected":
      return [cmp("hasRejection", "eq", true)];
    case "rejected-before-interview":
      return [cmp("hasRejection", "eq", true), cmp("rejectedBeforeInterview", "eq", true)];
    case "rejected-after-interview":
      return [cmp("hasRejection", "eq", true), cmp("rejectedBeforeInterview", "eq", false)];
    case "ghost-eligible":
      return [appliedBefore(29)];
    case "ghosted":
      return [cmp("status", "eq", APPLICATION_STATUS.Applied), cmp("firstResponseAt", "eq", null), appliedBefore(30)];
    case "open":
      return [cmp("status", "eq", APPLICATION_STATUS.Applied)];
  }
}

function windowClauses(entity: LensEntity, ctx: LensContext): Part[] {
  const field = entity === "postings" ? "firstSeenAt" : "appliedAt";
  const end = cmp(field, "lt", literal(shiftDay(ctx.asOf, 1)));
  if (ctx.window === "all") {
    return [end];
  }
  return [cmp(field, "ge", literal(shiftDay(ctx.asOf, -89))), end];
}

export function lensFilter(key: LensKey, ctx: LensContext): FilterExpression {
  const filter = and([...lensClauses(key, ctx), ...windowClauses(lensEntity(key), ctx)]);
  if (!filter) {
    throw new Error("A lens always has a window clause.");
  }
  return filter;
}

export function lensFilterString(key: LensKey, ctx: LensContext): string {
  return formatFilterExpression(lensFilter(key, ctx));
}

/** A count-only request: `$filter=...&$top=0&$count=true`. The count is the response's `@odata.count`. */
export function lensCountQuery(key: LensKey, ctx: LensContext): string {
  return buildODataQuery({ filter: lensFilter(key, ctx), countOnly: true });
}

export function lensListHref(key: LensKey, ctx: LensContext): string {
  return `/${lensEntity(key)}?lens=${key}&window=${ctx.window}&asOf=${ctx.asOf}`;
}

// ---- backfill coverage checks ----
// MongoDB's { flag: false } does not match a document that lacks the flag, so before reconcile-read-models has run the
// "eq false" counts would be silently low. Each check counts the windowed rows that carry the flag; the page compares
// it with the matching cohort count (leads or applied) and refuses flag-dependent numbers when they differ.

export function coverageEntity(key: CoverageKey): LensEntity {
  return key === "coverage-postings" ? "postings" : "applications";
}

export function coverageFilter(key: CoverageKey, ctx: LensContext): FilterExpression {
  const entity = coverageEntity(key);
  const flag = entity === "postings" ? "hasApplication" : "rejectedBeforeInterview";
  const carriesFlag = or(cmp(flag, "eq", true), cmp(flag, "eq", false));
  const filter = and([carriesFlag, ...windowClauses(entity, ctx)]);
  if (!filter) {
    throw new Error("A coverage check always has a window clause.");
  }
  return filter;
}

export function coverageFilterString(key: CoverageKey, ctx: LensContext): string {
  return formatFilterExpression(coverageFilter(key, ctx));
}

export function coverageCountQuery(key: CoverageKey, ctx: LensContext): string {
  return buildODataQuery({ filter: coverageFilter(key, ctx), countOnly: true });
}

// ---- URL parameters on the list pages ----

export interface ParsedLens {
  key: LensKey;
  ctx: LensContext;
}

/**
 * Reads `?window=&asOf=` for the Insights page and the list pages. A bad window or asOf falls back to the defaults
 * (90d, today UTC) and is never forwarded. asOf may be at most one day ahead of now (time zones).
 */
export function parseWindowParams(url: URL, now: Date = new Date()): LensContext {
  const window: LensWindow = url.searchParams.get("window") === "all" ? "all" : "90d";

  const today = defaultAsOf(now);
  const rawAsOf = url.searchParams.get("asOf");
  let asOf = today;
  if (rawAsOf !== null) {
    const parsed = parseDay(rawAsOf);
    const latest = parseDay(shiftDay(today, 1));
    if (parsed && latest && parsed.getTime() <= latest.getTime()) {
      asOf = rawAsOf;
    }
  }

  return { window, asOf };
}

/**
 * Reads `?lens=&window=&asOf=` for a list page. Returns undefined for an unknown key or a key that belongs to the
 * other entity. A raw `$filter` in the URL is never read.
 */
export function parseLensParams(url: URL, entity: LensEntity, now: Date = new Date()): ParsedLens | undefined {
  const key = url.searchParams.get("lens");
  if (!isLensKey(key) || lensEntity(key) !== entity) {
    return undefined;
  }
  return { key, ctx: parseWindowParams(url, now) };
}

/** The pair of query-string values the list pages put in their lens chip. */
export function lensChipLabel(parsed: ParsedLens): string {
  const window = parsed.ctx.window === "90d" ? "last 90 days" : "all time";
  return `${LENS_LABELS[parsed.key]}, ${window}, as of ${parsed.ctx.asOf}`;
}
