import { expect, type Page } from "@playwright/test";
import axe from "axe-core";

/** Serious and critical WCAG 2.x A and AA violations on the current page. */
export async function runAxeChecks(page: Page) {
  await page.evaluate(axe.source);
  const results = await page.evaluate(async () => {
    const win = window as unknown as {
      axe: { run: (doc: Document, opts: unknown) => Promise<{ violations: Array<{ id: string; impact?: string; nodes: Array<{ target: unknown }> }> }> };
    };
    return await win.axe.run(document, {
      runOnly: { type: "tag", values: ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"] },
    });
  });
  return results.violations
    .filter((v) => v.impact === "serious" || v.impact === "critical")
    .map((v) => ({ id: v.id, impact: v.impact, targets: v.nodes.map((n) => String(n.target)).slice(0, 5) }));
}

/**
 * The shared reduced-motion axe check: reduced motion is emulated so bundle.css disables transitions, every
 * animation is allowed to settle, and only then is axe run, in both themes. Results are deterministic.
 */
export async function checkAxeInThemes(page: Page) {
  await page.emulateMedia({ reducedMotion: "reduce" });

  for (const theme of ["light", "dark"] as const) {
    await page.evaluate((t) => {
      document.documentElement.setAttribute("data-theme", t);
    }, theme);

    await page.evaluate(async () => {
      const animations = document.getAnimations();
      // A cancelled animation (for example a dialog exit that was cut short) rejects `finished`; it has settled either way.
      await Promise.all(animations.map((a) => a.finished.catch(() => undefined)));
      await new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)));
    });

    expect(await runAxeChecks(page), `axe violations in the ${theme} theme`).toEqual([]);
  }
}
