import type { LinksFunction, LoaderFunctionArgs, MetaFunction } from "react-router";
import {
  Links,
  Meta,
  Outlet,
  Scripts,
  ScrollRestoration,
  useLoaderData,
} from "react-router";

import dsTokensHref from "./ds/tokens.css?url";
import dsBundleHref from "./ds/components/bundle.css?url";
import appCssHref from "./app.css?url";
import { THEME_BOOTSTRAP_SCRIPT, parseThemeCookie } from "./lib/theme.js";
import { useTheme } from "./lib/use-theme.js";

export const meta: MetaFunction = () => [
  { title: "RoleRadar" },
];

export const links: LinksFunction = () => [
  { rel: "stylesheet", href: dsTokensHref, nonce: "" },
  { rel: "stylesheet", href: dsBundleHref, nonce: "" },
  { rel: "stylesheet", href: appCssHref, nonce: "" },
];

export async function loader({ request, context }: LoaderFunctionArgs) {
  const theme = parseThemeCookie(request.headers.get("Cookie"));
  const nonce =
    (context as { nonce?: string })?.nonce ??
    request.headers.get("X-CSP-Nonce") ??
    "";

  return {
    theme,
    nonce,
  };
}

export default function App() {
  const { theme: serverTheme, nonce } = useLoaderData<typeof loader>();
  const { theme } = useTheme(serverTheme);

  // The server renders data-theme only when the cookie names a theme. With no cookie the blocking head script sets
  // it before first paint, so the server leaves it out rather than guess.
  const htmlTheme = typeof document === "undefined" ? serverTheme : theme;

  return (
    <html lang="en" data-theme={htmlTheme} suppressHydrationWarning>
      <head>
        <meta charSet="utf-8" />
        <meta name="viewport" content="width=device-width, initial-scale=1" />
        <Meta />
        <Links />
        <script
          nonce={nonce}
          dangerouslySetInnerHTML={{ __html: THEME_BOOTSTRAP_SCRIPT }}
        />
      </head>
      <body>
        <Outlet />
        <ScrollRestoration nonce={nonce} />
        <Scripts nonce={nonce} />
      </body>
    </html>
  );
}

export function ErrorBoundary({ error }: { error?: unknown }) {
  const message =
    error instanceof Error ? error.message : "An unexpected application error occurred.";

  return (
    <html lang="en">
      <head>
        <meta charSet="utf-8" />
        <meta name="viewport" content="width=device-width, initial-scale=1" />
        <title>Application Error</title>
        <Meta />
        <Links />
      </head>
      <body className="app-error-body">
        <h1>Application Error</h1>
        <p>{message}</p>
        <Scripts />
      </body>
    </html>
  );
}
