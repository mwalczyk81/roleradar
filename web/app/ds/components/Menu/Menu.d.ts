import * as React from "react";

export interface MenuItem {
  label?: string;
  onClick?: (event: React.SyntheticEvent) => void;
  /** Renders the item as a link. */
  href?: string;
  /** Destructive action, shown in the danger color. */
  danger?: boolean;
  disabled?: boolean;
  /** A divider line instead of an item. */
  separator?: boolean;
}

export interface MenuProps extends Omit<React.HTMLAttributes<HTMLDivElement>, "children"> {
  /** Content of the button that opens the menu. */
  trigger: React.ReactNode;
  /** Accessible name of the button and menu. Required when the trigger is only an icon. */
  label?: string;
  items: MenuItem[];
  /** Which edge of the button the menu lines up with. @default "start" */
  align?: "start" | "end";
  /** Button style. @default "secondary" */
  variant?: "secondary" | "ghost";
  /** Link component for items with an href (a router Link). It receives `href` and `to`. @default "a" */
  linkComponent?: React.ElementType;
}

/** Dropdown menu opened from a button, with full keyboard support. */
export function Menu(props: MenuProps): JSX.Element;
