import React from "react";

const cx = (...parts) => parts.filter(Boolean).join(" ");

/**
 * Input — single-line text field. Always pair with a <label> (or aria-label).
 * `width` is "full" (default, fills its container), "xs", "sm" or "md" for a fixed-width field.
 * Styled by `.ds-input` in bundle.css; focus, invalid and disabled are pseudo-classes and
 * attributes, so no state is kept in script and nothing is inline.
 */
export function Input({ size = "md", invalid = false, disabled = false, leadingIcon = null, width, className, style, ...rest }) {
  const field = (
    <input
      {...rest}
      disabled={disabled}
      aria-invalid={invalid || undefined}
      className={cx("ds-input", className)}
      data-size={size}
      data-icon={leadingIcon ? "" : undefined}
      data-width={!leadingIcon && width ? width : undefined}
      style={style}
    />
  );

  if (!leadingIcon) return field;
  return (
    <span className="ds-field" data-width={width || undefined}>
      <span className="ds-field-icon">{leadingIcon}</span>
      {field}
    </span>
  );
}
