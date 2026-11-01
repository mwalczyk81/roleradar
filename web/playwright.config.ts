import { defineConfig, devices } from "@playwright/test";

export function getRealStackHostResolverRules(identityPort?: string, apiPort?: string): string {
  if (identityPort === "5001") {
    throw new Error("Security violation: real-stack must never map identity to default host port 5001.");
  }
  const resolvedIdentityPort = identityPort || "0";
  const resolvedApiPort = apiPort && apiPort !== "5002" ? apiPort : "0";
  return `MAP identity:5001 127.0.0.1:${resolvedIdentityPort}, MAP identity 127.0.0.1:${resolvedIdentityPort}, MAP api:5002 127.0.0.1:${resolvedApiPort}, MAP api 127.0.0.1:${resolvedApiPort}`;
}

export default defineConfig({
  testDir: "./tests/e2e",
  fullyParallel: false,
  workers: 1,
  timeout: 30_000,
  expect: {
    timeout: 5_000,
  },
  use: {
    ignoreHTTPSErrors: true,
    trace: "on-first-retry",
  },
  projects: [
    {
      name: "chromium",
      // The real-stack spec must only run in the real-stack project: this project maps identity to host port 5001.
      testIgnore: ["**/real-stack.spec.ts"],
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
      testMatch: ["**/real-stack.spec.ts"],
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
