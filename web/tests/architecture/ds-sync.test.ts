import { describe, it, expect } from "vitest";
import crypto from "node:crypto";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";

/** SYNC.md must name the kit version id it was synced from. The id changes on every kit sync, so only its shape is checked. */
const KIT_VERSION_LINE = /^- Kit version: \d{10}-[0-9a-f]{4}\b/;

const HASH_LINE = /^([0-9a-f]{64})\s+(\S.*?)\s*$/;

function listFiles(root: string, dir = root): string[] {
  const out: string[] = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      out.push(...listFiles(root, full));
    } else {
      out.push(path.relative(root, full).split(path.sep).join("/"));
    }
  }
  return out.sort();
}

/** Returns a list of problems; empty when the vendored tree matches SYNC.md. SYNC.md itself is not hashed. */
export function verifyDsSync(dsDir: string): string[] {
  const problems: string[] = [];
  const syncPath = path.join(dsDir, "SYNC.md");
  if (!fs.existsSync(syncPath)) {
    return ["SYNC.md is missing"];
  }
  const sync = fs.readFileSync(syncPath, "utf-8");

  const versionLine = sync.split(/\r?\n/).find((l) => l.startsWith("- Kit version:"));
  if (!versionLine || !KIT_VERSION_LINE.test(versionLine)) {
    problems.push(`SYNC.md must name a kit version id such as 1793794809-c293; found: ${versionLine ?? "no Kit version line"}`);
  }

  const hashes = new Map<string, string>();
  for (const line of sync.split(/\r?\n/)) {
    const m = HASH_LINE.exec(line);
    if (m) {
      hashes.set(m[2], m[1]);
    }
  }

  const files = listFiles(dsDir).filter((f) => f !== "SYNC.md");
  for (const file of files) {
    const expected = hashes.get(file);
    if (!expected) {
      problems.push(`${file}: no sha256 line in SYNC.md`);
      continue;
    }
    const actual = crypto.createHash("sha256").update(fs.readFileSync(path.join(dsDir, file))).digest("hex");
    if (actual !== expected) {
      problems.push(`${file}: sha256 mismatch (SYNC.md ${expected}, file ${actual})`);
    }
  }
  for (const listed of hashes.keys()) {
    if (!files.includes(listed)) {
      problems.push(`${listed}: listed in SYNC.md but not present under web/app/ds`);
    }
  }
  return problems;
}

describe("Architecture Verification: vendored design system sync (007)", () => {
  const defaultDir = path.resolve(__dirname, "../../app/ds");

  it("every file under web/app/ds has a matching sha256 line in SYNC.md and SYNC.md names the expected kit version", () => {
    // DS_SYNC_DIR lets a reviewer point this check at a copy (used to show the check failing).
    const dir = process.env.DS_SYNC_DIR ? path.resolve(process.env.DS_SYNC_DIR) : defaultDir;
    expect(verifyDsSync(dir)).toEqual([]);
  });

  it("the check detects an altered file, a missing hash line and a wrong kit version (self-test on a temp copy)", () => {
    const tmp = fs.mkdtempSync(path.join(os.tmpdir(), "ds-sync-"));
    try {
      fs.cpSync(defaultDir, tmp, { recursive: true });
      expect(verifyDsSync(tmp)).toEqual([]);

      fs.appendFileSync(path.join(tmp, "tokens.json"), " ");
      expect(verifyDsSync(tmp).some((p) => p.startsWith("tokens.json: sha256 mismatch"))).toBe(true);

      fs.writeFileSync(path.join(tmp, "extra.txt"), "unlisted");
      expect(verifyDsSync(tmp)).toContain("extra.txt: no sha256 line in SYNC.md");

      const syncPath = path.join(tmp, "SYNC.md");
      fs.writeFileSync(syncPath, fs.readFileSync(syncPath, "utf-8").replace(/^(- Kit version: )\S+/m, "$1unknown"));
      expect(verifyDsSync(tmp).some((p) => p.startsWith("SYNC.md must name a kit version id"))).toBe(true);
    } finally {
      fs.rmSync(tmp, { recursive: true, force: true });
    }
  });
});
