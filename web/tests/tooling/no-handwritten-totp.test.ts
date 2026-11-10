import { describe, it, expect } from "vitest";
import fs from "node:fs";
import path from "node:path";

// Feature 014 (ADR 0042, FR-017, SC-003): no hand-written RFC 6238 or Base32 code is left in the web tests or the app.
// The e2e helper uses the maintained `otpauth` library. This file names the forbidden fragments, so it skips itself.

const FORBIDDEN: Array<{ label: string; fragment: string }> = [
  { label: "an HMAC-SHA1 TOTP", fragment: 'createHmac("sha1"' },
  { label: "a hand-written Base32 alphabet", fragment: 'ALPHABET = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"' },
  { label: "a hand-written Base32 alphabet", fragment: 'alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"' },
];

function sourceFiles(dir: string): string[] {
  const found: string[] = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      if (entry.name === "node_modules" || entry.name === "ds") {
        continue;
      }
      found.push(...sourceFiles(full));
    } else if (/\.(ts|tsx)$/.test(entry.name)) {
      found.push(full);
    }
  }
  return found;
}

describe("no hand-written TOTP in the web tests and app", () => {
  const roots = [path.resolve(process.cwd(), "tests"), path.resolve(process.cwd(), "app")];
  const self = path.resolve(process.cwd(), "tests", "tooling", "no-handwritten-totp.test.ts");
  const files = roots.flatMap((root) => sourceFiles(root)).filter((file) => file !== self);

  it("reads enough files that the scan cannot pass vacuously", () => {
    expect(files.length).toBeGreaterThan(100);
  });

  it("finds no forbidden fragment", () => {
    const hits: string[] = [];
    for (const file of files) {
      const text = fs.readFileSync(file, "utf8");
      for (const { label, fragment } of FORBIDDEN) {
        if (text.includes(fragment)) {
          hits.push(`${path.relative(process.cwd(), file)}: ${label}`);
        }
      }
    }
    expect(hits).toEqual([]);
  });
});
