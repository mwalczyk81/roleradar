import { randomBytes } from "node:crypto";
import { ROOT_CONTEXT, TraceFlags, defaultTextMapGetter, defaultTextMapSetter, isSpanContextValid, trace } from "@opentelemetry/api";
import { W3CTraceContextPropagator } from "@opentelemetry/core";
import { getRequestContext } from "./request-context.server.js";

// Feature 014 US2 (contracts/trace-context.md, ADR 0044): W3C Trace Context at the web edge. A maintained propagator
// validates an inbound `traceparent` and formats the outbound one. There is no SDK, exporter or instrumentation:
// feature 015 adds those on top of `@opentelemetry/api`, which this file already uses, and then deletes only the
// manual root context and the explicit injection written here.
//
// Rules: a valid inbound traceparent is honoured (its trace id becomes the request's). A malformed, all-zero, upper-case,
// oversized or extra-field value is discarded and replaced by a generated trace, and is never logged. `tracestate` is not
// read and not forwarded.

export const TRACEPARENT_HEADER = "traceparent";

/** A version 00 traceparent is exactly 55 characters. Anything else (oversized, extra fields) is discarded before parsing. */
const TRACEPARENT_LENGTH = 55;

const propagator = new W3CTraceContextPropagator();

export interface TraceIds {
  traceId: string;
  spanId: string;
}

function randomHex(bytes: number): string {
  for (;;) {
    const value = randomBytes(bytes).toString("hex");
    if (/[1-9a-f]/.test(value)) {
      return value;
    }
  }
}

export function newTraceId(): string {
  return randomHex(16);
}

export function newSpanId(): string {
  return randomHex(8);
}

function parseInbound(inbound: string): string | undefined {
  if (inbound.length !== TRACEPARENT_LENGTH) {
    return undefined;
  }
  const context = propagator.extract(ROOT_CONTEXT, { [TRACEPARENT_HEADER]: inbound }, defaultTextMapGetter);
  const spanContext = trace.getSpanContext(context);
  if (!spanContext || !isSpanContextValid(spanContext)) {
    return undefined;
  }
  // The propagator accepts any case on the version and flags bytes only through its own regex; keep the lower-case rule explicit.
  return /^[0-9a-f]{32}$/.test(spanContext.traceId) && inbound === inbound.toLowerCase() ? spanContext.traceId : undefined;
}

/**
 * The trace of one request: the inbound trace id when the header is valid, otherwise a generated one, plus a new span id for
 * this service's own work.
 */
export function startTrace(inbound: string | null | undefined): TraceIds {
  const accepted = typeof inbound === "string" ? parseInbound(inbound) : undefined;
  return { traceId: accepted ?? newTraceId(), spanId: newSpanId() };
}

/** The outbound `traceparent` value for a trace, built by the propagator from a span context. */
export function buildTraceparent(ids: TraceIds): string {
  const context = trace.setSpanContext(ROOT_CONTEXT, { traceId: ids.traceId, spanId: ids.spanId, traceFlags: TraceFlags.SAMPLED });
  const carrier: Record<string, string> = {};
  propagator.inject(context, carrier, defaultTextMapSetter);
  return carrier[TRACEPARENT_HEADER] ?? `00-${ids.traceId}-${ids.spanId}-01`;
}

/** Adds the current request's `traceparent` to outbound headers. Outside a request, adds nothing. `tracestate` is never added. */
export function withTraceparent(headers: Headers): Headers {
  const context = getRequestContext();
  if (context?.traceId && context.spanId) {
    headers.set(TRACEPARENT_HEADER, buildTraceparent({ traceId: context.traceId, spanId: context.spanId }));
  }
  return headers;
}
