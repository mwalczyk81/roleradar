import * as React from "react";

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
