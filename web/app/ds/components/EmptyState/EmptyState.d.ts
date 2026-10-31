import * as React from "react";

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
