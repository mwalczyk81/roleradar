import React from "react";

/**
 * Toolbar — search box, filter controls and removable filter chips for the area above a table.
 *
 * Typing in the search box reports through `onSearchChange` after `debounceMs` of no typing;
 * Enter reports immediately (and calls `onSearchSubmit`), Escape clears. Pass `children` for
 * filter controls (Select, Switch...) and `actions` for buttons at the end. `filters` is a list
 * of { id, label, value, onRemove } shown as chips; a chip is removed by click, Backspace or
 * Delete, and focus moves to a neighbouring chip or the search box.
 */
export function Toolbar({
  searchValue,
  defaultSearchValue = "",
  onSearchChange,
  onSearchSubmit,
  searchPlaceholder = "Search",
  searchLabel = "Search",
  debounceMs = 300,
  filters = [],
  onClearAll,
  actions,
  children,
  style = {},
  ...rest
}) {
  const controlled = searchValue !== undefined;
  const [text, setText] = React.useState(controlled ? searchValue : defaultSearchValue);
  const [focus, setFocus] = React.useState(false);
  const reported = React.useRef(text);
  const timer = React.useRef(null);
  const inputRef = React.useRef(null);
  const listRef = React.useRef(null);
  const inputId = React.useId();

  // Follow external changes (a reset from outside) without echoing our own reports back.
  React.useEffect(() => {
    if (controlled && searchValue !== reported.current) {
      reported.current = searchValue;
      setText(searchValue);
    }
  }, [controlled, searchValue]);

  React.useEffect(() => () => clearTimeout(timer.current), []);

  function emit(value) {
    clearTimeout(timer.current);
    reported.current = value;
    onSearchChange && onSearchChange(value);
  }

  function onInput(e) {
    const v = e.target.value;
    setText(v);
    clearTimeout(timer.current);
    timer.current = setTimeout(() => emit(v), debounceMs);
  }

  function onKeyDown(e) {
    if (e.key === "Enter") {
      emit(text);
      onSearchSubmit && onSearchSubmit(text);
    } else if (e.key === "Escape" && text) {
      e.preventDefault();
      setText("");
      emit("");
    }
  }

  function removeChip(index, filter) {
    const buttons = listRef.current ? Array.from(listRef.current.querySelectorAll("button")) : [];
    const neighbour = buttons[index + 1] || buttons[index - 1] || inputRef.current;
    if (neighbour) neighbour.focus();
    filter.onRemove && filter.onRemove();
  }

  return (
    <div {...rest} role="search" style={{ display: "flex", flexDirection: "column", gap: "var(--ds-space-3)", fontFamily: "var(--ds-font-sans)", ...style }}>
      <div style={{ display: "flex", flexWrap: "wrap", alignItems: "center", gap: "var(--ds-space-3)" }}>
        <div style={{ position: "relative", flex: "1 1 16rem", maxWidth: "26rem" }}>
          <label htmlFor={inputId} className="ds-visually-hidden">{searchLabel}</label>
          <span aria-hidden="true" style={{ position: "absolute", left: "var(--ds-space-3)", top: "50%", transform: "translateY(-50%)", display: "inline-flex", color: "var(--ds-text-secondary)", pointerEvents: "none" }}>
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><circle cx="11" cy="11" r="8" /><path d="m21 21-4.3-4.3" /></svg>
          </span>
          <input
            ref={inputRef}
            id={inputId}
            type="search"
            value={text}
            placeholder={searchPlaceholder}
            onChange={onInput}
            onKeyDown={onKeyDown}
            onFocus={() => setFocus(true)}
            onBlur={() => setFocus(false)}
            autoComplete="off"
            style={{
              width: "100%",
              height: "var(--ds-control-md)",
              padding: "0 var(--ds-space-8) 0 var(--ds-space-8)",
              background: "var(--ds-surface)",
              color: "var(--ds-text)",
              border: `var(--ds-border-thin) solid ${focus ? "var(--ds-focus-ring)" : "var(--ds-border-control)"}`,
              outline: focus ? "var(--ds-border-thick) solid var(--ds-focus-ring)" : "none",
              outlineOffset: 0,
              borderRadius: "var(--ds-radius-md)",
              fontFamily: "var(--ds-font-sans)",
              fontSize: "var(--ds-text-sm)",
              boxSizing: "border-box",
              WebkitAppearance: "none",
              appearance: "none",
            }}
          />
          {text ? (
            <button
              type="button"
              aria-label="Clear search"
              onClick={() => {
                setText("");
                emit("");
                inputRef.current && inputRef.current.focus();
              }}
              className="ds-icon-btn"
              style={{ position: "absolute", right: "var(--ds-space-2)", top: "50%", transform: "translateY(-50%)", display: "inline-flex", padding: "var(--ds-space-1)", background: "none", border: "none", borderRadius: "var(--ds-radius-sm)", color: "var(--ds-text-secondary)", cursor: "pointer" }}
            >
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M18 6 6 18M6 6l12 12" /></svg>
            </button>
          ) : null}
        </div>
        {children}
        {actions ? <div style={{ marginLeft: "auto", display: "flex", alignItems: "center", gap: "var(--ds-space-3)" }}>{actions}</div> : null}
      </div>
      {filters.length > 0 ? (
        <div style={{ display: "flex", flexWrap: "wrap", alignItems: "center", gap: "var(--ds-space-2)" }}>
          <ul ref={listRef} aria-label="Active filters" style={{ display: "flex", flexWrap: "wrap", gap: "var(--ds-space-2)", margin: 0, padding: 0, listStyle: "none" }}>
            {filters.map((f, i) => (
              <li key={f.id}>
                <button
                  type="button"
                  className="ds-chip"
                  aria-label={`Remove filter ${f.label}: ${f.value}`}
                  onClick={() => removeChip(i, f)}
                  onKeyDown={(e) => {
                    if (e.key === "Backspace" || e.key === "Delete") {
                      e.preventDefault();
                      removeChip(i, f);
                    }
                  }}
                  style={{
                    display: "inline-flex",
                    alignItems: "center",
                    gap: "var(--ds-space-2)",
                    padding: "var(--ds-space-1) var(--ds-space-2) var(--ds-space-1) var(--ds-space-3)",
                    background: "var(--ds-primary-subtle)",
                    color: "var(--ds-primary-subtle-text)",
                    border: "none",
                    borderRadius: "var(--ds-radius-full)",
                    fontFamily: "var(--ds-font-sans)",
                    fontSize: "var(--ds-text-xs)",
                    fontWeight: "var(--ds-weight-medium)",
                    lineHeight: 1.4,
                    cursor: "pointer",
                  }}
                >
                  <span>{f.label}: {f.value}</span>
                  <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M18 6 6 18M6 6l12 12" /></svg>
                </button>
              </li>
            ))}
          </ul>
          {onClearAll && filters.length > 1 ? (
            <button
              type="button"
              className="ds-icon-btn"
              onClick={() => {
                onClearAll();
                inputRef.current && inputRef.current.focus();
              }}
              style={{ background: "none", border: "none", padding: "var(--ds-space-1) var(--ds-space-2)", borderRadius: "var(--ds-radius-sm)", color: "var(--ds-text-secondary)", fontFamily: "var(--ds-font-sans)", fontSize: "var(--ds-text-xs)", fontWeight: "var(--ds-weight-medium)", cursor: "pointer" }}
            >
              Clear all
            </button>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}
