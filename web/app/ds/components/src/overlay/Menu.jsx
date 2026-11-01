import React from "react";

/**
 * Menu — a dropdown menu opened from a button. `trigger` is the button's content and `label`
 * its accessible name (required when the trigger is only an icon). `items` is a list of
 * { label, onClick, href, danger, disabled } or { separator: true }. Items with an `href`
 * render as links, using `linkComponent` (a router Link) when given.
 *
 * Keyboard: Enter, Space or ArrowDown on the button opens the menu on the first item,
 * ArrowUp on the last. Inside: Up/Down/Home/End move, a letter jumps to the next item that
 * starts with it, Escape closes and returns focus to the button, Tab closes. Clicking outside
 * closes. Selecting an item runs its handler, closes the menu and returns focus to the button.
 */
export function Menu({ trigger, label, items = [], align = "start", variant = "secondary", linkComponent, style = {}, ...rest }) {
  const [open, setOpen] = React.useState(false);
  const [request, setRequest] = React.useState(null); // which item to focus after opening
  const wrapRef = React.useRef(null);
  const triggerRef = React.useRef(null);
  const itemRefs = React.useRef([]);
  const menuId = React.useId();
  const Link = linkComponent || "a";
  const isPlain = typeof Link === "string";

  const enabled = items.map((it, i) => (it.separator || it.disabled ? -1 : i)).filter((i) => i >= 0);

  function openMenu(where) {
    setRequest(where);
    setOpen(true);
  }

  function closeMenu(restore) {
    setOpen(false);
    setRequest(null);
    if (restore && triggerRef.current) triggerRef.current.focus();
  }

  React.useEffect(() => {
    if (!open || !request || !enabled.length) return;
    const target = request === "last" ? enabled[enabled.length - 1] : enabled[0];
    const el = itemRefs.current[target];
    if (el) el.focus();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, request]);

  React.useEffect(() => {
    if (!open) return;
    function onDown(e) {
      if (wrapRef.current && !wrapRef.current.contains(e.target)) closeMenu(false);
    }
    document.addEventListener("mousedown", onDown);
    return () => document.removeEventListener("mousedown", onDown);
  }, [open]);

  function focusItem(index) {
    const el = itemRefs.current[index];
    if (el) el.focus();
  }

  function onMenuKeyDown(e) {
    if (!enabled.length) return;
    const current = itemRefs.current.findIndex((el) => el === document.activeElement);
    const pos = enabled.indexOf(current);
    if (e.key === "ArrowDown") {
      e.preventDefault();
      focusItem(enabled[(pos + 1) % enabled.length]);
    } else if (e.key === "ArrowUp") {
      e.preventDefault();
      focusItem(enabled[(pos - 1 + enabled.length) % enabled.length]);
    } else if (e.key === "Home") {
      e.preventDefault();
      focusItem(enabled[0]);
    } else if (e.key === "End") {
      e.preventDefault();
      focusItem(enabled[enabled.length - 1]);
    } else if (e.key === "Escape") {
      e.preventDefault();
      e.stopPropagation();
      closeMenu(true);
    } else if (e.key === "Tab") {
      closeMenu(true);
    } else if (e.key.length === 1 && !e.ctrlKey && !e.metaKey && !e.altKey) {
      const ch = e.key.toLowerCase();
      const order = [...enabled.slice(pos + 1), ...enabled.slice(0, pos + 1)];
      const hit = order.find((i) => String(items[i].label).toLowerCase().startsWith(ch));
      if (hit !== undefined) focusItem(hit);
    }
  }

  const triggerStyle = {
    display: "inline-flex",
    alignItems: "center",
    justifyContent: "center",
    gap: "var(--ds-space-2)",
    height: "var(--ds-control-md)",
    minWidth: "var(--ds-control-md)",
    padding: "0 var(--ds-space-3)",
    background: variant === "secondary" ? "var(--ds-surface)" : "transparent",
    color: "var(--ds-text)",
    border: `var(--ds-border-thin) solid ${variant === "secondary" ? "var(--ds-border-control)" : "transparent"}`,
    borderRadius: "var(--ds-radius-md)",
    fontFamily: "var(--ds-font-sans)",
    fontSize: "var(--ds-text-sm)",
    fontWeight: "var(--ds-weight-medium)",
    cursor: "pointer",
  };

  return (
    <div {...rest} ref={wrapRef} style={{ position: "relative", display: "inline-block", ...style }}>
      <button
        ref={triggerRef}
        type="button"
        className="ds-menu-trigger"
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={open ? menuId : undefined}
        aria-label={label}
        onClick={() => (open ? closeMenu(false) : openMenu("first"))}
        onKeyDown={(e) => {
          if (e.key === "ArrowDown") {
            e.preventDefault();
            openMenu("first");
          } else if (e.key === "ArrowUp") {
            e.preventDefault();
            openMenu("last");
          }
        }}
        style={triggerStyle}
      >
        {trigger}
      </button>
      {open ? (
        <div
          id={menuId}
          role="menu"
          aria-label={label}
          onKeyDown={onMenuKeyDown}
          style={{
            position: "absolute",
            top: "calc(100% + var(--ds-space-1))",
            [align === "end" ? "right" : "left"]: 0,
            zIndex: "var(--ds-z-dropdown)",
            minWidth: "12rem",
            padding: "var(--ds-space-1)",
            background: "var(--ds-surface)",
            border: "var(--ds-border-thin) solid var(--ds-border)",
            borderRadius: "var(--ds-radius-lg)",
            boxShadow: "var(--ds-shadow-md)",
            fontFamily: "var(--ds-font-sans)",
          }}
        >
          {items.map((it, i) => {
            if (it.separator) {
              return <div key={`sep-${i}`} role="separator" style={{ height: 0, margin: "var(--ds-space-1) 0", borderTop: "var(--ds-border-thin) solid var(--ds-border)" }} />;
            }
            const common = {
              role: "menuitem",
              tabIndex: -1,
              className: "ds-menu-item",
              "aria-disabled": it.disabled || undefined,
              "data-danger": it.danger ? "" : undefined,
              ref: (el) => {
                itemRefs.current[i] = el;
              },
              style: {
                display: "flex",
                alignItems: "center",
                width: "100%",
                boxSizing: "border-box",
                padding: "var(--ds-space-2) var(--ds-space-3)",
                background: "none",
                border: "none",
                borderRadius: "var(--ds-radius-md)",
                textAlign: "left",
                textDecoration: "none",
                fontFamily: "var(--ds-font-sans)",
                fontSize: "var(--ds-text-sm)",
                color: it.danger ? "var(--ds-danger-text)" : "var(--ds-text)",
                cursor: it.disabled ? "not-allowed" : "pointer",
                opacity: it.disabled ? 0.5 : 1,
              },
              onClick: (e) => {
                if (it.disabled) {
                  e.preventDefault();
                  return;
                }
                it.onClick && it.onClick(e);
                closeMenu(!it.href);
              },
            };
            return it.href && !it.disabled ? (
              <Link key={it.label + i} {...(isPlain ? { href: it.href } : { href: it.href, to: it.href })} {...common}>
                {it.label}
              </Link>
            ) : (
              <button key={it.label + i} type="button" {...common}>
                {it.label}
              </button>
            );
          })}
        </div>
      ) : null}
    </div>
  );
}
