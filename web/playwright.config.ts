import { defineConfig, devices, type ReporterDescription } from "@playwright/test";

export function getRealStackHostResolverRules(identityPort?: string, apiPort?: string): string {
  if (identityPort === "5001") {
    throw new Error("Security violation: real-stack must never map identity to default host port 5001.");
  }
  const resolvedIdentityPort = identityPort || "0";
  const resolvedApiPort = apiPort && apiPort !== "5002" ? apiPort : "0";
  return `MAP identity:5001 127.0.0.1:${resolvedIdentityPort}, MAP identity 127.0.0.1:${resolvedIdentityPort}, MAP api:5002 127.0.0.1:${resolvedApiPort}, MAP api 127.0.0.1:${resolvedApiPort}`;
}

// 009: when ROLERADAR_VISUAL_DIR is set (the visual baseline spec), screenshots are read from and written to that
// folder, which must be outside the repository. Unset, Playwright keeps its default snapshot location.
const visualDir = process.env.ROLERADAR_VISUAL_DIR?.split("\\").join("/");

const allureResultsDir = process.env.ALLURE_RESULTS_DIR;
const e2eReporters: ReporterDescription[] = [
  ["list"],
  ...(process.env.CI ? [["html", { open: "never" }] satisfies ReporterDescription] : []),
  ...(allureResultsDir ? [["allure-playwright", { resultsDir: allureResultsDir }] satisfies ReporterDescription] : []),
];

export default defineConfig({
  testDir: "./tests/e2e",
  ...(visualDir ? { snapshotPathTemplate: `${visualDir}/{platform}/{arg}{ext}` } : {}),
  fullyParallel: false,
  workers: 1,
  // GitHub runners occasionally drop the network interface mid-run (net::ERR_NETWORK_CHANGED on loopback goto).
  // One CI retry absorbs that; a real failure still fails twice.
  retries: process.env.CI ? 1 : 0,
  timeout: 30_000,
  expect: {
    timeout: 5_000,
  },
  // Failure evidence only (feature 011): a screenshot and a trace for every failed run, no video. "retain-on-failure"
  // records every run and keeps the trace of each failed one, so a test that fails once and passes on the retry still
  // leaves the trace of the failed attempt ("retain-on-first-failure" would record only the first run and keep nothing
  // when the retry is the one that fails; "on-first-retry" kept no trace of the first failure). CI also writes the HTML
  // report, and uploads these files only when a job fails (see ci.yml for the retention). When ALLURE_RESULTS_DIR is
  // set, Allure result files are written there too; the sanitizer decides which traces reach the published report.
  reporter: e2eReporters,
  use: {
    ignoreHTTPSErrors: true,
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  projects: [
    {
      name: "chromium",
      // The real-stack spec must only run in the real-stack project: this project maps identity to host port 5001.
      testIgnore: ["**/real-stack.spec.ts", "**/*.real-stack.spec.ts"],
      use: {
        ...devices["Desktop Chrome"],
        launchOptions: {
          args: [
            "--ignore-certificate-errors",
            "--host-resolver-rules=MAP identity 127.0.0.1, MAP api 127.0.0.1",
          ],
        },
      },
    },
    {
      name: "real-stack",
      testMatch: ["**/real-stack.spec.ts", "**/*.real-stack.spec.ts"],
      use: {
        ...devices["Desktop Chrome"],
        launchOptions: {
          args: [
            "--ignore-certificate-errors",
            `--host-resolver-rules=${getRealStackHostResolverRules(
              process.env.E2E_IDENTITY_PORT,
              process.env.E2E_API_PORT
            )}`,
          ],
        },
      },
    },
  ],
});
