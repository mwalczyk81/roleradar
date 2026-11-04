import { describe, it, expect } from "vitest";
import React from "react";
import { render, fireEvent, within } from "@testing-library/react";
import { createRoutesStub } from "react-router";
import Insights from "../../app/routes/insights.js";
import { buildInsightsView, type InsightsCounts } from "../../app/lib/insights-view.js";
import { buildStatsView, UNAVAILABLE_STATS, type InsightsStatsDto, type StatsView } from "../../app/lib/insights-stats.js";
import type { LensContext } from "../../app/lib/insights-lenses.js";

/**
 * T070 (component part): the trend section. A ColumnChart of weekly applications and a Sparkline of search-run yield,
 * each with a table view that works from the keyboard (native buttons), a median response time tile, and an honest
 * unavailable state that leaves the rest of the page alone.
 */

const CTX: LensContext = { window: "90d", asOf: "2026-09-30" };

const COUNTS: InsightsCounts = {
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
  },
  coverage: { "coverage-postings": 120, "coverage-applications": 40 },
  allTime: { interviewed: 20, applied: 55 },
};

function stats(overrides: Partial<InsightsStatsDto> = {}): StatsView {
  return buildStatsView({
    asOf: "2026-09-30",
    window: "last90Days",
    windowStart: "2026-07-03",
    responseTime: { medianDays: 10, sampleSize: 14 },
    weeklyApplications: [
      { weekStart: "2026-09-14", count: 2 },
      { weekStart: "2026-09-21", count: 0 },
      { weekStart: "2026-09-28", count: 1 },
    ],
    searchRunYield: {
      runs: [
        { startedAt: "2026-09-10T12:00:00Z", newPostings: 4 },
        { startedAt: "2026-09-20T12:00:00Z", newPostings: 9 },
      ],
      truncated: false,
    },
    ...overrides,
  });
}

function renderPage(statsView: StatsView) {
  const view = buildInsightsView(COUNTS, CTX);
  const Stub = createRoutesStub([
    {
      path: "/insights",
      Component: Insights,
      loader: () => ({ view, stats: statsView, csrfToken: "dummy-csrf-token", userLabel: "fictional@example.com" }),
    },
  ]);
  return render(React.createElement(Stub, { initialEntries: ["/insights"] }));
}

async function trendSection(statsView: StatsView) {
  const result = renderPage(statsView);
  const heading = await result.findByRole("heading", { level: 2, name: "Trends" });
  const section = heading.closest("section");
  if (!section) throw new Error("The Trends heading is not inside a section");
  return { ...result, section: within(section), sectionElement: section };
}

describe("Insights trends (T070)", () => {
  it("has a Trends section with a heading in the display face class", async () => {
    const { findByRole } = renderPage(stats());

    const heading = await findByRole("heading", { level: 2, name: "Trends" });
    expect(heading.classList.contains("page-heading")).toBe(true);
  });

  it("shows applications per week as a column chart with a table view", async () => {
    const { section } = await trendSection(stats());

    expect(section.getByText("Applications per week")).toBeDefined();
    expect(section.getByRole("group", { name: "Applications per week" })).toBeDefined();
    expect(section.getAllByRole("button", { name: /table view/i }).length).toBeGreaterThanOrEqual(1);
  });

  it("switches the weekly chart to a native table of every week, zero weeks included", async () => {
    const { section } = await trendSection(stats());
    const buttons = section.getAllByRole("button", { name: /table view/i });

    fireEvent.click(buttons[0]);

    const table = await section.findByRole("table", { name: "Applications per week" });
    const rows = within(table).getAllByRole("row").map((r) => r.textContent);
    expect(rows.join("|")).toContain("Week of 2026-09-14");
    expect(rows.join("|")).toContain("Week of 2026-09-21");
    expect(rows.join("|")).toContain("Week of 2026-09-28");
    const zeroRow = within(table).getAllByRole("row").find((r) => r.textContent?.includes("Week of 2026-09-21"));
    expect(zeroRow?.textContent).toContain("0");
  });

  it("makes the table-view controls native buttons, so they take focus and respond to Enter and Space", async () => {
    const { section } = await trendSection(stats());

    for (const button of section.getAllByRole("button", { name: /table view/i })) {
      expect(button.tagName).toBe("BUTTON");
      expect(button.getAttribute("tabindex")).not.toBe("-1");
      button.focus();
      expect(document.activeElement).toBe(button);
      expect(button.getAttribute("aria-pressed")).toBe("false");
    }
  });

  it("shows search-run yield as a sparkline named for what it is, with its own table view", async () => {
    const { section } = await trendSection(stats());

    expect(section.getByRole("img", { name: /new postings per search run/i })).toBeDefined();
    const buttons = section.getAllByRole("button", { name: /table view/i });
    expect(buttons).toHaveLength(2); // one for the column chart, one for the run yield
  });

  it("switches run yield to a native table, oldest first, and back", async () => {
    const { section } = await trendSection(stats());
    const runToggle = section.getByRole("button", { name: /table view of search runs/i });

    expect(runToggle.getAttribute("aria-pressed")).toBe("false");
    fireEvent.click(runToggle);

    expect(runToggle.getAttribute("aria-pressed")).toBe("true");
    const table = await section.findByRole("table", { name: /new postings per search run/i });
    const rows = within(table).getAllByRole("row").slice(1).map((r) => r.textContent);
    expect(rows).toEqual(["2026-09-104", "2026-09-209"]);
    expect(section.queryByRole("img", { name: /new postings per search run/i })).toBeNull();

    fireEvent.click(runToggle);
    expect(runToggle.getAttribute("aria-pressed")).toBe("false");
    expect(section.queryByRole("table", { name: /new postings per search run/i })).toBeNull();
    expect(section.getByRole("img", { name: /new postings per search run/i })).toBeDefined();
  });

  it("labels a capped run series 'showing latest 120 runs'", async () => {
    const runs = Array.from({ length: 120 }, (_, i) => ({ startedAt: "2026-09-10T12:00:00Z", newPostings: i }));
    const { section } = await trendSection(stats({ searchRunYield: { runs, truncated: true } }));

    expect(section.getByText(/showing latest 120 runs/i)).toBeDefined();
  });

  it("does not show the cap label when the series is not capped", async () => {
    const { section } = await trendSection(stats());

    expect(section.queryByText(/showing latest 120 runs/i)).toBeNull();
  });

  it("says so when there are no runs, and when all-time has no applications", async () => {
    const { section } = await trendSection(
      stats({ weeklyApplications: [], searchRunYield: { runs: [], truncated: false } })
    );

    expect(section.getByText(/no applications in this window/i)).toBeDefined();
    expect(section.getByText(/no search runs in this window/i)).toBeDefined();
  });

  it("shows the median response time tile in days with how many responses it is based on", async () => {
    const { findByText, container } = renderPage(stats());

    expect(await findByText("Median response time")).toBeDefined();
    const text = container.textContent ?? "";
    expect(text).toContain("10 days");
    expect(text).toContain("14 responses");
  });

  it("explains how the median is measured: applied to first response, whole UTC days, median not mean", async () => {
    const { findByText, container } = renderPage(stats());
    await findByText("Median response time");

    const text = (container.textContent ?? "").replace(/\s+/g, " ");
    expect(text).toMatch(/median/i);
    expect(text).toMatch(/applied.*first response/i);
    expect(text).toMatch(/follow-ups and withdrawals do not count/i);
  });

  it("shows 'No data' instead of a median when no application in the window has a response", async () => {
    const { findByText, container } = renderPage(stats({ responseTime: { medianDays: null, sampleSize: 0 } }));

    await findByText("Median response time");
    const text = container.textContent ?? "";
    expect(text).toContain("No data");
    expect(text).not.toMatch(/NaN|undefined|null/);
  });

  it("degrades only the trend and median tiles when the stats are unavailable", async () => {
    const { section, findByText, container, findByRole } = await trendSection(UNAVAILABLE_STATS);

    expect(section.getByText(/trends are unavailable/i)).toBeDefined();
    expect(section.queryByRole("button", { name: /table view/i })).toBeNull();
    await findByText("Median response time");
    expect(container.textContent).toContain("Unavailable");

    // The rest of the page is untouched.
    expect(await findByRole("heading", { level: 2, name: "Postings first seen in window" })).toBeDefined();
    expect(await findByRole("heading", { level: 2, name: "Applications applied in window" })).toBeDefined();
    expect(await findByRole("heading", { level: 2, name: "Rates" })).toBeDefined();
    expect(container.textContent).toContain("30 of 60");
  });

  it("puts no inline style attribute in the trend markup the page itself writes", async () => {
    const { sectionElement } = await trendSection(stats());

    // The kit draws its own SVG and layout; the page's wrappers and the table-view control must use classes only.
    const owned = sectionElement.querySelectorAll(".insights-trend, .insights-trend-note, .insights-run-table, .insights-run-toggle");
    expect(owned.length).toBeGreaterThan(0);
    for (const element of owned) {
      expect(element.hasAttribute("style")).toBe(false);
    }
  });
});
