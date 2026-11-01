import React from "react";

/**
 * Tabs — switch between sibling views. Controlled or uncontrolled via `value`/`defaultValue`.
 * `items` is an array of { value, label }.
 *
 * Keyboard: Left/Right move and select, Home/End jump to the first/last tab. Only the
 * active tab is in the tab order (roving tabindex). Tab ids are `${idBase}-tab-${value}`
 * and each tab sets aria-controls to `${idBase}-panel-${value}`; give your panel
 * role="tabpanel", that id, and aria-labelledby of the tab id.
 */
export function Tabs({ items = [], value, defaultValue, onChange, idBase, style = {}, onKeyDown, ...rest }) {
  const isControlled = value !== undefined;
  const [internal, setInternal] = React.useState(defaultValue ?? (items[0] && items[0].value));
  const active = isControlled ? value : internal;
  const generated = React.useId();
  const base = idBase || generated;
  const refs = React.useRef({});
  const focusable = items.some((it) => it.value === active) ? active : items[0] && items[0].value;

  function select(v) {
    if (!isControlled) setInternal(v);
    onChange && onChange(v);
  }

  function handleKeyDown(e) {
    onKeyDown && onKeyDown(e);
    if (e.defaultPrevented || !items.length) return;
    const i = Math.max(0, items.findIndex((it) => it.value === focusable));
    let n = -1;
    if (e.key === "ArrowRight") n = (i + 1) % items.length;
    else if (e.key === "ArrowLeft") n = (i - 1 + items.length) % items.length;
    else if (e.key === "Home") n = 0;
    else if (e.key === "End") n = items.length - 1;
    if (n < 0) return;
    e.preventDefault();
    const next = items[n].value;
    select(next);
    if (refs.current[next]) refs.current[next].focus();
  }

  return (
    <div
      {...rest}
      role="tablist"
      onKeyDown={handleKeyDown}
      style={{
        display: "flex",
        gap: "var(--ds-space-1)",
        borderBottom: "var(--ds-border-thin) solid var(--ds-border)",
        ...style,
      }}
    >
      {items.map((it) => {
        const on = it.value === active;
        return (
          <button
            key={it.value}
            ref={(el) => {
              refs.current[it.value] = el;
            }}
            type="button"
            role="tab"
            id={`${base}-tab-${it.value}`}
            aria-selected={on}
            aria-controls={`${base}-panel-${it.value}`}
            tabIndex={it.value === focusable ? 0 : -1}
            onClick={() => select(it.value)}
            style={{
              appearance: "none",
              background: "none",
              border: "none",
              borderBottom: `var(--ds-border-thick) solid ${on ? "var(--ds-primary)" : "transparent"}`,
              marginBottom: "-1px",
              padding: "var(--ds-space-2) var(--ds-space-3)",
              fontFamily: "var(--ds-font-sans)",
              fontSize: "var(--ds-text-sm)",
              fontWeight: "var(--ds-weight-medium)",
              color: on ? "var(--ds-text)" : "var(--ds-text-muted)",
              cursor: "pointer",
              transition: "color var(--ds-duration-fast) var(--ds-ease-standard)",
            }}
          >
            {it.label}
          </button>
        );
      })}
    </div>
  );
}
