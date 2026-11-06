import { describe, expect, it } from "vitest";
import { compositeOver, contrastRatio, parseCssColour } from "../e2e/support/contrast.js";

/** 009 phase 4: the contrast maths the polish spec relies on, pinned against known WCAG values. */
describe("contrast helper", () => {
  it("parses the colour strings Chromium returns for computed styles", () => {
    expect(parseCssColour("rgb(153, 95, 44)")).toEqual({ r: 153, g: 95, b: 44, a: 1 });
    expect(parseCssColour("rgba(0, 0, 0, 0.5)")).toEqual({ r: 0, g: 0, b: 0, a: 0.5 });
    expect(parseCssColour("rgba(0, 0, 0, 0)")).toEqual({ r: 0, g: 0, b: 0, a: 0 });
    expect(parseCssColour("color(srgb 1 0.5 0 / 0.25)")).toEqual({ r: 255, g: 127.5, b: 0, a: 0.25 });
  });

  it("rejects a colour it cannot read instead of guessing", () => {
    expect(() => parseCssColour("transparentish")).toThrow(/Cannot parse colour/);
  });

  it("gives 21 for black on white and 1 for identical colours", () => {
    const black = parseCssColour("rgb(0, 0, 0)");
    const white = parseCssColour("rgb(255, 255, 255)");
    expect(contrastRatio(black, white)).toBeCloseTo(21, 5);
    expect(contrastRatio(white, black)).toBeCloseTo(21, 5);
    expect(contrastRatio(white, white)).toBeCloseTo(1, 5);
  });

  it("matches known kit pairs (bronze 600 on white is 5.21, bronze 400 on neutral 700 is 4.05)", () => {
    expect(contrastRatio(parseCssColour("rgb(153, 95, 44)"), parseCssColour("rgb(255, 255, 255)"))).toBeCloseTo(5.21, 2);
    expect(contrastRatio(parseCssColour("rgb(207, 151, 106)"), parseCssColour("rgb(68, 64, 60)"))).toBeCloseTo(4.05, 2);
  });

  it("composites a translucent colour over its background before measuring", () => {
    const over = compositeOver(parseCssColour("rgba(0, 0, 0, 0.5)"), parseCssColour("rgb(255, 255, 255)"));
    expect(over).toEqual({ r: 127.5, g: 127.5, b: 127.5, a: 1 });
  });
});
