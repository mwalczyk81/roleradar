import React from "react";

/**
 * AppShell — page frame: sidebar navigation, top bar with the page title, and the main area.
 *
 * `navItems` is a list of { label, href, icon, active, badge }. Pass `linkComponent` (a router
 * Link or NavLink) to get client-side navigation; the default is a plain anchor. The link
 * component receives `href` and `to`. Icons arrive as nodes — the kit imports no icon package.
 * `title` becomes the page's h1. `theme` ("light" | "dark") with `onThemeToggle` shows a theme
 * button; `userLabel` shows who is signed in; `signOut` is a node (a form button, say) so the
 * app controls how sign-out is submitted. `headerActions` adds more buttons to the top bar.
 * A skip link jumps to the main area; below 48rem the sidebar folds behind a menu button.
 */
export function AppShell({
  brand = "App",
  navItems = [],
  linkComponent,
  title,
  userLabel,
  signOut,
  theme,
  onThemeToggle,
  headerActions,
  children,
  className,
  style,
  ...rest
}) {
  const [open, setOpen] = React.useState(false);
  const navId = React.useId();
  const Link = linkComponent || "a";
  const isPlain = typeof Link === "string";

  React.useEffect(() => {
    if (!open) return;
    function onKey(e) {
      if (e.key === "Escape") setOpen(false);
    }
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [open]);

  return (
    <div {...rest} className={["ds-appshell", className].filter(Boolean).join(" ")} data-nav-open={open ? "true" : "false"} style={style}>
      <a className="ds-skip-link" href="#ds-main-content">Skip to main content</a>
      <aside className="ds-appshell-sidebar">
        <div className="ds-appshell-brand">{brand}</div>
        <nav id={navId} aria-label="Primary">
          <ul className="ds-appshell-list">
            {navItems.map((it) => (
              <li key={it.href}>
                <Link
                  {...(isPlain ? { href: it.href } : { href: it.href, to: it.href })}
                  className="ds-appshell-link"
                  aria-current={it.active ? "page" : undefined}
                  onClick={() => setOpen(false)}
                >
                  {it.icon ? <span className="ds-appshell-icon" aria-hidden="true">{it.icon}</span> : null}
                  <span className="ds-appshell-label">{it.label}</span>
                  {it.badge !== undefined && it.badge !== null ? <span className="ds-appshell-badge">{it.badge}</span> : null}
                </Link>
              </li>
            ))}
          </ul>
        </nav>
      </aside>
      <header className="ds-appshell-topbar">
        <button
          type="button"
          className="ds-appshell-menu ds-icon-btn"
          aria-label={open ? "Close navigation" : "Open navigation"}
          aria-expanded={open}
          aria-controls={navId}
          onClick={() => setOpen((v) => !v)}
        >
          <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
            {open ? <path d="M18 6 6 18M6 6l12 12" /> : <path d="M4 6h16M4 12h16M4 18h16" />}
          </svg>
        </button>
        <h1 className="ds-appshell-title">{title}</h1>
        <div className="ds-appshell-actions">
          {headerActions}
          {onThemeToggle ? (
            <button
              type="button"
              className="ds-icon-btn ds-appshell-theme"
              onClick={onThemeToggle}
              aria-label={theme === "dark" ? "Switch to light theme" : "Switch to dark theme"}
              title={theme === "dark" ? "Switch to light theme" : "Switch to dark theme"}
            >
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                {theme === "dark" ? <><circle cx="12" cy="12" r="4" /><path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4" /></> : <path d="M12 3a6 6 0 0 0 9 9 9 9 0 1 1-9-9Z" />}
              </svg>
            </button>
          ) : null}
          {userLabel ? <span className="ds-appshell-user" title={userLabel}>{userLabel}</span> : null}
          {signOut}
        </div>
      </header>
      <main id="ds-main-content" tabIndex={-1} className="ds-appshell-main">
        {children}
      </main>
    </div>
  );
}
