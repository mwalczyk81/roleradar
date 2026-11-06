import type { ReactNode } from "react";
import type { LoaderFunctionArgs, MetaFunction } from "react-router";
import { Link, useLoaderData } from "react-router";
import { Brand } from "../components/Brand.js";
import { assertApiOk, requireUserSession } from "../lib/auth-guard.server.js";
import { loadInsightsCounts, MAX_IN_FLIGHT } from "../lib/insights-counts.server.js";
import { LENS_LABELS, lensListHref, parseWindowParams } from "../lib/insights-lenses.js";
import type { StatsView } from "../lib/insights-stats.js";
import { loadInsightsStats } from "../lib/insights-stats.server.js";
import { buildInsightsView, type CountItem, type FunnelView, type InsightsView, type RateView } from "../lib/insights-view.js";
import { getNavItems } from "../lib/nav.js";
import { createLimiter } from "../lib/request-limiter.server.js";
import { useTheme } from "../lib/use-theme.js";
import { Alert, AppShell, BarChart, Button, ColumnChart, StatTile, type BarChartItem } from "../ui.js";

export const meta: MetaFunction = () => [{ title: "Insights - RoleRadar" }];

export async function loader({ request }: LoaderFunctionArgs) {
  const session = await requireUserSession(request);
  const ctx = parseWindowParams(new URL(request.url));

  // One limiter for the counts and the stats read. The stats request is started first, so it runs in the first wave
  // alongside the first counts, and the total in flight never exceeds MAX_IN_FLIGHT.
  const limiter = createLimiter(MAX_IN_FLIGHT);
  const statsPromise = loadInsightsStats({ request, session, ctx, limiter });
  const counts = await loadInsightsCounts({ request, session, ctx, limiter });
  const stats = await statsPromise;
  if (stats.authFailure) {
    // 401 re-challenges once (never in a loop) and 403 goes to /not-authorized. Any other stats failure only
    // degrades the trend and median tiles.
    await assertApiOk(stats.authFailure, request, session.sessionId);
  }

  const view = buildInsightsView(counts, ctx);

  return {
    view,
    stats: stats.view,
    csrfToken: session.csrfToken,
    userLabel: session.ownerUsername || "Signed in",
  };
}

function stageText(item: { value: number | null; state: string }): string {
  if (item.state === "needs_backfill") return "Needs backfill";
  if (item.state === "unavailable" || item.value === null) return "Unavailable";
  return String(item.value);
}

/** A funnel and its drill-down links; children are the help text and links that belong with this funnel. */
function FunnelSection({ funnel, children }: { funnel: FunnelView; children?: ReactNode }) {
  const headingId = `funnel-${funnel.id}`;

  // Draw the leading stages whose counts can be trusted; anything gated or unavailable is listed below as text.
  const items: BarChartItem[] = [];
  for (const stage of funnel.stages) {
    if (stage.state !== "ok" || stage.value === null) break;
    items.push({
      label: stage.label,
      value: stage.value,
      ...(stage.denominator !== undefined ? { denominator: stage.denominator } : {}),
    });
  }

  return (
    <section aria-labelledby={headingId} className="insights-section">
      <h2 id={headingId} className="page-heading">
        {funnel.title}
      </h2>
      <p className="insights-basis">{funnel.basis}</p>

      {funnel.error ? (
        <Alert variant="danger" title="This funnel is not drawn">
          {funnel.error}
        </Alert>
      ) : (
        <>
          {items.length > 0 ? (
            <BarChart
              title={funnel.id === "postings" ? "Postings funnel stages" : "Applications funnel stages"}
              subtitle={funnel.basis}
              data={items}
              funnel
              labelHeader="Stage"
              valueHeader="Count"
            />
          ) : null}
          <ul className="insights-stage-links" aria-label={`${funnel.title}: drill down`}>
            {funnel.stages.map((stage) => (
              <li key={stage.key}>
                {stage.state === "ok" && stage.value !== null ? (
                  <Link to={stage.href} data-lens={stage.key} data-role="stage" data-window={funnelWindow(funnel, stage.href)}>
                    {stage.label}: {stage.value}
                  </Link>
                ) : (
                  <span>
                    {stage.label}: {stageText(stage)}
                  </span>
                )}
              </li>
            ))}
          </ul>
        </>
      )}
      {children}
    </section>
  );
}

function funnelWindow(_funnel: FunnelView, href: string): string {
  return new URL(href, "https://insights.invalid").searchParams.get("window") ?? "90d";
}

function RateTile({ rate }: { rate: RateView }) {
  const note =
    rate.state === "needs_backfill"
      ? "Needs the one-time backfill"
      : rate.state === "no_data"
        ? "Nothing to measure in this cohort"
        : rate.state === "unavailable"
          ? "This count could not be loaded"
          : undefined;
  const window = rate.allTime ? "all" : undefined;
  const showLinks = rate.state === "ok" || rate.state === "small_sample" || rate.state === "no_data";

  return (
    <div className="insights-tile" data-rate={rate.id}>
      <StatTile
        label={rate.label}
        value={rate.display}
        {...(rate.of ? { of: rate.of } : {})}
        {...(rate.denominator !== null && rate.state !== "no_data" ? { denominator: rate.denominator } : {})}
        {...(note ? { note } : {})}
      />
      {showLinks ? (
        <p className="insights-tile-links">
          <Link to={rate.numeratorHref} data-lens={rate.numeratorKey} data-role="numerator" data-window={window ?? hrefWindow(rate.numeratorHref)}>
            {rate.numerator} {LENS_LABELS[rate.numeratorKey].toLowerCase()}
          </Link>
          <Link to={rate.denominatorHref} data-lens={rate.denominatorKey} data-role="denominator" data-window={window ?? hrefWindow(rate.denominatorHref)}>
            {rate.denominator} {LENS_LABELS[rate.denominatorKey].toLowerCase()}
          </Link>
        </p>
      ) : null}
      <p className="insights-help">{rate.help}</p>
    </div>
  );
}

function hrefWindow(href: string): string {
  return new URL(href, "https://insights.invalid").searchParams.get("window") ?? "90d";
}

function countLink(item: CountItem, lens: "rejected-before-interview" | "rejected-after-interview" | "open", text: string) {
  return item.state === "ok" && item.count !== null ? (
    <Link to={item.href} data-lens={lens} data-role="count">
      {text}: {item.count}
    </Link>
  ) : (
    <span>
      {text}: {item.state === "needs_backfill" ? "Needs backfill" : "Unavailable"}
    </span>
  );
}

function BackfillNotice({ view }: { view: InsightsView }) {
  const needed = view.backfill.postings === "needed" || view.backfill.applications === "needed";
  const unknown = view.backfill.postings === "unknown" || view.backfill.applications === "unknown";
  if (!needed && !unknown) {
    return null;
  }
  if (needed) {
    return (
      <Alert variant="warning" title="Some numbers need a one-time backfill">
        Existing postings or applications do not carry the derived fields these numbers rely on yet, so they are not shown rather than shown wrong. Run{" "}
        <code>{view.backfill.command}</code>. {view.backfill.help}
      </Alert>
    );
  }
  return (
    <Alert variant="info" title="Could not check the read model">
      Some counts did not load, so it could not be confirmed that the derived fields are in place. Numbers that depend on them are unavailable until it can.
    </Alert>
  );
}

const MEDIAN_HELP =
  "Median days from the applied date to the first response: an interview, offer, offer accepted, offer declined or rejected event. Follow-ups and withdrawals do not count. Whole UTC days, and a median, not a mean.";

function plural(count: number, one: string, many: string): string {
  return `${count} ${count === 1 ? one : many}`;
}

function TrendsSection({ stats }: { stats: StatsView }) {
  const runTitle = "New postings per search run";
  const latestRun = stats.runs.rows[stats.runs.rows.length - 1];

  return (
    <section aria-labelledby="trends" className="insights-section">
      <h2 id="trends" className="page-heading">
        Trends
      </h2>

      {stats.state === "unavailable" ? (
        <Alert variant="warning" title="Trends are unavailable">
          The median response time, applications per week and search-run yield could not be loaded. Everything else on this page is unaffected.
        </Alert>
      ) : null}

      <div className="insights-tiles">
        <div className="insights-tile" data-rate="median-response-time">
          <StatTile label="Median response time" value={stats.median.value} note={stats.median.sampleText} />
          <p className="insights-help">{MEDIAN_HELP}</p>
        </div>
      </div>

      {stats.state === "ok" ? (
        <>
          <div className="insights-trend">
            <ColumnChart
              title="Applications per week"
              subtitle="By application date. Weeks start on Monday (UTC); weeks with none are shown as zero."
              data={stats.weekly.items}
              labelHeader="Week"
              valueHeader="Applications"
              emptyMessage="No applications in this window."
            />
          </div>

          <div className="insights-trend">
            {latestRun === undefined ? (
              <>
                <h3 className="insights-trend-title page-heading">{runTitle}</h3>
                <p className="insights-help">No search runs in this window.</p>
              </>
            ) : (
              <>
                <p className="insights-trend-note" data-testid="run-yield-summary">
                  Latest search run ({latestRun.date}): {plural(latestRun.newPostings, "new posting", "new postings")}. {plural(stats.runs.rows.length, "run", "runs")} in this window.
                </p>
                <ColumnChart
                  title={runTitle}
                  data={stats.runs.rows.map((row) => ({ label: row.date, fullLabel: `Run on ${row.date}`, value: row.newPostings }))}
                  height={120}
                  framed={false}
                  labelHeader="Run date"
                  valueHeader="New postings"
                />
              </>
            )}
            {stats.runs.note ? <p className="insights-trend-note">{stats.runs.note}</p> : null}
          </div>
        </>
      ) : null}
    </section>
  );
}

export default function Insights() {
  const { view, stats, csrfToken, userLabel } = useLoaderData<typeof loader>();
  const { theme, toggleTheme } = useTheme();
  const { ctx } = view;

  const signOutForm = (
    <form method="post" action="/auth/logout">
      <input type="hidden" name="_csrf" value={csrfToken} />
      <Button type="submit" variant="ghost" size="sm">
        Sign Out
      </Button>
    </form>
  );

  const offers = view.offersWithoutInterview;
  const [postingsFunnel, applicationsFunnel] = view.funnels;

  return (
    <AppShell
      brand={<Brand />}
      title="Insights"
      userLabel={userLabel}
      signOut={signOutForm}
      navItems={getNavItems("/insights")}
      linkComponent={Link}
      theme={theme}
      onThemeToggle={toggleTheme}
    >
      <div className="insights-page">
        <nav aria-label="Time window" className="insights-window">
          <Link
            to={`/insights?window=90d&asOf=${ctx.asOf}`}
            className="insights-window-link"
            aria-current={ctx.window === "90d" ? "page" : undefined}
          >
            Last 90 days
          </Link>
          <Link
            to={`/insights?window=all&asOf=${ctx.asOf}`}
            className="insights-window-link"
            aria-current={ctx.window === "all" ? "page" : undefined}
          >
            All time
          </Link>
        </nav>
        <p className="insights-basis">
          Counted as of {ctx.asOf} (UTC). {ctx.window === "90d" ? "The last 90 days include that date." : "All time up to and including that date."} Every
          number links to the list it counts.
        </p>

        <BackfillNotice view={view} />

        <FunnelSection funnel={postingsFunnel}>
          <p className="insights-help">
            First seen is the date RoleRadar first recorded a posting, not the date the employer posted it. Postings imported in bulk share one first-seen
            date, so a window can group them together. Reviewed means a posting that is not a lead or unscored, or that has an application; closed postings
            with no application count as expired, not reviewed. It uses each posting&apos;s current disposition, so a posting that was reviewed and later
            returned to lead is not counted as reviewed.
          </p>
          <p className="insights-help">
            <Link to={lensListHref("leads", ctx)}>See all postings in this window</Link>
          </p>
        </FunnelSection>

        <FunnelSection funnel={applicationsFunnel} />
        {offers.state === "ok" && offers.count !== null ? (
          <p className="insights-help">
            <Link to={offers.href} data-lens="offers" data-role="note">
              {offers.count} {offers.count === 1 ? "offer had" : "offers had"} no interview event
            </Link>{" "}
            and {offers.count === 1 ? "is" : "are"} not in the Offers bar, which counts applications with both an offer and an interview.
          </p>
        ) : null}
        <p className="insights-help">
          Applied to counts postings first seen in the window that have an application; Applied counts applications by application date. The two can
          legitimately differ, for example when a posting first seen before the window was applied to inside it.
        </p>

        <section aria-labelledby="rates" className="insights-section">
          <h2 id="rates" className="page-heading">
            Rates
          </h2>
          <p className="insights-basis">Every percentage shows the numbers behind it. A denominator under 10 is a small sample.</p>
          <div className="insights-tiles">
            {view.rates.map((rate) => (
              <RateTile key={rate.id} rate={rate} />
            ))}
            <div className="insights-tile" data-rate="open">
              <StatTile
                label="Open applications"
                value={view.open.count ?? (view.open.state === "needs_backfill" ? "Needs backfill" : "Unavailable")}
                {...(view.open.count !== null && view.open.of !== null ? { of: `of ${view.open.of} applications` } : {})}
              />
              <p className="insights-tile-links">{countLink(view.open, "open", "Open")}</p>
              <p className="insights-help">Applications with no rejected, withdrawn, offer accepted or offer declined event.</p>
            </div>
            <div className="insights-tile" data-rate="rejection-split">
              <StatTile
                label="Rejections by stage"
                value={
                  view.rejectionSplit.before.count !== null && view.rejectionSplit.after.count !== null
                    ? view.rejectionSplit.before.count + view.rejectionSplit.after.count
                    : view.rejectionSplit.before.state === "needs_backfill" || view.rejectionSplit.after.state === "needs_backfill"
                      ? "Needs backfill"
                      : "Unavailable"
                }
              />
              <p className="insights-tile-links">
                {countLink(view.rejectionSplit.before, "rejected-before-interview", "Before any interview")}
                {countLink(view.rejectionSplit.after, "rejected-after-interview", "After an interview")}
              </p>
              <p className="insights-help">Rejections split by whether an interview came first. A same-day interview counts as before the rejection.</p>
            </div>
          </div>
        </section>

        <TrendsSection stats={stats} />

        <section aria-labelledby="how-calculated" className="insights-section">
          <h2 id="how-calculated" className="page-heading">
            How these are calculated
          </h2>
          <p className="insights-help">
            Posting figures (leads, reviewed, applied to, screened out, expired) are counted by first-seen date. Application figures are counted by
            application date. Responded means at least one interview, offer, offer accepted, offer declined or rejected event; follow-ups and withdrawals do
            not count. Applications older than 14 days form the mature cohort for the interview rate, and older than 30 days for the ghost rate. An application exactly 30 days old
            is in the ghost rate denominator (30 days or older) but is not ghosted until it is older than 30 days. Lists open with the same filter,
            window and as-of date, so the number you click and the rows you see agree.
          </p>
        </section>
      </div>
    </AppShell>
  );
}
