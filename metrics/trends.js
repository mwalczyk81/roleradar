// RoleRadar test-quality trends page (feature 011, FR-017, FR-018).
//
// Reads metrics.jsonl (one JSON record per main run, appended by web/scripts/metrics-record.mjs) and draws small sparklines.
// Every field is untrusted text: lines that fail validation are skipped and counted visibly, numbers must be finite,
// and values are written only with textContent and setAttribute on elements created here. No HTML is built from strings,
// nothing is evaluated and there is no third-party script. Record shape: specs/011-test-quality-reporting/contracts/metrics-record.md.

const SHA_PATTERN = /^[0-9a-f]{40}$/;
const SVG_NAMESPACE = "http://www.w3.org/2000/svg";
const SPARKLINE_WIDTH = 240;
const SPARKLINE_HEIGHT = 48;

/** @param {unknown} value @returns {value is Record<string, unknown>} */
function isObject(value) {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

/** @param {unknown} value @returns {number | null} */
function finiteNumber(value) {
  return typeof value === "number" && Number.isFinite(value) ? value : null;
}

/**
 * Sum of the passed counts across the lanes, counting only lanes whose passed value is a finite number. Null when no
 * lane has one, so a missing figure is never drawn as zero.
 * @param {unknown} tests
 * @returns {number | null}
 */
function sumPassed(tests) {
  if (!isObject(tests)) {
    return null;
  }
  let total = 0;
  let any = false;
  for (const lane of Object.values(tests)) {
    const passed = isObject(lane) ? finiteNumber(lane["passed"]) : null;
    if (passed !== null) {
      total += passed;
      any = true;
    }
  }
  return any ? total : null;
}

/**
 * @typedef {{ commit: string, timestampUtc: string, time: number, testsPassed: number | null, coverageLinePct: number | null, mutationWebLib: number | null, mutationNote: string | null, jobDurations: Array<{ name: string, seconds: number }> }} MetricRecord
 */

/**
 * @param {unknown} value
 * @returns {MetricRecord | null}
 */
function toRecord(value) {
  if (!isObject(value) || value["schemaVersion"] !== 1) {
    return null;
  }
  const commit = value["commit"];
  const timestamp = value["timestampUtc"];
  if (typeof commit !== "string" || !SHA_PATTERN.test(commit) || typeof timestamp !== "string") {
    return null;
  }
  const time = Date.parse(timestamp);
  if (Number.isNaN(time)) {
    return null;
  }
  const coverage = isObject(value["coverage"]) ? value["coverage"] : null;
  const mutation = isObject(value["mutation"]) ? value["mutation"] : null;
  const mutationScore = mutation ? finiteNumber(mutation["web-lib"]) : null;
  let mutationNote = null;
  if (mutation && mutationScore !== null && typeof mutation["runDate"] === "string" && typeof mutation["commit"] === "string") {
    mutationNote = `nightly run ${mutation["runDate"]}, commit ${mutation["commit"].slice(0, 7)}`;
  }
  const durations = [];
  const rawDurations = value["jobDurationsSeconds"];
  if (isObject(rawDurations)) {
    for (const [name, seconds] of Object.entries(rawDurations)) {
      const parsed = finiteNumber(seconds);
      if (parsed !== null) {
        durations.push({ name, seconds: parsed });
      }
    }
  }
  return {
    commit,
    timestampUtc: timestamp,
    time,
    testsPassed: sumPassed(value["tests"]),
    coverageLinePct: coverage ? finiteNumber(coverage["overallLinePct"]) : null,
    // A mutation score is shown only with its own provenance (run date and commit), never without it.
    mutationWebLib: mutationNote === null ? null : mutationScore,
    mutationNote,
    jobDurations: durations,
  };
}

/**
 * @param {string} text contents of metrics.jsonl
 * @returns {{ records: MetricRecord[], skipped: number }}
 */
export function parseMetricsText(text) {
  /** @type {MetricRecord[]} */
  const records = [];
  let skipped = 0;
  for (const line of String(text).split(/\r?\n/)) {
    if (line.trim() === "") {
      continue;
    }
    let parsed;
    try {
      parsed = JSON.parse(line);
    } catch {
      skipped += 1;
      continue;
    }
    const record = toRecord(parsed);
    if (record === null) {
      skipped += 1;
    } else {
      records.push(record);
    }
  }
  records.sort((a, b) => a.time - b.time);
  return { records, skipped };
}

/**
 * @param {MetricRecord[]} records
 * @returns {Array<{ key: string, label: string, unit: string, points: Array<{ value: number, commit: string, timestampUtc: string, note: string | null }> }>}
 */
export function buildSeries(records) {
  const definitions = [
    { key: "tests", label: "Tests passed", unit: "", pick: (/** @type {MetricRecord} */ r) => ({ value: r.testsPassed, note: null }) },
    { key: "coverage", label: "Line coverage", unit: "%", pick: (/** @type {MetricRecord} */ r) => ({ value: r.coverageLinePct, note: null }) },
    {
      key: "mutation",
      label: "Mutation score (web lib)",
      unit: "%",
      pick: (/** @type {MetricRecord} */ r) => ({ value: r.mutationWebLib, note: r.mutationNote }),
    },
  ];
  const series = [];
  for (const definition of definitions) {
    const points = [];
    for (const record of records) {
      const { value, note } = definition.pick(record);
      if (value !== null) {
        points.push({ value, commit: record.commit, timestampUtc: record.timestampUtc, note });
      }
    }
    if (points.length > 0) {
      series.push({ key: definition.key, label: definition.label, unit: definition.unit, points });
    }
  }
  return series;
}

/** @param {Array<{ value: number }>} points @returns {string} */
function sparklinePoints(points) {
  const values = points.map((p) => p.value);
  const min = Math.min(...values);
  const max = Math.max(...values);
  const span = max - min;
  const step = points.length > 1 ? SPARKLINE_WIDTH / (points.length - 1) : 0;
  return points
    .map((point, index) => {
      const x = points.length > 1 ? index * step : SPARKLINE_WIDTH / 2;
      const y = span === 0 ? SPARKLINE_HEIGHT / 2 : SPARKLINE_HEIGHT - ((point.value - min) / span) * SPARKLINE_HEIGHT;
      return `${x.toFixed(1)},${y.toFixed(1)}`;
    })
    .join(" ");
}

/**
 * @param {Document} doc
 * @param {string} tag
 * @param {string} [text]
 * @param {string} [className]
 */
function element(doc, tag, text, className) {
  const node = doc.createElement(tag);
  if (text !== undefined) {
    node.textContent = text;
  }
  if (className !== undefined) {
    node.className = className;
  }
  return node;
}

/**
 * Replaces the contents of root. Text goes in with textContent only.
 * @param {Document} doc
 * @param {HTMLElement} root
 * @param {ReturnType<typeof parseMetricsText>} parsed
 */
export function renderTrends(doc, root, parsed) {
  root.replaceChildren();

  if (parsed.records.length === 0) {
    root.append(element(doc, "p", "No metrics recorded yet. A record is added after each run on the main branch.", "empty"));
  }

  const series = buildSeries(parsed.records);
  for (const entry of series) {
    const section = doc.createElement("section");
    section.dataset["series"] = entry.key;
    section.append(element(doc, "h2", entry.label));
    const last = entry.points[entry.points.length - 1];
    if (last) {
      const note = last.note ? ` (${last.note})` : "";
      section.append(element(doc, "p", `Latest: ${last.value}${entry.unit}${note}`, "latest"));
    }
    const svg = doc.createElementNS(SVG_NAMESPACE, "svg");
    svg.setAttribute("viewBox", `0 0 ${SPARKLINE_WIDTH} ${SPARKLINE_HEIGHT}`);
    svg.setAttribute("width", String(SPARKLINE_WIDTH));
    svg.setAttribute("height", String(SPARKLINE_HEIGHT));
    svg.setAttribute("role", "img");
    svg.setAttribute("aria-label", `${entry.label}, ${entry.points.length} points`);
    const line = doc.createElementNS(SVG_NAMESPACE, "polyline");
    line.setAttribute("points", sparklinePoints(entry.points));
    line.setAttribute("fill", "none");
    svg.append(line);
    section.append(svg);
    root.append(section);
  }

  const latestRecord = parsed.records[parsed.records.length - 1];
  if (latestRecord && latestRecord.jobDurations.length > 0) {
    const section = doc.createElement("section");
    section.dataset["series"] = "durations";
    section.append(element(doc, "h2", "Job durations, latest run"));
    const table = doc.createElement("table");
    const header = doc.createElement("tr");
    header.append(element(doc, "th", "Job"), element(doc, "th", "Seconds"));
    table.append(header);
    for (const job of latestRecord.jobDurations) {
      const row = doc.createElement("tr");
      row.append(element(doc, "td", job.name), element(doc, "td", String(job.seconds)));
      table.append(row);
    }
    section.append(table);
    root.append(section);
  }

  root.append(element(doc, "p", `Skipped lines: ${parsed.skipped}`, "skipped"));
}

/**
 * Loads metrics.jsonl next to this page and renders it.
 * @param {Document} doc
 * @param {typeof fetch} fetchImplementation
 */
export async function boot(doc, fetchImplementation) {
  const root = doc.getElementById("trends-root");
  if (!root) {
    return;
  }
  try {
    const response = await fetchImplementation("metrics.jsonl", { cache: "no-store" });
    if (!response.ok) {
      throw new Error(`HTTP ${response.status}`);
    }
    renderTrends(doc, root, parseMetricsText(await response.text()));
  } catch (error) {
    root.replaceChildren(element(doc, "p", `Could not load metrics.jsonl (${error instanceof Error ? error.message : "unknown error"}).`, "error"));
  }
}

if (typeof document !== "undefined" && typeof fetch === "function" && document.getElementById("trends-root")) {
  void boot(document, fetch);
}
