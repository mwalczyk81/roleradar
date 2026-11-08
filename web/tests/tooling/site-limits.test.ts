import { afterEach, describe, expect, it } from "vitest";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { checkSiteFileSizes, trimHistory, MAX_HISTORY_BYTES, MAX_FILE_BYTES } from "../../scripts/site-limits.mjs";

// The Allure history file is committed whole on every publish and GitHub rejects a file over 100 MB, so it is trimmed to a
// byte bound (oldest runs first) before it is staged, and the staged site fails the job if any file is over 50 MB.

const created: string[] = [];

afterEach(() => {
  for (const dir of created.splice(0)) fs.rmSync(dir, { recursive: true, force: true });
});

function scratch(): string {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "rr-limits-"));
  created.push(dir);
  return dir;
}

/** One history line of exactly `bytes` bytes including the newline. */
function line(label: string, bytes: number): string {
  const head = `{"run":"${label}","pad":"`;
  const tail = `"}\n`;
  return head + "x".repeat(bytes - head.length - tail.length) + tail;
}

describe("trimHistory", () => {
  it("leaves a history under the bound untouched, byte for byte", () => {
    const file = path.join(scratch(), "history.jsonl");
    const content = line("a", 100) + line("b", 100);
    fs.writeFileSync(file, content);
    const result = trimHistory(file, 1000);
    expect(fs.readFileSync(file, "utf8")).toBe(content);
    expect(result).toMatchObject({ trimmed: false, keptRuns: 2, droppedRuns: 0, bytes: 200 });
  });

  it("keeps a history exactly at the bound", () => {
    const file = path.join(scratch(), "history.jsonl");
    fs.writeFileSync(file, line("a", 100) + line("b", 100));
    expect(trimHistory(file, 200).trimmed).toBe(false);
  });

  it("drops the oldest runs first until the file fits", () => {
    const file = path.join(scratch(), "history.jsonl");
    fs.writeFileSync(file, line("a", 100) + line("b", 100) + line("c", 100) + line("d", 100));
    const result = trimHistory(file, 250);
    expect(fs.readFileSync(file, "utf8")).toBe(line("c", 100) + line("d", 100));
    expect(result).toMatchObject({ trimmed: true, keptRuns: 2, droppedRuns: 2, bytes: 200 });
  });

  it("always keeps the newest run, even when it alone is over the bound", () => {
    const file = path.join(scratch(), "history.jsonl");
    fs.writeFileSync(file, line("a", 100) + line("b", 500));
    const result = trimHistory(file, 200);
    expect(fs.readFileSync(file, "utf8")).toBe(line("b", 500));
    expect(result).toMatchObject({ trimmed: true, keptRuns: 1, droppedRuns: 1 });
  });

  it("treats a missing file as nothing to do", () => {
    expect(trimHistory(path.join(scratch(), "none.jsonl"), 100)).toMatchObject({ trimmed: false, keptRuns: 0 });
  });

  it("has a default bound of 20 MB, well under the 50 MB file check", () => {
    expect(MAX_HISTORY_BYTES).toBe(20 * 1024 * 1024);
    expect(MAX_FILE_BYTES).toBe(50 * 1024 * 1024);
    expect(MAX_HISTORY_BYTES).toBeLessThan(MAX_FILE_BYTES);
  });
});

describe("checkSiteFileSizes", () => {
  it("passes a tree whose files are all under the limit", () => {
    const dir = scratch();
    fs.mkdirSync(path.join(dir, "sub"));
    fs.writeFileSync(path.join(dir, "sub", "a.txt"), "x".repeat(100));
    expect(checkSiteFileSizes(dir, 200)).toEqual({ ok: true, violations: [] });
  });

  it("fails naming a file over the limit, at any depth, with its size", () => {
    const dir = scratch();
    fs.mkdirSync(path.join(dir, "deep", "er"), { recursive: true });
    fs.writeFileSync(path.join(dir, "deep", "er", "big.bin"), "x".repeat(300));
    fs.writeFileSync(path.join(dir, "small.txt"), "x");
    const result = checkSiteFileSizes(dir, 200);
    expect(result.ok).toBe(false);
    expect(result.violations).toHaveLength(1);
    expect(result.violations[0]).toMatch(/deep\/er\/big\.bin/);
    expect(result.violations[0]).toMatch(/300 bytes/);
    expect(result.violations[0]).toMatch(/GitHub/);
  });

  it("allows a file exactly at the limit", () => {
    const dir = scratch();
    fs.writeFileSync(path.join(dir, "edge.bin"), "x".repeat(200));
    expect(checkSiteFileSizes(dir, 200).ok).toBe(true);
  });
});

describe("workflow wiring", () => {
  const root = path.resolve(process.cwd(), "..", ".github", "workflows");
  for (const file of ["ci.yml", "nightly-quality.yml"]) {
    it(`${file} trims the staged history and checks the staged tree before deploying`, () => {
      const text = fs.readFileSync(path.join(root, file), "utf8");
      const trim = text.indexOf("site-limits.mjs trim-history");
      const check = text.indexOf("site-limits.mjs check-size");
      const deploy = text.indexOf("peaceiris/actions-gh-pages@");
      expect(trim).toBeGreaterThan(0);
      expect(check).toBeGreaterThan(trim);
      expect(deploy).toBeGreaterThan(check);
    });
  }
});
