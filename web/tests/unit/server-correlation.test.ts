import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import fs from "node:fs";
import http from "node:http";
import path from "node:path";
import type { AddressInfo } from "node:net";
import { createRequestHandler, setRequestListenerForTesting, startServer } from "../../server.js";
import { log } from "../../app/lib/log.server";

// Feature 013 US1 (FR-002, FR-003, FR-006): the web edge accepts or creates X-Correlation-ID, returns it on every
// response, writes one RequestCompleted record per request without the query string, and logs startup at info.

const HEADER = "x-correlation-id";
const ACCEPTED = /^[A-Za-z0-9][A-Za-z0-9._-]{7,63}$/;

function captureStreams() {
  const lines: string[] = [];
  const push = (chunk: string | Uint8Array) => {
    lines.push(...String(chunk).split("\n").filter((l) => l.length > 0));
    return true;
  };
  vi.spyOn(process.stdout, "write").mockImplementation(push);
  vi.spyOn(process.stderr, "write").mockImplementation(push);
  return lines;
}

function records(lines: string[]) {
  return lines.map((line) => JSON.parse(line) as Record<string, unknown>);
}

function requestCompleted(lines: string[], correlationId: string) {
  return records(lines).filter((r) => r.event === "RequestCompleted" && r.correlationId === correlationId);
}

async function waitFor(predicate: () => boolean) {
  for (let i = 0; i < 50 && !predicate(); i++) {
    await new Promise((resolve) => setTimeout(resolve, 10));
  }
}

describe("server.ts correlation and request records", () => {
  const originalEnv = { ...process.env };
  let server: http.Server;
  let baseUrl: string;

  beforeEach(async () => {
    process.env = { ...originalEnv, NODE_ENV: "test", ROLERADAR_LOG_FORMAT: "json" };
    server = http.createServer(createRequestHandler());
    await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
    baseUrl = `http://127.0.0.1:${(server.address() as AddressInfo).port}`;
  });

  afterEach(async () => {
    setRequestListenerForTesting(null);
    await new Promise<void>((resolve) => server.close(() => resolve()));
    process.env = { ...originalEnv };
    vi.restoreAllMocks();
  });

  it("returns a well-formed X-Correlation-ID on every response", async () => {
    captureStreams();
    setRequestListenerForTesting((_req, res) => {
      res.writeHead(200, { "Content-Type": "text/plain" });
      res.end("app route");
    });

    const responses = [
      await fetch(`${baseUrl}/health`),
      await fetch(`${baseUrl}/favicon.ico`),
      await fetch(`${baseUrl}/postings`),
    ];

    const ids = responses.map((r) => r.headers.get(HEADER));
    for (const id of ids) {
      expect(id).toMatch(ACCEPTED);
    }
    expect(new Set(ids).size).toBe(ids.length);
  });

  it.skipIf(!fs.existsSync(path.resolve(process.cwd(), "build/client")))(
    "returns the header on a static file too",
    async () => {
      captureStreams();
      const clientDir = path.resolve(process.cwd(), "build/client");
      const file = fs.readdirSync(clientDir).find((name) => fs.statSync(path.join(clientDir, name)).isFile());
      if (!file) return;

      const response = await fetch(`${baseUrl}/${file}`);
      expect(response.headers.get(HEADER)).toMatch(ACCEPTED);
    }
  );

  it("returns the header on a handler error, and logs the error with the same id", async () => {
    const lines = captureStreams();
    setRequestListenerForTesting(() => {
      throw new Error("listener exploded");
    });

    const response = await fetch(`${baseUrl}/postings`);
    const id = response.headers.get(HEADER) ?? "";

    expect(response.status).toBe(500);
    expect(id).toMatch(ACCEPTED);
    await waitFor(() => requestCompleted(lines, id).length > 0);
    const errors = records(lines).filter((r) => r.level === "error" && r.correlationId === id);
    expect(errors.length).toBeGreaterThan(0);
  });

  it("echoes a well-formed inbound id and replaces a malformed one without logging it", async () => {
    const lines = captureStreams();

    const good = await fetch(`${baseUrl}/health`, { headers: { "X-Correlation-ID": "test-corr-web-0001" } });
    expect(good.headers.get(HEADER)).toBe("test-corr-web-0001");

    const bad = await fetch(`${baseUrl}/health`, { headers: { "X-Correlation-ID": "bad id<script>" } });
    const replaced = bad.headers.get(HEADER) ?? "";
    expect(replaced).toMatch(ACCEPTED);
    expect(replaced).not.toBe("bad id<script>");

    process.env.ROLERADAR_LOG_LEVEL = "debug";
    const third = await fetch(`${baseUrl}/health`, { headers: { "X-Correlation-ID": "bad id<script>" } });
    await waitFor(() => requestCompleted(lines, third.headers.get(HEADER) ?? "").length > 0);
    expect(lines.join("\n")).not.toContain("bad id");
    expect(lines.join("\n")).not.toContain("<script>");
  });

  it("writes exactly one RequestCompleted record per request, without the query string", async () => {
    const lines = captureStreams();
    setRequestListenerForTesting((_req, res) => {
      res.writeHead(302, { Location: "/postings" });
      res.end();
    });

    const response = await fetch(`${baseUrl}/auth/callback?code=abc-SENTINEL&state=xyz-SENTINEL`, { redirect: "manual" });
    const id = response.headers.get(HEADER) ?? "";
    await waitFor(() => requestCompleted(lines, id).length > 0);

    expect(lines.length).toBeGreaterThan(0);
    const matching = requestCompleted(lines, id);
    expect(matching).toHaveLength(1);
    expect(matching[0].props).toMatchObject({ method: "GET", path: "/auth/callback", status: 302 });
    expect(typeof (matching[0].props as Record<string, unknown>).durationMs).toBe("number");
    const all = lines.join("\n");
    expect(all).not.toContain("abc-SENTINEL");
    expect(all).not.toContain("xyz-SENTINEL");
  });

  it("logs health requests at debug, so healthchecks do not flood the default level", async () => {
    const lines = captureStreams();

    const response = await fetch(`${baseUrl}/health`);
    await new Promise((resolve) => setTimeout(resolve, 30));

    expect(requestCompleted(lines, response.headers.get(HEADER) ?? "")).toHaveLength(0);
  });

  it("logs the listening line at info, not error (FR-002)", async () => {
    const lines = captureStreams();
    process.env.ROLERADAR_LOG_FORMAT = "json";
    const infoSpy = vi.spyOn(log, "info");

    const listening = startServer(0);
    await new Promise<void>((resolve) => listening.once("listening", () => resolve()));
    await new Promise<void>((resolve) => listening.close(() => resolve()));

    const startup = records(lines).find((r) => String(r.msg).includes("listening"));
    expect(startup?.level).toBe("info");
    expect(infoSpy).toHaveBeenCalled();
  });
});
