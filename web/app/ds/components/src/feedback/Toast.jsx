import React from "react";

const cx = (...parts) => parts.filter(Boolean).join(" ");

const tones = {
  info: "var(--ds-primary-subtle-text)",
  success: "var(--ds-success-text)",
  warning: "var(--ds-warning-text)",
  danger: "var(--ds-danger-text)",
};

const icons = {
  info: <path d="M12 16v-4M12 8h.01M22 12a10 10 0 1 1-20 0 10 10 0 0 1 20 0Z" />,
  success: <path d="M20 6 9 17l-5-5" />,
  warning: <path d="M12 9v4M12 17h.01M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0Z" />,
  danger: <path d="M12 8v4M12 16h.01M22 12a10 10 0 1 1-20 0 10 10 0 0 1 20 0Z" />,
};

/**
 * Toast — a short notification. Warning and danger toasts are announced assertively
 * (role="alert"), info and success politely (role="status"). It dismisses itself after
 * `duration` ms (default 5000) unless it has an `action` or `duration` is 0, and it stays
 * while the pointer is over it or focus is inside it. `action` is { label, onClick }.
 * Put toasts inside a ToastRegion so they stack in a corner of the screen.
 * Styled by `.ds-toast*` in bundle.css.
 */
export function Toast({ variant = "info", title, message, action, onDismiss, duration = 5000, className, style, onMouseEnter, onMouseLeave, onFocus, onBlur, ...rest }) {
  const [paused, setPaused] = React.useState(false);
  const sticky = !!action || duration <= 0 || !onDismiss;

  React.useEffect(() => {
    if (sticky || paused) return;
    const t = setTimeout(() => onDismiss(), duration);
    return () => clearTimeout(t);
  }, [sticky, paused, duration, onDismiss]);

  const assertive = variant === "danger" || variant === "warning";
  return (
    <div
      {...rest}
      role={assertive ? "alert" : "status"}
      className={cx("ds-toast", className)}
      onMouseEnter={(e) => {
        setPaused(true);
        onMouseEnter && onMouseEnter(e);
      }}
      onMouseLeave={(e) => {
        setPaused(false);
        onMouseLeave && onMouseLeave(e);
      }}
      onFocus={(e) => {
        setPaused(true);
        onFocus && onFocus(e);
      }}
      onBlur={(e) => {
        setPaused(false);
        onBlur && onBlur(e);
      }}
      style={style}
    >
      <svg className="ds-toast-icon" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke={tones[variant]} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
        {icons[variant]}
      </svg>
      <div className="ds-toast-body">
        <div className="ds-toast-title">{title}</div>
        {message ? <div className="ds-toast-message">{message}</div> : null}
        {action ? (
          <button type="button" className="ds-icon-btn ds-toast-action" onClick={action.onClick}>
            {action.label}
          </button>
        ) : null}
      </div>
      {onDismiss ? (
        <button type="button" className="ds-icon-btn ds-toast-close" onClick={onDismiss} aria-label="Dismiss notification">
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M18 6 6 18M6 6l12 12" /></svg>
        </button>
      ) : null}
    </div>
  );
}
