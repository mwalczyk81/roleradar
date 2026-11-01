import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { renderHook, act } from "@testing-library/react";
import { useTheme } from "../../app/lib/use-theme.js";

describe("useTheme hook", () => {
  const originalCookie = document.cookie;

  beforeEach(() => {
    document.documentElement.removeAttribute("data-theme");
    document.cookie = "roleradar_theme=; expires=Thu, 01 Jan 1970 00:00:00 UTC; path=/;";
  });

  afterEach(() => {
    document.cookie = originalCookie;
    vi.restoreAllMocks();
  });

  it("defaults to initialTheme or light", () => {
    const { result } = renderHook(() => useTheme("dark"));
    expect(result.current.theme).toBe("dark");

    const { result: defaultResult } = renderHook(() => useTheme());
    expect(defaultResult.current.theme).toBe("light");
  });

  it("detects theme from documentElement data-theme attribute", () => {
    document.documentElement.setAttribute("data-theme", "dark");
    const { result } = renderHook(() => useTheme());
    expect(result.current.theme).toBe("dark");
  });

  it("toggles theme from light to dark and writes cookie and document attribute", () => {
    const { result } = renderHook(() => useTheme("light"));
    expect(result.current.theme).toBe("light");

    act(() => {
      result.current.toggleTheme();
    });

    expect(result.current.theme).toBe("dark");
    expect(document.documentElement.getAttribute("data-theme")).toBe("dark");
    expect(document.cookie).toContain("roleradar_theme=dark");

    act(() => {
      result.current.toggleTheme();
    });

    expect(result.current.theme).toBe("light");
    expect(document.documentElement.getAttribute("data-theme")).toBe("light");
    expect(document.cookie).toContain("roleradar_theme=light");
  });

  it("syncs with matchMedia change when no cookie is set", () => {
    let changeHandler: ((e: MediaQueryListEvent) => void) | null = null;
    const matchMediaMock = vi.fn().mockImplementation((query: string) => ({
      matches: false,
      media: query,
      onchange: null,
      addListener: vi.fn(),
      removeListener: vi.fn(),
      addEventListener: vi.fn((event: string, handler: (e: MediaQueryListEvent) => void) => {
        if (event === "change") changeHandler = handler;
      }),
      removeEventListener: vi.fn(),
      dispatchEvent: vi.fn(),
    }));
    window.matchMedia = matchMediaMock as unknown as typeof window.matchMedia;

    const { result } = renderHook(() => useTheme());
    expect(result.current.theme).toBe("light");

    expect(changeHandler).not.toBeNull();
    act(() => {
      changeHandler?.({ matches: true } as MediaQueryListEvent);
    });

    expect(result.current.theme).toBe("dark");
    expect(document.documentElement.getAttribute("data-theme")).toBe("dark");
  });

  it("detects theme from matchMedia when no data-theme is present", () => {
    window.matchMedia = vi.fn().mockImplementation((query: string) => ({
      matches: query.includes("dark"),
      media: query,
      onchange: null,
      addListener: vi.fn(),
      removeListener: vi.fn(),
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      dispatchEvent: vi.fn(),
    })) as unknown as typeof window.matchMedia;

    const { result } = renderHook(() => useTheme());
    expect(result.current.theme).toBe("dark");
  });

  it("appends Secure attribute to cookie when protocol is https", () => {
    const originalLocation = window.location;
    const cookieSpy = vi.spyOn(document, "cookie", "set");
    Object.defineProperty(window, "location", {
      value: { ...originalLocation, protocol: "https:" },
      writable: true,
      configurable: true,
    });

    try {
      const { result } = renderHook(() => useTheme("light"));
      act(() => {
        result.current.toggleTheme();
      });
      expect(cookieSpy).toHaveBeenCalledWith(expect.stringContaining("; Secure"));
    } finally {
      cookieSpy.mockRestore();
      Object.defineProperty(window, "location", {
        value: originalLocation,
        writable: true,
        configurable: true,
      });
    }
  });

  it("ignores matchMedia change when cookie theme is already set", () => {
    document.cookie = "roleradar_theme=light; path=/;";
    let changeHandler: ((e: MediaQueryListEvent) => void) | null = null;
    window.matchMedia = vi.fn().mockImplementation((query: string) => ({
      matches: false,
      media: query,
      onchange: null,
      addListener: vi.fn(),
      removeListener: vi.fn(),
      addEventListener: vi.fn((event: string, handler: (e: MediaQueryListEvent) => void) => {
        if (event === "change") changeHandler = handler;
      }),
      removeEventListener: vi.fn(),
      dispatchEvent: vi.fn(),
    })) as unknown as typeof window.matchMedia;

    const { result } = renderHook(() => useTheme());
    expect(result.current.theme).toBe("light");

    // Even if handler is called, cookie presence prevents update
    if (changeHandler) {
      act(() => {
        changeHandler?.({ matches: true } as MediaQueryListEvent);
      });
      expect(result.current.theme).toBe("light");
    }
  });
});
