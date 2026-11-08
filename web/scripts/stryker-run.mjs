// Runs StrykerJS for web/app/lib (feature 011, specs/011-test-quality-reporting/research.md D2).
//
// Stryker runs the tests in a sandbox copy of web/ inside .stryker-tmp/sandbox-*. Some unit tests read the shared
// contract files with a path relative to the working directory (../contracts/...), which is outside web/ and so
// missing from the sandbox. This launcher copies the repository's contracts/ folder next to the sandbox first
// (.stryker-tmp/contracts), so those tests run unchanged. Stryker deletes the temp directory when it finishes.
//
// Usage (from web/): pnpm run mutation [-- extra stryker arguments]
import { spawnSync } from "node:child_process";
import { cpSync, existsSync, mkdirSync } from "node:fs";
import { createRequire } from "node:module";
import { dirname, join, resolve } from "node:path";
import process from "node:process";
import { fileURLToPath } from "node:url";

const webDir = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const contractsSource = resolve(webDir, "..", "contracts");
const tempDir = join(webDir, ".stryker-tmp");

if (!existsSync(contractsSource)) {
  process.stderr.write(`contracts folder not found at ${contractsSource}\n`);
  process.exit(2);
}

mkdirSync(tempDir, { recursive: true });
cpSync(contractsSource, join(tempDir, "contracts"), { recursive: true });

const require = createRequire(import.meta.url);
const strykerPackage = require.resolve("@stryker-mutator/core/package.json");
const strykerBin = join(dirname(strykerPackage), "bin", "stryker.js");

const result = spawnSync(process.execPath, [strykerBin, "run", ...process.argv.slice(2)], {
  cwd: webDir,
  stdio: "inherit",
});
process.exit(result.status ?? 1);
