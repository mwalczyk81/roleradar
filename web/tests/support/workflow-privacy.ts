import { parse } from "yaml";

// Feature 011, story 2b (FR-027, FR-028). Workflow-level privacy rules for the Allure report: real-stack results leave
// their job only after the sanitizer, nothing raw from the real-stack job is uploaded as report input, the final job
// sanitizes every input with the right lane, and the publish steps stage only the sanitized combined report.
// checkAllurePrivacy returns one message per broken rule, so the same check can prove the real workflows pass and that
// a broken copy is caught.

type Step = { name?: string; uses?: string; run?: string; if?: string; with?: { name?: string; path?: string; publish_dir?: string; force_orphan?: boolean | string } };
type Job = { steps?: Step[]; if?: string; permissions?: Record<string, string> };
type Workflow = { jobs?: Record<string, Job> };

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function jobsOf(text: string): Record<string, Job> {
  const doc: unknown = parse(text);
  return isRecord(doc) && isRecord(doc["jobs"]) ? (doc["jobs"] as Workflow["jobs"] as Record<string, Job>) : {};
}

function stepsOf(job: Job | undefined): Step[] {
  return Array.isArray(job?.steps) ? job.steps : [];
}

function isUpload(step: Step): boolean {
  return typeof step.uses === "string" && step.uses.startsWith("actions/upload-artifact@");
}

export function checkAllurePrivacy(ciText: string, nightlyText: string): string[] {
  const violations: string[] = [];
  const ci = jobsOf(ciText);
  const nightly = jobsOf(nightlyText);

  // 1. No upload anywhere carries the raw real-stack results folder.
  for (const [file, jobs] of [["ci.yml", ci], ["nightly-quality.yml", nightly]] as const) {
    for (const [jobName, job] of Object.entries(jobs)) {
      for (const step of stepsOf(job).filter(isUpload)) {
        const uploadPath = step.with?.path ?? "";
        if (/allure-input\/real-stack/.test(uploadPath)) {
          violations.push(`${file} job ${jobName} uploads the raw real-stack results (${step.with?.name ?? "unnamed"})`);
        }
      }
    }
  }

  // 2. The real-stack job sanitizes with the real-stack lane before it uploads the report input.
  const realStackSteps = stepsOf(ci["e2e-real-stack"]);
  const sanitizeIndex = realStackSteps.findIndex((step) => /allure-sanitize\.mjs/.test(step.run ?? "") && /--lane real-stack/.test(step.run ?? ""));
  const uploadIndex = realStackSteps.findIndex((step) => isUpload(step) && step.with?.name === "allure-in-real-stack");
  if (sanitizeIndex < 0) {
    violations.push("the e2e-real-stack job does not sanitize its Allure results with --lane real-stack");
  }
  if (uploadIndex < 0) {
    violations.push("the e2e-real-stack job does not upload allure-in-real-stack");
  } else {
    if (realStackSteps[uploadIndex]?.with?.path !== "allure-sanitized/real-stack/") {
      violations.push("allure-in-real-stack must upload allure-sanitized/real-stack/ and nothing else");
    }
    if (sanitizeIndex < 0 || sanitizeIndex > uploadIndex) {
      violations.push("the real-stack results must be sanitized before they are uploaded");
    }
  }

  // 3. The final job maps the real-stack input to the real-stack lane and sanitizes every input.
  const mergeRun = stepsOf(ci["allure-report"]).find((step) => /allure-sanitize\.mjs/.test(step.run ?? ""))?.run ?? "";
  if (!/real-stack/.test(mergeRun) || !/lane=real-stack/.test(mergeRun) || !/--lane "\$lane"/.test(mergeRun)) {
    violations.push("the ci.yml allure-report job does not sanitize each input with the lane of its source (real-stack must use --lane real-stack)");
  }
  if (/cp -r "?allure-input/.test(mergeRun)) {
    violations.push("the ci.yml allure-report job copies unsanitized input into the merged folder");
  }
  const nightlyMerge = stepsOf(nightly["allure-report"]).find((step) => /allure-sanitize\.mjs/.test(step.run ?? ""))?.run ?? "";
  if (!/allure-sanitize\.mjs/.test(nightlyMerge) || /cp -r "?allure-input/.test(nightlyMerge)) {
    violations.push("the nightly allure-report job does not sanitize its inputs before merging");
  }
  if (Object.keys(nightly).some((name) => /real-stack/.test(name))) {
    violations.push("the nightly workflow must not run the real-stack suite (its results are never published)");
  }

  // 4. Both reports are leak-checked before and after generating.
  for (const [file, jobs] of [["ci.yml", ci], ["nightly-quality.yml", nightly]] as const) {
    const runs = stepsOf(jobs["allure-report"]).map((step) => step.run ?? "");
    if (!runs.some((run) => /--check allure-merged/.test(run)) || !runs.some((run) => /--check allure-report/.test(run))) {
      violations.push(`${file} allure-report job must leak-check allure-merged and allure-report`);
    }
  }

  // 5. Publish steps stage only the sanitized combined report. There is one deploy per publishing job
  //    (peaceiris/actions-gh-pages with force_orphan, so the branch stays a single commit) and its publish_dir is site/,
  //    which the staging step fills; the Allure part of site/ comes only from the allure-report artifact.
  const allowedDownloads = new Set(["run-summary", "coverage-report", "allure-report", "allure-history"]);
  const ciPublish = stepsOf(ci["metrics-and-pages"]);
  const nightlyPublish = stepsOf(nightly["publish-allure"]);
  const stagedFromInput = ciPublish.map((step) => `${step.run ?? ""} ${JSON.stringify(step.with ?? {})}`).join(" ");
  if (/allure-input|allure-sanitized|allure-in-/.test(stagedFromInput)) {
    violations.push("the ci.yml publish job reads Allure inputs directly instead of the combined report artifact");
  }
  for (const [file, steps] of [["ci.yml", ciPublish], ["nightly-quality.yml", nightlyPublish]] as const) {
    for (const step of steps.filter((candidate) => candidate.uses?.startsWith("actions/download-artifact@"))) {
      if (!allowedDownloads.has(step.with?.name ?? "")) {
        violations.push(`the ${file} publish job downloads ${step.with?.name ?? "an artifact"}, which is not part of the publish input`);
      }
    }
    const stageRun = steps.find((step) => /site\/allure/.test(step.run ?? ""))?.run ?? "";
    if (!/cp -r publish-input\/allure site\/allure/.test(stageRun)) {
      violations.push(`the ${file} publish job must stage site/allure only from publish-input/allure (the allure-report artifact)`);
    }
    if (/allure-input|allure-sanitized|allure-in-/.test(stageRun)) {
      violations.push(`the ${file} publish job stages Allure inputs directly instead of the combined report artifact`);
    }
    const deploys = steps.filter((step) => step.uses?.startsWith("peaceiris/actions-gh-pages@"));
    if (deploys.length !== 1) {
      violations.push(`${file} publish job must have exactly one peaceiris/actions-gh-pages step, found ${deploys.length}`);
    }
    for (const step of deploys) {
      const folder = step.with?.publish_dir ?? "";
      if (folder !== "site") {
        violations.push(`${file} publishes ${folder || "an unset folder"}, which is not the staged site folder`);
      }
      if (String(step.with?.force_orphan) !== "true") {
        violations.push(`${file} must publish with force_orphan: true so gh-pages stays a single commit`);
      }
    }
  }

  // 6. The sticky pull request comment (job pr-report) is built only from the run summary, never from result files, and
  //    only that job may write pull requests.
  const prJob = ci["pr-report"];
  const prSteps = stepsOf(prJob);
  if (!prJob) {
    violations.push("ci.yml has no pr-report job (the sticky pull request comment)");
  } else {
    const workflowPermissions = permissionsOf(ciText);
    if (workflowPermissions["pull-requests"] !== undefined) {
      violations.push("ci.yml grants pull-requests at workflow level; only the pr-report job may");
    }
    if (prJob.permissions?.["pull-requests"] !== "write") {
      violations.push("the ci.yml pr-report job must have job-scoped permissions: pull-requests: write");
    }
    for (const [name, job] of Object.entries(ci)) {
      if (name !== "pr-report" && job.permissions?.["pull-requests"] === "write") {
        violations.push(`ci.yml job ${name} has pull-requests: write; only pr-report may`);
      }
    }
    for (const step of prSteps.filter((candidate) => candidate.uses?.startsWith("actions/download-artifact@"))) {
      if (step.with?.name !== "run-summary") {
        violations.push(`the ci.yml pr-report job downloads ${step.with?.name ?? "an artifact"}; the comment may be built only from run-summary`);
      }
    }
    const prText = prSteps.map((step) => `${step.run ?? ""} ${JSON.stringify(step.with ?? {})}`).join("\n");
    if (/allure-input|allure-sanitized|allure-merged|allure-report\b|allure-in-|TestResults|\.trx\b|real-stack/.test(prText.replace(/pull request report/g, ""))) {
      violations.push("the ci.yml pr-report job reads result files; the comment body must be built only from the run summary");
    }
    if (!prSteps.some((step) => /metrics-record\.mjs comment\b/.test(step.run ?? ""))) {
      violations.push("the ci.yml pr-report job must build the comment with metrics-record.mjs comment");
    }
    const jobIf = prJob.if ?? "";
    const gateText = prSteps.map((step) => step.run ?? "").join("\n");
    if (!/head\.repo\.full_name|HEAD_REPO/.test(`${jobIf}\n${gateText}`) || !/dependabot\[bot\]/.test(`${jobIf}\n${gateText}`)) {
      violations.push("the ci.yml pr-report job must comment only for same-repository pull requests not opened by dependabot[bot]");
    }
  }
  return violations;
}

function permissionsOf(text: string): Record<string, unknown> {
  const doc: unknown = parse(text);
  return isRecord(doc) && isRecord(doc["permissions"]) ? doc["permissions"] : {};
}
