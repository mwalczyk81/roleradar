import * as React from "react";
import {
  applyTheme,
  readAttributeTheme,
  readCookieTheme,
  readSystemTheme,
  watchSystemTheme,
  writeThemeCookie,
  type Theme,
} from "./theme.js";

/**
 * What the first render shows. On the server there is no document, so it is the theme the loader read from the cookie
 * (or light). In the browser it is the cookie, then the attribute the head script set, then a dark system, then the
 * given theme, then light.
 */
function initialThemeFor(initialTheme: Theme | undefined): Theme {
  if (typeof document === "undefined") return initialTheme ?? "light";
  return readCookieTheme() ?? readAttributeTheme() ?? (readSystemTheme() === "dark" ? "dark" : undefined) ?? initialTheme ?? "light";
}

export function useTheme(initialTheme?: Theme) {
  const [theme, setTheme] = React.useState<Theme>(() => initialThemeFor(initialTheme));

  React.useEffect(() => {
    // Once the owner has chosen a theme (a cookie exists) the system setting no longer matters.
    if (readCookieTheme()) return;

    const system = readSystemTheme();
    if (!system) return;
    setTheme(system);
    applyTheme(document, system);

    return watchSystemTheme((next) => {
      if (readCookieTheme()) return;
      setTheme(next);
      applyTheme(document, next);
    });
  }, []);

  const toggleTheme = React.useCallback(() => {
    const next: Theme = theme === "dark" ? "light" : "dark";
    applyTheme(document, next);
    writeThemeCookie(next, window.location.protocol === "https:");
    setTheme(next);
  }, [theme]);

  return { theme, toggleTheme };
}
