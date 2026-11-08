import { parse } from "yaml";

// Feature 011, story 2b (FR-027, FR-028). Workflow-level privacy rules for the Allure report: real-stack results leave
// their job only after the sanitizer, nothing raw from the real-stack job is uploaded as report input, the final job
// sanitizes every input with the right lane, and the publish steps stage only the sanitized combined report.
// checkAllurePrivacy returns one message per broken rule, so the same check can prove the real workflows pass and that
// a broken copy is caught.

type Step = { name?: string; uses?: string; run?: string; if?: string; with?: { name?: string; path?: string; publish_dir?: string; destination_dir?: string } };
type Job = { steps?: Step[] };
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

  // 5. Publish steps stage only the sanitized combined report. The publish input is what peaceiris/actions-gh-pages is
  //    given (publish_dir): only the three output folders the staging step fills, and the Allure one only from the
  //    allure-report artifact.
  const publishFolders = new Set(["site-keep", "site-coverage", "site-allure"]);
  const allowedDownloads = new Set(["run-summary", "coverage-report", "allure-report", "allure-history"]);
  const ciPublish = stepsOf(ci["metrics-and-pages"]);
  const stagedFromInput = ciPublish.map((step) => `${step.run ?? ""} ${JSON.stringify(step.with ?? {})}`).join(" ");
  if (/allure-input|allure-sanitized|allure-in-/.test(stagedFromInput)) {
    violations.push("the ci.yml publish job reads Allure inputs directly instead of the combined report artifact");
  }
  for (const step of ciPublish.filter((candidate) => candidate.uses?.startsWith("actions/download-artifact@"))) {
    if (!allowedDownloads.has(step.with?.name ?? "")) {
      violations.push(`the ci.yml publish job downloads ${step.with?.name ?? "an artifact"}, which is not part of the publish input`);
    }
  }
  const stageRun = ciPublish.find((step) => /site-allure/.test(step.run ?? ""))?.run ?? "";
  if (!/cp -r publish-input\/allure site-allure/.test(stageRun)) {
    violations.push("the ci.yml publish job must stage site-allure only from publish-input/allure (the allure-report artifact)");
  }
  const nightlyPublish = stepsOf(nightly["publish-allure"]);
  for (const [file, steps] of [["ci.yml", ciPublish], ["nightly-quality.yml", nightlyPublish]] as const) {
    const deploys = steps.filter((step) => step.uses?.startsWith("peaceiris/actions-gh-pages@"));
    if (deploys.length === 0) {
      violations.push(`${file} publish job has no peaceiris/actions-gh-pages step`);
    }
    for (const step of deploys) {
      const folder = step.with?.publish_dir ?? "";
      if (!publishFolders.has(folder)) {
        violations.push(`${file} publishes ${folder || "an unset folder"}, which is not one of the staged output folders`);
      }
      if (step.with?.destination_dir === "allure" && folder !== "site-allure") {
        violations.push(`${file} publishes ${folder} into allure/ instead of site-allure`);
      }
    }
  }
  for (const step of nightlyPublish) {
    if (step.uses?.startsWith("actions/download-artifact@") && !["allure-report", "allure-history"].includes(step.with?.name ?? "")) {
      violations.push(`the nightly publish job downloads ${step.with?.name ?? "an artifact"} instead of the combined allure-report`);
    }
    if (step.uses?.startsWith("actions/download-artifact@") && step.with?.path === "site-allure" && step.with?.name !== "allure-report") {
      violations.push("the nightly publish job fills site-allure from something other than the allure-report artifact");
    }
  }
  return violations;
}
