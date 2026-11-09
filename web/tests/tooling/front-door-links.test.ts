import { execFileSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { describe, it, expect } from "vitest";

// Feature 012, FR-017 to FR-023 and SC-006 to SC-008: the front door (root README, CONTRIBUTING, docs/README) is
// checked offline. markdownlint checks fragments but not file targets, images or URLs, so this test resolves every
// relative link, #anchor (GitHub slugs) and image reference (Markdown images, <img src>, <source srcset>), checks badge
// URLs with the URL parser (https, allowlisted hosts, no network), and holds the shared commands and statements.
// Links are extracted with regular expressions (R-004): the notes renderer's parser is not a direct dependency.
// The Linux web gate does not copy src/ or tests/ into its container, so the three files must not link into them.

const repoRoot = path.resolve(__dirname, "../../..");
const FRONT_DOOR = ["README.md", "CONTRIBUTING.md", "docs/README.md"] as const;
const BADGE_HOSTS = new Set(["github.com", "img.shields.io", "mwalczyk81.github.io", "badge-api.stryker-mutator.io"]);

/** The shared commands: each must appear in all three files as a fenced block holding exactly that line. */
const SHARED_COMMANDS = {
  demo: "docker compose -p roleradar-demo -f deploy/demo/docker-compose.yml up --build --wait",
  fastLoop: "pwsh ./scripts/test-fast.ps1",
  reviewGate: "pwsh ./scripts/review-gate.ps1",
} as const;

const CERTIFICATE_STATEMENT = "once for each origin";
const FICTIONAL_STATEMENT = "All demo data is fictional.";

interface Reference {
  kind: "link" | "image";
  target: string;
}

interface Problem {
  file: string;
  target: string;
  reason: string;
}

function read(relative: string): string {
  return fs.readFileSync(path.join(repoRoot, relative), "utf8");
}

/** Text with fenced code blocks and inline code spans blanked out, so code that looks like a link is ignored. */
// Fences may be indented (a code block inside a list item).
const FENCE = /^[ \t]*(```|~~~)[^\n]*\n([\s\S]*?)^[ \t]*\1[^\n]*$/gm;

function withoutCode(markdown: string): string {
  return markdown.replace(FENCE, "").replace(/`[^`\n]*`/g, "");
}

/** Contents of each fenced block, with the list indentation and surrounding blank lines removed. */
function fencedBlocks(markdown: string): string[] {
  return [...markdown.matchAll(FENCE)].map((m) =>
    (m[2] ?? "")
      .split("\n")
      .map((line) => line.trim())
      .join("\n")
      .trim(),
  );
}

function extractReferences(markdown: string): Reference[] {
  const text = withoutCode(markdown);
  const refs: Reference[] = [];
  for (const m of text.matchAll(/(!?)\[(?:[^\]\n]|\][^(\n])*?\]\(\s*<?([^)\s>]+)>?(?:\s+"[^"]*")?\s*\)/g)) {
    refs.push({ kind: m[1] === "!" ? "image" : "link", target: m[2] ?? "" });
  }
  for (const m of text.matchAll(/<img\b[^>]*\bsrc="([^"]+)"/gi)) {
    refs.push({ kind: "image", target: m[1] ?? "" });
  }
  for (const m of text.matchAll(/<source\b[^>]*\bsrcset="([^"]+)"/gi)) {
    for (const candidate of (m[1] ?? "").split(",")) {
      const url = candidate.trim().split(/\s+/)[0];
      if (url) refs.push({ kind: "image", target: url });
    }
  }
  for (const m of text.matchAll(/<a\b[^>]*\bhref="([^"]+)"/gi)) {
    refs.push({ kind: "link", target: m[1] ?? "" });
  }
  return refs;
}

/** GitHub's heading anchors: lower case, punctuation dropped, spaces to hyphens, repeats numbered. */
function headingSlugs(markdown: string): Set<string> {
  const slugs = new Set<string>();
  const counts = new Map<string, number>();
  const text = markdown.replace(FENCE, "");
  for (const m of text.matchAll(/^#{1,6}\s+(.+?)\s*#*\s*$/gm)) {
    const plain = (m[1] ?? "")
      .replace(/!?\[([^\]]*)\]\([^)]*\)/g, "$1")
      .replace(/<[^>]+>/g, "")
      .replace(/[`*_]/g, "");
    const base = plain
      .trim()
      .toLowerCase()
      .replace(/[^\p{L}\p{N}\s-]/gu, "")
      .replace(/\s/g, "-");
    const seen = counts.get(base) ?? 0;
    slugs.add(seen === 0 ? base : `${base}-${seen}`);
    counts.set(base, seen + 1);
  }
  return slugs;
}

function checkFile(absolutePath: string, markdown: string): Problem[] {
  const problems: Problem[] = [];
  const file = path.relative(repoRoot, absolutePath).replaceAll("\\", "/");
  const dir = path.dirname(absolutePath);
  for (const ref of extractReferences(markdown)) {
    const { target } = ref;
    if (/^[a-z][a-z0-9+.-]*:/i.test(target)) {
      let url: URL;
      try {
        url = new URL(target);
      } catch {
        problems.push({ file, target, reason: "malformed URL" });
        continue;
      }
      if (url.protocol === "mailto:") continue;
      if (url.protocol !== "https:") {
        problems.push({ file, target, reason: "not https" });
        continue;
      }
      if (ref.kind === "image" && !BADGE_HOSTS.has(url.hostname)) {
        problems.push({ file, target, reason: "image host not allowlisted" });
      }
      continue;
    }

    const [rawPath = "", anchor] = target.split("#");
    const filePart = decodeURIComponent(rawPath);
    const resolved = filePart === "" ? absolutePath : path.resolve(dir, filePart);
    const relativeToRoot = path.relative(repoRoot, resolved).replaceAll("\\", "/");
    if (relativeToRoot.startsWith("src/") || relativeToRoot.startsWith("tests/")) {
      problems.push({ file, target, reason: "links into src/ or tests/, which the Linux web gate does not copy" });
      continue;
    }
    if (!fs.existsSync(resolved)) {
      problems.push({ file, target, reason: `missing ${ref.kind === "image" ? "image" : "file"}` });
      continue;
    }
    if (anchor !== undefined && anchor !== "") {
      if (resolved !== absolutePath && !resolved.endsWith(".md")) {
        problems.push({ file, target, reason: "anchor on a file that is not Markdown" });
        continue;
      }
      const targetText = resolved === absolutePath ? markdown : fs.readFileSync(resolved, "utf8");
      if (!headingSlugs(targetText).has(anchor.toLowerCase())) {
        problems.push({ file, target, reason: "missing anchor" });
      }
    }
  }
  return problems;
}

function h2Headings(markdown: string): string[] {
  return [...withoutCode(markdown).matchAll(/^##\s+(.+?)\s*$/gm)].map((m) => m[1] ?? "");
}

describe("link checker (deliberately broken fixture)", () => {
  const fixturePath = path.resolve(__dirname, "../fixtures/front-door/broken.md.txt");
  const problems = checkFile(fixturePath, fs.readFileSync(fixturePath, "utf8"));
  const reasonFor = (target: string) => problems.filter((p) => p.target === target).map((p) => p.reason);

  it("reports a missing file, a missing anchor here and in another file", () => {
    expect(reasonFor("./does-not-exist.md")).toEqual(["missing file"]);
    expect(reasonFor("#no-such-heading")).toEqual(["missing anchor"]);
    expect(reasonFor("../../../../README.md#no-such-section-anywhere")).toEqual(["missing anchor"]);
  });

  it("reports missing images in Markdown, <img src> and <source srcset>", () => {
    expect(reasonFor("./missing-image.png")).toEqual(["missing image"]);
    expect(reasonFor("./missing-img-tag.png")).toEqual(["missing image"]);
    expect(reasonFor("./missing-source.png")).toEqual(["missing image"]);
    expect(reasonFor("./missing-picture.png")).toEqual(["missing image"]);
  });

  it("reports a badge that is not https and one on a host that is not allowlisted", () => {
    expect(reasonFor("http://img.shields.io/badge/x-y-blue.svg")).toEqual(["not https"]);
    expect(reasonFor("https://badges.invalid.example/x.svg")).toEqual(["image host not allowlisted"]);
  });

  it("accepts a good anchor and ignores links inside code blocks", () => {
    expect(reasonFor("#real-heading")).toEqual([]);
    expect(reasonFor("./also-missing.md")).toEqual([]);
    expect(problems).toHaveLength(9);
  });
});

describe("front door files", () => {
  it.each(FRONT_DOOR)("%s exists and every link, anchor and image reference resolves", (relative) => {
    expect(fs.existsSync(path.join(repoRoot, relative)), `${relative} is missing`).toBe(true);
    const problems = checkFile(path.join(repoRoot, relative), read(relative));
    expect(problems.map((p) => `${p.target}: ${p.reason}`)).toEqual([]);
  });

  it.each(Object.entries(SHARED_COMMANDS))("all three files give the same %s command as a fenced block", (_name, command) => {
    for (const relative of FRONT_DOOR) {
      expect(fencedBlocks(read(relative)), `${relative} lacks the block "${command}"`).toContain(command);
    }
  });

  it("all three files link the demo page", () => {
    for (const relative of FRONT_DOOR) {
      const targets = extractReferences(read(relative)).map((r) => path.resolve(path.dirname(path.join(repoRoot, relative)), r.target.split("#")[0] ?? ""));
      expect(targets, `${relative} does not link docs/demo.md`).toContain(path.join(repoRoot, "docs", "demo.md"));
    }
  });

  it("the README and docs/demo.md both carry the certificate-warning and fictional-data statements", () => {
    for (const relative of ["README.md", "docs/demo.md"]) {
      const text = read(relative);
      expect(text, relative).toContain(CERTIFICATE_STATEMENT);
      expect(text, relative).toContain("identity.localhost");
      expect(text, relative).toContain(FICTIONAL_STATEMENT);
    }
  });
});

describe("root README shape (FR-017, FR-018, SC-008)", () => {
  const readme = fs.existsSync(path.join(repoRoot, "README.md")) ? read("README.md") : "";
  const firstH2 = readme.search(/^## /m);
  const top = firstH2 === -1 ? readme : readme.slice(0, firstH2);

  it("is at most 200 lines", () => {
    expect(readme.split("\n").length).toBeLessThanOrEqual(200);
  });

  it("opens with the badges and a light and dark hero picture before any section", () => {
    for (const badge of [
      "https://github.com/mwalczyk81/roleradar/actions/workflows/ci.yml/badge.svg",
      "https://mwalczyk81.github.io/roleradar/badges/coverage.svg",
      "https://img.shields.io/endpoint?url=https%3A%2F%2Fbadge-api.stryker-mutator.io%2Fgithub.com%2Fmwalczyk81%2Froleradar%2Fmain%3Fmodule%3Dweb-lib",
    ]) {
      expect(top).toContain(badge);
    }
    expect(top).toMatch(/img\.shields\.io\/badge\/license/i);
    expect(top).toMatch(/img\.shields\.io\/badge\/\.NET-10/i);
    expect(top).toContain("<picture>");
    expect(top).toContain('srcset="docs/images/insights-dark.png"');
    expect(top).toContain('src="docs/images/insights-light.png"');
  });

  it("has the sections in the required order", () => {
    const order = ["Try the demo", "Architecture", "How it was built", "Evidence", "Status and limits"];
    const headings = h2Headings(readme);
    const positions = order.map((h) => headings.indexOf(h));
    expect(positions, `README sections: ${headings.join(" | ")}`).not.toContain(-1);
    expect([...positions].sort((a, b) => a - b)).toEqual(positions);
    expect(headings[0]).toBe("Try the demo");
  });

  it("says the first run builds four images and keeps the four demo steps", () => {
    expect(readme).toContain("first run builds four images");
    expect(readme).toContain("git clone https://github.com/mwalczyk81/roleradar.git");
    expect(readme).toContain("--profile tools run --rm demo-cli demo credentials");
  });

  it("draws the architecture with every host and layer", () => {
    const mermaid = /```mermaid\n([\s\S]*?)```/.exec(readme)?.[1] ?? "";
    for (const node of ["MCP", "CLI", "API", "Identity", "Web UI", "OData read seam", "Application", "Infrastructure", "MongoDB"]) {
      expect(mermaid, node).toContain(node);
    }
  });

  it("discloses how it was built and links the evidence, the ADR index and the worked example", () => {
    expect(readme).toMatch(/AI agents/);
    expect(readme).toContain("https://mwalczyk81.github.io/roleradar/");
    expect(readme).toContain("docs/adr/README.md");
    expect(readme).toContain("specs/008-posting-application-rejection-sync/");
    expect(readme).toContain("docs/README.md");
  });
});

describe("docs/README.md indexes everything under docs/ (FR-020)", () => {
  it("links every file under docs/ except images and ADR files (the ADR index covers those)", () => {
    const index = fs.existsSync(path.join(repoRoot, "docs", "README.md")) ? read("docs/README.md") : "";
    const linked = new Set(
      extractReferences(index).map((r) => path.relative(path.join(repoRoot, "docs"), path.resolve(path.join(repoRoot, "docs"), r.target.split("#")[0] ?? "")).replaceAll("\\", "/")),
    );
    const walk = (dir: string): string[] =>
      fs.readdirSync(dir, { withFileTypes: true }).flatMap((e) => (e.isDirectory() ? walk(path.join(dir, e.name)) : [path.join(dir, e.name)]));
    const expected = walk(path.join(repoRoot, "docs"))
      .map((f) => path.relative(path.join(repoRoot, "docs"), f).replaceAll("\\", "/"))
      .filter((f) => f !== "README.md" && !f.startsWith("images/") && !(f.startsWith("adr/") && f !== "adr/README.md"));
    expect(expected.filter((f) => !linked.has(f))).toEqual([]);
    expect(linked.has("images/manifest.json") || index.includes("images/")).toBe(true);
  });
});

describe("issue chooser and code of conduct (FR-021, FR-022)", () => {
  it("disables blank issues, links SECURITY.md and keeps both forms", () => {
    const configPath = path.join(repoRoot, ".github", "ISSUE_TEMPLATE", "config.yml");
    const config = fs.existsSync(configPath) ? fs.readFileSync(configPath, "utf8") : "";
    expect(config).toMatch(/^blank_issues_enabled:\s*false\s*$/m);
    expect(config).toContain("https://github.com/mwalczyk81/roleradar/blob/main/SECURITY.md");
    for (const form of ["bug_report.yml", "feature_request.yml"]) {
      expect(fs.existsSync(path.join(repoRoot, ".github", "ISSUE_TEMPLATE", form)), form).toBe(true);
    }
  });

  it("CODE_OF_CONDUCT.md is the Contributor Covenant 2.1 with the existing security contact, linked from CONTRIBUTING", () => {
    const coc = fs.existsSync(path.join(repoRoot, "CODE_OF_CONDUCT.md")) ? read("CODE_OF_CONDUCT.md") : "";
    expect(coc).toContain("Contributor Covenant");
    expect(coc).toContain("version 2.1");
    expect(coc).not.toContain("[INSERT CONTACT METHOD]");
    expect(coc).toContain("Private Vulnerability Reporting");
    expect(read("CONTRIBUTING.md")).toContain("(CODE_OF_CONDUCT.md)");
  });
});

describe("CHANGELOG.md and the v0.1.0 release text (FR-024, SC-010)", () => {
  const changelog = fs.existsSync(path.join(repoRoot, "CHANGELOG.md")) ? read("CHANGELOG.md") : "";
  const releaseTextPath = path.join(repoRoot, "specs", "012-front-door", "release-v0.1.0.md");
  const releaseText = fs.existsSync(releaseTextPath) ? fs.readFileSync(releaseTextPath, "utf8") : "";
  const release = changelog.split(/^## \[0\.1\.0\]/m)[1] ?? "";

  it("follows Keep a Changelog with an Unreleased section, 0.1.0 and the comparison links", () => {
    expect(changelog).toContain("https://keepachangelog.com/en/1.1.0/");
    expect(changelog).toMatch(/^## \[Unreleased\]$/m);
    expect(changelog).toMatch(/^## \[0\.1\.0\]/m);
    expect(changelog).toMatch(/^\[Unreleased\]: https:\/\/github\.com\/mwalczyk81\/roleradar\/compare\/v0\.1\.0\.\.\.HEAD$/m);
    expect(changelog).toMatch(/^\[0\.1\.0\]: https:\/\/github\.com\/mwalczyk81\/roleradar\/releases\/tag\/v0\.1\.0$/m);
  });

  it("summarizes features 001 to 012 in 0.1.0 by capability, under category headings rather than one per feature", () => {
    for (const id of ["001", "002", "003", "004", "005a", "005b", "006", "007", "008", "009", "010", "011", "012"]) {
      expect(release, `feature ${id} is not mentioned in 0.1.0`).toMatch(new RegExp(`\\b${id}\\b`));
    }
    expect(release).toMatch(/^### Added$/m);
    expect(release).not.toMatch(/^###.*\b0\d\d[ab]?\b/m);
  });

  it("is linked from the README and the docs index, and the release text exists", () => {
    expect(read("README.md")).toContain("(CHANGELOG.md)");
    expect(read("docs/README.md")).toContain("(../CHANGELOG.md)");
    expect(releaseText).toMatch(/v0\.1\.0/);
  });

  // The Linux web gate copies no .git and CI checks out one commit, so this needs a full local clone.
  const fullHistory = (() => {
    try {
      return execFileSync("git", ["rev-parse", "--is-shallow-repository"], { cwd: repoRoot, encoding: "utf8" }).trim() === "false";
    } catch {
      return false;
    }
  })();

  it.skipIf(!fullHistory)("every #NNN in the changelog and the release text is a pull request in the git history (skipped without full history)", () => {
    const subjects = execFileSync("git", ["log", "--format=%s"], { cwd: repoRoot, encoding: "utf8" });
    const cited = [...`${changelog}\n${releaseText}`.matchAll(/(?<![\w/])#(\d+)\b/g)].map((m) => m[1] ?? "");
    expect(cited.length).toBeGreaterThan(0);
    expect(cited.filter((n) => !subjects.includes(`(#${n})`))).toEqual([]);
  });
});
