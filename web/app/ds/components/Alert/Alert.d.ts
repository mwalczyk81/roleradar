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
