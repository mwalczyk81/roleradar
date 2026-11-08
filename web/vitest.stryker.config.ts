import { defineConfig } from "vitest/config";
import baseConfig from "./vitest.config";

// Vitest configuration used only by StrykerJS (stryker.config.mjs; specs/011-test-quality-reporting/research.md D2).
// Stryker runs the tests in a sandbox that holds only web/, once per mutant, so it runs only the tests whose subject
// is web/app/lib: tests/unit (pure logic of the lib modules) and the two markdown-body component tests, which are
// the only tests of markdown-limits and markdown-url. Tests about scripts, workflows, pages, the config files, routes
// and the app shell live in tests/tooling, tests/routes and the other folders, which this include never lists, so
// there is no exclude list to grow. tests/tooling/stryker-include-scope.test.ts fails when a test under this include
// imports or reads a path outside web/. Ordinary `vitest run` is unchanged and still runs everything.
const base = baseConfig as { test?: Record<string, unknown> };

export default defineConfig({
  ...baseConfig,
  test: {
    ...base.test,
    include: ["tests/unit/**/*.{test,spec}.{ts,tsx}", "tests/components/markdown-body.*.test.tsx"],
    exclude: ["node_modules/**"],
  },
});
