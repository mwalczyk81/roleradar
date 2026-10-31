import * as React from "react";

export interface ToolbarFilter {
  /** Stable id. */
  id: string;
  /** Filter name, e.g. "Disposition". */
  label: string;
  /** Current value, e.g. "Lead". */
  value: string;
  /** Called when the chip is removed by click, Backspace or Delete. */
  onRemove: () => void;
}

export interface ToolbarProps extends Omit<React.HTMLAttributes<HTMLDivElement>, "children"> {
  /** Controlled search text. */
  searchValue?: string;
  /** Initial search text (uncontrolled). */
  defaultSearchValue?: string;
  /** Called with the search text after typing pauses, on Enter, on Escape and on clear. */
  onSearchChange?: (value: string) => void;
  /** Called with the search text when Enter is pressed. */
  onSearchSubmit?: (value: string) => void;
  /** @default "Search" */
  searchPlaceholder?: string;
  /** Accessible label for the search box. @default "Search" */
  searchLabel?: string;
  /** Milliseconds of no typing before onSearchChange fires. @default 300 */
  debounceMs?: number;
  /** Active filters shown as removable chips. */
  filters?: ToolbarFilter[];
  /** Adds a "Clear all" button when there are two or more filters. */
  onClearAll?: () => void;
  /** Buttons placed at the end of the bar. */
  actions?: React.ReactNode;
  /** Filter controls (Select, Switch...) placed after the search box. */
  children?: React.ReactNode;
}

/** Search box, filter controls and removable filter chips. */
export function Toolbar(props: ToolbarProps): JSX.Element;
