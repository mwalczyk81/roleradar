import React from "react";

const sizes = {
  sm: { height: "var(--ds-control-sm)", fontSize: "var(--ds-text-sm)" },
  md: { height: "var(--ds-control-md)", fontSize: "var(--ds-text-sm)" },
  lg: { height: "var(--ds-control-lg)", fontSize: "var(--ds-text-base)" },
};

/**
 * Select — native dropdown styled to match inputs. Always pair with a <label>.
 */
export function Select({
  size = "md",
  invalid = false,
  disabled = false,
  style = {},
  onFocus,
  onBlur,
  children,
  ...rest
}) {
  const [focus, setFocus] = React.useState(false);
  const borderColor = invalid
    ? "var(--ds-danger)"
    : focus
    ? "var(--ds-focus-ring)"
    : "var(--ds-border-control)";

  return (
    <span style={{ position: "relative", display: "inline-block", width: "100%" }}>
      <select
        {...rest}
        disabled={disabled}
        aria-invalid={invalid || undefined}
        onFocus={(e) => {
          setFocus(true);
          onFocus && onFocus(e);
        }}
        onBlur={(e) => {
          setFocus(false);
          onBlur && onBlur(e);
        }}
        style={{
          width: "100%",
          height: sizes[size].height,
          padding: "0 var(--ds-space-8) 0 var(--ds-space-3)",
          background: "var(--ds-surface)",
          color: "var(--ds-text)",
          border: `var(--ds-border-thin) solid ${borderColor}`,
          outline: focus ? "var(--ds-border-thick) solid var(--ds-focus-ring)" : "none",
          borderRadius: "var(--ds-radius-md)",
          fontFamily: "var(--ds-font-sans)",
          fontSize: sizes[size].fontSize,
          appearance: "none",
          WebkitAppearance: "none",
          cursor: disabled ? "not-allowed" : "pointer",
          boxSizing: "border-box",
          ...(disabled ? { opacity: 0.5 } : null),
          ...style,
        }}
      >
        {children}
      </select>
      <svg
        width="16"
        height="16"
        viewBox="0 0 24 24"
        fill="none"
        stroke="var(--ds-text-muted)"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
        aria-hidden="true"
        style={{
          position: "absolute",
          right: "var(--ds-space-3)",
          top: "50%",
          transform: "translateY(-50%)",
          pointerEvents: "none",
        }}
      >
        <path d="m6 9 6 6 6-6" />
      </svg>
    </span>
  );
}
