using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shapes.Sim;

// Self-contained HTML metrics explorer -- PLAN.md Phase 4 step 2d. The report has outgrown
// reading: one run is ~3,700 lines of JSON and ~1,600 numbers for cards alone, and the console
// output (Program.cs) is a fixed slice chosen in advance. This answers "which cards are outliers
// on take rate AND have intervals tight enough to act on" by putting every card/move in a
// sortable table with a minimum-n filter, plus a client-side diff view (load a second
// --metrics-json file, no server) since step 3/4's loop is edit -> rerun -> compare and static
// text cannot show an interval overlap.
//
// Data is inlined as a JSON <script> block and rendered by vanilla JS -- no CDN, no build step,
// matching every other Shapes.Sim output (nothing here is a server). Lives next to ResultWriter
// as a reporting concern; Shapes.Core stays pure.
public static class HtmlReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // camelCase here (unlike ResultWriter's PascalCase) so the inlined data reads naturally
        // from vanilla JS below; a baseline file loaded from --metrics-json output is normalized
        // to match on load (see camelizeKeys in the template).
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },

        // The default encoder already escapes '<', '>', '&' (and so cannot produce a literal
        // "</script>"), which is exactly the property this inlining needs -- left at its default
        // rather than widened, since UnsafeRelaxedJsonEscaping would remove that protection.
        Encoder = JavaScriptEncoder.Default,
    };

    public static void Write(string path, MetricsReport metrics)
    {
        var json = JsonSerializer.Serialize(metrics, JsonOptions);
        var html = Template.Replace("__METRICS_JSON__", json, StringComparison.Ordinal);
        File.WriteAllText(path, html);
    }

    private const string Template = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>Shapes — Metrics Explorer</title>
<style>
  :root { color-scheme: light dark; }
  body {
    font: 14px/1.4 -apple-system, Segoe UI, sans-serif;
    margin: 0; padding: 24px; background: Canvas; color: CanvasText;
  }
  h1 { font-size: 18px; margin: 0 0 4px; }
  h2 { font-size: 15px; margin: 28px 0 8px; }
  .sub { opacity: 0.7; font-size: 12px; margin-bottom: 20px; }
  .panel {
    border: 1px solid color-mix(in srgb, CanvasText 20%, transparent);
    border-radius: 8px; padding: 12px 16px; margin-bottom: 16px;
  }
  .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(220px, 1fr)); gap: 10px; }
  .stat { padding: 8px 10px; border-radius: 6px; background: color-mix(in srgb, CanvasText 6%, transparent); }
  .stat .label { font-size: 11px; opacity: 0.65; text-transform: uppercase; letter-spacing: 0.03em; }
  .stat .value { font-size: 16px; font-variant-numeric: tabular-nums; }
  .stat .value.flag { color: #d14343; }
  table { border-collapse: collapse; width: 100%; font-variant-numeric: tabular-nums; }
  th, td { text-align: left; padding: 5px 10px; border-bottom: 1px solid color-mix(in srgb, CanvasText 12%, transparent); }
  th { cursor: pointer; user-select: none; font-size: 12px; opacity: 0.75; white-space: nowrap; }
  th:hover { opacity: 1; }
  th .arrow { opacity: 0.5; margin-left: 3px; }
  tr.thin-n { opacity: 0.4; }
  tr.outlier td.take { font-weight: 600; }
  .bar-wrap { position: relative; width: 140px; height: 14px; background: color-mix(in srgb, CanvasText 10%, transparent); border-radius: 3px; }
  .bar { position: absolute; top: 0; bottom: 0; background: color-mix(in srgb, CanvasText 35%, transparent); border-radius: 3px; }
  .bar-point { position: absolute; top: -1px; bottom: -1px; width: 2px; background: CanvasText; }
  .bar-delta { position: absolute; top: -1px; bottom: -1px; width: 2px; background: #d14343; }
  .controls { display: flex; gap: 16px; align-items: center; margin: 10px 0; flex-wrap: wrap; }
  .controls label { font-size: 12px; opacity: 0.8; }
  input[type=number] { width: 60px; }
  input[type=file] { font-size: 12px; }
  .diff-note { font-size: 12px; opacity: 0.7; }
  .toggle-btn {
    font-size: 12px; padding: 4px 10px; border-radius: 5px; cursor: pointer;
    border: 1px solid color-mix(in srgb, CanvasText 25%, transparent); background: transparent; color: CanvasText;
  }
  .toggle-btn.active { background: color-mix(in srgb, CanvasText 15%, transparent); }
  .moved { color: #d14343; font-weight: 600; }
  .not-moved { opacity: 0.55; }
  .hint { font-size: 12px; opacity: 0.7; margin: 4px 0 12px; max-width: 68em; }
  .resource-table td, .resource-table th { padding: 4px 12px; }
  .resource-table th:first-child, .resource-table td:first-child { padding-left: 0; }
  svg.margin-chart { display: block; }
  .margin-chart .zero-line { stroke: color-mix(in srgb, CanvasText 30%, transparent); stroke-width: 1; }
  .margin-chart .margin-line { stroke: color-mix(in srgb, CanvasText 65%, transparent); stroke-width: 1.5; fill: none; }
  .margin-chart .margin-band { fill: color-mix(in srgb, CanvasText 12%, transparent); }
  .margin-chart .axis-label { font-size: 10px; fill: color-mix(in srgb, CanvasText 55%, transparent); }
</style>
</head>
<body>

<h1>Shapes — Metrics Explorer</h1>
<div class="sub" id="provenance-line"></div>

<div class="panel">
  <h2 style="margin-top:0">Batch summary</h2>
  <div class="grid" id="summary-grid"></div>
</div>

<div class="panel">
  <h2 style="margin-top:0">Score margin by turn (seat 1 − seat 2)</h2>
  <p class="hint">
    Mean seat-one score margin at each turn, 95% interval shaded. A lead that widens turn over
    turn is compounding (income/board-state snowball); one that oscillates or flattens is not.
    Later turns average over fewer games (short games have already ended), so read the tail —
    where the shaded band widens — with that in mind.
  </p>
  <div id="margin-chart"></div>
</div>

<div class="panel">
  <h2 style="margin-top:0">Economy</h2>
  <p class="hint">
    <strong>Cost pressure</strong> is the share of decisions where a card sat in hand, legal to
    play in every way except cost — i.e. affordability, not preference, was what stopped it.
    Read it against unspent resources below: <em>high</em> unspent resources with
    <em>low</em> cost pressure means income simply exceeds what there is worth buying (an
    income-level problem); high unspent with <em>high</em> pressure means players are holding the
    wrong resource <em>types</em> for what's in hand (a type-chart / cost-distribution problem).
    Those need opposite fixes, and neither number alone can tell them apart.
    <strong>Cost pressure (batch)</strong> below is this same rate pooled across every card and
    every decision in the run — the one-number version of the per-card column in the Cards table.
  </p>
  <div class="grid" id="economy-grid" style="margin-bottom:16px"></div>
  <p class="hint" style="margin-bottom:6px">
    Unspent resources per turn, split winner/loser and by seat. End-of-game alone would mix the
    winner (just spent everything to close it out) with the loser (starved for turns) into a
    midpoint that describes neither — this samples every turn boundary instead.
  </p>
  <table class="resource-table" id="resource-table">
    <thead></thead>
    <tbody></tbody>
  </table>
</div>

<h2>Cards</h2>
<div class="controls">
  <label>Min n (offers): <input type="number" id="card-min-n" value="0" min="0"></label>
  <button class="toggle-btn active" id="card-outliers-btn">Outliers only (excludes field median)</button>
  <span class="diff-note">Baseline: <input type="file" id="baseline-input" accept="application/json"> <span id="baseline-status"></span></span>
</div>
<table id="card-table">
  <thead></thead>
  <tbody></tbody>
</table>

<h2>Moves</h2>
<div class="controls">
  <label>Min n (offers): <input type="number" id="move-min-n" value="0" min="0"></label>
</div>
<table id="move-table">
  <thead></thead>
  <tbody></tbody>
</table>

<script id="metrics-data" type="application/json">__METRICS_JSON__</script>
<script>
// Recursively lower-cases the first letter of every object key. The inlined report below is
// serialized camelCase for readable JS; a baseline file loaded via the file picker is normally a
// --metrics-json output, which is PascalCase (ResultWriter's convention) -- normalizing on load
// lets the diff view accept either without maintaining two property-name spellings in this script.
function camelizeKeys(value) {
  if (Array.isArray(value)) return value.map(camelizeKeys);
  if (value && typeof value === 'object') {
    const out = {};
    for (const [k, v] of Object.entries(value)) {
      const key = k.charAt(0).toLowerCase() + k.slice(1);
      out[key] = camelizeKeys(v);
    }
    return out;
  }
  return value;
}

const metrics = JSON.parse(document.getElementById('metrics-data').textContent);
let baseline = null;

function pct(x) { return (x * 100).toFixed(1) + '%'; }
function num(x, d) { return (typeof x === 'number') ? x.toFixed(d === undefined ? 2 : d) : '—'; }

function median(values) {
  if (values.length === 0) return 0;
  const s = [...values].sort((a, b) => a - b);
  const mid = Math.floor(s.length / 2);
  return s.length % 2 ? s[mid] : (s[mid - 1] + s[mid]) / 2;
}

function excludes(interval, reference) {
  return reference < interval.low || reference > interval.high;
}

function renderProvenance() {
  const p = metrics.provenance;
  const el = document.getElementById('provenance-line');
  if (!p) { el.textContent = `${metrics.gameCount} games (no provenance recorded)`; return; }
  el.textContent =
    `${p.ruleSetName}  ·  ${p.cardCount} cards (hash ${p.cardSetHash})  ·  agents: ${p.agents.join(', ')}  ·  `
    + `${p.gamesPerPairing} games/pairing  ·  ${p.iterations} iterations  ·  seed ${p.baseSeed}  ·  `
    + `${metrics.gameCount} games total  ·  run at ${new Date(p.runAtUtc).toISOString()}`;
}

function statTile(label, value, flag) {
  return `<div class="stat"><div class="label">${label}</div><div class="value${flag ? ' flag' : ''}">${value}</div></div>`;
}

function renderSummary() {
  const m = metrics;
  const grid = document.getElementById('summary-grid');
  const marginFlag = excludes(m.finalScoreMargin, 0);
  const tiles = [
    statTile('Seat 1 win rate', pct(m.seatOneWinRate.rate) + ` [${pct(m.seatOneWinRate.low)}, ${pct(m.seatOneWinRate.high)}]`),
    statTile('Seat 2 win rate', pct(m.seatTwoWinRate.rate) + ` [${pct(m.seatTwoWinRate.low)}, ${pct(m.seatTwoWinRate.high)}]`),
    statTile('Score margin (P1-P2)', num(m.finalScoreMargin.mean) + ` [${num(m.finalScoreMargin.low)}, ${num(m.finalScoreMargin.high)}]`, marginFlag),
    statTile('Decisiveness |margin|', num(m.absoluteScoreMargin.mean)),
    statTile('Game length', num(m.gameLength.mean) + ' turns'),
    statTile('Move usage rate', pct(m.moveUsageRate)),
    statTile('Merges/game', num(m.mergesPerGame)),
    statTile('Merge take rate', pct(m.mergeTakeRate.rate)),
    statTile('Unopposed slot rate', pct(m.unopposedSlotRate.rate)),
    statTile('Longest unopposed streak', num(m.longestUnopposedStreak.mean) + ' steps'),
    statTile('No sustained unopposed', `${m.gamesWithNoSustainedUnopposed} / ${m.gameCount} games`),
    statTile('Cards drawn/game (winners)', num(m.cardsDrawnWinners.mean) + ` [${num(m.cardsDrawnWinners.low)}, ${num(m.cardsDrawnWinners.high)}]`),
    statTile('Cards drawn/game (losers)', num(m.cardsDrawnLosers.mean) + ` [${num(m.cardsDrawnLosers.low)}, ${num(m.cardsDrawnLosers.high)}]`),
  ];
  grid.innerHTML = tiles.join('');
}

function renderEconomy() {
  const m = metrics;
  document.getElementById('economy-grid').innerHTML =
    statTile('Cost pressure (batch)', pct(m.costPressure.rate) + ` [${pct(m.costPressure.low)}, ${pct(m.costPressure.high)}]`);

  const rows = [
    ['Winners', m.resourcesWinners],
    ['Losers', m.resourcesLosers],
    ['Seat 1', m.resourcesSeatOne],
    ['Seat 2', m.resourcesSeatTwo],
  ];
  document.querySelector('#resource-table thead').innerHTML =
    '<tr><th>Population</th><th>Spike △</th><th>Anvil ▢</th><th>Wheel ◯</th></tr>';
  document.querySelector('#resource-table tbody').innerHTML = rows.map(([label, profile]) => `
    <tr>
      <td>${label}</td>
      <td>${num(profile.spike.mean)} <span class="hint" style="margin:0">[${num(profile.spike.low)}, ${num(profile.spike.high)}]</span></td>
      <td>${num(profile.anvil.mean)} <span class="hint" style="margin:0">[${num(profile.anvil.low)}, ${num(profile.anvil.high)}]</span></td>
      <td>${num(profile.wheel.mean)} <span class="hint" style="margin:0">[${num(profile.wheel.low)}, ${num(profile.wheel.high)}]</span></td>
    </tr>`).join('');
}

// Line + shaded-interval SVG chart for ScoreMarginByTurn -- no charting library, matching the
// rest of the page. Simple enough to hand-roll: one polyline for the mean, one filled band for
// the 95% interval, a zero reference line since the entire read is "does this diverge from 0."
function renderMarginChart() {
  const series = metrics.scoreMarginByTurn;
  const container = document.getElementById('margin-chart');
  if (!series || series.length === 0) {
    container.innerHTML = '<p class="hint">No per-turn data in this report.</p>';
    return;
  }

  const width = 900, height = 220, padL = 40, padR = 12, padT = 12, padB = 24;
  const plotW = width - padL - padR, plotH = height - padT - padB;

  const allLows = series.map(s => s.low), allHighs = series.map(s => s.high);
  const yMin = Math.min(0, ...allLows), yMax = Math.max(0, ...allHighs);
  const yPad = Math.max((yMax - yMin) * 0.08, 0.5);
  const y0 = yMin - yPad, y1 = yMax + yPad;

  const x = (i) => padL + (series.length === 1 ? 0 : (i / (series.length - 1)) * plotW);
  const y = (v) => padT + plotH - ((v - y0) / (y1 - y0)) * plotH;

  const meanPoints = series.map((s, i) => `${x(i)},${y(s.mean)}`).join(' ');
  const bandTop = series.map((s, i) => `${x(i)},${y(s.high)}`);
  const bandBottom = series.map((s, i) => `${x(i)},${y(s.low)}`).reverse();
  const bandPath = [...bandTop, ...bandBottom].join(' ');

  const zeroY = y(0);
  const turnTicks = [0, Math.floor(series.length / 2), series.length - 1];

  container.innerHTML = `
    <svg class="margin-chart" viewBox="0 0 ${width} ${height}" width="100%" style="max-width:${width}px">
      <polygon class="margin-band" points="${bandPath}"></polygon>
      <line class="zero-line" x1="${padL}" y1="${zeroY}" x2="${width - padR}" y2="${zeroY}"></line>
      <polyline class="margin-line" points="${meanPoints}"></polyline>
      ${turnTicks.map(i => `<text class="axis-label" x="${x(i)}" y="${height - 6}" text-anchor="middle">turn ${i + 1} (n=${series[i].count})</text>`).join('')}
      <text class="axis-label" x="${padL - 6}" y="${zeroY + 3}" text-anchor="end">0</text>
      <text class="axis-label" x="${padL - 6}" y="${y(y1) + 8}" text-anchor="end">${num(y1, 1)}</text>
      <text class="axis-label" x="${padL - 6}" y="${y(y0)}" text-anchor="end">${num(y0, 1)}</text>
    </svg>`;
}

// --- Sortable, filterable card/move tables -------------------------------------------------

let cardSort = { key: 'playTakeRate', dir: -1 };
let moveSort = { key: 'useTakeRate', dir: -1 };
let cardOutliersOnly = true;

function intervalBar(interval, refLine, deltaInterval) {
  const low = interval.low * 100, high = interval.high * 100, rate = interval.rate * 100;
  const width = Math.max(high - low, 0.5);
  let extra = '';
  if (refLine !== undefined) {
    extra += `<div class="bar-point" style="left:${refLine * 100}%"></div>`;
  }
  if (deltaInterval) {
    extra += `<div class="bar-delta" style="left:${deltaInterval.rate * 100}%"></div>`;
  }
  return `<div class="bar-wrap"><div class="bar" style="left:${low}%; width:${width}%"></div>${extra}</div>`;
}

function cardRows() {
  const fieldMedian = median(metrics.cardStats.filter(c => c.offerCount > 0).map(c => c.playTakeRate.rate));
  const minN = parseInt(document.getElementById('card-min-n').value, 10) || 0;
  const baselineById = baseline ? Object.fromEntries(baseline.cardStats.map(c => [c.cardId, c])) : null;

  let rows = metrics.cardStats.filter(c => c.offerCount >= minN);
  if (cardOutliersOnly && !baseline) {
    rows = rows.filter(c => c.offerCount > 0 && excludes(c.playTakeRate, fieldMedian));
  }

  rows = [...rows].sort((a, b) => {
    const get = (c) => cardSort.key.split('.').reduce((o, k) => o && o[k], c);
    const av = get(a), bv = get(b);
    if (av === bv) return 0;
    return (av > bv ? 1 : -1) * cardSort.dir;
  });

  return rows.map(c => {
    const thin = c.offerCount < 20;
    const bl = baselineById ? baselineById[c.cardId] : null;
    let deltaCell = '';
    let moveClass = '';
    if (bl) {
      const delta = c.playTakeRate.rate - bl.playTakeRate.rate;
      const moved = excludes(c.playTakeRate, bl.playTakeRate.rate) || excludes(bl.playTakeRate, c.playTakeRate.rate);
      moveClass = moved ? 'moved' : 'not-moved';
      deltaCell = `<td class="${moveClass}">${delta >= 0 ? '+' : ''}${pct(delta)}</td>`;
    }
    return `<tr class="${thin ? 'thin-n' : ''} ${cardOutliersOnly ? 'outlier' : ''}">
      <td>${c.cardId}</td>
      <td class="take">${pct(c.playTakeRate.rate)}</td>
      <td>${intervalBar(c.playTakeRate, bl ? undefined : fieldMedian, bl ? bl.playTakeRate : undefined)}</td>
      ${deltaCell}
      <td>${c.offerCount}</td>
      <td>${c.playCount}</td>
      <td>${pct(c.winRateWhenPlayed.rate)}</td>
      <td>${pct(c.winRateWhenDrawn.rate)}</td>
      <td>${pct(c.costPressure.rate)}</td>
      <td>${c.survivalSteps.count > 0 ? num(c.survivalSteps.mean, 1) : '—'}</td>
      <td>${c.survivalSteps.count > 0 ? pct(c.scoredWhileAliveRate.rate) : '—'}</td>
    </tr>`;
  }).join('');
}

function cardHeader() {
  const cols = [
    ['cardId', 'Card'],
    ['playTakeRate.rate', 'Take rate'],
    [null, 'Interval'],
    ...(baseline ? [[null, 'Δ vs baseline']] : []),
    ['offerCount', 'Offers (n)'],
    ['playCount', 'Plays'],
    ['winRateWhenPlayed.rate', 'Win (played)'],
    ['winRateWhenDrawn.rate', 'Win (drawn)'],
    ['costPressure.rate', 'Cost pressure'],
    ['survivalSteps.mean', 'Survival (steps)'],
    ['scoredWhileAliveRate.rate', 'Scored while alive'],
  ];
  return '<tr>' + cols.map(([key, label]) => {
    if (!key) return `<th>${label}</th>`;
    const arrow = cardSort.key === key ? (cardSort.dir === 1 ? '▲' : '▼') : '';
    return `<th data-key="${key}">${label} <span class="arrow">${arrow}</span></th>`;
  }).join('') + '</tr>';
}

function renderCardTable() {
  document.querySelector('#card-table thead').innerHTML = cardHeader();
  document.querySelector('#card-table tbody').innerHTML = cardRows();
  document.querySelectorAll('#card-table th[data-key]').forEach(th => {
    th.addEventListener('click', () => {
      const key = th.dataset.key;
      cardSort = { key, dir: cardSort.key === key ? -cardSort.dir : -1 };
      renderCardTable();
    });
  });
}

function moveRows() {
  const minN = parseInt(document.getElementById('move-min-n').value, 10) || 0;
  let rows = metrics.moveStats.filter(m => m.offerCount >= minN);
  rows = [...rows].sort((a, b) => {
    const get = (m) => moveSort.key.split('.').reduce((o, k) => o && o[k], m);
    const av = get(a), bv = get(b);
    if (av === bv) return 0;
    return (av > bv ? 1 : -1) * moveSort.dir;
  });
  return rows.map(m => {
    const thin = m.offerCount < 20;
    return `<tr class="${thin ? 'thin-n' : ''}">
      <td>${m.moveName}</td>
      <td>${m.cardId}</td>
      <td class="take">${pct(m.useTakeRate.rate)}</td>
      <td>${intervalBar(m.useTakeRate)}</td>
      <td>${m.offerCount}</td>
      <td>${m.useCount}</td>
      <td>${pct(m.winRateWhenUsed.rate)}</td>
    </tr>`;
  }).join('');
}

function moveHeader() {
  const cols = [
    ['moveName', 'Move'],
    ['cardId', 'Card'],
    ['useTakeRate.rate', 'Take rate'],
    [null, 'Interval'],
    ['offerCount', 'Offers (n)'],
    ['useCount', 'Uses'],
    ['winRateWhenUsed.rate', 'Win rate'],
  ];
  return '<tr>' + cols.map(([key, label]) => {
    if (!key) return `<th>${label}</th>`;
    const arrow = moveSort.key === key ? (moveSort.dir === 1 ? '▲' : '▼') : '';
    return `<th data-key="${key}">${label} <span class="arrow">${arrow}</span></th>`;
  }).join('') + '</tr>';
}

function renderMoveTable() {
  document.querySelector('#move-table thead').innerHTML = moveHeader();
  document.querySelector('#move-table tbody').innerHTML = moveRows();
  document.querySelectorAll('#move-table th[data-key]').forEach(th => {
    th.addEventListener('click', () => {
      const key = th.dataset.key;
      moveSort = { key, dir: moveSort.key === key ? -moveSort.dir : -1 };
      renderMoveTable();
    });
  });
}

document.getElementById('card-min-n').addEventListener('input', renderCardTable);
document.getElementById('move-min-n').addEventListener('input', renderMoveTable);
document.getElementById('card-outliers-btn').addEventListener('click', (e) => {
  cardOutliersOnly = !cardOutliersOnly;
  e.target.classList.toggle('active', cardOutliersOnly);
  renderCardTable();
});
document.getElementById('baseline-input').addEventListener('change', (e) => {
  const file = e.target.files[0];
  if (!file) return;
  const reader = new FileReader();
  reader.onload = () => {
    try {
      const parsed = camelizeKeys(JSON.parse(reader.result));
      baseline = parsed.metrics || parsed;
      document.getElementById('baseline-status').textContent = `loaded (${baseline.gameCount} games)`;
      cardOutliersOnly = false;
      document.getElementById('card-outliers-btn').classList.remove('active');
      renderCardTable();
    } catch (err) {
      document.getElementById('baseline-status').textContent = 'failed to parse: ' + err.message;
    }
  };
  reader.readAsText(file);
});

renderProvenance();
renderSummary();
renderMarginChart();
renderEconomy();
renderCardTable();
renderMoveTable();
</script>
</body>
</html>
""";
}
