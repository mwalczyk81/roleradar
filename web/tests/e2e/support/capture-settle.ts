import type { Page } from "@playwright/test";

/**
 * Brings a page to a state that a screenshot can capture deterministically, by waiting on real conditions rather than
 * on time (010 US3, the intermittent `company-detail-errors` visual baseline failure):
 *
 * - reduced motion, fonts loaded and every CSS animation finished;
 * - the scroll offset is 0. A full-page capture shows a `position: sticky` sidebar at the current scroll offset, and the
 *   click that precedes a capture (Playwright scrolls to the button) plus the reflow around an error banner leave the
 *   offset moving by a few pixels, which moves every sidebar glyph. Capturing from the top is canonical;
 * - the geometry (scroll offset, document height, the shell landmarks) is unchanged for several consecutive frames.
 *
 * `keepScroll: true` is for a capture of a hover (or any pointer-dependent state): the pointer is where the hover left
 * it, so scrolling would move other content under it (on the Insights page, a chart tooltip and a highlighted row).
 * The scroll offset is then left as it is and only the stability wait applies.
 *
 * If that never happens within the frame budget the capture is refused with the geometry that kept changing, instead
 * of producing a picture that depends on timing.
 */
const MAX_FRAMES = 240;
const STEADY_FRAMES = 4;

export interface SettleOptions {
  /** Do not scroll to the top (a hover capture: the pointer must stay on its target). */
  keepScroll?: boolean;
}

export async function settleForCapture(page: Page, options: SettleOptions = {}): Promise<void> {
  await page.emulateMedia({ reducedMotion: "reduce" });

  const unsettled = await page.evaluate(
    async ([maxFrames, steadyFrames, keepScroll]) => {
      await document.fonts.ready;
      await Promise.all(document.getAnimations().map((animation) => animation.finished));

      const nextFrame = () => new Promise<void>((resolve) => requestAnimationFrame(() => resolve()));
      const geometry = () => {
        const parts: string[] = [String(Math.round(window.scrollY)), String(document.documentElement.scrollHeight)];
        for (const element of document.querySelectorAll("aside, nav, main, header")) {
          const box = element.getBoundingClientRect();
          parts.push(`${element.tagName}:${box.x.toFixed(1)},${box.y.toFixed(1)},${box.width.toFixed(1)},${box.height.toFixed(1)}`);
        }
        return parts.join("|");
      };

      let last = "";
      let steady = 0;
      for (let frame = 0; frame < maxFrames; frame++) {
        if (!keepScroll) {
          window.scrollTo(0, 0);
        }
        await nextFrame();
        const now = geometry();
        steady = now === last && (keepScroll || window.scrollY === 0) ? steady + 1 : 0;
        last = now;
        if (steady >= steadyFrames) {
          return null;
        }
      }

      return last;
    },
    [MAX_FRAMES, STEADY_FRAMES, options.keepScroll === true] as const,
  );

  if (unsettled !== null) {
    throw new Error(
      `The page did not settle for a capture: the scroll offset or the layout kept changing for ${MAX_FRAMES} frames (last geometry: ${unsettled})`,
    );
  }
}
