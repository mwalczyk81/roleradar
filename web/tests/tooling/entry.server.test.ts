import { describe, it, expect, vi, beforeEach } from "vitest";
import type { EntryContext, AppLoadContext } from "react-router";

const mockPipe = vi.fn();
const mockAbort = vi.fn();
let mockReadyCallback: "onShellReady" | "onAllReady" = "onShellReady";
let mockErrorToThrow: Error | null = null;
let mockShellErrorToThrow: Error | null = null;

vi.mock("react-dom/server", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-dom/server")>();
  return {
    ...actual,
    renderToPipeableStream: (
      _element: unknown,
      options: {
        onShellReady?: () => void;
        onAllReady?: () => void;
        onShellError: (err: unknown) => void;
        onError: (err: unknown) => void;
        [key: string]: unknown;
      }
    ) => {
      setTimeout(() => {
        if (mockShellErrorToThrow) {
          options.onShellError(mockShellErrorToThrow);
        } else {
          if (mockErrorToThrow) {
            options.onError(mockErrorToThrow);
          }
          if (options[mockReadyCallback]) {
            const callback = options[mockReadyCallback] as () => void;
            callback();
          }
        }
      }, 0);
      return { pipe: mockPipe, abort: mockAbort };
    },
  };
});

import handleRequest, { streamTimeout } from "../../app/entry.server.js";

const emptyContext = {} as unknown as EntryContext;
const emptyLoadContext = {} as unknown as AppLoadContext;

describe("entry.server", () => {
  beforeEach(() => {
    mockPipe.mockClear();
    mockAbort.mockClear();
    mockReadyCallback = "onShellReady";
    mockErrorToThrow = null;
    mockShellErrorToThrow = null;
  });

  it("exports streamTimeout as 5000ms", () => {
    expect(streamTimeout).toBe(5000);
  });

  it("handles HEAD requests immediately without streaming body", async () => {
    const headRequest = new Request("https://localhost:5003/postings", {
      method: "HEAD",
    });
    const headers = new Headers({ "X-Test": "Value" });

    const response = (await handleRequest(
      headRequest,
      200,
      headers,
      emptyContext,
      emptyLoadContext
    )) as Response;

    expect(response.status).toBe(200);
    expect(response.headers.get("X-Test")).toBe("Value");
    expect(response.body).toBeNull();
  });

  it("renders with onShellReady for non-bot user agent", async () => {
    mockReadyCallback = "onShellReady";

    const getRequest = new Request("https://localhost:5003/postings", {
      method: "GET",
      headers: { "user-agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64)" },
    });
    const headers = new Headers();

    const response = (await handleRequest(
      getRequest,
      200,
      headers,
      { isSpaMode: false } as unknown as EntryContext,
      { nonce: "nonce-123" } as unknown as AppLoadContext
    )) as Response;

    expect(response.status).toBe(200);
    expect(response.headers.get("Content-Type")).toBe("text/html");
    expect(mockPipe).toHaveBeenCalled();
  });

  it("renders with onAllReady for bots or SPA mode", async () => {
    mockReadyCallback = "onAllReady";

    const botRequest = new Request("https://localhost:5003/postings", {
      method: "GET",
      headers: { "user-agent": "Googlebot/2.1 (+http://www.google.com/bot.html)" },
    });
    const headers = new Headers();

    const response = (await handleRequest(
      botRequest,
      200,
      headers,
      { isSpaMode: true } as unknown as EntryContext,
      emptyLoadContext
    )) as Response;

    expect(response.status).toBe(200);
    expect(mockPipe).toHaveBeenCalled();
  });

  it("handles onShellError by rejecting the promise", async () => {
    mockShellErrorToThrow = new Error("Shell fatal render error");

    const request = new Request("https://localhost:5003/postings", { method: "GET" });
    await expect(
      handleRequest(request, 200, new Headers(), { isSpaMode: false } as unknown as EntryContext, emptyLoadContext)
    ).rejects.toThrow("Shell fatal render error");
  });

  it("handles onError callback by updating response status code", async () => {
    mockErrorToThrow = new Error("Render pipeline error");

    const request = new Request("https://localhost:5003/postings", { method: "GET" });
    const response = (await handleRequest(
      request,
      200,
      new Headers(),
      { isSpaMode: false } as unknown as EntryContext,
      emptyLoadContext
    )) as Response;

    expect(response.status).toBe(500);
  });
});
