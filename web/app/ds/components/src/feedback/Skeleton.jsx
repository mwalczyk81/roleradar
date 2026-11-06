import React from "react";

const cx = (...parts) => parts.filter(Boolean).join(" ");

const PERCENT = /^(\d{1,3})%$/;

/** Whole-percent widths are classes (SSR-correct, no inline style); any other size is applied after mount. */
function percentClass(prefix, value) {
  const m = typeof value === "string" ? PERCENT.exec(value) : null;
  return m && Number(m[1]) <= 100 ? `${prefix}-${m[1]}` : undefined;
}

function useSize(ref, width, height) {
  React.useLayoutEffect(() => {
    const el = ref.current;
    if (!el) return;
    if (width && !percentClass("ds-w", width)) el.style.width = typeof width === "number" ? `${width}px` : width;
    if (height && !percentClass("ds-h", height)) el.style.height = typeof height === "number" ? `${height}px` : height;
  }, [ref, width, height]);
}

function Block({ width, height, className }) {
  const ref = React.useRef(null);
  useSize(ref, width, height);
  return <span ref={ref} className={cx("ds-skeleton-block", "ds-skeleton-solo", percentClass("ds-w", width), percentClass("ds-h", height), className)} />;
}

/**
 * Skeleton — placeholder shapes while content loads. The whole group is announced once as
 * "Loading" (override with `label`); the shapes are hidden from assistive tech. Variants:
 * "text" (lines), "table" (header + rows), "card" (a Card-shaped block), "block" (one box
 * sized with `width` and `height`). The pulse stops under prefers-reduced-motion.
 * `width` and `height` take a whole percent ("60%") for a server-rendered size, or any CSS
 * length, which is applied after the page loads.
 */
export function Skeleton({ variant = "text", rows = 3, columns = 4, width, height, label = "Loading", className, style, ...rest }) {
  const rootRef = React.useRef(null);
  useSize(rootRef, variant === "block" ? undefined : width, undefined);

  let shapes;
  if (variant === "table") {
    shapes = (
      <div className="ds-skeleton-stack" data-gap="3">
        {Array.from({ length: rows + 1 }, (_, r) => (
          <div key={r} className="ds-skeleton-grid" data-cols={Math.min(Math.max(columns, 1), 24)}>
            {Array.from({ length: columns }, (_, c) => (
              <span key={c} className={cx("ds-skeleton-block", r === 0 ? "ds-skeleton-head ds-w-50" : c === 0 ? "ds-skeleton-cell ds-w-80" : "ds-skeleton-cell ds-w-60")} />
            ))}
          </div>
        ))}
      </div>
    );
  } else if (variant === "card") {
    shapes = (
      <div className="ds-skeleton-card">
        <span className="ds-skeleton-block ds-skeleton-title ds-w-40" />
        <span className="ds-skeleton-block ds-skeleton-line ds-w-90" />
        <span className="ds-skeleton-block ds-skeleton-line ds-w-75" />
      </div>
    );
  } else if (variant === "block") {
    shapes = <Block width={width} height={height} />;
  } else {
    shapes = (
      <div className="ds-skeleton-stack" data-gap="2">
        {Array.from({ length: rows }, (_, i) => (
          <span key={i} className={cx("ds-skeleton-block ds-skeleton-line", i === rows - 1 && rows > 1 ? "ds-w-60" : "ds-w-100")} />
        ))}
      </div>
    );
  }

  return (
    <div
      {...rest}
      ref={rootRef}
      role="status"
      aria-live="polite"
      className={cx(variant !== "block" ? percentClass("ds-w", width) : undefined, className)}
      style={style}
    >
      <span className="ds-visually-hidden">{label}</span>
      <div aria-hidden="true">{shapes}</div>
    </div>
  );
}
