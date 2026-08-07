using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shapes.Sim;

// Standalone A/B comparison report: two --metrics-json files in, one self-contained HTML diff
// out. HtmlReportWriter already has a diff view, but it is a manual step inside a single-report
// page (load a second file through a picker) -- this is the CLI-first counterpart for the step
// 3/4 loop (edit -> rerun -> compare), so "compare these two runs" is one command producing one
// shareable artifact, not "open A's report, then remember to load B."
//
// Same rendering approach as HtmlReportWriter: data inlined as a JSON <script> block, vanilla JS,
// no CDN, no build step, no server. Lives next to it as a reporting concern -- Shapes.Core stays
// pure.
public static class CompareReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        Encoder = JavaScriptEncoder.Default,
    };

    public static void Write(string path, MetricsReport baseline, MetricsReport candidate)
    {
        var payload = JsonSerializer.Serialize(new { baseline, candidate }, JsonOptions);
        var html = Template.Replace("__COMPARE_JSON__", payload, StringComparison.Ordinal);
        File.WriteAllText(path, html);
    }

    private const string Template = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>Shapes — Compare Reports</title>
<style>
  :root { color-scheme: light dark; }
  body {
    font: 14px/1.4 -apple-system, Segoe UI, sans-serif;
    margin: 0; padding: 24px; background: Canvas; color: CanvasText;
  }
  h1 { font-size: 18px; margin: 0 0 4px; }
  h2 { font-size: 15px; margin: 28px 0 8px; }
  .sub { opacity: 0.7; font-size: 12px; margin-bottom: 4px; }
  .panel {
    border: 1px solid color-mix(in srgb, CanvasText 20%, transparent);
    border-radius: 8px; padding: 12px 16px; margin-bottom: 16px;
  }
  .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(240px, 1fr)); gap: 10px; }
  .stat { padding: 8px 10px; border-radius: 6px; background: color-mix(in srgb, CanvasText 6%, transparent); }
  .stat .label { font-size: 11px; opacity: 0.65; text-transform: uppercase; letter-spacing: 0.03em; }
  .stat .value { font-size: 15px; font-variant-numeric: tabular-nums; }
  .stat .row { display: flex; justify-content: space-between; gap: 8px; }
  .stat .delta { font-size: 13px; font-variant-numeric: tabular-nums; }
  table { border-collapse: collapse; width: 100%; font-variant-numeric: tabular-nums; }
  th, td { text-align: left; padding: 5px 10px; border-bottom: 1px solid color-mix(in srgb, CanvasText 12%, transparent); }
  th { cursor: pointer; user-select: none; font-size: 12px; opacity: 0.75; white-space: nowrap; }
  th:hover { opacity: 1; }
  th .arrow { opacity: 0.5; margin-left: 3px; }
  tr.thin-n { opacity: 0.4; }
  .moved-up { color: #2e8b3d; font-weight: 600; }
  .moved-down { color: #d14343; font-weight: 600; }
  .not-moved { opacity: 0.55; }
  .controls { display: flex; gap: 16px; align-items: center; margin: 10px 0; flex-wrap: wrap; }
  .controls label { font-size: 12px; opacity: 0.8; }
  input[type=number] { width: 60px; }
  .toggle-btn {
    font-size: 12px; padding: 4px 10px; border-radius: 5px; cursor: pointer;
    border: 1px solid color-mix(in srgb, CanvasText 25%, transparent); background: transparent; color: CanvasText;
  }
  .toggle-btn.active { background: color-mix(in srgb, CanvasText 15%, transparent); }
  .hint { font-size: 12px; opacity: 0.7; margin: 4px 0 12px; max-width: 68em; }
  .resource-table td, .resource-table th { padding: 4px 12px; }
  .resource-table th:first-child, .resource-table td:first-child { padding-left: 0; }
  .legend { font-size: 12px; opacity: 0.75; margin: 2px 0 14px; }
  .legend .swatch { display: inline-block; width: 10px; height: 10px; border-radius: 2px; margin-right: 4px; vertical-align: middle; }
  .legend .b { background: color-mix(in srgb, CanvasText 40%, transparent); }
  .legend .c { background: #4a78d1; }
</style>
</head>
<body>

<h1>Shapes — Compare Reports</h1>
<div class="sub" id="baseline-line"></div>
<div class="sub" id="candidate-line" style="margin-bottom:16px"></div>
<p class="legend">
  <span class="swatch b"></span>baseline &nbsp; <span class="swatch c"></span>candidate &nbsp;·&nbsp;
  <span class="moved-up">green</span> = candidate higher and intervals don't overlap &nbsp;
  <span class="moved-down">red</span> = candidate lower and intervals don't overlap &nbsp;
  <span class="not-moved">dim</span> = change within noise
</p>

<div class="panel">
  <h2 style="margin-top:0">Batch summary</h2>
  <div class="grid" id="summary-grid"></div>
</div>

<div class="panel">
  <h2 style="margin-top:0">Scoring &amp; unopposed occupancy</h2>
  <p class="hint">
    A <em>low</em> unopposed-slot rate means slots are hard to keep unopposed and each is worth a
    lot (tune <code>pointsPerUnopposedCreature</code>); a <em>high</em> rate means they come
    easily and points follow (tune board size, removal, durability). The streak figures are step
    4.2's income-compounding finding as a standing metric.
  </p>
  <div class="grid" id="scoring-grid"></div>
</div>

<div class="panel">
  <h2 style="margin-top:0">Economy</h2>
  <p class="hint">
    High unspent resources with <em>low</em> cost pressure means income exceeds what there is to
    buy (an income-level problem); high unspent with <em>high</em> pressure means players hold the
    wrong resource <em>types</em> (a cost-distribution problem). Read the two together.
  </p>
  <div class="grid" id="economy-grid" style="margin-bottom:16px"></div>
  <table class="resource-table" id="resource-table">
    <thead></thead>
    <tbody></tbody>
  </table>
</div>

<h2>Cards</h2>
<p class="hint">
  <strong>Power score</strong> is the same opinionated rollup as the single-report metrics
  explorer's Power score column: a z-score average of take rate, take rate/turn, and win rate
  played/drawn, plus (creatures only) a take-rate-weighted mean of the card's own moves' take
  rate and win rate. Computed independently against EACH run's own field (baseline's mean/stdev
  for the baseline score, candidate's for the candidate score), so a card's z-score can move
  between runs even if its raw take rate barely changed, if the rest of the field moved around it.
  Δ power score colors when the change clears ±0.5, since this metric carries no confidence
  interval to test for overlap the way the rate columns do. Read the individual rate columns
  before trusting it — it inherits every caveat those already carry.
</p>
<div class="controls">
  <label>Min n (offers, either run): <input type="number" id="card-min-n" value="0" min="0"></label>
  <button class="toggle-btn active" id="card-moved-btn">Moved beyond noise only</button>
</div>
<table id="card-table">
  <thead></thead>
  <tbody></tbody>
</table>

<h2>Moves</h2>
<div class="controls">
  <label>Min n (offers, either run): <input type="number" id="move-min-n" value="0" min="0"></label>
  <button class="toggle-btn active" id="move-moved-btn">Moved beyond noise only</button>
</div>
<table id="move-table">
  <thead></thead>
  <tbody></tbody>
</table>

<script id="compare-data" type="application/json">__COMPARE_JSON__</script>
<script>
const data = JSON.parse(document.getElementById('compare-data').textContent);
const baseline = data.baseline;
const candidate = data.candidate;

function pct(x) { return (x * 100).toFixed(1) + '%'; }
function num(x, d) { return (typeof x === 'number') ? x.toFixed(d === undefined ? 2 : d) : '—'; }
function signedPct(x) { return (x >= 0 ? '+' : '') + pct(x); }
function signedNum(x, d) { return (x >= 0 ? '+' : '') + num(x, d); }

// True when two Wilson/normal intervals don't overlap -- "moved beyond noise," not just "moved."
function intervalsDisjoint(a, b) { return a.high < b.low || b.high < a.low; }

function provenanceLine(label, p, gameCount) {
  if (!p) return `${label}: ${gameCount} games (no provenance recorded)`;
  return `${label}: ${p.ruleSetName}  ·  ${p.cardCount} cards (hash ${p.cardSetHash})  ·  `
    + `agents: ${p.agents.join(', ')}  ·  ${p.gamesPerPairing} games/pairing  ·  `
    + `${p.iterations} iterations  ·  seed ${p.baseSeed}  ·  ${gameCount} games total  ·  `
    + `run at ${new Date(p.runAtUtc).toISOString()}`;
}

function statTileRate(label, b, c) {
  const moved = intervalsDisjoint(b, c);
  const cls = moved ? (c.rate > b.rate ? 'moved-up' : 'moved-down') : 'not-moved';
  return `<div class="stat">
    <div class="label">${label}</div>
    <div class="row"><span class="value">${pct(b.rate)} → ${pct(c.rate)}</span>
      <span class="delta ${cls}">${signedPct(c.rate - b.rate)}</span></div>
  </div>`;
}

function statTileMean(label, b, c, d) {
  const moved = intervalsDisjoint(b, c);
  const cls = moved ? (c.mean > b.mean ? 'moved-up' : 'moved-down') : 'not-moved';
  return `<div class="stat">
    <div class="label">${label}</div>
    <div class="row"><span class="value">${num(b.mean, d)} → ${num(c.mean, d)}</span>
      <span class="delta ${cls}">${signedNum(c.mean - b.mean, d)}</span></div>
  </div>`;
}

function statTilePlain(label, bVal, cVal) {
  return `<div class="stat">
    <div class="label">${label}</div>
    <div class="row"><span class="value">${bVal} → ${cVal}</span></div>
  </div>`;
}

function renderSummary() {
  const b = baseline, c = candidate;
  document.getElementById('summary-grid').innerHTML = [
    statTileRate('Seat 1 win rate', b.seatOneWinRate, c.seatOneWinRate),
    statTileRate('Seat 2 win rate', b.seatTwoWinRate, c.seatTwoWinRate),
    statTileMean('Score margin (P1-P2)', b.finalScoreMargin, c.finalScoreMargin),
    statTileMean('Decisiveness |margin|', b.absoluteScoreMargin, c.absoluteScoreMargin),
    statTileMean('Game length (turns)', b.gameLength, c.gameLength, 1),
    statTilePlain('Move usage rate', pct(b.moveUsageRate), pct(c.moveUsageRate)),
    statTilePlain('Merges/game', num(b.mergesPerGame), num(c.mergesPerGame)),
    statTileRate('Merge take rate', b.mergeTakeRate, c.mergeTakeRate),
  ].join('');
}

function renderScoring() {
  const b = baseline, c = candidate;
  document.getElementById('scoring-grid').innerHTML = [
    statTileRate('Unopposed slot rate', b.unopposedSlotRate, c.unopposedSlotRate),
    statTileMean('Unopposed creatures/step', b.unopposedCreaturesPerStep, c.unopposedCreaturesPerStep),
    statTileMean('Longest unopposed streak', b.longestUnopposedStreak, c.longestUnopposedStreak, 1),
    statTilePlain(
      'No sustained unopposed',
      `${b.gamesWithNoSustainedUnopposed}/${b.gameCount}`,
      `${c.gamesWithNoSustainedUnopposed}/${c.gameCount}`),
  ].join('');
}

function renderEconomy() {
  const b = baseline, c = candidate;
  document.getElementById('economy-grid').innerHTML =
    statTileRate('Cost pressure (batch)', b.costPressure, c.costPressure);

  const rows = [
    ['Winners', b.resourcesWinners, c.resourcesWinners],
    ['Losers', b.resourcesLosers, c.resourcesLosers],
    ['Seat 1', b.resourcesSeatOne, c.resourcesSeatOne],
    ['Seat 2', b.resourcesSeatTwo, c.resourcesSeatTwo],
  ];
  document.querySelector('#resource-table thead').innerHTML =
    '<tr><th>Population</th><th>Spike △</th><th>Anvil ▢</th><th>Wheel ◯</th></tr>';

  function resCell(bm, cm) {
    const moved = intervalsDisjoint(bm, cm);
    const cls = moved ? (cm.mean > bm.mean ? 'moved-up' : 'moved-down') : 'not-moved';
    return `<td>${num(bm.mean)} → ${num(cm.mean)} <span class="${cls}">(${signedNum(cm.mean - bm.mean)})</span></td>`;
  }

  document.querySelector('#resource-table tbody').innerHTML = rows.map(([label, bp, cp]) => `
    <tr>
      <td>${label}</td>
      ${resCell(bp.spike, cp.spike)}
      ${resCell(bp.anvil, cp.anvil)}
      ${resCell(bp.wheel, cp.wheel)}
    </tr>`).join('');
}

// --- Composite power score, ported from HtmlReportWriter's report.html -----------------------
//
// Same formula as the single-report explorer's Power score column (see HtmlReportWriter.cs for
// the full rationale): a z-score average of take rate, take rate/turn, win rate played/drawn,
// plus (creatures only) a take-rate-weighted mean of the card's own moves' take rate and win
// rate. Computed independently for baseline and candidate (each against its OWN field's mean/
// stdev, not a pooled one -- comparing a card's z-score under two different populations is
// exactly what a delta-based sweep needs, matching how every other stat here is a baseline vs.
// candidate lookup rather than one field-wide computation) and then diffed like every other
// column, since compare.html's entire point is "diff two whole reports", not "recompute in one."
//
// Unlike report.html, this page never loads a CardDatabase (the --compare path reads two
// --metrics-json files and never plays a game or touches Shapes.Content/cards/), so there is no
// cardInfo lookup to ask "is this a creature." A card counts as a creature here if it has at
// least one entry in that SIDE's moveStats -- true by construction (CardValidator: a spell has
// effects and no moves, a creature has moves and no effects), and needs nothing beyond the two
// metrics.json files already loaded.
function weightedMean(items, valueOf, weightOf) {
  const totalWeight = items.reduce((sum, item) => sum + weightOf(item), 0);
  if (totalWeight === 0) return null;
  return items.reduce((sum, item) => sum + valueOf(item) * weightOf(item), 0) / totalWeight;
}

function zScores(values) {
  const finite = values.filter(v => typeof v === 'number' && !Number.isNaN(v));
  if (finite.length === 0) return () => null;
  const mean = finite.reduce((a, b) => a + b, 0) / finite.length;
  const variance = finite.reduce((a, b) => a + (b - mean) ** 2, 0) / finite.length;
  const stdev = Math.sqrt(variance);
  return (v) => (typeof v !== 'number' || Number.isNaN(v) || stdev === 0) ? null : (v - mean) / stdev;
}

function computeCompositeScores(cardStats, moveStats, minN) {
  const eligible = cardStats.filter(c => c.offerCount >= minN && c.offerCount > 0);

  const movesByCard = {};
  for (const m of moveStats) {
    (movesByCard[m.cardId] ||= []).push(m);
  }
  const isCreature = (cardId) => (movesByCard[cardId] || []).length > 0;

  const moveTakeRollup = {};
  const moveWinRollup = {};
  for (const c of eligible) {
    const moves = (movesByCard[c.cardId] || []).filter(m => m.offerCount > 0);
    moveTakeRollup[c.cardId] = weightedMean(moves, m => m.useTakeRate.rate, m => m.offerCount);
    moveWinRollup[c.cardId] = weightedMean(moves, m => m.winRateWhenUsed.rate, m => m.offerCount);
  }

  const zTake = zScores(eligible.map(c => c.playTakeRate.rate));
  const zTakePerTurn = zScores(eligible.map(c => c.playTakeRatePerTurn.rate));
  const zWinPlayed = zScores(eligible.map(c => c.winRateWhenPlayed.rate));
  const zWinDrawn = zScores(eligible.map(c => c.winRateWhenDrawn.rate));
  const zMoveTake = zScores(eligible.filter(c => isCreature(c.cardId)).map(c => moveTakeRollup[c.cardId]));
  const zMoveWin = zScores(eligible.filter(c => isCreature(c.cardId)).map(c => moveWinRollup[c.cardId]));

  const scores = {};
  for (const c of eligible) {
    const parts = [zTake(c.playTakeRate.rate), zTakePerTurn(c.playTakeRatePerTurn.rate),
      zWinPlayed(c.winRateWhenPlayed.rate), zWinDrawn(c.winRateWhenDrawn.rate)];
    if (isCreature(c.cardId)) {
      parts.push(zMoveTake(moveTakeRollup[c.cardId]), zMoveWin(moveWinRollup[c.cardId]));
    }
    const present = parts.filter(p => p !== null);
    scores[c.cardId] = present.length === 0 ? null : present.reduce((a, b) => a + b, 0) / present.length;
  }
  return scores;
}

// --- Cards / moves: outer-joined on id, sorted by |delta| descending by default -------------

let cardSort = { key: 'delta', dir: -1 };
let moveSort = { key: 'delta', dir: -1 };
let cardMovedOnly = true;
let moveMovedOnly = true;

function joinById(baseList, candList, idFn) {
  const byId = new Map();
  for (const item of baseList) byId.set(idFn(item), { b: item, c: null });
  for (const item of candList) {
    const key = idFn(item);
    const existing = byId.get(key);
    if (existing) existing.c = item; else byId.set(key, { b: null, c: item });
  }
  return [...byId.values()];
}

// Reduces one rate field (e.g. "playTakeRate") present on both b and c into the {bt, ct, delta,
// moved, direction} shape every rate column renders from -- each rate gets its own moved/not-moved
// call from its own pair of intervals, since take rate, win-when-played, and win-when-drawn can
// each have a different sample size and therefore disagree about whether a card "moved."
function rateDelta(b, c, field) {
  const bt = b?.[field] ?? { rate: 0, low: 0, high: 1, trials: 0 };
  const ct = c?.[field] ?? { rate: 0, low: 0, high: 1, trials: 0 };
  const moved = (b && c) ? intervalsDisjoint(bt, ct) : false;
  return {
    bt, ct,
    delta: ct.rate - bt.rate,
    moved,
    direction: moved ? (ct.rate > bt.rate ? 'up' : 'down') : 'none',
  };
}

function rateCell(has_b, has_c, rd) {
  const cls = rd.direction === 'up' ? 'moved-up' : rd.direction === 'down' ? 'moved-down' : 'not-moved';
  return `<td>${has_b ? pct(rd.bt.rate) : '—'} (n=${rd.bt.trials ?? 0})</td>`
    + `<td>${has_c ? pct(rd.ct.rate) : '—'} (n=${rd.ct.trials ?? 0})</td>`
    + `<td class="${cls}">${(has_b && has_c) ? signedPct(rd.delta) : '—'}</td>`;
}

// Power score has no confidence interval (it is a plain z-score average, not a Wilson/normal
// rate) so "moved" here means |delta| clears a fixed threshold rather than two intervals failing
// to overlap. 0.5 matches report.html's own hi/lo coloring threshold for the same score, so the
// two pages agree on what counts as a real move for this metric.
const POWER_SCORE_MOVE_THRESHOLD = 0.5;

function powerScoreDelta(bScore, cScore) {
  const moved = (bScore != null && cScore != null) && Math.abs(cScore - bScore) >= POWER_SCORE_MOVE_THRESHOLD;
  const delta = (bScore != null && cScore != null) ? cScore - bScore : null;
  return { bScore, cScore, delta, moved, direction: !moved ? 'none' : (delta > 0 ? 'up' : 'down') };
}

function powerScoreCell(rd) {
  const cls = rd.direction === 'up' ? 'moved-up' : rd.direction === 'down' ? 'moved-down' : 'not-moved';
  const fmt = (s) => s == null ? '—' : (s >= 0 ? '+' : '') + num(s, 2);
  return `<td>${fmt(rd.bScore)}</td><td>${fmt(rd.cScore)}</td>`
    + `<td class="${cls}">${rd.delta == null ? '—' : signedNum(rd.delta, 2)}</td>`;
}

function cardRows() {
  const minN = parseInt(document.getElementById('card-min-n').value, 10) || 0;
  const joined = joinById(baseline.cardStats, candidate.cardStats, x => x.cardId);

  const baselineScores = computeCompositeScores(baseline.cardStats, baseline.moveStats, minN);
  const candidateScores = computeCompositeScores(candidate.cardStats, candidate.moveStats, minN);

  let rows = joined
    .filter(({ b, c }) => (b?.offerCount ?? 0) >= minN || (c?.offerCount ?? 0) >= minN)
    .map(({ b, c }) => {
      const take = rateDelta(b, c, 'playTakeRate');
      const winPlayed = rateDelta(b, c, 'winRateWhenPlayed');
      const winDrawn = rateDelta(b, c, 'winRateWhenDrawn');
      const costPressure = rateDelta(b, c, 'costPressure');
      const cardId = b?.cardId ?? c?.cardId;
      const power = powerScoreDelta(baselineScores[cardId] ?? null, candidateScores[cardId] ?? null);
      return {
        cardId,
        b, c, take, winPlayed, winDrawn, costPressure, power,
        thin: Math.min(take.bt.trials || 0, take.ct.trials || 0) < 20,
      };
    });

  if (cardMovedOnly) {
    rows = rows.filter(r => r.take.moved || r.winPlayed.moved || r.winDrawn.moved || r.power.moved);
  }

  const sortField = { take: 'take', winPlayed: 'winPlayed', winDrawn: 'winDrawn', power: 'power' }[cardSort.key];
  rows.sort((x, y) => {
    let av, bv;
    // Signed, not |delta| -- a click on a Δ header should put every regression on one end and
    // every improvement on the other, not interleave a +20% mover with a -20% one because they
    // have the same magnitude. Default landing (cardSort.dir = -1) still surfaces the biggest
    // movers first via the initial sort key/dir below; toggling now walks from most-improved to
    // most-regressed instead of biggest-magnitude to smallest.
    if (sortField) { av = x[sortField].delta ?? 0; bv = y[sortField].delta ?? 0; }
    else if (cardSort.key === 'cardId') { av = x.cardId; bv = y.cardId; }
    if (av === bv) return 0;
    return (av > bv ? 1 : -1) * cardSort.dir;
  });

  return rows.map(r => `<tr class="${r.thin ? 'thin-n' : ''}">
      <td>${r.cardId}</td>
      ${powerScoreCell(r.power)}
      ${rateCell(r.b, r.c, r.take)}
      ${rateCell(r.b, r.c, r.winPlayed)}
      ${rateCell(r.b, r.c, r.winDrawn)}
      <td>${r.b ? pct(r.costPressure.bt.rate) : '—'} → ${r.c ? pct(r.costPressure.ct.rate) : '—'}</td>
    </tr>`).join('');
}

function cardHeader() {
  const cols = [
    ['cardId', 'Card'],
    [null, 'Baseline power'], [null, 'Candidate power'], ['power', 'Δ power score'],
    [null, 'Baseline take'], [null, 'Candidate take'], ['take', 'Δ take'],
    [null, 'Baseline win (played)'], [null, 'Candidate win (played)'], ['winPlayed', 'Δ win (played)'],
    [null, 'Baseline win (drawn)'], [null, 'Candidate win (drawn)'], ['winDrawn', 'Δ win (drawn)'],
    [null, 'Cost pressure'],
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
  const joined = joinById(baseline.moveStats, candidate.moveStats, x => x.cardId + '::' + x.moveName);

  let rows = joined
    .filter(({ b, c }) => (b?.offerCount ?? 0) >= minN || (c?.offerCount ?? 0) >= minN)
    .map(({ b, c }) => {
      const take = rateDelta(b, c, 'useTakeRate');
      const winUsed = rateDelta(b, c, 'winRateWhenUsed');
      return {
        moveName: b?.moveName ?? c?.moveName,
        cardId: b?.cardId ?? c?.cardId,
        b, c, take, winUsed,
        thin: Math.min(take.bt.trials || 0, take.ct.trials || 0) < 20,
      };
    });

  if (moveMovedOnly) rows = rows.filter(r => r.take.moved || r.winUsed.moved);

  const sortField = { take: 'take', winUsed: 'winUsed' }[moveSort.key];
  rows.sort((x, y) => {
    let av, bv;
    // Signed, not |delta| -- same reasoning as cardRows' sort below: a Δ column should separate
    // regressions from improvements, not interleave them by magnitude.
    if (sortField) { av = x[sortField].delta; bv = y[sortField].delta; }
    else if (moveSort.key === 'moveName') { av = x.moveName; bv = y.moveName; }
    if (av === bv) return 0;
    return (av > bv ? 1 : -1) * moveSort.dir;
  });

  return rows.map(r => `<tr class="${r.thin ? 'thin-n' : ''}">
      <td>${r.moveName}</td>
      <td>${r.cardId}</td>
      ${rateCell(r.b, r.c, r.take)}
      ${rateCell(r.b, r.c, r.winUsed)}
    </tr>`).join('');
}

function moveHeader() {
  const cols = [
    ['moveName', 'Move'],
    [null, 'Card'],
    [null, 'Baseline take'], [null, 'Candidate take'], ['take', 'Δ take'],
    [null, 'Baseline win (used)'], [null, 'Candidate win (used)'], ['winUsed', 'Δ win (used)'],
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
document.getElementById('card-moved-btn').addEventListener('click', (e) => {
  cardMovedOnly = !cardMovedOnly;
  e.target.classList.toggle('active', cardMovedOnly);
  renderCardTable();
});
document.getElementById('move-moved-btn').addEventListener('click', (e) => {
  moveMovedOnly = !moveMovedOnly;
  e.target.classList.toggle('active', moveMovedOnly);
  renderMoveTable();
});

document.getElementById('baseline-line').textContent = provenanceLine('Baseline', baseline.provenance, baseline.gameCount);
document.getElementById('candidate-line').textContent = provenanceLine('Candidate', candidate.provenance, candidate.gameCount);
renderSummary();
renderScoring();
renderEconomy();
renderCardTable();
renderMoveTable();
</script>
</body>
</html>
""";
}
