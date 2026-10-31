import * as React from "react";

export interface ToastRegionProps extends React.HTMLAttributes<HTMLDivElement> {
  /** Accessible name of the region. @default "Notifications" */
  label?: string;
  /** Toast elements, newest last. */
  children?: React.ReactNode;
}

/** Fixed bottom-right stack that holds Toasts. Render once near the root. */
export function ToastRegion(props: ToastRegionProps): JSX.Element;
