import { describe, it, expect } from "vitest";
import fs from "node:fs";
import path from "node:path";
import ts from "typescript";

/**
 * 009 inline-style ratchet (contracts/inline-style-ratchet.md). Counts JSX `style` attributes per file under
 * web/app (never web/app/ds, which is the vendored kit). A file may not have more than its ceiling (no new inline
 * style), and a ceiling may not be higher than the real count (a phase that removed styles must lower it).
 */

const appDir = path.resolve(process.cwd(), "app");
const ceilingPath = path.resolve(process.cwd(), "tests", "architecture", "inline-style-ceiling.json");

interface Ceiling {
  total: number;
  files: Record<string, number>;
}

function sourceFiles(dir: string, out: string[] = []): string[] {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      if (full === path.join(appDir, "ds")) continue;
      sourceFiles(full, out);
    } else if (/\.(ts|tsx)$/.test(entry.name) && !entry.name.endsWith(".d.ts")) {
      out.push(full);
    }
  }
  return out;
}

/** Number of JSX attributes named `style` in a source file, by syntax tree (not by text). */
export function countStyleAttributes(fileName: string, source: string): number {
  const kind = fileName.endsWith(".tsx") ? ts.ScriptKind.TSX : ts.ScriptKind.TS;
  const sf = ts.createSourceFile(fileName, source, ts.ScriptTarget.Latest, true, kind);
  let count = 0;
  const visit = (node: ts.Node): void => {
    if (ts.isJsxAttribute(node) && ts.isIdentifier(node.name) && node.name.text === "style") count += 1;
    ts.forEachChild(node, visit);
  };
  visit(sf);
  return count;
}

function realCounts(): Record<string, number> {
  const counts: Record<string, number> = {};
  for (const file of sourceFiles(appDir)) {
    const n = countStyleAttributes(file, fs.readFileSync(file, "utf8"));
    if (n > 0) counts[path.relative(path.resolve(process.cwd()), file).split(path.sep).join("/")] = n;
  }
  return counts;
}

describe("Inline style ratchet (web/app, excluding the vendored kit)", () => {
  const ceiling = JSON.parse(fs.readFileSync(ceilingPath, "utf8")) as Ceiling;
  const real = realCounts();

  it("counts attributes by syntax tree, not by text", () => {
    expect(countStyleAttributes("a.tsx", `const a = <div style={{ color: "red" }} />;`)).toBe(1);
    expect(countStyleAttributes("a.tsx", `const a = <div className="style" title="style={{}}" />;`)).toBe(0);
    expect(countStyleAttributes("a.tsx", `// style={{ a: 1 }}\nconst a = <p style={s}>style</p>;`)).toBe(1);
    expect(countStyleAttributes("a.ts", `const style = { a: 1 }; export default style;`)).toBe(0);
  });

  it("no file has more inline styles than its ceiling (no new inline styles)", () => {
    const over = Object.entries(real)
      .filter(([file, n]) => n > (ceiling.files[file] ?? 0))
      .map(([file, n]) => `${file}: ${n} found, ceiling ${ceiling.files[file] ?? 0}`);
    expect(over, `Inline styles above the ceiling:\n${over.join("\n")}`).toEqual([]);
  });

  it("no ceiling is higher than the real count (lower the ceiling when styles are removed)", () => {
    const stale = Object.entries(ceiling.files)
      .filter(([file, max]) => (real[file] ?? 0) < max)
      .map(([file, max]) => `${file}: ceiling ${max}, found ${real[file] ?? 0}`);
    expect(stale, `Ceilings above the real count:\n${stale.join("\n")}`).toEqual([]);
  });

  it("the total matches the sum of the per-file ceilings and the real total", () => {
    const sum = Object.values(ceiling.files).reduce((a, b) => a + b, 0);
    const realTotal = Object.values(real).reduce((a, b) => a + b, 0);
    expect(ceiling.total).toBe(sum);
    expect(realTotal).toBe(sum);
  });
});
