import { describe, it, expect } from "vitest";
import { styleAttributeElements } from "../e2e/support/style-census.js";

describe("style attribute census (009 phase 1)", () => {
  it("lists every element that carries a style attribute as tag.firstClass", () => {
    const html = `<main style="padding:1rem" class="shell wide"><p>text</p><div class="ds-card extra" style="display:flex"></div></main>`;
    expect(styleAttributeElements(html)).toEqual(["main.shell", "div.ds-card"]);
  });

  it("uses the bare tag when the element has no class", () => {
    expect(styleAttributeElements(`<span style="white-space:nowrap">1 Nov</span>`)).toEqual(["span"]);
  });

  it("ignores elements without a style attribute and text that only looks like one", () => {
    const html = `<p class="note" title="style=x">style="not an attribute" &lt;div style="no"&gt;</p><dd data-style="x">ok</dd>`;
    expect(styleAttributeElements(html)).toEqual([]);
  });

  it("handles single-quoted, unquoted and self-closing tags", () => {
    expect(styleAttributeElements(`<hr style='margin:0'/><br style=none><input class="f" style="width:7rem" />`)).toEqual([
      "hr",
      "br",
      "input.f",
    ]);
  });

  it("returns an empty list for markup with no inline styles", () => {
    expect(styleAttributeElements(`<!DOCTYPE html><html><body><h1 class="t">Hi</h1></body></html>`)).toEqual([]);
  });
});
