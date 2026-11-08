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

  // The Vitest adapter records every expect(...) call as a step whose name is the source text of the call, so a step name
  // can hold backslash-n sequences (the text of a string literal) or real line breaks. Raw JSON doubles each backslash,
  // so a rule applied to the file text read two such sequences with a word between them as a UNC path. Result files are
  // now scanned as decoded string values, at any depth.
  describe("result files are scanned as decoded values", () => {
    const sourceTextWithBackslashN = 'expect("### Combined report inputs\\n\\n| Source | Tests |\\n|---|---:|\\n| Unit | 3 |\\n| Architecture | 2 |")';
    const realLineBreaks = "expect(received)\nfoo\nbar\n";
    const resultWith = (nested: Record<string, unknown>): string =>
      JSON.stringify({ name: "a test", status: "passed", steps: [{ name: "outer", steps: [nested] }] });

    it("does not report backslash-n text or real line breaks in step names", () => {
      withFiles(
        {
          "one-result.json": resultWith({ name: sourceTextWithBackslashN, status: "passed" }),
          "two-result.json": resultWith({ name: realLineBreaks, status: "passed" }),
          "three-result.json": JSON.stringify({ name: realLineBreaks, statusDetails: { message: sourceTextWithBackslashN, trace: realLineBreaks } }),
        },
        (dir) => {
          expect(findLeaks(dir, { redact: [FIXTURE_USER] })).toEqual([]);
        },
      );
    });

    it("still reports a real UNC path, Windows path and home path in a nested step", () => {
      withFiles(
        {
          "unc-result.json": resultWith({ name: "\\\\fileserver\\share\\project\\x.ts", status: "passed" }),
          "windows-result.json": resultWith({ name: "step", status: "failed", statusDetails: { trace: `at C:\\Users\\${FIXTURE_USER}\\project\\x.ts:1` } }),
          "home-result.json": resultWith({ name: "step", status: "passed", parameters: [{ name: "file", value: "/home/someone/project/x.ts" }] }),
        },
        (dir) => {
          const found = findLeaks(dir, { redact: [FIXTURE_USER] }).map((leak) => `${leak.file}:${leak.rule}`);
          expect(found).toContain("unc-result.json:unc-path");
          expect(found).toContain("windows-result.json:windows-path");
          expect(found).toContain("home-result.json:posix-home-path");
        },
      );
    });

    it("reports a path hidden next to backslash-n text in the same step", () => {
      withFiles({ "mixed-result.json": resultWith({ name: `${sourceTextWithBackslashN} at \\\\fileserver\\share\\x.ts`, status: "passed" }) }, (dir) => {
        expect(findLeaks(dir, {}).map((leak) => leak.rule)).toEqual(["unc-path"]);
      });
    });

    it("keeps plain text files tight: a real UNC path is reported, backslash-n text is not", () => {
      withFiles({ "log.txt": "failed at \\\\fileserver\\share\\x.ts" }, (dir) => {
        expect(findLeaks(dir, {}).map((leak) => leak.rule)).toEqual(["unc-path"]);
      });
      withFiles({ "log.txt": "line one\\nfoo\\nbar and \\\\n\\\\nbaz" }, (dir) => {
        expect(findLeaks(dir, {})).toEqual([]);
      });
    });

    // Test titles may carry hostile fictional input, for example a UNC-shaped URL a security test must reject. A host under a
    // reserved name (RFC 2606: .example, .test, .invalid, .localhost) can never be a real machine, so it is not a leak.
    it("does not report a UNC-shaped string whose host is a reserved fictional name, in JSON values or in JUnit XML", () => {
      const title = 'rejects "\\\\evil.example\\a"';
      withFiles(
        {
          "one-result.json": resultWith({ name: title, status: "passed" }),
          "Vitest.xml": `<testsuites><testsuite><testcase classname="tests/unit/x.test.ts" name="${title.replaceAll('"', "&quot;")}"></testcase></testsuite></testsuites>`,
          "log.txt": `step ${title}`,
        },
        (dir) => {
          expect(findLeaks(dir, {})).toEqual([]);
        },
      );
    });

    it("still reports a UNC path to a real-looking host, also when it sits next to a reserved fictional one", () => {
      withFiles(
        {
          "real-result.json": resultWith({ name: "\\\\build01.corp.local\\share\\x.ts", status: "passed" }),
          "mixed-result.json": resultWith({ name: 'rejects "\\\\evil.example\\a" at \\\\fileserver\\share\\x.ts', status: "passed" }),
          "Unit.xml": '<testsuites><testsuite><testcase name="at \\\\fileserver\\share\\x.ts"></testcase></testsuite></testsuites>',
        },
        (dir) => {
          const found = findLeaks(dir, {}).map((leak) => `${leak.file}:${leak.rule}`);
          expect(found).toContain("real-result.json:unc-path");
          expect(found).toContain("mixed-result.json:unc-path");
          expect(found).toContain("Unit.xml:unc-path");
        },
      );
    });

    it("falls back to the raw text rules for a .json file that is not valid JSON", () => {
      withFiles({ "broken.json": "{ not json at C:\\Users\\someone\\x" }, (dir) => {
        expect(findLeaks(dir, {}).map((leak) => leak.rule)).toContain("windows-path");
      });
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

  it("keeps every test case of a Vitest JUnit file, with its file path as class name, and redacts the machine name", () => {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), "rr-allure-junit-"));
    try {
      const input = path.join(root, "in");
      fs.mkdirSync(input);
      const xml =
        '<?xml version="1.0" encoding="UTF-8" ?>\n<testsuites name="vitest tests" tests="2">\n' +
        '  <testsuite name="tests/unit/nav.test.ts" timestamp="2026-11-08T15:38:00.272Z" hostname="fv-az1234-567" tests="2">\n' +
        '    <testcase classname="tests/unit/nav.test.ts" name="nav &gt; lists the items" time="0.002"></testcase>\n' +
        '    <testcase classname="tests/unit/nav.test.ts" name="nav &gt; marks the active item" time="0.003"><failure message="at C:\\Users\\someone\\proj\\x.ts:1">trace</failure></testcase>\n' +
        "  </testsuite>\n</testsuites>\n";
      fs.writeFileSync(path.join(input, "Vitest.xml"), xml);

      expect(run(["--input", input, "--output", path.join(root, "out"), "--lane", "fictional"]).status).toBe(0);

      const out = fs.readFileSync(path.join(root, "out", "Vitest.xml"), "utf8");
      expect(out).not.toContain("fv-az1234-567");
      expect(out).toContain('hostname="[redacted]"');
      expect(out).not.toContain("someone");
      expect(out.match(/<testcase\b/g)).toHaveLength(2);
      expect(out).toContain('classname="tests/unit/nav.test.ts"');
      expect(out).toContain("nav &gt; marks the active item");
      expect(run(["--check", path.join(root, "out")]).status).toBe(0);
    } finally {
      fs.rmSync(root, { recursive: true, force: true });
    }
  });
});


const TRACE_TYPE = "application/vnd.allure.playwright-trace";
const MIB = 1024 * 1024;

type TraceFixture = { source: string; bytes: number };
type OutStep = { name: string; steps?: OutStep[]; attachments?: Array<{ name: string; source: string; type: string }> };

/** Runs the sanitizer on one failing result whose steps each carry one Playwright trace; returns what came out. */
async function sanitizeTraces(
  lane: Lane,
  traces: TraceFixture[],
  options: { missing?: string[]; nested?: boolean } = {},
): Promise<{
  steps: OutStep[];
  files: string[];
  read: (name: string) => Buffer;
  stats: Awaited<ReturnType<typeof sanitizeResults>>;
  leaks: ReturnType<typeof findLeaks>;
}> {
  const input = fs.mkdtempSync(path.join(os.tmpdir(), "rr-traces-in-"));
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "rr-traces-out-"));
  const output = path.join(root, "out");
  try {
    for (const trace of traces) {
      fs.writeFileSync(path.join(input, trace.source), Buffer.alloc(trace.bytes, 0x50));
    }
    const all = [...traces.map((trace) => trace.source), ...(options.missing ?? [])];
    const traceSteps = all.map((source, index) => ({ name: `trace step ${index + 1}`, status: "unknown", steps: [], attachments: [{ name: "trace", source, type: TRACE_TYPE }] }));
    const steps = options.nested ? [{ name: "After Hooks", status: "passed", steps: traceSteps, attachments: [] }] : traceSteps;
    fs.writeFileSync(path.join(input, "t-result.json"), JSON.stringify({ uuid: "t", name: "a test", status: "failed", labels: [], steps }));
    const stats = await sanitizeResults({ inputDir: input, outputDir: output, lane });
    const parsed = JSON.parse(fs.readFileSync(path.join(output, "t-result.json"), "utf8")) as { steps: OutStep[] };
    const files = fs.readdirSync(output).sort();
    const contents = new Map(files.map((name) => [name, fs.readFileSync(path.join(output, name))]));
    return {
      steps: options.nested ? (parsed.steps[0]?.steps ?? []) : parsed.steps,
      files,
      read: (name) => contents.get(name) ?? Buffer.alloc(0),
      stats,
      leaks: findLeaks(output),
    };
  } finally {
    fs.rmSync(input, { recursive: true, force: true });
    fs.rmSync(root, { recursive: true, force: true });
  }
}

const referenced = (steps: OutStep[], type: string): string[] =>
  steps.flatMap((step) => (step.attachments ?? []).filter((attachment) => attachment.type === type).map((attachment) => attachment.source));

// The owner decided that Playwright keeps a trace for every failed run and that the published report carries the trace
// zip when it can (fictional lane only). A trace is kept when the file is at most 3 MiB, and at most 15 MiB of traces are
// kept per sanitized result set, dropping the largest first. A dropped trace leaves a visible text note on the result and
// the trace stays in the CI artifact. Real-stack results lose every attachment, traces included.
describe("Playwright traces in the fictional lane", () => {
  it("keeps a trace under the cap: the zip is copied byte for byte and the result still references it", async () => {
    const { steps, files, read, stats } = await sanitizeTraces("fictional", [{ source: "aaaa-trace-attachment.zip", bytes: 4096 }]);
    expect(files).toContain("aaaa-trace-attachment.zip");
    expect(read("aaaa-trace-attachment.zip").equals(Buffer.alloc(4096, 0x50))).toBe(true);
    expect(referenced(steps, TRACE_TYPE)).toEqual(["aaaa-trace-attachment.zip"]);
    expect(stats).toMatchObject({ tracesKept: 1, tracesDropped: 0 });
  });

  it("keeps a trace of exactly 3 MiB and drops one byte more, with a note", async () => {
    const { steps, files, stats } = await sanitizeTraces("fictional", [
      { source: "a-trace-attachment.zip", bytes: 3 * MIB },
      { source: "b-trace-attachment.zip", bytes: 3 * MIB + 1 },
    ]);
    expect(files).toContain("a-trace-attachment.zip");
    expect(files).not.toContain("b-trace-attachment.zip");
    expect(referenced(steps, TRACE_TYPE)).toEqual(["a-trace-attachment.zip"]);
    expect(stats).toMatchObject({ tracesKept: 1, tracesDropped: 1 });
  });

  it("leaves a visible plain-text note where a too-large trace was dropped, saying why and where the trace is", async () => {
    const { steps, files, read, leaks } = await sanitizeTraces("fictional", [{ source: "big-trace-attachment.zip", bytes: 4 * MIB }]);
    expect(files).not.toContain("big-trace-attachment.zip");
    expect(JSON.stringify(steps)).not.toContain("big-trace-attachment.zip");
    const noteSources = referenced(steps, "text/plain");
    expect(noteSources).toHaveLength(1);
    const noteSource = noteSources[0] ?? "";
    expect(files).toContain(noteSource);
    const note = read(noteSource).toString("utf8");
    expect(note).toMatch(/trace/i);
    expect(note).toMatch(/dropped/i);
    expect(note).toMatch(/size/i);
    expect(note).toMatch(/3 MiB/);
    expect(note).toMatch(/CI artifact/);
    expect(leaks).toEqual([]);
    // The step that carried the trace is still there and now carries the note.
    expect(steps.map((step) => step.name)).toEqual(["trace step 1"]);
  });

  it("keeps at most 15 MiB of traces per result set, dropping the largest first and noting each one", async () => {
    const traces = [
      { source: "t1-trace-attachment.zip", bytes: 3 * MIB },
      { source: "t2-trace-attachment.zip", bytes: 3 * MIB },
      { source: "t3-trace-attachment.zip", bytes: 3 * MIB },
      { source: "t4-trace-attachment.zip", bytes: 3 * MIB },
      { source: "t5-trace-attachment.zip", bytes: 3 * MIB },
      { source: "t6-trace-attachment.zip", bytes: 1 * MIB },
    ];
    const { steps, files, stats } = await sanitizeTraces("fictional", traces);
    const keptNames = files.filter((name) => name.endsWith("-trace-attachment.zip"));
    const keptBytes = keptNames.reduce((sum, name) => sum + (traces.find((trace) => trace.source === name)?.bytes ?? 0), 0);
    expect(keptBytes).toBeLessThanOrEqual(15 * MIB);
    expect(keptNames).toContain("t6-trace-attachment.zip");
    expect(keptNames).toHaveLength(5);
    expect(stats).toMatchObject({ tracesKept: 5, tracesDropped: 1 });
    expect(referenced(steps, TRACE_TYPE).sort()).toEqual(keptNames);
    expect(referenced(steps, "text/plain")).toHaveLength(1);
  });

  it("keeps exactly 15 MiB of traces without dropping any", async () => {
    const traces = ["a", "b", "c", "d", "e"].map((id) => ({ source: `${id}-trace-attachment.zip`, bytes: 3 * MIB }));
    const { stats, steps } = await sanitizeTraces("fictional", traces);
    expect(stats).toMatchObject({ tracesKept: 5, tracesDropped: 0 });
    expect(referenced(steps, "text/plain")).toEqual([]);
  });

  it("is deterministic: the same input drops the same trace when sizes tie", async () => {
    const traces = ["a", "b", "c", "d", "e", "f"].map((id) => ({ source: `${id}-trace-attachment.zip`, bytes: 3 * MIB }));
    const first = await sanitizeTraces("fictional", traces);
    const second = await sanitizeTraces("fictional", traces);
    expect(second.files).toEqual(first.files);
  });

  it("drops a trace whose file is missing, with a note, instead of leaving a dead reference", async () => {
    const { steps, files } = await sanitizeTraces("fictional", [], { missing: ["gone-trace-attachment.zip"] });
    expect(referenced(steps, TRACE_TYPE)).toEqual([]);
    expect(JSON.stringify(steps)).not.toContain("gone-trace-attachment.zip");
    const noteSources = referenced(steps, "text/plain");
    expect(noteSources).toHaveLength(1);
    expect(files).toContain(noteSources[0] ?? "");
  });

  it("keeps a trace nested inside another step", async () => {
    const { steps, files } = await sanitizeTraces("fictional", [{ source: "n-trace-attachment.zip", bytes: 1024 }], { nested: true });
    expect(files).toContain("n-trace-attachment.zip");
    expect(referenced(steps, TRACE_TYPE)).toEqual(["n-trace-attachment.zip"]);
  });
});

describe("Playwright traces in the real-stack lane", () => {
  it("drops every trace, whatever its size, with no note, no reference and no file", async () => {
    const { steps, files, stats } = await sanitizeTraces("real-stack", [
      { source: "small-trace-attachment.zip", bytes: 1024 },
      { source: "big-trace-attachment.zip", bytes: 4 * MIB },
    ]);
    expect(files).toEqual(["t-result.json"]);
    expect(steps).toEqual([]);
    expect(stats).toMatchObject({ tracesKept: 0 });
  });

  it("drops a nested trace too, and the step that only carried it", async () => {
    const { steps, files } = await sanitizeTraces("real-stack", [{ source: "n-trace-attachment.zip", bytes: 1024 }], { nested: true });
    expect(files).toEqual(["t-result.json"]);
    expect(steps).toEqual([]);
  });
});

// The privacy gate is still binding: a sanitizer that applies the fictional rules to a real-stack folder is caught.
describe("the privacy binding with traces", () => {
  /** Runs the real sanitizer with the fictional rules whatever the lane says, so real-stack traces would be published. */
  const fictionalRulesEverywhere: SanitizeFunction = async (options) => sanitizeResults({ ...options, lane: "fictional" });

  it("catches a sanitizer that keeps real-stack traces", async () => {
    const violations = await checkSanitizer(fictionalRulesEverywhere, "real-stack");
    expect(violations.join("\n")).toMatch(/real-stack output still has \d+ attachment reference/);
  });

  it("is met by the real sanitizer in both lanes, with a nested Playwright trace in the fixture", async () => {
    expect(await checkSanitizer(sanitizeResults as SanitizeFunction, "real-stack")).toEqual([]);
    expect(await checkSanitizer(sanitizeResults as SanitizeFunction, "fictional")).toEqual([]);
  });
});

describe("the leak check and a trace zip", () => {
  it("does not read a zip, even when its bytes look like a path, and still reads a text attachment next to it", () => {
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), "rr-zip-leaks-"));
    try {
      fs.writeFileSync(
        path.join(dir, "x-trace-attachment.zip"),
        Buffer.from(`PK\u0003\u0004 C:\\Users\\${FIXTURE_USER}\\x.ts /home/${FIXTURE_USER}/y.ts \u0000\u00ff\u00fe`, "latin1"),
      );
      expect(findLeaks(dir, { redact: [FIXTURE_USER] })).toEqual([]);
      fs.writeFileSync(path.join(dir, "note-attachment.txt"), `at C:\\Users\\${FIXTURE_USER}\\x.ts`);
      const leaks = findLeaks(dir, { redact: [FIXTURE_USER] });
      expect(leaks.length).toBeGreaterThan(0);
      expect(leaks.every((leak) => leak.file === "note-attachment.txt")).toBe(true);
    } finally {
      fs.rmSync(dir, { recursive: true, force: true });
    }
  });

  it("checks a sanitized output that holds a kept trace and a note without error", async () => {
    const { leaks, files } = await sanitizeTraces("fictional", [
      { source: "k-trace-attachment.zip", bytes: 2048 },
      { source: "z-trace-attachment.zip", bytes: 4 * MIB },
    ]);
    expect(files).toContain("k-trace-attachment.zip");
    expect(leaks).toEqual([]);
  });
});

// allure-playwright records a trace as its own leaf step ("trace") whose only content is the trace attachment. When the
// trace is dropped without a note (real-stack lane) the attachment goes and the empty "unknown" step goes with it. A step
// whose trace is kept, or whose trace was replaced by a note, still carries an attachment and stays.
describe("the empty trace step", () => {
  const traceStep = { name: "trace", status: "unknown", steps: [], attachments: [{ name: "trace", source: "trace.zip", type: TRACE_TYPE }] };
  const screenshotStep = { name: "screenshot", status: "passed", steps: [], attachments: [{ name: "screenshot", source: "shot.png", type: "image/png" }] };
  const plainStep = { name: "expect.toBeVisible", status: "failed", steps: [], attachments: [] };

  async function sanitizedSteps(lane: Lane, steps: unknown[]): Promise<Array<{ name: string; steps?: Array<{ name: string }> }>> {
    const input = fs.mkdtempSync(path.join(os.tmpdir(), "rr-trace-in-"));
    const output = path.join(fs.mkdtempSync(path.join(os.tmpdir(), "rr-trace-out-")), "out");
    try {
      fs.writeFileSync(path.join(input, "t-result.json"), JSON.stringify({ uuid: "t", name: "a test", status: "failed", labels: [], steps }));
      fs.writeFileSync(path.join(input, "trace.zip"), Buffer.from([0x50, 0x4b]));
      fs.writeFileSync(path.join(input, "shot.png"), Buffer.from([0x89, 0x50]));
      await sanitizeResults({ inputDir: input, outputDir: output, lane });
      return (JSON.parse(fs.readFileSync(path.join(output, "t-result.json"), "utf8")) as { steps: Array<{ name: string; steps?: Array<{ name: string }> }> }).steps;
    } finally {
      fs.rmSync(input, { recursive: true, force: true });
    }
  }

  const names = (steps: Array<{ name: string }>): string[] => steps.map((step) => step.name);

  it("stays in a fictional result when its trace is kept, with the screenshot and the other steps", async () => {
    expect(names(await sanitizedSteps("fictional", [plainStep, traceStep, screenshotStep]))).toEqual(["expect.toBeVisible", "trace", "screenshot"]);
  });

  it("is removed from a real-stack result", async () => {
    expect(names(await sanitizedSteps("real-stack", [plainStep, traceStep]))).toEqual(["expect.toBeVisible"]);
  });

  it("is removed when nested inside another step of a real-stack result, and kept in a fictional one", async () => {
    const nested = { name: "After Hooks", status: "passed", steps: [traceStep, plainStep], attachments: [] };
    expect(names((await sanitizedSteps("real-stack", [nested]))[0]?.steps ?? [])).toEqual(["expect.toBeVisible"]);
    expect(names((await sanitizedSteps("fictional", [nested]))[0]?.steps ?? [])).toEqual(["trace", "expect.toBeVisible"]);
  });

  it("does not remove a step that has children, other attachments or a different dropped type", async () => {
    const withChild = { name: "has a child", status: "passed", steps: [plainStep], attachments: [{ name: "trace", source: "trace.zip", type: TRACE_TYPE }] };
    const withShot = { name: "trace and shot", status: "passed", steps: [], attachments: [{ name: "trace", source: "trace.zip", type: TRACE_TYPE }, { name: "s", source: "shot.png", type: "image/png" }] };
    const video = { name: "video step", status: "passed", steps: [], attachments: [{ name: "video", source: "trace.zip", type: "video/webm" }] };
    for (const lane of ["real-stack", "fictional"] as const) {
      expect(names(await sanitizedSteps(lane, [withChild, withShot, video])), lane).toEqual(["has a child", "trace and shot", "video step"]);
    }
  });
});
