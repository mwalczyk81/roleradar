/**
 * The one implementation of theme handling (009 US2, specs/009-ui-polish-theming/contracts/theme-module.md).
 *
 * The theme is `light` or `dark`. It is chosen by, in order: the `roleradar_theme` cookie, then the system colour
 * scheme. The server renders `data-theme` from the cookie; a blocking script in the document head sets it from the
 * system scheme when there is no cookie, before first paint. The vendored kit selects its dark tokens only through the
 * `data-theme` attribute (it has no prefers-color-scheme rule), so the attribute is always what decides the look.
 *
 * root.tsx and use-theme.ts are consumers. Nothing else may parse or write the cookie or ask for the colour scheme.
 */

export const THEME_COOKIE = "roleradar_theme";

export type Theme = "light" | "dark";

const COOKIE_MAX_AGE_SECONDS = 31_536_000;
const DARK_SCHEME_QUERY = "(prefers-color-scheme: dark)";

/** The cookie name must be a whole cookie name (start of header or after `;`) and the value exactly light or dark. */
const COOKIE_PATTERN = new RegExp(`(?:^|;\\s*)${THEME_COOKIE}=(light|dark)(?=;|$)`);

/** The theme named by a Cookie header (or document.cookie), or undefined when absent or not exactly light or dark. */
export function parseThemeCookie(cookieHeader: string | null | undefined): Theme | undefined {
  if (!cookieHeader) return undefined;
  const match = COOKIE_PATTERN.exec(cookieHeader);
  return match ? (match[1] as Theme) : undefined;
}

/** The Set-Cookie style string the theme toggle writes: one year, lax, and Secure over https. */
export function serializeThemeCookie(theme: Theme, isHttps: boolean): string {
  const secure = isHttps ? "; Secure" : "";
  return `${THEME_COOKIE}=${theme}; Path=/; SameSite=Lax; Max-Age=${COOKIE_MAX_AGE_SECONDS}${secure}`;
}

/** `dark` when the media query matches, else `light`. */
export function systemTheme(query: { matches: boolean }): Theme {
  return query.matches ? "dark" : "light";
}

/** Writes `data-theme` on the document element. */
export function applyTheme(doc: Document, theme: Theme): void {
  doc.documentElement.setAttribute("data-theme", theme);
}

/** Browser only: the theme in document.cookie, if any. */
export function readCookieTheme(): Theme | undefined {
  if (typeof document === "undefined") return undefined;
  return parseThemeCookie(document.cookie);
}

/** Browser only: the theme currently on the document element, if it is a valid one. */
export function readAttributeTheme(): Theme | undefined {
  if (typeof document === "undefined") return undefined;
  const value = document.documentElement.getAttribute("data-theme");
  return value === "light" || value === "dark" ? value : undefined;
}

/** Browser only: the system colour scheme, or undefined when the browser cannot say. */
export function readSystemTheme(): Theme | undefined {
  if (typeof window === "undefined" || typeof window.matchMedia !== "function") return undefined;
  return systemTheme(window.matchMedia(DARK_SCHEME_QUERY));
}

/**
 * Browser only: calls `onChange` whenever the system colour scheme changes. Returns the function that stops it, or
 * undefined when the browser has no matchMedia. The caller decides whether a change still matters (it does not once
 * the owner has picked a theme and a cookie exists).
 */
export function watchSystemTheme(onChange: (theme: Theme) => void): (() => void) | undefined {
  if (typeof window === "undefined" || typeof window.matchMedia !== "function") return undefined;
  const query = window.matchMedia(DARK_SCHEME_QUERY);
  const handler = (event: MediaQueryListEvent) => onChange(systemTheme(event));
  query.addEventListener("change", handler);
  return () => query.removeEventListener("change", handler);
}

/** Browser only: stores the owner's choice. `isHttps` adds the Secure attribute. */
export function writeThemeCookie(theme: Theme, isHttps: boolean): void {
  document.cookie = serializeThemeCookie(theme, isHttps);
}

/**
 * The blocking inline script for the document head. It runs before the body paints: cookie first, then the system
 * scheme, then sets `data-theme`. Errors are swallowed so a locked-down browser still renders. Rendered with the
 * request's CSP nonce. Built from the same cookie name and pattern as the functions above.
 */
export const THEME_BOOTSTRAP_SCRIPT = `(function(){try{var m=document.cookie.match(/${COOKIE_PATTERN.source}/);var t=m?m[1]:(window.matchMedia('${DARK_SCHEME_QUERY}').matches?'dark':'light');document.documentElement.setAttribute('data-theme',t);}catch(e){}})();`;
