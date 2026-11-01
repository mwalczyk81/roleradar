import type { ActionFunctionArgs, LoaderFunctionArgs, FetcherWithComponents } from "react-router";

export type TestParams = Record<string, string | undefined>;

export async function callLoader<T>(
  loader: (args: LoaderFunctionArgs) => Promise<T> | T,
  request: Request,
  params?: TestParams,
  context?: unknown
): Promise<T>;
export async function callLoader<T>(
  loader: (args: LoaderFunctionArgs) => unknown,
  request: Request,
  params?: TestParams,
  context?: unknown
): Promise<T>;
export async function callLoader<T>(
  loader: (args: LoaderFunctionArgs) => unknown,
  request: Request,
  params: TestParams = {},
  context: unknown = {}
): Promise<T> {
  return (await loader({
    request,
    params,
    context,
  } as unknown as LoaderFunctionArgs)) as T;
}

export async function callAction<T>(
  action: (args: ActionFunctionArgs) => Promise<T> | T,
  request: Request,
  params?: TestParams,
  context?: unknown
): Promise<T>;
export async function callAction<T>(
  action: (args: ActionFunctionArgs) => unknown,
  request: Request,
  params?: TestParams,
  context?: unknown
): Promise<T>;
export async function callAction<T>(
  action: (args: ActionFunctionArgs) => unknown,
  request: Request,
  params: TestParams = {},
  context: unknown = {}
): Promise<T> {
  return (await action({
    request,
    params,
    context,
  } as unknown as ActionFunctionArgs)) as T;
}

export function createMockFetcher<TData = unknown>(
  overrides?: Partial<FetcherWithComponents<TData>>
): FetcherWithComponents<TData> {
  return {
    Form: "form" as unknown as FetcherWithComponents<TData>["Form"],
    submit: () => {},
    load: () => {},
    state: "idle",
    data: undefined,
    ...overrides,
  } as unknown as FetcherWithComponents<TData>;
}
