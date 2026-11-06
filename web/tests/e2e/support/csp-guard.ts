import { test as base, expect } from "@playwright/test";

/**
 * 009 CSP guard. Import `test` and `expect` from here instead of "@playwright/test" and every test fails if any page
 * in its context reports a Content Security Policy violation, on any route, in any theme.
 *
 * Two signals are collected, because Chromium reports them differently:
 *  - the `securitypolicyviolation` DOM event (installed before any page script runs), and
 *  - console messages that mention the policy ("Content Security Policy" or "violates the following").
 *
 * A test that needs to provoke a violation on purpose (the guard's own check) sets `cspGuardExpected` to true and
 * reads `cspViolations` itself.
 */

export interface CspGuardOptions {
  /** True when the test provokes violations on purpose; the fixture then does not fail the test. */
  cspGuardExpected: boolean;
}

export interface CspGuardFixtures {
  /** The violations seen so far in this test (live array). */
  cspViolations: string[];
}

const COLLECT_SCRIPT = `
  window.__cspViolations = window.__cspViolations || [];
  document.addEventListener("securitypolicyviolation", function (e) {
    window.__cspViolations.push(e.violatedDirective + " blocked " + (e.blockedURI || "inline") + " at " + e.documentURI);
  });
`;

export const test = base.extend<CspGuardFixtures & CspGuardOptions>({
  cspGuardExpected: [false, { option: true }],

  cspViolations: [
    async ({ context, cspGuardExpected }, use) => {
      const violations: string[] = [];
      const pages = new Set<import("@playwright/test").Page>();

      const watch = (page: import("@playwright/test").Page) => {
        pages.add(page);
        page.on("console", (msg) => {
          const text = msg.text();
          const lower = text.toLowerCase();
          if (lower.includes("content security policy") || lower.includes("violates the following")) {
            violations.push(`console: ${text}`);
          }
        });
      };

      await context.addInitScript(COLLECT_SCRIPT);
      for (const page of context.pages()) watch(page);
      context.on("page", watch);

      await use(violations);

      for (const page of pages) {
        if (page.isClosed()) continue;
        try {
          const fromDom = await page.evaluate(
            () => (window as unknown as { __cspViolations?: string[] }).__cspViolations ?? [],
          );
          for (const v of fromDom) violations.push(`event: ${v}`);
        } catch {
          // The page navigated away or is not scriptable (about:blank, a download): nothing to collect.
        }
      }

      if (!cspGuardExpected) {
        expect(violations, `Content Security Policy violations:\n${violations.join("\n")}`).toEqual([]);
      }
    },
    { auto: true },
  ],
});

export { expect };
