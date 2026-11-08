import { describe, it, expect } from "vitest";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { findLeaks, sanitizeResults, scrubText } from "../../scripts/allure-sanitize.mjs";
import { checkSanitizer, FIXTURE_HOST, FIXTURE_USER, type Lane, type SanitizeFunction } from "../support/allure-sanitizer-contract";

// Feature 011, story 2b (FR-028, FR-029). Allure results are sanitized before anything is published: real-stack
// results lose every attachment (nested ones too) and keep only pass or fail and timings; every lane loses absolute
// paths, user names and host names; traces never reach the published site. Fictional names only.

// --- Deliberately broken sanitizers: the contract must catch each one. -----------------------------------------------

/** Copies everything unchanged. */
const noopSanitizer: SanitizeFunction = async ({ inputDir, outputDir }) => {
  fs.cpSync(inputDir, outputDir, { recursive: true });
};

/** Strips attachments from the top level of each result only, so nested step attachments survive. */
const topLevelOnlySanitizer: SanitizeFunction = async ({ inputDir, outputDir, lane }) => {
  fs.mkdirSync(outputDir, { recursive: true });
  for (const name of fs.readdirSync(inputDir)) {
    const source = path.join(inputDir, name);
    if (name.endsWith("-result.json")) {
      const parsed = JSON.parse(fs.readFileSync(source, "utf8")) as Record<string, unknown>;
      if (lane === "real-stack") {
        parsed["attachments"] = [];
      }
      fs.writeFileSync(path.join(outputDir, name), JSON.stringify(parsed));
    } else {
      fs.copyFileSync(source, path.join(outputDir, name));
    }
  }
};

const lanes: Lane[] = ["real-stack", "fictional"];

describe("the sanitizer contract", () => {
  for (const lane of lanes) {
    it(`is met by the real sanitizer (${lane} lane)`, async () => {
      expect(await checkSanitizer(sanitizeResults as SanitizeFunction, lane)).toEqual([]);
    });

    it(`catches a no-op sanitizer (${lane} lane)`, async () => {
      const violations = await checkSanitizer(noopSanitizer, lane);
      expect(violations.length).toBeGreaterThan(0);
      expect(violations.join("\n")).toMatch(/host label|Windows drive path|POSIX home path|fixture user|fixture host/);
    });
  }

  it("catches a sanitizer that only strips top-level attachments from real-stack results", async () => {
    const violations = await checkSanitizer(topLevelOnlySanitizer, "real-stack");
    expect(violations.join("\n")).toMatch(/real-stack output still has \d+ attachment reference/);
  });
});

describe("scrubText", () => {
  it("replaces Windows, POSIX, UNC and file URL paths and keeps the file name and line numbers", () => {
    const scrubbed = scrubText(
      [
        "at C:\\Users\\somebody\\work\\repo\\web\\x.ts:10:5",
        "at D:/a/repo/repo/web/y.ts:3:1",
        "at (/home/runner/work/repo/repo/web/z.ts:7:9)",
        "at /Users/somebody/repo/w.ts:2:2",
        "see \\\\fileserver\\share\\folder\\v.ts",
        "at file:///C:/Users/somebody/u.ts:1:1",
      ].join("\n"),
    );

    expect(scrubbed).not.toMatch(/somebody|fileserver|runner|C:\\|D:\/|\/home\/|\/Users\//);
    for (const expected of ["x.ts:10:5", "y.ts:3:1", "z.ts:7:9", "w.ts:2:2", "v.ts", "u.ts:1:1"]) {
      expect(scrubbed).toContain(expected);
    }
  });

  it("leaves repository-relative paths, public URLs and plain text alone", () => {
    const text = "web/app/lib/format-date.ts:9:7 and https://example.test/home/users/page and a:b and 12:30/45";
    expect(scrubText(text)).toBe(text);
  });

  it("redacts the given user and host names as whole words, ignoring case", () => {
    const scrubbed = scrubText(`${FIXTURE_USER} logged in on ${FIXTURE_HOST.toLowerCase()}; ${FIXTURE_USER}2 is someone else`, {
      redact: [FIXTURE_USER, FIXTURE_HOST],
    });
    expect(scrubbed).toBe("[redacted] logged in on [redacted]; fixtureuser2 is someone else");
  });

  it("by default redacts the name of the account and the machine it runs on", () => {
    const user = os.userInfo().username;
    const host = os.hostname();
    const scrubbed = scrubText(`built by ${user} on ${host}`);
    if (user.length >= 3) {
      expect(scrubbed).not.toContain(user);
    }
    if (host.length >= 3) {
      expect(scrubbed.toLowerCase()).not.toContain(host.toLowerCase());
    }
  });
});

describe("findLeaks", () => {
  function withFiles(files: Record<string, string | Buffer>, run: (dir: string) => void): void {
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), "rr-allure-leaks-"));
    try {
      for (const [name, content] of Object.entries(files)) {
        fs.mkdirSync(path.dirname(path.join(dir, name)), { recursive: true });
        fs.writeFileSync(path.join(dir, name), content);
      }
      run(dir);
    } finally {
      fs.rmSync(dir, { recursive: true, force: true });
    }
  }

  it("reports a path in raw JSON with escaped backslashes, by rule and file and never by the matched text", () => {
    withFiles({ "a-result.json": JSON.stringify({ message: `at C:\\Users\\${FIXTURE_USER}\\x.ts` }) }, (dir) => {
      const leaks = findLeaks(dir, { redact: [FIXTURE_USER] });
      expect(leaks.length).toBeGreaterThan(0);
      expect(JSON.stringify(leaks)).not.toContain(FIXTURE_USER);
      expect(leaks[0]).toMatchObject({ file: "a-result.json" });
    });
  });

  it("reports POSIX home paths and redacted names, and finds nothing in clean content or binary files", () => {
    withFiles({ "log.txt": "at /home/someone/x.ts", "b.json": `{"n":"${FIXTURE_HOST}"}` }, (dir) => {
      const rules = findLeaks(dir, { redact: [FIXTURE_HOST] }).map((leak) => leak.rule);
      expect(rules).toContain("posix-home-path");
      expect(rules).toContain("redacted-name");
    });
    withFiles({ "ok.json": '{"a":"web/app/x.ts","u":"https://example.test/home/x"}', "shot.png": Buffer.from("C:\\Users\\hidden\\x") }, (dir) => {
      expect(findLeaks(dir, { redact: [FIXTURE_USER] })).toEqual([]);
    });
  });
});

describe("the command line", () => {
  const script = path.resolve(process.cwd(), "scripts", "allure-sanitize.mjs");

  function run(args: string[]): { status: number | null; output: string } {
    const result = spawnSync(process.execPath, [script, ...args], { encoding: "utf8", timeout: 30_000 });
    return { status: result.status, output: `${result.stdout}${result.stderr}` };
  }

  it("exits 1 on a leak without printing the leaked text, and 0 on clean content", () => {
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), "rr-allure-cli-"));
    try {
      fs.writeFileSync(path.join(dir, "r.json"), JSON.stringify({ m: `/home/${FIXTURE_USER}/x.ts` }));
      const leaking = run(["--check", dir, "--redact", FIXTURE_USER]);
      expect(leaking.status).toBe(1);
      expect(leaking.output).not.toContain(FIXTURE_USER);
      fs.writeFileSync(path.join(dir, "r.json"), JSON.stringify({ m: "web/app/x.ts" }));
      expect(run(["--check", dir, "--redact", FIXTURE_USER]).status).toBe(0);
    } finally {
      fs.rmSync(dir, { recursive: true, force: true });
    }
  });

  it("sanitizes a folder, and refuses an unknown lane, a missing input and a non-empty output", () => {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), "rr-allure-cli-"));
    try {
      const input = path.join(root, "in");
      fs.mkdirSync(input);
      fs.writeFileSync(path.join(input, "a-result.json"), JSON.stringify({ uuid: "a", status: "passed", labels: [{ name: "host", value: "H" }] }));
      expect(run(["--input", input, "--output", path.join(root, "out"), "--lane", "real-stack"]).status).toBe(0);
      expect(fs.existsSync(path.join(root, "out", "a-result.json"))).toBe(true);
      expect(run(["--input", input, "--output", path.join(root, "out2"), "--lane", "nonsense"]).status).toBe(2);
      expect(run(["--input", path.join(root, "missing"), "--output", path.join(root, "out3"), "--lane", "fictional"]).status).toBe(2);
      expect(run(["--input", input, "--output", path.join(root, "out"), "--lane", "fictional"]).status).toBe(2);
      expect(run(["--input", input, "--output", path.join(input, "inside"), "--lane", "fictional"]).status).toBe(2);
    } finally {
      fs.rmSync(root, { recursive: true, force: true });
    }
  });
});
