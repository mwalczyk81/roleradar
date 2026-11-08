import { defineConfig } from "vitest/config";
import baseConfig from "./vitest.config";

// Vitest configuration used only by StrykerJS (stryker.config.mjs; specs/011-test-quality-reporting/research.md D2).
// Mutation runs execute the tests once per mutant, so they use only the fast pure-logic unit tests in tests/unit.
// The two markdown-body component tests are the only tests of markdown-limits and markdown-url, so they are included.
// Other route, component, architecture and security tests cover the lib modules through React rendering and file scans
// and made each mutant cost many seconds. workflow-triggers.test.ts reads the repository's .github folder, which
// is outside the Stryker sandbox, and is about a workflow file rather than the lib modules.
// Ordinary `vitest run` is unchanged and still runs everything.
const base = baseConfig as { test?: Record<string, unknown> };

export default defineConfig({
  ...baseConfig,
  test: {
    ...base.test,
    include: ["tests/unit/**/*.{test,spec}.{ts,tsx}", "tests/components/markdown-body.*.test.tsx"],
    exclude: ["tests/e2e/**", "node_modules/**", "tests/unit/workflow-triggers.test.ts"],
  },
});
