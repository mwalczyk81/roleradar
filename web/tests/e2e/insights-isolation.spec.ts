import { test, expect } from "@playwright/test";
import { startInsightsHarness, type InsightsHarness } from "./support/insights-stub.js";
import { assertLoopbackStub, assertNotRealStackProject } from "./support/isolation.js";

/**
 * T050: the Insights specs run only against the loopback stub and the fictional fixture. This spec fails if the
 * real-stack project, a non-loopback API base URL, or any API traffic outside the stub is involved.
 */

let harness: InsightsHarness;

test.describe("Insights e2e isolation (fictional loopback stub only)", () => {
  test.beforeAll(async ({ browserName: _browserName }, testInfo) => {
    assertNotRealStackProject(testInfo.project.name);
    harness = await startInsightsHarness("e2e-insights-isolation-session");
  });

  test.afterAll(async () => {
    await harness?.close();
  });

  test("never runs in the real-stack project", async ({ browserName: _browserName }, testInfo) => {
    expect(testInfo.project.name).not.toBe("real-stack");
    expect(() => assertNotRealStackProject("real-stack")).toThrow(/real-stack/);
  });

  test("the web server talks to this run's loopback stub, and the guard rejects anything else", async () => {
    expect(process.env.ROLERADAR_API_BASE_URL).toBe(`http://127.0.0.1:${harness.apiPort}`);
    expect(() => assertLoopbackStub(process.env.ROLERADAR_API_BASE_URL, harness.apiPort)).not.toThrow();

    expect(() => assertLoopbackStub(undefined, harness.apiPort)).toThrow();
    expect(() => assertLoopbackStub("https://localhost:5002", harness.apiPort)).toThrow();
    expect(() => assertLoopbackStub("https://api:5002", harness.apiPort)).toThrow();
    expect(() => assertLoopbackStub("http://10.1.2.3:5002", harness.apiPort)).toThrow();
  });

  test("the page only ever reaches the stub, and only for the two OData sets and the stats read", async ({ page, context }) => {
    await harness.signIn(context);
    await page.goto(`${harness.baseUrl}/insights?window=90d&asOf=2026-09-30`, { waitUntil: "networkidle" });
    await expect(page.getByRole("heading", { level: 1, name: "Insights" })).toBeVisible();

    expect(harness.requests.length).toBeGreaterThan(0);
    for (const request of harness.requests) {
      expect(request.method).toBe("GET");
      expect(["/odata/Postings", "/odata/Applications", "/api/v1/insights/stats"]).toContain(request.pathname);
    }
    expect(harness.unexpectedFilters).toEqual([]);

    // The browser itself never talked to anything but the web server on loopback.
    const origins = new Set<string>();
    page.on("request", (r) => origins.add(new URL(r.url()).origin));
    await page.reload({ waitUntil: "networkidle" });
    for (const origin of origins) {
      expect(origin).toBe(harness.baseUrl);
    }
  });
});
