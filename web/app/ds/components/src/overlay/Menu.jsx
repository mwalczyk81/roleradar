import React from "react";

const cx = (...parts) => parts.filter(Boolean).join(" ");

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
export function Menu({ trigger, label, items = [], align = "start", variant = "secondary", linkComponent, className, style, ...rest }) {
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

  return (
    <div {...rest} ref={wrapRef} className={cx("ds-menu", className)} style={style}>
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
        data-variant={variant}
      >
        {trigger}
      </button>
      {open ? (
        <div
          id={menuId}
          role="menu"
          aria-label={label}
          onKeyDown={onMenuKeyDown}
          className="ds-menu-list"
          data-align={align === "end" ? "end" : "start"}
        >
          {items.map((it, i) => {
            if (it.separator) {
              return <div key={`sep-${i}`} role="separator" className="ds-menu-separator" />;
            }
            const common = {
              role: "menuitem",
              tabIndex: -1,
              className: "ds-menu-item",
              "data-disabled": it.disabled ? "" : undefined,
              "aria-disabled": it.disabled || undefined,
              "data-danger": it.danger ? "" : undefined,
              ref: (el) => {
                itemRefs.current[i] = el;
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
