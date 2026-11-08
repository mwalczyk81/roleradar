// Allure 3 configuration for the combined test report (feature 011, story 2b).
//
// One report per publishing run, built by the final always-running job of ci.yml (main) and nightly-quality.yml from
// results the existing jobs already produced: xUnit v3 JUnit files (a plain -junit workflow step), Vitest and Playwright
// Allure results. Inputs are sanitized first (scripts/allure-sanitize.mjs); only sanitized output reaches this config.
//
// History lives in a CI cache, never in the repository: the workflows restore history/history.jsonl from the Actions
// cache before generating and save the updated file after, so no commit is made on a normal run. (Why a cache and not
// the gh-pages branch: every update would add a new multi-megabyte version of the file to that branch's history.)
//
// historyLimit caps the history file at the most recent N runs, one JSON line per run. Measured in the T038 probe:
// about 0.6 MB per run for 659 tests. The real suite is about 2,600 tests, so about 2.4 MB per run and at most about
// 70 MB at the limit of 30, roughly a month of nightlies. Busy days on main shorten that window, since every publishing
// run adds one line. Re-measure from the first real runs and adjust (research.md D10).

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
