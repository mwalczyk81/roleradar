import { trace } from "@opentelemetry/api";
import { formatRecord, isLevelEnabled, resolveLogFormat, resolveLogLevel, scrub, scrubProps, type LogLevel } from "./observability.js";
import { getRequestContext } from "./request-context.server.js";
import { emitWebLog, type WebLogAttributes } from "./telemetry.server.js";

// Feature 013 & Feature 015 Gate 3 (RR-70, RR-86, RR-72): the web logger.
// info and below go to stdout, warn and above to stderr. The format comes from
// ROLERADAR_LOG_FORMAT (json in the image, text otherwise), the minimum level from ROLERADAR_LOG_LEVEL (default info).
// The trace id (field traceId) comes from the active span or request store.
// When OpenTelemetry logging is active, scrubbed log records are bridged to emitWebLog with traceId and spanId.

export interface LogFields {
  event?: string;
  traceId?: string;
  props?: Record<string, unknown>;
  error?: unknown;
}

function toLogAttributes(props: Record<string, unknown>): WebLogAttributes {
  const result: WebLogAttributes = {};
  for (const [key, value] of Object.entries(props)) {
    if (typeof value === "string" || typeof value === "number" || typeof value === "boolean") {
      result[key] = value;
    } else if (value !== null && value !== undefined) {
      result[key] = String(value);
    }
  }
  return result;
}

function write(level: LogLevel, msg: string, fields: LogFields = {}): void {
  if (!isLevelEnabled(level, resolveLogLevel(process.env.ROLERADAR_LOG_LEVEL))) {
    return;
  }

  const activeSpan = trace.getActiveSpan();
  const spanCtx = activeSpan?.spanContext();
  const resolvedTraceId = fields.traceId ?? spanCtx?.traceId ?? getRequestContext()?.traceId;
  const resolvedSpanId = spanCtx?.spanId;

  const line = formatRecord(
    {
      level,
      msg,
      event: fields.event,
      traceId: resolvedTraceId,
      props: fields.props,
      error: fields.error,
    },
    resolveLogFormat(process.env.ROLERADAR_LOG_FORMAT)
  );

  const stream = level === "warn" || level === "error" || level === "fatal" ? process.stderr : process.stdout;
  stream.write(`${line}\n`);

  try {
    const cleanProps = fields.props ? scrubProps(fields.props) : {};
    const attributes = toLogAttributes(cleanProps);
    attributes["log.level"] = level;
    if (fields.event) {
      attributes["event"] = scrub(fields.event);
    }

    emitWebLog({
      body: scrub(msg),
      traceId: resolvedTraceId,
      spanId: resolvedSpanId,
      attributes,
      severityText: level,
      timestamp: new Date(),
    });
  } catch {
    // Non-blocking telemetry emission
  }
}

export const log = {
  debug: (msg: string, fields?: LogFields) => write("debug", msg, fields),
  info: (msg: string, fields?: LogFields) => write("info", msg, fields),
  warn: (msg: string, fields?: LogFields) => write("warn", msg, fields),
  error: (msg: string, fields?: LogFields) => write("error", msg, fields),
  fatal: (msg: string, fields?: LogFields) => write("fatal", msg, fields),
};
