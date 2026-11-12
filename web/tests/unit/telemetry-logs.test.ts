import { describe, expect, it } from "vitest";
import { log } from "../../app/lib/log.server";
import {
  getTestExportedLogs,
  initializeWebTelemetry,
  resetWebTelemetryForTests,
  type WebLogRecord,
} from "../../app/lib/telemetry.server";

// Feature 015 Gate 3 (RR-70, RR-86, RR-72, FR-004, FR-005, SC-001):
// Unit tests for Web OpenTelemetry log emission, trace correlation, and attribute privacy scrubbing.

describe("Web OpenTelemetry Logs (Gate 3)", () => {
  it("registers zero OTLP log exporters when OTEL_EXPORTER_OTLP_ENDPOINT is unset or empty", () => {
    resetWebTelemetryForTests();
    const state = initializeWebTelemetry({
      env: {},
      forceReinit: true,
    });

    expect(state.otlpLogExporterRegistered).toBe(false);
  });

  it("attaches traceId and spanId from the active span to emitted log records", async () => {
    resetWebTelemetryForTests();
    const state = initializeWebTelemetry({
      env: { OTEL_EXPORTER_OTLP_ENDPOINT: "http://127.0.0.1:4318" },
      forceReinit: true,
      enableLogCaptureForTests: true,
    });

    const tracer = state.provider.getTracer("test-logger");

    await tracer.startActiveSpan("test-http-request", async (span) => {
      const spanContext = span.spanContext();

      log.info("Processing web request", { event: "request.received" });

      const logs: WebLogRecord[] = getTestExportedLogs();
      expect(logs.length).toBeGreaterThan(0);

      const lastLog = logs[logs.length - 1];
      expect(lastLog.traceId).toBe(spanContext.traceId);
      expect(lastLog.spanId).toBe(spanContext.spanId);
      expect(lastLog.body).toContain("Processing web request");

      span.end();
    });
  });

  it("scrubs password, token, authorization, and cookie from log attributes and bodies", async () => {
    resetWebTelemetryForTests();
    initializeWebTelemetry({
      env: { OTEL_EXPORTER_OTLP_ENDPOINT: "http://127.0.0.1:4318" },
      forceReinit: true,
      enableLogCaptureForTests: true,
    });

    log.info("Sensitive operation with password=SENTINEL-EX-PW-1000", { // gitleaks:allow (fictional test vector, not a credential)
      props: {
        password: "SENTINEL-PW-996", // gitleaks:allow (fictional test vector, not a credential)
        token: "SENTINEL-BEARER-TOKEN-991", // gitleaks:allow (fictional test vector, not a credential)
        authorization: "Bearer SENTINEL-BASIC-CRED-992", // gitleaks:allow (fictional test vector, not a credential)
        cookie: "session=SENTINEL-REFRESH-995", // gitleaks:allow (fictional test vector, not a credential)
        safeProp: "safe-value",
      },
    });

    const logs: WebLogRecord[] = getTestExportedLogs();
    expect(logs.length).toBeGreaterThan(0);

    const record = logs[logs.length - 1];
    expect(record.body).not.toContain("SENTINEL-EX-PW-1000"); // gitleaks:allow (fictional test vector, not a credential)

    const serializedAttrs = JSON.stringify(record.attributes ?? {});
    expect(serializedAttrs).not.toContain("SENTINEL-PW-996"); // gitleaks:allow (fictional test vector, not a credential)
    expect(serializedAttrs).not.toContain("SENTINEL-BEARER-TOKEN-991"); // gitleaks:allow (fictional test vector, not a credential)
    expect(serializedAttrs).not.toContain("SENTINEL-BASIC-CRED-992"); // gitleaks:allow (fictional test vector, not a credential)
    expect(serializedAttrs).not.toContain("SENTINEL-REFRESH-995"); // gitleaks:allow (fictional test vector, not a credential)

    expect(record.attributes?.["password"]).toBe("[redacted]");
    expect(record.attributes?.["token"]).toBe("[redacted]");
    expect(record.attributes?.["authorization"]).toBe("[redacted]");
    expect(record.attributes?.["cookie"]).toBe("[redacted]");
    expect(record.attributes?.["safeProp"]).toBe("safe-value");
  });
  it("scrubs an object-valued attribute the way the .NET hosts do (a PKCE code_verifier never reaches the exporter)", async () => {
    resetWebTelemetryForTests();
    initializeWebTelemetry({
      env: { OTEL_EXPORTER_OTLP_ENDPOINT: "http://127.0.0.1:4318" },
      forceReinit: true,
      enableLogCaptureForTests: true,
    });

    const sentinelVerifier = "SENTINEL-VERIFIER-7b3e91d04f6a2c85e1d09a3b-0123456789"; // gitleaks:allow (fictional test vector, not a credential)
    // An object whose own text carries the verifier, the shape Duende's validation log has, and a plain nested object.
    const detailsLikeDuende = {
      toString: () =>
        `{ "GrantType": "authorization_code", "Raw": { "redirect_uri": "https://localhost:5003/auth/callback", "code_verifier": "${sentinelVerifier}" } }`,
    };

    log.info("Token request validation success", {
      props: {
        details: detailsLikeDuende,
        nested: { code_verifier: sentinelVerifier },
        list: [`"code_verifier": "${sentinelVerifier}"`],
      },
    });

    const record = getTestExportedLogs().at(-1);
    const serialized = JSON.stringify({ body: record?.body, attributes: record?.attributes ?? {} });
    expect(serialized).not.toContain(sentinelVerifier);
    expect(serialized).toContain("authorization_code");
    expect(serialized).toContain("[redacted]");
  });
});
