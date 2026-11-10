import { describe, it, expect, vi } from "vitest";
import React from "react";
import { render } from "@testing-library/react";
import { createRoutesStub } from "react-router";
import App, { meta, links, loader, ErrorBoundary } from "../../app/root.js";
import { SERVICE_UNAVAILABLE_MESSAGE } from "../../app/lib/observability.js";

describe("Root layout (root.tsx)", () => {
  it("exports meta returning title RoleRadar", () => {
    const metaTags = meta({} as unknown as Parameters<typeof meta>[0]);
    expect(metaTags).toEqual([{ title: "RoleRadar" }]);
  });

  it("exports links returning stylesheets", () => {
    const linkTags = links();
    expect(linkTags.length).toBeGreaterThanOrEqual(3);
    for (const link of linkTags) {
      expect((link as { rel?: string }).rel).toBe("stylesheet");
    }
  });

  it("loader extracts theme from cookie and nonce from context or headers", async () => {
    const reqWithCookie = new Request("https://localhost:5003/", {
      headers: {
        Cookie: "roleradar_theme=dark",
        "X-CSP-Nonce": "test-nonce-123",
      },
    });

    const res = await loader({
      request: reqWithCookie,
      params: {},
      context: { nonce: "ctx-nonce-456" },
    } as unknown as Parameters<typeof loader>[0]);

    expect(res.theme).toBe("dark");
    expect(res.nonce).toBe("ctx-nonce-456");

    const reqWithoutContext = new Request("https://localhost:5003/", {
      headers: {
        Cookie: "roleradar_theme=light",
        "X-CSP-Nonce": "header-nonce-789",
      },
    });

    const res2 = await loader({
      request: reqWithoutContext,
      params: {},
      context: {},
    } as unknown as Parameters<typeof loader>[0]);

    expect(res2.theme).toBe("light");
    expect(res2.nonce).toBe("header-nonce-789");
  });

  it("App component renders with server theme and nonce", async () => {
    const AppStub = createRoutesStub([
      {
        path: "/",
        Component: App,
        loader: () => ({
          theme: "dark",
          nonce: "test-nonce-app",
        }),
        children: [
          {
            index: true,
            Component: () => <div>Home child route</div>,
          },
        ],
      },
    ]);

    const { findByText } = render(React.createElement(AppStub, { initialEntries: ["/"] }));
    expect(await findByText("Home child route")).toBeDefined();
  });

  it("App component initializes theme from document.cookie, data-theme, and prefers-color-scheme", async () => {
    // 1. With cookie
    document.cookie = "roleradar_theme=dark; path=/;";
    let AppStub = createRoutesStub([
      {
        path: "/",
        Component: App,
        loader: () => ({ theme: "light", nonce: "nonce-1" }),
        children: [{ index: true, Component: () => <div>Cookie Route</div> }],
      },
    ]);
    let res = render(React.createElement(AppStub, { initialEntries: ["/"] }));
    expect(await res.findByText("Cookie Route")).toBeDefined();
    res.unmount();

    // 2. With data-theme attribute
    document.cookie = "roleradar_theme=; expires=Thu, 01 Jan 1970 00:00:00 UTC; path=/;";
    document.documentElement.setAttribute("data-theme", "dark");
    AppStub = createRoutesStub([
      {
        path: "/",
        Component: App,
        loader: () => ({ theme: "light", nonce: "nonce-2" }),
        children: [{ index: true, Component: () => <div>Attr Route</div> }],
      },
    ]);
    res = render(React.createElement(AppStub, { initialEntries: ["/"] }));
    expect(await res.findByText("Attr Route")).toBeDefined();
    res.unmount();
    document.documentElement.removeAttribute("data-theme");

    // 3. With matchMedia
    const changeHandlers: Array<(e: { matches: boolean }) => void> = [];
    window.matchMedia = vi.fn().mockImplementation((query: string) => ({
      matches: true,
      media: query,
      addEventListener: vi.fn((event: string, handler: (e: { matches: boolean }) => void) => {
        if (event === "change") changeHandlers.push(handler);
      }),
      removeEventListener: vi.fn(),
    })) as unknown as typeof window.matchMedia;

    AppStub = createRoutesStub([
      {
        path: "/",
        Component: App,
        loader: () => ({ theme: "light", nonce: "nonce-3" }),
        children: [{ index: true, Component: () => <div>MatchMedia Route</div> }],
      },
    ]);
    res = render(React.createElement(AppStub, { initialEntries: ["/"] }));
    expect(await res.findByText("MatchMedia Route")).toBeDefined();
    for (const handler of changeHandlers) {
      handler({ matches: false });
    }
    res.unmount();
  });

  it("ErrorBoundary renders application error heading and message", async () => {
    const AppStub = createRoutesStub([
      {
        path: "/",
        Component: () => {
          throw new Error("Boom test failure");
        },
        ErrorBoundary: ErrorBoundary,
      },
    ]);

    const { findByText } = render(React.createElement(AppStub, { initialEntries: ["/"] }));
    expect(await findByText("Application Error")).toBeDefined();
    expect(await findByText("Boom test failure")).toBeDefined();
  });

  it("ErrorBoundary shows a friendly, retryable message for a 503 and never the generic application error", async () => {
    const AppStub = createRoutesStub([
      {
        path: "/",
        Component: () => <div>never rendered</div>,
        loader: () => {
          throw new Response(SERVICE_UNAVAILABLE_MESSAGE, { status: 503 });
        },
        ErrorBoundary: ErrorBoundary,
      },
    ]);

    const { findByRole, findByText, queryByText } = render(React.createElement(AppStub, { initialEntries: ["/"] }));

    expect(await findByRole("heading", { name: /temporarily unavailable/i })).toBeDefined();
    expect(await findByText(SERVICE_UNAVAILABLE_MESSAGE)).toBeDefined();
    const retry = await findByRole("link", { name: /try again/i });
    expect(retry.getAttribute("href")).toBe("/");
    expect(queryByText("Application Error")).toBeNull();
  });

  it("ErrorBoundary does not echo the text of a non-503 response either", async () => {
    const AppStub = createRoutesStub([
      {
        path: "/",
        Component: () => <div>never rendered</div>,
        loader: () => {
          throw new Response("MongoConnectionException at db.fictional.invalid", { status: 500 });
        },
        ErrorBoundary: ErrorBoundary,
      },
    ]);

    const { findByText, queryByText } = render(React.createElement(AppStub, { initialEntries: ["/"] }));

    expect(await findByText("Application Error")).toBeDefined();
    expect(queryByText(/Mongo/)).toBeNull();
  });
});
