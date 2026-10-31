import React from "react";

/**
 * ToastRegion — fixed stack in the bottom-right corner (full width on narrow screens) that
 * holds Toasts. Render it once near the root; add and remove Toast children as notifications
 * come and go. Newest last. The region itself is a labelled landmark region and does not
 * trap clicks outside its toasts.
 */
export function ToastRegion({ label = "Notifications", style = {}, children, ...rest }) {
  return (
    <div
      {...rest}
      role="region"
      aria-label={label}
      style={{
        position: "fixed",
        right: "var(--ds-space-4)",
        bottom: "var(--ds-space-4)",
        left: "var(--ds-space-4)",
        zIndex: "var(--ds-z-toast)",
        display: "flex",
        flexDirection: "column",
        alignItems: "flex-end",
        gap: "var(--ds-space-3)",
        pointerEvents: "none",
        ...style,
      }}
    >
      {children}
    </div>
  );
}
