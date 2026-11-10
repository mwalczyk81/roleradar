import { describe, it, expect } from "vitest";
import config from "../../playwright.config.js";
import { assertLoopbackStub, assertNotRealStackProject } from "../e2e/support/isolation.js";

describe("Insights e2e isolation guard (T050): fictional loopback stub only, never the real stack or a real database", () => {
  const STUB_PORT = 43210;

  it("accepts the loopback stub's own address", () => {
    expect(() => assertLoopbackStub(`http://127.0.0.1:${STUB_PORT}`, STUB_PORT)).not.toThrow();
    expect(() => assertLoopbackStub(`http://localhost:${STUB_PORT}`, STUB_PORT)).not.toThrow();
    expect(() => assertLoopbackStub(`http://[::1]:${STUB_PORT}`, STUB_PORT)).not.toThrow();
  });

  it("fails when the API base url is missing, so a forgotten setting can never fall back to the default stack", () => {
    expect(() => assertLoopbackStub(undefined, STUB_PORT)).toThrow(/ROLERADAR_API_BASE_URL/);
    expect(() => assertLoopbackStub("", STUB_PORT)).toThrow();
  });

  it("fails for the default API address, the compose hostnames and any non-loopback host", () => {
    for (const base of [
      "https://localhost:5002", // the developer's default API port, not this stub's
      "https://api:5002",
      "http://api.internal.example:5000",
      "http://10.0.0.5:43210",
      "http://192.168.1.20:43210",
      "https://example.com",
      "http://127.0.0.1.evil.example:43210",
      "http://localhost.evil.example:43210",
    ]) {
      expect(() => assertLoopbackStub(base, STUB_PORT), base).toThrow(/loopback|stub/i);
    }
  });

  it("fails when the loopback port is not the stub's port, even on 127.0.0.1", () => {
    expect(() => assertLoopbackStub("http://127.0.0.1:5002", STUB_PORT)).toThrow(/stub/i);
    expect(() => assertLoopbackStub("http://127.0.0.1", STUB_PORT)).toThrow(/stub/i);
  });

  it("fails for a malformed url", () => {
    expect(() => assertLoopbackStub("not a url", STUB_PORT)).toThrow();
  });

  it("fails when the spec runs in the real-stack project", () => {
    expect(() => assertNotRealStackProject("real-stack")).toThrow(/real-stack/);
    expect(() => assertNotRealStackProject("chromium")).not.toThrow();
  });

  it("the real-stack project runs only the real-stack specs, and the chromium project ignores them", () => {
    const projects = config.projects ?? [];
    const realStack = projects.find((p) => p.name === "real-stack");
    const chromium = projects.find((p) => p.name === "chromium");

    // Feature 013 added operability.real-stack.spec.ts; the real-stack project still matches only *real-stack.spec.ts.
    expect(realStack?.testMatch).toEqual(["**/real-stack.spec.ts", "**/*.real-stack.spec.ts"]);
    expect(chromium?.testIgnore).toEqual(["**/real-stack.spec.ts", "**/*.real-stack.spec.ts"]);
  });

  it("no Insights spec could be picked up by the real-stack project", () => {
    const projects = config.projects ?? [];
    const realStack = projects.find((p) => p.name === "real-stack");
    const patterns = realStack?.testMatch;
    expect(patterns).toEqual(["**/real-stack.spec.ts", "**/*.real-stack.spec.ts"]);
    for (const spec of ["insights.spec.ts", "insights-drilldown.spec.ts", "insights-isolation.spec.ts"]) {
      expect(spec.endsWith("real-stack.spec.ts")).toBe(false);
    }
  });
});
