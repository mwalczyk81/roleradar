import { afterEach, describe, expect, it } from "vitest";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { appendRecord, buildRecord, buildRunSummary, durationsFromJobs, escapeMarkdown } from "../../scripts/metrics-record.mjs";

// Feature 011, story 2 (FR-013, FR-014, FR-017). One tool turns a run's own outputs into (1) the run-summary tables and a
// normalized run-summary.json, and (2) one metrics record appended to the existing metrics.jsonl. Rules kept from the
// PowerShell scripts it replaces: a number that cannot be read is shown as "unavailable" and fails the step, never
// replaced by a default, a remembered value or a previous run; text read from reports is escaped before it reaches
// Markdown; the record is never written when a value is missing. Fixtures are fictional.

const SCRIPT = path.resolve(process.cwd(), "scripts", "metrics-record.mjs");
const FIXTURES = path.resolve(process.cwd(), "tests", "fixtures", "run-summary");
const COMMIT_A = "a".repeat(40);
const COMMIT_B = "b".repeat(40);
const created: string[] = [];

afterEach(() => {
  for (const dir of created.splice(0)) {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

function tempDir(): string {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "rr-metrics-"));
  created.push(dir);
  return dir;
}

function fixture(...parts: string[]): string {
  return path.join(FIXTURES, ...parts);
}

/** The committed `*.trx.fixture` files, copied to a temp folder under their real `.trx` name. */
function trxFolder(name: string): string {
  const dir = tempDir();
  for (const file of fs.readdirSync(fixture(name))) {
    fs.copyFileSync(fixture(name, file), path.join(dir, file.replace(/\.fixture$/, "")));
  }
  return dir;
}

function write(dir: string, name: string, content: string): string {
  const full = path.join(dir, name);
  fs.mkdirSync(path.dirname(full), { recursive: true });
  fs.writeFileSync(full, content);
  return full;
}

const summaryFixture = (): Record<string, unknown> => JSON.parse(fs.readFileSync(fixture("run-summary.json"), "utf8")) as Record<string, unknown>;

describe("buildRunSummary", () => {
  it("renders the tests and coverage tables with values from the fixtures", () => {
    const result = buildRunSummary({ trxDirectory: trxFolder("trx"), coverageSummaryFile: fixture("Summary.json") });

    expect(result.problems).toEqual([]);
    expect(result.markdown).toContain("| Lane | Passed | Failed | Skipped | Total |");
    expect(result.markdown).toContain("| Contract | 5 | 0 | 0 | 5 |");
    expect(result.markdown).toContain("| Unit | 9 | 2 | 1 | 12 |");
    expect(result.markdown).toContain("| Project | Line % | Branch % |");
    expect(result.markdown).toContain("| RoleRadar.Domain | 95.0 | 70.0 |");
    expect(result.markdown).toContain("| RoleRadar.Application | 85.0 | 90.0 |");
    expect(result.markdown).toContain("| Overall | 90.0 | 80.0 |");
    expect(result.markdown).not.toContain("unavailable");
  });

  it("normalizes the same numbers into the run summary", () => {
    const { summary } = buildRunSummary({ trxDirectory: trxFolder("trx"), coverageSummaryFile: fixture("Summary.json") });

    expect(summary).toEqual(summaryFixture());
  });

  it("shows unavailable and fails, inventing nothing, when the TRX directory is missing", () => {
    const result = buildRunSummary({ trxDirectory: path.join(tempDir(), "no-such-folder") });

    expect(result.problems.join("\n")).toContain("TrxDirectory");
    expect(result.markdown).toContain("unavailable");
    expect(result.markdown).not.toContain("| Unit |");
  });

  it("names the file of a malformed TRX", () => {
    const result = buildRunSummary({ trxDirectory: trxFolder("trx-malformed") });

    expect(result.problems.join("\n")).toContain("broken.trx");
    expect(result.markdown).toContain("unavailable");
  });

  it("refuses to double count a lane found in two files", () => {
    const result = buildRunSummary({ trxDirectory: trxFolder("trx-duplicate") });

    expect(result.problems.join("\n")).toMatch(/duplicate lane/i);
  });

  it("shows unavailable when the coverage summary has no assemblies or no overall line figure", () => {
    for (const name of ["Summary-no-assemblies.json", "Summary-null-line.json"]) {
      const result = buildRunSummary({ coverageSummaryFile: fixture(name) });

      expect(result.problems.join("\n"), name).toContain("CoverageSummary");
      expect(result.markdown, name).toContain("unavailable");
    }
  });

  it("shows unavailable when the coverage summary file is missing or not JSON", () => {
    const dir = tempDir();
    expect(buildRunSummary({ coverageSummaryFile: path.join(dir, "missing.json") }).problems.join("\n")).toContain("CoverageSummary");
    expect(buildRunSummary({ coverageSummaryFile: write(dir, "bad.json", "{ not json") }).problems.join("\n")).toContain("CoverageSummary");
  });

  it("escapes a hostile lane name before it reaches Markdown", () => {
    const result = buildRunSummary({ trxDirectory: trxFolder("trx-hostile") });

    expect(result.problems).toEqual([]);
    expect(result.markdown).not.toContain("<b>");
    expect(result.markdown).toContain("&lt;b&gt;");
    expect(result.markdown).toContain("Evil\\|");
  });

  it("fails instead of printing an empty summary when no input is given", () => {
    const result = buildRunSummary({});

    expect(result.problems.join("\n")).toMatch(/no inputs/i);
    expect(result.summary).toBeNull();
  });
});

describe("escapeMarkdown", () => {
  it("escapes the characters that change Markdown or HTML meaning and flattens line breaks", () => {
    expect(escapeMarkdown("a|b <i> & `c` [d] \\e\nf")).toBe("a\\|b &lt;i&gt; &amp; \\`c\\` \\[d\\] \\\\e f");
  });
});

describe("summary command line", () => {
  it("writes the tables to stdout and appends them to GITHUB_STEP_SUMMARY, plus the normalized JSON", () => {
    const dir = tempDir();
    const stepSummary = write(dir, "step-summary.md", "existing line\n");
    const jsonOutput = path.join(dir, "out", "run-summary.json");

    const result = spawnSync(
      process.execPath,
      [SCRIPT, "summary", "--trx-directory", trxFolder("trx"), "--coverage-summary", fixture("Summary.json"), "--json-output", jsonOutput],
      { encoding: "utf8", env: { ...process.env, GITHUB_STEP_SUMMARY: stepSummary } },
    );

    expect(result.status, result.stdout + result.stderr).toBe(0);
    expect(result.stdout).toContain("| Unit | 9 | 2 | 1 | 12 |");
    const appended = fs.readFileSync(stepSummary, "utf8");
    expect(appended.startsWith("existing line")).toBe(true);
    expect(appended).toContain("| Unit | 9 | 2 | 1 | 12 |");
    expect(JSON.parse(fs.readFileSync(jsonOutput, "utf8"))).toEqual(summaryFixture());
  });

  it("exits 1 and writes no JSON when an input is unavailable", () => {
    const dir = tempDir();
    const jsonOutput = path.join(dir, "run-summary.json");

    const result = spawnSync(process.execPath, [SCRIPT, "summary", "--trx-directory", path.join(dir, "none"), "--json-output", jsonOutput], { encoding: "utf8" });

    expect(result.status).toBe(1);
    expect(result.stdout).toContain("unavailable");
    expect(fs.existsSync(jsonOutput)).toBe(false);
  });
});

describe("durationsFromJobs", () => {
  it("keeps completed jobs only, in whole seconds", () => {
    const durations = durationsFromJobs({
      jobs: [
        { name: "Web (Build, Lint & Test)", status: "completed", startedAt: "2026-01-01T00:00:00Z", completedAt: "2026-01-01T00:01:35Z" },
        { name: "Still running", status: "in_progress", startedAt: "2026-01-01T00:00:00Z", completedAt: "0001-01-01T00:00:00Z" },
        { name: "Never started", status: "completed", startedAt: null, completedAt: null },
      ],
    });

    expect(durations).toEqual({ "Web (Build, Lint & Test)": 95 });
  });
});

describe("buildRecord", () => {
  const durations = { web: 95, backend: 310 };
  const base = () => ({ commit: COMMIT_B, timestampUtc: "2026-02-03T04:05:06Z", summary: summaryFixture(), durations });

  it("builds the record from the run's own numbers", () => {
    const record = buildRecord(base());

    expect(record).toEqual({
      schemaVersion: 1,
      commit: COMMIT_B,
      timestampUtc: "2026-02-03T04:05:06Z",
      tests: { Unit: { passed: 9, failed: 2, skipped: 1 }, Contract: { passed: 5, failed: 0, skipped: 0 } },
      coverage: { overallLinePct: 90, projects: { "RoleRadar.Domain": 95, "RoleRadar.Application": 85 } },
      jobDurationsSeconds: { web: 95, backend: 310 },
    });
  });

  it("carries the mutation score with its own run date and commit, never the main run's", () => {
    const record = buildRecord({ ...base(), mutation: { project: "web-lib", score: 69.2, runDate: "2026-02-02T03:00:00Z", commit: COMMIT_A } });

    expect(record["mutation"]).toEqual({ "web-lib": 69.2, runDate: "2026-02-02T03:00:00Z", commit: COMMIT_A });
    expect(record["commit"]).toBe(COMMIT_B);
  });

  it("leaves mutation out when the nightly file has no score", () => {
    const record = buildRecord({ ...base(), mutation: { project: "web-lib", runDate: "2026-02-02T03:00:00Z", commit: COMMIT_A } });

    expect(record).not.toHaveProperty("mutation");
  });

  it("refuses a mutation score that has no provenance", () => {
    expect(() => buildRecord({ ...base(), mutation: { project: "web-lib", score: 70 } })).toThrow(/runDate and commit/);
  });

  it("fails, naming the value, when the overall coverage is unavailable", () => {
    const summary = summaryFixture();
    (summary["coverage"] as Record<string, unknown>)["overallLinePct"] = null;

    expect(() => buildRecord({ ...base(), summary })).toThrow(/overallLinePct/);
  });

  it("fails when a lane count is unavailable", () => {
    const summary = summaryFixture();
    ((summary["tests"] as Record<string, Record<string, unknown>>)["Unit"] as Record<string, unknown>)["failed"] = null;

    expect(() => buildRecord({ ...base(), summary })).toThrow(/tests\.Unit\.failed/);
  });

  it("fails when the summary has no tests, no durations, or a malformed commit", () => {
    const summary = summaryFixture();
    delete summary["tests"];
    expect(() => buildRecord({ ...base(), summary })).toThrow(/tests/);
    expect(() => buildRecord({ ...base(), durations: {} })).toThrow(/durations/);
    expect(() => buildRecord({ ...base(), commit: "ABC" })).toThrow(/40 lowercase hex/);
  });
});

describe("appendRecord", () => {
  const line = JSON.stringify({ schemaVersion: 1, commit: COMMIT_B });
  const existing = `${JSON.stringify({ schemaVersion: 1, commit: COMMIT_A })}\n`;

  it("adds exactly one line after the existing ones", () => {
    const result = appendRecord(existing, line, COMMIT_B);

    expect(result.appended).toBe(true);
    expect(result.text.split("\n").filter(Boolean)).toHaveLength(2);
    expect(result.text.endsWith("\n")).toBe(true);
  });

  it("starts a new file when there is no existing history", () => {
    expect(appendRecord("", line, COMMIT_B)).toEqual({ appended: true, text: `${line}\n` });
  });

  it("adds a missing final newline instead of joining two records", () => {
    const result = appendRecord(existing.trimEnd(), line, COMMIT_B);

    expect(result.text.split("\n").filter(Boolean)).toHaveLength(2);
  });

  it("does not append a second record for the same commit", () => {
    const once = appendRecord(existing, line, COMMIT_B).text;
    const twice = appendRecord(once, line, COMMIT_B);

    expect(twice.appended).toBe(false);
    expect(twice.text).toBe(once);
  });

  it("keeps an unparsable existing line untouched and still appends", () => {
    const result = appendRecord(`{ broken\n${existing}`, line, COMMIT_B);

    expect(result.appended).toBe(true);
    expect(result.text.startsWith("{ broken\n")).toBe(true);
  });
});

describe("record command line", () => {
  function inputs(dir: string) {
    return {
      summary: write(dir, "run-summary.json", JSON.stringify(summaryFixture())),
      jobs: write(dir, "jobs.json", JSON.stringify({ jobs: [{ name: "web", status: "completed", startedAt: "2026-01-01T00:00:00Z", completedAt: "2026-01-01T00:01:35Z" }] })),
      existing: write(dir, "pages/metrics/metrics.jsonl", `${JSON.stringify({ schemaVersion: 1, commit: COMMIT_A })}\n`),
      output: path.join(dir, "site", "metrics", "metrics.jsonl"),
    };
  }

  const run = (args: string[]) => spawnSync(process.execPath, [SCRIPT, "record", ...args], { encoding: "utf8" });

  it("writes the existing history plus the new record to a separate output file", () => {
    const dir = tempDir();
    const files = inputs(dir);
    const before = fs.readFileSync(files.existing, "utf8");

    const result = run(["--commit", COMMIT_B, "--summary", files.summary, "--jobs", files.jobs, "--existing", files.existing, "--output", files.output, "--timestamp", "2026-02-03T04:05:06Z"]);

    expect(result.status, result.stdout + result.stderr).toBe(0);
    const lines = fs.readFileSync(files.output, "utf8").split("\n").filter(Boolean);
    expect(lines).toHaveLength(2);
    expect(JSON.parse(lines[1] as string)).toMatchObject({ commit: COMMIT_B, jobDurationsSeconds: { web: 95 } });
    expect(fs.readFileSync(files.existing, "utf8")).toBe(before);
  });

  it("starts the history when no file exists yet", () => {
    const dir = tempDir();
    const files = inputs(dir);

    const result = run(["--commit", COMMIT_B, "--summary", files.summary, "--jobs", files.jobs, "--existing", path.join(dir, "none.jsonl"), "--output", files.output]);

    expect(result.status, result.stdout + result.stderr).toBe(0);
    expect(fs.readFileSync(files.output, "utf8").split("\n").filter(Boolean)).toHaveLength(1);
  });

  it("fails and writes nothing when a number is unavailable", () => {
    const dir = tempDir();
    const files = inputs(dir);
    const broken = summaryFixture();
    (broken["coverage"] as Record<string, unknown>)["overallLinePct"] = null;
    write(dir, "run-summary.json", JSON.stringify(broken));

    const result = run(["--commit", COMMIT_B, "--summary", files.summary, "--jobs", files.jobs, "--existing", files.existing, "--output", files.output]);

    expect(result.status).toBe(1);
    expect(result.stdout).toContain("overallLinePct");
    expect(fs.existsSync(files.output)).toBe(false);
  });

  it("reads the nightly score file for the mutation block", () => {
    const dir = tempDir();
    const files = inputs(dir);
    const mutation = write(dir, "mutation-score.json", JSON.stringify({ project: "web-lib", score: 69.2, commit: COMMIT_A, runDate: "2026-02-02T03:00:00Z" }));

    const result = run(["--commit", COMMIT_B, "--summary", files.summary, "--jobs", files.jobs, "--existing", files.existing, "--output", files.output, "--mutation", mutation]);

    expect(result.status, result.stdout + result.stderr).toBe(0);
    const last = fs.readFileSync(files.output, "utf8").split("\n").filter(Boolean).pop() as string;
    expect(JSON.parse(last)).toMatchObject({ mutation: { "web-lib": 69.2, commit: COMMIT_A, runDate: "2026-02-02T03:00:00Z" } });
  });
});
