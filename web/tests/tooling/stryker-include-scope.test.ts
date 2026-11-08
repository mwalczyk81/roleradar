import { describe, it, expect } from "vitest";
import fs from "node:fs";
import path from "node:path";
import strykerConfig from "../../vitest.stryker.config";

// Feature 011: StrykerJS runs the tests in a sandbox that holds only web/ (plus the contracts/ folder that
// scripts/stryker-run.mjs copies next to it). A test whose subject lives anywhere else (pages/, .github/, quality/,
// specs/, scripts/ at the repository root) cannot run there, and the dry run stops at the first such import.
// This test scans every test the Stryker config includes and fails when one imports or reads a path outside web/.
// Tests about scripts, workflows, pages or other tooling belong in tests/tooling, which Stryker does not include.

const WEB_ROOT = path.resolve(import.meta.dirname, "..", "..");

// The one folder outside web/ that the sandbox provides, copied by scripts/stryker-run.mjs.
const PROVIDED_OUTSIDE = "contracts";

export function findOutsideWebReferences(file: string, text: string): string[] {
  const problems: string[] = [];
  const name = path.relative(WEB_ROOT, file).split(path.sep).join("/");
  const lineOf = (offset: number): number => text.slice(0, offset).split("\n").length;

  // Imports (also multi-line) and dynamic imports with a relative specifier, resolved against the test file.
  // `import type` is erased at build time, so it is not a runtime dependency.
  const importPattern = /(?:^|\n)\s*(?:import|export)\s+(type\s+)?[^;"']*?\bfrom\s+["']([^"']+)["']|\bimport\(\s*["']([^"']+)["']\s*\)/g;
  let withoutImports = text;
  for (const match of text.matchAll(importPattern)) {
    withoutImports = withoutImports.replace(match[0], match[0].replace(/[^\n]/g, " "));
    const specifier = match[2] ?? match[3];
    if (match[1] || !specifier?.startsWith(".")) continue;
    const relative = path.relative(WEB_ROOT, path.resolve(path.dirname(file), specifier));
    if (relative.startsWith("..") && !relative.split(path.sep).includes(PROVIDED_OUTSIDE)) {
      problems.push(`${name}:${lineOf(match.index ?? 0)} imports ${specifier}, which is outside web/`);
    }
  }

  // File reads: a parent-directory segment inside a path-building or file-system call, unless the same statement
  // targets contracts/.
  const pathCall = /\b(?:path\.)?(?:resolve|join)\(|readFileSync|readdirSync|existsSync|statSync|__dirname|process\.cwd\(\)/;
  const parentSegment = /["'`]\.\.["'`]|["'`]\.\.\/|\/\.\.\//;
  withoutImports.split("\n").forEach((line, index) => {
    const code = line.trim();
    if (code.startsWith("//") || code.startsWith("*")) return;
    if (pathCall.test(line) && parentSegment.test(line) && !line.includes(`"${PROVIDED_OUTSIDE}"`) && !line.includes(`${PROVIDED_OUTSIDE}/`)) {
      problems.push(`${name}:${index + 1} builds a path outside web/: ${code}`);
    }
  });
  return problems;
}

function includedTestFiles(): string[] {
  const include = (strykerConfig as { test?: { include?: string[] } }).test?.include ?? [];
  const files = include.flatMap((pattern) => fs.globSync(pattern, { cwd: WEB_ROOT }));
  return [...new Set(files)].sort().map((f) => path.join(WEB_ROOT, f));
}

describe("the tests Stryker runs", () => {
  it("include test files", () => {
    expect(includedTestFiles().length).toBeGreaterThan(10);
  });

  it("only import or read paths inside web/", () => {
    const problems = includedTestFiles().flatMap((file) => findOutsideWebReferences(file, fs.readFileSync(file, "utf8")));
    expect(problems).toEqual([]);
  });
});

describe("the outside-web scan", () => {
  const file = path.join(WEB_ROOT, "tests", "unit", "scratch.test.ts");

  it("flags a relative import that leaves web/", () => {
    expect(findOutsideWebReferences(file, 'import { x } from "../../../pages/metrics/trends.js";')).toHaveLength(1);
  });

  it("flags a file read that leaves web/", () => {
    expect(findOutsideWebReferences(file, 'const p = path.resolve(process.cwd(), "..", "pages", "metrics");')).toHaveLength(1);
    expect(findOutsideWebReferences(file, 'fs.readFileSync("../.github/workflows/ci.yml", "utf8");')).toHaveLength(1);
  });

  it("flags a dynamic import that leaves web/", () => {
    expect(findOutsideWebReferences(file, 'const m = await import("../../../quality/x.js");')).toHaveLength(1);
  });

  it("allows imports inside web/, type-only imports, and the provided contracts folder", () => {
    expect(findOutsideWebReferences(file, 'import { x } from "../../app/lib/nav.js";')).toEqual([]);
    expect(findOutsideWebReferences(file, 'import type { components } from "../../../contracts/client/api-types.js";')).toEqual([]);
    expect(findOutsideWebReferences(file, 'const p = path.resolve(process.cwd(), "..", "contracts", "insights-lenses.json");')).toEqual([]);
  });
});
