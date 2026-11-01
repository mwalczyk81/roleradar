import * as React from "react";

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
