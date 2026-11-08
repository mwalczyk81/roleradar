// Fails when a required test source is missing from the combined Allure report (feature 011, FR-024, FR-025). CI-only
// logic, tested by Vitest (tests/unit/check-combined-report.test.ts). It replaces scripts/check-combined-report.ps1.
//
// The merged results folder that feeds `allure generate` holds one JUnit file per .NET test project (<Name>.xml, written by
// a plain `-junit` workflow step) and Allure result files from Vitest and Playwright (*-result.json with a framework
// label). For every required source it checks that the input exists, is not empty and contains at least one test, and
// prints a table of counts. A source can never drop out silently: a missing, empty or absent source fails the check and
// is named. A missing .NET file also covers a test executable that ran but wrote no JUnit file.
//
// With --report-directory it also reads the generated report's summary.json and fails when the report counts fewer tests
// than the inputs hold. Retries of one Playwright test share a historyId and count once.
//
// CLI: node scripts/check-combined-report.mjs --results-directory <dir> --required Unit,Architecture,Vitest,... [--report-directory <dir>]
// Exit codes: 0 all sources present, 1 a source is missing or empty or the report total is too small, 2 the results folder
// is missing. The table is also appended to GITHUB_STEP_SUMMARY when that is set.

import fs from "node:fs";
import path from "node:path";
import process from "node:process";
import { fileURLToPath } from "node:url";

const STACK_FRAMEWORKS = new Map([
  ["vitest", "vitest"],
  ["playwright", "playwright"],
]);

/** @param {string} directory */
function allureResults(directory) {
  return fs
    .readdirSync(directory)
    .filter((name) => name.endsWith("-result.json"))
    .map((name) => {
      try {
        return { name, parsed: JSON.parse(fs.readFileSync(path.join(directory, name), "utf8")) };
      } catch {
        return null;
      }
    })
    .filter((entry) => entry !== null);
}

/**
 * @param {{ resultsDirectory: string, requiredSources: string | string[], reportDirectory?: string }} options
 * @returns {{ exitCode: number, output: string }}
 */
export function checkCombinedReport({ resultsDirectory, requiredSources, reportDirectory }) {
  if (!fs.existsSync(resultsDirectory) || !fs.statSync(resultsDirectory).isDirectory()) {
    return { exitCode: 2, output: `Results directory not found: ${resultsDirectory}\n` };
  }
  const sources = (Array.isArray(requiredSources) ? requiredSources : [requiredSources])
    .flatMap((entry) => entry.split(","))
    .map((entry) => entry.trim())
    .filter(Boolean);

  /** @type {string[]} */
  const failures = [];
  /** @type {string[]} */
  const rows = [];
  let inputTotal = 0;
  const results = allureResults(resultsDirectory);

  for (const source of sources) {
    const framework = STACK_FRAMEWORKS.get(source.toLowerCase());
    if (framework !== undefined) {
      const ids = new Set();
      for (const { name, parsed } of results) {
        const labels = Array.isArray(parsed?.labels) ? parsed.labels : [];
        if (labels.some((label) => label?.name === "framework" && label?.value === framework)) {
          ids.add(String(parsed.historyId || parsed.uuid || name));
        }
      }
      if (ids.size === 0) {
        failures.push(`MISSING ${source}: no ${framework} results in the merged results folder`);
        rows.push(`| ${source} | missing |`);
      } else {
        inputTotal += ids.size;
        rows.push(`| ${source} | ${ids.size} |`);
      }
      continue;
    }

    const junitPath = path.join(resultsDirectory, `${source}.xml`);
    if (!fs.existsSync(junitPath)) {
      failures.push(`MISSING ${source}: ${source}.xml not found in the merged results folder (the test executable did not run or wrote no JUnit file)`);
      rows.push(`| ${source} | missing |`);
      continue;
    }
    const count = (fs.readFileSync(junitPath, "utf8").match(/<testcase[\s/>]/g) ?? []).length;
    if (count === 0) {
      failures.push(`EMPTY ${source}: ${source}.xml holds no test cases`);
      rows.push(`| ${source} | empty |`);
      continue;
    }
    inputTotal += count;
    rows.push(`| ${source} | ${count} |`);
  }

  const lines = ["### Combined report inputs", "", "| Source | Tests |", "|---|---:|", ...rows, ""];

  if (reportDirectory !== undefined) {
    const summaryPath = path.join(reportDirectory, "summary.json");
    if (!fs.existsSync(summaryPath)) {
      failures.push(`summary.json not found in the report folder: ${reportDirectory}`);
    } else {
      try {
        const total = JSON.parse(fs.readFileSync(summaryPath, "utf8"))?.stats?.total;
        if (typeof total !== "number") {
          failures.push("summary.json has no stats.total");
        } else if (total < inputTotal) {
          failures.push(`report counts ${total} tests but the inputs hold ${inputTotal}`);
        } else {
          lines.push(`Report total ${total}, inputs ${inputTotal}.`, "");
        }
      } catch {
        failures.push("summary.json is not valid JSON");
      }
    }
  }

  for (const failure of failures) {
    lines.push(`- FAIL: ${failure}`);
  }
  const output = `${lines.join("\n")}\n`;
  const stepSummary = process.env["GITHUB_STEP_SUMMARY"];
  if (stepSummary) {
    fs.appendFileSync(stepSummary, output);
  }
  return { exitCode: failures.length > 0 ? 1 : 0, output };
}

/** @param {string[]} argv */
export function main(argv) {
  /** @type {Record<string, string>} */
  const values = {};
  for (let index = 0; index < argv.length; index += 2) {
    const name = (argv[index] ?? "").replace(/^--/, "");
    const value = argv[index + 1];
    if (value === undefined) {
      process.stderr.write(`missing value for --${name}\n`);
      return 2;
    }
    values[name] = value;
  }
  if (!values["results-directory"] || !values["required"]) {
    process.stderr.write("usage: --results-directory <dir> --required <Source,Source,...> [--report-directory <dir>]\n");
    return 2;
  }
  const result = checkCombinedReport({ resultsDirectory: values["results-directory"], requiredSources: values["required"], reportDirectory: values["report-directory"] });
  process.stdout.write(result.output);
  return result.exitCode;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exitCode = main(process.argv.slice(2));
}
