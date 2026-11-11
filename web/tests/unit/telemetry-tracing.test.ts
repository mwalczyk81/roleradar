import http from "node:http";
import { InMemorySpanExporter } from "@opentelemetry/sdk-trace";
import { afterEach, describe, expect, it, vi } from "vitest";
import { apiFetch } from "../../app/lib/api-client.server";
import { runWithRequestContext } from "../../app/lib/request-context.server";
import {
  initializeWebTelemetry,
  isWebOtlpEndpointConfigured,
  resolveOtlpTracesUrl,
  resolveWebServiceName,
  shutdownWebTelemetry,
} from "../../app/lib/telemetry.server";
import {
  buildTraceparent,
  startTrace,
  withTraceparent,
} from "../../app/lib/trace-context.server";
import { createRequestHandler, setRequestListenerForTesting } from "../../server";

// Feature 015 Gate 2 (RR-68, FR-001, FR-002, FR-003, SC-001, SC-003):
// Web distributed tracing, single propagator mechanism, and default-off exporter tests.

const VALID_TRACE_ID = "0af7651916cd43dd8448eb211c80319c";
const VALID_PARENT_SPAN_ID = "b7ad6b7169203331";
const VALID_TRACEPARENT = `00-${VALID_TRACE_ID}-${VALID_PARENT_SPAN_ID}-01`;

afterEach(async () => {
  setRequestListenerForTesting(null);
  vi.restoreAllMocks();
  await shutdownWebTelemetry();
});

describe("telemetry.server configuration & SC-003 no-endpoint default", () => {
  it("registers zero OTLP exporters when OTEL_EXPORTER_OTLP_ENDPOINT is unset, empty, or whitespace", async () => {
    for (const val of [undefined, "", "   "]) {
      expect(isWebOtlpEndpointConfigured({ OTEL_EXPORTER_OTLP_ENDPOINT: val })).toBe(false);
      const state = initializeWebTelemetry({
        env: { OTEL_EXPORTER_OTLP_ENDPOINT: val },
        forceReinit: true,
      });
      expect(state.otlpExporterRegistered).toBe(false);
      await shutdownWebTelemetry();
    }
  });

  it("registers OTLP exporter when OTEL_EXPORTER_OTLP_ENDPOINT is set and resolves /v1/traces URL", async () => {
    expect(isWebOtlpEndpointConfigured({ OTEL_EXPORTER_OTLP_ENDPOINT: "http://localhost:4318" })).toBe(true);
    expect(resolveOtlpTracesUrl("http://localhost:4318")).toBe("http://localhost:4318/v1/traces");
    expect(resolveOtlpTracesUrl("http://localhost:4318/")).toBe("http://localhost:4318/v1/traces");
    expect(resolveOtlpTracesUrl("http://localhost:4318/v1/traces")).toBe("http://localhost:4318/v1/traces");

    const state = initializeWebTelemetry({
      env: { OTEL_EXPORTER_OTLP_ENDPOINT: "http://127.0.0.1:4318" },
      forceReinit: true,
    });
    expect(state.otlpExporterRegistered).toBe(true);
  });

  it("resolves canonical service name roleradar-web unless OTEL_SERVICE_NAME overrides it", () => {
    expect(resolveWebServiceName({})).toBe("roleradar-web");
    expect(resolveWebServiceName({ OTEL_SERVICE_NAME: "   " })).toBe("roleradar-web");
    expect(resolveWebServiceName({ OTEL_SERVICE_NAME: "custom-web" })).toBe("custom-web");
  });
});

describe("Single W3C propagator and SDK span integration across server.ts and api-client.server.ts", () => {
  it("preserves all Feature 014 strict inbound traceparent validation rules while backed by the OTel SDK tracer", () => {
    const exporter = new InMemorySpanExporter();
    initializeWebTelemetry({
      env: {},
      testExporter: exporter,
      forceReinit: true,
    });

    const accepted = startTrace(VALID_TRACEPARENT);
    expect(accepted.traceId).toBe(VALID_TRACE_ID);
    expect(accepted.spanId).toMatch(/^[0-9a-f]{16}$/);
    expect(accepted.spanId).not.toBe(VALID_PARENT_SPAN_ID);

    const rejectedCases = [
      "not-a-traceparent",
      `00-${"0".repeat(32)}-${VALID_PARENT_SPAN_ID}-01`,
      `00-${VALID_TRACE_ID}-${"0".repeat(16)}-01`,
      `00-${VALID_TRACE_ID.toUpperCase()}-${VALID_PARENT_SPAN_ID}-01`,
      `ff-${VALID_TRACE_ID}-${VALID_PARENT_SPAN_ID}-01`,
      `${VALID_TRACEPARENT}-extra`,
    ];

    for (const bad of rejectedCases) {
      const generated = startTrace(bad);
      expect(generated.traceId).toMatch(/^[0-9a-f]{32}$/);
      expect(generated.traceId).not.toBe(VALID_TRACE_ID);
    }

    const headers = new Headers();
    runWithRequestContext({ traceId: accepted.traceId, spanId: accepted.spanId, nonce: "" }, () => {
      withTraceparent(headers);
    });
    expect(headers.get("traceparent")).toBe(buildTraceparent(accepted));
    expect(headers.has("tracestate")).toBe(false);
  });

  it("emits a server span in server.ts and a child client span in apiFetch sharing one 32-hex traceId", async () => {
    const exporter = new InMemorySpanExporter();
    const { provider } = initializeWebTelemetry({
      env: {},
      testExporter: exporter,
      forceReinit: true,
    });

    let outboundTraceparent: string | null = null;
    vi.spyOn(globalThis, "fetch").mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      const req = input instanceof Request ? input : new Request(input, init);
      outboundTraceparent = req.headers.get("traceparent");
      return new Response(JSON.stringify({ value: [] }), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      });
    });

    setRequestListenerForTesting(async (_req, res) => {
      await apiFetch(
        new Request("https://api.local:5002/odata/Postings?$filter=contains(tolower(title),'secret')", {
          method: "GET",
          headers: { Authorization: "Bearer SENTINEL-BEARER-991" },
        })
      );
      res.writeHead(200, { "Content-Type": "text/html" });
      res.end("ok");
    });

    const server = http.createServer(createRequestHandler());
    await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
    const address = server.address();
    if (!address || typeof address === "string") {
      throw new Error("Expected TCP address");
    }

    try {
      await new Promise<void>((resolve, reject) => {
        const req = http.request(
          {
            hostname: "127.0.0.1",
            port: address.port,
            path: "/postings?secret=SENTINEL-QUERY-CODE-999",
            method: "GET",
            headers: {
              traceparent: VALID_TRACEPARENT,
              cookie: "__Host-roleradar.sid=SENTINEL-COOKIE-999",
            },
          },
          (res) => {
            res.resume();
            res.on("end", () => resolve());
          }
        );
        req.on("error", reject);
        req.end();
      });

      await provider.forceFlush();
      const spans = exporter.getFinishedSpans();

      // Both server and client spans must be exported under the same traceId
      const traceSpans = spans.filter((s) => s.spanContext().traceId === VALID_TRACE_ID);
      expect(traceSpans.length).toBeGreaterThanOrEqual(2);

      const serverSpan = traceSpans.find((s) => s.attributes["url.path"] === "/postings");
      const clientSpan = traceSpans.find((s) => s.attributes["url.path"] === "/odata/Postings");

      expect(serverSpan).toBeDefined();
      expect(clientSpan).toBeDefined();
      expect(serverSpan?.parentSpanContext?.spanId).toBe(VALID_PARENT_SPAN_ID);
      expect(clientSpan?.parentSpanContext?.spanId).toBe(serverSpan?.spanContext().spanId);

      // Outbound header propagated the same traceId
      expect(outboundTraceparent).toMatch(new RegExp(`^00-${VALID_TRACE_ID}-[0-9a-f]{16}-01$`));

      // Privacy processor stripped query strings and headers on both spans
      const serialized = JSON.stringify(traceSpans.map((s) => ({ attributes: s.attributes, events: s.events })));
      expect(serialized).not.toContain("SENTINEL-QUERY-CODE-999");
      expect(serialized).not.toContain("SENTINEL-COOKIE-999");
      expect(serialized).not.toContain("SENTINEL-BEARER-991");
      expect(serialized).not.toContain("secret");
    } finally {
      await new Promise<void>((resolve) => server.close(() => resolve()));
    }
  });
});
