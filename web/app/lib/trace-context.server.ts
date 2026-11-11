import { randomBytes } from "node:crypto";
import {
  ROOT_CONTEXT,
  SpanKind,
  TraceFlags,
  context as otelContext,
  defaultTextMapGetter,
  defaultTextMapSetter,
  isSpanContextValid,
  trace,
  type Context,
  type Span,
  type SpanContext,
} from "@opentelemetry/api";
import { W3CTraceContextPropagator } from "@opentelemetry/core";
import { getRequestContext } from "./request-context.server.js";
import { getWebTracer } from "./telemetry.server.js";

// Feature 014 US2 (contracts/trace-context.md, ADR 0044) & Feature 015 Gate 2 (RR-68, FR-001, FR-002):
// Single W3C Trace Context mechanism at the web edge backed by W3CTraceContextPropagator and the OpenTelemetry SDK tracer.
//
// Rules: a valid inbound traceparent (exact 55-char lowercase version 00 with non-zero traceId and spanId) is honoured.
// Any malformed, all-zero, upper-case, oversized or extra-field value is discarded and replaced by a generated trace,
// and is never logged. `tracestate` is not read and not forwarded.

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

function parseInboundSpanContext(inbound: string): SpanContext | undefined {
  if (inbound.length !== TRACEPARENT_LENGTH || inbound !== inbound.toLowerCase()) {
    return undefined;
  }
  const extracted = propagator.extract(
    ROOT_CONTEXT,
    { [TRACEPARENT_HEADER]: inbound },
    defaultTextMapGetter
  );
  const spanContext = trace.getSpanContext(extracted);
  if (!spanContext || !isSpanContextValid(spanContext)) {
    return undefined;
  }
  if (!/^[0-9a-f]{32}$/.test(spanContext.traceId) || !/^[0-9a-f]{16}$/.test(spanContext.spanId)) {
    return undefined;
  }
  return {
    traceId: spanContext.traceId,
    spanId: spanContext.spanId,
    traceFlags: TraceFlags.SAMPLED,
    isRemote: true,
  };
}

export function extractValidatedParentContext(
  inbound: string | null | undefined
): Context {
  if (typeof inbound !== "string") {
    return ROOT_CONTEXT;
  }
  const validSpanContext = parseInboundSpanContext(inbound);
  if (!validSpanContext) {
    return ROOT_CONTEXT;
  }
  return trace.setSpanContext(ROOT_CONTEXT, validSpanContext);
}

/**
 * Starts an OpenTelemetry SERVER span for an incoming HTTP request, honouring a valid inbound `traceparent`
 * as the remote parent span context and otherwise starting a fresh root trace.
 */
export function startServerRequestSpan(
  method: string,
  path: string,
  inbound: string | null | undefined,
  scheme: string = "https"
): {
  span: Span;
  activeContext: Context;
  traceId: string;
  spanId: string;
} {
  const parentContext = extractValidatedParentContext(inbound);
  const tracer = getWebTracer();
  const span = tracer.startSpan(
    `${method} ${path}`,
    {
      kind: SpanKind.SERVER,
      attributes: {
        "http.request.method": method,
        "url.path": path,
        "url.scheme": scheme,
      },
    },
    parentContext
  );
  const spanCtx = span.spanContext();
  const activeContext = trace.setSpan(parentContext, span);
  return {
    span,
    activeContext,
    traceId: spanCtx.traceId,
    spanId: spanCtx.spanId,
  };
}

/**
 * The trace of one request: the inbound trace id when the header is valid, otherwise a generated one, plus a new span id for
 * this service's own work.
 */
export function startTrace(inbound: string | null | undefined): TraceIds {
  const validSpanContext =
    typeof inbound === "string" ? parseInboundSpanContext(inbound) : undefined;
  return {
    traceId: validSpanContext?.traceId ?? newTraceId(),
    spanId: newSpanId(),
  };
}

/** The outbound `traceparent` value for a trace, built by the propagator from a span context. */
export function buildTraceparent(ids: TraceIds): string {
  const ctx = trace.setSpanContext(ROOT_CONTEXT, {
    traceId: ids.traceId,
    spanId: ids.spanId,
    traceFlags: TraceFlags.SAMPLED,
  });
  const carrier: Record<string, string> = {};
  propagator.inject(ctx, carrier, defaultTextMapSetter);
  return carrier[TRACEPARENT_HEADER] ?? `00-${ids.traceId}-${ids.spanId}-01`;
}

/**
 * Adds the current request's `traceparent` to outbound headers using the single
 * W3CTraceContextPropagator. Outside a request, adds nothing. `tracestate` is never added.
 */
export function withTraceparent(headers: Headers): Headers {
  const reqContext = getRequestContext();
  if (reqContext?.traceId && reqContext.spanId) {
    headers.set(
      TRACEPARENT_HEADER,
      buildTraceparent({ traceId: reqContext.traceId, spanId: reqContext.spanId })
    );
    return headers;
  }

  const activeSpan = trace.getSpan(otelContext.active());
  if (activeSpan) {
    const spanCtx = activeSpan.spanContext();
    if (isSpanContextValid(spanCtx)) {
      headers.set(
        TRACEPARENT_HEADER,
        buildTraceparent({ traceId: spanCtx.traceId, spanId: spanCtx.spanId })
      );
    }
  }
  return headers;
}
