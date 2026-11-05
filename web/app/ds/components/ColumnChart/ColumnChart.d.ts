import * as React from "react";

export interface ColumnChartItem {
  /** Short x-axis label, such as "Oct 5". */
  label: string;
  value: number;
  /** Long form for the tooltip, table and screen readers, such as "Week of Oct 5". */
  fullLabel?: string;
}

export interface ColumnChartProps extends Omit<React.HTMLAttributes<HTMLElement>, "title"> {
  /** Names the chart; it is the figure's caption. */
  title: React.ReactNode;
  subtitle?: React.ReactNode;
  /** Time buckets, oldest first. */
  data: ColumnChartItem[];
  /** Plot height in px, not counting the value and axis labels. @default 180 */
  height?: number;
  /** Formats values in the tooltip and the table. @default formatCount (1,284 / 12.9K) */
  valueFormat?: (value: number) => string;
  /** Heading of the label column in the table view. @default "Period" */
  labelHeader?: string;
  /** Heading of the value column in the table view. @default "Count" */
  valueHeader?: string;
  /** Draw the card surface around the chart. @default true */
  framed?: boolean;
  /** Keep the current render, dimmed, while data reloads. @default false */
  isLoading?: boolean;
  /** @default "No data for this period." */
  emptyMessage?: React.ReactNode;
}

/** Vertical columns for counts over time, with clean y ticks, tooltips and a table view. */
export function ColumnChart(props: ColumnChartProps): JSX.Element;
