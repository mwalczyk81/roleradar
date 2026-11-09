import { AsyncLocalStorage } from "node:async_hooks";

// Feature 013: the per-request store (CSP nonce and correlation id). web/server.ts and the React Router build are
// bundled separately, so the store lives on globalThis under a registered symbol: both bundles must share one
// AsyncLocalStorage instance or loaders would not see the request's context.

export interface RequestContext {
  correlationId: string;
  nonce: string;
}

const STORE_KEY = Symbol.for("roleradar.requestContext");

type GlobalWithStore = typeof globalThis & { [STORE_KEY]?: AsyncLocalStorage<RequestContext> };

function store(): AsyncLocalStorage<RequestContext> {
  const holder = globalThis as GlobalWithStore;
  holder[STORE_KEY] ??= new AsyncLocalStorage<RequestContext>();
  return holder[STORE_KEY];
}

export function runWithRequestContext<T>(context: RequestContext, fn: () => T): T {
  return store().run(context, fn);
}

export function getRequestContext(): RequestContext | undefined {
  return store().getStore();
}
