import { afterEach, describe, expect, it, vi } from "vitest";
import { buildTraceparent, newSpanId, newTraceId, startTrace, withTraceparent } from "../../app/lib/trace-context.server";
import { runWithRequestContext } from "../../app/lib/request-context.server";

// Feature 014 US2 (FR-023 to FR-026, contracts/trace-context.md): the web edge honours a valid inbound traceparent and
// otherwise starts a trace of its own; the outbound header is the W3C form; tracestate is never forwarded; a bad
// inbound value is discarded and never logged.

const TRACE_ID = "0af7651916cd43dd8448eb211c80319c";
const PARENT_SPAN = "b7ad6b7169203331";
const VALID = `00-${TRACE_ID}-${PARENT_SPAN}-01`;
const OUTBOUND = /^00-[0-9a-f]{32}-[0-9a-f]{16}-0[01]$/;

afterEach(() => {
  vi.restoreAllMocks();
});

describe("startTrace", () => {
  it("accepts a valid inbound traceparent: its trace id becomes the request's", () => {
    const trace = startTrace(VALID);

    expect(trace.traceId).toBe(TRACE_ID);
    expect(trace.spanId).toMatch(/^[0-9a-f]{16}$/);
    expect(trace.spanId).not.toBe(PARENT_SPAN);
  });

  it.each([
    ["malformed", "not-a-traceparent"],
    ["all-zero trace id", `00-${"0".repeat(32)}-${PARENT_SPAN}-01`],
    ["all-zero span id", `00-${TRACE_ID}-${"0".repeat(16)}-01`],
    ["upper case", `00-${TRACE_ID.toUpperCase()}-${PARENT_SPAN}-01`],
    ["version ff", `ff-${TRACE_ID}-${PARENT_SPAN}-01`],
    ["extra trailing fields", `${VALID}-extra-field`],
    ["oversized (10,000 characters)", `00-${TRACE_ID}-${PARENT_SPAN}-01${"-x".repeat(5000)}`],
    ["empty", ""],
  ])("discards a %s value and generates a new trace", (_name, bad) => {
    const trace = startTrace(bad);

    expect(trace.traceId).toMatch(/^[0-9a-f]{32}$/);
    expect(trace.traceId).not.toBe(TRACE_ID);
    expect(trace.traceId).not.toBe("0".repeat(32));
    expect(trace.spanId).toMatch(/^[0-9a-f]{16}$/);
  });

  it("generates a new trace when there is no header", () => {
    const first = startTrace(undefined);
    const second = startTrace(null);

    expect(first.traceId).toMatch(/^[0-9a-f]{32}$/);
    expect(first.traceId).not.toBe(second.traceId);
  });

  it("never logs a discarded value", () => {
    const written: string[] = [];
    vi.spyOn(process.stdout, "write").mockImplementation((chunk: string | Uint8Array) => (written.push(String(chunk)), true));
    vi.spyOn(process.stderr, "write").mockImplementation((chunk: string | Uint8Array) => (written.push(String(chunk)), true));

    startTrace("bad<script>traceparent");
    startTrace(`00-${"x".repeat(9000)}`);

    expect(written.join("")).toBe("");
  });
});

describe("generated ids", () => {
  it("match the W3C forms and differ between calls", () => {
    expect(newTraceId()).toMatch(/^[0-9a-f]{32}$/);
    expect(newSpanId()).toMatch(/^[0-9a-f]{16}$/);
    expect(newTraceId()).not.toBe(newTraceId());
    expect(newSpanId()).not.toBe(newSpanId());
  });
});

describe("outbound traceparent", () => {
  it("buildTraceparent has the version 00 form and carries the trace id", () => {
    const header = buildTraceparent({ traceId: TRACE_ID, spanId: "00f067aa0ba902b7" });

    expect(header).toMatch(OUTBOUND);
    expect(header).toBe(`00-${TRACE_ID}-00f067aa0ba902b7-01`);
  });

  it("withTraceparent sets the header from the request context and never sets tracestate", () => {
    const headers = new Headers();

    runWithRequestContext({ traceId: TRACE_ID, spanId: "00f067aa0ba902b7", nonce: "" }, () => withTraceparent(headers));

    expect(headers.get("traceparent")).toMatch(OUTBOUND);
    expect(headers.get("traceparent")).toContain(TRACE_ID);
    expect(headers.has("tracestate")).toBe(false);
  });

  it("withTraceparent adds nothing outside a request", () => {
    const headers = withTraceparent(new Headers());

    expect(headers.has("traceparent")).toBe(false);
  });
});
