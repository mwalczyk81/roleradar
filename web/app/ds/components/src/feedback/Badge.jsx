import React from "react";

const cx = (...parts) => parts.filter(Boolean).join(" ");

/**
 * Badge — a small pill for status or counts. Use status tones for status only.
 * Tones are `[data-tone]` rules in bundle.css; text uses the -text tokens so every tone
 * clears 4.5:1 on its subtle ground in both themes.
 */
export function Badge({ tone = "primary", dot = false, className, style, children, ...rest }) {
  return (
    <span {...rest} className={cx("ds-badge", className)} data-tone={tone} style={style}>
      {dot && <span className="ds-badge-dot" aria-hidden="true" />}
      {children}
    </span>
  );
}
