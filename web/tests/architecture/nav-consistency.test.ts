import { describe, it, expect } from "vitest";
import fs from "node:fs";
import path from "node:path";

const routesDir = path.resolve(process.cwd(), "app", "routes");

function routesRenderingAppShell(): { file: string; source: string }[] {
  return fs
    .readdirSync(routesDir)
    .filter((f) => f.endsWith(".tsx"))
    .map((f) => ({ file: f, source: fs.readFileSync(path.join(routesDir, f), "utf8") }))
    .filter(({ source }) => source.includes("navItems="));
}

describe("Navigation consistency", () => {
  it("finds the routes that render the shell", () => {
    expect(routesRenderingAppShell().length).toBeGreaterThanOrEqual(10);
  });

  it("every route builds its sidebar from the shared getNavItems helper", () => {
    const offenders = routesRenderingAppShell()
      .filter(({ source }) => !source.includes("getNavItems("))
      .map(({ file }) => file);
    expect(offenders).toEqual([]);
  });

  it("no route declares its own navItems array", () => {
    const offenders = routesRenderingAppShell()
      .filter(({ source }) => /navItems\s*=\s*\[/.test(source))
      .map(({ file }) => file);
    expect(offenders).toEqual([]);
  });
});
