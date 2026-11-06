import React from "react";

const cx = (...parts) => parts.filter(Boolean).join(" ");

/**
 * Select — native dropdown styled to match inputs. Always pair with a <label>.
 * Styled by `.ds-select` in bundle.css.
 */
export function Select({ size = "md", invalid = false, disabled = false, className, style, children, ...rest }) {
  return (
    <span className="ds-field">
      <select
        {...rest}
        disabled={disabled}
        aria-invalid={invalid || undefined}
        className={cx("ds-select", className)}
        data-size={size}
        style={style}
      >
        {children}
      </select>
      <svg
        className="ds-field-chevron"
        width="16"
        height="16"
        viewBox="0 0 24 24"
        fill="none"
        stroke="var(--ds-text-muted)"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
        aria-hidden="true"
      >
        <path d="m6 9 6 6 6-6" />
      </svg>
    </span>
  );
}
