// Size limits for the published gh-pages site (feature 011, ADR 0038). CI-only logic, tested by Vitest
// (tests/tooling/site-limits.test.ts).
//
// GitHub rejects a single file over 100 MB and warns over 50 MB. The Allure history file is committed whole on every
// publish, so it is trimmed to a byte bound before it is staged (oldest runs first, the newest run is always kept), and
// the staged tree fails the job if any file is over 50 MB, so a surprise never reaches the push.
//
//   node web/scripts/site-limits.mjs trim-history <history.jsonl> [--max-bytes N]
//   node web/scripts/site-limits.mjs check-size <site-dir> [--max-bytes N]

import { Buffer } from "node:buffer";
import fs from "node:fs";
import path from "node:path";
import process from "node:process";
import { fileURLToPath } from "node:url";

export const MAX_HISTORY_BYTES = 20 * 1024 * 1024;
export const MAX_FILE_BYTES = 50 * 1024 * 1024;

/**
 * Drops the oldest lines (one run per line) until the file is at most maxBytes. The newest line is always kept, even when
 * it alone is over the bound. A missing file is left alone.
 * @param {string} file
 * @param {number} [maxBytes]
 */
export function trimHistory(file, maxBytes = MAX_HISTORY_BYTES) {
  if (!fs.existsSync(file)) return { trimmed: false, keptRuns: 0, droppedRuns: 0, bytes: 0 };
  const lines = fs.readFileSync(file, "utf8").split("\n").filter((l) => l.length > 0).map((l) => `${l}\n`);
  const sizes = lines.map((l) => Buffer.byteLength(l));
  let bytes = sizes.reduce((a, b) => a + b, 0);
  let first = 0;
  while (bytes > maxBytes && first < lines.length - 1) {
    bytes -= sizes[first];
    first += 1;
  }
  if (first === 0) return { trimmed: false, keptRuns: lines.length, droppedRuns: 0, bytes };
  fs.writeFileSync(file, lines.slice(first).join(""));
  return { trimmed: true, keptRuns: lines.length - first, droppedRuns: first, bytes };
}

/**
 * @param {string} directory
 * @param {number} [maxBytes]
 */
export function checkSiteFileSizes(directory, maxBytes = MAX_FILE_BYTES) {
  /** @type {string[]} */
  const violations = [];
  const walk = (dir) => {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) walk(full);
      else if (entry.isFile()) {
        const size = fs.statSync(full).size;
        if (size > maxBytes) {
          const relative = path.relative(directory, full).split(path.sep).join("/");
          violations.push(`${relative} is ${size} bytes, over the ${maxBytes} byte limit (GitHub warns at 50 MB and rejects a file over 100 MB); nothing is published.`);
        }
      }
    }
  };
  walk(directory);
  return { ok: violations.length === 0, violations };
}

function main(argv) {
  const [command, target, ...rest] = argv;
  const flag = rest.indexOf("--max-bytes");
  const override = flag >= 0 ? Number(rest[flag + 1]) : undefined;
  if (!target || (override !== undefined && !Number.isFinite(override))) {
    process.stderr.write("usage: trim-history <history.jsonl> [--max-bytes N] | check-size <site-dir> [--max-bytes N]\n");
    return 2;
  }
  if (command === "trim-history") {
    const r = trimHistory(target, override);
    const message = r.trimmed
      ? `Allure history trimmed: dropped the ${r.droppedRuns} oldest run(s), kept ${r.keptRuns} (${r.bytes} bytes).`
      : `Allure history within the bound: ${r.keptRuns} run(s), ${r.bytes} bytes.`;
    process.stdout.write(`${message}\n`);
    if (process.env.GITHUB_STEP_SUMMARY) fs.appendFileSync(process.env.GITHUB_STEP_SUMMARY, `${message}\n`);
    return 0;
  }
  if (command === "check-size") {
    const r = checkSiteFileSizes(target, override);
    for (const v of r.violations) process.stderr.write(`${v}\n`);
    if (r.ok) process.stdout.write("Staged site: no file over the size limit.\n");
    return r.ok ? 0 : 1;
  }
  process.stderr.write(`unknown command ${command}\n`);
  return 2;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exitCode = main(process.argv.slice(2));
}
