import { describe, it, expect } from "vitest";
import fs from "node:fs";
import path from "node:path";

describe("Architecture Verification: Dependency Rules (FR-026)", () => {
  const webRoot = path.resolve(__dirname, "../..");
  const packageJsonPath = path.join(webRoot, "package.json");

  it("package.json does not declare or depend on any Duende IdentityServer packages", () => {
    expect(fs.existsSync(packageJsonPath)).toBe(true);
    const pkg = JSON.parse(fs.readFileSync(packageJsonPath, "utf-8"));

    const allDeps = {
      ...(pkg.dependencies || {}),
      ...(pkg.devDependencies || {}),
      ...(pkg.peerDependencies || {}),
      ...(pkg.optionalDependencies || {}),
    };

    const forbiddenTokens = ["duende", "identityserver", "identity-server"];

    for (const [depName] of Object.entries(allDeps)) {
      for (const token of forbiddenTokens) {
        expect(
          depName.toLowerCase(),
          `web/package.json must not depend on ${depName} (violates FR-026)`
        ).not.toContain(token);
      }
    }
  });

  it("web relies strictly on standard OpenID Connect (openid-client) for OIDC authentication", () => {
    const pkg = JSON.parse(fs.readFileSync(packageJsonPath, "utf-8"));
    const deps = pkg.dependencies || {};
    expect(deps["openid-client"]).toBeDefined();
  });

  it("zero source files in web/app and web/server.ts import or reference Duende IdentityServer", () => {
    const scanDirs = [path.join(webRoot, "app")];
    const sourceFiles: string[] = [];

    const serverTs = path.join(webRoot, "server.ts");
    if (fs.existsSync(serverTs)) {
      sourceFiles.push(serverTs);
    }

    function collectFiles(dir: string) {
      const entries = fs.readdirSync(dir, { withFileTypes: true });
      for (const entry of entries) {
        const fullPath = path.join(dir, entry.name);
        if (entry.isDirectory()) {
          // Do not recurse into node_modules or build
          if (entry.name !== "node_modules" && entry.name !== "build") {
            collectFiles(fullPath);
          }
        } else if (/\.(ts|tsx|js|jsx)$/.test(entry.name)) {
          sourceFiles.push(fullPath);
        }
      }
    }

    for (const dir of scanDirs) {
      if (fs.existsSync(dir)) {
        collectFiles(dir);
      }
    }

    expect(sourceFiles.length).toBeGreaterThan(0);

    const forbiddenPatterns = [
      /from\s+['"][^'"]*duende[^'"]*['"]/i,
      /import\s+['"][^'"]*duende[^'"]*['"]/i,
      /require\s*\(\s*['"][^'"]*duende[^'"]*['"]\s*\)/i,
      /from\s+['"][^'"]*identityserver[^'"]*['"]/i,
      /import\s+['"][^'"]*identityserver[^'"]*['"]/i,
      /Duende\.IdentityServer/i,
    ];

    for (const file of sourceFiles) {
      const content = fs.readFileSync(file, "utf-8");
      for (const pattern of forbiddenPatterns) {
        const match = content.match(pattern);
        expect(
          match,
          `File ${path.relative(webRoot, file)} references forbidden Duende symbol: ${match?.[0]}`
        ).toBeNull();
      }
    }
  });
});
