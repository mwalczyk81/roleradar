import { defineConfig } from "vitest/config";

// Feature 011: when ALLURE_RESULTS_DIR is set (CI and the nightly), Vitest also writes Allure result files there for the
// combined report. Unset, nothing changes. Results are sanitized before anything is published (scripts/allure-sanitize.mjs).
const allureResultsDir = process.env.ALLURE_RESULTS_DIR;

export default defineConfig({
  test: {
    environment: "jsdom",
    globals: true,
    setupFiles: ["./tests/setup.ts", ...(allureResultsDir ? ["allure-vitest/setup"] : [])],
    reporters: allureResultsDir ? ["default", ["allure-vitest/reporter", { resultsDir: allureResultsDir }]] : ["default"],
    include: ["tests/**/*.{test,spec}.{ts,tsx}"],
    exclude: ["tests/e2e/**", "node_modules/**"],
    coverage: {
      provider: "v8",
      reporter: ["text", "json", "html"],
      include: ["app/**"],
      exclude: ["app/ds/**"],
      thresholds: {
        lines: 80,
        branches: 75,
      },
    },
  },
});
