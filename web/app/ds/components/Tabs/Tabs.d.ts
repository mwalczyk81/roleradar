import * as React from "react";

export interface TabItem {
  /** Stable value returned by onChange. */
  value: string;
  /** Visible label. */
  label: React.ReactNode;
}

export interface TabsProps extends Omit<React.HTMLAttributes<HTMLDivElement>, "onChange"> {
  /** The tabs to render. */
  items: TabItem[];
  /** Controlled active value. */
  value?: string;
  /** Initial active value (uncontrolled). */
  defaultValue?: string;
  /** Fires with the new value when a tab is clicked. */
  onChange?: (value: string) => void;
  /** Prefix for tab and panel ids. Tabs get `${idBase}-tab-${value}` and aria-controls `${idBase}-panel-${value}`. Defaults to a generated id. */
  idBase?: string;
}

/** Underline tab bar for switching between sibling views. */
export function Tabs(props: TabsProps): JSX.Element;
