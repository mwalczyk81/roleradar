import React from "react";

const cx = (...parts) => parts.filter(Boolean).join(" ");

/**
 * Card — the default surface container: surface bg, 1px border, sm shadow, lg radius.
 * `padding` is "none", "sm" (space-4), "md" (space-5) or "lg" (space-6, the default); it
 * wins over `padded`, which stays for compatibility (`padded={false}` is `padding="none"`).
 * Styled by `.ds-card` in bundle.css; the interactive hover lift is a pseudo-class.
 */
export function Card({ padded = true, padding, interactive = false, className, style, children, ...rest }) {
  const pad = padding || (padded ? "lg" : "none");
  return (
    <div {...rest} className={cx("ds-card", className)} data-padding={pad} data-interactive={interactive ? "" : undefined} style={style}>
      {children}
    </div>
  );
}
