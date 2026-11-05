import { describe, it, expect } from "vitest";
import fs from "node:fs";
import path from "node:path";

describe("Route registration", () => {
  it("every file in app/routes is registered in app/routes.ts, so a route module can never ship unreachable", () => {
    const routesDir = path.resolve(process.cwd(), "app", "routes");
    const config = fs.readFileSync(path.resolve(process.cwd(), "app", "routes.ts"), "utf8");

    const unregistered = fs
      .readdirSync(routesDir)
      .filter((f) => f.endsWith(".tsx"))
      .filter((f) => !config.includes(`"routes/${f}"`));

    expect(unregistered).toEqual([]);
  });

  it("registers the Insights page at /insights", () => {
    const config = fs.readFileSync(path.resolve(process.cwd(), "app", "routes.ts"), "utf8");
    expect(config).toContain('route("insights", "routes/insights.tsx")');
  });
});
