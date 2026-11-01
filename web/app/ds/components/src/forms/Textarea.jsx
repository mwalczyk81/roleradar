import React from "react";

/**
 * Textarea — multi-line text field. Always pair with a <label>.
 */
export function Textarea({ invalid = false, disabled = false, rows = 4, style = {}, onFocus, onBlur, ...rest }) {
  const [focus, setFocus] = React.useState(false);
  const borderColor = invalid
    ? "var(--ds-danger)"
    : focus
    ? "var(--ds-focus-ring)"
    : "var(--ds-border-control)";

  return (
    <textarea
      {...rest}
      rows={rows}
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
        minHeight: "5rem",
        padding: "var(--ds-space-3)",
        background: "var(--ds-surface)",
        color: "var(--ds-text)",
        border: `var(--ds-border-thin) solid ${borderColor}`,
        outline: focus ? "var(--ds-border-thick) solid var(--ds-focus-ring)" : "none",
        borderRadius: "var(--ds-radius-md)",
        fontFamily: "var(--ds-font-sans)",
        fontSize: "var(--ds-text-sm)",
        lineHeight: "var(--ds-leading-normal)",
        resize: "vertical",
        boxSizing: "border-box",
        ...(disabled ? { opacity: 0.5, cursor: "not-allowed" } : null),
        ...style,
      }}
    />
  );
}
