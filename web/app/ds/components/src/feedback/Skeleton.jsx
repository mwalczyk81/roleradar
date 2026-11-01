import React from "react";

const block = (extra) => ({ borderRadius: "var(--ds-radius-sm)", ...extra });

/**
 * Skeleton — placeholder shapes while content loads. The whole group is announced once as
 * "Loading" (override with `label`); the shapes are hidden from assistive tech. Variants:
 * "text" (lines), "table" (header + rows), "card" (a Card-shaped block), "block" (one box
 * sized with `width` and `height`). The pulse stops under prefers-reduced-motion.
 */
export function Skeleton({ variant = "text", rows = 3, columns = 4, width, height, label = "Loading", style = {}, ...rest }) {
  let shapes;
  if (variant === "table") {
    shapes = (
      <div style={{ display: "flex", flexDirection: "column", gap: "var(--ds-space-3)" }}>
        {Array.from({ length: rows + 1 }, (_, r) => (
          <div key={r} style={{ display: "grid", gridTemplateColumns: `repeat(${columns}, minmax(0, 1fr))`, gap: "var(--ds-space-4)" }}>
            {Array.from({ length: columns }, (_, c) => (
              <span key={c} className="ds-skeleton-block" style={block({ height: r === 0 ? "0.75rem" : "1rem", width: r === 0 ? "50%" : c === 0 ? "80%" : "60%" })} />
            ))}
          </div>
        ))}
      </div>
    );
  } else if (variant === "card") {
    shapes = (
      <div style={{ background: "var(--ds-surface)", border: "var(--ds-border-thin) solid var(--ds-border)", borderRadius: "var(--ds-radius-lg)", padding: "var(--ds-space-6)", display: "flex", flexDirection: "column", gap: "var(--ds-space-3)" }}>
        <span className="ds-skeleton-block" style={block({ height: "1.25rem", width: "40%" })} />
        <span className="ds-skeleton-block" style={block({ height: "0.875rem", width: "90%" })} />
        <span className="ds-skeleton-block" style={block({ height: "0.875rem", width: "75%" })} />
      </div>
    );
  } else if (variant === "block") {
    shapes = <span className="ds-skeleton-block" style={block({ display: "block", width: width || "100%", height: height || "6rem" })} />;
  } else {
    shapes = (
      <div style={{ display: "flex", flexDirection: "column", gap: "var(--ds-space-2)" }}>
        {Array.from({ length: rows }, (_, i) => (
          <span key={i} className="ds-skeleton-block" style={block({ height: "0.875rem", width: i === rows - 1 && rows > 1 ? "60%" : "100%" })} />
        ))}
      </div>
    );
  }

  return (
    <div {...rest} role="status" aria-live="polite" style={{ width: width && variant !== "block" ? width : undefined, ...style }}>
      <span className="ds-visually-hidden">{label}</span>
      <div aria-hidden="true">{shapes}</div>
    </div>
  );
}
