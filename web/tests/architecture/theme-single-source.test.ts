import { describe, it, expect } from "vitest";
import fs from "node:fs";
import path from "node:path";

/**
 * 009 US2: the theme cookie and the system colour-scheme query are handled in exactly one module,
 * web/app/lib/theme.ts. root.tsx and use-theme.ts are consumers and carry neither. The vendored kit is out of scope.
 */

const appDir = path.resolve(process.cwd(), "app");
const THEME_MODULE = "lib/theme.ts";

function sourceFiles(dir: string, out: string[] = []): string[] {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      if (full === path.join(appDir, "ds")) continue;
      sourceFiles(full, out);
    } else if (/\.(ts|tsx)$/.test(entry.name)) {
      out.push(full);
    }
  }
  return out;
}

function filesContaining(needle: string): string[] {
  return sourceFiles(appDir)
    .filter((file) => fs.readFileSync(file, "utf8").includes(needle))
    .map((file) => path.relative(appDir, file).split(path.sep).join("/"));
}

describe("Theme logic lives in one module", () => {
  it("only lib/theme.ts names the roleradar_theme cookie", () => {
    expect(filesContaining("roleradar_theme")).toEqual([THEME_MODULE]);
  });

  it("only lib/theme.ts asks for prefers-color-scheme", () => {
    expect(filesContaining("prefers-color-scheme")).toEqual([THEME_MODULE]);
  });

  it("root.tsx and use-theme.ts both import the theme module", () => {
    for (const file of ["root.tsx", "lib/use-theme.ts"]) {
      const source = fs.readFileSync(path.join(appDir, file), "utf8");
      expect(source, `${file} must import the theme module`).toMatch(/from\s+"\.{1,2}\/(lib\/)?theme\.js"/);
    }
  });
});
