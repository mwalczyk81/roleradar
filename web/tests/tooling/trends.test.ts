import { describe, it, expect } from "vitest";
import fs from "node:fs";
import path from "node:path";
import { buildSeries, parseMetricsText, renderTrends } from "../../../pages/metrics/trends.js";

// Feature 011, story 2 (FR-017, FR-018). The trends page reads metrics/metrics.jsonl from gh-pages. Every field is
// untrusted text: lines that fail to parse are skipped and counted visibly, values render only as text nodes, and the
// page loads no third-party script. All data here is fictional.

const SHA_A = "a".repeat(40);
const SHA_B = "b".repeat(40);
const SHA_C = "c".repeat(40);

function record(overrides: Record<string, unknown> = {}): string {
  return JSON.stringify({
    schemaVersion: 1,
    commit: SHA_A,
    timestampUtc: "2026-01-01T00:00:00Z",
    tests: { unit: { passed: 10, failed: 0, skipped: 1 }, contract: { passed: 5, failed: 0, skipped: 0 } },
    coverage: { overallLinePct: 90.5, projects: { "RoleRadar.Domain": 95 } },
    jobDurationsSeconds: { "ubuntu-build-and-test": 600, web: 300 },
    ...overrides,
  });
}

function mount(): HTMLElement {
  document.body.innerHTML = "";
  const root = document.createElement("div");
  root.id = "trends-root";
  document.body.append(root);
  return root;
}

describe("parseMetricsText", () => {
  it("reads valid lines and ignores blank lines", () => {
    const text = [record(), "", record({ commit: SHA_B, timestampUtc: "2026-01-02T00:00:00Z" }), ""].join("\n");

    const parsed = parseMetricsText(text);

    expect(parsed.records).toHaveLength(2);
    expect(parsed.skipped).toBe(0);
    expect(parsed.records.map((r) => r.commit)).toEqual([SHA_A, SHA_B]);
  });

  it("skips and counts malformed JSON, wrong schema versions, bad commits and bad timestamps", () => {
    const text = [
      record(),
      "{ not json",
      record({ schemaVersion: 2, commit: SHA_B }),
      record({ commit: "not-a-sha" }),
      record({ commit: SHA_C, timestampUtc: "yesterday" }),
      "[1,2,3]",
      "null",
    ].join("\n");

    const parsed = parseMetricsText(text);

    expect(parsed.records).toHaveLength(1);
    expect(parsed.skipped).toBe(6);
  });

  it("orders records by timestamp, oldest first", () => {
    const text = [
      record({ commit: SHA_B, timestampUtc: "2026-01-03T00:00:00Z" }),
      record({ commit: SHA_A, timestampUtc: "2026-01-01T00:00:00Z" }),
    ].join("\n");

    expect(parseMetricsText(text).records.map((r) => r.commit)).toEqual([SHA_A, SHA_B]);
  });

  it("drops values that are not finite numbers instead of treating them as zero", () => {
    const text = record({
      tests: { unit: { passed: "10", failed: 0, skipped: 0 }, contract: { passed: 5, failed: 0, skipped: 0 } },
      coverage: { overallLinePct: "ninety" },
    });

    const parsed = parseMetricsText(text);

    expect(parsed.records).toHaveLength(1);
    expect(parsed.records[0]?.testsPassed).toBe(5);
    expect(parsed.records[0]?.coverageLinePct).toBeNull();
  });
});

describe("buildSeries", () => {
  it("builds tests, coverage and mutation series, and the mutation point keeps its own provenance", () => {
    const parsed = parseMetricsText(
      [
        record(),
        record({
          commit: SHA_B,
          timestampUtc: "2026-01-02T00:00:00Z",
          mutation: { "web-lib": 81.2, runDate: "2025-12-31T02:00:00Z", commit: SHA_C },
        }),
      ].join("\n"),
    );

    const series = buildSeries(parsed.records);

    expect(series.map((s) => s.key)).toEqual(["tests", "coverage", "mutation"]);
    expect(series[0]?.points.map((p) => p.value)).toEqual([15, 15]);
    expect(series[1]?.points.map((p) => p.value)).toEqual([90.5, 90.5]);
    expect(series[2]?.points).toHaveLength(1);
    expect(series[2]?.points[0]).toMatchObject({ value: 81.2, note: `nightly run 2025-12-31T02:00:00Z, commit ${SHA_C.slice(0, 7)}` });
  });

  it("leaves a series out when no record carries it", () => {
    const series = buildSeries(parseMetricsText(record()).records);

    expect(series.map((s) => s.key)).toEqual(["tests", "coverage"]);
  });
});

describe("renderTrends", () => {
  it("shows an empty state, and a visible skipped count of zero, for an empty file", () => {
    const root = mount();

    renderTrends(document, root, parseMetricsText(""));

    expect(root.textContent).toContain("No metrics recorded yet");
    expect(root.textContent).toContain("Skipped lines: 0");
  });

  it("shows the skipped count when lines were unreadable", () => {
    const root = mount();

    renderTrends(document, root, parseMetricsText([record(), "garbage", "{"].join("\n")));

    expect(root.textContent).toContain("Skipped lines: 2");
  });

  it("draws one sparkline per series with one point per record", () => {
    const root = mount();
    const text = [
      record(),
      record({ commit: SHA_B, timestampUtc: "2026-01-02T00:00:00Z", coverage: { overallLinePct: 91 } }),
      record({ commit: SHA_C, timestampUtc: "2026-01-03T00:00:00Z", coverage: { overallLinePct: 92 } }),
    ].join("\n");

    renderTrends(document, root, parseMetricsText(text));

    const lines = root.querySelectorAll("svg polyline");
    expect(lines.length).toBe(2);
    expect(lines[1]?.getAttribute("points")?.trim().split(" ")).toHaveLength(3);
  });

  it("renders hostile job names and notes as text, never as elements", () => {
    const root = mount();
    const hostile = '<img src=x onerror="globalThis.__pwned = true">';
    const text = record({ jobDurationsSeconds: { [hostile]: 12, "<script>globalThis.__pwned = true</script>": 3 } });

    renderTrends(document, root, parseMetricsText(text));

    expect(root.querySelector("img")).toBeNull();
    expect(root.querySelector("script")).toBeNull();
    expect(root.textContent).toContain(hostile);
    expect((globalThis as { __pwned?: boolean }).__pwned).toBeUndefined();
  });

  it("rerendering replaces the previous content instead of appending", () => {
    const root = mount();

    renderTrends(document, root, parseMetricsText(record()));
    renderTrends(document, root, parseMetricsText(record()));

    expect(root.querySelectorAll("h2").length).toBe(root.querySelectorAll("section").length);
    // Default record: tests, coverage and the job-durations table.
    expect(root.querySelectorAll("[data-series]").length).toBe(3);
  });
});

describe("static safety of the page source", () => {
  const pageDir = path.resolve(process.cwd(), "..", "pages", "metrics");

  it("trends.js never builds markup from strings or evaluates code", () => {
    const source = fs.readFileSync(path.join(pageDir, "trends.js"), "utf8");
    for (const forbidden of ["innerHTML", "outerHTML", "insertAdjacentHTML", "document.write", "eval(", "new Function"]) {
      expect(source.includes(forbidden), forbidden).toBe(false);
    }
  });

  it("index.html has a strict CSP, no inline script or style, and no third-party origin", () => {
    const html = fs.readFileSync(path.join(pageDir, "index.html"), "utf8");
    expect(html).toMatch(/<meta http-equiv="Content-Security-Policy"/);
    expect(html).toContain("script-src 'self'");
    expect(html).toContain("default-src 'none'");
    expect(html).not.toMatch(/<script(?![^>]*\bsrc=)[^>]*>/i);
    expect(html).not.toMatch(/<style[\s>]/i);
    expect(html).not.toMatch(/\sstyle=/i);
    expect(html).not.toMatch(/(?:src|href)="https?:\/\//i);
  });
});
