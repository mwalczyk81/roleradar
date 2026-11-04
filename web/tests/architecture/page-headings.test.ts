import { describe, it, expect } from "vitest";
import fs from "node:fs";
import path from "node:path";

const appDir = path.resolve(process.cwd(), "app");
const routesDir = path.join(appDir, "routes");

// Detail pages and the Insights page: their page and section titles use the display face through a class.
const HEADING_ROUTES = ["postings.$id.tsx", "companies.$id.tsx", "applications.$id.tsx", "notes.$id.tsx", "insights.tsx"];

function headingTags(source: string): string[] {
  return source.match(/<h[1-6]\b[^>]*>/g) ?? [];
}

describe("Route headings use the display face through a class", () => {
  it("defines .page-heading in app.css with the display face and no other font-family", () => {
    const css = fs.readFileSync(path.join(appDir, "app.css"), "utf8");
    const rule = /\.page-heading\s*\{([^}]*)\}/.exec(css);
    expect(rule, ".page-heading rule is missing from app.css").not.toBeNull();
    expect(rule?.[1]).toContain("font-family: var(--ds-font-display)");
  });

  it("the shell page title (h1) also uses the display face", () => {
    const css = fs.readFileSync(path.join(appDir, "app.css"), "utf8");
    const rule = /\.ds-appshell-title\s*\{([^}]*)\}/.exec(css);
    expect(rule, ".ds-appshell-title override is missing from app.css").not.toBeNull();
    expect(rule?.[1]).toContain("font-family: var(--ds-font-display)");
  });

  for (const file of HEADING_ROUTES) {
    it(`${file}: every h2 and h3 carries the page-heading class`, () => {
      const full = path.join(routesDir, file);
      expect(fs.existsSync(full), `${file} must exist`).toBe(true);
      const tags = headingTags(fs.readFileSync(full, "utf8")).filter((t) => /^<h[23]\b/.test(t));
      expect(tags.length).toBeGreaterThan(0);
      const missing = tags.filter((t) => !/className="[^"]*\bpage-heading\b[^"]*"/.test(t));
      expect(missing).toEqual([]);
    });
  }

  it("the new Insights route and shared Brand use classes only: no inline style attributes", () => {
    const insights = path.join(routesDir, "insights.tsx");
    const brand = path.join(appDir, "components", "Brand.tsx");
    for (const file of [insights, brand]) {
      expect(fs.existsSync(file), `${path.basename(file)} must exist`).toBe(true);
      expect(/\bstyle\s*=/.test(fs.readFileSync(file, "utf8")), `${path.basename(file)} must not use inline style attributes`).toBe(false);
    }
  });
});
