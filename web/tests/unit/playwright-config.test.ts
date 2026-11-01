import { describe, it, expect } from "vitest";
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
