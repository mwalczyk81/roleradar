// Sanitizes Allure result folders before anything is published (feature 011, story 2b, FR-028, FR-029).
//
// Rules, in one place:
//  - Real-stack results (lane "real-stack") lose every attachment. Playwright nests attachments inside result steps, so
//    the walk is recursive over every object and array at any depth, not just the top-level attachments list. The
//    referenced files are never copied. Only pass or fail, names and timings remain.
//  - Fictional results keep screenshots and plain-text attachments (also nested ones). Trace archives, video and any
//    other type are dropped: traces stay in the 7 or 14 day CI artifact and never reach the published site.
//  - Every lane: the host label is removed, one lane label is set, and every string has absolute paths (Windows, POSIX,
//    UNC, file URLs) replaced by "[path]/<file name>" and user and host names redacted. Text attachments are scrubbed
//    the same way. Repository-relative paths and public URLs are left alone.
//  - The input folder is never modified. The output folder must be new or empty and must not sit inside the input.
//
// CLI:
//   node scripts/allure-sanitize.mjs --input <dir> --output <dir> --lane fictional|real-stack [--redact <name>]...
//   node scripts/allure-sanitize.mjs --check <dir> [--redact <name>]...   (exit 1 when a path, user or host is found)
// A leak is reported by file and rule, never by the matched text, because CI logs of a public repository are public.

import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import process from "node:process";
import { fileURLToPath } from "node:url";

const PATH_TOKEN = "[path]";
const REDACTED_TOKEN = "[redacted]";
const LANES = new Set(["fictional", "real-stack"]);
const KEPT_ATTACHMENT_TYPES = new Set(["image/png", "image/jpeg", "image/gif", "image/webp", "text/plain", "text/markdown", "application/json"]);
const TEXT_ATTACHMENT_TYPES = new Set(["text/plain", "text/markdown", "application/json"]);
const SIDE_FILES = new Set(["environment.properties", "categories.json", "executor.json"]);
const POSIX_ROOTS = "home|Users|root|tmp|var|opt|mnt|usr|etc|srv|workspace|__w|github|runner";
const LEAK_FILE_PATTERN = /\.(json|jsonl|md|txt|properties|csv|log|xml)$/i;

/** @param {unknown} value @returns {value is Record<string, unknown>} */
function isRecord(value) {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

/** @param {string} value */
function escapeRegExp(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

/** The account and machine this process runs on, redacted by default. @returns {string[]} */
function defaultRedactions() {
  const names = [];
  try {
    names.push(os.userInfo().username);
  } catch {
    // No account information available; nothing to add.
  }
  names.push(os.hostname());
  return names.filter((name) => typeof name === "string" && name.length >= 3);
}

/**
 * @param {string[]} extra
 * @returns {RegExp | null}
 */
function redactionPattern(extra) {
  const names = [...new Set([...defaultRedactions(), ...extra].filter((name) => name.length >= 2))];
  if (names.length === 0) {
    return null;
  }
  return new RegExp(`(?<![A-Za-z0-9_])(?:${names.map(escapeRegExp).join("|")})(?![A-Za-z0-9_])`, "gi");
}

/**
 * Replaces absolute paths with "[path]/<file name>" and redacts user and host names.
 * @param {string} text
 * @param {{ redact?: string[] }} [options]
 * @returns {string}
 */
export function scrubText(text, options = {}) {
  let result = String(text);
  // file:///C:/Users/x/y.ts and file:///home/x/y.ts
  result = result.replace(/file:\/\/\/?(?:[A-Za-z]:)?(?:[\\/][^\\/\s"'<>()|:]+)*[\\/]([^\\/\s"'<>()|:]*)/gi, (_m, name) => `${PATH_TOKEN}/${name}`);
  // UNC: \\server\share\folder\file
  result = result.replace(/\\\\[^\s\\/"'<>|]+(?:\\[^\s\\/"'<>|:]+)+/g, (match) => `${PATH_TOKEN}/${match.split("\\").pop() ?? ""}`);
  // Windows drive paths, either slash style.
  result = result.replace(/(?<![A-Za-z0-9])[A-Za-z]:[\\/]+(?:[^\\/\s"'<>|:*?]+[\\/]+)*([^\\/\s"'<>|:*?]*)/g, (_m, name) => `${PATH_TOKEN}/${name}`);
  // POSIX absolute paths under well-known roots, not part of a URL or a longer path. Greedy, so the whole path goes and
  // the last segment is kept as the file name.
  const posix = new RegExp(String.raw`(?<![A-Za-z0-9_.:/\\-])/(?:${POSIX_ROOTS})(?:/[^\s"'<>():/]+)*/?`, "g");
  result = result.replace(posix, (match) => `${PATH_TOKEN}/${match.split("/").filter(Boolean).pop() ?? ""}`);
  const names = redactionPattern(options.redact ?? []);
  if (names) {
    result = result.replace(names, REDACTED_TOKEN);
  }
  return result;
}

/**
 * Scrubs every string in a parsed JSON value, keys untouched.
 * @param {unknown} value
 * @param {{ redact?: string[] }} options
 * @returns {unknown}
 */
function scrubValue(value, options) {
  if (typeof value === "string") {
    return scrubText(value, options);
  }
  if (Array.isArray(value)) {
    return value.map((item) => scrubValue(item, options));
  }
  if (isRecord(value)) {
    /** @type {Record<string, unknown>} */
    const copy = {};
    for (const [key, child] of Object.entries(value)) {
      copy[key] = scrubValue(child, options);
    }
    return copy;
  }
  return value;
}

/**
 * Walks every object and array at any depth and filters each "attachments" list, so attachments nested in steps (or in
 * a container's befores and afters) are handled the same as top-level ones.
 * @param {unknown} value
 * @param {Lane} lane
 * @param {Set<string>} keep sources to copy
 * @param {{ dropped: number }} stats
 * @returns {unknown}
 */
function filterAttachments(value, lane, keep, stats) {
  if (Array.isArray(value)) {
    return value.map((item) => filterAttachments(item, lane, keep, stats));
  }
  if (!isRecord(value)) {
    return value;
  }
  /** @type {Record<string, unknown>} */
  const copy = {};
  for (const [key, child] of Object.entries(value)) {
    if (key === "attachments" && Array.isArray(child)) {
      copy[key] = child.filter((attachment) => {
        const type = isRecord(attachment) && typeof attachment["type"] === "string" ? attachment["type"] : "";
        const source = isRecord(attachment) && typeof attachment["source"] === "string" ? attachment["source"] : "";
        const keepIt = lane === "fictional" && source !== "" && KEPT_ATTACHMENT_TYPES.has(type);
        if (keepIt) {
          keep.add(source);
        } else {
          stats.dropped += 1;
        }
        return keepIt;
      });
    } else {
      copy[key] = filterAttachments(child, lane, keep, stats);
    }
  }
  return copy;
}

/**
 * Removes the host label and sets exactly one lane label.
 * @param {Record<string, unknown>} parsed
 * @param {Lane} lane
 */
function normalizeLabels(parsed, lane) {
  const labels = Array.isArray(parsed["labels"]) ? parsed["labels"] : [];
  const kept = labels.filter((label) => !(isRecord(label) && (label["name"] === "host" || label["name"] === "lane")));
  kept.push({ name: "lane", value: lane });
  parsed["labels"] = kept;
}

/** @typedef {"fictional" | "real-stack"} Lane */

/**
 * @param {{ inputDir: string, outputDir: string, lane: Lane, redact?: string[] }} options
 * @returns {Promise<{ results: number, attachmentsKept: number, attachmentsDropped: number }>}
 */
export async function sanitizeResults({ inputDir, outputDir, lane, redact = [] }) {
  if (!LANES.has(lane)) {
    throw new Error(`lane must be fictional or real-stack, got '${String(lane)}'`);
  }
  const input = path.resolve(inputDir);
  const output = path.resolve(outputDir);
  if (!fs.existsSync(input) || !fs.statSync(input).isDirectory()) {
    throw new Error(`input folder not found: ${inputDir}`);
  }
  const relation = path.relative(input, output);
  if (relation === "" || (!relation.startsWith("..") && !path.isAbsolute(relation))) {
    throw new Error("output folder must not be the input folder or inside it");
  }
  if (fs.existsSync(output) && fs.readdirSync(output).length > 0) {
    throw new Error(`output folder is not empty: ${outputDir}`);
  }
  fs.mkdirSync(output, { recursive: true });

  const options = { redact };
  const keep = new Set();
  const stats = { dropped: 0 };
  let results = 0;

  for (const name of fs.readdirSync(input).sort()) {
    const source = path.join(input, name);
    if (!fs.statSync(source).isFile()) {
      continue;
    }
    if (name.endsWith("-result.json") || name.endsWith("-container.json")) {
      const parsed = JSON.parse(fs.readFileSync(source, "utf8"));
      const filtered = filterAttachments(parsed, lane, keep, stats);
      const scrubbed = scrubValue(filtered, options);
      if (isRecord(scrubbed) && name.endsWith("-result.json")) {
        normalizeLabels(scrubbed, lane);
        results += 1;
      }
      fs.writeFileSync(path.join(output, name), `${JSON.stringify(scrubbed)}\n`);
    } else if (name.endsWith(".xml")) {
      // JUnit from the xUnit v3 test executables: the test assembly is named by absolute path and the machine name is
      // recorded in a hostname attribute. Both go, every test case and its outcome stays.
      const xml = fs.readFileSync(source, "utf8").replace(/\bhostname="[^"]*"/g, `hostname="${REDACTED_TOKEN}"`);
      fs.writeFileSync(path.join(output, name), scrubText(xml, options));
    } else if (SIDE_FILES.has(name)) {
      fs.writeFileSync(path.join(output, name), scrubText(fs.readFileSync(source, "utf8"), options));
    }
  }

  let kept = 0;
  for (const sourceName of keep) {
    const safeName = path.basename(sourceName);
    const attachment = path.join(input, safeName);
    if (!fs.existsSync(attachment)) {
      continue;
    }
    const type = TEXT_ATTACHMENT_TYPES.has(attachmentType(safeName)) ? "text" : "binary";
    if (type === "text") {
      fs.writeFileSync(path.join(output, safeName), scrubText(fs.readFileSync(attachment, "utf8"), options));
    } else {
      fs.copyFileSync(attachment, path.join(output, safeName));
    }
    kept += 1;
  }
  return { results, attachmentsKept: kept, attachmentsDropped: stats.dropped };
}

/** @param {string} fileName @returns {string} */
function attachmentType(fileName) {
  const extension = path.extname(fileName).toLowerCase();
  if (extension === ".txt" || extension === ".log") return "text/plain";
  if (extension === ".md") return "text/markdown";
  if (extension === ".json") return "application/json";
  return "application/octet-stream";
}

// Rules for text that is not parsed as JSON (plain text, logs, and a .json file that is not valid JSON). A path may appear
// with doubled backslashes there, so the Windows rule allows one to four. The UNC rule needs exactly two leading
// backslashes, a host and one more backslash (or the escaped form: four, a host, two), so text that only contains
// backslash-n sequences, such as "a\nfoo\nb" or "\\n\\nfoo", is not read as a path.
const TEXT_LEAK_RULES = [
  ["windows-path", /(?<![A-Za-z0-9])[A-Za-z]:(?:\\{1,4}|\/)[A-Za-z0-9_.$]/],
  ["posix-home-path", /(?<![A-Za-z0-9_.:/-])\/(?:home|Users|root)\/[A-Za-z0-9_.-]/],
  ["unc-path", /(?<![\\A-Za-z0-9])(?:\\{2}([A-Za-z0-9_.-]+)\\[A-Za-z0-9_.$-]|\\{4}([A-Za-z0-9_.-]+)\\{2}[A-Za-z0-9_.$-])/g],
];

// Rules for decoded string values of a parsed JSON file. After JSON.parse a path has single backslashes, so the Windows
// rule allows one or two (a path quoted inside another string) and a UNC path starts with exactly two. A backslash-n in
// a step name (the source text of an expect call) or a real line break is never a path.
const DECODED_LEAK_RULES = [
  ["windows-path", /(?<![A-Za-z0-9])[A-Za-z]:(?:\\{1,2}|\/)[A-Za-z0-9_.$]/],
  ["posix-home-path", /(?<![A-Za-z0-9_.:/-])\/(?:home|Users|root)\/[A-Za-z0-9_.-]/],
  ["unc-path", /(?<![\\A-Za-z0-9])\\{2}([A-Za-z0-9_.-]+)\\[A-Za-z0-9_.$-]/g],
];

// A host under a reserved name (RFC 2606: .example, .test, .invalid, .localhost) can never be a real machine, so a UNC-shaped
// string naming one (a hostile fictional input in a test title) is not a leak.
const RESERVED_HOST = /(?:^|\.)(?:example|test|invalid|localhost)$/i;

/**
 * Whether a rule matches the text. The UNC rule captures the host and ignores reserved fictional ones.
 * @param {string} rule
 * @param {RegExp} pattern
 * @param {string} text
 */
function ruleMatches(rule, pattern, text) {
  if (rule !== "unc-path") {
    return pattern.test(text);
  }
  for (const match of text.matchAll(pattern)) {
    const host = match[1] ?? match[2] ?? "";
    if (!RESERVED_HOST.test(host)) {
      return true;
    }
  }
  return false;
}

/**
 * Every string value and key of a parsed JSON document, at any depth (steps, statusDetails, parameters, labels).
 * @param {unknown} node
 * @returns {Generator<string>}
 */
function* decodedStrings(node) {
  if (typeof node === "string") {
    yield node;
  } else if (Array.isArray(node)) {
    for (const item of node) {
      yield* decodedStrings(item);
    }
  } else if (node !== null && typeof node === "object") {
    for (const [key, value] of Object.entries(node)) {
      yield key;
      yield* decodedStrings(value);
    }
  }
}

/**
 * The names of the rules that match the file's text. A .json file that parses is checked as decoded values; anything
 * else as raw text.
 * @param {string} file
 * @param {string} text
 * @returns {string[]}
 */
function matchingRules(file, text) {
  if (/\.json$/i.test(file)) {
    try {
      const strings = [...decodedStrings(JSON.parse(text))];
      return DECODED_LEAK_RULES.filter(([rule, pattern]) => strings.some((value) => ruleMatches(rule, pattern, value))).map(([rule]) => rule);
    } catch {
      // Not valid JSON: fall through to the raw text rules.
    }
  }
  return TEXT_LEAK_RULES.filter(([rule, pattern]) => ruleMatches(rule, pattern, text)).map(([rule]) => rule);
}

/**
 * @param {string} dir
 * @returns {string[]}
 */
function listFiles(dir) {
  const found = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      found.push(...listFiles(full));
    } else {
      found.push(full);
    }
  }
  return found;
}

/**
 * Scans the text files of a folder for absolute paths and the given names. Binary files are skipped. The result names
 * the file and the rule only, never the matched text.
 * @param {string} dir
 * @param {{ redact?: string[] }} [options]
 * @returns {Array<{ file: string, rule: string }>}
 */
export function findLeaks(dir, options = {}) {
  const names = redactionPattern(options.redact ?? []);
  const leaks = [];
  for (const file of listFiles(dir)) {
    if (!LEAK_FILE_PATTERN.test(file)) {
      continue;
    }
    const text = fs.readFileSync(file, "utf8");
    const relative = path.relative(dir, file).split(path.sep).join("/");
    for (const rule of matchingRules(file, text)) {
      leaks.push({ file: relative, rule });
    }
    if (names && new RegExp(names.source, names.flags.replace("g", "")).test(text)) {
      leaks.push({ file: relative, rule: "redacted-name" });
    }
  }
  return leaks;
}

/** @param {string[]} argv */
function parseArguments(argv) {
  /** @type {{ input?: string, output?: string, lane?: string, check?: string, redact: string[] }} */
  const parsed = { redact: [] };
  for (let index = 0; index < argv.length; index += 1) {
    const flag = argv[index];
    const value = argv[index + 1];
    if (flag === "--redact" && value !== undefined) {
      parsed.redact.push(value);
      index += 1;
    } else if ((flag === "--input" || flag === "--output" || flag === "--lane" || flag === "--check") && value !== undefined) {
      parsed[flag.slice(2)] = value;
      index += 1;
    } else {
      throw new Error(`unknown or incomplete argument: ${String(flag)}`);
    }
  }
  return parsed;
}

async function main() {
  let args;
  try {
    args = parseArguments(process.argv.slice(2));
  } catch (error) {
    process.stderr.write(`${error instanceof Error ? error.message : String(error)}\n`);
    return 2;
  }
  if (args.check !== undefined) {
    if (!fs.existsSync(args.check)) {
      process.stderr.write(`folder not found: ${args.check}\n`);
      return 2;
    }
    const leaks = findLeaks(args.check, { redact: args.redact });
    for (const leak of leaks.slice(0, 20)) {
      process.stdout.write(`leak: ${leak.rule} in ${leak.file}\n`);
    }
    if (leaks.length > 0) {
      process.stdout.write(`${leaks.length} leak(s) found; the matched text is not printed.\n`);
      return 1;
    }
    process.stdout.write("no absolute paths, user names or host names found\n");
    return 0;
  }
  if (args.input === undefined || args.output === undefined || args.lane === undefined) {
    process.stderr.write("usage: --input <dir> --output <dir> --lane fictional|real-stack [--redact <name>]... | --check <dir>\n");
    return 2;
  }
  try {
    const stats = await sanitizeResults({ inputDir: args.input, outputDir: args.output, lane: /** @type {Lane} */ (args.lane), redact: args.redact });
    process.stdout.write(`sanitized ${stats.results} result(s); attachments kept ${stats.attachmentsKept}, dropped ${stats.attachmentsDropped}\n`);
    return 0;
  } catch (error) {
    process.stderr.write(`${error instanceof Error ? error.message : String(error)}\n`);
    return 2;
  }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exitCode = await main();
}
