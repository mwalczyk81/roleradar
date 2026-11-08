// Mutation-score ratchet and report (feature 011, FR-007, FR-011). CI-only logic, tested by Vitest
// (tests/tooling/mutation-ratchet.test.ts). It replaces scripts/check-mutation-minimum.ps1 and write-mutation-summary.ps1.
//
// Two modes.
//  Score:   --project <name> --report <Stryker mutation.json>. Counts mutants by status, writes a Markdown table (stdout, and
//           --summary-path or GITHUB_STEP_SUMMARY when set) and <output-directory>/mutation-score.json, then requires the
//           score to be at or above the committed minimum for that project in quality/mutation-minimums.json.
//           The score follows Stryker's rule: (killed + timeout) / (killed + timeout + survived + no coverage) * 100, one
//           decimal. Compile errors, runtime errors, ignored and pending mutants are not scorable. With no scorable mutant
//           there is no score (shown as unavailable, never 100) and the check never passes by default.
//  Base ref: --base-ref <ref>. The committed minimums may only rise relative to that ref; lowering or removing a project
//           fails. A file that does not exist at the ref (first introduction) passes.
//
// Exit codes: 0 pass, 1 below the minimum or a minimum lowered or removed, 2 input missing or unreadable or no score.
// The minimums file is only ever changed by a reviewed commit; nothing here writes it. This script never reads or prints
// any secret; the dashboard key is only ever referenced by the workflow's single publish step.

import fs from "node:fs";
import path from "node:path";
import process from "node:process";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const MINIMUMS_RELATIVE = "quality/mutation-minimums.json";
const COUNTED_STATUSES = ["Killed", "Survived", "NoCoverage", "Timeout", "Ignored"];

/** @typedef {{ exitCode: number, output: string }} CheckResult */

class InputError extends Error {}

/**
 * Counts mutants by status and applies Stryker's score rule.
 * @param {{ files?: Record<string, { mutants?: Array<{ status?: string }> }> }} report
 * @returns {{ counts: Record<string, number>, score: number | null }}
 */
export function scoreReport(report) {
  /** @type {Record<string, number>} */
  const counts = Object.fromEntries(COUNTED_STATUSES.map((status) => [status, 0]));
  for (const file of Object.values(report.files ?? {})) {
    for (const mutant of file.mutants ?? []) {
      const status = String(mutant.status);
      if (status in counts) {
        counts[status] = (counts[status] ?? 0) + 1;
      }
    }
  }
  const detected = (counts["Killed"] ?? 0) + (counts["Timeout"] ?? 0);
  const scorable = detected + (counts["Survived"] ?? 0) + (counts["NoCoverage"] ?? 0);
  const score = scorable > 0 ? Math.round((100 * detected * 10) / scorable) / 10 : null;
  return { counts, score };
}

/**
 * @param {string} text
 * @param {string} origin
 * @returns {Record<string, number>}
 */
function readMinimums(text, origin) {
  let parsed;
  try {
    parsed = JSON.parse(text);
  } catch {
    throw new InputError(`Cannot read minimums from ${origin}: not valid JSON.`);
  }
  const minimums = parsed?.minimums;
  if (typeof minimums !== "object" || minimums === null || Array.isArray(minimums)) {
    throw new InputError(`Cannot read minimums from ${origin}: no minimums object.`);
  }
  /** @type {Record<string, number>} */
  const table = {};
  for (const [name, value] of Object.entries(minimums)) {
    if (typeof value !== "number" || !Number.isFinite(value)) {
      throw new InputError(`Cannot read minimums from ${origin}: ${name} is not a number.`);
    }
    table[name] = value;
  }
  return table;
}

/** A project name is the only text from outside that reaches the table; keep it from breaking the cell. */
function markdownCell(text) {
  return String(text)
    .replace(/[\r\n]+/g, " ")
    .replaceAll("\\", "\\\\")
    .replaceAll("|", "\\|")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;");
}

/**
 * Score mode: report, write, then compare with the committed floor.
 * @param {{ repositoryRoot: string, project?: string, reportFile?: string, outputDirectory?: string, summaryPath?: string,
 *           runId?: string, commit?: string, dashboardSkipped?: boolean, env?: NodeJS.ProcessEnv }} options
 * @returns {CheckResult}
 */
export function runScoreCheck(options) {
  const { repositoryRoot, project, reportFile, outputDirectory, summaryPath, runId = "", commit = "", dashboardSkipped = false, env = process.env } = options;
  try {
    if (!project || !reportFile) {
      throw new InputError("Pass either --base-ref <ref>, or --project <name> with --report <file>.");
    }
    const minimumsPath = path.join(repositoryRoot, MINIMUMS_RELATIVE);
    if (!fs.existsSync(minimumsPath)) {
      throw new InputError(`Minimums file not found: ${minimumsPath}`);
    }
    if (!fs.existsSync(reportFile)) {
      const message = `No Stryker report was produced: ${reportFile} does not exist, so there is no score to check. Stryker did not finish; read the "Run StrykerJS" step above this one for the reason (a failed initial test run writes no report).`;
      const target = summaryPath ?? env["GITHUB_STEP_SUMMARY"];
      if (target) {
        fs.appendFileSync(target, `### Mutation testing\n\n${message}\n`);
      }
      throw new InputError(message);
    }
    const minimums = readMinimums(fs.readFileSync(minimumsPath, "utf8"), MINIMUMS_RELATIVE);
    const minimum = minimums[project];
    if (minimum === undefined) {
      throw new InputError(`No committed minimum for project '${project}' in ${MINIMUMS_RELATIVE}.`);
    }
    let report;
    try {
      report = JSON.parse(fs.readFileSync(reportFile, "utf8"));
    } catch {
      throw new InputError(`ReportFile is not valid JSON: ${reportFile}`);
    }
    if (typeof report?.files !== "object" || report.files === null) {
      throw new InputError(`ReportFile has no files object: ${reportFile}`);
    }

    const { counts, score } = scoreReport(report);

    if (outputDirectory) {
      /** @type {Record<string, unknown>} */
      const record = { project };
      if (score !== null) {
        record["score"] = score;
      }
      Object.assign(record, {
        killed: counts["Killed"],
        survived: counts["Survived"],
        noCoverage: counts["NoCoverage"],
        timeout: counts["Timeout"],
        ignored: counts["Ignored"],
        runId,
        commit,
        runDate: new Date().toISOString().replace(/\.\d{3}Z$/, "Z"),
      });
      fs.mkdirSync(outputDirectory, { recursive: true });
      fs.writeFileSync(path.join(outputDirectory, "mutation-score.json"), `${JSON.stringify(record, null, 2)}\n`);
    }

    const lines = [
      "### Mutation testing",
      "",
      "| Project | Score | Killed | Survived | No coverage | Timeout | Ignored |",
      "|---|---|---|---|---|---|---|",
      `| ${markdownCell(project)} | ${score === null ? "unavailable" : score.toFixed(1)} | ${counts["Killed"]} | ${counts["Survived"]} | ${counts["NoCoverage"]} | ${counts["Timeout"]} | ${counts["Ignored"]} |`,
    ];
    if (score === null) {
      lines.push("", "No score available: the report has no killed, timed-out, survived or uncovered mutants (unavailable, never a default score).");
    }
    if (dashboardSkipped) {
      lines.push("", "Stryker Dashboard publish skipped: secret not configured.");
    }
    lines.push("");
    const table = `${lines.join("\n")}\n`;
    const target = summaryPath ?? env["GITHUB_STEP_SUMMARY"];
    if (target) {
      fs.appendFileSync(target, table);
    }

    if (score === null) {
      return { exitCode: 2, output: `${table}Project '${project}' has no score (zero scorable mutants). A missing score never passes.\n` };
    }
    const line = `${project}: score ${score.toFixed(1)} minimum ${minimum.toFixed(0)}`;
    if (score < minimum) {
      return { exitCode: 1, output: `${table}FAIL ${line} (below the committed minimum)\n` };
    }
    return { exitCode: 0, output: `${table}PASS ${line}\n` };
  } catch (error) {
    if (error instanceof InputError) {
      return { exitCode: 2, output: `${error.message}\n` };
    }
    throw error;
  }
}

/**
 * Base ref mode: the committed minimums may only rise.
 * @param {{ repositoryRoot: string, baseRef: string }} options
 * @returns {CheckResult}
 */
export function runBaseRefCheck({ repositoryRoot, baseRef }) {
  try {
    const minimumsPath = path.join(repositoryRoot, MINIMUMS_RELATIVE);
    if (!fs.existsSync(minimumsPath)) {
      throw new InputError(`Minimums file not found: ${minimumsPath}`);
    }
    const current = readMinimums(fs.readFileSync(minimumsPath, "utf8"), MINIMUMS_RELATIVE);

    const verify = spawnSync("git", ["-C", repositoryRoot, "rev-parse", "--verify", "--quiet", `${baseRef}^{commit}`], { encoding: "utf8" });
    if (verify.status !== 0) {
      throw new InputError(`Base ref not found: ${baseRef}`);
    }
    const show = spawnSync("git", ["-C", repositoryRoot, "show", `${baseRef}:${MINIMUMS_RELATIVE}`], { encoding: "utf8" });
    if (show.status !== 0) {
      return { exitCode: 0, output: `No ${MINIMUMS_RELATIVE} at ${baseRef} (first introduction): nothing to compare, pass.\n` };
    }
    const base = readMinimums(show.stdout, `${baseRef}:${MINIMUMS_RELATIVE}`);

    /** @type {string[]} */
    const failures = [];
    for (const [name, baseValue] of Object.entries(base)) {
      const currentValue = current[name];
      if (currentValue === undefined) {
        failures.push(`${name}: removed (minimum at ${baseRef} was ${baseValue.toFixed(0)})`);
      } else if (currentValue < baseValue) {
        failures.push(`${name}: lowered from ${baseValue.toFixed(0)} to ${currentValue.toFixed(0)}`);
      }
    }
    if (failures.length > 0) {
      return { exitCode: 1, output: `${failures.map((failure) => `FAIL ${failure}`).join("\n")}\nA mutation minimum may only rise. Lowering or removing one is rejected.\n` };
    }
    return { exitCode: 0, output: `Mutation minimums are unchanged or raised relative to ${baseRef}.\n` };
  } catch (error) {
    if (error instanceof InputError) {
      return { exitCode: 2, output: `${error.message}\n` };
    }
    throw error;
  }
}

/** @param {string[]} argv */
function parseArguments(argv) {
  /** @type {Record<string, string | boolean>} */
  const values = {};
  for (let index = 0; index < argv.length; index += 1) {
    const argument = argv[index] ?? "";
    if (!argument.startsWith("--")) {
      throw new Error(`unexpected argument: ${argument}`);
    }
    const name = argument.slice(2);
    if (name === "dashboard-skipped") {
      values[name] = true;
      continue;
    }
    const value = argv[index + 1];
    if (value === undefined) {
      throw new Error(`missing value for ${argument}`);
    }
    values[name] = value;
    index += 1;
  }
  return values;
}

/** @param {string[]} argv */
export function main(argv) {
  let args;
  try {
    args = parseArguments(argv);
  } catch (error) {
    process.stderr.write(`${error instanceof Error ? error.message : String(error)}\n`);
    return 2;
  }
  const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
  const repositoryRoot = typeof args["repository-root"] === "string" ? args["repository-root"] : path.resolve(scriptDirectory, "..", "..");
  const text = (/** @type {string} */ name) => (typeof args[name] === "string" ? /** @type {string} */ (args[name]) : undefined);

  const result =
    text("base-ref") !== undefined
      ? runBaseRefCheck({ repositoryRoot, baseRef: /** @type {string} */ (text("base-ref")) })
      : runScoreCheck({
          repositoryRoot,
          project: text("project"),
          reportFile: text("report"),
          outputDirectory: text("output-directory"),
          summaryPath: text("summary-path"),
          runId: text("run-id"),
          commit: text("commit"),
          dashboardSkipped: args["dashboard-skipped"] === true,
        });
  process.stdout.write(result.output);
  return result.exitCode;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exitCode = main(process.argv.slice(2));
}
