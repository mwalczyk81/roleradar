import { describe, it, expect } from "vitest";
import React from "react";
import { render, within } from "@testing-library/react";
import { createRoutesStub } from "react-router";
import Insights from "../../app/routes/insights.js";
import { buildInsightsView, type InsightsCounts } from "../../app/lib/insights-view.js";
import { buildStatsView } from "../../app/lib/insights-stats.js";
import type { LensContext, LensKey } from "../../app/lib/insights-lenses.js";

const CTX: LensContext = { window: "90d", asOf: "2026-09-30" };

function counts(overrides: Partial<Record<LensKey, number | null>> = {}, extra: Partial<InsightsCounts> = {}): InsightsCounts {
  return {
    lens: {
      leads: 120,
      reviewed: 60,
      "applied-to": 30,
      "screened-out": 15,
      expired: 7,
      applied: 40,
      responded: 24,
      interviewed: 16,
      offers: 7,
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
    },
    coverage: { "coverage-postings": 120, "coverage-applications": 40 },
    allTime: { interviewed: 20, applied: 55 },
    ...extra,
  };
}

// The trend section has its own tests (insights-trends.test.tsx); here it just has to be healthy.
const HEALTHY_STATS = buildStatsView({
  asOf: "2026-09-30",
  window: "last90Days",
  windowStart: "2026-07-03",
  responseTime: { medianDays: 10, sampleSize: 14 },
  // No dated rows: the as-of date stays the only date this page shows besides the trend labels, which the trend tests cover.
  weeklyApplications: [],
  searchRunYield: { runs: [], truncated: false },
});

function renderPage(view: ReturnType<typeof buildInsightsView>) {
  const Stub = createRoutesStub([
    {
      path: "/insights",
      Component: Insights,
      loader: () => ({ view, stats: HEALTHY_STATS, csrfToken: "dummy-csrf-token", userLabel: "fictional@example.com" }),
    },
  ]);
  return render(React.createElement(Stub, { initialEntries: ["/insights"] }));
}

describe("Insights page (T045)", () => {
  it("shows the page title and one titled section per cohort", async () => {
    const { findByRole } = renderPage(buildInsightsView(counts(), CTX));

    expect(await findByRole("heading", { level: 1, name: "Insights" })).toBeDefined();
    expect(await findByRole("heading", { level: 2, name: "Postings first seen in window" })).toBeDefined();
    expect(await findByRole("heading", { level: 2, name: "Applications applied in window" })).toBeDefined();
    expect(await findByRole("heading", { level: 2, name: "Rates" })).toBeDefined();
  });

  it("offers a 90 day and an all-time window as links that keep asOf", async () => {
    const { findByRole } = renderPage(buildInsightsView(counts(), CTX));

    const last90 = await findByRole("link", { name: "Last 90 days" });
    const allTime = await findByRole("link", { name: "All time" });
    expect(last90.getAttribute("href")).toBe("/insights?window=90d&asOf=2026-09-30");
    expect(allTime.getAttribute("href")).toBe("/insights?window=all&asOf=2026-09-30");
    expect(last90.getAttribute("aria-current")).toBe("page");
    expect(allTime.getAttribute("aria-current")).toBeNull();
  });

  it("links every funnel stage to its list with the same lens, window and asOf", async () => {
    const { findByRole } = renderPage(buildInsightsView(counts(), CTX));

    const leads = await findByRole("link", { name: "Leads: 120" });
    expect(leads.getAttribute("href")).toBe("/postings?lens=leads&window=90d&asOf=2026-09-30");
    expect((await findByRole("link", { name: "Reviewed: 60" })).getAttribute("href")).toBe("/postings?lens=reviewed&window=90d&asOf=2026-09-30");
    expect((await findByRole("link", { name: "Applied to: 30" })).getAttribute("href")).toBe("/postings?lens=applied-to&window=90d&asOf=2026-09-30");
    expect((await findByRole("link", { name: "Responded: 24" })).getAttribute("href")).toBe("/applications?lens=responded&window=90d&asOf=2026-09-30");
    expect((await findByRole("link", { name: "Interviewed: 16" })).getAttribute("href")).toBe("/applications?lens=interviewed&window=90d&asOf=2026-09-30");
    expect((await findByRole("link", { name: "Offers: 4" })).getAttribute("href")).toBe("/applications?lens=offers-interviewed&window=90d&asOf=2026-09-30");
  });

  it("shows offers logged without an interview as a note with its own link", async () => {
    const { findByRole } = renderPage(buildInsightsView(counts(), CTX));

    const note = await findByRole("link", { name: /3 offers had no interview event/i });
    expect(note.getAttribute("href")).toBe("/applications?lens=offers-without-interview&window=90d&asOf=2026-09-30");
  });

  it("gives each funnel a table view", async () => {
    const { findAllByRole } = renderPage(buildInsightsView(counts(), CTX));

    const buttons = await findAllByRole("button", { name: /table view/i });
    expect(buttons.length).toBeGreaterThanOrEqual(2);
  });

  it("shows every rate as 'x of y' with numerator and denominator links, and flags a small sample", async () => {
    const view = buildInsightsView(counts({ ghosted: 2, "ghost-eligible": 8 }), CTX);
    const { findByText, container } = renderPage(view);

    expect(await findByText("30 of 60")).toBeDefined();
    expect(await findByText("15 of 36")).toBeDefined();
    expect(await findByText("20 of 55")).toBeDefined();
    expect(await findByText("2 of 8")).toBeDefined();
    expect(container.textContent).toMatch(/small sample/i);

    const numerator = container.querySelector('a[data-lens="applied-to"][data-role="numerator"]');
    const denominator = container.querySelector('a[data-lens="reviewed"][data-role="denominator"]');
    expect(numerator?.getAttribute("href")).toBe("/postings?lens=applied-to&window=90d&asOf=2026-09-30");
    expect(denominator?.getAttribute("href")).toBe("/postings?lens=reviewed&window=90d&asOf=2026-09-30");
  });

  it("links the all-time interview rate with window=all even in the 90 day view", async () => {
    const { container, findByText } = renderPage(buildInsightsView(counts(), CTX));
    await findByText("20 of 55");

    const numerator = container.querySelector('a[data-lens="interviewed"][data-role="numerator"][data-window="all"]');
    expect(numerator?.getAttribute("href")).toBe("/applications?lens=interviewed&window=all&asOf=2026-09-30");
  });

  it("shows 'No data' with '0 of 0' for an empty cohort", async () => {
    const view = buildInsightsView(counts({ mature: 0, "mature-interviewed": 0 }), CTX);
    const { findAllByText } = renderPage(view);

    expect((await findAllByText("No data")).length).toBeGreaterThan(0);
    expect((await findAllByText("0 of 0")).length).toBeGreaterThan(0);
  });

  it("shows the rejection split and the open count with links", async () => {
    const { container, findByText } = renderPage(buildInsightsView(counts(), CTX));
    await findByText("30 of 60");

    expect(container.querySelector('a[data-lens="rejected-before-interview"]')?.getAttribute("href")).toBe(
      "/applications?lens=rejected-before-interview&window=90d&asOf=2026-09-30"
    );
    expect(container.querySelector('a[data-lens="rejected-after-interview"]')?.getAttribute("href")).toBe(
      "/applications?lens=rejected-after-interview&window=90d&asOf=2026-09-30"
    );
    expect(container.querySelector('a[data-lens="open"]')?.getAttribute("href")).toBe("/applications?lens=open&window=90d&asOf=2026-09-30");
  });

  it("explains the first-seen date, bulk imports, how reviewed is defined and why the two funnels can differ (FR-008, FR-008a)", async () => {
    const { findByText, container } = renderPage(buildInsightsView(counts(), CTX));
    await findByText("30 of 60");
    const text = container.textContent ?? "";

    expect(text).toMatch(/first recorded/i);
    expect(text).toMatch(/imported in bulk share one/i);
    expect(text).toMatch(/reviewed.*not a lead or unscored|not a lead or unscored.*reviewed/i);
    expect(text).toMatch(/returned to lead/i);
    expect(text).toMatch(/applied to.*can (legitimately )?differ|can (legitimately )?differ.*applied to/i);
  });

  it("states the one-day difference between the ghost rate denominator and the ghosted numerator (A3)", async () => {
    const { findByText, container } = renderPage(buildInsightsView(counts(), CTX));
    await findByText("30 of 60");
    const text = (container.textContent ?? "").replace(/\s+/g, " ");

    expect(text).toMatch(
      /an application exactly 30 days old is in the ghost rate denominator \(30 days or older\) but is not ghosted until it is older than 30 days/i
    );
  });

  it("never shows a real-looking date or count that did not come from the view: the as-of date is the only date", async () => {
    const { findByText, container } = renderPage(buildInsightsView(counts(), CTX));
    await findByText("30 of 60");

    const dates = (container.textContent ?? "").match(/\d{4}-\d{2}-\d{2}/g) ?? [];
    expect(new Set(dates)).toEqual(new Set(["2026-09-30"]));
  });

  it("shows 'Needs backfill' with the command instead of numbers that need the flags", async () => {
    const view = buildInsightsView(counts({}, { coverage: { "coverage-postings": 90, "coverage-applications": 31 } }), CTX);
    const { findAllByText, container } = renderPage(view);

    expect((await findAllByText("Needs backfill")).length).toBeGreaterThan(0);
    expect(container.textContent).toContain("roleradar reconcile-read-models");
    expect(container.textContent).toMatch(/dry run/i);
    // the application rate must not show a percentage or "x of y"
    expect(container.textContent).not.toContain("30 of 60");
    // numbers that do not depend on the flags are still shown
    expect(container.textContent).toContain("9 of 30");
  });

  it("labels the rejection split tile 'Needs backfill', not 'Unavailable', when the application flags are missing", async () => {
    const view = buildInsightsView(counts({}, { coverage: { "coverage-postings": 120, "coverage-applications": 31 } }), CTX);
    const { container, findAllByText } = renderPage(view);
    await findAllByText("Needs backfill");

    const tile = container.querySelector('[data-rate="rejection-split"]');
    expect(tile?.textContent).toContain("Needs backfill");
    expect(tile?.textContent).not.toContain("Unavailable");
  });

  it("shows an error instead of an inverted funnel when the counts disagree", async () => {
    const view = buildInsightsView(counts({ interviewed: 30 }), CTX);
    const { findByRole, queryByRole } = renderPage(view);

    const alert = await findByRole("alert");
    expect(alert.textContent).toMatch(/Interviewed \(30\) is higher than Responded \(24\)/);
    expect(queryByRole("link", { name: "Interviewed: 30" })).toBeNull();
  });

  it("shows 'Unavailable' for a number whose request failed, and keeps the rest", async () => {
    const view = buildInsightsView(counts({ "ghost-eligible": null }), CTX);
    const { findAllByText, findByText } = renderPage(view);

    expect((await findAllByText("Unavailable")).length).toBeGreaterThan(0);
    expect(await findByText("30 of 60")).toBeDefined();
  });

  it("keeps the main content free of unlabelled landmarks: sections are named", async () => {
    const { findByText, container } = renderPage(buildInsightsView(counts(), CTX));
    await findByText("30 of 60");

    const sections = Array.from(container.querySelectorAll("section"));
    expect(sections.length).toBeGreaterThanOrEqual(3);
    for (const section of sections) {
      expect(section.getAttribute("aria-labelledby")).toBeTruthy();
      expect(within(section).queryAllByRole("heading").length).toBeGreaterThan(0);
    }
  });
});
