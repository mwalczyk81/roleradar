// StrykerJS mutation testing for the pure-logic modules in web/app/lib.
// Nightly and manual only (never on push or ordinary pull requests); see
// .github/workflows/nightly-quality.yml and specs/011-test-quality-reporting/research.md D2.
//
// Run from web/:  pnpm run mutation   (scripts/stryker-run.mjs copies contracts/ next to the sandbox first)
// Reports:        web/reports/mutation/mutation.html and mutation.json
//
// Included: pure modules with direct unit tests. Excluded (reason in research.md D2):
// *.server.ts (cookies, sessions, OIDC, network, request limiter), use-*.ts (React hooks),
// markdown-plugins.ts (no direct test), routes and components (not pure logic).

/** @type {import('@stryker-mutator/api/core').PartialStrykerOptions} */
export default {
  testRunner: "vitest",
  // pnpm's strict node_modules hides the runner from Stryker's plugin auto-discovery, so load it explicitly.
  plugins: ["@stryker-mutator/vitest-runner"],
  vitest: { configFile: "vitest.stryker.config.ts" },
  checkers: [],
  mutate: [
    "app/lib/application-enums.ts",
    "app/lib/company-enums.ts",
    "app/lib/operational-enums.ts",
    "app/lib/enum-order.ts",
    "app/lib/dispositions.ts",
    "app/lib/format-date.ts",
    "app/lib/insights-stats.ts",
    "app/lib/insights-lenses.ts",
    "app/lib/insights-view.ts",
    "app/lib/markdown-limits.ts",
    "app/lib/markdown-url.ts",
    "app/lib/nav.ts",
    "app/lib/odata-client.ts",
    "app/lib/theme.ts",
  ],
  coverageAnalysis: "perTest",
  // Report-only: break stays 0 here. The committed floor in quality/mutation-minimums.json is
  // enforced by web/scripts/mutation-ratchet.mjs, which cannot be lowered silently.
  thresholds: { high: 80, low: 60, break: 0 },
  reporters: ["clear-text", "progress", "json", "html"],
  jsonReporter: { fileName: "reports/mutation/mutation.json" },
  htmlReporter: { fileName: "reports/mutation/mutation.html" },
  tempDirName: ".stryker-tmp",
  cleanTempDir: true,
  // The GitHub-hosted ubuntu runner has 4 vCPUs. Local measurements: more workers did not help (research.md D5).
  concurrency: 4,
  timeoutMS: 10000,
  timeoutFactor: 2,
};
