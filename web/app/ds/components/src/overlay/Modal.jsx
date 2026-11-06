import React from "react";

const cx = (...parts) => parts.filter(Boolean).join(" ");

const FOCUSABLE =
  'a[href], button:not([disabled]), textarea:not([disabled]), input:not([disabled]):not([type="hidden"]), select:not([disabled]), [tabindex]:not([tabindex="-1"])';

/**
 * Modal — centered dialog over a dimmed backdrop. Closes on Esc / backdrop click.
 * Focus moves into the dialog on open, Tab is trapped inside, focus returns to the
 * element that opened it on close, and page scroll is locked while open. Pass `title`
 * (labels the dialog) or `ariaLabel` when there is no visible title. `width` is the dialog's
 * maximum width (a number is px); it is applied after mount, the dialog is only ever opened by script.
 */
export function Modal({ open, onClose, title, ariaLabel, footer, width = 480, className, style, children }) {
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

  React.useLayoutEffect(() => {
    if (open && dialogRef.current) dialogRef.current.style.maxWidth = typeof width === "number" ? `${width}px` : width;
  }, [open, width]);

  if (!open) return null;

  return (
    <div
      className="ds-modal-backdrop"
      onMouseDown={(e) => {
        if (e.target === e.currentTarget) onClose && onClose();
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
        className={cx("ds-modal", className)}
        style={style}
      >
        {title && (
          <div className="ds-modal-header">
            <div id={titleId} className="ds-modal-title">{title}</div>
            <button type="button" onClick={onClose} aria-label="Close" className="ds-modal-close">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M18 6 6 18M6 6l12 12" /></svg>
            </button>
          </div>
        )}
        <div className="ds-modal-body">{children}</div>
        {footer && <div className="ds-modal-footer">{footer}</div>}
      </div>
    </div>
  );
}
