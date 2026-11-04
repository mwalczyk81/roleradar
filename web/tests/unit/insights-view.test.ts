import { describe, it, expect } from "vitest";
import {
  buildInsightsView,
  formatPercent,
  BACKFILL_COMMAND,
  type InsightsCounts,
  type RateView,
} from "../../app/lib/insights-view.js";
import type { LensContext, LensKey } from "../../app/lib/insights-lenses.js";

const CTX: LensContext = { window: "90d", asOf: "2026-09-30" };
const ALL: LensContext = { window: "all", asOf: "2026-09-30" };

/** A consistent, fully covered set of counts (every funnel nests; flags present everywhere). */
function counts(overrides: Partial<Record<LensKey, number | null>> = {}, extra: Partial<InsightsCounts> = {}): InsightsCounts {
  const lens: Partial<Record<LensKey, number | null>> = {
    leads: 120,
    reviewed: 60,
    "applied-to": 30,
    "screened-out": 15,
    expired: 7,
    applied: 40,
    responded: 24,
    interviewed: 16,
    offers: 5,
    "offers-interviewed": 4,
    mature: 36,
    "mature-interviewed": 15,
    rejected: 12,
    "rejected-before-interview": 7,
    "rejected-after-interview": 5,
    "ghost-eligible": 30,
    ghosted: 9,
    open: 22,
    ...overrides,
  };
  return {
    lens,
    coverage: { "coverage-postings": 120, "coverage-applications": 40 },
    allTime: { interviewed: 20, applied: 55 },
    ...extra,
  };
}

function rate(view: ReturnType<typeof buildInsightsView>, id: string): RateView {
  const found = view.rates.find((r) => r.id === id);
  if (!found) throw new Error(`rate ${id} missing: ${view.rates.map((r) => r.id).join(", ")}`);
  return found;
}

describe("formatPercent", () => {
  it("rounds to whole percents, keeps one decimal below 10%, and prints zero plainly", () => {
    expect(formatPercent(0.25)).toBe("25%");
    expect(formatPercent(1 / 3)).toBe("33%");
    expect(formatPercent(0.05)).toBe("5.0%");
    expect(formatPercent(0.034)).toBe("3.4%");
    expect(formatPercent(1)).toBe("100%");
    expect(formatPercent(0)).toBe("0%");
  });
});

describe("insights rates: x of y, sample flags, no data", () => {
  it("computes every rate from its numerator and denominator lens counts and shows 'x of y'", () => {
    const view = buildInsightsView(counts(), CTX);

    expect(rate(view, "application-rate")).toMatchObject({ numerator: 30, denominator: 60, display: "50%", of: "30 of 60", state: "ok" });
    expect(rate(view, "screen-out-rate")).toMatchObject({ numerator: 15, denominator: 60, of: "15 of 60" });
    expect(rate(view, "interview-rate")).toMatchObject({ numerator: 15, denominator: 36, of: "15 of 36" });
    expect(rate(view, "rejection-rate")).toMatchObject({ numerator: 12, denominator: 40, of: "12 of 40" });
    expect(rate(view, "offer-rate-interviews")).toMatchObject({ numerator: 4, denominator: 16, of: "4 of 16" });
    expect(rate(view, "offer-rate-applications")).toMatchObject({ numerator: 5, denominator: 40, of: "5 of 40" });
    expect(rate(view, "ghost-rate")).toMatchObject({ numerator: 9, denominator: 30, of: "9 of 30", display: "30%" });
  });

  it("links the numerator and denominator to the lists with the same lens, window and asOf", () => {
    const view = buildInsightsView(counts(), CTX);
    const application = rate(view, "application-rate");
    expect(application.numeratorHref).toBe("/postings?lens=applied-to&window=90d&asOf=2026-09-30");
    expect(application.denominatorHref).toBe("/postings?lens=reviewed&window=90d&asOf=2026-09-30");
    expect(rate(view, "ghost-rate").numeratorHref).toBe("/applications?lens=ghosted&window=90d&asOf=2026-09-30");
  });

  it("flags a denominator under 10 as a small sample and keeps the value", () => {
    const view = buildInsightsView(counts({ reviewed: 9, "applied-to": 3, "screened-out": 2 }), CTX);
    expect(rate(view, "application-rate")).toMatchObject({ state: "small_sample", denominator: 9, of: "3 of 9", display: "33%" });
    expect(rate(view, "screen-out-rate").state).toBe("small_sample");
    expect(rate(view, "ghost-rate").state).toBe("ok");
  });

  it("treats exactly 10 as a sample and 9 as small", () => {
    expect(rate(buildInsightsView(counts({ reviewed: 10, "applied-to": 5 }), CTX), "application-rate").state).toBe("ok");
    expect(rate(buildInsightsView(counts({ reviewed: 9, "applied-to": 5 }), CTX), "application-rate").state).toBe("small_sample");
  });

  it("shows 'No data' and '0 of 0' for a zero denominator, never a percentage, NaN or Infinity", () => {
    const view = buildInsightsView(
      counts({ reviewed: 0, "applied-to": 0, "screened-out": 0, mature: 0, "mature-interviewed": 0, "ghost-eligible": 0, ghosted: 0, interviewed: 0, "offers-interviewed": 0 }),
      CTX
    );
    for (const id of ["application-rate", "screen-out-rate", "interview-rate", "ghost-rate", "offer-rate-interviews"]) {
      const r = rate(view, id);
      expect(r.state).toBe("no_data");
      expect(r.value).toBeNull();
      expect(r.display).toBe("No data");
      expect(r.of).toBe("0 of 0");
    }
    expect(JSON.stringify(view)).not.toMatch(/NaN|Infinity/);
  });

  it("marks a rate unavailable when a count request failed, instead of guessing", () => {
    const view = buildInsightsView(counts({ "ghost-eligible": null }), CTX);
    expect(rate(view, "ghost-rate")).toMatchObject({ state: "unavailable", value: null, display: "Unavailable" });
    expect(rate(view, "application-rate").state).toBe("ok");
  });

  it("never lets the application rate exceed 100%: inconsistent counts become unavailable", () => {
    const view = buildInsightsView(counts({ reviewed: 20, "applied-to": 25 }), CTX);
    expect(rate(view, "application-rate").state).toBe("unavailable");
  });

  it("the all-time interview rate uses all-time counts and window=all links whatever the active window", () => {
    const view = buildInsightsView(counts(), CTX);
    const allTime = rate(view, "interview-rate-all-time");
    expect(allTime).toMatchObject({ numerator: 20, denominator: 55, of: "20 of 55", allTime: true });
    expect(allTime.numeratorHref).toBe("/applications?lens=interviewed&window=all&asOf=2026-09-30");
    expect(allTime.denominatorHref).toBe("/applications?lens=applied&window=all&asOf=2026-09-30");
  });

  it("reuses the active counts for the all-time interview rate when the window is already all time", () => {
    const view = buildInsightsView(counts({ interviewed: 18, applied: 50 }, { allTime: undefined }), ALL);
    expect(rate(view, "interview-rate-all-time")).toMatchObject({ numerator: 18, denominator: 50 });
  });

  it("reports the open count and the rejection split with drill-down links", () => {
    const view = buildInsightsView(counts(), CTX);
    expect(view.open).toMatchObject({ count: 22, of: 40, href: "/applications?lens=open&window=90d&asOf=2026-09-30" });
    expect(view.rejectionSplit).toMatchObject({
      before: { count: 7, href: "/applications?lens=rejected-before-interview&window=90d&asOf=2026-09-30" },
      after: { count: 5, href: "/applications?lens=rejected-after-interview&window=90d&asOf=2026-09-30" },
    });
    expect(view.rejectionSplit.before.count! + view.rejectionSplit.after.count!).toBe(rate(view, "rejection-rate").numerator);
  });
});

describe("insights funnels: two cohorts, each nests", () => {
  it("builds Funnel A (postings first seen) and Funnel B (applications applied) with the documented stages", () => {
    const view = buildInsightsView(counts(), CTX);
    expect(view.funnels.map((f) => f.id)).toEqual(["postings", "applications"]);

    const [a, b] = view.funnels;
    expect(a.title).toBe("Postings first seen in window");
    expect(a.stages.map((s) => [s.key, s.value])).toEqual([["leads", 120], ["reviewed", 60], ["applied-to", 30]]);
    expect(b.title).toBe("Applications applied in window");
    expect(b.stages.map((s) => [s.key, s.value])).toEqual([
      ["applied", 40],
      ["responded", 24],
      ["interviewed", 16],
      ["offers-interviewed", 4],
    ]);
    expect(a.error).toBeUndefined();
    expect(b.error).toBeUndefined();
  });

  it("states the date basis of each funnel", () => {
    const [a, b] = buildInsightsView(counts(), CTX).funnels;
    expect(a.basis).toMatch(/first recorded/i);
    expect(b.basis).toMatch(/application date/i);
  });

  it("gives every stage a drill-down link and the previous stage as its denominator", () => {
    const [a, b] = buildInsightsView(counts(), CTX).funnels;
    expect(a.stages[1]).toMatchObject({ href: "/postings?lens=reviewed&window=90d&asOf=2026-09-30", denominator: 120 });
    expect(a.stages[0].denominator).toBeUndefined();
    expect(b.stages[3]).toMatchObject({ href: "/applications?lens=offers-interviewed&window=90d&asOf=2026-09-30", denominator: 16 });
  });

  it("each stage is at most its predecessor in both funnels; a violation is an error state, never an inverted bar", () => {
    const bad = buildInsightsView(counts({ interviewed: 30 }), CTX);
    expect(bad.funnels[1].error).toMatch(/interviewed.*responded|responded.*interviewed/i);
    expect(bad.funnels[1].stages).toEqual([]);
    expect(bad.funnels[0].error).toBeUndefined();

    const badA = buildInsightsView(counts({ "applied-to": 99 }), CTX);
    expect(badA.funnels[0].error).toBeDefined();
    expect(badA.funnels[0].stages).toEqual([]);
  });

  it("equal adjacent stages are allowed", () => {
    const view = buildInsightsView(counts({ responded: 40, interviewed: 40 }), CTX);
    expect(view.funnels[1].error).toBeUndefined();
  });

  it("the offers-without-interview note is offers minus offers-interviewed and links to its own lens, whose list holds exactly those rows", () => {
    const view = buildInsightsView(counts({ offers: 7, "offers-interviewed": 4 }), CTX);
    expect(view.offersWithoutInterview).toMatchObject({ count: 3, href: "/applications?lens=offers-without-interview&window=90d&asOf=2026-09-30" });
    expect(buildInsightsView(counts({ offers: 4, "offers-interviewed": 4 }), CTX).offersWithoutInterview?.count).toBe(0);
    expect(buildInsightsView(counts({ offers: null }), CTX).offersWithoutInterview?.count).toBeNull();
  });
});

describe("insights backfill coverage gate", () => {
  it("passes when every windowed row carries the flags", () => {
    const view = buildInsightsView(counts(), CTX);
    expect(view.backfill).toMatchObject({ postings: "ok", applications: "ok" });
    expect(view.rates.every((r) => r.state !== "needs_backfill")).toBe(true);
  });

  it("shows 'needs backfill' instead of numbers that depend on the posting flag, and keeps the rest", () => {
    const view = buildInsightsView(counts({}, { coverage: { "coverage-postings": 90, "coverage-applications": 40 } }), CTX);

    expect(view.backfill.postings).toBe("needed");
    for (const id of ["application-rate", "screen-out-rate"]) {
      expect(rate(view, id)).toMatchObject({ state: "needs_backfill", value: null, display: "Needs backfill" });
    }
    const [a, b] = view.funnels;
    expect(a.stages.find((s) => s.key === "leads")?.state).toBe("ok");
    expect(a.stages.find((s) => s.key === "reviewed")?.state).toBe("needs_backfill");
    expect(a.stages.find((s) => s.key === "applied-to")?.state).toBe("needs_backfill");
    expect(a.error).toBeUndefined();
    expect(b.stages.every((s) => s.state === "ok")).toBe(true);
    expect(rate(view, "ghost-rate").state).toBe("ok");
  });

  it("shows 'needs backfill' for numbers that depend on the application flags, and keeps the rest", () => {
    const view = buildInsightsView(counts({}, { coverage: { "coverage-postings": 120, "coverage-applications": 31 } }), CTX);

    expect(view.backfill).toMatchObject({ postings: "ok", applications: "needed" });
    for (const id of ["rejection-rate", "offer-rate-interviews", "offer-rate-applications"]) {
      expect(rate(view, id).state).toBe("needs_backfill");
    }
    expect(view.rejectionSplit.before.state).toBe("needs_backfill");
    expect(view.funnels[1].stages.find((s) => s.key === "offers-interviewed")?.state).toBe("needs_backfill");
    expect(view.funnels[1].stages.find((s) => s.key === "interviewed")?.state).toBe("ok");
    expect(view.offersWithoutInterview?.state).toBe("needs_backfill");
    expect(rate(view, "application-rate").state).toBe("ok");
    expect(rate(view, "interview-rate").state).toBe("ok");
    expect(view.open.count).toBe(22);
  });

  it("names the command to run and never prints a number for a gated item", () => {
    const view = buildInsightsView(counts({}, { coverage: { "coverage-postings": 1, "coverage-applications": 1 } }), CTX);
    expect(view.backfill.command).toBe(BACKFILL_COMMAND);
    expect(BACKFILL_COMMAND).toContain("reconcile-read-models");
    expect(rate(view, "application-rate").of).toBe("");
    expect(rate(view, "application-rate").value).toBeNull();
  });

  it("treats an unknown coverage check as unavailable rather than assuming the data is complete", () => {
    const view = buildInsightsView(counts({}, { coverage: { "coverage-postings": null, "coverage-applications": 40 } }), CTX);
    expect(view.backfill.postings).toBe("unknown");
    expect(rate(view, "application-rate").state).toBe("unavailable");
  });

  it("an empty cohort has nothing to backfill: zero flagged of zero rows is ok", () => {
    const view = buildInsightsView(
      counts({ leads: 0, reviewed: 0, "applied-to": 0, "screened-out": 0, expired: 0 }, { coverage: { "coverage-postings": 0, "coverage-applications": 40 } }),
      CTX
    );
    expect(view.backfill.postings).toBe("ok");
  });
});
