import { describe, it, expect, afterAll } from "vitest";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { spawnSync } from "node:child_process";

/**
 * T082: the invariants behind ADR 0032 that a component test cannot show. The dependency set is exact and small,
 * no rehype plugin can parse HTML, nothing in the app writes raw HTML into the DOM except the one static theme script,
 * and the licence audit over the resolved tree runs as part of this suite so a bad licence fails the build.
 */

const WEB_DIR = path.resolve(import.meta.dirname, "..", "..");
const REPO_DIR = path.resolve(WEB_DIR, "..");
const SCRIPT = path.join(REPO_DIR, "scripts", "licence-audit.mjs");

const PINS: Record<string, string> = {
  "react-markdown": "10.1.0",
  "remark-gfm": "4.0.1",
  "remark-breaks": "4.0.0",
};

function readPackageJson(): { dependencies: Record<string, string>; devDependencies: Record<string, string> } {
  return JSON.parse(fs.readFileSync(path.join(WEB_DIR, "package.json"), "utf8")) as {
    dependencies: Record<string, string>;
    devDependencies: Record<string, string>;
  };
}

function listFiles(dir: string, skip: (full: string) => boolean): string[] {
  const out: string[] = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (skip(full)) continue;
    if (entry.isDirectory()) out.push(...listFiles(full, skip));
    else if (/\.(ts|tsx|js|jsx|mjs)$/.test(entry.name)) out.push(full);
  }
  return out;
}

describe("Markdown invariants (T082)", () => {
  describe("dependencies", () => {
    it("pins react-markdown, remark-gfm and remark-breaks to exact versions, with no ^ or ~", () => {
      const pkg = readPackageJson();
      for (const [name, version] of Object.entries(PINS)) {
        expect(pkg.dependencies[name], name).toBe(version);
      }
    });

    it("resolves exactly those versions in the lockfile", () => {
      const lock = fs.readFileSync(path.join(REPO_DIR, "pnpm-lock.yaml"), "utf8");
      for (const [name, version] of Object.entries(PINS)) {
        expect(lock, name).toContain(`\n  ${name}@${version}:`);
      }
    });

    it("has no rehype plugin (rehype-raw, rehype-sanitize or any other) in package.json or the lockfile", () => {
      const pkg = readPackageJson();
      const declared = [...Object.keys(pkg.dependencies), ...Object.keys(pkg.devDependencies)];
      expect(declared.filter((n) => n.startsWith("rehype"))).toEqual([]);

      const lock = fs.readFileSync(path.join(REPO_DIR, "pnpm-lock.yaml"), "utf8");
      expect(lock).not.toContain("rehype-raw");
      // remark-rehype (a dependency of react-markdown) is not a plugin that parses HTML; a package called rehype-* is.
      expect(lock).not.toMatch(/(^|[\s'"/])rehype-[a-z]/m);
    });

    it("has no syntax highlighter or html parsing library added for markdown", () => {
      const pkg = readPackageJson();
      const declared = Object.keys(pkg.dependencies).join(" ");
      for (const banned of ["highlight.js", "prismjs", "shiki", "lowlight", "parse5", "hast-util-raw", "dompurify", "sanitize-html"]) {
        expect(declared, banned).not.toContain(banned);
      }
    });
  });

  describe("no raw HTML sink", () => {
    it("writes dangerouslySetInnerHTML nowhere under web/app except the static, nonce'd theme script in root.tsx", () => {
      const files = listFiles(path.join(WEB_DIR, "app"), (full) => full.includes(`${path.sep}ds${path.sep}`));
      const offenders = files.filter((f) => fs.readFileSync(f, "utf8").includes("dangerouslySetInnerHTML"));

      expect(offenders.map((f) => path.relative(WEB_DIR, f).replaceAll("\\", "/"))).toEqual(["app/root.tsx"]);

      const root = fs.readFileSync(path.join(WEB_DIR, "app", "root.tsx"), "utf8");
      expect(root.match(/dangerouslySetInnerHTML/g)?.length).toBe(1);
      expect(root).toContain("dangerouslySetInnerHTML={{ __html: THEME_BOOTSTRAP_SCRIPT }}");

      // The script is a module constant (009: it moved to lib/theme.ts with the rest of the theme logic). Its template
      // may interpolate only the two module constants that name the cookie pattern and the colour-scheme query, never
      // request data, so nothing a user controls can reach the sink.
      const theme = fs.readFileSync(path.join(WEB_DIR, "app", "lib", "theme.ts"), "utf8");
      const template = /export const THEME_BOOTSTRAP_SCRIPT = `([^`]*)`;/.exec(theme)?.[1];
      expect(template, "THEME_BOOTSTRAP_SCRIPT must be a constant template literal").toBeDefined();
      const interpolations = [...(template ?? "").matchAll(/\$\{([^}]*)\}/g)].map((m) => m[1]);
      expect(interpolations).toEqual(["COOKIE_PATTERN.source", "DARK_SCHEME_QUERY"]);
    });

    it("never enables raw HTML in react-markdown: no skipHtml opt-out of text, no allowDangerousHtml, no rehypePlugins", () => {
      const source = fs.readFileSync(path.join(WEB_DIR, "app", "components", "MarkdownBody.tsx"), "utf8");

      expect(source).not.toMatch(/allowDangerousHtml/);
      expect(source).not.toMatch(/rehypePlugins/);
      expect(source).not.toMatch(/import[^;]*rehype/i);
      expect(source).toContain("unwrapDisallowed");
      expect(source).toContain("urlTransform");
    });
  });

  describe("licence audit (scripts/licence-audit.mjs)", () => {
    const made: string[] = [];

    afterAll(() => {
      for (const dir of made) fs.rmSync(dir, { recursive: true, force: true });
    });

    function run(args: string[]) {
      const result = spawnSync(process.execPath, [SCRIPT, ...args], { encoding: "utf8" });
      return { status: result.status, out: `${result.stdout}${result.stderr}` };
    }

    /** A fictional node_modules tree: { name: { license?, licenses?, dependencies?, optionalDependencies? } }. */
    function fixture(packages: Record<string, Record<string, unknown>>): string {
      const dir = fs.mkdtempSync(path.join(os.tmpdir(), "roleradar-licence-"));
      made.push(dir);
      fs.mkdirSync(path.join(dir, "web"), { recursive: true });
      for (const [name, fields] of Object.entries(packages)) {
        const pkgDir = path.join(dir, "web", "node_modules", name);
        fs.mkdirSync(pkgDir, { recursive: true });
        fs.writeFileSync(path.join(pkgDir, "package.json"), JSON.stringify({ name, version: "1.0.0", ...fields }));
      }
      return path.join(dir, "web");
    }

    it("passes on the real resolved tree of the three packages and lists every package with its licence", () => {
      const { status, out } = run([]);

      expect(out).toMatch(/react-markdown@10\.1\.0\s+MIT/);
      expect(out).toMatch(/remark-gfm@4\.0\.1\s+MIT/);
      expect(out).toMatch(/remark-breaks@4\.0\.0\s+MIT/);
      const total = Number(/(\d+) packages audited/.exec(out)?.[1] ?? "0");
      expect(total).toBeGreaterThan(40);
      expect(out).toMatch(/PASS/);
      expect(status).toBe(0);
    });

    it("fails on a transitive dependency with a licence outside the allowed list", () => {
      const from = fixture({
        root: { license: "MIT", dependencies: { middle: "1.0.0" } },
        middle: { license: "ISC", dependencies: { leaf: "1.0.0" } },
        leaf: { license: "GPL-3.0-only" },
      });
      const { status, out } = run(["--from", from, "--packages", "root"]);

      expect(status).toBe(1);
      expect(out).toMatch(/leaf@1\.0\.0\s+GPL-3\.0-only\s+FAIL/);
      expect(out).toMatch(/FAIL/);
    });

    it.each(["MIT", "ISC", "BSD-2-Clause", "BSD-3-Clause", "Apache-2.0", "0BSD"])("accepts %s", (license) => {
      const from = fixture({ root: { license } });
      expect(run(["--from", from, "--packages", "root"]).status).toBe(0);
    });

    it.each(["GPL-2.0", "LGPL-3.0", "AGPL-3.0", "MPL-2.0", "CC-BY-4.0", "UNLICENSED", "BSD-4-Clause", "MIT-0", "Unlicense", "WTFPL"])(
      "rejects %s (outside the allowed list)",
      (license) => {
        const from = fixture({ root: { license } });
        expect(run(["--from", from, "--packages", "root"]).status).toBe(1);
      }
    );

    it("treats a missing licence field as a failure, not as permissive", () => {
      const from = fixture({ root: {} });
      const { status, out } = run(["--from", from, "--packages", "root"]);

      expect(status).toBe(1);
      expect(out).toMatch(/root@1\.0\.0\s+\(none\)\s+FAIL/);
    });

    it("treats 'SEE LICENSE IN ...' as a failure because it cannot be matched to an allowed licence", () => {
      const from = fixture({ root: { license: "SEE LICENSE IN LICENSE.md" } });
      expect(run(["--from", from, "--packages", "root"]).status).toBe(1);
    });

    it("reads the legacy licenses array", () => {
      const ok = fixture({ root: { licenses: [{ type: "MIT", url: "https://example.invalid/mit" }] } });
      expect(run(["--from", ok, "--packages", "root"]).status).toBe(0);

      const bad = fixture({ root: { licenses: [{ type: "GPL-3.0" }] } });
      expect(run(["--from", bad, "--packages", "root"]).status).toBe(1);
    });

    describe("SPDX expressions", () => {
      it.each([
        ["(MIT OR GPL-3.0)", 0],
        ["(GPL-3.0 OR MIT)", 0],
        ["MIT OR Apache-2.0", 0],
        ["MIT AND ISC", 0],
        ["(MIT AND GPL-3.0)", 1],
        ["MIT AND (GPL-3.0 OR ISC)", 0],
        ["GPL-3.0 OR LGPL-3.0", 1],
        ["Apache-2.0 WITH LLVM-exception", 1],
        ["(MIT", 1],
        ["", 1],
      ])("%j gives exit %i", (expression, expected) => {
        const from = fixture({ root: { license: expression } });
        expect(run(["--from", from, "--packages", "root"]).status).toBe(expected);
      });
    });

    it("fails when a required dependency cannot be resolved, but skips an optional one that is not installed", () => {
      const missing = fixture({ root: { license: "MIT", dependencies: { ghost: "1.0.0" } } });
      const missingRun = run(["--from", missing, "--packages", "root"]);
      expect(missingRun.status).toBe(1);
      expect(missingRun.out).toMatch(/ghost/);
      expect(missingRun.out).toMatch(/unresolved/i);

      const optional = fixture({ root: { license: "MIT", optionalDependencies: { ghost: "1.0.0" } } });
      expect(run(["--from", optional, "--packages", "root"]).status).toBe(0);
    });

    it("fails when a named root package is not installed", () => {
      const from = fixture({ other: { license: "MIT" } });
      const { status, out } = run(["--from", from, "--packages", "root"]);

      expect(status).toBe(1);
      expect(out).toMatch(/root/);
    });

    it("handles a dependency cycle and a package reached twice without looping or double counting", () => {
      const from = fixture({
        a: { license: "MIT", dependencies: { b: "1.0.0", c: "1.0.0" } },
        b: { license: "MIT", dependencies: { a: "1.0.0", c: "1.0.0" } },
        c: { license: "MIT" },
      });
      const { status, out } = run(["--from", from, "--packages", "a"]);

      expect(status).toBe(0);
      expect(out).toMatch(/3 packages audited/);
    });
  });
});
