import fs from "node:fs";
import path from "node:path";
import { SpanKind, SpanStatusCode } from "@opentelemetry/api";
import {
  AlwaysOnSampler,
  InMemorySpanExporter,
  SimpleSpanProcessor,
  TracerProvider,
} from "@opentelemetry/sdk-trace";
import { describe, expect, it } from "vitest";
import {
  WebSpanAttributeProcessor,
  createWebResource,
  sanitizeFullUrl,
  stripQueryAndFragment,
} from "../../app/lib/telemetry.server";

// Feature 015 Gate 2 (RR-72, FR-012 to FR-014, SC-002, ADR 0050):
// Telemetry Privacy Gate unit tests for web spans and resource attributes.
// Enforces the closed operational attribute allowlist, explicit sensitive-key drop list,
// URL query/fragment/userinfo stripping, status message clearing, and span exception-event sanitization.

const SENTINEL_SECRETS = [
  "SENTINEL-BEARER-TOKEN-991",
  "SENTINEL-BASIC-CRED-992",
  "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJTRU5USU5FTC1KV1QtOTkzIn0.sig",
  "SENTINEL-MONGO-PW-994",
  "SENTINEL-REFRESH-995",
  "SENTINEL-PW-996",
  "SENTINEL-JSON-SECRET-997",
  "SENTINEL-AUTH-CODE-998",
  "SENTINEL-QUERY-CODE-999",
  "SENTINEL-STATE-999",
  "SENTINEL-EX-PW-1000",
  "SENTINEL-EX-BEARER-1001",
  "SENTINEL-NOTE-BODY-1002",
  "SENTINEL-RULE-TEXT-1003",
  "SENTINEL-POSTING-DESC-1004",
  "SENTINEL-FILTER-LITERAL-1005",
];

function createTestPipeline() {
  const exporter = new InMemorySpanExporter();
  const provider = new TracerProvider({
    sampler: new AlwaysOnSampler(),
    resource: createWebResource("roleradar-web"),
    spanProcessors: [
      new WebSpanAttributeProcessor(),
      new SimpleSpanProcessor({ exporter }),
    ],
  });
  const tracer = provider.getTracer("roleradar-web-test");
  return { exporter, provider, tracer };
}

describe("WebSpanAttributeProcessor (ADR 0050)", () => {
  it("retains allowlisted HTTP attributes and removes url.query, query/fragment/userinfo in url.full, headers, and unlisted tags", async () => {
    const { exporter, provider, tracer } = createTestPipeline();

    const span = tracer.startSpan("GET /auth/callback", { kind: SpanKind.SERVER });
    span.setAttribute("http.request.method", "GET");
    span.setAttribute("http.request.method_original", "GET");
    span.setAttribute("url.scheme", "https");
    span.setAttribute("url.path", "/auth/callback?code=SENTINEL-QUERY-CODE-999#frag");
    span.setAttribute(
      "url.full",
      "https://admin:SENTINEL-MONGO-PW-994@localhost:5003/auth/callback?code=SENTINEL-QUERY-CODE-999&state=SENTINEL-STATE-999#frag"
    );
    span.setAttribute("http.route", "/auth/callback?state=SENTINEL-STATE-999");
    span.setAttribute("http.response.status_code", 302);
    span.setAttribute("server.address", "localhost");
    span.setAttribute("server.port", 5003);
    span.setAttribute("network.protocol.version", "1.1");
    span.setAttribute("network.peer.address", "127.0.0.1");
    span.setAttribute("error.type", "none");

    // Dropped & unlisted attributes containing sentinel secrets
    span.setAttribute("url.query", "?code=SENTINEL-QUERY-CODE-999&state=SENTINEL-STATE-999");
    span.setAttribute("http.target", "/auth/callback?code=SENTINEL-QUERY-CODE-999");
    span.setAttribute("http.url", "https://localhost:5003/auth/callback?code=SENTINEL-QUERY-CODE-999");
    span.setAttribute("http.request.header.authorization", "Bearer SENTINEL-BEARER-TOKEN-991");
    span.setAttribute(
      "http.request.header.cookie",
      "__Host-roleradar.sid=eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJTRU5USU5FTC1KV1QtOTkzIn0.sig"
    );
    span.setAttribute("http.response.header.set_cookie", "refresh_token=SENTINEL-REFRESH-995");
    span.setAttribute("db.query.text", "{ \"find\": \"notes\", \"filter\": \"SENTINEL-NOTE-BODY-1002\" }");
    span.setAttribute("db.statement", "SELECT * WHERE rule = 'SENTINEL-RULE-TEXT-1003'");
    span.setAttribute("password", "SENTINEL-PW-996");
    span.setAttribute("secret", "{\"client_secret\":\"SENTINEL-JSON-SECRET-997\",\"code\":\"SENTINEL-AUTH-CODE-998\"}");
    span.setAttribute("content", "SENTINEL-NOTE-BODY-1002");
    span.setAttribute("ruleText", "SENTINEL-RULE-TEXT-1003");
    span.setAttribute("description", "SENTINEL-POSTING-DESC-1004");
    span.setAttribute("odata.filter", "SENTINEL-FILTER-LITERAL-1005");
    span.setAttribute("custom.unlisted.tag", "Basic SENTINEL-BASIC-CRED-992");

    span.end();
    await provider.forceFlush();

    const spans = exporter.getFinishedSpans();
    expect(spans).toHaveLength(1);
    const attrs = spans[0].attributes;

    expect(attrs["http.request.method"]).toBe("GET");
    expect(attrs["http.request.method_original"]).toBe("GET");
    expect(attrs["url.scheme"]).toBe("https");
    expect(attrs["url.path"]).toBe("/auth/callback");
    expect(attrs["url.full"]).toBe("https://localhost:5003/auth/callback");
    expect(attrs["http.route"]).toBe("/auth/callback");
    expect(attrs["http.response.status_code"]).toBe(302);
    expect(attrs["server.address"]).toBe("localhost");
    expect(attrs["server.port"]).toBe(5003);
    expect(attrs["network.protocol.version"]).toBe("1.1");
    expect(attrs["network.peer.address"]).toBe("127.0.0.1");

    expect(attrs["url.query"]).toBeUndefined();
    expect(attrs["http.target"]).toBeUndefined();
    expect(attrs["http.url"]).toBeUndefined();
    expect(attrs["db.query.text"]).toBeUndefined();
    expect(attrs["db.statement"]).toBeUndefined();
    expect(attrs["password"]).toBeUndefined();
    expect(attrs["secret"]).toBeUndefined();
    expect(attrs["content"]).toBeUndefined();
    expect(attrs["ruleText"]).toBeUndefined();
    expect(attrs["description"]).toBeUndefined();
    expect(attrs["odata.filter"]).toBeUndefined();
    expect(attrs["custom.unlisted.tag"]).toBeUndefined();

    const attrKeys = Object.keys(attrs);
    expect(attrKeys.some((k) => k.toLowerCase().startsWith("http.request.header"))).toBe(false);
    expect(attrKeys.some((k) => k.toLowerCase().startsWith("http.response.header"))).toBe(false);

    const serialized = JSON.stringify(attrs);
    for (const sentinel of SENTINEL_SECRETS) {
      expect(serialized).not.toContain(sentinel);
    }

    await provider.shutdown();
  });

  it("strips exception.message, exception.stacktrace, and status.message while retaining exception.type and error.type", async () => {
    const { exporter, provider, tracer } = createTestPipeline();

    const span = tracer.startSpan("POST /connect/token", { kind: SpanKind.CLIENT });
    const err = new TypeError("Token exchange failed: password=SENTINEL-EX-PW-1000 Bearer SENTINEL-EX-BEARER-1001");
    span.recordException(err);
    span.setStatus({
      code: SpanStatusCode.ERROR,
      message: err.message,
    });
    span.end();
    await provider.forceFlush();

    const spans = exporter.getFinishedSpans();
    expect(spans).toHaveLength(1);
    const exported = spans[0];

    expect(exported.status.code).toBe(SpanStatusCode.ERROR);
    expect(exported.status.message).toBeUndefined();
    expect(exported.attributes["error.type"]).toBe("TypeError");

    expect(exported.events).toHaveLength(1);
    const exEvent = exported.events[0];
    expect(exEvent.name).toBe("exception");
    expect(exEvent.attributes?.["exception.type"]).toBe("TypeError");
    expect(exEvent.attributes?.["exception.message"]).toBeUndefined();
    expect(exEvent.attributes?.["exception.stacktrace"]).toBeUndefined();

    const serializedEvent = JSON.stringify(exEvent);
    expect(serializedEvent).not.toContain("SENTINEL-EX-PW-1000");
    expect(serializedEvent).not.toContain("SENTINEL-EX-BEARER-1001");

    await provider.shutdown();
  });

  it("creates a resource with only service.name, service.namespace, and service.version (no host/os/process/owner attributes)", () => {
    const resource = createWebResource("roleradar-web");
    const attrs = resource.attributes;

    expect(attrs["service.name"]).toBe("roleradar-web");
    expect(attrs["service.namespace"]).toBe("roleradar");
    expect(typeof attrs["service.version"]).toBe("string");
    expect(String(attrs["service.version"]).length).toBeGreaterThan(0);

    const keys = Object.keys(attrs);
    expect(keys.some((k) => k.toLowerCase().includes("owner"))).toBe(false);
    expect(keys.some((k) => k.toLowerCase().startsWith("process."))).toBe(false);
    expect(keys.some((k) => k.toLowerCase().startsWith("host."))).toBe(false);
    expect(keys.some((k) => k.toLowerCase().startsWith("os."))).toBe(false);
  });

  it("sanitizes full URLs and paths deterministically", () => {
    expect(
      sanitizeFullUrl(
        "https://user:SENTINEL-MONGO-PW-994@api.local:5002/odata/Postings?$filter=contains(tolower(title),'secret')#top"
      )
    ).toBe("https://api.local:5002/odata/Postings");
    expect(stripQueryAndFragment("/odata/Companies?$orderby=name#section")).toBe("/odata/Companies");
  });

  it("has zero dependency on log scrubbing modules (independent of RR-86 in Gate 3)", () => {
    const sourcePath = path.resolve(process.cwd(), "app/lib/telemetry.server.ts");
    const sourceText = fs.readFileSync(sourcePath, "utf8");
    const nonCommentLines = sourceText
      .split("\n")
      .filter((line) => !line.trimStart().startsWith("//") && !line.trimStart().startsWith("*"))
      .join("\n");

    expect(nonCommentLines).not.toContain("LogScrubber");
    expect(nonCommentLines).not.toContain("scrub");
  });
});
