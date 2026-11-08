// Allure 3 configuration for the combined test report (feature 011, story 2b).
//
// One report per publishing run, built by the final always-running job of ci.yml (main) and nightly-quality.yml from
// results the existing jobs already produced: JUnit files for the xUnit v3 projects (a plain -junit workflow step) and for
// Vitest (its own junit reporter, Vitest.xml), and Allure results for Playwright. Inputs are sanitized first
// (scripts/allure-sanitize.mjs); only sanitized output reaches this config.
//
// History is one file, allure-history/history.jsonl, in the gh-pages tree: the report job reads it from the branch before
// generating and uploads the updated file, and the publish job puts it into the site that is published as one commit
// (so it never accumulates in the branch history). It is served with the site.
//
// historyLimit caps the history file at the most recent N runs, one JSON line per run. Measured on the real suite
// (2,146 tests) with the artifacts of a validation run: 1.86 MB per run, so about 56 MB at the limit of 30, roughly a
// month of nightlies. Busy days on main shorten that window, since every publishing run adds one line (research.md D16).
//
// The count cap alone is not enough: the nightly's repeated Playwright runs make its line about 4.0 MB, so 30 runs could
// pass GitHub's 100 MB file limit. The publish jobs therefore also trim the staged file to 20 MiB, oldest runs first
// (scripts/site-limits.mjs, research.md D18), so the effective window is about 5 nightly-sized to 10 CI-sized runs.

import process from "node:process";
import { defineConfig } from "allure";

export default defineConfig({
  name: "RoleRadar tests",
  output: process.env.ALLURE_REPORT_DIR ?? "./allure-report",
  historyPath: process.env.ALLURE_HISTORY_PATH ?? "./allure-history/history.jsonl",
  historyLimit: 30,
  plugins: {
    awesome: {
      options: {
        singleFile: false,
        reportName: "RoleRadar tests",
      },
    },
  },
});
