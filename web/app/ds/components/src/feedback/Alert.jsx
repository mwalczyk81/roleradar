import React from "react";

// Foreground uses the -text tokens so title text clears 4.5:1 on each subtle ground in both themes.
const variants = {
  info: { bg: "var(--ds-primary-subtle)", fg: "var(--ds-primary-subtle-text)" },
  success: { bg: "var(--ds-success-subtle)", fg: "var(--ds-success-text)" },
  warning: { bg: "var(--ds-warning-subtle)", fg: "var(--ds-warning-text)" },
  danger: { bg: "var(--ds-danger-subtle)", fg: "var(--ds-danger-text)" },
};

const icons = {
  info: <path d="M12 16v-4M12 8h.01" />,
  success: <path d="M20 6 9 17l-5-5" />,
  warning: <path d="M12 9v4M12 17h.01M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0Z" />,
  danger: <path d="M12 8v4M12 16h.01M22 12a10 10 0 1 1-20 0 10 10 0 0 1 20 0Z" />,
};

/**
 * Alert — an inline banner conveying status. Variant carries meaning.
 * Warning and danger are announced assertively (role="alert"); info and success politely (role="status").
 */
export function Alert({ variant = "info", title, onClose, style = {}, children, ...rest }) {
  const v = variants[variant];
  const assertive = variant === "danger" || variant === "warning";
  return (
    <div
      role={assertive ? "alert" : "status"}
      {...rest}
      style={{
        display: "flex",
        gap: "var(--ds-space-3)",
        padding: "var(--ds-space-3) var(--ds-space-4)",
        borderRadius: "var(--ds-radius-md)",
        background: v.bg,
        color: v.fg,
        fontFamily: "var(--ds-font-sans)",
        fontSize: "var(--ds-text-sm)",
        lineHeight: "var(--ds-leading-normal)",
        ...style,
      }}
    >
      <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" style={{ flexShrink: 0, marginTop: "1px" }}>
        {icons[variant]}
      </svg>
      <div style={{ flex: 1 }}>
        {title && <div style={{ fontWeight: "var(--ds-weight-semibold)", marginBottom: children ? "var(--ds-space-1)" : 0 }}>{title}</div>}
        {children && <div style={{ color: "var(--ds-text)" }}>{children}</div>}
      </div>
      {onClose && (
        <button
          type="button"
          onClick={onClose}
          aria-label="Dismiss"
          style={{ background: "none", border: "none", color: "currentColor", cursor: "pointer", padding: 0, display: "inline-flex", flexShrink: 0 }}
        >
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M18 6 6 18M6 6l12 12" /></svg>
        </button>
      )}
    </div>
  );
}
