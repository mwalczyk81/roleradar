// @vitest-environment node
import { describe, it, expect } from "vitest";
import React from "react";
import { renderToString } from "react-dom/server";
import { useTheme } from "../../app/lib/use-theme.js";

/**
 * 009 US2: the hook on the server, where there is no document, cookie or matchMedia. It must render the theme it is
 * given (the cookie the loader read), and light when it is given nothing, without throwing.
 */

function Probe({ initial }: { initial?: "light" | "dark" }) {
  const { theme } = useTheme(initial);
  return React.createElement("span", { "data-probe": theme }, theme);
}

describe("useTheme during server rendering", () => {
  it("renders the theme from the cookie that the loader read", () => {
    expect(renderToString(React.createElement(Probe, { initial: "dark" }))).toContain('data-probe="dark"');
    expect(renderToString(React.createElement(Probe, { initial: "light" }))).toContain('data-probe="light"');
  });

  it("renders light when there is no cookie, without touching document or window", () => {
    expect(typeof document).toBe("undefined");
    expect(renderToString(React.createElement(Probe, {}))).toContain('data-probe="light"');
  });
});
