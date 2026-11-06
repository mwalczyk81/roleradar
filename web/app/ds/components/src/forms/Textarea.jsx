import React from "react";

const cx = (...parts) => parts.filter(Boolean).join(" ");

/**
 * Textarea — multi-line text field. Always pair with a <label>.
 * Styled by `.ds-textarea` in bundle.css.
 */
export function Textarea({ invalid = false, disabled = false, rows = 4, className, style, ...rest }) {
  return (
    <textarea
      {...rest}
      rows={rows}
      disabled={disabled}
      aria-invalid={invalid || undefined}
      className={cx("ds-textarea", className)}
      style={style}
    />
  );
}
