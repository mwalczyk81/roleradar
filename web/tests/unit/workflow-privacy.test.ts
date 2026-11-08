import { describe, it, expect } from "vitest";
import fs from "node:fs";
import path from "node:path";
import { checkAllurePrivacy } from "../support/workflow-privacy";

// Feature 011, story 2b (FR-027, FR-028): a test fails if the publish path would include a real-stack attachment.

function workflow(name: string): string {
  return fs.readFileSync(path.resolve(process.cwd(), "..", ".github", "workflows", name), "utf8");
}

const ci = workflow("ci.yml");
const nightly = workflow("nightly-quality.yml");

describe("Allure privacy rules in the workflows", () => {
  it("hold for the real ci.yml and nightly-quality.yml", () => {
    expect(checkAllurePrivacy(ci, nightly)).toEqual([]);
  });

  it("catch a real-stack job that uploads its raw results", () => {
    const broken = ci.replace("path: allure-sanitized/real-stack/", "path: allure-input/real-stack/");
    expect(broken).not.toBe(ci);
    const violations = checkAllurePrivacy(broken, nightly).join("\n");
    expect(violations).toMatch(/uploads the raw real-stack results/);
  });

  it("catch a real-stack job whose sanitize step is removed", () => {
    const broken = ci.replace("--lane real-stack", "--lane fictional");
    expect(broken).not.toBe(ci);
    expect(checkAllurePrivacy(broken, nightly).join("\n")).toMatch(/does not sanitize its Allure results with --lane real-stack/);
  });

  it("catch a final job that stops mapping the real-stack input to its lane", () => {
    const broken = ci.replace('if [ "$name" = "real-stack" ]; then lane=real-stack; fi', "true");
    expect(broken).not.toBe(ci);
    expect(checkAllurePrivacy(broken, nightly).join("\n")).toMatch(/does not sanitize each input with the lane of its source/);
  });

  it("catch a final job that merges unsanitized input", () => {
    const broken = ci.replace('cp -r "allure-sanitized/$name"/. allure-merged/', 'cp -r "allure-input/allure-in-$name"/. allure-merged/');
    expect(broken).not.toBe(ci);
    expect(checkAllurePrivacy(broken, nightly).join("\n")).toMatch(/copies unsanitized input into the merged folder/);
  });

  it("catch a report that is not leak-checked after generating", () => {
    const broken = nightly.replace("--check allure-report", "--version");
    expect(broken).not.toBe(nightly);
    expect(checkAllurePrivacy(ci, broken).join("\n")).toMatch(/must leak-check allure-merged and allure-report/);
  });

  // The publish input is what peaceiris/actions-gh-pages is given: site-keep, site-coverage and site-allure. These
  // tests put a scratch real-stack attachment on that path in different ways and expect the check to name each one.

  it("catch a publish job that stages the raw real-stack input into the published Allure folder", () => {
    const broken = ci.replace("cp -r publish-input/allure site-allure", "cp -r allure-input/allure-in-real-stack site-allure");
    expect(broken).not.toBe(ci);
    const violations = checkAllurePrivacy(broken, nightly).join("\n");
    expect(violations).toMatch(/reads Allure inputs directly/);
    expect(violations).toMatch(/must stage site-allure only from publish-input\/allure/);
  });

  it("catch a publish job that downloads a raw stack artifact", () => {
    const broken = ci.replace(/(download-artifact@[^\n]*\n\s+with:\n\s+name: )allure-history/, "$1allure-in-real-stack");
    expect(broken).not.toBe(ci);
    expect(checkAllurePrivacy(broken, nightly).join("\n")).toMatch(/downloads allure-in-real-stack, which is not part of the publish input/);
  });

  it("catch a deploy of a folder that is not one of the staged output folders", () => {
    const broken = ci.replace("publish_dir: site-allure", "publish_dir: allure-input/real-stack");
    expect(broken).not.toBe(ci);
    expect(checkAllurePrivacy(broken, nightly).join("\n")).toMatch(/publishes allure-input\/real-stack, which is not one of the staged output folders/);
  });

  it("catch a nightly publish job that fills the published Allure folder from a raw stack artifact", () => {
    const broken = nightly.replace(/(download-artifact@[^\n]*\n\s+with:\n\s+name: )allure-report(\n\s+path: site-allure)/, "$1allure-in-real-stack$2");
    expect(broken).not.toBe(nightly);
    expect(checkAllurePrivacy(ci, broken).join("\n")).toMatch(/downloads allure-in-real-stack instead of the combined allure-report/);
  });
});
