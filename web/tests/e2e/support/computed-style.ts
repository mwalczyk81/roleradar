import { expect, type Locator } from "@playwright/test";

/**
 * 009 phase 3: computed-style pins for the inline-style conversion. A pin states the declarations the element used to
 * carry as an inline style, written exactly as they were (tokens and all), and the helper resolves each one the way the
 * browser would (on a probe element, through the CSS object model, which the CSP allows) and compares it with what the
 * real element computes now. A conversion that changes a value, drops a declaration or loses the class fails here.
 *
 * Properties are written in CSS kebab-case ("padding", "font-size"). Do not pin percentage widths: they resolve against
 * the parent, so a probe on the body cannot stand in for the element.
 */

async function resolveOnProbe(locator: Locator, property: string, css: string): Promise<string> {
  return await locator.page().evaluate(
    ([prop, value]) => {
      const probe = document.createElement("span");
      document.body.appendChild(probe);
      probe.style.setProperty(prop as string, value as string);
      const resolved = getComputedStyle(probe).getPropertyValue(prop as string);
      probe.remove();
      return resolved;
    },
    [property, css]
  );
}

/** Each declaration, resolved on a probe, equals the computed value on the first element the locator matches. */
export async function expectDeclarations(locator: Locator, declarations: Record<string, string>): Promise<void> {
  expect(await locator.count(), `${String(locator)} matches at least one element`).toBeGreaterThan(0);
  const target = locator.first();
  for (const [property, css] of Object.entries(declarations)) {
    const expected = await resolveOnProbe(target, property, css);
    const actual = await target.evaluate((el, prop) => getComputedStyle(el).getPropertyValue(prop), property);
    expect(actual, `${property} of ${String(locator)} (${css})`).toBe(expected);
  }
}

/** The element carries no style attribute at all: its look comes from classes only. */
export async function expectNoStyleAttribute(locator: Locator): Promise<void> {
  expect(await locator.count(), `${String(locator)} matches at least one element`).toBeGreaterThan(0);
  const attribute = await locator.first().getAttribute("style");
  expect(attribute, `${String(locator)} has no style attribute`).toBeNull();
}

/** A colour that was set to an undefined custom property computes to the inherited colour; pin it as "same as parent". */
export async function expectColourInheritedFromParent(locator: Locator): Promise<void> {
  const [own, parent] = await locator.first().evaluate((el) => [
    getComputedStyle(el).color,
    getComputedStyle(el.parentElement as Element).color,
  ]);
  expect(own, `${String(locator)} colour is inherited from its parent`).toBe(parent);
}
