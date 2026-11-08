import { describe, it, expect } from "vitest";
import fs from "node:fs";
import path from "node:path";

// The site root had no index.html, so the Pages root returned 404. A static landing page is staged at the root by both
// publish jobs.

const repoRoot = path.resolve(process.cwd(), "..");

function read(...parts: string[]): string {
  return fs.readFileSync(path.resolve(repoRoot, ...parts), "utf8");
}

describe("pages landing page", () => {
  const exists = fs.existsSync(path.resolve(repoRoot, "pages", "index.html"));

  it("exists with the same restrictive CSP as the trends page and no scripts", () => {
    expect(exists).toBe(true);
    const html = read("pages", "index.html");
    const trends = read("pages", "metrics", "index.html");
    const csp = (text: string): string | undefined => /http-equiv="Content-Security-Policy" content="([^"]*)"/.exec(text)?.[1];
    expect(csp(html)).toBe(csp(trends));
    expect(html).not.toMatch(/<script/i);
    expect(html).not.toMatch(/\son[a-z]+=/i);
  });

  it("links to the three published areas and says the numbers come from the pipeline", () => {
    const html = read("pages", "index.html");
    for (const href of ["allure/", "coverage/index.html", "metrics/"]) {
      expect(html).toContain(`href="${href}"`);
    }
    expect(html).toMatch(/come from the pipeline/);
  });

  for (const file of ["ci.yml", "nightly-quality.yml"]) {
    it(`is staged at the site root by ${file}`, () => {
      expect(read(".github", "workflows", file)).toContain("cp pages/index.html site/index.html");
    });
  }
});
