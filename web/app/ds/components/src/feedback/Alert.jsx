import React from "react";

const cx = (...parts) => parts.filter(Boolean).join(" ");

const icons = {
  info: <path d="M12 16v-4M12 8h.01" />,
  success: <path d="M20 6 9 17l-5-5" />,
  warning: <path d="M12 9v4M12 17h.01M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0Z" />,
  danger: <path d="M12 8v4M12 16h.01M22 12a10 10 0 1 1-20 0 10 10 0 0 1 20 0Z" />,
};

/**
 * Alert — an inline banner conveying status. Variant carries meaning.
 * Warning and danger are announced assertively (role="alert"); info and success politely (role="status").
 * Variants are `[data-variant]` rules in bundle.css; foreground uses the -text tokens so title
 * text clears 4.5:1 on each subtle ground in both themes.
 */
export function Alert({ variant = "info", title, onClose, className, style, children, ...rest }) {
  const assertive = variant === "danger" || variant === "warning";
  return (
    <div role={assertive ? "alert" : "status"} {...rest} className={cx("ds-alert", className)} data-variant={variant} style={style}>
      <svg className="ds-alert-icon" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
        {icons[variant]}
      </svg>
      <div className="ds-alert-body">
        {title && <div className="ds-alert-title" data-gap={children ? "" : undefined}>{title}</div>}
        {children && <div className="ds-alert-text">{children}</div>}
      </div>
      {onClose && (
        <button type="button" className="ds-alert-close" onClick={onClose} aria-label="Dismiss">
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M18 6 6 18M6 6l12 12" /></svg>
        </button>
      )}
    </div>
  );
}
