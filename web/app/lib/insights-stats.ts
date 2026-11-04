/**
 * The Insights stats read (GET /api/v1/insights/stats) as the page uses it: the typed window mapping and a pure view
 * built from the generated DTO. Field names and the window union come from the generated client types; nothing here
 * invents a value. Dates are written as UTC YYYY-MM-DD by the shared `formatDate`, never with a locale.
 */
import type { components } from "../../../contracts/client/api-types.js";
import { formatDate } from "./format-date.js";
import type { LensContext } from "./insights-lenses.js";

export type InsightsStatsDto = components["schemas"]["InsightsStatsDto"];
export type StatsWindow = InsightsStatsDto["window"];

/** The API caps the run series at the latest 120 runs and says so with `truncated`. */
export const MAX_RUNS_SHOWN = 120;

export type MedianState = "ok" | "no_data" | "unavailable";

export interface MedianView {
  state: MedianState;
  /** The headline: "10 days", "No data" or "Unavailable". */
  value: string;
  /** What it is based on: "14 responses". */
  sampleText: string;
  days: number | null;
  sampleSize: number;
}

export interface WeeklyItem {
  label: string;
  fullLabel: string;
  value: number;
}

export interface RunRow {
  date: string;
  newPostings: number;
}

export interface StatsView {
  state: "ok" | "unavailable";
  median: MedianView;
  weekly: { items: WeeklyItem[]; total: number; empty: boolean };
  runs: { values: number[]; rows: RunRow[]; truncated: boolean; note: string | null; empty: boolean };
}

export function statsWindow(ctx: LensContext): StatsWindow {
  return ctx.window === "all" ? "allTime" : "last90Days";
}

export function formatMedianDays(days: number): string {
  return `${days} ${days === 1 ? "day" : "days"}`;
}

function responsesText(count: number): string {
  return `${count} ${count === 1 ? "response" : "responses"}`;
}

export const UNAVAILABLE_STATS: StatsView = {
  state: "unavailable",
  median: { state: "unavailable", value: "Unavailable", sampleText: "The statistics could not be loaded", days: null, sampleSize: 0 },
  weekly: { items: [], total: 0, empty: true },
  runs: { values: [], rows: [], truncated: false, note: null, empty: true },
};

export function buildStatsView(dto: InsightsStatsDto | null): StatsView {
  if (dto === null) {
    return UNAVAILABLE_STATS;
  }

  const { medianDays, sampleSize } = dto.responseTime;
  const median: MedianView =
    medianDays === null || medianDays === undefined
      ? { state: "no_data", value: "No data", sampleText: "No responses in this window", days: null, sampleSize }
      : { state: "ok", value: formatMedianDays(medianDays), sampleText: responsesText(sampleSize), days: medianDays, sampleSize };

  const items = dto.weeklyApplications.map((week) => ({
    label: formatDate(week.weekStart),
    fullLabel: `Week of ${formatDate(week.weekStart)}`,
    value: week.count,
  }));

  const rows = dto.searchRunYield.runs.map((run) => ({ date: formatDate(run.startedAt), newPostings: run.newPostings }));
  const truncated = dto.searchRunYield.truncated;

  return {
    state: "ok",
    median,
    weekly: { items, total: items.reduce((sum, item) => sum + item.value, 0), empty: items.length === 0 },
    runs: {
      values: rows.map((row) => row.newPostings),
      rows,
      truncated,
      note: truncated ? `Showing latest ${MAX_RUNS_SHOWN} runs` : null,
      empty: rows.length === 0,
    },
  };
}
