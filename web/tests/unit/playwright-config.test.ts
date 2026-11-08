import { describe, it, expect } from "vitest";
import fs from "node:fs";
import path from "node:path";
import { parse } from "yaml";
import playwrightConfig, { getRealStackHostResolverRules } from "../../playwright.config.js";

describe("playwright.config isolation invariants", () => {
  it("real-stack project exists and is distinct from stubbed chromium project", () => {
    const projects = playwrightConfig.projects ?? [];
    const chromiumProject = projects.find((p) => p.name === "chromium");
    const realStackProject = projects.find((p) => p.name === "real-stack");

    expect(chromiumProject).toBeDefined();
    expect(realStackProject).toBeDefined();
    expect(realStackProject?.name).not.toBe(chromiumProject?.name);
  });

  it("real-stack project launch args never map identity to default host port 5001", () => {
    const projects = playwrightConfig.projects ?? [];
    const realStackProject = projects.find((p) => p.name === "real-stack");
    expect(realStackProject).toBeDefined();

    const args = realStackProject?.use?.launchOptions?.args ?? [];
    const resolverRuleArg = args.find((a) => a.startsWith("--host-resolver-rules="));
    expect(resolverRuleArg).toBeDefined();

    // Invariant: launch args must NEVER map identity to 5001 or 127.0.0.1 without a non-5001 port
    expect(resolverRuleArg).not.toContain("127.0.0.1:5001");
    expect(resolverRuleArg).not.toMatch(/MAP\s+identity\s+127\.0\.0\.1(,|$)/);
  });

  it("getRealStackHostResolverRules throws if identityPort is explicitly 5001", () => {
    expect(() => getRealStackHostResolverRules("5001")).toThrow(
      /Security violation: real-stack must never map identity to default host port 5001/i
    );
  });

  it("getRealStackHostResolverRules correctly maps identity and api to specified ephemeral ports", () => {
    const rule = getRealStackHostResolverRules("54321", "54322");
    expect(rule).toContain("MAP identity:5001 127.0.0.1:54321");
    expect(rule).toContain("MAP identity 127.0.0.1:54321");
    expect(rule).toContain("MAP api:5002 127.0.0.1:54322");
    expect(rule).toContain("MAP api 127.0.0.1:54322");
  });

  it("getRealStackHostResolverRules maps identity to 127.0.0.1:0 when identityPort is unset", () => {
    const rule = getRealStackHostResolverRules(undefined);
    expect(rule).not.toContain("127.0.0.1:5001");
    expect(rule).toContain("MAP identity:5001 127.0.0.1:0");
    expect(rule).toContain("MAP identity 127.0.0.1:0");
  });
});

// Feature 011, story 2 (FR-021, FR-022). Failure evidence is captured only when a test fails, uploaded only when a job
// fails, and kept for a short, stated time. Traces from the real-stack job can show real-looking sessions, so that job
// keeps them 7 days and nothing from it ever reaches the published site.
describe("Playwright failure artifacts", () => {
  type Step = { name?: string; uses?: string; if?: string; with?: { name?: string; path?: string; "retention-days"?: number } };
  type Workflow = { jobs: Record<string, { steps?: Step[] }> };

  function loadCi(): Workflow {
    const text = fs.readFileSync(path.resolve(process.cwd(), "..", ".github", "workflows", "ci.yml"), "utf8");
    return parse(text) as Workflow;
  }

  function uploadSteps(job: string): Step[] {
    return (loadCi().jobs[job]?.steps ?? []).filter((step) => typeof step.uses === "string" && step.uses.startsWith("actions/upload-artifact@"));
  }

  it("keeps screenshots on failure only, traces on first retry, and no video", () => {
    const use = playwrightConfig.use ?? {};
    expect(use.screenshot).toBe("only-on-failure");
    expect(use.trace).toBe("on-first-retry");
    expect(use.video === undefined || use.video === "off").toBe(true);
  });

  it("writes an HTML report in CI so a failure has something to open", () => {
    const source = fs.readFileSync(path.resolve(process.cwd(), "playwright.config.ts"), "utf8");
    expect(source).toMatch(/process\.env\.CI[^;]*"html"/s);
  });

  it("uploads the web e2e failure artifacts only on failure, for 14 days", () => {
    const steps = uploadSteps("web").filter((step) => (step.with?.path ?? "").includes("web/test-results"));
    expect(steps).toHaveLength(1);
    expect(steps[0]?.if).toBe("failure()");
    expect(steps[0]?.with?.["retention-days"]).toBe(14);
    expect(steps[0]?.with?.path).toContain("web/playwright-report");
  });

  it("uploads the real-stack failure artifacts only on failure, for 7 days", () => {
    const steps = uploadSteps("e2e-real-stack").filter((step) => (step.with?.path ?? "").includes("web/test-results"));
    expect(steps).toHaveLength(1);
    expect(steps[0]?.if).toBe("failure()");
    expect(steps[0]?.with?.["retention-days"]).toBe(7);
  });

  it("never lets the publish job download an end-to-end or real-stack artifact", () => {
    const steps = loadCi().jobs["metrics-and-pages"]?.steps ?? [];
    const downloaded = steps
      .filter((step) => typeof step.uses === "string" && step.uses.startsWith("actions/download-artifact@"))
      .map((step) => step.with?.name);
    expect(downloaded.length).toBeGreaterThan(0);
    for (const name of downloaded) {
      expect(String(name), String(name)).not.toMatch(/e2e|playwright|real-stack|test-results/i);
    }
  });

  it("explains the retention choices in the workflow itself", () => {
    const text = fs.readFileSync(path.resolve(process.cwd(), "..", ".github", "workflows", "ci.yml"), "utf8");
    // Both retention values appear next to a comment that says why (14 days fictional, 7 days real-stack).
    expect(text).toContain("retention-days: 14");
    expect(text).toContain("retention-days: 7");
    expect(text).toContain("14 days");
    expect(text).toContain("7 days");
  });
});
