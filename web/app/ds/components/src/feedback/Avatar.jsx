import React from "react";

const sizes = { sm: "1.75rem", md: "2.5rem", lg: "3rem" };
const fontSizes = { sm: "var(--ds-text-xs)", md: "var(--ds-text-sm)", lg: "var(--ds-text-base)" };

function initials(name = "") {
  return name
    .trim()
    .split(/\s+/)
    .slice(0, 2)
    .map((w) => w[0])
    .join("")
    .toUpperCase();
}

/**
 * Avatar — a circular user image, falling back to initials on a tinted ground.
 */
export function Avatar({ src, name = "", size = "md", style = {}, ...rest }) {
  const dim = sizes[size];
  return (
    <span
      {...rest}
      title={name || undefined}
      style={{
        display: "inline-flex",
        alignItems: "center",
        justifyContent: "center",
        width: dim,
        height: dim,
        borderRadius: "var(--ds-radius-full)",
        background: "var(--ds-primary-subtle)",
        color: "var(--ds-primary-subtle-text)",
        fontFamily: "var(--ds-font-sans)",
        fontSize: fontSizes[size],
        fontWeight: "var(--ds-weight-semibold)",
        overflow: "hidden",
        flexShrink: 0,
        ...style,
      }}
    >
      {src ? (
        <img src={src} alt={name} style={{ width: "100%", height: "100%", objectFit: "cover" }} />
      ) : (
        initials(name)
      )}
    </span>
  );
}
