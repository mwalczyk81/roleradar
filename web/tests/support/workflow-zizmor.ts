import { parse } from "yaml";

// Feature 014, story 4 (RR-83, ADR 0048). zizmor's online audits (impostor-commit: a pinned SHA that is not in the action's
// repository; ref-version-mismatch: a version comment that does not match the pin) run only when zizmor has a token, and
// the pre-commit hook goes offline silently without one. This is why verify-action-pins.ps1 could be retired. The
// rules below keep the token on exactly one step that runs only the zizmor hook, keep the other hooks away from it, and
// keep the retired script out of the workflow. checkZizmorStep returns one message per broken rule, so the same check
// proves the real ci.yml passes and that a broken copy is caught.

type Obj = Record<string, unknown>;

function isObj(value: unknown): value is Obj {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function normalize(value: unknown): string {
  return typeof value === "string" ? value.replace(/\s+/g, " ").trim() : "";
}

const TOKEN_EXPRESSION = "${{ github.token }}";
const ZIZMOR_COMMAND = /^pre-commit run zizmor --all-files(?: (?:--verbose|-v|--show-diff-on-failure))*$/;
const TOKEN_NAMES = ["GH_TOKEN", "GITHUB_TOKEN"];

function stepsOf(doc: unknown): Obj[] {
  if (!isObj(doc) || !isObj(doc["jobs"])) {
    return [];
  }
  const job = doc["jobs"]["pre-commit"];
  if (!isObj(job) || !Array.isArray(job["steps"])) {
    return [];
  }
  return job["steps"].filter(isObj);
}

function envOf(holder: unknown): Obj {
  return isObj(holder) && isObj(holder["env"]) ? holder["env"] : {};
}

function runOf(step: Obj): string {
  return typeof step["run"] === "string" ? step["run"] : "";
}

function isZizmorStep(step: Obj): boolean {
  return normalize(runOf(step)).startsWith("pre-commit run zizmor");
}

function setsToken(holder: unknown): boolean {
  const env = envOf(holder);
  return TOKEN_NAMES.some((name) => name in env);
}

export function checkZizmorStep(ciText: string): string[] {
  const doc: unknown = parse(ciText);
  const steps = stepsOf(doc);
  const violations: string[] = [];

  if (steps.length === 0) {
    return ["the pre-commit job was not found in ci.yml"];
  }

  const zizmorSteps = steps.filter(isZizmorStep);
  if (zizmorSteps.length !== 1) {
    violations.push(`the pre-commit job must have exactly one step that runs the zizmor hook, found ${zizmorSteps.length}`);
  }

  for (const step of zizmorSteps) {
    const command = normalize(runOf(step));
    if (!ZIZMOR_COMMAND.test(command)) {
      violations.push(`the zizmor step must run only 'pre-commit run zizmor --all-files' (with --verbose so the log shows the audit), not '${command}'`);
    }
    if (normalize(envOf(step)["GH_TOKEN"]) !== TOKEN_EXPRESSION) {
      violations.push(`the zizmor step must set GH_TOKEN: ${TOKEN_EXPRESSION} so the online audits run`);
    }
    if (step["if"] !== undefined) {
      violations.push("the zizmor step must run on every event, with no if condition");
    }
    if (step["continue-on-error"] !== undefined && step["continue-on-error"] !== false) {
      violations.push("the zizmor step must fail the job on a finding (no continue-on-error)");
    }
  }

  // The other hooks must never see the token, and must skip zizmor so it runs only once, with the token.
  const others = steps.filter((step) => !zizmorSteps.includes(step));
  for (const step of others) {
    if (setsToken(step)) {
      violations.push(`step '${normalize(step["name"])}' sets a token variable; only the zizmor step may`);
    }
    if (normalize(runOf(step)).includes("pre-commit run")) {
      const skip = normalize(envOf(step)["SKIP"]).split(",").map((id) => id.trim());
      if (!skip.includes("zizmor")) {
        violations.push(`step '${normalize(step["name"])}' runs the other hooks without SKIP containing zizmor, so zizmor would run offline there`);
      }
    }
  }

  if (isObj(doc)) {
    const job = isObj(doc["jobs"]) ? doc["jobs"]["pre-commit"] : undefined;
    if (setsToken(doc) || setsToken(job)) {
      violations.push("a workflow-level or job-level env sets a token variable, which every step would see");
    }
  }

  // The install step has to come before the zizmor step.
  const installAt = steps.findIndex((step) => normalize(runOf(step)).includes("pipx install pre-commit"));
  const zizmorAt = steps.findIndex(isZizmorStep);
  if (zizmorAt >= 0 && (installAt < 0 || installAt > zizmorAt)) {
    violations.push("pre-commit must be installed before the zizmor step runs");
  }

  // The retired script must not come back through a workflow.
  const anyMention = steps.some((step) => normalize(runOf(step)).includes("verify-action-pins"));
  if (anyMention) {
    violations.push("a step still runs scripts/verify-action-pins.ps1, which ADR 0048 retired");
  }

  return violations;
}
