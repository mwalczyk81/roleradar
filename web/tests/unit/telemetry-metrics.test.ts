import { describe, expect, it, beforeEach, afterEach } from "vitest";
import { MetricReader } from "@opentelemetry/sdk-metrics";
import {
  initializeWebTelemetry,
  shutdownWebTelemetry,
  resolveHttpRoute,
  normalizeHttpRoute,
  setConfiguredRoutesForTelemetry,
  recordServerRequestDuration,
  trackServerActiveRequests,
  recordClientRequestDuration,
  type RouteDefinition,
} from "../../app/lib/telemetry.server";
import appRoutes from "../../app/routes";

const TEST_BUILD_ROUTES: RouteDefinition[] = [
  { id: "root", path: "" },
  { id: "routes/_index", index: true, parentId: "root" },
  { id: "routes/auth.callback", path: "auth/callback", parentId: "root" },
  { id: "routes/auth.logout", path: "auth/logout", parentId: "root" },
  { id: "routes/not-authorized", path: "not-authorized", parentId: "root" },
  { id: "routes/service-unavailable", path: "service-unavailable", parentId: "root" },
  { id: "routes/postings._index", path: "postings", parentId: "root" },
  { id: "routes/postings.$id", path: "postings/:id", parentId: "root" },
  { id: "routes/companies._index", path: "companies", parentId: "root" },
  { id: "routes/companies.$id", path: "companies/:id", parentId: "root" },
  { id: "routes/applications._index", path: "applications", parentId: "root" },
  { id: "routes/applications.$id", path: "applications/:id", parentId: "root" },
  { id: "routes/criteria", path: "criteria", parentId: "root" },
  { id: "routes/notes._index", path: "notes", parentId: "root" },
  { id: "routes/notes.$id", path: "notes/:id", parentId: "root" },
  { id: "routes/search-runs", path: "search-runs", parentId: "root" },
  { id: "routes/insights", path: "insights", parentId: "root" },
];

class InProcessTestMetricReader extends MetricReader {
  protected async onCollect(): Promise<void> {}
  protected async onShutdown(): Promise<void> {}
  protected async onForceFlush(): Promise<void> {}
}

// Feature 015 Gate 4 (RR-69, RR-72, SC-003, SC-005, ADR 0052):
// Unit tests for Web OpenTelemetry metrics and route template normalization.

describe("Web Telemetry Metrics (Gate 4)", () => {
  beforeEach(async () => {
    await shutdownWebTelemetry();
  });

  afterEach(async () => {
    await shutdownWebTelemetry();
  });

  it("registers zero OTLP metric exporters when OTEL_EXPORTER_OTLP_ENDPOINT is unset (SC-003)", () => {
    const state = initializeWebTelemetry({
      env: {},
    });

    expect(state.otlpMetricExporterRegistered).toBe(false);
    expect(state.meterProvider).toBeDefined();
  });

  it("registers MeterProvider with OTLP metric exporter when OTEL_EXPORTER_OTLP_ENDPOINT is set", () => {
    const state = initializeWebTelemetry({
      env: {
        OTEL_EXPORTER_OTLP_ENDPOINT: "http://127.0.0.1:4318",
      },
    });

    expect(state.otlpMetricExporterRegistered).toBe(true);
    expect(state.meterProvider).toBeDefined();
  });

  it("returns unmatched when no routes are configured (DEFAULT_WEB_ROUTES removed)", () => {
    setConfiguredRoutesForTelemetry(null);
    expect(resolveHttpRoute("/postings/12345")).toBe("unmatched");
  });

  it("resolves route from React Router matched route pattern and bounds numeric id, slug, 404 with random segments, query string, and trailing slash", () => {
    setConfiguredRoutesForTelemetry(TEST_BUILD_ROUTES);
    expect(resolveHttpRoute("/postings/12345")).toBe("/postings/:id");
    expect(resolveHttpRoute("/companies/acme-corp-slug")).toBe("/companies/:id");
    expect(resolveHttpRoute("/applications/numeric-42/")).toBe("/applications/:id");
    expect(resolveHttpRoute("/postings/slug-abc?filter=active&sort=desc")).toBe("/postings/:id");
    expect(resolveHttpRoute("/random/nonexistent/404/path")).toBe("unmatched");
    expect(resolveHttpRoute("/unknown-slug-404")).toBe("unmatched");
    expect(resolveHttpRoute("/health")).toBe("/health");
    expect(resolveHttpRoute("/")).toBe("/");

    expect(normalizeHttpRoute("/postings/12345")).toBe("/postings/:id");
    expect(normalizeHttpRoute("/companies/acme-corp-slug")).toBe("/companies/:id");
    expect(normalizeHttpRoute("/applications/numeric-42/")).toBe("/applications/:id");
    expect(normalizeHttpRoute("/postings/slug-abc?filter=active&sort=desc")).toBe("/postings/:id");
    expect(normalizeHttpRoute("/random/nonexistent/404/path")).toBe("unmatched");
    expect(normalizeHttpRoute("/unknown-slug-404")).toBe("unmatched");
  });

  it("resolves every route in web/app/routes.ts to its own pattern", () => {
    setConfiguredRoutesForTelemetry(TEST_BUILD_ROUTES);
    for (const r of appRoutes) {
      if (r.index) {
        expect(resolveHttpRoute("/")).toBe("/");
      } else if (r.path) {
        const expectedPattern = r.path.startsWith("/") ? r.path : `/${r.path}`;
        const sampleUrl = expectedPattern.replace(/:([a-zA-Z0-9_]+)/g, "sample-$1");
        expect(resolveHttpRoute(sampleUrl)).toBe(expectedPattern);
      }
    }
  });

  it("filters forbidden labels on all three HTTP instruments via real MeterProvider Views", async () => {
    const reader = new InProcessTestMetricReader();
    initializeWebTelemetry({
      testMetricReader: reader,
    });

    const forbiddenTags = {
      owner_id: "0194fa10-1234-7000-8000-000000000001",
      guid: "0194fa10-1234-7000-8000-000000000099",
      posting_id: "0194fa10-1234-7000-8000-000000000002",
      company_id: "0194fa10-1234-7000-8000-000000000003",
      application_id: "0194fa10-1234-7000-8000-000000000004",
      note_id: "0194fa10-1234-7000-8000-000000000005",
      traceId: "4bf92f3577b34da6a3ce929d0e0e4736",
      sub: "auth0|123456",
      username: "matt.walsh",
      "url.full": "https://example.com/api?secret=pw",
      "url.query": "?secret=pw",
    };

    recordServerRequestDuration(0.125, {
      method: "GET",
      route: "/postings/:id",
      statusCode: 200,
      scheme: "http",
      ...forbiddenTags,
    });

    trackServerActiveRequests("GET", "http", 1, forbiddenTags);

    recordClientRequestDuration(0.05, {
      method: "POST",
      statusCode: 201,
      address: "api.internal",
      port: 5002,
      scheme: "http",
      ...forbiddenTags,
    });

    const collection = await reader.collect();
    expect(collection).toBeDefined();

    const allowlists: Record<string, Set<string>> = {
      "http.server.request.duration": new Set([
        "http.request.method",
        "http.route",
        "http.response.status_code",
        "error.type",
        "url.scheme",
      ]),
      "http.server.active_requests": new Set([
        "http.request.method",
        "url.scheme",
      ]),
      "http.client.request.duration": new Set([
        "http.request.method",
        "http.response.status_code",
        "server.address",
        "server.port",
        "error.type",
        "url.scheme",
      ]),
    };

    let foundServerDuration = false;
    let foundActiveRequests = false;
    let foundClientDuration = false;

    for (const scopeMetric of collection.resourceMetrics.scopeMetrics) {
      for (const metric of scopeMetric.metrics) {
        const allowed = allowlists[metric.descriptor.name];
        if (allowed) {
          if (metric.descriptor.name === "http.server.request.duration") foundServerDuration = true;
          if (metric.descriptor.name === "http.server.active_requests") foundActiveRequests = true;
          if (metric.descriptor.name === "http.client.request.duration") foundClientDuration = true;

          for (const dataPoint of metric.dataPoints) {
            const keys = Object.keys(dataPoint.attributes || {});
            for (const key of keys) {
              expect(allowed.has(key), `Key "${key}" not in allowlist for ${metric.descriptor.name}`).toBe(true);
            }
          }
        }
      }
    }

    expect(foundServerDuration).toBe(true);
    expect(foundActiveRequests).toBe(true);
    expect(foundClientDuration).toBe(true);
  });

  it("exports Node runtime metrics including memory and event loop utilization", async () => {
    const reader = new InProcessTestMetricReader();
    initializeWebTelemetry({
      testMetricReader: reader,
    });

    const collection = await reader.collect();
    expect(collection).toBeDefined();

    const metricNames = new Set(
      collection.resourceMetrics.scopeMetrics.flatMap((s) =>
        s.metrics.map((m) => m.descriptor.name)
      )
    );

    expect(metricNames.has("process.runtime.nodejs.memory.heap_used")).toBe(true);
    expect(metricNames.has("process.runtime.nodejs.memory.rss")).toBe(true);
    expect(metricNames.has("nodejs.eventloop.utilization")).toBe(true);
  });
});
