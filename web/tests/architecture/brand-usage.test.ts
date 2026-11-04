import { describe, it, expect } from "vitest";
import fs from "node:fs";
import path from "node:path";

const appDir = path.resolve(process.cwd(), "app");

function sourceFiles(dir: string): string[] {
  const out: string[] = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      if (entry.name === "ds") continue; // vendored, copy-only
      out.push(...sourceFiles(full));
    } else if (/\.(tsx|ts)$/.test(entry.name)) {
      out.push(full);
    }
  }
  return out;
}

const routeFiles = fs
  .readdirSync(path.join(appDir, "routes"))
  .filter((f) => f.endsWith(".tsx"))
  .map((f) => ({ file: f, source: fs.readFileSync(path.join(appDir, "routes", f), "utf8") }));

describe("Shell brand consistency", () => {
  it("finds the routes that render the shell", () => {
    expect(routeFiles.filter(({ source }) => source.includes("<AppShell")).length).toBeGreaterThanOrEqual(10);
  });

  it("no route passes a bare string brand to the shell", () => {
    const offenders = sourceFiles(appDir)
      .map((f) => ({ f, source: fs.readFileSync(f, "utf8") }))
      .filter(({ source }) => /\bbrand\s*=\s*"/.test(source) || /\bbrand\s*=\s*\{\s*["'`]/.test(source))
      .map(({ f }) => path.relative(appDir, f));
    expect(offenders).toEqual([]);
  });

  it("every route that renders the shell uses the shared Brand component", () => {
    const offenders = routeFiles
      .filter(({ source }) => source.includes("<AppShell"))
      .filter(({ source }) => !/brand=\{<Brand\s*\/>\}/.test(source))
      .map(({ file }) => file);
    expect(offenders).toEqual([]);
  });

  it("every route that renders the shell imports Brand from the shared component", () => {
    const offenders = routeFiles
      .filter(({ source }) => source.includes("<AppShell"))
      .filter(({ source }) => !/import\s*\{\s*Brand\s*\}\s*from\s*"\.\.\/components\/Brand\.js"/.test(source))
      .map(({ file }) => file);
    expect(offenders).toEqual([]);
  });
});
