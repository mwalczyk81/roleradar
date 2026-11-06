import { describe, it, expect, vi } from "vitest";
import {
  THEME_COOKIE,
  THEME_BOOTSTRAP_SCRIPT,
  applyTheme,
  parseThemeCookie,
  serializeThemeCookie,
  systemTheme,
} from "../../app/lib/theme.js";

/** Runs the head bootstrap script against a fake document and window, the way a browser would before first paint. */
function runBootstrap(cookie: string, prefersDark: boolean | "no-matchmedia"): string | null {
  const attributes = new Map<string, string>();
  const fakeDocument = {
    cookie,
    documentElement: { setAttribute: (name: string, value: string) => attributes.set(name, value) },
  };
  const fakeWindow =
    prefersDark === "no-matchmedia" ? {} : { matchMedia: (query: string) => ({ matches: query.includes("dark") && prefersDark }) };
  new Function("document", "window", THEME_BOOTSTRAP_SCRIPT)(fakeDocument, fakeWindow);
  return attributes.get("data-theme") ?? null;
}

describe("theme module (009 US2, contracts/theme-module.md)", () => {
  it("keeps the cookie name unchanged", () => {
    expect(THEME_COOKIE).toBe("roleradar_theme");
  });

  describe("parseThemeCookie", () => {
    it.each([
      ["roleradar_theme=dark", "dark"],
      ["roleradar_theme=light", "light"],
      ["a=1; roleradar_theme=dark; b=2", "dark"],
      ["roleradar_theme=light; other=x", "light"],
      ["other=x;roleradar_theme=dark", "dark"],
    ])("reads %s as %s", (header, expected) => {
      expect(parseThemeCookie(header)).toBe(expected);
    });

    it.each([
      ["no cookie header", null],
      ["undefined header", undefined],
      ["empty header", ""],
      ["an unrelated cookie", "session=abc"],
      ["an invalid value", "roleradar_theme=purple"],
      ["a value that only starts like a valid one", "roleradar_theme=darkish"],
      ["a cookie whose name merely ends the same way", "x_roleradar_theme=dark"],
      ["an empty value", "roleradar_theme="],
    ])("ignores %s", (_label, header) => {
      expect(parseThemeCookie(header)).toBeUndefined();
    });
  });

  describe("serializeThemeCookie", () => {
    it("writes the same cookie string as before over http", () => {
      expect(serializeThemeCookie("dark", false)).toBe("roleradar_theme=dark; Path=/; SameSite=Lax; Max-Age=31536000");
      expect(serializeThemeCookie("light", false)).toBe("roleradar_theme=light; Path=/; SameSite=Lax; Max-Age=31536000");
    });

    it("adds Secure over https", () => {
      expect(serializeThemeCookie("dark", true)).toBe("roleradar_theme=dark; Path=/; SameSite=Lax; Max-Age=31536000; Secure");
    });

    it("round-trips through parseThemeCookie", () => {
      expect(parseThemeCookie(serializeThemeCookie("dark", true))).toBe("dark");
      expect(parseThemeCookie(serializeThemeCookie("light", false))).toBe("light");
    });
  });

  describe("systemTheme and applyTheme", () => {
    it("maps the media query result to a theme", () => {
      expect(systemTheme({ matches: true })).toBe("dark");
      expect(systemTheme({ matches: false })).toBe("light");
    });

    it("writes data-theme on the document element", () => {
      const setAttribute = vi.fn();
      applyTheme({ documentElement: { setAttribute } } as unknown as Document, "dark");
      expect(setAttribute).toHaveBeenCalledWith("data-theme", "dark");
    });
  });

  describe("THEME_BOOTSTRAP_SCRIPT (runs in the head before first paint)", () => {
    it("uses the cookie when present, whatever the system setting", () => {
      expect(runBootstrap("roleradar_theme=dark", false)).toBe("dark");
      expect(runBootstrap("roleradar_theme=light", true)).toBe("light");
    });

    it("falls back to the system setting when there is no valid cookie", () => {
      expect(runBootstrap("", true)).toBe("dark");
      expect(runBootstrap("", false)).toBe("light");
      expect(runBootstrap("roleradar_theme=purple", true)).toBe("dark");
    });

    it("does not throw when matchMedia is missing", () => {
      expect(() => runBootstrap("", "no-matchmedia")).not.toThrow();
      expect(runBootstrap("", "no-matchmedia")).toBeNull();
      expect(runBootstrap("roleradar_theme=dark", "no-matchmedia")).toBe("dark");
    });

    it("is built from the shared cookie name", () => {
      expect(THEME_BOOTSTRAP_SCRIPT).toContain(THEME_COOKIE);
    });
  });
});
