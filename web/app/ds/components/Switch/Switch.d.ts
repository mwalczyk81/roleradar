import * as React from "react";

export interface SwitchProps extends Omit<React.InputHTMLAttributes<HTMLInputElement>, "type" | "size"> {
  /** Text label shown next to the track. */
  label?: React.ReactNode;
  /** Controlled on/off state. */
  checked?: boolean;
}

/** Instant on/off toggle — use when the change applies immediately (no Save). */
export function Switch(props: SwitchProps): JSX.Element;
