import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import fs from "node:fs";
import http from "node:http";
import path from "node:path";
import type { AddressInfo } from "node:net";
import { createRequestHandler, setRequestListenerForTesting, startServer } from "../../server.js";
import { log } from "../../app/lib/log.server";
import { getRequestContext } from "../../app/lib/request-context.server";

// Feature 014 US2 (FR-023, FR-026, contracts/trace-context.md; 013 FR-002, FR-003, FR-006): the web edge honours a valid
// inbound traceparent or starts a trace, keeps the trace id in the request context, returns NO trace header (and no
// X-Correlation-ID), writes one RequestCompleted record per request without the query string, and logs startup at info.

const TRACE_ID = "0af7651916cd43dd8448eb211c80319c";
const VALID = `00-${TRACE_ID}-b7ad6b7169203331-01`;
const HEX32 = /^[0-9a-f]{32}$/;

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

function requestCompleted(lines: string[], traceId?: string) {
  return records(lines).filter((r) => r.event === "RequestCompleted" && (traceId === undefined || r.traceId === traceId));
}

async function waitFor(predicate: () => boolean) {
  for (let i = 0; i < 50 && !predicate(); i++) {
    await new Promise((resolve) => setTimeout(resolve, 10));
  }
}

describe("server.ts trace context and request records", () => {
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

  it("returns no trace header and no X-Correlation-ID on any response", async () => {
    captureStreams();
    setRequestListenerForTesting((_req, res) => {
      res.writeHead(200, { "Content-Type": "text/plain" });
      res.end("app route");
    });

    const responses = [
      await fetch(`${baseUrl}/health`, { headers: { traceparent: VALID } }),
      await fetch(`${baseUrl}/favicon.ico`, { headers: { traceparent: VALID } }),
      await fetch(`${baseUrl}/postings`, { headers: { traceparent: VALID } }),
    ];

    for (const response of responses) {
      expect(response.headers.has("x-correlation-id")).toBe(false);
      expect(response.headers.has("traceparent")).toBe(false);
      expect(response.headers.has("tracestate")).toBe(false);
      for (const [, value] of response.headers) {
        expect(value).not.toContain(TRACE_ID);
      }
    }
  });

  it("stores the trace id in the request context: a valid inbound traceparent is honoured, otherwise a trace is started", async () => {
    captureStreams();
    const seen: Array<{ traceId?: string; spanId?: string }> = [];
    setRequestListenerForTesting((_req, res) => {
      const context = getRequestContext();
      seen.push({ traceId: context?.traceId, spanId: context?.spanId });
      res.writeHead(200);
      res.end("ok");
    });

    await fetch(`${baseUrl}/postings`, { headers: { traceparent: VALID } });
    await fetch(`${baseUrl}/postings`);

    expect(seen).toHaveLength(2);
    expect(seen[0].traceId).toBe(TRACE_ID);
    expect(seen[1].traceId).toMatch(HEX32);
    expect(seen[1].traceId).not.toBe(TRACE_ID);
    expect(seen[0].spanId).toMatch(/^[0-9a-f]{16}$/);
  });

  it("ignores an inbound X-Correlation-ID", async () => {
    const lines = captureStreams();
    setRequestListenerForTesting((_req, res) => {
      res.writeHead(200);
      res.end("ok");
    });

    const response = await fetch(`${baseUrl}/postings`, { headers: { "X-Correlation-ID": "legacy-corr-ignored-0001" } });
    await waitFor(() => requestCompleted(lines).length > 0);

    expect(response.headers.has("x-correlation-id")).toBe(false);
    expect(lines.join("\n")).not.toContain("legacy-corr-ignored-0001");
    expect(requestCompleted(lines)[0].traceId).toMatch(HEX32);
  });

  it.skipIf(!fs.existsSync(path.resolve(process.cwd(), "build/client")))(
    "returns no trace header on a static file either",
    async () => {
      captureStreams();
      const clientDir = path.resolve(process.cwd(), "build/client");
      const file = fs.readdirSync(clientDir).find((name) => fs.statSync(path.join(clientDir, name)).isFile());
      if (!file) return;

      const response = await fetch(`${baseUrl}/${file}`);
      expect(response.headers.has("x-correlation-id")).toBe(false);
      expect(response.headers.has("traceparent")).toBe(false);
    }
  );

  it("logs a handler error with the request's trace id", async () => {
    const lines = captureStreams();
    setRequestListenerForTesting(() => {
      throw new Error("listener exploded");
    });

    const response = await fetch(`${baseUrl}/postings`, { headers: { traceparent: VALID } });

    expect(response.status).toBe(500);
    await waitFor(() => requestCompleted(lines, TRACE_ID).length > 0);
    const errors = records(lines).filter((r) => r.level === "error" && r.traceId === TRACE_ID);
    expect(errors.length).toBeGreaterThan(0);
  });

  it("puts a valid inbound trace id in the RequestCompleted record and replaces a bad one without logging it", async () => {
    const lines = captureStreams();

    await fetch(`${baseUrl}/health`, { headers: { traceparent: VALID } });
    process.env.ROLERADAR_LOG_LEVEL = "debug";
    await fetch(`${baseUrl}/health`, { headers: { traceparent: VALID } });
    await waitFor(() => requestCompleted(lines, TRACE_ID).length > 0);
    expect(requestCompleted(lines, TRACE_ID)).toHaveLength(1);

    const bad = `00-${TRACE_ID.toUpperCase()}-b7ad6b7169203331-01`;
    await fetch(`${baseUrl}/health`, { headers: { traceparent: bad } });
    await fetch(`${baseUrl}/health`, { headers: { traceparent: "bad<script>traceparent" } });
    await waitFor(() => requestCompleted(lines).length >= 3);

    const all = requestCompleted(lines);
    expect(all.length).toBeGreaterThanOrEqual(3);
    for (const record of all) {
      expect(record.traceId).toMatch(HEX32);
    }
    expect(lines.join("\n")).not.toContain(bad);
    expect(lines.join("\n")).not.toContain("<script>");
  });

  it("writes exactly one RequestCompleted record per request, without the query string", async () => {
    const lines = captureStreams();
    setRequestListenerForTesting((_req, res) => {
      res.writeHead(302, { Location: "/postings" });
      res.end();
    });

    await fetch(`${baseUrl}/auth/callback?code=abc-SENTINEL&state=xyz-SENTINEL`, { redirect: "manual", headers: { traceparent: VALID } });
    await waitFor(() => requestCompleted(lines, TRACE_ID).length > 0);

    expect(lines.length).toBeGreaterThan(0);
    const matching = requestCompleted(lines, TRACE_ID);
    expect(matching).toHaveLength(1);
    expect(matching[0].props).toMatchObject({ method: "GET", path: "/auth/callback", status: 302 });
    expect(typeof (matching[0].props as Record<string, unknown>).durationMs).toBe("number");
    expect("correlationId" in matching[0]).toBe(false);
    const all = lines.join("\n");
    expect(all).not.toContain("abc-SENTINEL");
    expect(all).not.toContain("xyz-SENTINEL");
  });

  it("logs health requests at debug, so healthchecks do not flood the default level", async () => {
    const lines = captureStreams();

    await fetch(`${baseUrl}/health`, { headers: { traceparent: VALID } });
    await new Promise((resolve) => setTimeout(resolve, 30));

    expect(requestCompleted(lines, TRACE_ID)).toHaveLength(0);
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
