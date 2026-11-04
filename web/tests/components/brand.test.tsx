import { describe, it, expect } from "vitest";
import { render } from "@testing-library/react";
import { Brand } from "../../app/components/Brand.js";

describe("Brand (shared shell brand)", () => {
  it("renders the app name RoleRadar as its text", () => {
    const { container } = render(<Brand />);
    expect(container.textContent).toBe("RoleRadar");
  });

  it("keeps the accessible name RoleRadar: the mark is decorative and hidden from assistive tech", () => {
    const { container } = render(<Brand />);
    const svg = container.querySelector("svg");
    expect(svg).not.toBeNull();
    expect(svg?.getAttribute("aria-hidden")).toBe("true");
    expect(svg?.getAttribute("focusable")).toBe("false");
    expect(svg?.querySelector("title")).toBeNull();
    expect(svg?.hasAttribute("aria-label")).toBe(false);
  });

  it("draws the radar mark: two circles r=9.5 and r=5, a line from the centre toward the upper right, a filled dot at its end", () => {
    const { container } = render(<Brand />);
    const circles = Array.from(container.querySelectorAll("svg circle"));
    const rings = circles.filter((c) => c.getAttribute("fill") === "none");
    expect(rings.map((c) => c.getAttribute("r")).sort()).toEqual(["5", "9.5"]);
    for (const ring of rings) {
      expect(ring.getAttribute("cx")).toBe("12");
      expect(ring.getAttribute("cy")).toBe("12");
      expect(ring.getAttribute("stroke")).toBe("currentColor");
    }

    const line = container.querySelector("svg line");
    expect(line).not.toBeNull();
    expect(line?.getAttribute("x1")).toBe("12");
    expect(line?.getAttribute("y1")).toBe("12");
    const x2 = Number(line?.getAttribute("x2"));
    const y2 = Number(line?.getAttribute("y2"));
    expect(x2).toBeGreaterThan(12); // toward the right
    expect(y2).toBeLessThan(12); // toward the top
    expect(line?.getAttribute("stroke")).toBe("currentColor");

    const dot = circles.find((c) => c.getAttribute("fill") === "currentColor");
    expect(dot).toBeDefined();
    expect(Number(dot?.getAttribute("cx"))).toBe(x2);
    expect(Number(dot?.getAttribute("cy"))).toBe(y2);
  });

  it("styles through classes only: no inline style attributes (CSP)", () => {
    const { container } = render(<Brand />);
    expect(container.querySelectorAll("[style]").length).toBe(0);
    expect(container.querySelector(".brand")).not.toBeNull();
    expect(container.querySelector(".brand-mark")).not.toBeNull();
    expect(container.querySelector(".brand-name")?.textContent).toBe("RoleRadar");
  });
});
