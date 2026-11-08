import { parse } from "yaml";

// Trigger-shape rules for .github/workflows/nightly-quality.yml (feature 011, FR-004, FR-005).
// The workflow runs mutation testing. It may run on a schedule, on manual dispatch, and as one no-publish validation run
// on same-repository pull requests that change the workflow file itself. Anything else, or any secret or write
// permission reachable on the pull request path, is a violation.

export const WORKFLOW_PATH = ".github/workflows/nightly-quality.yml";

const SAME_REPO_GUARD =
  "github.event_name != 'pull_request' || github.event.pull_request.head.repo.full_name == github.repository";
const NO_PR_GUARD = "github.event_name != 'pull_request'";
const EXPECTED_TRIGGERS = ["pull_request", "schedule", "workflow_dispatch"];

type Obj = Record<string, unknown>;

function isObj(value: unknown): value is Obj {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function normalizeIf(value: unknown): string | null {
  if (typeof value !== "string") {
    return null;
  }
  let text = value.trim();
  const wrapped = /^\$\{\{\s*([\s\S]*?)\s*\}\}$/.exec(text);
  if (wrapped?.[1] !== undefined) {
    text = wrapped[1].trim();
  }
  return text.replace(/\s+/g, " ");
}

function stripAlways(text: string): string {
  return text.startsWith("always() && ") ? text.slice("always() && ".length) : text;
}

function stripParens(text: string): string {
  return text.startsWith("(") && text.endsWith(")") ? text.slice(1, -1).trim() : text;
}

// A job or step is "guarded" when its condition is exactly the same-repository guard or exactly the
// not-a-pull-request guard, optionally preceded by always() &&.
export function isSameRepoGuarded(ifValue: unknown): boolean {
  const text = normalizeIf(ifValue);
  if (text === null) {
    return false;
  }
  const core = stripParens(stripAlways(text));
  return core === SAME_REPO_GUARD || core === NO_PR_GUARD;
}

export function isNoPullRequest(ifValue: unknown): boolean {
  const text = normalizeIf(ifValue);
  if (text === null) {
    return false;
  }
  return stripParens(stripAlways(text)) === NO_PR_GUARD;
}

function mentionsSecret(value: unknown): boolean {
  return JSON.stringify(value ?? null).includes("secrets.");
}

function hasWritePermission(value: unknown): boolean {
  if (typeof value === "string") {
    return value === "write-all";
  }
  if (!isObj(value)) {
    return false;
  }
  return Object.values(value).some((entry) => entry === "write");
}

export function checkNightlyWorkflowShape(yamlText: string): string[] {
  const violations: string[] = [];
  let doc: unknown;
  try {
    doc = parse(yamlText);
  } catch (error) {
    return [`workflow YAML does not parse: ${error instanceof Error ? error.message : String(error)}`];
  }
  if (!isObj(doc)) {
    return ["workflow YAML is not a mapping"];
  }

  const on = doc["on"];
  if (!isObj(on)) {
    violations.push("`on:` must be a mapping with exactly schedule, workflow_dispatch and pull_request");
  } else {
    const keys = Object.keys(on).sort();
    if (JSON.stringify(keys) !== JSON.stringify(EXPECTED_TRIGGERS)) {
      violations.push(`triggers must be exactly ${EXPECTED_TRIGGERS.join(", ")}; found ${keys.join(", ") || "none"}`);
    }
    const schedule = on["schedule"];
    if (!Array.isArray(schedule) || schedule.length === 0) {
      violations.push("schedule must list at least one cron entry");
    }
    const pullRequest = on["pull_request"];
    if (!isObj(pullRequest)) {
      violations.push("pull_request must be a mapping with only `paths`");
    } else {
      const prKeys = Object.keys(pullRequest);
      if (JSON.stringify(prKeys) !== JSON.stringify(["paths"])) {
        violations.push(`pull_request must have only \`paths\`; found ${prKeys.join(", ") || "none"}`);
      }
      const paths = pullRequest["paths"];
      if (JSON.stringify(paths) !== JSON.stringify([WORKFLOW_PATH])) {
        violations.push(`pull_request.paths must be exactly [${WORKFLOW_PATH}]`);
      }
    }
  }

  if (mentionsSecret(doc["env"])) {
    violations.push("workflow-level env must not reference secrets");
  }
  if (!isObj(doc["permissions"]) || Object.keys(doc["permissions"]).length !== 0) {
    violations.push("workflow-level permissions must be exactly {} (least privilege, grants per job)");
  }

  const jobs = doc["jobs"];
  if (!isObj(jobs) || Object.keys(jobs).length === 0) {
    violations.push("workflow must define jobs");
    return violations;
  }

  for (const [name, rawJob] of Object.entries(jobs)) {
    if (!isObj(rawJob)) {
      violations.push(`job ${name} is not a mapping`);
      continue;
    }
    if (!isSameRepoGuarded(rawJob["if"])) {
      violations.push(`job ${name} must be guarded by the same-repository condition or the not-a-pull-request condition`);
    }
    if (typeof rawJob["timeout-minutes"] !== "number") {
      violations.push(`job ${name} must set timeout-minutes`);
    }
    const jobNoPr = isNoPullRequest(rawJob["if"]);
    if (hasWritePermission(rawJob["permissions"]) && !jobNoPr) {
      violations.push(`job ${name} grants write permission on a path reachable from a pull request`);
    }
    const { steps, ...jobWithoutSteps } = rawJob;
    if (mentionsSecret(jobWithoutSteps) && !jobNoPr) {
      violations.push(`job ${name} references secrets at job level on a path reachable from a pull request`);
    }
    if (Array.isArray(steps)) {
      steps.forEach((step, index) => {
        if (!mentionsSecret(step)) {
          return;
        }
        const stepNoPr = isObj(step) && isNoPullRequest(step["if"]);
        if (!jobNoPr && !stepNoPr) {
          violations.push(`job ${name} step ${index + 1} references secrets without the not-a-pull-request guard`);
        }
      });
    }
  }
  return violations;
}
