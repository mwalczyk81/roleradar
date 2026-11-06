import type { Locator, Page } from "@playwright/test";

/**
 * 009 phase 4: WCAG contrast helpers for the polish spec. The maths is pure (unit tested in tests/unit/contrast.test.ts);
 * the page helpers read computed colours, so what is checked is what the browser paints, in the theme that is set.
 */

export interface Rgba {
  r: number;
  g: number;
  b: number;
  a: number;
}

/** Reads the colour strings getComputedStyle returns: rgb(), rgba() and color(srgb ...). */
export function parseCssColour(value: string): Rgba {
  const text = value.trim();
  const legacy = /^rgba?\(\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*([\d.]+)\s*(?:,\s*([\d.]+)\s*)?\)$/.exec(text);
  if (legacy) {
    return { r: Number(legacy[1]), g: Number(legacy[2]), b: Number(legacy[3]), a: legacy[4] === undefined ? 1 : Number(legacy[4]) };
  }
  const modern = /^rgba?\(\s*([\d.]+)\s+([\d.]+)\s+([\d.]+)\s*(?:\/\s*([\d.]+%?)\s*)?\)$/.exec(text);
  if (modern) {
    const alpha = modern[4] === undefined ? 1 : modern[4].endsWith("%") ? Number(modern[4].slice(0, -1)) / 100 : Number(modern[4]);
    return { r: Number(modern[1]), g: Number(modern[2]), b: Number(modern[3]), a: alpha };
  }
  const srgb = /^color\(srgb\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)\s*(?:\/\s*([\d.]+%?)\s*)?\)$/.exec(text);
  if (srgb) {
    const alpha = srgb[4] === undefined ? 1 : srgb[4].endsWith("%") ? Number(srgb[4].slice(0, -1)) / 100 : Number(srgb[4]);
    return { r: Number(srgb[1]) * 255, g: Number(srgb[2]) * 255, b: Number(srgb[3]) * 255, a: alpha };
  }
  throw new Error(`Cannot parse colour: ${value}`);
}

/** The colour a translucent foreground produces over an opaque background. */
export function compositeOver(foreground: Rgba, background: Rgba): Rgba {
  const a = foreground.a;
  return {
    r: foreground.r * a + background.r * (1 - a),
    g: foreground.g * a + background.g * (1 - a),
    b: foreground.b * a + background.b * (1 - a),
    a: 1,
  };
}

function channel(value: number): number {
  const v = value / 255;
  return v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4;
}

function luminance(c: Rgba): number {
  return 0.2126 * channel(c.r) + 0.7152 * channel(c.g) + 0.0722 * channel(c.b);
}

/** WCAG 2.x contrast ratio of two opaque colours. */
export function contrastRatio(a: Rgba, b: Rgba): number {
  const la = luminance(a);
  const lb = luminance(b);
  return (Math.max(la, lb) + 0.05) / (Math.min(la, lb) + 0.05);
}

/** Lets every running animation and transition finish (hover and focus colours are transitions, even when very short). */
export async function settleAnimations(page: Page): Promise<void> {
  await page.evaluate(async () => {
    const animations = document.getAnimations();
    await Promise.all(animations.map((a) => a.finished.catch(() => undefined)));
    await new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)));
  });
}

/** Sets the theme the way the app does (the data-theme attribute) and the matching colour scheme. */
export async function setTheme(page: Page, theme: "light" | "dark"): Promise<void> {
  await page.emulateMedia({ colorScheme: theme, reducedMotion: "reduce" });
  await page.evaluate((t) => document.documentElement.setAttribute("data-theme", t), theme);
  await settleAnimations(page);
}

/** What a design token resolves to as a computed colour, in the current theme. */
export async function tokenColour(page: Page, token: string): Promise<string> {
  return await page.evaluate((name) => {
    const probe = document.createElement("span");
    probe.style.setProperty("color", `var(${name})`);
    document.body.appendChild(probe);
    const value = getComputedStyle(probe).color;
    probe.remove();
    return value;
  }, token);
}

/** The element's computed text colour. */
export async function textColour(locator: Locator): Promise<string> {
  return await locator.evaluate((el) => getComputedStyle(el).color);
}

/** The opaque colour painted behind an element: its own and its ancestors' backgrounds composited, then the canvas. */
export async function paintedBackground(locator: Locator): Promise<Rgba> {
  const layers = await locator.evaluate((el) => {
    const out: string[] = [];
    let node: Element | null = el;
    while (node) {
      out.push(getComputedStyle(node).backgroundColor);
      node = node.parentElement;
    }
    out.push("rgb(255, 255, 255)");
    return out;
  });
  let result: Rgba = { r: 255, g: 255, b: 255, a: 1 };
  for (const layer of layers.reverse()) {
    const colour = parseCssColour(layer);
    if (colour.a > 0) result = compositeOver(colour, result);
  }
  return result;
}

/** Contrast of the element's own text colour against what is painted behind it. */
export async function textContrast(locator: Locator): Promise<number> {
  const foreground = parseCssColour(await textColour(locator));
  const background = await paintedBackground(locator);
  return contrastRatio(foreground.a < 1 ? compositeOver(foreground, background) : foreground, background);
}
