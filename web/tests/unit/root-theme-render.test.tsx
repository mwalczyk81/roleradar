import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import React from "react";
import { render } from "@testing-library/react";
import { createRoutesStub } from "react-router";
import App from "../../app/root.js";

/**
 * 009 US2: render the root document for each cookie state and for no cookie, and read the data-theme the document
 * ends up with. React renders <html> onto the document element itself, so the attribute can be read from
 * document.documentElement. The first server-rendered response is checked in tests/e2e/theme-first-paint.spec.ts.
 */

function mockSystemDark(matches: boolean) {
  window.matchMedia = vi.fn().mockImplementation((query: string) => ({
    matches: query.includes("dark") && matches,
    media: query,
    onchange: null,
    addListener: vi.fn(),
    removeListener: vi.fn(),
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
    dispatchEvent: vi.fn(),
  })) as unknown as typeof window.matchMedia;
}

function clearCookie() {
  document.cookie = "roleradar_theme=; expires=Thu, 01 Jan 1970 00:00:00 UTC; path=/;";
}

async function renderRoot(serverTheme: "light" | "dark" | undefined) {
  const Stub = createRoutesStub([
    {
      path: "/",
      Component: App,
      loader: () => ({ theme: serverTheme, nonce: "test-nonce" }),
      children: [{ index: true, Component: () => <div>root child</div> }],
    },
  ]);
  const view = render(React.createElement(Stub, { initialEntries: ["/"] }));
  await view.findByText("root child");
  return view;
}

describe("root document theme (009 US2)", () => {
  const originalMatchMedia = window.matchMedia;

  beforeEach(() => {
    clearCookie();
    document.documentElement.removeAttribute("data-theme");
  });

  afterEach(() => {
    clearCookie();
    document.documentElement.removeAttribute("data-theme");
    window.matchMedia = originalMatchMedia;
    vi.restoreAllMocks();
  });

  it("cookie dark: the document is dark, even when the system is light", async () => {
    mockSystemDark(false);
    document.cookie = "roleradar_theme=dark; path=/;";
    const view = await renderRoot("dark");
    expect(document.documentElement.getAttribute("data-theme")).toBe("dark");
    view.unmount();
  });

  it("cookie light: the document is light, even when the system is dark", async () => {
    mockSystemDark(true);
    document.cookie = "roleradar_theme=light; path=/;";
    const view = await renderRoot("light");
    expect(document.documentElement.getAttribute("data-theme")).toBe("light");
    view.unmount();
  });

  it("no cookie and a dark system: the document is dark", async () => {
    mockSystemDark(true);
    const view = await renderRoot(undefined);
    expect(document.documentElement.getAttribute("data-theme")).toBe("dark");
    view.unmount();
  });

  it("no cookie and a light system: the document is light", async () => {
    mockSystemDark(false);
    const view = await renderRoot(undefined);
    expect(document.documentElement.getAttribute("data-theme")).toBe("light");
    view.unmount();
  });

  it("no cookie: follows a live system change", async () => {
    let change: ((e: { matches: boolean }) => void) | undefined;
    window.matchMedia = vi.fn().mockImplementation((query: string) => ({
      matches: false,
      media: query,
      addEventListener: vi.fn((event: string, handler: (e: { matches: boolean }) => void) => {
        if (event === "change") change = handler;
      }),
      removeEventListener: vi.fn(),
    })) as unknown as typeof window.matchMedia;

    const view = await renderRoot(undefined);
    expect(document.documentElement.getAttribute("data-theme")).toBe("light");
    expect(change).toBeDefined();
    const { act } = await import("@testing-library/react");
    act(() => change?.({ matches: true }));
    expect(document.documentElement.getAttribute("data-theme")).toBe("dark");
    view.unmount();
  });
});
