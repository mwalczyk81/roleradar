import * as React from "react";

export interface AlertProps extends React.HTMLAttributes<HTMLDivElement> {
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

export interface InputProps extends React.InputHTMLAttributes<HTMLInputElement> {
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

export interface SelectProps extends React.SelectHTMLAttributes<HTMLSelectElement> {
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
    };
  }
}
