#!/usr/bin/env node
// Licence audit for the Markdown rendering dependencies (ADR 0032, tasks.md T082).
//
// Walks the resolved dependency tree on disk (node_modules, as pnpm laid it out from pnpm-lock.yaml) from react-markdown,
// remark-gfm and remark-breaks through every transitive "dependencies" and "optionalDependencies" entry, and fails when
// any package carries a licence outside ALLOWED. Peer dependencies are not followed: they are supplied by the app (react).
//
// How a package's licence is read:
//   - "license": "MIT"                     a single SPDX id
//   - "license": "(MIT OR Apache-2.0)"     an SPDX expression: OR passes if any alternative is allowed, AND needs all of them
//   - "license": { "type": "MIT" }         the old object form
//   - "licenses": [{ "type": "MIT" }, ...] the old array form, read as OR
//   - missing, empty, unparseable, "SEE LICENSE IN ...", "UNLICENSED", or any "X WITH exception": FAIL (never assumed safe)
//
// Usage: node scripts/licence-audit.mjs [--from <package dir, default web>] [--packages a,b,c]
// Exit code 0 = every package allowed, 1 = at least one package failed or could not be resolved.

import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const ALLOWED = new Set(["MIT", "ISC", "BSD-2-Clause", "BSD-3-Clause", "Apache-2.0", "0BSD"].map((id) => id.toLowerCase()));
const DEFAULT_ROOTS = ["react-markdown", "remark-gfm", "remark-breaks"];

// ---- SPDX expression evaluation -------------------------------------------------------------------------------------

function tokenize(expression) {
  const tokens = expression.match(/\(|\)|[^\s()]+/g);
  return tokens ?? [];
}

/** Returns true when the expression is satisfied using only allowed licences; throws on anything it cannot parse. */
function evaluateSpdx(expression) {
  const tokens = tokenize(expression);
  let position = 0;

  const peek = () => tokens[position];
  const next = () => tokens[position++];

  function parseOr() {
    let result = parseAnd();
    while (peek() === "OR") {
      next();
      const right = parseAnd();
      result = result || right;
    }
    return result;
  }

  function parseAnd() {
    let result = parseAtom();
    while (peek() === "AND") {
      next();
      const right = parseAtom();
      result = result && right;
    }
    return result;
  }

  function parseAtom() {
    const token = next();
    if (token === undefined) throw new Error("unexpected end of expression");
    if (token === "(") {
      const inner = parseOr();
      if (next() !== ")") throw new Error("missing closing parenthesis");
      return inner;
    }
    if (token === ")" || token === "AND" || token === "OR" || token === "WITH") throw new Error(`unexpected "${token}"`);
    if (peek() === "WITH") {
      next();
      if (next() === undefined) throw new Error("WITH needs an exception id");
      return false; // a licence with an exception is a different licence: not on the allowed list
    }
    return ALLOWED.has(token.toLowerCase());
  }

  const result = parseOr();
  if (position !== tokens.length) throw new Error(`unexpected "${tokens[position]}"`);
  return result;
}

/** { text, allowed } for a package.json: text is what is shown, allowed is the verdict. */
export function judgeLicence(pkg) {
  let expression;
  if (typeof pkg.license === "string") {
    expression = pkg.license;
  } else if (pkg.license && typeof pkg.license === "object" && typeof pkg.license.type === "string") {
    expression = pkg.license.type;
  } else if (Array.isArray(pkg.licenses) && pkg.licenses.length > 0) {
    const types = pkg.licenses.map((entry) => (typeof entry === "string" ? entry : entry?.type)).filter((t) => typeof t === "string");
    expression = types.length === pkg.licenses.length ? types.join(" OR ") : undefined;
  }

  if (expression === undefined || expression.trim() === "") {
    return { text: "(none)", allowed: false };
  }
  try {
    return { text: expression.trim(), allowed: evaluateSpdx(expression) };
  } catch {
    return { text: expression.trim(), allowed: false };
  }
}

// ---- resolving the tree on disk -------------------------------------------------------------------------------------

function readPackage(dir) {
  try {
    return JSON.parse(fs.readFileSync(path.join(dir, "package.json"), "utf8"));
  } catch {
    return null;
  }
}

/** Node's lookup: from a directory upwards, <dir>/node_modules/<name>. A pnpm package's own node_modules holds its siblings. */
function resolvePackage(fromDir, name) {
  let dir = fromDir;
  for (;;) {
    const candidate = path.basename(dir) === "node_modules" ? path.join(dir, name) : path.join(dir, "node_modules", name);
    if (fs.existsSync(path.join(candidate, "package.json"))) {
      return fs.realpathSync(candidate);
    }
    const parent = path.dirname(dir);
    if (parent === dir) return null;
    dir = parent;
  }
}

export function audit({ from, roots }) {
  const rows = [];
  const problems = [];
  const seen = new Set();
  const queue = [];

  for (const name of roots) {
    const dir = resolvePackage(from, name);
    if (dir === null) {
      problems.push({ name, text: "(not installed)", detail: "named root package not found" });
    } else {
      queue.push({ dir, requiredBy: null });
    }
  }

  while (queue.length > 0) {
    const { dir } = queue.shift();
    if (seen.has(dir)) continue;
    seen.add(dir);

    const pkg = readPackage(dir);
    if (pkg === null) {
      problems.push({ name: path.basename(dir), text: "(unreadable package.json)", detail: dir });
      continue;
    }

    const id = `${pkg.name ?? path.basename(dir)}@${pkg.version ?? "?"}`;
    const verdict = judgeLicence(pkg);
    rows.push({ id, text: verdict.text, allowed: verdict.allowed });

    for (const [field, optional] of [["dependencies", false], ["optionalDependencies", true]]) {
      for (const dependency of Object.keys(pkg[field] ?? {})) {
        const resolved = resolvePackage(dir, dependency);
        if (resolved !== null) {
          queue.push({ dir: resolved, requiredBy: id });
        } else if (!optional) {
          problems.push({ name: dependency, text: "(unresolved)", detail: `required by ${id}` });
        }
      }
    }
  }

  rows.sort((a, b) => a.id.localeCompare(b.id));
  return { rows, problems };
}

// ---- command line ---------------------------------------------------------------------------------------------------

function argument(name) {
  const index = process.argv.indexOf(name);
  return index >= 0 ? process.argv[index + 1] : undefined;
}

function main() {
  const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
  const from = path.resolve(argument("--from") ?? path.join(repoRoot, "web"));
  const roots = (argument("--packages") ?? DEFAULT_ROOTS.join(",")).split(",").map((n) => n.trim()).filter(Boolean);

  const { rows, problems } = audit({ from, roots });
  const failed = rows.filter((row) => !row.allowed);

  const width = Math.max(10, ...rows.map((row) => row.id.length)) + 2;
  const licenceWidth = Math.max(8, ...rows.map((row) => row.text.length)) + 2;
  console.log(`Licence audit from ${roots.join(", ")} (allowed: MIT, ISC, BSD-2-Clause, BSD-3-Clause, Apache-2.0, 0BSD)`);
  for (const row of rows) {
    console.log(`${row.id.padEnd(width)}${row.text.padEnd(licenceWidth)}${row.allowed ? "PASS" : "FAIL"}`);
  }
  for (const problem of problems) {
    console.log(`${problem.name.padEnd(width)}${problem.text.padEnd(licenceWidth)}FAIL  ${problem.detail}`);
  }

  const failures = failed.length + problems.length;
  console.log(`${rows.length} packages audited, ${failures} failing`);
  console.log(failures === 0 ? "PASS" : "FAIL");
  process.exitCode = failures === 0 ? 0 : 1;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main();
}
