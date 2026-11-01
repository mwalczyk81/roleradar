import React from "react";

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
 */
export function Toast({ variant = "info", title, message, action, onDismiss, duration = 5000, style = {}, onMouseEnter, onMouseLeave, onFocus, onBlur, ...rest }) {
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
      style={{
        display: "flex",
        alignItems: "flex-start",
        gap: "var(--ds-space-3)",
        width: "100%",
        maxWidth: "24rem",
        boxSizing: "border-box",
        padding: "var(--ds-space-3) var(--ds-space-4)",
        background: "var(--ds-surface)",
        color: "var(--ds-text)",
        border: "var(--ds-border-thin) solid var(--ds-border)",
        borderRadius: "var(--ds-radius-lg)",
        boxShadow: "var(--ds-shadow-md)",
        fontFamily: "var(--ds-font-sans)",
        fontSize: "var(--ds-text-sm)",
        lineHeight: "var(--ds-leading-normal)",
        pointerEvents: "auto",
        ...style,
      }}
    >
      <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke={tones[variant]} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" style={{ flexShrink: 0, marginTop: "1px" }}>
        {icons[variant]}
      </svg>
      <div style={{ flex: 1, minWidth: 0 }}>
        <div style={{ fontWeight: "var(--ds-weight-semibold)" }}>{title}</div>
        {message ? <div style={{ color: "var(--ds-text-secondary)", marginTop: "var(--ds-space-1)" }}>{message}</div> : null}
        {action ? (
          <button
            type="button"
            className="ds-icon-btn"
            onClick={action.onClick}
            style={{ marginTop: "var(--ds-space-2)", padding: "var(--ds-space-1) var(--ds-space-2)", marginLeft: "calc(var(--ds-space-2) * -1)", background: "none", border: "none", borderRadius: "var(--ds-radius-sm)", color: "var(--ds-primary-subtle-text)", fontFamily: "var(--ds-font-sans)", fontSize: "var(--ds-text-sm)", fontWeight: "var(--ds-weight-semibold)", cursor: "pointer" }}
          >
            {action.label}
          </button>
        ) : null}
      </div>
      {onDismiss ? (
        <button
          type="button"
          className="ds-icon-btn"
          onClick={onDismiss}
          aria-label="Dismiss notification"
          style={{ display: "inline-flex", padding: "var(--ds-space-1)", background: "none", border: "none", borderRadius: "var(--ds-radius-sm)", color: "var(--ds-text-secondary)", cursor: "pointer", flexShrink: 0 }}
        >
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M18 6 6 18M6 6l12 12" /></svg>
        </button>
      ) : null}
    </div>
  );
}
