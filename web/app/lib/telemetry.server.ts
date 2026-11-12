import { performance } from "node:perf_hooks";
import {
  SpanStatusCode,
  context,
  propagation,
  trace,
  type Attributes,
  type AttributeValue,
  type Context,
  type Histogram,
  type Tracer,
  type UpDownCounter,
} from "@opentelemetry/api";
import {
  METRIC_HTTP_SERVER_REQUEST_DURATION,
  METRIC_HTTP_CLIENT_REQUEST_DURATION,
} from "@opentelemetry/semantic-conventions";
import { METRIC_NODEJS_EVENTLOOP_UTILIZATION } from "@opentelemetry/semantic-conventions/incubating";
import { matchRoutes, type RouteObject } from "react-router";
import { AsyncLocalStorageContextManager } from "@opentelemetry/context-async-hooks";
import { W3CTraceContextPropagator } from "@opentelemetry/core";
import { OTLPLogExporter } from "@opentelemetry/exporter-logs-otlp-http";
import { OTLPMetricExporter } from "@opentelemetry/exporter-metrics-otlp-http";
import { OTLPTraceExporter } from "@opentelemetry/exporter-trace-otlp-http";
import { resourceFromAttributes, type Resource } from "@opentelemetry/resources";
import {
  BatchLogRecordProcessor,
  LoggerProvider,
  SimpleLogRecordProcessor,
  type LogRecordExporter,
  type LogRecordProcessor,
} from "@opentelemetry/sdk-logs";
import {
  MeterProvider,
  PeriodicExportingMetricReader,
  createAllowListAttributesProcessor,
  type MetricReader,
  type ViewOptions,
} from "@opentelemetry/sdk-metrics";
import {
  AlwaysOnSampler,
  BatchSpanProcessor,
  SimpleSpanProcessor,
  TracerProvider,
  type ReadableSpan,
  type Span,
  type SpanExporter,
  type SpanProcessor,
} from "@opentelemetry/sdk-trace";

// Feature 015 Gate 2 (RR-68, RR-72, FR-001 to FR-003, FR-012 to FR-015, SC-001 to SC-003, ADR 0050):
// OpenTelemetry setup and closed allowlist/drop-list span processor for the React Router v7 SSR web server.

const DEFAULT_WEB_SERVICE_NAME = "roleradar-web";
const SERVICE_NAMESPACE = "roleradar";
const SERVICE_VERSION = "0.1.0";
const DEFAULT_FLUSH_TIMEOUT_MS = 2000;
const MIN_FLUSH_TIMEOUT_MS = 100;
const MAX_FLUSH_TIMEOUT_MS = 5000;

export const ALLOWED_SPAN_ATTRIBUTE_KEYS: ReadonlySet<string> = new Set<string>([
  "http.request.method",
  "http.request.method_original",
  "http.response.status_code",
  "http.route",
  "url.scheme",
  "url.path",
  "url.full",
  "server.address",
  "server.port",
  "network.protocol.version",
  "network.peer.address",
  "network.transport",
  "db.system",
  "db.system.name",
  "db.namespace",
  "db.name",
  "db.collection.name",
  "db.mongodb.collection",
  "db.operation",
  "db.operation.name",
  "db.operation.summary",
  "db.command.name",
  "db.query.summary",
  "db.response.status_code",
  "error.type",
  "exception.type",
  "otel.status_code",
  "roleradar.actor",
  "roleradar.command",
  "roleradar.query",
  "roleradar.tool",
]);

export const DROPPED_SPAN_ATTRIBUTE_KEYS: ReadonlySet<string> = new Set<string>([
  "url.query",
  "http.target",
  "http.url",
  "db.query.text",
  "db.statement",
  "db.connection_string",
  "exception.message",
  "exception.stacktrace",
  "otel.status_description",
  "enduser.id",
  "user.id",
  "client.address",
  "user_agent.original",
  "http.user_agent",
]);

function isDroppedOrUnlistedKey(key: string): boolean {
  const lower = key.toLowerCase();
  if (DROPPED_SPAN_ATTRIBUTE_KEYS.has(lower)) {
    return true;
  }
  if (
    lower.startsWith("http.request.header.") ||
    lower.startsWith("http.response.header.")
  ) {
    return true;
  }
  return !ALLOWED_SPAN_ATTRIBUTE_KEYS.has(lower);
}

export function stripQueryAndFragment(value: string): string {
  if (!value) {
    return "";
  }
  const qIdx = value.indexOf("?");
  const fIdx = value.indexOf("#");
  let cut = -1;
  if (qIdx >= 0 && fIdx >= 0) {
    cut = Math.min(qIdx, fIdx);
  } else if (qIdx >= 0) {
    cut = qIdx;
  } else if (fIdx >= 0) {
    cut = fIdx;
  }
  return cut >= 0 ? value.slice(0, cut) : value;
}

export function sanitizeFullUrl(rawUrl: string): string {
  if (!rawUrl || rawUrl.trim().length === 0) {
    return "";
  }
  const withoutQueryOrFragment = stripQueryAndFragment(rawUrl.trim());
  try {
    const parsed = new URL(withoutQueryOrFragment);
    parsed.username = "";
    parsed.password = "";
    parsed.search = "";
    parsed.hash = "";
    return parsed.toString();
  } catch {
    const schemeIdx = withoutQueryOrFragment.indexOf("://");
    if (schemeIdx < 0) {
      return withoutQueryOrFragment;
    }
    const authorityStart = schemeIdx + 3;
    const pathStart = withoutQueryOrFragment.indexOf("/", authorityStart);
    const authorityEnd =
      pathStart >= 0 ? pathStart : withoutQueryOrFragment.length;
    const authority = withoutQueryOrFragment.slice(authorityStart, authorityEnd);
    const atIdx = authority.indexOf("@");
    if (atIdx < 0) {
      return withoutQueryOrFragment;
    }
    return (
      withoutQueryOrFragment.slice(0, authorityStart) +
      authority.slice(atIdx + 1) +
      withoutQueryOrFragment.slice(authorityEnd)
    );
  }
}

export class WebSpanAttributeProcessor implements SpanProcessor {
  onStart(_span: Span, _parentContext: Context): void {
    // Attributes and events are finalized in onEnd before export.
  }

  onEnd(span: ReadableSpan): void {
    // 1. Clear free-text status message while preserving status code (UNSET, OK, ERROR).
    if (span.status.message !== undefined) {
      const mutableStatus: { code: SpanStatusCode; message?: string } = span.status;
      delete mutableStatus.message;
    }

    // 2. Sanitize span events (e.g. exception events): retain only exception.type and copy to error.type.
    const mutableAttrs: Record<string, unknown> = span.attributes;
    for (const ev of span.events) {
      if (!ev.attributes) {
        continue;
      }
      const evAttrs: Record<string, unknown> = ev.attributes;
      const exType = evAttrs["exception.type"];
      if (typeof exType === "string" && exType.trim().length > 0) {
        if (mutableAttrs["error.type"] === undefined) {
          mutableAttrs["error.type"] = exType;
        }
      }
      for (const key of Object.keys(evAttrs)) {
        if (key !== "exception.type" && isDroppedOrUnlistedKey(key)) {
          delete evAttrs[key];
        }
      }
    }

    // 3. Enforce closed operational attribute allowlist, drop list, and URL sanitization.
    for (const key of Object.keys(mutableAttrs)) {
      if (isDroppedOrUnlistedKey(key)) {
        delete mutableAttrs[key];
        continue;
      }
      const val = mutableAttrs[key];
      if (typeof val === "string") {
        const lowerKey = key.toLowerCase();
        if (lowerKey === "url.full") {
          mutableAttrs[key] = sanitizeFullUrl(val);
        } else if (lowerKey === "url.path" || lowerKey === "http.route") {
          mutableAttrs[key] = stripQueryAndFragment(val);
        }
      }
    }
  }

  async forceFlush(): Promise<void> {
    // Stateless processor
  }

  async shutdown(): Promise<void> {
    // Stateless processor
  }
}

export function resolveWebServiceName(
  env: NodeJS.ProcessEnv = process.env
): string {
  const configured = env.OTEL_SERVICE_NAME;
  return configured && configured.trim().length > 0
    ? configured.trim()
    : DEFAULT_WEB_SERVICE_NAME;
}

export function isWebOtlpEndpointConfigured(
  env: NodeJS.ProcessEnv = process.env
): boolean {
  const endpoint = env.OTEL_EXPORTER_OTLP_ENDPOINT;
  return Boolean(endpoint && endpoint.trim().length > 0);
}

export function resolveOtlpTracesUrl(rawEndpoint: string): string {
  const trimmed = rawEndpoint.trim().replace(/\/+$/, "");
  if (trimmed.endsWith("/v1/traces")) {
    return trimmed;
  }
  return `${trimmed}/v1/traces`;
}

export function resolveOtlpLogsUrl(rawEndpoint: string): string {
  const trimmed = rawEndpoint.trim().replace(/\/+$/, "");
  if (trimmed.endsWith("/v1/logs")) {
    return trimmed;
  }
  return `${trimmed}/v1/logs`;
}

export function resolveOtlpMetricsUrl(rawEndpoint: string): string {
  const trimmed = rawEndpoint.trim().replace(/\/+$/, "");
  if (trimmed.endsWith("/v1/metrics")) {
    return trimmed;
  }
  if (trimmed.endsWith("/v1/traces") || trimmed.endsWith("/v1/logs")) {
    return trimmed.replace(/\/v1\/(traces|logs)$/, "/v1/metrics");
  }
  return `${trimmed}/v1/metrics`;
}

export function resolveWebFlushTimeoutMs(
  env: NodeJS.ProcessEnv = process.env
): number {
  const raw = env.ROLERADAR_TELEMETRY_FLUSH_TIMEOUT_MS;
  if (raw && raw.trim().length > 0) {
    const parsed = Number.parseInt(raw.trim(), 10);
    if (!Number.isNaN(parsed)) {
      return Math.min(
        MAX_FLUSH_TIMEOUT_MS,
        Math.max(MIN_FLUSH_TIMEOUT_MS, parsed)
      );
    }
  }
  return DEFAULT_FLUSH_TIMEOUT_MS;
}

export function createWebResource(
  serviceName: string = DEFAULT_WEB_SERVICE_NAME
): Resource {
  return resourceFromAttributes({
    "service.name": serviceName,
    "service.namespace": SERVICE_NAMESPACE,
    "service.version": SERVICE_VERSION,
  });
}

export type WebLogAttributes = Record<string, string | number | boolean | undefined>;

export interface WebLogRecord {
  body: string;
  traceId?: string;
  spanId?: string;
  attributes?: WebLogAttributes;
  severityText?: string;
  timestamp?: Date;
}

export interface WebTelemetryInitOptions {
  env?: NodeJS.ProcessEnv;
  testExporter?: SpanExporter;
  testLogExporter?: LogRecordExporter;
  testMetricReader?: MetricReader;
  enableLogCaptureForTests?: boolean;
  forceReinit?: boolean;
}

export interface WebMetricInstruments {
  serverDuration: Histogram;
  serverActiveRequests: UpDownCounter;
  clientDuration: Histogram;
}

export interface WebTelemetryState {
  provider: TracerProvider;
  loggerProvider: LoggerProvider | null;
  meterProvider: MeterProvider | null;
  instruments?: WebMetricInstruments;
  otlpExporterRegistered: boolean;
  otlpLogExporterRegistered: boolean;
  otlpMetricExporterRegistered: boolean;
  enableCapture?: boolean;
}

const TELEMETRY_STATE_KEY = Symbol.for("roleradar.webTelemetryState");

interface GlobalTelemetryHolder {
  activeState: WebTelemetryState | null;
  contextManagerInitialized: boolean;
}

type GlobalWithTelemetry = typeof globalThis & {
  [TELEMETRY_STATE_KEY]?: GlobalTelemetryHolder;
};

function telemetryHolder(): GlobalTelemetryHolder {
  const g = globalThis as GlobalWithTelemetry;
  g[TELEMETRY_STATE_KEY] ??= {
    activeState: null,
    contextManagerInitialized: false,
  };
  return g[TELEMETRY_STATE_KEY];
}

let testExportedLogs: WebLogRecord[] = [];

export function getTestExportedLogs(): WebLogRecord[] {
  return [...testExportedLogs];
}

// Custom runtime metric names (not defined in @opentelemetry/semantic-conventions):
export const METRIC_NODEJS_MEMORY_HEAP_USED = "process.runtime.nodejs.memory.heap_used";
export const METRIC_NODEJS_MEMORY_RSS = "process.runtime.nodejs.memory.rss";

export function resetWebTelemetryForTests(): void {
  testExportedLogs = [];
  configuredRoutes = null;
  const holder = telemetryHolder();
  if (holder.activeState) {
    const current = holder.activeState;
    holder.activeState = null;
    try {
      current.provider.shutdown();
      current.loggerProvider?.shutdown();
      current.meterProvider?.shutdown();
    } catch {
      // Ignore
    }
  }
}

export const ALLOWED_SERVER_DURATION_KEYS: readonly string[] = [
  "http.request.method",
  "http.route",
  "http.response.status_code",
  "error.type",
  "url.scheme",
];
export const ALLOWED_SERVER_ACTIVE_KEYS: readonly string[] = ["http.request.method", "url.scheme"];
export const ALLOWED_CLIENT_DURATION_KEYS: readonly string[] = [
  "http.request.method",
  "http.response.status_code",
  "server.address",
  "server.port",
  "url.scheme",
  "error.type",
];

export function createWebMetricViews(): ViewOptions[] {
  return [
    {
      instrumentName: "http.server.request.duration",
      attributesProcessors: [createAllowListAttributesProcessor([...ALLOWED_SERVER_DURATION_KEYS])],
    },
    {
      instrumentName: "http.server.active_requests",
      attributesProcessors: [createAllowListAttributesProcessor([...ALLOWED_SERVER_ACTIVE_KEYS])],
    },
    {
      instrumentName: "http.client.request.duration",
      attributesProcessors: [createAllowListAttributesProcessor([...ALLOWED_CLIENT_DURATION_KEYS])],
    },
  ];
}

export function emitWebLog(record: WebLogRecord): void {
  const holder = telemetryHolder();
  if (holder.activeState?.enableCapture) {
    testExportedLogs.push(record);
  }
  if (holder.activeState?.loggerProvider) {
    const logger = holder.activeState.loggerProvider.getLogger(DEFAULT_WEB_SERVICE_NAME, SERVICE_VERSION);
    logger.emit({
      body: record.body,
      severityText: record.severityText,
      attributes: record.attributes,
    });
  }
}

export function initializeWebTelemetry(
  options?: WebTelemetryInitOptions
): WebTelemetryState {
  const holder = telemetryHolder();
  if (holder.activeState && !options?.forceReinit) {
    return holder.activeState;
  }

  const env = options?.env ?? process.env;
  const serviceName = resolveWebServiceName(env);
  const flushTimeoutMs = resolveWebFlushTimeoutMs(env);

  if (!holder.contextManagerInitialized) {
    const contextManager = new AsyncLocalStorageContextManager();
    contextManager.enable();
    context.setGlobalContextManager(contextManager);
    propagation.setGlobalPropagator(new W3CTraceContextPropagator());
    holder.contextManagerInitialized = true;
  }

  const spanProcessors: SpanProcessor[] = [new WebSpanAttributeProcessor()];
  let otlpExporterRegistered = false;

  if (isWebOtlpEndpointConfigured(env)) {
    const endpoint = env.OTEL_EXPORTER_OTLP_ENDPOINT ?? "";
    const exporter = new OTLPTraceExporter({
      url: resolveOtlpTracesUrl(endpoint),
      timeoutMillis: flushTimeoutMs,
    });
    spanProcessors.push(
      new BatchSpanProcessor({
        exporter,
        scheduledDelayMillis: Math.min(250, flushTimeoutMs),
        exportTimeoutMillis: flushTimeoutMs,
      })
    );
    otlpExporterRegistered = true;
  }

  if (options?.testExporter) {
    spanProcessors.push(new SimpleSpanProcessor({ exporter: options.testExporter }));
  }

  const provider = new TracerProvider({
    sampler: new AlwaysOnSampler(),
    resource: createWebResource(serviceName),
    spanProcessors,
  });

  trace.disable();
  trace.setGlobalTracerProvider(provider);

  const logProcessors: LogRecordProcessor[] = [];
  let otlpLogExporterRegistered = false;

  if (isWebOtlpEndpointConfigured(env)) {
    const endpoint = env.OTEL_EXPORTER_OTLP_ENDPOINT ?? "";
    const logExporter = new OTLPLogExporter({
      url: resolveOtlpLogsUrl(endpoint),
      timeoutMillis: flushTimeoutMs,
    });
    logProcessors.push(
      new BatchLogRecordProcessor({
        exporter: logExporter,
        scheduledDelayMillis: Math.min(250, flushTimeoutMs),
        exportTimeoutMillis: flushTimeoutMs,
      })
    );
    otlpLogExporterRegistered = true;
  }

  if (options?.testLogExporter) {
    logProcessors.push(new SimpleLogRecordProcessor({ exporter: options.testLogExporter }));
  }

  let loggerProvider: LoggerProvider | null = null;
  if (logProcessors.length > 0 || options?.enableLogCaptureForTests) {
    loggerProvider = new LoggerProvider({
      resource: createWebResource(serviceName),
      processors: logProcessors,
    });
  }

  let otlpMetricExporterRegistered = false;
  const metricReaders: MetricReader[] = [];

  if (isWebOtlpEndpointConfigured(env)) {
    const endpoint = env.OTEL_EXPORTER_OTLP_ENDPOINT ?? "";
    const metricExporter = new OTLPMetricExporter({
      url: resolveOtlpMetricsUrl(endpoint),
      timeoutMillis: flushTimeoutMs,
    });
    const exportTimeout = Math.min(flushTimeoutMs, 30000);
    const exportInterval = Math.max(exportTimeout, Math.max(1000, flushTimeoutMs));
    metricReaders.push(
      new PeriodicExportingMetricReader({
        exporter: metricExporter,
        exportIntervalMillis: exportInterval,
        exportTimeoutMillis: exportTimeout,
      })
    );
    otlpMetricExporterRegistered = true;
  }

  if (options?.testMetricReader) {
    metricReaders.push(options.testMetricReader);
  }

  const meterProvider = new MeterProvider({
    resource: createWebResource(serviceName),
    readers: metricReaders,
    views: createWebMetricViews(),
  });

  const runtimeMeter = meterProvider.getMeter("roleradar-web-runtime", SERVICE_VERSION);
  const heapGauge = runtimeMeter.createObservableGauge(METRIC_NODEJS_MEMORY_HEAP_USED, {
    description: "Process heap memory used",
    unit: "By",
  });
  heapGauge.addCallback((observableResult) => {
    observableResult.observe(process.memoryUsage().heapUsed);
  });

  const rssGauge = runtimeMeter.createObservableGauge(METRIC_NODEJS_MEMORY_RSS, {
    description: "Process resident set size",
    unit: "By",
  });
  rssGauge.addCallback((observableResult) => {
    observableResult.observe(process.memoryUsage().rss);
  });

  const eluGauge = runtimeMeter.createObservableGauge(METRIC_NODEJS_EVENTLOOP_UTILIZATION, {
    description: "Event loop utilization ratio",
    unit: "1",
  });
  eluGauge.addCallback((observableResult) => {
    try {
      observableResult.observe(performance.eventLoopUtilization().utilization);
    } catch {
      // Ignore if eventLoopUtilization is unavailable
    }
  });

  const httpMeter = meterProvider.getMeter(DEFAULT_WEB_SERVICE_NAME, SERVICE_VERSION);
  const serverDuration = httpMeter.createHistogram(METRIC_HTTP_SERVER_REQUEST_DURATION, { unit: "s" });
  const serverActiveRequests = httpMeter.createUpDownCounter("http.server.active_requests", { unit: "{request}" });
  const clientDuration = httpMeter.createHistogram(METRIC_HTTP_CLIENT_REQUEST_DURATION, { unit: "s" });

  holder.activeState = {
    provider,
    loggerProvider,
    meterProvider,
    instruments: {
      serverDuration,
      serverActiveRequests,
      clientDuration,
    },
    otlpExporterRegistered,
    otlpLogExporterRegistered,
    otlpMetricExporterRegistered,
    enableCapture: Boolean(options?.enableLogCaptureForTests),
  };

  return holder.activeState;
}

export function getWebTracer(): Tracer {
  const holder = telemetryHolder();
  const state = holder.activeState ?? initializeWebTelemetry();
  return state.provider.getTracer(DEFAULT_WEB_SERVICE_NAME, SERVICE_VERSION);
}

export async function shutdownWebTelemetry(): Promise<void> {
  const holder = telemetryHolder();
  if (holder.activeState) {
    const current = holder.activeState;
    holder.activeState = null;
    try {
      await current.provider.forceFlush();
      await current.provider.shutdown();
      if (current.loggerProvider) {
        await current.loggerProvider.forceFlush();
        await current.loggerProvider.shutdown();
      }
      if (current.meterProvider) {
        await current.meterProvider.forceFlush();
        await current.meterProvider.shutdown();
      }
    } catch {
      // Ignore shutdown errors on unreachable endpoints
    }
    trace.disable();
  }
}

export function recordServerRequestDuration(
  durationSeconds: number,
  attributes: {
    method: string;
    route?: string;
    statusCode: number;
    scheme?: string;
    errorType?: string;
    [key: string]: unknown;
  }
): void {
  const holder = telemetryHolder();
  if (!holder.activeState?.instruments) return;
  const attrs: Attributes = {
    "http.request.method": attributes.method,
    "http.response.status_code": attributes.statusCode,
    "url.scheme": attributes.scheme ?? "http",
  };
  if (attributes.route) {
    attrs["http.route"] = attributes.route;
  }
  if (attributes.errorType) {
    attrs["error.type"] = attributes.errorType;
  }
  for (const [key, value] of Object.entries(attributes)) {
    if (!(key in attrs) && key !== "method" && key !== "route" && key !== "statusCode" && key !== "scheme" && key !== "errorType") {
      attrs[key] = value as AttributeValue;
    }
  }
  holder.activeState.instruments.serverDuration.record(durationSeconds, attrs);
}

export function trackServerActiveRequests(
  method: string,
  scheme: string,
  delta: number,
  extraAttrs?: Attributes
): void {
  const holder = telemetryHolder();
  if (!holder.activeState?.instruments) return;
  const attrs: Attributes = {
    "http.request.method": method,
    "url.scheme": scheme,
    ...(extraAttrs || {}),
  };
  holder.activeState.instruments.serverActiveRequests.add(delta, attrs);
}

export function recordClientRequestDuration(
  durationSeconds: number,
  attributes: {
    method: string;
    statusCode: number;
    address?: string;
    port?: number;
    scheme?: string;
    errorType?: string;
    [key: string]: unknown;
  }
): void {
  const holder = telemetryHolder();
  if (!holder.activeState?.instruments) return;
  const attrs: Attributes = {
    "http.request.method": attributes.method,
    "http.response.status_code": attributes.statusCode,
  };
  if (attributes.address) {
    attrs["server.address"] = attributes.address;
  }
  if (attributes.port) {
    attrs["server.port"] = attributes.port;
  }
  if (attributes.scheme) {
    attrs["url.scheme"] = attributes.scheme;
  }
  if (attributes.errorType) {
    attrs["error.type"] = attributes.errorType;
  }
  for (const [key, value] of Object.entries(attributes)) {
    if (!(key in attrs) && key !== "method" && key !== "statusCode" && key !== "address" && key !== "port" && key !== "scheme" && key !== "errorType") {
      attrs[key] = value as AttributeValue;
    }
  }
  holder.activeState.instruments.clientDuration.record(durationSeconds, attrs);
}

export interface RouteDefinition {
  id?: string;
  parentId?: string;
  path?: string;
  index?: boolean;
}

let configuredRoutes: RouteDefinition[] | null = null;

export function setConfiguredRoutesForTelemetry(routes: RouteDefinition[] | null): void {
  configuredRoutes = routes;
}

export function resolveHttpRoute(rawPath: string, routes?: RouteDefinition[] | null): string {
  const activeRoutes = routes !== undefined ? routes : configuredRoutes;
  if (!activeRoutes || activeRoutes.length === 0) {
    return "unmatched";
  }
  if (!rawPath || typeof rawPath !== "string") {
    return "unmatched";
  }
  const cleanPath = rawPath.split("?")[0].split("#")[0].trim() || "/";
  if (cleanPath === "/health" || cleanPath === "/health/live" || cleanPath === "/health/ready") {
    return cleanPath;
  }
  const matches = matchRoutes(activeRoutes as unknown as RouteObject[], cleanPath);
  if (!matches || matches.length === 0) {
    return "unmatched";
  }
  const leaf = matches[matches.length - 1];
  if (leaf.route.index) {
    return "/";
  }
  if (leaf.route.path) {
    return leaf.route.path.startsWith("/") ? leaf.route.path : `/${leaf.route.path}`;
  }
  if (leaf.route.id) {
    const id = leaf.route.id.replace(/^routes\//, "").replace(/\._index$/, "");
    return id.startsWith("/") ? id : `/${id}`;
  }
  return "unmatched";
}

export function normalizeHttpRoute(path: string): string {
  return resolveHttpRoute(path);
}
