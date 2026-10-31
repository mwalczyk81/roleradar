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

export const meta: MetaFunction = () => [
  { title: "RoleRadar" },
];

export const links: LinksFunction = () => [
  { rel: "stylesheet", href: dsTokensHref },
  { rel: "stylesheet", href: dsBundleHref },
];

export async function loader({ request, context }: LoaderFunctionArgs) {
  const cookieHeader = request.headers.get("Cookie");
  const match = cookieHeader?.match(/roleradar_theme=(light|dark)/);
  const theme = match ? match[1] : undefined;
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
  const { theme, nonce } = useLoaderData<typeof loader>();

  const themeScript = `
    (function() {
      try {
        var theme = ${JSON.stringify(theme)};
        if (!theme) {
          var match = document.cookie.match(/roleradar_theme=(light|dark)/);
          theme = match ? match[1] : (window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light');
        }
        document.documentElement.setAttribute('data-theme', theme);
      } catch (e) {}
    })();
  `;

  return (
    <html lang="en" data-theme={theme ?? "light"}>
      <head>
        <meta charSet="utf-8" />
        <meta name="viewport" content="width=device-width, initial-scale=1" />
        <Meta />
        <Links />
        <script
          nonce={nonce}
          dangerouslySetInnerHTML={{ __html: themeScript }}
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
      <body style={{ padding: "2rem", fontFamily: "sans-serif" }}>
        <h1>Application Error</h1>
        <p>{message}</p>
        <Scripts />
      </body>
    </html>
  );
}
