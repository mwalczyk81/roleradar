import React from "react";

const base = {
  display: "inline-flex",
  alignItems: "center",
  justifyContent: "center",
  gap: "var(--ds-space-2)",
  border: "var(--ds-border-thin) solid transparent",
  borderRadius: "var(--ds-radius-md)",
  fontFamily: "var(--ds-font-sans)",
  fontWeight: "var(--ds-weight-medium)",
  lineHeight: 1.2,
  whiteSpace: "nowrap",
  cursor: "pointer",
  transition:
    "background var(--ds-duration-fast) var(--ds-ease-standard), border-color var(--ds-duration-fast) var(--ds-ease-standard), filter var(--ds-duration-fast) var(--ds-ease-standard)",
};

const sizes = {
  sm: { height: "var(--ds-control-sm)", padding: "0 var(--ds-space-3)", fontSize: "var(--ds-text-sm)" },
  md: { height: "var(--ds-control-md)", padding: "0 var(--ds-space-4)", fontSize: "var(--ds-text-sm)" },
  lg: { height: "var(--ds-control-lg)", padding: "0 var(--ds-space-5)", fontSize: "var(--ds-text-base)" },
};

// Filled buttons use the -solid tokens so white text clears 4.5:1 in both themes.
const variants = {
  primary: { background: "var(--ds-primary-solid)", color: "var(--ds-text-on-accent)" },
  secondary: {
    background: "var(--ds-surface)",
    color: "var(--ds-text)",
    borderColor: "var(--ds-border-control)",
  },
  ghost: { background: "transparent", color: "var(--ds-text)" },
  danger: { background: "var(--ds-danger-solid)", color: "var(--ds-text-on-accent)" },
};

/**
 * Button — the primary interactive control. One primary action per view;
 * everything else is secondary / ghost.
 */
export function Button({
  variant = "primary",
  size = "md",
  type = "button",
  disabled = false,
  fullWidth = false,
  leadingIcon = null,
  trailingIcon = null,
  style = {},
  onMouseEnter,
  onMouseLeave,
  children,
  ...rest
}) {
  const [hover, setHover] = React.useState(false);

  let hoverStyle = {};
  if (hover && !disabled) {
    if (variant === "primary") hoverStyle = { background: "var(--ds-primary-solid-hover)" };
    else if (variant === "secondary") hoverStyle = { background: "var(--ds-surface-hover)" };
    else if (variant === "ghost") hoverStyle = { background: "var(--ds-surface-hover)" };
    else if (variant === "danger") hoverStyle = { filter: "brightness(0.95)" };
  }

  return (
    <button
      {...rest}
      type={type}
      disabled={disabled}
      onMouseEnter={(e) => {
        setHover(true);
        onMouseEnter && onMouseEnter(e);
      }}
      onMouseLeave={(e) => {
        setHover(false);
        onMouseLeave && onMouseLeave(e);
      }}
      style={{
        ...base,
        ...sizes[size],
        ...variants[variant],
        ...(fullWidth ? { width: "100%" } : null),
        ...(disabled ? { opacity: 0.5, cursor: "not-allowed" } : null),
        ...hoverStyle,
        ...style,
      }}
    >
      {leadingIcon}
      {children}
      {trailingIcon}
    </button>
  );
}
