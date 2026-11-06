import { describe, it, expect } from "vitest";
import path from "node:path";
import { countStyleObjects, scanKitStyleEmitters } from "../../scripts/kit-style-emitters.mjs";

/**
 * The precondition for dropping `style-src-attr 'unsafe-inline'` from the web CSP (009 phase 6): the vendored kit source
 * must not contain an inline style OBJECT, `style={{ ... }}` or `style: { ... }`. Those render a `style` attribute that
 * the policy would block.
 *
 * Not counted, on purpose:
 * - `style={style}`: the caller's own `style` prop passed through. The app has none (the ESLint rule forbids it).
 * - `el.style.x = ...` and `style.setProperty(...)`: CSSOM writes, which the policy allows.
 *
 * The render side is guarded by tests/unit/kit-no-style-attributes.test.tsx (no kit component renders a style attribute);
 * this test only checks the source, so the two are not duplicates.
 */
const KIT_SRC = path.resolve(__dirname, "../../app/ds/components/src");

describe("countStyleObjects", () => {
  it("counts style object literals in JSX and in plain objects", () => {
    expect(countStyleObjects(`<div style={{ color: "red" }} />`)).toBe(1);
    expect(countStyleObjects(`<div style={ { color: "red" } } />`)).toBe(1);
    expect(countStyleObjects(`const props = { style: { color: "red" } };`)).toBe(1);
    expect(countStyleObjects(`<a style={{ a: 1 }} /><b style={{ b: 2 }} />`)).toBe(2);
  });

  it("does not count a passed-through style prop or CSSOM writes", () => {
    expect(countStyleObjects(`<div style={style} />`)).toBe(0);
    expect(countStyleObjects(`el.style.left = "1px"; el.style.setProperty("--x", "1"); node.style.minWidth = "0";`)).toBe(0);
    expect(countStyleObjects(`const { style, ...rest } = props;`)).toBe(0);
  });
});

describe("kit style emitters (vendored web/app/ds/components/src)", () => {
  it("emits no inline style object in any kit source file", () => {
    const result = scanKitStyleEmitters(KIT_SRC);
    expect(result.files.length, "the scan found the kit source").toBeGreaterThan(20);
    expect(result.perFile, `kit files with inline style objects: ${JSON.stringify(result.perFile)}`).toEqual({});
    expect(result.total).toBe(0);
  });
});
