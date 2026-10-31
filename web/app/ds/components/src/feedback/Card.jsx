import React from "react";

/**
 * Card — the default surface container: surface bg, 1px border, sm shadow, lg radius.
 */
export function Card({ padded = true, interactive = false, style = {}, onMouseEnter, onMouseLeave, children, ...rest }) {
  const [hover, setHover] = React.useState(false);
  return (
    <div
      {...rest}
      onMouseEnter={(e) => {
        interactive && setHover(true);
        onMouseEnter && onMouseEnter(e);
      }}
      onMouseLeave={(e) => {
        interactive && setHover(false);
        onMouseLeave && onMouseLeave(e);
      }}
      style={{
        background: "var(--ds-surface)",
        border: "var(--ds-border-thin) solid var(--ds-border)",
        borderRadius: "var(--ds-radius-lg)",
        boxShadow: interactive && hover ? "var(--ds-shadow-md)" : "var(--ds-shadow-sm)",
        padding: padded ? "var(--ds-space-6)" : 0,
        transition: "box-shadow var(--ds-duration-base) var(--ds-ease-standard)",
        ...(interactive ? { cursor: "pointer" } : null),
        ...style,
      }}
    >
      {children}
    </div>
  );
}
