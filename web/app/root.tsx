import type { LinksFunction, LoaderFunctionArgs, MetaFunction } from "react-router";
import * as React from "react";
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

export const meta: MetaFunction = () => [
  { title: "RoleRadar" },
];

export const links: LinksFunction = () => [
  { rel: "stylesheet", href: dsTokensHref, nonce: "" },
  { rel: "stylesheet", href: dsBundleHref, nonce: "" },
  { rel: "stylesheet", href: appCssHref, nonce: "" },
];

export async function loader({ request, context }: LoaderFunctionArgs) {
  const cookieHeader = request.headers.get("Cookie");
  const match = cookieHeader?.match(/roleradar_theme=(light|dark)/);
  const theme = match ? (match[1] as "light" | "dark") : undefined;
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
  const [theme, setTheme] = React.useState<"light" | "dark" | undefined>(() => {
    if (typeof document !== "undefined") {
      const match = document.cookie.match(/roleradar_theme=(light|dark)/);
      if (match) return match[1] as "light" | "dark";
      const attr = document.documentElement.getAttribute("data-theme");
      if (attr === "light" || attr === "dark") return attr;
      return window.matchMedia?.("(prefers-color-scheme: dark)").matches ? "dark" : "light";
    }
    return serverTheme;
  });

  React.useEffect(() => {
    const getCookieTheme = () => {
      const match = document.cookie.match(/roleradar_theme=(light|dark)/);
      return match ? (match[1] as "light" | "dark") : null;
    };

    const cookieTheme = getCookieTheme();
    if (!cookieTheme && typeof window !== "undefined" && typeof window.matchMedia === "function") {
      const mql = window.matchMedia("(prefers-color-scheme: dark)");
      const current = mql.matches ? "dark" : "light";
      setTheme(current);
      document.documentElement.setAttribute("data-theme", current);

      const handleChange = (e: MediaQueryListEvent) => {
        if (!getCookieTheme()) {
          const next = e.matches ? "dark" : "light";
          setTheme(next);
          document.documentElement.setAttribute("data-theme", next);
        }
      };

      mql.addEventListener("change", handleChange);
      return () => mql.removeEventListener("change", handleChange);
    }
  }, []);

  const themeScript = `
    (function() {
      try {
        var match = document.cookie.match(/roleradar_theme=(light|dark)/);
        var theme = match ? match[1] : (window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light');
        document.documentElement.setAttribute('data-theme', theme);
      } catch (e) {}
    })();
  `;

  return (
    <html lang="en" data-theme={theme} suppressHydrationWarning>
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
