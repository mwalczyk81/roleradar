import * as React from "react";

export interface BarChartItem {
  label: string;
  value: number;
  /** Make this bar the point: the others turn gray. */
  highlight?: boolean;
  /** The count this value is out of. Under `smallSampleBelow` the bar gets a small-sample note. Funnels use the previous stage. */
  denominator?: number;
}

export interface BarChartProps extends Omit<React.HTMLAttributes<HTMLElement>, "title"> {
  /** Names the chart; it is the figure's caption. */
  title: React.ReactNode;
  subtitle?: React.ReactNode;
  /** In display order. */
  data: BarChartItem[];
  /** Stages that narrow: adds "% of previous stage" to every stage after the first. @default false */
  funnel?: boolean;
  /** Formats values at the bar tips, in the tooltip and in the table. @default formatCount (1,284 / 12.9K) */
  valueFormat?: (value: number) => string;
  /** Heading of the label column in the table view. @default "Category" */
  labelHeader?: string;
  /** Heading of the value column in the table view. @default "Count" */
  valueHeader?: string;
  /** A denominator under this gets a small-sample note. @default 10 */
  smallSampleBelow?: number;
  /** Draw the card surface around the chart. @default true */
  framed?: boolean;
  /** Keep the current render, dimmed, while data reloads. @default false */
  isLoading?: boolean;
  /** @default "No data for this period." */
  emptyMessage?: React.ReactNode;
}

/** Horizontal bars with the value at the tip, an optional funnel mode, tooltips and a table view. */
export function BarChart(props: BarChartProps): JSX.Element;
