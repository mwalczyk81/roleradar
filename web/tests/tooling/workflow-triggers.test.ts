// @vitest-environment node
import { existsSync, readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";
import { checkNightlyWorkflowShape, WORKFLOW_PATH } from "../support/workflow-shape";

// Feature 011 (FR-004, FR-005, SC-005): the nightly quality workflow has exactly three triggers and exposes no secret or
// write permission on the pull request path. The valid fixture below is the shape the real workflow must keep.

const VALID = `
name: Nightly quality
on:
  schedule:
    - cron: "0 2 * * *"
  workflow_dispatch:
  pull_request:
    paths:
      - ".github/workflows/nightly-quality.yml"
permissions: {}
jobs:
  mutation:
    if: github.event_name != 'pull_request' || github.event.pull_request.head.repo.full_name == github.repository
    runs-on: ubuntu-latest
    timeout-minutes: 30
    permissions:
      contents: read
    steps:
      - uses: actions/checkout@0000000000000000000000000000000000000000
      - name: Publish to dashboard
        if: github.event_name != 'pull_request'
        env:
          KEY: \${{ secrets.STRYKER_DASHBOARD_API_KEY }}
        run: echo publish
  report:
    if: always() && (github.event_name != 'pull_request' || github.event.pull_request.head.repo.full_name == github.repository)
    needs: [mutation]
    runs-on: ubuntu-latest
    timeout-minutes: 10
    permissions:
      contents: read
    steps:
      - run: echo report
  publish:
    if: github.event_name != 'pull_request'
    needs: [report]
    runs-on: ubuntu-latest
    timeout-minutes: 10
    permissions:
      contents: write
    steps:
      - run: echo publish
`;

function violationsAfter(edit: (text: string) => string): string[] {
  const edited = edit(VALID);
  expect(edited).not.toBe(VALID);
  return checkNightlyWorkflowShape(edited);
}

describe("nightly-quality workflow trigger shape (fixtures)", () => {
  it("accepts the intended shape", () => {
    expect(checkNightlyWorkflowShape(VALID)).toEqual([]);
  });

  it("rejects a push trigger", () => {
    const found = violationsAfter((t) => t.replace("  workflow_dispatch:\n", "  workflow_dispatch:\n  push:\n    branches: [main]\n"));
    expect(found.join("\n")).toMatch(/triggers must be exactly/);
  });

  it("rejects pull_request_target", () => {
    const found = violationsAfter((t) => t.replace("  pull_request:\n", "  pull_request_target:\n"));
    expect(found.join("\n")).toMatch(/triggers must be exactly/);
  });

  it("rejects a missing schedule", () => {
    const found = violationsAfter((t) => t.replace('  schedule:\n    - cron: "0 2 * * *"\n', ""));
    expect(found.join("\n")).toMatch(/triggers must be exactly/);
  });

  it("rejects a pull_request path filter other than the workflow file", () => {
    const found = violationsAfter((t) => t.replace('".github/workflows/nightly-quality.yml"', '"web/**"'));
    expect(found.join("\n")).toMatch(/pull_request.paths must be exactly/);
  });

  it("rejects extra pull_request keys such as branches", () => {
    const found = violationsAfter((t) => t.replace("  pull_request:\n    paths:", "  pull_request:\n    branches: [main]\n    paths:"));
    expect(found.join("\n")).toMatch(/pull_request must have only/);
  });

  it("rejects a job without the same-repository guard", () => {
    const found = violationsAfter((t) =>
      t.replace(
        "    if: github.event_name != 'pull_request' || github.event.pull_request.head.repo.full_name == github.repository\n    runs-on: ubuntu-latest\n    timeout-minutes: 30",
        "    runs-on: ubuntu-latest\n    timeout-minutes: 30",
      ),
    );
    expect(found.join("\n")).toMatch(/job mutation must be guarded/);
  });

  it("rejects a weakened guard", () => {
    const found = violationsAfter((t) =>
      t.replace(
        "if: github.event_name != 'pull_request'\n    needs: [report]",
        "if: github.event_name != 'pull_request' || true\n    needs: [report]",
      ),
    );
    expect(found.join("\n")).toMatch(/job publish must be guarded/);
  });

  it("rejects a secret reachable on the pull request path", () => {
    const found = violationsAfter((t) => t.replace("        if: github.event_name != 'pull_request'\n        env:", "        env:"));
    expect(found.join("\n")).toMatch(/references secrets without the not-a-pull-request guard/);
  });

  it("rejects a secret at job level on a pull request reachable job", () => {
    const found = violationsAfter((t) =>
      t.replace("    timeout-minutes: 10\n    permissions:\n      contents: read\n    steps:\n      - run: echo report", "    timeout-minutes: 10\n    env:\n      KEY: ${{ secrets.STRYKER_DASHBOARD_API_KEY }}\n    permissions:\n      contents: read\n    steps:\n      - run: echo report"),
    );
    expect(found.join("\n")).toMatch(/references secrets at job level/);
  });

  it("rejects contents write on a job reachable from a pull request", () => {
    const found = violationsAfter((t) => t.replace("      contents: read\n    steps:\n      - uses", "      contents: write\n    steps:\n      - uses"));
    expect(found.join("\n")).toMatch(/grants write permission/);
  });

  it("rejects missing workflow-level permissions", () => {
    const found = violationsAfter((t) => t.replace("permissions: {}\n", ""));
    expect(found.join("\n")).toMatch(/workflow-level permissions/);
  });

  it("rejects a job without a timeout", () => {
    const found = violationsAfter((t) => t.replace("    timeout-minutes: 30\n", ""));
    expect(found.join("\n")).toMatch(/job mutation must set timeout-minutes/);
  });
});

describe("nightly-quality workflow trigger shape (the real file)", () => {
  const file = resolve(process.cwd(), "..", WORKFLOW_PATH);

  it("exists", () => {
    expect(existsSync(file), `${WORKFLOW_PATH} must exist`).toBe(true);
  });

  it("has exactly schedule, workflow_dispatch and the path-filtered pull_request, with no secret or write access on the pull request path", () => {
    const text = readFileSync(file, "utf8");
    expect(checkNightlyWorkflowShape(text)).toEqual([]);
  });
});
