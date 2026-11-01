import React from "react";

// Text uses the -text tokens so every tone clears 4.5:1 on its subtle ground in both themes.
const tones = {
  neutral: { background: "var(--ds-surface-hover)", color: "var(--ds-text-secondary)" },
  primary: { background: "var(--ds-primary-subtle)", color: "var(--ds-primary-subtle-text)" },
  success: { background: "var(--ds-success-subtle)", color: "var(--ds-success-text)" },
  warning: { background: "var(--ds-warning-subtle)", color: "var(--ds-warning-text)" },
  danger: { background: "var(--ds-danger-subtle)", color: "var(--ds-danger-text)" },
};

/**
 * Badge — a small pill for status or counts. Use status tones for status only.
 */
export function Badge({ tone = "primary", dot = false, style = {}, children, ...rest }) {
  return (
    <span
      {...rest}
      style={{
        display: "inline-flex",
        alignItems: "center",
        gap: "var(--ds-space-1)",
        padding: "var(--ds-space-1) var(--ds-space-2)",
        borderRadius: "var(--ds-radius-full)",
        fontFamily: "var(--ds-font-sans)",
        fontSize: "var(--ds-text-xs)",
        fontWeight: "var(--ds-weight-medium)",
        lineHeight: 1.4,
        whiteSpace: "nowrap",
        ...tones[tone],
        ...style,
      }}
    >
      {dot && (
        <span
          aria-hidden="true"
          style={{
            width: "0.375rem",
            height: "0.375rem",
            borderRadius: "var(--ds-radius-full)",
            background: "currentColor",
          }}
        />
      )}
      {children}
    </span>
  );
}
