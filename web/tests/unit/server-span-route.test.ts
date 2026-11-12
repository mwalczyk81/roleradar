import { EventEmitter } from "node:events";
import type http from "node:http";
import { MetricReader } from "@opentelemetry/sdk-metrics";
import { InMemorySpanExporter } from "@opentelemetry/sdk-trace";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import {
  initializeWebTelemetry,
  resolveHttpRoute,
  setConfiguredRoutesForTelemetry,
  shutdownWebTelemetry,
  type RouteDefinition,
} from "../../app/lib/telemetry.server";
import { createRequestHandler, setRequestListenerForTesting } from "../../server";

// Feature 015 Gate 5 fix (RR-71, OpenTelemetry HTTP semantic conventions): a server span is named "{method} {http.route}"
// (a low-cardinality template, never the raw path), carries http.route, and reports the real url.scheme. React Router
// single-fetch requests end in ".data" and must resolve to the real route template, not to "unmatched".

const COMPANY_ID = "76d9aabb-961e-44c2-9da2-285d058cef20";

const ROUTES: RouteDefinition[] = [
  { id: "root", path: "" },
  { id: "routes/_index", index: true, parentId: "root" },
  { id: "routes/postings._index", path: "postings", parentId: "root" },
  { id: "routes/companies._index", path: "companies", parentId: "root" },
  { id: "routes/companies.$id", path: "companies/:id", parentId: "root" },
];

class InProcessTestMetricReader extends MetricReader {
  protected async onCollect(): Promise<void> {}
  protected async onShutdown(): Promise<void> {}
  protected async onForceFlush(): Promise<void> {}
}

describe("resolveHttpRoute with React Router single-fetch .data suffixes", () => {
  beforeEach(() => setConfiguredRoutesForTelemetry(ROUTES));
  afterEach(() => setConfiguredRoutesForTelemetry(null));

  it("resolves /companies/<uuid>.data to the real template", () => {
    expect(resolveHttpRoute(`/companies/${COMPANY_ID}.data`)).toBe("/companies/:id");
  });

  it("resolves a static /postings.data to /postings, and /_root.data to /", () => {
    expect(resolveHttpRoute("/postings.data")).toBe("/postings");
    expect(resolveHttpRoute("/_root.data")).toBe("/");
  });

  it("resolves the trailing-slash form /companies/_.data and keeps a query out of the match", () => {
    expect(resolveHttpRoute("/companies/_.data")).toBe("/companies");
    expect(resolveHttpRoute(`/companies/${COMPANY_ID}.data?_routes=routes%2Fcompanies.%24id`)).toBe("/companies/:id");
  });

  it("still returns unmatched for a .data path that matches no route, and for a plain unknown path", () => {
    expect(resolveHttpRoute("/nothing-here.data")).toBe("unmatched");
    expect(resolveHttpRoute("/random/nonexistent/404/path")).toBe("unmatched");
  });

  it("does not change what a path without the suffix resolves to", () => {
    expect(resolveHttpRoute(`/companies/${COMPANY_ID}`)).toBe("/companies/:id");
    expect(resolveHttpRoute("/postings")).toBe("/postings");
    expect(resolveHttpRoute("/")).toBe("/");
  });
});

interface FakeSocket {
  encrypted?: boolean;
}

async function handle(url: string, socket: FakeSocket) {
  const exporter = new InMemorySpanExporter();
  const reader = new InProcessTestMetricReader();
  const { provider } = initializeWebTelemetry({ env: {}, testExporter: exporter, testMetricReader: reader, forceReinit: true });

  setRequestListenerForTesting((_req, res) => {
    res.writeHead(200, { "Content-Type": "text/plain" });
    res.end("ok");
  });

  const req = Object.assign(new EventEmitter(), { url, method: "GET", headers: {}, socket }) as unknown as http.IncomingMessage;
  const emitter = new EventEmitter();
  const res = Object.assign(emitter, {
    statusCode: 200,
    headersSent: false,
    setHeader: () => undefined,
    writeHead(code: number) {
      this.statusCode = code;
      return this;
    },
    end() {
      emitter.emit("finish");
    },
  }) as unknown as http.ServerResponse;

  await createRequestHandler()(req, res);
  await provider.forceFlush();
  const serverSpan = exporter.getFinishedSpans().find((s) => s.kind === 1);
  const collection = await reader.collect();
  const metric = collection?.resourceMetrics.scopeMetrics
    .flatMap((s) => s.metrics)
    .find((m) => m.descriptor.name === "http.server.request.duration");
  const routeLabels = (metric?.dataPoints ?? []).map((d) => String(d.attributes["http.route"]));
  return { serverSpan, routeLabels };
}

describe("web server span (createRequestHandler)", () => {
  beforeEach(() => setConfiguredRoutesForTelemetry(ROUTES));
  afterEach(async () => {
    setRequestListenerForTesting(null);
    setConfiguredRoutesForTelemetry(null);
    await shutdownWebTelemetry();
  });

  it("reports url.scheme http over a plain socket and https over an encrypted one", async () => {
    expect((await handle("/postings", {})).serverSpan?.attributes["url.scheme"]).toBe("http");
    expect((await handle("/postings", { encrypted: true })).serverSpan?.attributes["url.scheme"]).toBe("https");
  });

  it("names the span '{method} {http.route}' and sets http.route for a .data request, with no id and no .data anywhere", async () => {
    const { serverSpan, routeLabels } = await handle(`/companies/${COMPANY_ID}.data`, {});

    expect(serverSpan?.name).toBe("GET /companies/:id");
    expect(serverSpan?.attributes["http.route"]).toBe("/companies/:id");
    // url.path keeps the raw path (the query is stripped), as before.
    expect(serverSpan?.attributes["url.path"]).toBe(`/companies/${COMPANY_ID}.data`);

    for (const lowCardinality of [serverSpan?.name, String(serverSpan?.attributes["http.route"]), ...routeLabels]) {
      expect(lowCardinality).not.toContain(COMPANY_ID);
      expect(lowCardinality).not.toContain(".data");
    }
    expect(routeLabels).toContain("/companies/:id");
  });

  it("resolves /postings.data to the /postings template in the span name and the metric label", async () => {
    const { serverSpan, routeLabels } = await handle("/postings.data", {});
    expect(serverSpan?.name).toBe("GET /postings");
    expect(serverSpan?.attributes["http.route"]).toBe("/postings");
    expect(routeLabels).toContain("/postings");
  });

  it("collapses every unmatched path to one span name, one http.route and one metric label", async () => {
    const a = await handle("/one/unknown/path-1", {});
    const b = await handle("/two/other-unknown.data?x=1", {});

    expect(a.serverSpan?.name).toBe("GET unmatched");
    expect(b.serverSpan?.name).toBe("GET unmatched");
    expect(a.serverSpan?.attributes["http.route"]).toBe("unmatched");
    expect(a.routeLabels).toEqual(["unmatched"]);
    expect(b.routeLabels).toEqual(["unmatched"]);
  });
});
