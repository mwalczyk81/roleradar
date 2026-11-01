import React from "react";

const FOCUSABLE =
  'a[href], button:not([disabled]), textarea:not([disabled]), input:not([disabled]):not([type="hidden"]), select:not([disabled]), [tabindex]:not([tabindex="-1"])';

/**
 * Modal — centered dialog over a dimmed backdrop. Closes on Esc / backdrop click.
 * Focus moves into the dialog on open, Tab is trapped inside, focus returns to the
 * element that opened it on close, and page scroll is locked while open. Pass `title`
 * (labels the dialog) or `ariaLabel` when there is no visible title.
 */
export function Modal({ open, onClose, title, ariaLabel, footer, width = 480, style = {}, children }) {
  const dialogRef = React.useRef(null);
  const titleId = React.useId();

  // Focus, scroll lock, focus return. Depends only on `open` so a parent re-render
  // (a new onClose each render) never steals focus.
  React.useEffect(() => {
    if (!open) return;
    const previous = document.activeElement;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    if (dialogRef.current) dialogRef.current.focus();
    return () => {
      document.body.style.overflow = previousOverflow;
      if (previous && previous.focus) previous.focus();
    };
  }, [open]);

  React.useEffect(() => {
    if (!open) return;
    function onKey(e) {
      if (e.key === "Escape") onClose && onClose();
    }
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [open, onClose]);

  function trapTab(e) {
    if (e.key !== "Tab" || !dialogRef.current) return;
    const nodes = Array.from(dialogRef.current.querySelectorAll(FOCUSABLE));
    if (!nodes.length) {
      e.preventDefault();
      return;
    }
    const first = nodes[0];
    const last = nodes[nodes.length - 1];
    const current = document.activeElement;
    if (e.shiftKey && (current === first || current === dialogRef.current)) {
      e.preventDefault();
      last.focus();
    } else if (!e.shiftKey && current === last) {
      e.preventDefault();
      first.focus();
    }
  }

  if (!open) return null;

  return (
    <div
      onMouseDown={(e) => {
        if (e.target === e.currentTarget) onClose && onClose();
      }}
      style={{
        position: "fixed",
        inset: 0,
        zIndex: "var(--ds-z-modal)",
        background: "rgb(2 6 23 / 0.55)",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        padding: "var(--ds-space-4)",
      }}
    >
      <div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={title ? titleId : undefined}
        aria-label={!title ? ariaLabel : undefined}
        tabIndex={-1}
        onKeyDown={trapTab}
        style={{
          width: "100%",
          maxWidth: typeof width === "number" ? `${width}px` : width,
          background: "var(--ds-surface)",
          color: "var(--ds-text)",
          borderRadius: "var(--ds-radius-lg)",
          boxShadow: "var(--ds-shadow-lg)",
          fontFamily: "var(--ds-font-sans)",
          outline: "none",
          ...style,
        }}
      >
        {title && (
          <div
            style={{
              display: "flex",
              alignItems: "center",
              justifyContent: "space-between",
              gap: "var(--ds-space-4)",
              padding: "var(--ds-space-5) var(--ds-space-6)",
              borderBottom: "var(--ds-border-thin) solid var(--ds-border)",
            }}
          >
            <div id={titleId} style={{ fontSize: "var(--ds-text-lg)", fontWeight: "var(--ds-weight-semibold)" }}>{title}</div>
            <button
              type="button"
              onClick={onClose}
              aria-label="Close"
              style={{ background: "none", border: "none", color: "var(--ds-text-muted)", cursor: "pointer", display: "inline-flex", padding: 0 }}
            >
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M18 6 6 18M6 6l12 12" /></svg>
            </button>
          </div>
        )}
        <div style={{ padding: "var(--ds-space-6)", fontSize: "var(--ds-text-sm)", lineHeight: "var(--ds-leading-normal)" }}>
          {children}
        </div>
        {footer && (
          <div
            style={{
              display: "flex",
              justifyContent: "flex-end",
              gap: "var(--ds-space-3)",
              padding: "var(--ds-space-4) var(--ds-space-6)",
              borderTop: "var(--ds-border-thin) solid var(--ds-border)",
            }}
          >
            {footer}
          </div>
        )}
      </div>
    </div>
  );
}
