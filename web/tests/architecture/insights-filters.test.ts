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

const read = (relative: string) => fs.readFileSync(path.join(appDir, relative), "utf8");
const rel = (file: string) => path.relative(appDir, file).split(path.sep).join("/");

describe("Insights filters are built only by the lens module (ADR 0029)", () => {
  it("the lens module, view model and counts loader exist", () => {
    for (const file of ["lib/insights-lenses.ts", "lib/insights-view.ts", "lib/insights-counts.server.ts", "routes/insights.tsx"]) {
      expect(fs.existsSync(path.join(appDir, file)), `${file} must exist`).toBe(true);
    }
  });

  it("the Insights route, view model and counts loader build no filters of their own", () => {
    for (const file of ["routes/insights.tsx", "lib/insights-view.ts", "lib/insights-counts.server.ts"]) {
      const source = read(file);
      expect(source, `${file} must not import the query builder`).not.toMatch(/from "\.\/odata-client\.js"|from "\.\.\/lib\/odata-client\.js"/);
      expect(source, `${file} must not mention $filter`).not.toContain("$filter");
      expect(source, `${file} must not name filter fields`).not.toMatch(/firstSeenAt|hasOffer|hasRejection|rejectedBeforeInterview|firstResponseAt/);
    }
  });

  it("filter fields owned by the lenses appear only in the lens module (and the postings has-application filter)", () => {
    const offenders: string[] = [];
    for (const file of sourceFiles(appDir)) {
      const name = rel(file);
      if (name === "lib/insights-lenses.ts") continue;
      const source = fs.readFileSync(file, "utf8");
      if (/rejectedBeforeInterview|"hasOffer"|"hasRejection"|firstSeenAt/.test(source) && !/ODataModel|interface /.test(name)) {
        // Row models may declare the fields; filters may not.
        if (/field:\s*"(rejectedBeforeInterview|hasOffer|hasRejection|firstSeenAt)"/.test(source)) {
          offenders.push(name);
        }
      }
    }
    expect(offenders).toEqual([]);
  });

  it("both list loaders import the lens module and use lensFilter", () => {
    for (const file of ["routes/postings._index.tsx", "routes/applications._index.tsx"]) {
      const source = read(file);
      expect(source).toMatch(/from "\.\.\/lib\/insights-lenses\.js"/);
      expect(source).toContain("lensFilter(");
      expect(source).toContain("parseLensParams(");
    }
  });

  it("the Insights route links through lensListHref and counts through the counts loader", () => {
    const route = read("routes/insights.tsx");
    expect(route).toMatch(/from "\.\.\/lib\/insights-counts\.server\.js"/);
    expect(route).toMatch(/from "\.\.\/lib\/insights-view\.js"/);
  });

  it("only web/app/ui.ts imports the vendored design system, and never the shared chart internals", () => {
    const offenders: string[] = [];
    const internals: string[] = [];
    for (const file of sourceFiles(appDir)) {
      const name = rel(file);
      const source = fs.readFileSync(file, "utf8");
      if (/from\s+"[^"]*\/ds\/components\/src\//.test(source) && name !== "ui.ts") offenders.push(name);
      if (/chartParts/.test(source) && name !== "ui.ts") internals.push(name);
      if (/chartParts/.test(source) && name === "ui.ts") internals.push(name);
    }
    expect(offenders).toEqual([]);
    expect(internals).toEqual([]);
  });

  it("ui.ts exports the four chart components", () => {
    const ui = read("ui.ts");
    for (const name of ["StatTile", "BarChart", "ColumnChart", "Sparkline"]) {
      expect(ui, `${name} must be exported through ui.ts`).toMatch(new RegExp(`export const ${name} =`));
    }
  });
});
