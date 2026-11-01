import * as React from "react";

export function useTheme(initialTheme?: "light" | "dark") {
  const [theme, setTheme] = React.useState<"light" | "dark">(() => {
    if (typeof document !== "undefined") {
      const attr = document.documentElement.getAttribute("data-theme") as "light" | "dark" | null;
      if (attr === "light" || attr === "dark") return attr;
      if (window.matchMedia?.("(prefers-color-scheme: dark)").matches) return "dark";
    }
    return initialTheme ?? "light";
  });

  React.useEffect(() => {
    const getCookieTheme = () => {
      const match = document.cookie.match(/roleradar_theme=(light|dark)/);
      return match ? (match[1] as "light" | "dark") : null;
    };

    // If no cookie set, sync with current DOM or system and listen for live changes
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

  const toggleTheme = React.useCallback(() => {
    setTheme((prevTheme) => {
      const nextTheme = prevTheme === "dark" ? "light" : "dark";
      if (typeof document !== "undefined") {
        document.documentElement.setAttribute("data-theme", nextTheme);
        const isHttps = typeof window !== "undefined" && window.location.protocol === "https:";
        const secureAttr = isHttps ? "; Secure" : "";
        document.cookie = `roleradar_theme=${nextTheme}; Path=/; SameSite=Lax; Max-Age=31536000${secureAttr}`;
      }
      return nextTheme;
    });
  }, []);

  return { theme, toggleTheme };
}
