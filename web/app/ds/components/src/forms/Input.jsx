import React from "react";

const sizes = {
  sm: { height: "var(--ds-control-sm)", fontSize: "var(--ds-text-sm)" },
  md: { height: "var(--ds-control-md)", fontSize: "var(--ds-text-sm)" },
  lg: { height: "var(--ds-control-lg)", fontSize: "var(--ds-text-base)" },
};

/**
 * Input — single-line text field. Always pair with a <label> (or aria-label).
 */
export function Input({
  size = "md",
  invalid = false,
  disabled = false,
  leadingIcon = null,
  style = {},
  onFocus,
  onBlur,
  ...rest
}) {
  const [focus, setFocus] = React.useState(false);
  const borderColor = invalid
    ? "var(--ds-danger)"
    : focus
    ? "var(--ds-focus-ring)"
    : "var(--ds-border-control)";

  const field = (
    <input
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
        padding: leadingIcon
          ? "0 var(--ds-space-3) 0 var(--ds-space-8)"
          : "0 var(--ds-space-3)",
        background: "var(--ds-surface)",
        color: "var(--ds-text)",
        border: `var(--ds-border-thin) solid ${borderColor}`,
        outline: focus ? "var(--ds-border-thick) solid var(--ds-focus-ring)" : "none",
        outlineOffset: 0,
        borderRadius: "var(--ds-radius-md)",
        fontFamily: "var(--ds-font-sans)",
        fontSize: sizes[size].fontSize,
        boxSizing: "border-box",
        ...(disabled ? { opacity: 0.5, cursor: "not-allowed" } : null),
        ...style,
      }}
    />
  );

  if (!leadingIcon) return field;
  return (
    <span style={{ position: "relative", display: "inline-block", width: "100%" }}>
      <span
        style={{
          position: "absolute",
          left: "var(--ds-space-3)",
          top: "50%",
          transform: "translateY(-50%)",
          display: "inline-flex",
          color: "var(--ds-text-muted)",
          pointerEvents: "none",
        }}
      >
        {leadingIcon}
      </span>
      {field}
    </span>
  );
}
