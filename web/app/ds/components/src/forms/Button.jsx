import React from "react";

const cx = (...parts) => parts.filter(Boolean).join(" ");

/**
 * Button — the primary interactive control. One primary action per view;
 * everything else is secondary / ghost. Styled by `.ds-btn` in bundle.css (variant,
 * size and hover are data attributes and pseudo-classes, so nothing is inline).
 */
export function Button({
  variant = "primary",
  size = "md",
  type = "button",
  disabled = false,
  fullWidth = false,
  leadingIcon = null,
  trailingIcon = null,
  className,
  style,
  children,
  ...rest
}) {
  return (
    <button
      {...rest}
      type={type}
      disabled={disabled}
      className={cx("ds-btn", className)}
      data-variant={variant}
      data-size={size}
      data-full={fullWidth ? "" : undefined}
      style={style}
    >
      {leadingIcon}
      {children}
      {trailingIcon}
    </button>
  );
}
