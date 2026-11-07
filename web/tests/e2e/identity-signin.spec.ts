import { execFileSync } from "node:child_process";
import crypto from "node:crypto";
import path from "node:path";
import type { Locator, Page } from "@playwright/test";
import { test, expect } from "./support/csp-guard.js";
import { runAxeChecks } from "./support/axe.js";
import { contrastRatio, paintedBackground, parseCssColour, settleAnimations, textColour, textContrast, tokenColour } from "./support/contrast.js";
import { assertNotRealStackProject } from "./support/isolation.js";

/**
 * 009 US4 (FR-040 to FR-042): the Identity sign-in and signed-out pages in real Chromium, served by the locally built
 * Identity image, in the system light and dark colour schemes. Same isolation as IdentityImageLoginPageTests: a uniquely
 * named container on a random loopback port with a throwaway volume and fictional secrets; the developer's stack, volumes
 * and ports are never touched. Skipped with a stated reason when Docker is unavailable (for example the Linux web gate).
 * The pages need no owner to render, so none is seeded; the sign-in itself is covered by the real-stack journey.
 */

const REPO_ROOT = path.resolve(process.cwd(), "..");
const RUN_ID = crypto.randomBytes(6).toString("hex");
const IMAGE_TAG = "roleradar-identity:e2e-identity-signin";
const CONTAINER = `rrtest-e2e-signin-${RUN_ID}`;
const VOLUME = `rrtest-e2e-signin-vol-${RUN_ID}`;
const SCHEMES = ["light", "dark"] as const;

function docker(args: string[], timeoutMs = 60_000): string {
  return execFileSync("docker", args, { cwd: REPO_ROOT, encoding: "utf8", timeout: timeoutMs, stdio: ["ignore", "pipe", "pipe"] });
}

function dockerAvailable(): boolean {
  try {
    docker(["info", "--format", "{{.ServerVersion}}"], 15_000);
    return true;
  } catch {
    return false;
  }
}

const DOCKER = dockerAvailable();
let baseUrl = "";

test.describe("Identity sign-in pages (Docker image, real Chromium)", () => {
  test.skip(!DOCKER, "Docker is unavailable, so the Identity image cannot be built or run here.");

  test.beforeAll(async ({ browserName: _browserName }, testInfo) => {
    assertNotRealStackProject(testInfo.project.name);
    test.setTimeout(900_000);
    docker(["build", "-f", "deploy/Dockerfile.identity", "-t", IMAGE_TAG, "."], 840_000);
    docker([
      "run", "-d", "--name", CONTAINER, "--read-only", "--tmpfs", "/tmp:rw,noexec,nosuid,size=64m",
      "-v", `${VOLUME}:/data/identity:rw`,
      "-e", "ASPNETCORE_URLS=http://0.0.0.0:8080",
      "-e", "ROLERADAR_IDENTITY_DB=Data Source=/data/identity/identity.db",
      "-e", "ROLERADAR_WEB_CLIENT_SECRET=SigninE2eWebSecret!2026",
      "-e", "ROLERADAR_HEADLESS_CLIENT_SECRET=SigninE2eHeadlessSecret!2026",
      "-p", "127.0.0.1::8080", "--cap-drop", "ALL", "--security-opt", "no-new-privileges",
      IMAGE_TAG,
    ]);
    const mapping = docker(["port", CONTAINER, "8080/tcp"]).split("\n")[0]?.trim() ?? "";
    const port = mapping.slice(mapping.lastIndexOf(":") + 1);
    baseUrl = `http://127.0.0.1:${port}`;
    const deadline = Date.now() + 60_000;
    for (;;) {
      try {
        if ((await fetch(`${baseUrl}/health`)).ok) break;
      } catch {
        // not accepting connections yet
      }
      if (Date.now() > deadline) throw new Error(`Identity container did not become healthy.\n${docker(["logs", CONTAINER])}`);
      await new Promise((resolve) => setTimeout(resolve, 500));
    }
  });

  test.afterAll(() => {
    for (const args of [["rm", "-f", CONTAINER], ["volume", "rm", "-f", VOLUME]]) {
      try {
        docker(args, 30_000);
      } catch {
        // cleanup must never mask the test result
      }
    }
  });

  const PAGES = [
    { name: "Login", route: "/Account/Login", heading: "h1", primary: 'button[type="submit"]', label: "label" },
    { name: "Logout", route: "/Account/Logout", heading: "h1", primary: 'a[href="/Account/Login"]', label: "p" },
  ] as const;

  for (const scheme of SCHEMES) {
    for (const spec of PAGES) {
      test(`${spec.name} in the system ${scheme} scheme: themed, Plex type, kit primary, no CSP violation, accessible`, async ({ page, cspViolations }) => {
        await page.emulateMedia({ colorScheme: scheme, reducedMotion: "reduce" });
        const response = await page.goto(`${baseUrl}${spec.route}`, { waitUntil: "networkidle" });
        expect(response?.status()).toBe(200);
        expect(response?.headers()["content-security-policy"]).toContain("form-action 'self'");

        // The system scheme drives the theme with no cookie and no inline script.
        await expect(page.locator("html")).toHaveAttribute("data-theme", scheme);
        await page.evaluate(() => document.fonts.ready);
        await settleAnimations(page);

        const headingFamily = await page.locator(spec.heading).first().evaluate((el) => getComputedStyle(el).fontFamily);
        expect(headingFamily.replace(/["']/g, "")).toMatch(/^IBM Plex Serif/);
        const bodyFamily = await page.locator(spec.label).first().evaluate((el) => getComputedStyle(el).fontFamily);
        expect(bodyFamily.replace(/["']/g, "")).toMatch(/^IBM Plex Sans/);
        expect(await page.evaluate(() => document.fonts.check('16px "IBM Plex Sans"')), "the Sans font file was served and loaded").toBe(true);
        expect(await page.evaluate(() => document.fonts.check('600 24px "IBM Plex Serif"')), "the Serif font file was served and loaded").toBe(true);

        const primary = page.locator(spec.primary).first();
        const background = await primary.evaluate((el) => getComputedStyle(el).backgroundColor);
        expect(background, "the primary action uses the kit primary token for this theme").toBe(await tokenColour(page, "--ds-primary-solid"));
        expect(await textContrast(primary), "primary action text contrast").toBeGreaterThanOrEqual(4.5);
        expect(await textContrast(page.locator(spec.label).first()), "label or body text contrast").toBeGreaterThanOrEqual(4.5);
        expect(await textContrast(page.locator(spec.heading).first()), "heading contrast").toBeGreaterThanOrEqual(4.5);

        expect(await runAxeChecks(page), `axe violations on ${spec.name} (${scheme})`).toEqual([]);
        expect(cspViolations, "no CSP violations").toEqual([]);
      });
    }
  }

  test("the theme follows a live change of the system scheme", async ({ page }) => {
    await page.emulateMedia({ colorScheme: "light" });
    await page.goto(`${baseUrl}/Account/Login`, { waitUntil: "networkidle" });
    await expect(page.locator("html")).toHaveAttribute("data-theme", "light");
    await page.emulateMedia({ colorScheme: "dark" });
    await expect(page.locator("html")).toHaveAttribute("data-theme", "dark");
  });

  // ---- Phase 5 amendment: the pages look like the web app, not just use its tokens. Computed values only. ----

  /** What a kit token resolves to for one CSS property, in the current theme (read from a probe, so it is the browser's own value). */
  async function resolved(page: Page, property: "height" | "borderRadius" | "boxShadow" | "fontSize" | "fontWeight" | "borderTopWidth" | "paddingTop" | "paddingLeft" | "backgroundColor" | "color", token: string): Promise<string> {
    return await page.evaluate(
      ([prop, name]) => {
        const probe = document.createElement("div");
        probe.style.setProperty(prop.replace(/[A-Z]/g, (c) => `-${c.toLowerCase()}`), `var(${name})`);
        if (prop === "borderTopWidth") probe.style.setProperty("border-top-style", "solid");
        document.body.appendChild(probe);
        const value = getComputedStyle(probe)[prop as "height"];
        probe.remove();
        return value;
      },
      [property, token] as const
    );
  }

  const style = (locator: Locator, property: string) => locator.evaluate((el, p) => getComputedStyle(el).getPropertyValue(p), property);

  async function openLoginWithError(page: Page): Promise<void> {
    await page.goto(`${baseUrl}/Account/Login`, { waitUntil: "networkidle" });
    await page.fill('input[name="Input.Username"]', "demo-owner");
    await page.fill('input[name="Input.Password"]', "not-a-real-password");
    await page.click('button[type="submit"]');
    await page.waitForLoadState("networkidle");
  }

  // 010 US4: the page binds the one-time-code field on every post, so a wrong password used to list its field messages
  // ("The Code field is required", the 6-to-7 length message) under the real error. Only the password-step error may show.
  test("a wrong password lists only the password-step error, never the one-time-code field messages", async ({ page }) => {
    await openLoginWithError(page);

    const alert = page.getByRole("alert");
    await expect(alert).toBeVisible();
    expect(await alert.locator("p").allTextContents()).toEqual(["Invalid username or password."]);
    await expect(page.getByText("The Code field is required")).toHaveCount(0);
    await expect(page.getByText(/minimum length of 6/)).toHaveCount(0);
  });

  for (const scheme of SCHEMES) {
    for (const spec of PAGES) {
      test(`${spec.name} (${scheme}): the brand lockup is the app shell brand, and the page has one h1`, async ({ page }) => {
        await page.emulateMedia({ colorScheme: scheme, reducedMotion: "reduce" });
        await page.goto(`${baseUrl}${spec.route}`, { waitUntil: "networkidle" });
        await settleAnimations(page);

        const card = page.locator(".login-card, .logout-card");
        const mark = card.locator("svg").first();
        expect(await mark.getAttribute("aria-hidden")).toBe("true");
        expect(await mark.getAttribute("focusable")).toBe("false");
        expect(await mark.getAttribute("viewBox")).toBe("0 0 24 24");
        expect(await mark.locator("circle, line").count(), "the four-element radar mark").toBe(4);
        expect(await style(mark, "width")).toBe("22px");
        expect(await style(mark, "height")).toBe("22px");
        expect(await textColour(mark), "mark uses the accent colour like .brand-mark").toBe(await tokenColour(page, "--ds-primary"));

        const name = card.getByText("RoleRadar", { exact: true });
        await expect(name).toBeVisible();
        expect((await style(name, "font-family")).replace(/["']/g, "")).toMatch(/^IBM Plex Serif/);
        expect(await style(name, "font-weight"), "semibold like the shell brand").toBe("600");
        expect(await style(name, "font-size"), "text-lg like .ds-appshell-brand").toBe(await resolved(page, "fontSize", "--ds-text-lg"));
        expect(parseFloat(await style(name, "letter-spacing"))).toBeCloseTo(0.01 * parseFloat(await style(name, "font-size")), 2);
        const box = await mark.boundingBox();
        const nameBox = await name.boundingBox();
        const cardBox = await card.boundingBox();
        const centre = (box!.x + (nameBox!.x + nameBox!.width)) / 2;
        expect(Math.abs(centre - (cardBox!.x + cardBox!.width / 2)), "the lockup is centred in the card").toBeLessThan(2);

        await expect(page.locator("h1")).toHaveCount(1);
        await expect(page.locator("h1")).toHaveText(spec.name === "Login" ? "Sign in" : "You are signed out");
        expect(await page.title()).toBe(spec.name === "Login" ? "RoleRadar - Owner Login" : "RoleRadar - Signed Out");
      });

      test(`${spec.name} (${scheme}): subtle page ground, quiet kit-Card surface, legible card`, async ({ page }) => {
        await page.emulateMedia({ colorScheme: scheme, reducedMotion: "reduce" });
        await page.goto(`${baseUrl}${spec.route}`, { waitUntil: "networkidle" });
        await settleAnimations(page);

        const card = page.locator(".login-card, .logout-card");
        expect(await style(page.locator("body"), "background-color"), "ground is the sidebar tone").toBe(await tokenColour(page, "--ds-bg-subtle"));
        expect(await style(card, "background-color")).toBe(await tokenColour(page, "--ds-surface"));
        expect(await style(card, "border-top-width")).toBe(await resolved(page, "borderTopWidth", "--ds-border-thin"));
        expect(await style(card, "border-top-color")).toBe(await tokenColour(page, "--ds-border"));
        expect(await style(card, "border-top-left-radius")).toBe(await resolved(page, "borderRadius", "--ds-radius-lg"));
        expect(await style(card, "box-shadow"), "the kit Card shadow").toBe(await resolved(page, "boxShadow", "--ds-shadow-sm"));
        expect(await style(card, "padding-top")).toBe(await resolved(page, "paddingTop", "--ds-space-6"));

        const surface = await paintedBackground(card);
        const ground = parseCssColour(await tokenColour(page, "--ds-bg-subtle"));
        const edge = parseCssColour(await tokenColour(page, "--ds-border"));
        expect(await textContrast(card.locator("h1")), "heading on the card").toBeGreaterThanOrEqual(4.5);
        expect(await textContrast(card.getByText("RoleRadar", { exact: true })), "wordmark on the card").toBeGreaterThanOrEqual(4.5);
        expect(contrastRatio(surface, ground), "the card surface is distinguishable from the ground").toBeGreaterThan(1.0);
        console.log(`identity ${spec.name} ${scheme}: card edge vs ground ${contrastRatio(edge, ground).toFixed(2)}:1, card surface vs ground ${contrastRatio(surface, ground).toFixed(2)}:1`);
      });
    }

    test(`Login (${scheme}): inputs, button and focus ring match the web app's Input and Button`, async ({ page }) => {
      await page.emulateMedia({ colorScheme: scheme, reducedMotion: "reduce" });
      await page.goto(`${baseUrl}/Account/Login`, { waitUntil: "networkidle" });
      await settleAnimations(page);

      const input = page.locator('input[name="Input.Password"]');
      expect(await style(input, "height")).toBe(await resolved(page, "height", "--ds-control-md"));
      expect(await style(input, "border-top-left-radius")).toBe(await resolved(page, "borderRadius", "--ds-radius-md"));
      expect(await style(input, "border-top-width")).toBe(await resolved(page, "borderTopWidth", "--ds-border-thin"));
      expect(await style(input, "border-top-color")).toBe(await tokenColour(page, "--ds-border-control"));
      expect(await style(input, "background-color"), "kit Input is on --ds-surface").toBe(await tokenColour(page, "--ds-surface"));
      expect(await style(input, "font-size")).toBe(await resolved(page, "fontSize", "--ds-text-sm"));
      expect(await style(input, "padding-left")).toBe(await resolved(page, "paddingLeft", "--ds-space-3"));
      expect((await style(input, "font-family")).replace(/["']/g, "")).toMatch(/^IBM Plex Sans/);

      await input.focus();
      await settleAnimations(page);
      expect(await style(input, "border-top-color"), "focused border like the kit Input").toBe(await tokenColour(page, "--ds-focus-ring"));
      expect(await style(input, "outline-width")).toBe(await resolved(page, "borderTopWidth", "--ds-border-thick"));
      expect(await style(input, "outline-style")).toBe("solid");
      expect(await style(input, "outline-color")).toBe(await tokenColour(page, "--ds-focus-ring"));
      expect(await style(input, "outline-offset")).toBe("0px");

      const button = page.locator('button[type="submit"]');
      expect(await style(button, "height")).toBe(await resolved(page, "height", "--ds-control-md"));
      expect(await style(button, "border-top-left-radius")).toBe(await resolved(page, "borderRadius", "--ds-radius-md"));
      expect(await style(button, "font-size")).toBe(await resolved(page, "fontSize", "--ds-text-sm"));
      expect(await style(button, "font-weight"), "kit Button is medium weight").toBe(await resolved(page, "fontWeight", "--ds-weight-medium"));
      expect(await style(button, "background-color")).toBe(await tokenColour(page, "--ds-primary-solid"));
      expect(await style(button, "color")).toBe(await tokenColour(page, "--ds-text-on-accent"));
      expect(await style(button, "border-top-width")).toBe(await resolved(page, "borderTopWidth", "--ds-border-thin"));
      expect(await style(button, "padding-left"), "kit Button md padding").toBe(await resolved(page, "paddingLeft", "--ds-space-4"));
    });

    test(`Login (${scheme}): the error box looks like the kit danger Alert`, async ({ page }) => {
      await page.emulateMedia({ colorScheme: scheme, reducedMotion: "reduce" });
      await openLoginWithError(page);
      await settleAnimations(page);

      const alert = page.getByRole("alert");
      await expect(alert).toBeVisible();
      expect(await style(alert, "background-color")).toBe(await tokenColour(page, "--ds-danger-subtle"));
      expect(await style(alert, "color")).toBe(await tokenColour(page, "--ds-danger-text"));
      expect(await style(alert, "border-top-width"), "the kit Alert has no border").toBe("0px");
      expect(await style(alert, "border-top-left-radius")).toBe(await resolved(page, "borderRadius", "--ds-radius-md"));
      expect(await style(alert, "padding-top")).toBe(await resolved(page, "paddingTop", "--ds-space-3"));
      expect(await style(alert, "padding-left")).toBe(await resolved(page, "paddingLeft", "--ds-space-4"));
      expect(await style(alert, "font-size")).toBe(await resolved(page, "fontSize", "--ds-text-sm"));
      expect(await style(alert, "display")).toBe("flex");
      const icon = alert.locator("svg").first();
      expect(await icon.getAttribute("aria-hidden")).toBe("true");
      expect(await style(icon, "width")).toBe("18px");
      expect(await textContrast(alert.locator("p").first()), "message text on the alert ground").toBeGreaterThanOrEqual(4.5);
      expect(await runAxeChecks(page), "axe on the error state").toEqual([]);
    });

    for (const spec of PAGES) {
      test(`${spec.name} (${scheme}): fits a 375px phone with 16px gutters and no horizontal scroll`, async ({ page }) => {
        await page.setViewportSize({ width: 375, height: 700 });
        await page.emulateMedia({ colorScheme: scheme, reducedMotion: "reduce" });
        await page.goto(`${baseUrl}${spec.route}`, { waitUntil: "networkidle" });
        const metrics = await page.evaluate(() => ({ scrollWidth: document.documentElement.scrollWidth, clientWidth: document.documentElement.clientWidth }));
        expect(metrics.scrollWidth, "no horizontal scroll").toBeLessThanOrEqual(metrics.clientWidth);
        const box = await page.locator(".login-card, .logout-card").boundingBox();
        expect(box!.x, "left gutter").toBeGreaterThanOrEqual(16);
        expect(375 - (box!.x + box!.width), "right gutter").toBeGreaterThanOrEqual(16);
      });
    }
  }
});
