import { afterEach, describe, expect, it } from "vitest";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { checkCombinedReport } from "../../scripts/check-combined-report.mjs";

// Feature 011, story 2b (FR-024, FR-025). The .NET and Vitest results reach the combined Allure report as JUnit files
// (one per project: xUnit v3's native -junit output, and Vitest's own junit reporter writing Vitest.xml), Playwright as Allure
// result files. The check fails,
// naming the source, when a required source is missing, empty or absent from the merged results, and when the generated
// report counts fewer tests than were fed in. A missing .NET file also covers a test executable that ran but wrote no
// JUnit file. Fixtures are generated per test and fictional.

const REQUIRED = "Unit,Architecture,Contract,Integration,Vitest,Playwright";
// Inputs counted: 3 + 2 + 1 + 4 = 10 JUnit test cases, 2 Vitest, and 2 unique Playwright tests (one is retried) = 14.
const EXPECTED_INPUTS = 14;
const created: string[] = [];

afterEach(() => {
  for (const dir of created.splice(0)) {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

function junit(testCases: number, project = "Fixture"): string {
  const cases = Array.from({ length: testCases }, (_, index) => `<testcase name="${project}.Test${index}" classname="${project}" time="0.1" />`).join("");
  return `<testsuites name="Test results"><testsuite name="[path]/Tests.dll" hostname="[redacted]">${cases}</testsuite></testsuites>`;
}

function allureResult(id: string, framework: string, historyId = `h-${id}`): string {
  return JSON.stringify({ uuid: id, historyId, name: "t", status: "passed", labels: [{ name: "framework", value: framework }] });
}

/** A complete merged results folder. */
function completeFolder(): string {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "rr-combined-"));
  created.push(root);
  const results = path.join(root, "results");
  fs.mkdirSync(results);
  const put = (name: string, content: string) => fs.writeFileSync(path.join(results, name), content);
  put("Unit.xml", junit(3, "Unit"));
  put("Architecture.xml", junit(2, "Architecture"));
  put("Contract.xml", junit(1, "Contract"));
  put("Integration.xml", junit(4, "Integration"));
  put("Vitest.xml", junit(2, "Vitest"));
  put("p1-result.json", allureResult("p1", "playwright"));
  put("p1-retry-result.json", allureResult("p1retry", "playwright", "h-p1"));
  put("p2-result.json", allureResult("p2", "playwright"));
  return root;
}

const check = (root: string, options: { required?: string; reportDirectory?: string } = {}) =>
  checkCombinedReport({ resultsDirectory: path.join(root, "results"), requiredSources: options.required ?? REQUIRED, reportDirectory: options.reportDirectory });

function reportWithTotal(root: string, total: number): string {
  const report = path.join(root, "report");
  fs.mkdirSync(report, { recursive: true });
  fs.writeFileSync(path.join(report, "summary.json"), JSON.stringify({ stats: { total } }));
  return report;
}

describe("checkCombinedReport", () => {
  it("passes when every source is present and shows each count", () => {
    const result = check(completeFolder());

    expect(result.exitCode).toBe(0);
    expect(result.output).toContain("| Unit | 3 |");
    expect(result.output).toContain("| Integration | 4 |");
    expect(result.output).toContain("| Vitest | 2 |");
    expect(result.output).toContain("| Playwright | 2 |");
  });

  it.each(["Unit", "Architecture", "Contract", "Integration", "Vitest"])("fails, naming %s, when its JUnit file is missing (the executable did not run or wrote no file)", (source) => {
    const root = completeFolder();
    fs.rmSync(path.join(root, "results", `${source}.xml`));

    const result = check(root);

    expect(result.exitCode).toBe(1);
    expect(result.output).toContain(`MISSING ${source}`);
    expect(result.output).toContain("wrote no JUnit file");
  });

  it("fails, naming the source, when a JUnit file is empty", () => {
    const root = completeFolder();
    fs.writeFileSync(path.join(root, "results", "Contract.xml"), "");

    const result = check(root);

    expect(result.exitCode).toBe(1);
    expect(result.output).toContain("EMPTY Contract");
  });

  it("fails, naming the source, when a JUnit file has no test cases", () => {
    const root = completeFolder();
    fs.writeFileSync(path.join(root, "results", "Architecture.xml"), junit(0));

    const result = check(root);

    expect(result.exitCode).toBe(1);
    expect(result.output).toContain("EMPTY Architecture");
  });

  it.each([["Playwright", ["p1-result.json", "p2-result.json", "p1-retry-result.json"]]] as const)("fails, naming %s, when the stack is absent from the merged results", (source, files) => {
    const root = completeFolder();
    for (const file of files) {
      fs.rmSync(path.join(root, "results", file));
    }

    const result = check(root);

    expect(result.exitCode).toBe(1);
    expect(result.output).toContain(`MISSING ${source}`);
  });

  it("fails on an unknown required source instead of ignoring it", () => {
    const result = check(completeFolder(), { required: `${REQUIRED},Typo` });

    expect(result.exitCode).toBe(1);
    expect(result.output).toContain("MISSING Typo");
  });

  it("fails when the report total is smaller than the inputs, showing both numbers", () => {
    const root = completeFolder();

    const result = check(root, { reportDirectory: reportWithTotal(root, EXPECTED_INPUTS - 1) });

    expect(result.exitCode).toBe(1);
    expect(result.output).toContain("report counts 13 tests but the inputs hold 14");
  });

  it.each([EXPECTED_INPUTS, EXPECTED_INPUTS + 5])("passes when the report total is %i, at least the inputs", (total) => {
    const root = completeFolder();

    expect(check(root, { reportDirectory: reportWithTotal(root, total) }).exitCode).toBe(0);
  });

  it("counts a repeated JUnit name once, as the report does (xUnit v3 gives every row of a theory the same name)", () => {
    const root = completeFolder();
    const row = '<testcase name="Fixture.Theory" classname="Fixture" time="0.1" />';
    const other = '<testcase name="Fixture.Other" classname="Fixture" time="0.1" />';
    fs.writeFileSync(path.join(root, "results", "Unit.xml"), `<testsuites><testsuite>${row}${row}${row}${other}</testsuite></testsuites>`);
    // Unit now holds 4 cases but 2 distinct names, so the inputs are 14 - 3 + 2 = 13 and the report folds the rows into 13 too.
    const result = check(root, { reportDirectory: reportWithTotal(root, EXPECTED_INPUTS - 1) });

    expect(result.exitCode).toBe(0);
    expect(result.output).toContain("| Unit | 2 |");
    expect(result.output).toContain("Report total 13, inputs 13");
  });

  it("does not hide a lost test behind repeated names: two distinct names still need two report entries", () => {
    const root = completeFolder();
    const row = '<testcase name="Fixture.Theory" classname="Fixture" time="0.1" />';
    const other = '<testcase name="Fixture.Other" classname="Fixture" time="0.1" />';
    fs.writeFileSync(path.join(root, "results", "Unit.xml"), `<testsuites><testsuite>${row}${row}${row}${other}</testsuite></testsuites>`);

    const result = check(root, { reportDirectory: reportWithTotal(root, EXPECTED_INPUTS - 2) });

    expect(result.exitCode).toBe(1);
    expect(result.output).toContain("report counts 12 tests but the inputs hold 13");
  });

  it("fails when the report has no summary.json", () => {
    const root = completeFolder();

    const result = check(root, { reportDirectory: path.join(root, "report") });

    expect(result.exitCode).toBe(1);
    expect(result.output).toContain("summary.json");
  });

  it("exits 2 when the results folder does not exist", () => {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), "rr-combined-"));
    created.push(root);

    expect(check(root).exitCode).toBe(2);
  });
});
