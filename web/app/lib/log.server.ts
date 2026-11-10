import { formatRecord, isLevelEnabled, resolveLogFormat, resolveLogLevel, type LogLevel } from "./observability.js";
import { getRequestContext } from "./request-context.server.js";

// Feature 013: the web logger. info and below go to stdout, warn and above to stderr. The format comes from
// ROLERADAR_LOG_FORMAT (json in the image, text otherwise), the minimum level from ROLERADAR_LOG_LEVEL (default info).
// The correlation id comes from the request store unless a caller passes one (callbacks outside the store).

export interface LogFields {
  event?: string;
  correlationId?: string;
  props?: Record<string, unknown>;
  error?: unknown;
}

function write(level: LogLevel, msg: string, fields: LogFields = {}): void {
  if (!isLevelEnabled(level, resolveLogLevel(process.env.ROLERADAR_LOG_LEVEL))) {
    return;
  }

  const line = formatRecord(
    {
      level,
      msg,
      event: fields.event,
      correlationId: fields.correlationId ?? getRequestContext()?.correlationId,
      props: fields.props,
      error: fields.error,
    },
    resolveLogFormat(process.env.ROLERADAR_LOG_FORMAT)
  );

  const stream = level === "warn" || level === "error" || level === "fatal" ? process.stderr : process.stdout;
  stream.write(`${line}\n`);
}

export const log = {
  debug: (msg: string, fields?: LogFields) => write("debug", msg, fields),
  info: (msg: string, fields?: LogFields) => write("info", msg, fields),
  warn: (msg: string, fields?: LogFields) => write("warn", msg, fields),
  error: (msg: string, fields?: LogFields) => write("error", msg, fields),
  fatal: (msg: string, fields?: LogFields) => write("fatal", msg, fields),
};
