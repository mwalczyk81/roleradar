import { afterEach, describe, expect, it } from "vitest";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { runBaseRefCheck, runScoreCheck, scoreReport } from "../../scripts/mutation-ratchet.mjs";

// Feature 011, story 1 (FR-007, FR-011). The ratchet reads a Stryker report (mutation-testing-elements schema), writes the
// score table and mutation-score.json, and enforces the committed floor in quality/mutation-minimums.json. The score
// follows Stryker's rule: (killed + timeout) / (killed + timeout + survived + no coverage); compile errors, runtime
// errors and ignored mutants do not count; zero scorable mutants means no score, never 100. A floor may only rise
// relative to a base ref. Exit codes: 0 pass, 1 below the floor or a floor lowered or removed, 2 input missing or
// unreadable or no score. Every test builds its own temp folder (and temp git repository); all data is fictional.

const SCRIPT = path.resolve(process.cwd(), "scripts", "mutation-ratchet.mjs");
const FIXTURE_SECRET = "fixture-dashboard-key-0000-not-a-real-secret";
const created: string[] = [];

afterEach(() => {
  for (const dir of created.splice(0)) {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

function tempDir(): string {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "rr-ratchet-"));
  created.push(dir);
  return dir;
}

function write(root: string, relative: string, content: string): string {
  const full = path.join(root, relative);
  fs.mkdirSync(path.dirname(full), { recursive: true });
  fs.writeFileSync(full, content);
  return full;
}

function minimumsJson(minimums: Record<string, number>): string {
  return `${JSON.stringify({ schemaVersion: 1, minimums }, null, 2)}\n`;
}

function reportJson(...mutants: Array<[status: string, count: number]>): string {
  const list: unknown[] = [];
  for (const [status, count] of mutants) {
    for (let index = 0; index < count; index += 1) {
      list.push({ id: String(list.length + 1), mutatorName: "ConditionalExpression", replacement: "true", location: { start: { line: 1, column: 1 }, end: { line: 1, column: 2 } }, status });
    }
  }
  return JSON.stringify({ schemaVersion: "2", thresholds: { high: 80, low: 60 }, files: { "app/lib/a.ts": { language: "typescript", source: "export const x = 1;", mutants: list } } });
}

/** A report whose score is exactly the given one (1000 scorable mutants), or has no scorable mutant when null. */
function reportFor(score: number | null): string {
  if (score === null) {
    return reportJson(["CompileError", 4], ["Ignored", 1]);
  }
  const killed = Math.round(score * 10);
  return reportJson(["Killed", killed], ["Survived", 1000 - killed]);
}

const standardReport = (): string => reportJson(["Killed", 8], ["Survived", 3], ["NoCoverage", 1], ["Timeout", 1], ["CompileError", 3], ["Ignored", 2]);

function git(cwd: string, ...args: string[]): void {
  const result = spawnSync("git", args, { cwd, encoding: "utf8" });
  expect(result.status, `git ${args.join(" ")}: ${result.stderr}`).toBe(0);
}

function initRepo(baseMinimums: Record<string, number> | null): string {
  const repo = tempDir();
  git(repo, "init", "-q");
  git(repo, "config", "user.email", "fixture@example.invalid");
  git(repo, "config", "user.name", "Fixture");
  git(repo, "config", "commit.gpgsign", "false");
  git(repo, "config", "core.autocrlf", "false");
  write(repo, "README.md", "fixture\n");
  if (baseMinimums !== null) {
    write(repo, "quality/mutation-minimums.json", minimumsJson(baseMinimums));
  }
  git(repo, "add", "-A");
  git(repo, "commit", "-q", "-m", "base");
  git(repo, "branch", "-M", "main");
  return repo;
}

describe("scoreReport", () => {
  it("follows Stryker's rule and ignores compile errors, runtime errors and ignored mutants", () => {
    const { counts, score } = scoreReport(JSON.parse(standardReport()));

    // (8 killed + 1 timeout) / (8 + 1 + 3 survived + 1 no coverage) = 9 / 13 = 69.2
    expect(score).toBe(69.2);
    expect(counts).toEqual({ Killed: 8, Survived: 3, NoCoverage: 1, Timeout: 1, Ignored: 2 });
  });

  it("returns no score, not 100, when no mutant is scorable", () => {
    expect(scoreReport(JSON.parse(reportFor(null))).score).toBeNull();
    expect(scoreReport(JSON.parse(reportJson())).score).toBeNull();
  });
});

describe("score mode", () => {
  const run = (repo: string, project: string, reportFile: string, extra: Record<string, unknown> = {}) =>
    runScoreCheck({ repositoryRoot: repo, project, reportFile, ...extra });

  it("passes when the score is above the floor and names the project", () => {
    const repo = initRepo({ Domain: 80, Application: 70 });
    const result = run(repo, "Domain", write(repo, "out/domain.json", reportFor(83.3)));

    expect(result.exitCode).toBe(0);
    expect(result.output).toContain("PASS Domain: score 83.3 minimum 80");
  });

  it("passes when the score equals the floor", () => {
    const repo = initRepo({ Domain: 80 });
    expect(run(repo, "Domain", write(repo, "out/domain.json", reportFor(80.0))).exitCode).toBe(0);
  });

  it("fails with exit 1, naming project, score and minimum, when below the floor", () => {
    const repo = initRepo({ Domain: 80 });
    const result = run(repo, "Domain", write(repo, "out/domain.json", reportFor(79.9)));

    expect(result.exitCode).toBe(1);
    expect(result.output).toContain("FAIL Domain: score 79.9 minimum 80");
  });

  it("exits 2 and says no report was produced, naming the file and the step to read, when the report file is missing", () => {
    const repo = initRepo({ Domain: 80 });
    const summary = path.join(repo, "summary.md");
    const missing = path.join(repo, "out", "missing.json");
    const result = run(repo, "Domain", missing, { summaryPath: summary });

    expect(result.exitCode).toBe(2);
    expect(result.output).toContain("No Stryker report was produced");
    expect(result.output).toContain(missing);
    expect(result.output).toContain("Run StrykerJS");
    expect(result.output).toContain("no score");
    // The same explanation reaches the job summary, where it is read first, and no score file is invented.
    expect(fs.readFileSync(summary, "utf8")).toContain("No Stryker report was produced");
  });

  it("exits 2 when the project has no committed minimum", () => {
    const repo = initRepo({ Domain: 80 });
    expect(run(repo, "Other", write(repo, "out/other.json", reportFor(90))).exitCode).toBe(2);
  });

  it("exits 2 and writes nothing when the report is not JSON", () => {
    const repo = initRepo({ Domain: 80 });
    const outputDirectory = path.join(repo, "score");
    const result = run(repo, "Domain", write(repo, "out/mutation.json", "{ this is not json"), { outputDirectory });

    expect(result.exitCode).toBe(2);
    expect(fs.existsSync(path.join(outputDirectory, "mutation-score.json"))).toBe(false);
  });

  it("renders the counts table and writes the score file with its provenance, then checks the floor", () => {
    const repo = initRepo({ "web-lib": 69 });
    const outputDirectory = path.join(repo, "score");
    const result = run(repo, "web-lib", write(repo, "out/mutation.json", standardReport()), { outputDirectory, runId: "1234", commit: "abc1234" });

    expect(result.exitCode).toBe(0);
    expect(result.output).toContain("| Project | Score | Killed | Survived | No coverage | Timeout | Ignored |");
    expect(result.output).toContain("| web-lib | 69.2 | 8 | 3 | 1 | 1 | 2 |");
    const score = JSON.parse(fs.readFileSync(path.join(outputDirectory, "mutation-score.json"), "utf8")) as Record<string, unknown>;
    expect(score).toMatchObject({ project: "web-lib", score: 69.2, killed: 8, survived: 3, noCoverage: 1, timeout: 1, ignored: 2, runId: "1234", commit: "abc1234" });
    expect(typeof score["runDate"]).toBe("string");
  });

  it("with no scorable mutants writes a score file without a score, shows unavailable, never 100, and exits 2", () => {
    const repo = initRepo({ Domain: 0 });
    const outputDirectory = path.join(repo, "score");
    const result = run(repo, "Domain", write(repo, "out/mutation.json", reportFor(null)), { outputDirectory });

    expect(result.exitCode).toBe(2);
    expect(result.output).toContain("unavailable");
    expect(result.output).not.toContain("100");
    const score = JSON.parse(fs.readFileSync(path.join(outputDirectory, "mutation-score.json"), "utf8")) as Record<string, unknown>;
    expect(score).not.toHaveProperty("score");
    expect(score["project"]).toBe("Domain");
  });

  it("adds the dashboard-skipped line only when asked", () => {
    const repo = initRepo({ "web-lib": 60 });
    const report = write(repo, "out/mutation.json", standardReport());

    expect(run(repo, "web-lib", report, { dashboardSkipped: true }).output).toContain("Stryker Dashboard publish skipped: secret not configured");
    expect(run(repo, "web-lib", report).output).not.toContain("Stryker Dashboard publish skipped");
  });

  it("appends the table to the summary path and never lets a secret reach any output", () => {
    const repo = initRepo({ "web-lib": 60 });
    const report = write(repo, "out/mutation.json", standardReport());
    const summary = path.join(repo, "summary.md");
    const outputDirectory = path.join(repo, "score");

    const result = spawnSync(
      process.execPath,
      [SCRIPT, "--repository-root", repo, "--project", "web-lib", "--report", report, "--output-directory", outputDirectory, "--summary-path", summary, "--dashboard-skipped"],
      { encoding: "utf8", env: { ...process.env, STRYKER_DASHBOARD_API_KEY: FIXTURE_SECRET } },
    );

    expect(result.status, result.stdout + result.stderr).toBe(0);
    expect(result.stdout + result.stderr).not.toContain(FIXTURE_SECRET);
    expect(fs.readFileSync(summary, "utf8")).not.toContain(FIXTURE_SECRET);
    expect(fs.readFileSync(summary, "utf8")).toContain("| web-lib | 69.2 |");
    expect(fs.readFileSync(path.join(outputDirectory, "mutation-score.json"), "utf8")).not.toContain(FIXTURE_SECRET);
    expect(fs.readFileSync(SCRIPT, "utf8")).not.toContain("STRYKER_DASHBOARD_API_KEY");
  });

  it("exits with the same codes through the command line", () => {
    const repo = initRepo({ Domain: 80 });
    const report = write(repo, "out/domain.json", reportFor(79.9));
    const result = spawnSync(process.execPath, [SCRIPT, "--repository-root", repo, "--project", "Domain", "--report", report], { encoding: "utf8" });

    expect(result.status).toBe(1);
    expect(result.stdout).toContain("FAIL Domain");
  });
});

describe("base ref mode", () => {
  const check = (repo: string, baseRef: string) => runBaseRefCheck({ repositoryRoot: repo, baseRef });

  it("passes when minimums are raised", () => {
    const repo = initRepo({ Domain: 50, Application: 40 });
    write(repo, "quality/mutation-minimums.json", minimumsJson({ Domain: 60, Application: 40 }));

    expect(check(repo, "main").exitCode).toBe(0);
  });

  it("passes when minimums are unchanged", () => {
    const repo = initRepo({ Domain: 50, Application: 40 });
    expect(check(repo, "main").exitCode).toBe(0);
  });

  it("fails, naming the project, when a minimum is lowered", () => {
    const repo = initRepo({ Domain: 50, Application: 40 });
    write(repo, "quality/mutation-minimums.json", minimumsJson({ Domain: 50, Application: 39 }));

    const result = check(repo, "main");

    expect(result.exitCode).toBe(1);
    expect(result.output).toContain("Application: lowered from 40 to 39");
  });

  it("fails when a project is removed", () => {
    const repo = initRepo({ Domain: 50, Application: 40, "web-lib": 30 });
    write(repo, "quality/mutation-minimums.json", minimumsJson({ Domain: 50, Application: 40 }));

    const result = check(repo, "main");

    expect(result.exitCode).toBe(1);
    expect(result.output).toContain("web-lib: removed");
  });

  it("passes when the file does not exist at the base ref (first introduction)", () => {
    const repo = initRepo(null);
    write(repo, "quality/mutation-minimums.json", minimumsJson({ Domain: 0 }));

    expect(check(repo, "main").exitCode).toBe(0);
  });

  it("exits 2 when the base ref does not exist", () => {
    const repo = initRepo({ Domain: 50 });
    expect(check(repo, "no-such-ref").exitCode).toBe(2);
  });
});
