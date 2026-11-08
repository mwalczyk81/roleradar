// Counts inline style OBJECTS in the vendored kit source (web/app/ds/components/src/**/*.jsx).
//
// An inline style object (`style={{ ... }}` in JSX, `style: { ... }` in a props object) renders a `style` attribute,
// which the web Content-Security-Policy blocks once `style-src-attr 'unsafe-inline'` is gone. Not counted:
//   - `style={style}`, the caller's own style prop passed through (the app has none; ESLint forbids it);
//   - CSSOM writes (`el.style.left = ...`, `el.style.setProperty(...)`), which the policy allows.
//
// Usage: node web/scripts/kit-style-emitters.mjs   (prints JSON: { total, perFile, files }; exit code 1 when total > 0)
// The render-side guard is web/tests/tooling/kit-no-style-attributes.test.tsx; this script only reads source.
import fs from "node:fs";
import path from "node:path";
import process from "node:process";
import { fileURLToPath } from "node:url";

const STYLE_OBJECT = /\bstyle\s*=\s*\{\s*\{|\bstyle\s*:\s*\{/g;

function stripComments(source) {
  return source.replace(/\/\*[\s\S]*?\*\//g, "").replace(/(^|[^:"'`])\/\/.*$/gm, "$1");
}

/** Number of inline style object literals in a piece of source text. */
export function countStyleObjects(source) {
  return (stripComments(source).match(STYLE_OBJECT) ?? []).length;
}

function listJsx(dir) {
  const out = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) out.push(...listJsx(full));
    else if (entry.name.endsWith(".jsx")) out.push(full);
  }
  return out.sort();
}

/** Scans every .jsx file under `srcDir`. `perFile` lists only files with at least one emitter. */
export function scanKitStyleEmitters(srcDir) {
  const files = listJsx(srcDir).map((full) => path.relative(srcDir, full).split(path.sep).join("/"));
  const perFile = {};
  let total = 0;
  for (const rel of files) {
    const count = countStyleObjects(fs.readFileSync(path.join(srcDir, rel), "utf-8"));
    if (count > 0) {
      perFile[rel] = count;
      total += count;
    }
  }
  return { total, perFile, files };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const srcDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../app/ds/components/src");
  const { total, perFile, files } = scanKitStyleEmitters(srcDir);
  process.stdout.write(`${JSON.stringify({ total, files: files.length, perFile }, null, 2)}
`);
  process.exitCode = total === 0 ? 0 : 1;
}
