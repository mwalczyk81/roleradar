import * as React from "react";

export interface AlertProps extends Omit<React.HTMLAttributes<HTMLDivElement>, "title"> {
  /** Status variant — determines color and icon. @default "info" */
  variant?: "info" | "success" | "warning" | "danger";
  /** Bold title line shown above the body. */
  title?: React.ReactNode;
  /** Show a dismiss (×) button; called when clicked. */
  onClose?: () => void;
  children?: React.ReactNode;
}

/** Inline banner conveying a status message. */
export function Alert(props: AlertProps): JSX.Element;

export interface AvatarProps extends React.HTMLAttributes<HTMLSpanElement> {
  /** Image URL. Omit to show initials derived from `name`. */
  src?: string;
  /** Full name — used for initials fallback and the tooltip/alt text. */
  name?: string;
  /** Diameter. @default "md" */
  size?: "sm" | "md" | "lg";
}

/** Circular user image with an initials fallback. */
export function Avatar(props: AvatarProps): JSX.Element;

export interface BadgeProps extends React.HTMLAttributes<HTMLSpanElement> {
  /** Color tone. Status tones carry meaning — don't use them decoratively. @default "primary" */
  tone?: "neutral" | "primary" | "success" | "warning" | "danger";
  /** Show a leading status dot. @default false */
  dot?: boolean;
  children?: React.ReactNode;
}

/** Small pill for status labels or counts. */
export function Badge(props: BadgeProps): JSX.Element;

export interface CardProps extends React.HTMLAttributes<HTMLDivElement> {
  /** Apply the standard space-6 padding. Set false to lay out edge-to-edge content (e.g. a table). @default true */
  padded?: boolean;
  /** Lift to shadow-md on hover and show a pointer cursor. @default false */
  interactive?: boolean;
  children?: React.ReactNode;
}

/** The default surface container. */
export function Card(props: CardProps): JSX.Element;

export interface ButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  /** Visual style. Use exactly one `primary` per view. @default "primary" */
  variant?: "primary" | "secondary" | "ghost" | "danger";
  /** Control height. @default "md" */
  size?: "sm" | "md" | "lg";
  /** Stretch to fill the container width. @default false */
  fullWidth?: boolean;
  /** Icon node rendered before the label. */
  leadingIcon?: React.ReactNode;
  /** Icon node rendered after the label. */
  trailingIcon?: React.ReactNode;
  children?: React.ReactNode;
}

/**
 * Primary interactive control.
 * @startingPoint section="Forms" subtitle="Button variants & sizes" viewport="700x200"
 */
export function Button(props: ButtonProps): JSX.Element;

export interface CheckboxProps extends Omit<React.InputHTMLAttributes<HTMLInputElement>, "type" | "size"> {
  /** Text label shown next to the box. */
  label?: React.ReactNode;
  /** Controlled checked state. */
  checked?: boolean;
}

/** Boolean toggle with a custom-styled box and an optional label. */
export function Checkbox(props: CheckboxProps): JSX.Element;

export interface InputProps extends Omit<React.InputHTMLAttributes<HTMLInputElement>, "size"> {
  /** Control height. @default "md" */
  size?: "sm" | "md" | "lg";
  /** Render in an error state (red border). @default false */
  invalid?: boolean;
  /** Icon node rendered inside, at the start of the field. */
  leadingIcon?: React.ReactNode;
}

/** Single-line text field. Always paired with a label. */
export function Input(props: InputProps): JSX.Element;

export interface RadioProps extends Omit<React.InputHTMLAttributes<HTMLInputElement>, "type" | "size"> {
  /** Text label shown next to the dot. */
  label?: React.ReactNode;
  /** Controlled checked state. */
  checked?: boolean;
}

/** Single choice within a group — give every option in the group the same `name`. */
export function Radio(props: RadioProps): JSX.Element;

export interface SelectProps extends Omit<React.SelectHTMLAttributes<HTMLSelectElement>, "size"> {
  /** Control height. @default "md" */
  size?: "sm" | "md" | "lg";
  /** Render in an error state (red border). @default false */
  invalid?: boolean;
  children?: React.ReactNode;
}

/** Native dropdown styled to match Input, with a chevron affordance. */
export function Select(props: SelectProps): JSX.Element;

export interface SwitchProps extends Omit<React.InputHTMLAttributes<HTMLInputElement>, "type" | "size"> {
  /** Text label shown next to the track. */
  label?: React.ReactNode;
  /** Controlled on/off state. */
  checked?: boolean;
}

/** Instant on/off toggle — use when the change applies immediately (no Save). */
export function Switch(props: SwitchProps): JSX.Element;

export interface TextareaProps extends React.TextareaHTMLAttributes<HTMLTextAreaElement> {
  /** Render in an error state (red border). @default false */
  invalid?: boolean;
}

/** Multi-line text field. Always paired with a label. */
export function Textarea(props: TextareaProps): JSX.Element;

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

export interface ModalProps {
  /** Whether the modal is shown. */
  open: boolean;
  /** Called on Esc, backdrop click, or close button. */
  onClose?: () => void;
  /** Header title. Omit for a chrome-less dialog. */
  title?: React.ReactNode;
  /** Accessible name when there is no visible `title`. */
  ariaLabel?: string;
  /** Footer node — typically the action buttons. */
  footer?: React.ReactNode;
  /** Max width in px (or any CSS length). @default 480 */
  width?: number | string;
  children?: React.ReactNode;
}

/** Centered dialog over a dimmed backdrop; closes on Esc / backdrop click. */
export function Modal(props: ModalProps): JSX.Element | null;

export interface DataTableColumn<T = any> {
  /** Field name on each row; also the sort key. */
  key: string;
  /** Column heading text (sentence case; shown uppercase). */
  header: React.ReactNode;
  /** Allow sorting this column. Sorting applies to the rows on the current page only. */
  sortable?: boolean;
  /** Monospace, right-aligned, tabular figures. Use for numbers, scores and money. */
  numeric?: boolean;
  /** Text alignment for non-numeric columns. @default "left" */
  align?: "left" | "right" | "center";
  /** CSS width of the column. */
  width?: string;
  /** Custom cell content. Empty values show an em dash. */
  render?: (row: T) => React.ReactNode;
  /** Value to sort by when it differs from `row[key]`. */
  sortValue?: (row: T) => string | number | Date | null | undefined;
}

export interface DataTablePagination {
  /** Total rows across all pages. */
  total: number;
  /** Rows per page. */
  limit: number;
  /** Zero-based index of the first row on this page. */
  offset: number;
  /** Called with the new offset when Previous or Next is pressed. */
  onPageChange: (offset: number) => void;
}

export interface DataTableProps<T = any> extends Omit<React.HTMLAttributes<HTMLDivElement>, "children"> {
  columns: DataTableColumn<T>[];
  /** The rows for the current page. */
  data: T[];
  /** Field that uniquely identifies a row. @default "id" */
  keyField?: keyof T & string;
  /** Visually hidden table caption: the table's accessible name. */
  caption?: string;
  /** Accessible name when there is no caption. */
  ariaLabel?: string;
  /** Makes rows focusable and clickable (Enter and Space). Links and buttons inside a row keep their own behaviour. */
  onRowClick?: (row: T, event: React.SyntheticEvent) => void;
  /** Show placeholder rows and mark the table busy. @default false */
  isLoading?: boolean;
  /** Placeholder row count while loading. @default 5 */
  loadingRows?: number;
  /** Text shown when there are no rows. @default "Nothing to show." */
  emptyMessage?: React.ReactNode;
  /** Node shown under the empty message, such as a Button. */
  emptyAction?: React.ReactNode;
  /** Controlled sort column key. */
  sortColumn?: string;
  /** Initial sort column key (uncontrolled). */
  defaultSortColumn?: string;
  /** Controlled sort direction. */
  sortDirection?: "asc" | "desc";
  /** Initial sort direction. @default "asc" */
  defaultSortDirection?: "asc" | "desc";
  /** Called when a sortable header is pressed. */
  onSortChange?: (column: string, direction: "asc" | "desc") => void;
  /** Shows the range and Previous/Next controls. */
  pagination?: DataTablePagination;
  /** Keep the header visible while the body scrolls (needs `maxHeight`). @default true */
  stickyHeader?: boolean;
  /** Make the body scroll above this height (px number or CSS length). */
  maxHeight?: number | string;
}

/** Sticky-header table with page-local sorting, numeric columns and paging. */
export function DataTable<T = any>(props: DataTableProps<T>): JSX.Element;

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

export interface EmptyStateProps extends Omit<React.HTMLAttributes<HTMLDivElement>, "title"> {
  /** Icon node shown in a circle above the title. */
  icon?: React.ReactNode;
  title: React.ReactNode;
  description?: React.ReactNode;
  /** Primary action, usually a Button. */
  action?: React.ReactNode;
  /** Secondary action. */
  secondaryAction?: React.ReactNode;
  /** Heading level for the title (1-6). @default 2 */
  headingLevel?: 1 | 2 | 3 | 4 | 5 | 6;
}

/** Placeholder for an empty list, no search results, a missing page or an unavailable service. */
export function EmptyState(props: EmptyStateProps): JSX.Element;

export interface SkeletonProps extends React.HTMLAttributes<HTMLDivElement> {
  /** Shape of the placeholder. @default "text" */
  variant?: "text" | "table" | "card" | "block";
  /** Lines (text) or body rows (table). @default 3 */
  rows?: number;
  /** Column count for the table variant. @default 4 */
  columns?: number;
  /** Width of the block variant, or of the whole skeleton. */
  width?: string;
  /** Height of the block variant. */
  height?: string;
  /** Text announced to screen readers. @default "Loading" */
  label?: string;
}

/** Loading placeholder shapes, announced once as "Loading". */
export function Skeleton(props: SkeletonProps): JSX.Element;

export interface ToastProps extends Omit<React.HTMLAttributes<HTMLDivElement>, "title"> {
  /** Tone and announcement level: warning and danger are assertive. @default "info" */
  variant?: "info" | "success" | "warning" | "danger";
  title: React.ReactNode;
  message?: React.ReactNode;
  /** Button shown under the message. A toast with an action does not dismiss itself. */
  action?: { label: string; onClick: () => void };
  /** Called when the toast is dismissed by the user or by its timer. */
  onDismiss?: () => void;
  /** Milliseconds before it dismisses itself; 0 keeps it until dismissed. Paused while hovered or focused. @default 5000 */
  duration?: number;
}

/** A short notification with optional action; dismisses itself unless it needs a response. */
export function Toast(props: ToastProps): JSX.Element;

export interface ToastRegionProps extends React.HTMLAttributes<HTMLDivElement> {
  /** Accessible name of the region. @default "Notifications" */
  label?: string;
  /** Toast elements, newest last. */
  children?: React.ReactNode;
}

/** Fixed bottom-right stack that holds Toasts. Render once near the root. */
export function ToastRegion(props: ToastRegionProps): JSX.Element;

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

export interface TooltipProps extends Omit<React.HTMLAttributes<HTMLSpanElement>, "content"> {
  /** The hint text. Keep it short. */
  content: React.ReactNode;
  /** Exactly one element, such as a button. It gets aria-describedby. */
  children: React.ReactElement;
  /** @default "top" */
  placement?: "top" | "bottom";
  /** Milliseconds of hover before it shows (focus shows it at once). @default 300 */
  delay?: number;
}

/** Short hint for one control, shown on hover and keyboard focus. */
export function Tooltip(props: TooltipProps): JSX.Element;

declare global {
  interface Window {
    MattSDesignSystem_522a7a: {
      Alert: typeof Alert;
      Avatar: typeof Avatar;
      Badge: typeof Badge;
      Card: typeof Card;
      Button: typeof Button;
      Checkbox: typeof Checkbox;
      Input: typeof Input;
      Radio: typeof Radio;
      Select: typeof Select;
      Switch: typeof Switch;
      Textarea: typeof Textarea;
      Tabs: typeof Tabs;
      Modal: typeof Modal;
      DataTable: typeof DataTable;
      Toolbar: typeof Toolbar;
      AppShell: typeof AppShell;
      EmptyState: typeof EmptyState;
      Skeleton: typeof Skeleton;
      Toast: typeof Toast;
      ToastRegion: typeof ToastRegion;
      Menu: typeof Menu;
      Tooltip: typeof Tooltip;
    };
  }
}
