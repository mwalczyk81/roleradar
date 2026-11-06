import { expect, type Locator } from "@playwright/test";

/**
 * 009 phase 3: computed-style pins for the inline-style conversion. A pin states the declarations the element used to
 * carry as an inline style, written exactly as they were (tokens and all), and the helper resolves each one the way the
 * browser would (on a stand-in copy of the element, through the CSS object model, which the CSP allows) and compares it with what the
 * real element computes now. A conversion that changes a value, drops a declaration or loses the class fails here.
 *
 * Properties are written in CSS kebab-case ("padding", "font-size").
 */

/**
 * Compares, for every declaration, the element's computed value with the value a stand-in computes when it carries the
 * old inline style instead of its classes. The stand-in is a deep copy of the element placed next to it, so it sits in
 * the same parent (same flex or grid context, same inheritance, same width) and has the same children, which is what
 * lets percentage widths, grid track sizes and inherited colours resolve the way they did when the style was inline.
 */
async function compareWithInlineStandIn(
  locator: Locator,
  declarations: Record<string, string>
): Promise<Array<{ property: string; css: string; expected: string; actual: string }>> {
  return await locator.evaluate((el, entries) => {
    const parent = el.parentElement as Element;
    const standIn = el.cloneNode(true) as HTMLElement;
    standIn.removeAttribute("class");
    standIn.removeAttribute("style");
    parent.insertBefore(standIn, el.nextSibling);
    const rows = (entries as Array<[string, string]>).map(([property, css]) => {
      standIn.style.setProperty(property, css);
      return { property, css, expected: "", actual: "" };
    });
    const standInStyle = getComputedStyle(standIn);
    const elementStyle = getComputedStyle(el);
    for (const row of rows) {
      row.expected = standInStyle.getPropertyValue(row.property);
      row.actual = elementStyle.getPropertyValue(row.property);
    }
    standIn.remove();
    return rows;
  }, Object.entries(declarations));
}

/** Each declaration, applied as an inline style to a stand-in, computes the same as on the first matched element. */
export async function expectDeclarations(locator: Locator, declarations: Record<string, string>): Promise<void> {
  expect(await locator.count(), `${String(locator)} matches at least one element`).toBeGreaterThan(0);
  const rows = await compareWithInlineStandIn(locator.first(), declarations);
  for (const row of rows) {
    expect(row.actual, `${row.property} of ${String(locator)} (${row.css})`).toBe(row.expected);
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
