import { readdirSync, readFileSync, statSync } from "node:fs";
import path from "node:path";
import { describe, expect, it } from "vitest";

// Feature 014 G3 (SC-009, ADR 0045): retry, backoff, timeout and concurrency limiting come from ky and p-limit. No file
// under web/app may bring a hand-written one back: a `sleep` or `abortable` helper, `createLimiter`, or the old
// request-limiter.server.ts file.

const APP_DIR = path.resolve(__dirname, "../../app");
const SKIPPED_DIRS = new Set(["ds", "node_modules"]);
const SOURCE_FILE = /\.(ts|tsx|js|jsx|mjs)$/;

const FORBIDDEN_TEXT = ["function sleep", "function abortable", "createLimiter"] as const;
const FORBIDDEN_FILE = "request-limiter.server.ts";

function sourceFiles(dir: string): string[] {
  const found: string[] = [];
  for (const entry of readdirSync(dir)) {
    const full = path.join(dir, entry);
    if (statSync(full).isDirectory()) {
      if (!SKIPPED_DIRS.has(entry)) {
        found.push(...sourceFiles(full));
      }
    } else {
      found.push(full);
    }
  }
  return found;
}

describe("no hand-written resilience code in web/app (SC-009)", () => {
  const files = sourceFiles(APP_DIR);
  const sources = files.filter((file) => SOURCE_FILE.test(file));

  it("reads the app sources (sentinel: the scan is not vacuous)", () => {
    expect(sources.length).toBeGreaterThan(20);
    expect(sources.some((file) => file.endsWith("api-client.server.ts"))).toBe(true);
  });

  it.each(FORBIDDEN_TEXT)("no file defines or mentions %s", (needle) => {
    const offenders = sources.filter((file) => readFileSync(file, "utf8").includes(needle)).map((file) => path.relative(APP_DIR, file));
    expect(offenders).toEqual([]);
  });

  it("no file is named request-limiter.server.ts", () => {
    const offenders = files.filter((file) => path.basename(file) === FORBIDDEN_FILE).map((file) => path.relative(APP_DIR, file));
    expect(offenders).toEqual([]);
  });
});
