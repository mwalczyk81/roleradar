import React from "react";

const cx = (...parts) => parts.filter(Boolean).join(" ");

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
 * `size` is "sm", "md" or "lg" (`[data-size]` rules in bundle.css).
 */
export function Avatar({ src, name = "", size = "md", className, style, ...rest }) {
  return (
    <span {...rest} title={name || undefined} className={cx("ds-avatar", className)} data-size={size} style={style}>
      {src ? <img className="ds-avatar-img" src={src} alt={name} /> : initials(name)}
    </span>
  );
}
