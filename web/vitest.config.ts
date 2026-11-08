import path from "node:path";
import { defineConfig } from "vitest/config";

// Feature 011: when ALLURE_RESULTS_DIR is set (CI and the nightly), Vitest also writes its own JUnit file there,
// <dir>/Vitest.xml, which the combined Allure report reads like the xUnit v3 JUnit files. No Allure adapter is loaded: it
// wraps every expect call in a report step and made assertion-heavy tests about 35 times slower (research.md D15).
// Unset, nothing changes. Results are sanitized before anything is published (scripts/allure-sanitize.mjs).
const allureResultsDir = process.env.ALLURE_RESULTS_DIR;

export default defineConfig({
  test: {
    environment: "jsdom",
    globals: true,
    setupFiles: ["./tests/setup.ts"],
    reporters: allureResultsDir ? ["default", ["junit", { outputFile: path.join(allureResultsDir, "Vitest.xml") }]] : ["default"],
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
