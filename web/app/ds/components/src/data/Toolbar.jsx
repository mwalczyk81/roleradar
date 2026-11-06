import React from "react";

const cx = (...parts) => parts.filter(Boolean).join(" ");

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
  className,
  style,
  ...rest
}) {
  const controlled = searchValue !== undefined;
  const [text, setText] = React.useState(controlled ? searchValue : defaultSearchValue);
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
    <div {...rest} role="search" className={cx("ds-toolbar", className)} style={style}>
      <div className="ds-toolbar-row">
        <div className="ds-toolbar-search">
          <label htmlFor={inputId} className="ds-visually-hidden">{searchLabel}</label>
          <span aria-hidden="true" className="ds-toolbar-search-icon">
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
            autoComplete="off"
            className="ds-toolbar-input"
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
              className="ds-icon-btn ds-toolbar-clear"
            >
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M18 6 6 18M6 6l12 12" /></svg>
            </button>
          ) : null}
        </div>
        {children}
        {actions ? <div className="ds-toolbar-actions">{actions}</div> : null}
      </div>
      {filters.length > 0 ? (
        <div className="ds-toolbar-filters">
          <ul ref={listRef} aria-label="Active filters" className="ds-toolbar-chips">
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
              className="ds-icon-btn ds-toolbar-clearall"
              onClick={() => {
                onClearAll();
                inputRef.current && inputRef.current.focus();
              }}
            >
              Clear all
            </button>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}
