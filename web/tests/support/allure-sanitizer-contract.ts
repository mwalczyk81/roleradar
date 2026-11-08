import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { createHash } from "node:crypto";

// Feature 011, story 2b (FR-028, FR-029). The contract every Allure sanitizer must meet before a report is published.
// checkSanitizer builds a fictional results folder, runs the sanitizer on it and returns one message per broken rule,
// so the same checks can prove the real sanitizer passes and that a broken one (a no-op, or one that only looks at
// top-level attachments) is caught. Fictional names only; nothing here is a real user, host or path.

export type Lane = "fictional" | "real-stack";

export type SanitizeFunction = (options: { inputDir: string; outputDir: string; lane: Lane; redact?: string[] }) => Promise<unknown>;

export const FIXTURE_USER = "fixtureuser";
export const FIXTURE_HOST = "FIXTUREHOST";

const PNG_BYTES = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0x01, 0x02, 0x03]);
const ZIP_BYTES = Buffer.from([0x50, 0x4b, 0x03, 0x04, 0x0a, 0x0b, 0x0c]);

type Attachment = { name: string; source: string; type: string };

function result(id: string, name: string, status: string, extra: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    uuid: id,
    historyId: `history-${id}`,
    testCaseId: `case-${id}`,
    name,
    fullName: `suite.spec.ts > ${name}`,
    status,
    stage: "finished",
    start: 1_700_000_000_000,
    stop: 1_700_000_000_500,
    labels: [
      { name: "framework", value: "playwright" },
      { name: "host", value: FIXTURE_HOST },
      { name: "thread", value: "pid-1-worker-0" },
      { name: "suite", value: "suite.spec.ts" },
    ],
    links: [],
    parameters: [],
    attachments: [] as Attachment[],
    steps: [] as unknown[],
    ...extra,
  };
}

export const NESTED_SOURCES = {
  deepTrace: "11111111-deep-attachment.zip",
  midScreenshot: "22222222-mid-attachment.png",
  topLog: "33333333-top-attachment.txt",
  containerShot: "44444444-container-attachment.png",
};

function writeFixture(inputDir: string): void {
  fs.mkdirSync(inputDir, { recursive: true });
  const failing = result("aaaa", "fails with evidence", "failed", {
    statusDetails: {
      message: `Error: expected 1 to be 2 at C:\\Users\\${FIXTURE_USER}\\work\\roleradar\\web\\app\\lib\\x.ts:10:5 on ${FIXTURE_HOST}`,
      trace: `at fn (/home/${FIXTURE_USER}/work/roleradar/web/app/lib/y.ts:20:7)\nat web/app/lib/format-date.ts:9:7\nat file:///C:/Users/${FIXTURE_USER}/z.ts:1:1`,
    },
    parameters: [{ name: "path", value: `C:\\Users\\${FIXTURE_USER}\\secret.txt` }],
    attachments: [{ name: "log", source: NESTED_SOURCES.topLog, type: "text/plain" }],
    steps: [
      {
        name: "Before Hooks",
        steps: [
          {
            name: `open https://example.test/home/page from ${FIXTURE_HOST}`,
            attachments: [{ name: "screenshot", source: NESTED_SOURCES.midScreenshot, type: "image/png" }],
            steps: [
              {
                name: "deeper step",
                steps: [{ name: "trace", attachments: [{ name: "trace", source: NESTED_SOURCES.deepTrace, type: "application/zip" }] }],
              },
            ],
          },
        ],
      },
    ],
  });
  const passing = result("bbbb", "passes", "passed");
  const container = {
    uuid: "cccc",
    name: "suite",
    children: ["aaaa", "bbbb"],
    befores: [
      {
        name: "setup",
        steps: [{ name: "inner", attachments: [{ name: "shot", source: NESTED_SOURCES.containerShot, type: "image/png" }] }],
      },
    ],
    afters: [],
  };
  fs.writeFileSync(path.join(inputDir, "aaaa-result.json"), JSON.stringify(failing));
  fs.writeFileSync(path.join(inputDir, "bbbb-result.json"), JSON.stringify(passing));
  fs.writeFileSync(path.join(inputDir, "cccc-container.json"), JSON.stringify(container));
  fs.writeFileSync(path.join(inputDir, NESTED_SOURCES.deepTrace), ZIP_BYTES);
  fs.writeFileSync(path.join(inputDir, NESTED_SOURCES.midScreenshot), PNG_BYTES);
  fs.writeFileSync(path.join(inputDir, NESTED_SOURCES.containerShot), PNG_BYTES);
  fs.writeFileSync(
    path.join(inputDir, NESTED_SOURCES.topLog),
    `visited /home/${FIXTURE_USER}/work/page on ${FIXTURE_HOST} as ${FIXTURE_USER}\nrelative web/app/x.ts stays\n`,
  );
  fs.writeFileSync(path.join(inputDir, "environment.properties"), `host=${FIXTURE_HOST}\nuser=${FIXTURE_USER}\nnode=22\n`);
  // The native xUnit v3 -junit output names the test assembly by absolute path and records the machine name.
  fs.writeFileSync(
    path.join(inputDir, "Unit.xml"),
    `<testsuites name="Test results" tests="2" failures="1"><testsuite name="C:\\Users\\${FIXTURE_USER}\\src\\RoleRadar.Tests.Unit.dll" tests="2" failures="1" hostname="${FIXTURE_HOST}">` +
      `<testcase name="RoleRadar.Tests.Unit.Fine" classname="RoleRadar.Tests.Unit" time="0.1" />` +
      `<testcase name="RoleRadar.Tests.Unit.Broken" classname="RoleRadar.Tests.Unit"><failure message="at /home/${FIXTURE_USER}/x.ts:1">at C:\\Users\\${FIXTURE_USER}\\y.cs:line 5</failure></testcase>` +
      `</testsuite></testsuites>`,
  );
}

function listFiles(dir: string): string[] {
  const found: string[] = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      found.push(...listFiles(full));
    } else {
      found.push(full);
    }
  }
  return found.sort();
}

function digest(dir: string): string {
  const hash = createHash("sha256");
  for (const file of listFiles(dir)) {
    hash.update(path.relative(dir, file));
    hash.update(fs.readFileSync(file));
  }
  return hash.digest("hex");
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

/** Every attachments array found at any depth, in objects and arrays. */
function collectAttachments(value: unknown, into: Attachment[] = []): Attachment[] {
  if (Array.isArray(value)) {
    for (const item of value) {
      collectAttachments(item, into);
    }
  } else if (isRecord(value)) {
    for (const [key, child] of Object.entries(value)) {
      if (key === "attachments" && Array.isArray(child)) {
        for (const attachment of child) {
          if (isRecord(attachment) && typeof attachment["source"] === "string") {
            into.push({ name: String(attachment["name"]), source: attachment["source"], type: String(attachment["type"]) });
          }
        }
      }
      collectAttachments(child, into);
    }
  }
  return into;
}

function readJson(file: string): unknown {
  return JSON.parse(fs.readFileSync(file, "utf8")) as unknown;
}

export async function checkSanitizer(sanitize: SanitizeFunction, lane: Lane): Promise<string[]> {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "rr-allure-contract-"));
  const violations: string[] = [];
  try {
    const inputDir = path.join(root, "in");
    const outputDir = path.join(root, "out");
    writeFixture(inputDir);
    const before = digest(inputDir);

    await sanitize({ inputDir, outputDir, lane, redact: [FIXTURE_USER, FIXTURE_HOST] });

    if (!fs.existsSync(outputDir)) {
      return ["the sanitizer produced no output directory"];
    }
    if (digest(inputDir) !== before) {
      violations.push("the sanitizer modified its input directory");
    }

    const outputFiles = listFiles(outputDir);
    const outputNames = outputFiles.map((file) => path.basename(file));
    const jsonFiles = outputFiles.filter((file) => file.endsWith(".json"));
    const allText = outputFiles
      .filter((file) => /\.(json|txt|md|properties|xml)$/.test(file))
      .map((file) => fs.readFileSync(file, "utf8"))
      .join("\n");

    // Rule: results survive with their outcome and timings.
    const resultFiles = jsonFiles.filter((file) => file.endsWith("-result.json"));
    if (resultFiles.length !== 2) {
      violations.push(`expected 2 result files in the output, found ${resultFiles.length}`);
    }
    for (const file of resultFiles) {
      const parsed = readJson(file);
      if (!isRecord(parsed)) {
        violations.push(`${path.basename(file)} is not a JSON object`);
        continue;
      }
      const expectedStatus = parsed["uuid"] === "aaaa" ? "failed" : "passed";
      if (parsed["status"] !== expectedStatus || parsed["start"] !== 1_700_000_000_000 || parsed["stop"] !== 1_700_000_000_500) {
        violations.push(`${path.basename(file)} lost its status or timings`);
      }
      if (parsed["historyId"] !== `history-${String(parsed["uuid"])}`) {
        violations.push(`${path.basename(file)} lost its historyId`);
      }
      // Rule: host label removed, lane label set once to the lane of this results folder.
      const labels = Array.isArray(parsed["labels"]) ? parsed["labels"] : [];
      if (labels.some((label) => isRecord(label) && label["name"] === "host")) {
        violations.push(`${path.basename(file)} still carries the host label`);
      }
      const laneLabels = labels.filter((label) => isRecord(label) && label["name"] === "lane");
      if (laneLabels.length !== 1 || !isRecord(laneLabels[0]) || laneLabels[0]["value"] !== lane) {
        violations.push(`${path.basename(file)} must carry exactly one lane label equal to ${lane}`);
      }
    }

    // Rule: a JUnit file keeps every test case and its outcome, with no path or host in it.
    const junit = outputFiles.find((file) => path.basename(file) === "Unit.xml");
    if (!junit) {
      violations.push("the JUnit file Unit.xml did not reach the output");
    } else {
      const xml = fs.readFileSync(junit, "utf8");
      if ((xml.match(/<testcase /g) ?? []).length !== 2 || !xml.includes("<failure")) {
        violations.push("Unit.xml lost a test case or a failure");
      }
    }

    // Rule: attachments.
    const remaining = jsonFiles.flatMap((file) => collectAttachments(readJson(file)));
    if (lane === "real-stack") {
      if (remaining.length > 0) {
        violations.push(`real-stack output still has ${remaining.length} attachment reference(s), nested ones included`);
      }
      const attachmentFiles = outputNames.filter((name) => /attachment\./.test(name));
      if (attachmentFiles.length > 0) {
        violations.push(`real-stack output still has attachment file(s): ${attachmentFiles.join(", ")}`);
      }
    } else {
      // Fictional lane: a trace archive is never published, screenshots and text are kept (nested ones too).
      if (outputNames.includes(NESTED_SOURCES.deepTrace) || allText.includes(NESTED_SOURCES.deepTrace)) {
        violations.push("a trace archive nested three steps deep reached the output");
      }
      for (const kept of [NESTED_SOURCES.midScreenshot, NESTED_SOURCES.containerShot, NESTED_SOURCES.topLog]) {
        if (!outputNames.includes(kept) || !remaining.some((attachment) => attachment.source === kept)) {
          violations.push(`fictional attachment ${kept} was not kept with its reference`);
        }
      }
      const screenshotPath = outputFiles.find((file) => path.basename(file) === NESTED_SOURCES.midScreenshot);
      if (screenshotPath && !fs.readFileSync(screenshotPath).equals(PNG_BYTES)) {
        violations.push("a kept screenshot was altered");
      }
    }

    // Rule: no absolute path, user or host in any text that is published.
    const leakPatterns: Array<[string, RegExp]> = [
      ["Windows drive path", /(?<![A-Za-z0-9])[A-Za-z]:(?:\\{1,4}|\/)[A-Za-z0-9_.$]/],
      ["POSIX home path", /(?<![A-Za-z0-9_.:/-])\/(?:home|Users|root)\/[A-Za-z0-9_.-]/],
      ["fixture user", new RegExp(FIXTURE_USER, "i")],
      ["fixture host", new RegExp(FIXTURE_HOST, "i")],
    ];
    for (const [rule, pattern] of leakPatterns) {
      if (pattern.test(allText)) {
        violations.push(`output still contains a ${rule}`);
      }
    }
    // Rule: ordinary content survives scrubbing (relative paths, public URLs, plain text).
    if (!allText.includes("web/app/lib/format-date.ts:9:7")) {
      violations.push("a repository-relative path was damaged");
    }
    if (!allText.includes("https://example.test/home/page")) {
      violations.push("a public URL containing /home/ was damaged");
    }
    return violations;
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
}
