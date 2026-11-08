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

  // The publish input is what peaceiris/actions-gh-pages is given: the site/ folder the staging step fills. These tests put
  // a scratch real-stack path on that input in different ways, and remove the force_orphan that keeps gh-pages one commit.

  it("catch a publish job that stages the raw real-stack input into the published Allure folder", () => {
    const broken = ci.replace("cp -r publish-input/allure site/allure", "cp -r allure-input/allure-in-real-stack site/allure");
    expect(broken).not.toBe(ci);
    const violations = checkAllurePrivacy(broken, nightly).join("\n");
    expect(violations).toMatch(/reads Allure inputs directly/);
    expect(violations).toMatch(/must stage site\/allure only from publish-input\/allure/);
  });

  it("catch a publish job that downloads a raw stack artifact", () => {
    const broken = ci.replace(/(download-artifact@[^\n]*\n\s+with:\n\s+name: )allure-history/, "$1allure-in-real-stack");
    expect(broken).not.toBe(ci);
    expect(checkAllurePrivacy(broken, nightly).join("\n")).toMatch(/downloads allure-in-real-stack, which is not part of the publish input/);
  });

  it("catch a deploy of a folder that is not the staged site folder", () => {
    const broken = ci.replace("publish_dir: site", "publish_dir: allure-input/real-stack");
    expect(broken).not.toBe(ci);
    expect(checkAllurePrivacy(broken, nightly).join("\n")).toMatch(/publishes allure-input\/real-stack, which is not the staged site folder/);
  });

  it("catch a nightly publish job that stages the Allure folder from a raw stack artifact", () => {
    const broken = nightly.replace(/(download-artifact@[^\n]*\n\s+with:\n\s+name: )allure-report(\n\s+path: publish-input\/allure)/, "$1allure-in-real-stack$2");
    expect(broken).not.toBe(nightly);
    expect(checkAllurePrivacy(ci, broken).join("\n")).toMatch(/nightly-quality.yml publish job downloads allure-in-real-stack, which is not part of the publish input/);
  });

  it("catch a publish job that loses force_orphan or adds a second deploy", () => {
    const withoutOrphan = ci.replace("force_orphan: true", "force_orphan: false");
    expect(withoutOrphan).not.toBe(ci);
    expect(checkAllurePrivacy(withoutOrphan, nightly).join("\n")).toMatch(/ci.yml must publish with force_orphan: true/);

    const marker = "      - name: Say what the owner must enable";
    const secondDeploy = nightly.replace(
      marker,
      `      - name: A second deploy\n        uses: peaceiris/actions-gh-pages@84c30a85c19949d7eee79c4ff27748b70285e453 # v4.1.0\n        with:\n          publish_dir: site\n          force_orphan: true\n\n${marker}`,
    );
    expect(secondDeploy).not.toBe(nightly);
    expect(checkAllurePrivacy(ci, secondDeploy).join("\n")).toMatch(/nightly-quality.yml publish job must have exactly one peaceiris\/actions-gh-pages step, found 2/);
  });
});
