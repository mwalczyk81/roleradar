import { describe, it, expect } from "vitest";
import fs from "node:fs";
import path from "node:path";
import { checkZizmorStep } from "../support/workflow-zizmor";

// Feature 014, story 4 (RR-83, ADR 0048): the zizmor hook runs in its own CI step with GH_TOKEN so its online audits
// (impostor-commit, ref-version-mismatch) run, the other hooks never see the token, and the retired pin script is gone.

const root = path.resolve(process.cwd(), "..");
const ci = fs.readFileSync(path.join(root, ".github", "workflows", "ci.yml"), "utf8");

const MAIN_STEP_SKIP = "SKIP: roleradar-denylist,no-commit-to-branch,zizmor";

describe("the zizmor step in ci.yml", () => {
  it("holds for the real ci.yml", () => {
    expect(checkZizmorStep(ci)).toEqual([]);
  });

  it("is found: the real file has the token on a step that runs the zizmor hook", () => {
    expect(ci).toMatch(/pre-commit run zizmor --all-files/);
    expect(ci).toContain("GH_TOKEN: ${{ github.token }}");
  });

  it("catches a missing zizmor step", () => {
    const broken = ci.replace("pre-commit run zizmor --all-files", "echo skipped");
    expect(broken).not.toBe(ci);
    expect(checkZizmorStep(broken).join("\n")).toMatch(/exactly one step that runs the zizmor hook, found 0/);
  });

  it("catches a zizmor step without the token", () => {
    const broken = ci.replace("GH_TOKEN: ${{ github.token }}\n        run: pre-commit run zizmor", "FOO: bar\n        run: pre-commit run zizmor");
    expect(broken).not.toBe(ci);
    expect(checkZizmorStep(broken).join("\n")).toMatch(/must set GH_TOKEN: \$\{\{ github\.token \}\}/);
  });

  it("catches a token that is not the workflow token", () => {
    const broken = ci.replace("GH_TOKEN: ${{ github.token }}\n        run: pre-commit run zizmor", "GH_TOKEN: ${{ secrets.SOME_PAT }}\n        run: pre-commit run zizmor");
    expect(broken).not.toBe(ci);
    expect(checkZizmorStep(broken).join("\n")).toMatch(/must set GH_TOKEN/);
  });

  it("catches a zizmor step that runs anything more than the zizmor hook", () => {
    const broken = ci.replace("pre-commit run zizmor --all-files", "pre-commit run --all-files");
    expect(broken).not.toBe(ci);
    expect(checkZizmorStep(broken).join("\n")).toMatch(/must run only 'pre-commit run zizmor --all-files'|exactly one step/);
  });

  it("catches a main pre-commit step that no longer skips zizmor", () => {
    const broken = ci.replace(MAIN_STEP_SKIP, "SKIP: roleradar-denylist,no-commit-to-branch");
    expect(broken).not.toBe(ci);
    expect(checkZizmorStep(broken).join("\n")).toMatch(/without SKIP containing zizmor/);
  });

  it("catches the token leaking to the other hooks through the main step", () => {
    const broken = ci.replace(MAIN_STEP_SKIP, `${MAIN_STEP_SKIP}\n          GH_TOKEN: \${{ github.token }}`);
    expect(broken).not.toBe(ci);
    expect(checkZizmorStep(broken).join("\n")).toMatch(/sets a token variable; only the zizmor step may/);
  });

  it("catches the token set for the whole job", () => {
    const broken = ci.replace("  pre-commit:\n    name:", "  pre-commit:\n    env:\n      GH_TOKEN: ${{ github.token }}\n    name:");
    expect(broken).not.toBe(ci);
    expect(checkZizmorStep(broken).join("\n")).toMatch(/job-level env sets a token variable/);
  });

  it("catches a zizmor step that a condition can skip or a failure cannot stop", () => {
    const skipped = ci.replace("      - name: Run zizmor", "      - if: github.event_name == 'push'\n        name: Run zizmor");
    expect(skipped).not.toBe(ci);
    expect(checkZizmorStep(skipped).join("\n")).toMatch(/no if condition/);
    const lenient = ci.replace("        run: pre-commit run zizmor", "        continue-on-error: true\n        run: pre-commit run zizmor");
    expect(lenient).not.toBe(ci);
    expect(checkZizmorStep(lenient).join("\n")).toMatch(/no continue-on-error/);
  });

  it("catches the retired pin script coming back", () => {
    const broken = ci.replace("pre-commit run zizmor --all-files", "pwsh -File scripts/verify-action-pins.ps1");
    expect(broken).not.toBe(ci);
    expect(checkZizmorStep(broken).join("\n")).toMatch(/verify-action-pins/);
  });

  it("no longer ships scripts/verify-action-pins.ps1", () => {
    expect(fs.existsSync(path.join(root, "scripts", "verify-action-pins.ps1"))).toBe(false);
  });
});
