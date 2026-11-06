import React from "react";

const cx = (...parts) => parts.filter(Boolean).join(" ");

/**
 * ToastRegion — fixed stack in the bottom-right corner (full width on narrow screens) that
 * holds Toasts. Render it once near the root; add and remove Toast children as notifications
 * come and go. Newest last. The region itself is a labelled landmark region and does not
 * trap clicks outside its toasts. Styled by `.ds-toast-region` in bundle.css.
 */
export function ToastRegion({ label = "Notifications", className, style, children, ...rest }) {
  return (
    <div {...rest} role="region" aria-label={label} className={cx("ds-toast-region", className)} style={style}>
      {children}
    </div>
  );
}
