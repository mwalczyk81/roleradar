import * as React from "react";

export interface AppShellNavItem {
  label: string;
  href: string;
  /** Icon node (the kit imports no icon package). */
  icon?: React.ReactNode;
  /** Marks the current page (sets aria-current="page"). */
  active?: boolean;
  /** Small count shown at the end of the item. */
  badge?: React.ReactNode;
}

export interface AppShellProps extends Omit<React.HTMLAttributes<HTMLDivElement>, "title"> {
  /** Name or logo at the top of the sidebar. @default "App" */
  brand?: React.ReactNode;
  navItems?: AppShellNavItem[];
  /** Link component for client-side navigation (a router Link or NavLink). It receives `href` and `to`. @default "a" */
  linkComponent?: React.ElementType;
  /** Page title, rendered as the page's h1. */
  title?: React.ReactNode;
  /** Who is signed in, shown as a small chip. */
  userLabel?: string;
  /** Sign-out control. Pass a form button so the app controls how it submits. */
  signOut?: React.ReactNode;
  /** Current theme; shows a theme button when `onThemeToggle` is also set. */
  theme?: "light" | "dark";
  onThemeToggle?: () => void;
  /** Extra controls in the top bar. */
  headerActions?: React.ReactNode;
  children?: React.ReactNode;
}

/** Page frame: sidebar navigation, top bar and main area with a skip link. */
export function AppShell(props: AppShellProps): JSX.Element;
