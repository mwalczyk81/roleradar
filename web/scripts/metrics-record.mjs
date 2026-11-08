// Run summary and metrics record (feature 011, FR-013, FR-014, FR-017). CI-only logic, tested by Vitest
// (tests/tooling/metrics-record.test.ts). It replaces scripts/build-run-summary.ps1 and scripts/append-metrics-record.ps1.
//
//  summary  Reads the run's own outputs (TRX files for test counts per lane, ReportGenerator's Summary.json for coverage),
//           prints Markdown tables to stdout and appends them to GITHUB_STEP_SUMMARY, and writes a normalized
//           run-summary.json for the metrics record. A missing or unparsable input shows as "unavailable" in its cell and
//           the command exits 1; it never substitutes a default, a remembered value or a previous run. All text read from
//           reports is escaped before it is written into Markdown.
//  record   Builds one metrics record (one line of JSON, shape in specs/011-test-quality-reporting/contracts/metrics-record.md)
//           from the run summary, the run's job list (gh run view --json jobs) and optionally the latest nightly
//           mutation-score.json, and writes the existing metrics.jsonl plus that line to --output (a separate file, so the
//           deploy step only copies it). No record is written if any number cannot be read, and a commit that is already
//           recorded is not recorded twice. Nothing here uses the network or publishes.
//
// CLI:
//   node scripts/metrics-record.mjs summary [--trx-directory <dir>] [--coverage-summary <Summary.json>] [--json-output <file>]
//   node scripts/metrics-record.mjs record --commit <sha> --summary <run-summary.json> --jobs <jobs.json> --output <file>
//                                          [--existing <metrics.jsonl>] [--mutation <mutation-score.json>] [--timestamp <iso>]
// Exit codes: 0 done, 1 an input is unavailable (nothing written), 2 usage error.

import fs from "node:fs";
import path from "node:path";
import process from "node:process";
import { fileURLToPath } from "node:url";

const UNAVAILABLE = "unavailable";

class UnavailableError extends Error {}

/** Escapes text read from a report before it is written into a Markdown table cell. */
export function escapeMarkdown(value) {
  return String(value)
    .replaceAll("\\", "\\\\")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll("|", "\\|")
    .replaceAll("`", "\\`")
    .replaceAll("[", "\\[")
    .replaceAll("]", "\\]")
    .replace(/[\r\n]+/g, " ");
}

function decodeXml(text) {
  return text
    .replace(/&#x([0-9a-fA-F]+);/g, (_, hex) => String.fromCodePoint(Number.parseInt(hex, 16)))
    .replace(/&#(\d+);/g, (_, decimal) => String.fromCodePoint(Number.parseInt(decimal, 10)))
    .replaceAll("&lt;", "<")
    .replaceAll("&gt;", ">")
    .replaceAll("&quot;", '"')
    .replaceAll("&apos;", "'")
    .replaceAll("&amp;", "&");
}

/** @param {string} tag the attribute text of one element */
function attributesOf(tag) {
  /** @type {Record<string, string>} */
  const attributes = {};
  for (const match of tag.matchAll(/([A-Za-z_][\w:.-]*)\s*=\s*"([^"]*)"/g)) {
    attributes[/** @type {string} */ (match[1])] = decodeXml(/** @type {string} */ (match[2]));
  }
  return attributes;
}

function firstAttributes(text, element) {
  const match = new RegExp(`<${element}\\b([^>]*)>`, "i").exec(text);
  return match ? attributesOf(/** @type {string} */ (match[1])) : null;
}

function laneName(assemblyPath) {
  const match = /([^\\/]+)\.dll$/i.exec(assemblyPath ?? "");
  if (!match) {
    return null;
  }
  const assembly = /** @type {string} */ (match[1]);
  return assembly.startsWith("RoleRadar.Tests.") ? assembly.slice("RoleRadar.Tests.".length) : assembly;
}

function percent(value) {
  return typeof value === "number" && Number.isFinite(value) ? Math.round(value * 10) / 10 : null;
}

/**
 * Test counts per lane from every .trx file in a folder. Returns null and adds problems when anything is unreadable.
 * @param {string} directory
 * @param {string[]} problems
 */
function readTrxDirectory(directory, problems) {
  if (!fs.existsSync(directory) || !fs.statSync(directory).isDirectory()) {
    problems.push(`TrxDirectory: directory not found: ${directory}`);
    return null;
  }
  const files = fs.readdirSync(directory).filter((name) => name.endsWith(".trx")).sort();
  if (files.length === 0) {
    problems.push(`TrxDirectory: no .trx files in ${directory}`);
    return null;
  }
  /** @type {Record<string, { passed: number, failed: number, skipped: number }>} */
  const lanes = {};
  /** @type {Record<string, string>} */
  const sources = {};
  let ok = true;
  for (const file of files) {
    const text = fs.readFileSync(path.join(directory, file), "utf8");
    if (!/<TestRun\b/i.test(text) || !/<\/TestRun>\s*$/i.test(text)) {
      problems.push(`TrxDirectory: ${file} is not valid XML`);
      ok = false;
      continue;
    }
    const counters = firstAttributes(text, "Counters");
    if (counters === null) {
      problems.push(`TrxDirectory: ${file} has no Counters element`);
      ok = false;
      continue;
    }
    const lane = laneName(firstAttributes(text, "TestMethod")?.["codeBase"]) ?? laneName(firstAttributes(text, "UnitTest")?.["storage"]);
    if (lane === null) {
      problems.push(`TrxDirectory: ${file} names no test assembly, so its lane is unknown`);
      ok = false;
      continue;
    }
    if (lane in lanes) {
      problems.push(`TrxDirectory: duplicate lane '${lane}' in ${file} and ${sources[lane]}; refusing to double count`);
      ok = false;
      continue;
    }
    lanes[lane] = {
      passed: Number.parseInt(counters["passed"] ?? "", 10),
      failed: Number.parseInt(counters["failed"] ?? "", 10),
      skipped: counters["notExecuted"] ? Number.parseInt(counters["notExecuted"], 10) : 0,
    };
    sources[lane] = file;
    if (Object.values(lanes[lane] ?? {}).some((count) => Number.isNaN(count))) {
      problems.push(`TrxDirectory: ${file} has a Counters element without numeric passed and failed counts`);
      ok = false;
    }
  }
  return ok ? lanes : null;
}

/**
 * Coverage from ReportGenerator's JsonSummary (Summary.json). Returns null and adds problems when a figure is missing.
 * @param {string} file
 * @param {string[]} problems
 */
function readCoverageSummary(file, problems) {
  if (!fs.existsSync(file)) {
    problems.push(`CoverageSummary: file not found: ${file}`);
    return null;
  }
  let parsed;
  try {
    parsed = JSON.parse(fs.readFileSync(file, "utf8"));
  } catch {
    problems.push(`CoverageSummary: ${file} is not valid JSON`);
    return null;
  }
  const assemblies = parsed?.coverage?.assemblies;
  if (!Array.isArray(assemblies) || assemblies.length === 0) {
    problems.push(`CoverageSummary: ${file} has no assemblies`);
    return null;
  }
  const overallLinePct = percent(parsed?.summary?.linecoverage);
  const overallBranchPct = percent(parsed?.summary?.branchcoverage);
  if (overallLinePct === null) {
    problems.push("CoverageSummary: summary.linecoverage (overallLinePct) is unavailable");
    return null;
  }
  /** @type {Record<string, { linePct: number | null, branchPct: number | null }>} */
  const projects = {};
  for (const assembly of assemblies) {
    projects[String(assembly?.name)] = { linePct: percent(assembly?.coverage), branchPct: percent(assembly?.branchcoverage) };
  }
  return { overallLinePct, overallBranchPct, projects };
}

const cell = (value, digits = 0) => (value === null || value === undefined ? UNAVAILABLE : Number(value).toFixed(digits));

/**
 * Tables and the normalized summary for the inputs given.
 * @param {{ trxDirectory?: string, coverageSummaryFile?: string }} inputs
 * @returns {{ markdown: string, summary: Record<string, unknown> | null, problems: string[] }}
 */
export function buildRunSummary({ trxDirectory, coverageSummaryFile }) {
  /** @type {string[]} */
  const problems = [];
  if (trxDirectory === undefined && coverageSummaryFile === undefined) {
    return { markdown: "", summary: null, problems: ["No inputs supplied: pass --trx-directory and/or --coverage-summary."] };
  }
  const tests = trxDirectory === undefined ? undefined : readTrxDirectory(trxDirectory, problems);
  const coverage = coverageSummaryFile === undefined ? undefined : readCoverageSummary(coverageSummaryFile, problems);

  const lines = ["## Run summary", ""];
  if (tests !== undefined) {
    lines.push("### Tests per lane", "", "| Lane | Passed | Failed | Skipped | Total |", "|---|---:|---:|---:|---:|");
    if (tests === null) {
      lines.push(`| ${UNAVAILABLE} | ${UNAVAILABLE} | ${UNAVAILABLE} | ${UNAVAILABLE} | ${UNAVAILABLE} |`);
    } else {
      for (const lane of Object.keys(tests).sort((a, b) => a.localeCompare(b))) {
        const counts = /** @type {{ passed: number, failed: number, skipped: number }} */ (tests[lane]);
        lines.push(`| ${escapeMarkdown(lane)} | ${cell(counts.passed)} | ${cell(counts.failed)} | ${cell(counts.skipped)} | ${cell(counts.passed + counts.failed + counts.skipped)} |`);
      }
    }
    lines.push("");
  }
  if (coverage !== undefined) {
    lines.push("### Coverage", "", "| Project | Line % | Branch % |", "|---|---:|---:|");
    if (coverage === null) {
      lines.push(`| ${UNAVAILABLE} | ${UNAVAILABLE} | ${UNAVAILABLE} |`);
    } else {
      for (const [name, project] of Object.entries(coverage.projects)) {
        if (project.linePct === null || project.branchPct === null) {
          problems.push(`CoverageSummary: ${name} has an unavailable line or branch figure`);
        }
        lines.push(`| ${escapeMarkdown(name)} | ${cell(project.linePct, 1)} | ${cell(project.branchPct, 1)} |`);
      }
      if (coverage.overallBranchPct === null) {
        problems.push("CoverageSummary: summary.branchcoverage is unavailable");
      }
      lines.push(`| Overall | ${cell(coverage.overallLinePct, 1)} | ${cell(coverage.overallBranchPct, 1)} |`);
    }
    lines.push("");
  }
  if (problems.length > 0) {
    lines.push("Unavailable values:", "", ...problems.map((problem) => `- ${escapeMarkdown(problem)}`), "");
  }

  /** @type {Record<string, unknown>} */
  const summary = { schemaVersion: 1 };
  if (tests !== undefined) {
    summary["tests"] = tests;
  }
  if (coverage !== undefined) {
    summary["coverage"] = coverage;
  }
  return { markdown: lines.join("\n"), summary, problems };
}

/**
 * Whole seconds per completed job, from `gh run view <id> --json jobs`. Jobs that have not finished are left out.
 * @param {{ jobs?: Array<{ name?: string, status?: string, startedAt?: string | null, completedAt?: string | null }> }} document
 * @returns {Record<string, number>}
 */
export function durationsFromJobs(document) {
  /** @type {Record<string, number>} */
  const durations = {};
  for (const job of document?.jobs ?? []) {
    if (job.status !== "completed" || !job.name || !job.startedAt || !job.completedAt) {
      continue;
    }
    const seconds = (Date.parse(job.completedAt) - Date.parse(job.startedAt)) / 1000;
    if (Number.isFinite(seconds) && seconds >= 0) {
      durations[job.name] = Math.round(seconds);
    }
  }
  return durations;
}

const present = (value) => value !== null && value !== undefined;

/**
 * One metrics record. Throws when any number cannot be read; nothing is ever defaulted to zero.
 * @param {{ commit: string, timestampUtc?: string, summary: Record<string, any>, durations: Record<string, number | null>,
 *           mutation?: { project?: string, score?: number, runDate?: string, commit?: string } | null }} input
 * @returns {Record<string, unknown>}
 */
export function buildRecord({ commit, timestampUtc, summary, durations, mutation }) {
  const fail = (/** @type {string} */ reason) => {
    throw new UnavailableError(`Metrics record not written: ${reason}`);
  };
  if (!/^[0-9a-f]{40}$/.test(commit)) {
    fail(`Commit must be 40 lowercase hex characters, got '${commit}'`);
  }
  const timestamp = timestampUtc ?? new Date().toISOString().replace(/\.\d{3}Z$/, "Z");

  const summaryTests = summary?.tests;
  if (!summaryTests || Object.keys(summaryTests).length === 0) {
    fail("tests is missing or empty in the summary");
  }
  /** @type {Record<string, Record<string, number>>} */
  const tests = {};
  for (const [lane, counts] of Object.entries(summaryTests)) {
    tests[lane] = {};
    for (const field of ["passed", "failed", "skipped"]) {
      const value = /** @type {Record<string, unknown>} */ (counts)?.[field];
      if (!present(value)) {
        fail(`tests.${lane}.${field} is unavailable`);
      }
      /** @type {Record<string, number>} */ (tests[lane])[field] = /** @type {number} */ (value);
    }
  }

  const overall = summary?.coverage?.overallLinePct;
  if (!present(overall)) {
    fail("coverage.overallLinePct (overallLinePct) is unavailable in the summary");
  }
  /** @type {Record<string, number>} */
  const projects = {};
  for (const [name, project] of Object.entries(summary?.coverage?.projects ?? {})) {
    const linePct = /** @type {Record<string, unknown>} */ (project)?.["linePct"];
    if (!present(linePct)) {
      fail(`coverage.projects.${name}.linePct is unavailable`);
    }
    projects[name] = /** @type {number} */ (linePct);
  }

  if (!durations || Object.keys(durations).length === 0) {
    fail("no job durations in the durations input");
  }
  for (const [job, seconds] of Object.entries(durations)) {
    if (!present(seconds)) {
      fail(`duration for job '${job}' is unavailable`);
    }
  }

  /** @type {Record<string, unknown>} */
  const record = {
    schemaVersion: 1,
    commit,
    timestampUtc: timestamp,
    tests,
    coverage: { overallLinePct: overall, projects },
    jobDurationsSeconds: durations,
  };
  if (mutation && present(mutation.score)) {
    if (!mutation.project) {
      fail("mutation file has no project");
    }
    if (!mutation.runDate || !mutation.commit) {
      fail("mutation file must carry its own runDate and commit");
    }
    record["mutation"] = { [/** @type {string} */ (mutation.project)]: mutation.score, runDate: mutation.runDate, commit: mutation.commit };
  }
  return record;
}

/**
 * Adds the record line to the existing history unless that commit is already in it.
 * @param {string} existingText
 * @param {string} recordLine
 * @param {string} commit
 * @returns {{ text: string, appended: boolean }}
 */
export function appendRecord(existingText, recordLine, commit) {
  for (const line of existingText.split("\n")) {
    if (line.trim() === "") {
      continue;
    }
    try {
      if (JSON.parse(line)?.commit === commit) {
        return { text: existingText, appended: false };
      }
    } catch {
      // An unparsable line is kept as it is and ignored for the duplicate check.
    }
  }
  const prefix = existingText.length > 0 && !existingText.endsWith("\n") ? "\n" : "";
  return { text: `${existingText}${prefix}${recordLine}\n`, appended: true };
}

const figure = (value, digits = 0) => (typeof value === "number" && Number.isFinite(value) ? value.toFixed(digits) : UNAVAILABLE);

export const PR_COMMENT_MARKER = "<!-- roleradar-ci-report -->";

/** An https URL without whitespace or markup characters, or undefined. */
function safeUrl(value) {
  return typeof value === "string" && /^https:\/\/[^\s<>()"'`\\]+$/.test(value) ? value : undefined;
}

const shortSha = (value) => (typeof value === "string" && /^[0-9a-f]{40}$/.test(value) ? value.slice(0, 7) : undefined);

/**
 * The body of the sticky pull request comment. Built only from the run summary (aggregate counts and percentages), an
 * optional mutation score file and a few URLs. Every value is escaped; unknown fields are never copied.
 * @param {{ summary: Record<string, any> | null, mutation?: { project?: string, score?: number, runDate?: string, commit?: string } | null,
 *           commit?: string, runUrl?: string, artifactsUrl?: string, pagesUrl?: string }} input
 * @returns {string}
 */
export function buildPrComment({ summary, mutation, commit, runUrl, artifactsUrl, pagesUrl }) {
  const lines = [PR_COMMENT_MARKER, "## CI report", ""];
  const sha = shortSha(commit);
  if (sha) {
    lines.push(`Commit \`${sha}\``, "");
  }

  const tests = summary?.tests;
  if (!summary || !tests || typeof tests !== "object") {
    lines.push("Run summary: not available for this run (the backend lanes did not produce one).", "");
  } else {
    lines.push("### Tests per lane", "", "| Lane | Passed | Failed | Skipped | Total |", "|---|---:|---:|---:|---:|");
    for (const lane of Object.keys(tests).sort((a, b) => a.localeCompare(b))) {
      const counts = tests[lane] ?? {};
      const numbers = ["passed", "failed", "skipped"].map((field) => counts[field]);
      const total = numbers.every((n) => typeof n === "number") ? numbers.reduce((a, b) => a + b, 0) : null;
      lines.push(`| ${escapeMarkdown(lane)} | ${[...numbers, total].map((n) => figure(n)).join(" | ")} |`);
    }
    lines.push("");
    const coverage = summary.coverage;
    if (coverage && typeof coverage === "object") {
      lines.push("### Coverage", "", "| Project | Line % | Branch % |", "|---|---:|---:|");
      for (const [name, project] of Object.entries(coverage.projects ?? {})) {
        lines.push(`| ${escapeMarkdown(name)} | ${figure(project?.linePct, 1)} | ${figure(project?.branchPct, 1)} |`);
      }
      lines.push(`| Overall | ${figure(coverage.overallLinePct, 1)} | ${figure(coverage.overallBranchPct, 1)} |`, "");
    }
  }

  const date = typeof mutation?.runDate === "string" && /^\d{4}-\d{2}-\d{2}/.test(mutation.runDate) ? mutation.runDate.slice(0, 10) : undefined;
  if (mutation && typeof mutation.score === "number" && Number.isFinite(mutation.score) && mutation.project && date && shortSha(mutation.commit)) {
    lines.push(`Mutation score: ${escapeMarkdown(mutation.project)} ${(Math.round(mutation.score * 10) / 10).toFixed(1)}% (latest nightly, ${date}, commit \`${shortSha(mutation.commit)}\`)`, "");
  } else {
    lines.push("Mutation score: not available yet (it comes from the latest nightly run).", "");
  }

  const links = [["Run", runUrl], ["Artifacts", artifactsUrl], ["Pages report (latest from main)", pagesUrl]]
    .map(([label, url]) => [label, safeUrl(url)])
    .filter(([, url]) => url !== undefined)
    .map(([label, url]) => `[${label}](${url})`);
  if (links.length > 0) {
    lines.push(links.join(" | "), "");
  }
  return `${lines.join("\n").trimEnd()}\n`;
}

/** @param {string[]} argv */
function parseArguments(argv) {
  /** @type {Record<string, string>} */
  const values = {};
  for (let index = 0; index < argv.length; index += 2) {
    const name = (argv[index] ?? "").replace(/^--/, "");
    const value = argv[index + 1];
    if (value === undefined) {
      throw new Error(`missing value for --${name}`);
    }
    values[name] = value;
  }
  return values;
}

function readJson(file, label) {
  if (!fs.existsSync(file)) {
    throw new UnavailableError(`Metrics record not written: ${label} file not found: ${file}`);
  }
  try {
    return JSON.parse(fs.readFileSync(file, "utf8"));
  } catch {
    throw new UnavailableError(`Metrics record not written: ${label} file is not valid JSON: ${file}`);
  }
}

/** @param {string[]} argv */
export function main(argv) {
  const [command, ...rest] = argv;
  let args;
  try {
    args = parseArguments(rest);
  } catch (error) {
    process.stderr.write(`${error instanceof Error ? error.message : String(error)}\n`);
    return 2;
  }

  if (command === "summary") {
    const result = buildRunSummary({ trxDirectory: args["trx-directory"], coverageSummaryFile: args["coverage-summary"] });
    process.stdout.write(`${result.markdown}\n`);
    const stepSummary = process.env["GITHUB_STEP_SUMMARY"];
    if (stepSummary && result.markdown) {
      fs.appendFileSync(stepSummary, `${result.markdown}\n`);
    }
    if (result.problems.length > 0) {
      return result.summary === null ? 2 : 1;
    }
    if (args["json-output"]) {
      fs.mkdirSync(path.dirname(args["json-output"]), { recursive: true });
      fs.writeFileSync(args["json-output"], `${JSON.stringify(result.summary, null, 2)}\n`);
    }
    return 0;
  }

  if (command === "record") {
    if (!args["commit"] || !args["summary"] || !args["jobs"] || !args["output"]) {
      process.stderr.write("usage: record --commit <sha> --summary <file> --jobs <file> --output <file> [--existing <file>] [--mutation <file>] [--timestamp <iso>]\n");
      return 2;
    }
    try {
      const record = buildRecord({
        commit: args["commit"],
        timestampUtc: args["timestamp"],
        summary: readJson(args["summary"], "Summary"),
        durations: durationsFromJobs(readJson(args["jobs"], "Jobs")),
        mutation: args["mutation"] ? readJson(args["mutation"], "Mutation") : null,
      });
      const existing = args["existing"] && fs.existsSync(args["existing"]) ? fs.readFileSync(args["existing"], "utf8") : "";
      const result = appendRecord(existing, JSON.stringify(record), args["commit"]);
      fs.mkdirSync(path.dirname(args["output"]), { recursive: true });
      fs.writeFileSync(args["output"], result.text);
      process.stdout.write(result.appended ? `Metrics record written for ${args["commit"]}.\n` : `A record for ${args["commit"]} already exists; not appending.\n`);
      return 0;
    } catch (error) {
      if (error instanceof UnavailableError) {
        process.stdout.write(`${error.message}\n`);
        return 1;
      }
      throw error;
    }
  }

  if (command === "comment") {
    if (!args["output"]) {
      process.stderr.write("usage: comment --output <file> [--summary <file>] [--mutation <file>] [--commit <sha>] [--run-url <url>] [--artifacts-url <url>] [--pages-url <url>]\n");
      return 2;
    }
    // A missing or unreadable summary is not an error here: a run in which the backend lanes did not run has none,
    // and the comment says so. The body is built from these inputs only, never from result files.
    const readOptional = (/** @type {string | undefined} */ file) => {
      if (!file || !fs.existsSync(file)) return null;
      try {
        return JSON.parse(fs.readFileSync(file, "utf8"));
      } catch {
        return null;
      }
    };
    const body = buildPrComment({
      summary: readOptional(args["summary"]),
      mutation: readOptional(args["mutation"]),
      commit: args["commit"],
      runUrl: args["run-url"],
      artifactsUrl: args["artifacts-url"],
      pagesUrl: args["pages-url"],
    });
    fs.mkdirSync(path.dirname(args["output"]), { recursive: true });
    fs.writeFileSync(args["output"], body);
    return 0;
  }

  process.stderr.write("usage: metrics-record.mjs summary|record|comment ...\n");
  return 2;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exitCode = main(process.argv.slice(2));
}
