using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shapes.Core.Cards;

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

    // cards is optional -- --from-metrics-json re-derives a report from a saved MetricsReport
    // with no CardDatabase in hand, and the page degrades gracefully to its pre-step-5 columns
    // (cardInfo/moveInfo render as empty objects, and every lookup in the template already
    // guards with `|| {}`).
    public static void Write(string path, MetricsReport metrics, CardDatabase? cards = null)
    {
        var json = JsonSerializer.Serialize(metrics, JsonOptions);

        var cardInfo = cards is null
            ? new Dictionary<string, CardInfo>()
            : CardInfo.BuildLookup(cards).ToDictionary(kv => kv.Key, kv => kv.Value);
        var moveInfo = cards is null
            ? new Dictionary<string, MoveInfo>()
            : MoveInfo.BuildLookup(cards)
                .ToDictionary(kv => MoveKey.Of(kv.Key.CardId, kv.Key.MoveName), kv => kv.Value);

        var cardInfoJson = JsonSerializer.Serialize(cardInfo, JsonOptions);
        var moveInfoJson = JsonSerializer.Serialize(moveInfo, JsonOptions);

        var html = Template
            .Replace("__METRICS_JSON__", json, StringComparison.Ordinal)
            .Replace("__CARD_INFO_JSON__", cardInfoJson, StringComparison.Ordinal)
            .Replace("__MOVE_INFO_JSON__", moveInfoJson, StringComparison.Ordinal);
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
  .effect-text { opacity: 0.75; max-width: 320px; white-space: normal; }
  .res-chip { display: inline-block; padding: 1px 6px; border-radius: 4px; font-size: 12px; font-weight: 600; }
  .res-spike { background: color-mix(in srgb, #d14343 22%, transparent); color: #d14343; }
  .res-anvil { background: color-mix(in srgb, #3a6bd1 22%, transparent); color: #3a6bd1; }
  .res-wheel { background: color-mix(in srgb, #2f9e5e 22%, transparent); color: #2f9e5e; }
  .res-none { opacity: 0.5; }
  td.take.hi { color: #2f9e5e; font-weight: 700; }
  td.take.lo { color: #d14343; font-weight: 700; }
  /* Interval bounds ride along with the rate they qualify, deliberately dimmer: the point is
     that they are always visible, not that they compete with the point estimate. */
  .ci { font-size: 11px; opacity: 0.6; white-space: nowrap; }
  /* A win rate whose interval clears the field mean. Deliberately a thin underline rather than
     the bold+color used for take rate -- at 400 games this fires for ~1 card in 36, and giving
     it the same visual weight as take rate would imply the two columns rank equally well. */
  td.wr.sep { text-decoration: underline; text-decoration-thickness: 2px; }
  td.wr.sep-hi { text-decoration-color: #2f9e5e; }
  td.wr.sep-lo { text-decoration-color: #d14343; }
  /* The field-average row lives in <thead> so it stays put while the body sorts, and is styled
     as a summary rather than as data -- it is a reference line, not a card. */
  tr.avg-row td {
    background: color-mix(in srgb, CanvasText 7%, transparent);
    font-weight: 600; font-size: 12px; opacity: 0.85;
    border-bottom: 2px solid color-mix(in srgb, CanvasText 25%, transparent);
  }
  .resource-table td, .resource-table th { padding: 4px 12px; }
  .resource-table th:first-child, .resource-table td:first-child { padding-left: 0; }
  svg.margin-chart { display: block; }
  .margin-chart .zero-line { stroke: color-mix(in srgb, CanvasText 30%, transparent); stroke-width: 1; }
  .margin-chart .margin-line { stroke: color-mix(in srgb, CanvasText 65%, transparent); stroke-width: 1.5; fill: none; }
  .margin-chart .margin-band { fill: color-mix(in srgb, CanvasText 12%, transparent); }
  .margin-chart .axis-label { font-size: 10px; fill: color-mix(in srgb, CanvasText 55%, transparent); }
  svg.hand-chart { display: block; }
  .hand-chart .seat-one-line { stroke: #3a6bd1; stroke-width: 1.5; fill: none; }
  .hand-chart .seat-two-line { stroke: #d14343; stroke-width: 1.5; fill: none; }
  .hand-chart .axis-label { font-size: 10px; fill: color-mix(in srgb, CanvasText 55%, transparent); }
  .hand-chart .grid-line { stroke: color-mix(in srgb, CanvasText 12%, transparent); stroke-width: 1; }
  .chart-legend { font-size: 12px; margin-top: 4px; }
  .chart-legend .swatch { display: inline-block; width: 10px; height: 10px; border-radius: 2px; margin-right: 4px; vertical-align: middle; }
</style>
</head>
<body>

<h1>Shapes — Metrics Explorer</h1>
<div class="sub" id="provenance-line"></div>

<div class="panel">
  <h2 style="margin-top:0">Batch summary</h2>
  <div class="grid" id="summary-grid"></div>
  <p class="hint" style="margin:14px 0 4px">
    <strong>Game length distribution.</strong> The mean alone cannot tell "every game got longer"
    apart from "one game never ended" — a single non-terminating game moves the mean and the
    standard deviation far more than the median. Read the p50/p95 marks: a long right tail past
    p95 means games are failing to resolve, which is a rules problem rather than a pacing one.
  </p>
  <div id="length-histogram"></div>
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
  <h2 style="margin-top:0">Hand size by turn, per seat</h2>
  <p class="hint">
    Mean cards in hand at each turn boundary, split by seat (never pooled — one seat starved while
    the other floods would average out to a healthy-looking midpoint that neither seat actually
    had). A hand hovering near 0-1 most turns means income or draw is too stingy, or combat burns
    cards faster than they can be replaced; a hand routinely at 6+ means income/draw outpaces what
    a turn can spend, or removal/board clears keep resetting the board without spending the hand
    down. Read alongside <strong>cost pressure</strong> below: a low hand size with <em>low</em>
    cost pressure means the hand itself is the bottleneck (not enough draw); a low hand size with
    <em>high</em> cost pressure means resources are the bottleneck and the (already thin) hand is
    just waiting on affordability. Later turns average over fewer games (short games have already
    ended).
  </p>
  <div id="hand-chart"></div>
  <div class="chart-legend">
    <span class="swatch" style="background:#3a6bd1"></span>Seat 1
    <span class="swatch" style="background:#d14343"></span>Seat 2
  </div>
</div>

<div class="panel">
  <h2 style="margin-top:0">Board presence by turn, per seat</h2>
  <p class="hint">
    Slots occupied and combined <strong>current</strong> (not max) creature health at each scoring
    step, split by seat. Slot count and unopposed-slot rate (in Scoring rule, below) can look
    identical for very different boards: three 1-health creatures occupying every slot score the
    same as three full-health ones, but defend nothing like as well. Combined health is what tells
    "present" apart from "actually threatening" — a slot count holding steady while combined
    health falls means the board is present but getting worn down, not stable.
  </p>
  <div class="grid" id="board-presence-charts"></div>
  <div class="chart-legend">
    <span class="swatch" style="background:#3a6bd1"></span>Seat 1
    <span class="swatch" style="background:#d14343"></span>Seat 2
  </div>
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
  <p class="hint" style="margin-top:16px; margin-bottom:6px">
    The same unspent-resource levels as a curve instead of a single mean, one chart per resource
    type, seat 1 vs. seat 2 (never pooled — one seat sitting on a pile of Anvil while the other
    stays spent is a real asymmetry a pooled mean would hide). A level climbing turn over turn
    with <em>low</em> cost pressure for that type (see the Cards table) means income for that
    type is outpacing what there is to spend it on; climbing with <em>high</em> cost pressure
    means the type itself is the bottleneck — nothing affordable currently asks for it — not the
    amount held.
  </p>
  <div class="grid" id="resource-charts"></div>
</div>

<h2>Resource types</h2>
<p class="hint" id="by-resource-unavailable" style="display:none">
  Needs card cost data, which a saved metrics file does not carry — regenerate the report from a
  live run (not <code>--from-metrics-json</code>) to populate this section.
</p>
<div id="by-resource-body">
  <p class="hint">
    The Economy panel above reads resources as things players <em>hold</em>; this reads them as the
    three card pools they buy from. A type whose cards are uniformly weak, or clustered at one
    cost, or whose creatures outclass its spells, is a content problem no per-card row shows —
    those rows are sorted against the whole field, so a systematically weak type reads as a dozen
    unrelated mediocre cards. Cards are grouped by <strong>cost type</strong> (what you pay), which
    for every current card equals its attack type; a hypothetical mixed-cost card would count once
    per type it costs, and a free card appears under none.
  </p>
  <div class="grid" id="resource-summary"></div>

  <p class="hint" style="margin-top:16px; margin-bottom:6px">
    <strong>Cost distribution.</strong> How many cards each type offers at each total cost, split
    creature / spell. Read down a column to compare types at the same price point: a type with no
    cheap creatures cannot contest the board early, and one whose curve stops short has no
    late-game payoff to spend a full pool on.
  </p>
  <table class="resource-table" id="resource-cost-table">
    <thead></thead>
    <tbody></tbody>
  </table>

  <p class="hint" style="margin-top:16px; margin-bottom:6px">
    <strong>Average metrics per type</strong>, over cards passing the min-n filter below. Power
    score is the same z-score rollup the Cards table uses — z-scored against the whole field, so
    these averages are directly comparable across types: a type averaging below zero is
    underperforming the field, not merely different from it. Spell and creature means are split
    because the score's creature half includes a move rollup spells have no counterpart for.
  </p>
  <div class="controls">
    <label>Min n (offers): <input type="number" id="resource-min-n" value="0" min="0"></label>
  </div>
  <table class="resource-table" id="resource-metrics-table">
    <thead></thead>
    <tbody></tbody>
  </table>

  <p class="hint" style="margin-top:16px; margin-bottom:6px">
    <strong>Strongest and weakest per type.</strong> Cards rank by power score, moves by take
    rate/turn — a move has no play/draw win rate to roll up, and take rate/turn is the closest
    per-move equivalent of "chosen when it could have been." Moves are attributed to their own cost
    type, which need not match the creature's: a wheel creature with an anvil move counts that move
    under anvil.
  </p>
  <div class="grid" id="resource-extremes"></div>
</div>

<h2>Cards</h2>
<p class="hint">
  <strong>Power score</strong> is an opinionated rollup, not a replacement for the columns beside
  it: the z-score average of take rate, take rate/turn, win rate (played), and win rate (drawn)
  across the field of cards passing the min-n filter, plus (for creatures only) a take-rate-weighted
  average of that creature's own moves' take rate and win rate -- so a creature that gets played but
  whose moves mostly go unused scores lower than its play stats alone would suggest. Z-scored so
  take rates (naturally low) and win rates (naturally near 50%) don't distort each other by scale.
  Spells and creatures are only comparable <em>within</em> their own kind, not across. It knows
  nothing about what a card costs or is meant to do -- a low score on a situational answer card is
  not the same finding as a low score on a vanilla creature. Read the individual columns first.
</p>
<p class="hint">
  <strong>On the win-rate columns.</strong> Both now carry their Wilson interval inline, and a
  cell is underlined only when that interval clears the <em>field mean</em> (not 50% -- under
  symmetric decks the whole field sits near 50% by construction, so beating a coin flip is not
  the interesting comparison). Expect almost nothing to be underlined: both seats hold every card,
  so most cards contribute a win <em>and</em> a loss in most games and win rate compresses toward
  the middle mechanically. At 400 games the per-card draw-WR interval is several times wider than
  the entire field's spread, which is why <strong>take rate, not win rate, is the column this
  table sorts and colours by</strong> -- take rate measures what a strong agent chose when it had
  the option, and is the metric a card-level balance change actually moves. Use the draw-WR
  filter to see how few cards separate on it; a run needs far more games before that column can
  rank anything.
</p>
<div class="controls">
  <label>Min n (offers): <input type="number" id="card-min-n" value="0" min="0"></label>
  <button class="toggle-btn active" id="card-outliers-btn">Outliers only (excludes field median)</button>
  <button class="toggle-btn" id="card-drawwr-btn" title="Cards whose win-rate-when-drawn interval excludes the field mean. Expect very few: see the note above.">Draw-WR outliers only</button>
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
<script id="card-info-data" type="application/json">__CARD_INFO_JSON__</script>
<script id="move-info-data" type="application/json">__MOVE_INFO_JSON__</script>
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
const cardInfo = camelizeKeys(JSON.parse(document.getElementById('card-info-data').textContent));
const moveInfo = camelizeKeys(JSON.parse(document.getElementById('move-info-data').textContent));
let baseline = null;

function pct(x) { return (x * 100).toFixed(1) + '%'; }
function num(x, d) { return (typeof x === 'number') ? x.toFixed(d === undefined ? 2 : d) : '—'; }

// A rate with its Wilson interval spelled out: "50.3% [45.8, 54.9]".
//
// Exists because win-rate columns are the ones most likely to be over-read. Under symmetric
// decks both seats hold every card, so most cards contribute a win AND a loss in most games and
// every win rate compresses toward 50% mechanically -- at 400 games the per-card draw-WR
// interval is several times wider than the whole field's spread. Printing the bounds inline is
// the cheapest way to make "this card is at 53%" and "this card is indistinguishable from every
// other card" read differently at a glance.
function pctInterval(interval) {
  if (!interval || interval.trials === 0) return '—';
  return `${pct(interval.rate)} <span class="ci">[${(interval.low * 100).toFixed(1)}, `
    + `${(interval.high * 100).toFixed(1)}]</span>`;
}

// Whether a rate is separated from the field at all: does its interval exclude the field's
// own mean? Cards failing this are not ranked by that column in any meaningful sense.
function separated(interval, reference) {
  return interval && interval.trials > 0 && excludes(interval, reference);
}

// Marking for a win-rate cell. Only cards whose interval actually clears the field mean get
// marked -- everything else is left plain, which for win rate is most of the table and is the
// honest rendering of it.
function wrClass(interval, reference) {
  if (!separated(interval, reference)) return '';
  return interval.rate > reference ? 'sep sep-hi' : 'sep sep-lo';
}

// intervalBar draws on a 0-100% axis. Win rates under symmetric decks occupy roughly 45-55% of
// that axis, so drawn unzoomed they are all the same centred smudge. These rescale the 30-70%
// window onto the full bar; clamped so an extreme card pins to an edge instead of overflowing.
const WR_ZOOM_LO = 0.30, WR_ZOOM_HI = 0.70;
function zoomRate(r) {
  return Math.min(1, Math.max(0, (r - WR_ZOOM_LO) / (WR_ZOOM_HI - WR_ZOOM_LO)));
}
function zoomInterval(iv) {
  return { rate: zoomRate(iv.rate), low: zoomRate(iv.low), high: zoomRate(iv.high), trials: iv.trials };
}

// Cost/attack-type reference data is looked up by id -- not part of MetricsReport itself (that
// stays pure aggregation over played games), joined in here purely for display so an outlier
// row doesn't send the reader to Shapes.Content/cards/ to remember what a card costs or does.
const RES_SYMBOL = { spike: '△', anvil: '▢', wheel: '◯' };

function resChip(type) {
  if (!type) return '<span class="res-none">—</span>';
  const key = String(type).toLowerCase();
  return `<span class="res-chip res-${key}">${RES_SYMBOL[key] || type}</span>`;
}

function costText(cost) {
  if (!cost) return '—';
  const parts = [];
  if (cost.spike) parts.push(`${cost.spike}△`);
  if (cost.anvil) parts.push(`${cost.anvil}▢`);
  if (cost.wheel) parts.push(`${cost.wheel}◯`);
  return parts.length ? parts.join(' ') : '0';
}

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
    statTile('Game length', num(m.gameLength.mean) + ' turns'
      + `<div class="hint" style="margin:2px 0 0">median ${num(m.gameLengthDistribution.p50, 0)} · p95 ${num(m.gameLengthDistribution.p95, 0)} · max ${num(m.gameLengthDistribution.max, 0)}</div>`),
    statTile('Move usage rate', pct(m.moveUsageRate)),
    statTile('Merges/game', num(m.mergesPerGame)),
    statTile('Merge take rate', pct(m.mergeTakeRate.rate)),
    statTile('Unopposed slot rate', pct(m.unopposedSlotRate.rate)),
    statTile('Longest unopposed streak', num(m.longestUnopposedStreak.mean) + ' steps'),
    statTile('No sustained unopposed', `${m.gamesWithNoSustainedUnopposed} / ${m.gameCount} games`),
    statTile('Cards drawn/game (winners)', num(m.cardsDrawnWinners.mean) + ` [${num(m.cardsDrawnWinners.low)}, ${num(m.cardsDrawnWinners.high)}]`),
    statTile('Cards drawn/game (losers)', num(m.cardsDrawnLosers.mean) + ` [${num(m.cardsDrawnLosers.low)}, ${num(m.cardsDrawnLosers.high)}]`),
  ];

  // Fatigue tiles only appear when the rule actually fired -- a run with fatigue disabled (or one
  // where no deck ever emptied) would otherwise show three permanent zeroes that read as a
  // finding rather than as "not applicable".
  if (m.deckExhaustionRateSeatOne.successes > 0 || m.deckExhaustionRateSeatTwo.successes > 0) {
    tiles.push(
      statTile('Decked out (P1 / P2)',
        `${pct(m.deckExhaustionRateSeatOne.rate)} / ${pct(m.deckExhaustionRateSeatTwo.rate)}`
        + `<div class="hint" style="margin:2px 0 0">first at turn ${num(m.firstFatigueTurnSeatOne.mean, 0)} / ${num(m.firstFatigueTurnSeatTwo.mean, 0)}</div>`),
      statTile('Fatigue score conceded',
        `P1 ${m.fatigueScoreConcededSeatOne} · P2 ${m.fatigueScoreConcededSeatTwo}`),
      // Flagged red past a quarter: fatigue is a backstop, and a large share here means the
      // timer decides games rather than play doing so.
      statTile('Games decided by fatigue', pct(m.gamesDecidedByFatigue.rate),
        m.gamesDecidedByFatigue.rate > 0.25));
  }

  grid.innerHTML = tiles.join('');
  renderLengthHistogram();
}

// Game-length histogram. A mean and a standard deviation cannot show a bimodal or long-tailed
// distribution, which is the shape a termination problem produces -- this makes a fat right tail
// visible directly instead of inferred from a suspiciously large standard deviation.
function renderLengthHistogram() {
  const d = metrics.gameLengthDistribution;
  const el = document.getElementById('length-histogram');
  if (!el || !d || !d.histogram || d.histogram.length === 0) return;

  const w = 620, h = 130, padL = 34, padB = 22, padT = 6;
  const max = Math.max(...d.histogram, 1);
  const bw = (w - padL - 6) / d.histogram.length;
  const bars = d.histogram.map((count, i) => {
    const bh = (h - padB - padT) * (count / max);
    const x = padL + (i * bw);
    const y = h - padB - bh;
    return `<rect x="${x.toFixed(1)}" y="${y.toFixed(1)}" width="${Math.max(bw - 1, 1).toFixed(1)}" height="${bh.toFixed(1)}" fill="currentColor" opacity="0.55"><title>${(d.histogramMin + (i * d.bucketWidth)).toFixed(0)}-${(d.histogramMin + ((i + 1) * d.bucketWidth)).toFixed(0)} turns: ${count} games</title></rect>`;
  }).join('');

  // Percentile ticks, so the tail is readable as "p95 sits here" rather than as a bare shape.
  const xOf = v => padL + ((v - d.histogramMin) / Math.max(d.bucketWidth * d.histogram.length, 1e-9)) * (w - padL - 6);
  const marks = [['p50', d.p50], ['p95', d.p95]].map(([label, v]) => {
    const x = Math.min(Math.max(xOf(v), padL), w - 6);
    return `<line x1="${x.toFixed(1)}" y1="${padT}" x2="${x.toFixed(1)}" y2="${h - padB}" stroke="currentColor" stroke-dasharray="3 2" opacity="0.7"/>`
      + `<text x="${x.toFixed(1)}" y="${padT + 9}" font-size="10" text-anchor="middle" opacity="0.8">${label}</text>`;
  }).join('');

  el.innerHTML = `<svg class="margin-chart" viewBox="0 0 ${w} ${h}" width="100%" height="${h}">
    <text x="0" y="${padT + 9}" font-size="10" opacity="0.7">${max}</text>
    <line x1="${padL}" y1="${h - padB}" x2="${w - 6}" y2="${h - padB}" stroke="currentColor" opacity="0.3"/>
    ${bars}${marks}
    <text x="${padL}" y="${h - 2}" font-size="10" opacity="0.7">${num(d.min, 0)}</text>
    <text x="${w - 6}" y="${h - 2}" font-size="10" text-anchor="end" opacity="0.7">${num(d.max, 0)} turns</text>
  </svg>`;
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

  renderResourceCharts();
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

// Two-line SVG chart for a per-seat MeanEstimate series (hand size, resource-by-turn) --
// deliberately two overlaid lines rather than margin chart's single-line-plus-band, because
// there is no natural "seat one minus seat two" framing for either: both being low is a starved
// economy, both being high is a bloated one, and a difference would erase exactly that finding.
// No interval band per line to keep two series legible at once; the width of the swing between
// seats is itself informative. gridStep/width/height are parameters so the same renderer serves
// the full-width hand chart and the three smaller side-by-side resource charts.
function twoSeatLineChart(seriesOne, seriesTwo, { width = 900, height = 220, gridStep = 2, cssClass = 'hand-chart' } = {}) {
  if (!seriesOne || !seriesTwo || (seriesOne.length === 0 && seriesTwo.length === 0)) {
    return '<p class="hint">No per-turn data in this report.</p>';
  }

  const padL = 40, padR = 12, padT = 12, padB = 24;
  const plotW = width - padL - padR, plotH = height - padT - padB;
  const longest = Math.max(seriesOne.length, seriesTwo.length);

  const allMeans = [...seriesOne, ...seriesTwo].map(s => s.mean);
  const yMin = Math.min(0, ...allMeans), yMax = Math.max(1, ...allMeans);
  const yPad = Math.max((yMax - yMin) * 0.08, 0.5);
  const y0 = yMin - yPad, y1 = yMax + yPad;

  const x = (i) => padL + (longest === 1 ? 0 : (i / (longest - 1)) * plotW);
  const y = (v) => padT + plotH - ((v - y0) / (y1 - y0)) * plotH;

  const linePoints = (series) => series.map((s, i) => `${x(i)},${y(s.mean)}`).join(' ');
  const turnTicks = [0, Math.floor(longest / 2), longest - 1];
  const tickLabel = (i) => {
    const n1 = seriesOne[i]?.count ?? 0, n2 = seriesTwo[i]?.count ?? 0;
    return `t${i + 1} (n=${n1}/${n2})`;
  };
  const gridValues = [];
  for (let v = 0; v <= y1; v += gridStep) gridValues.push(v);

  return `
    <svg class="${cssClass}" viewBox="0 0 ${width} ${height}" width="100%" style="max-width:${width}px">
      ${gridValues.filter(v => v >= y0 && v <= y1).map(v => `
        <line class="grid-line" x1="${padL}" y1="${y(v)}" x2="${width - padR}" y2="${y(v)}"></line>
        <text class="axis-label" x="${padL - 6}" y="${y(v) + 3}" text-anchor="end">${num(v, 0)}</text>
      `).join('')}
      <polyline class="seat-one-line" points="${linePoints(seriesOne)}"></polyline>
      <polyline class="seat-two-line" points="${linePoints(seriesTwo)}"></polyline>
      ${turnTicks.map(i => `<text class="axis-label" x="${x(i)}" y="${height - 6}" text-anchor="middle">${tickLabel(i)}</text>`).join('')}
    </svg>`;
}

function renderHandChart() {
  document.getElementById('hand-chart').innerHTML =
    twoSeatLineChart(metrics.handSizeByTurnOne, metrics.handSizeByTurnTwo, { cssClass: 'hand-chart' });
}

// Two side-by-side charts, same twoSeatLineChart shape as hand size/resources: slot count
// (integer, small range, so a coarser gridStep) and combined current health (can run into the
// teens/twenties with several creatures, wider range).
function renderBoardPresenceCharts() {
  const container = document.getElementById('board-presence-charts');
  if (!container) return;

  container.innerHTML = `
    <div>
      <div class="stat label" style="margin-bottom:4px">Slots occupied</div>
      ${twoSeatLineChart(
        metrics.slotsOccupiedByTurnOne, metrics.slotsOccupiedByTurnTwo,
        { width: 420, height: 200, gridStep: 1, cssClass: 'hand-chart' })}
    </div>
    <div>
      <div class="stat label" style="margin-bottom:4px">Combined health</div>
      ${twoSeatLineChart(
        metrics.combinedHealthByTurnOne, metrics.combinedHealthByTurnTwo,
        { width: 420, height: 200, gridStep: 5, cssClass: 'hand-chart' })}
    </div>`;
}

// Three small charts, one per resource type, each the same two-seat-line shape as the hand
// chart -- reuses twoSeatLineChart rather than a bespoke renderer since the underlying data
// (a per-seat MeanEstimate series) and the "no P1-minus-P2 framing" reasoning are identical.
function renderResourceCharts() {
  const container = document.getElementById('resource-charts');
  if (!container) return;

  const types = [
    ['spike', 'Spike △'],
    ['anvil', 'Anvil ▢'],
    ['wheel', 'Wheel ◯'],
  ];

  container.innerHTML = types.map(([key, label]) => `
    <div>
      <div class="stat label" style="margin-bottom:4px">${label}</div>
      ${twoSeatLineChart(
        metrics.resourcesByTurnOne?.[key], metrics.resourcesByTurnTwo?.[key],
        { width: 300, height: 180, gridStep: 2, cssClass: 'hand-chart' })}
    </div>`).join('') + `
    <div class="chart-legend" style="grid-column: 1 / -1">
      <span class="swatch" style="background:#3a6bd1"></span>Seat 1
      <span class="swatch" style="background:#d14343"></span>Seat 2
    </div>`;
}

// --- Sortable, filterable card/move tables -------------------------------------------------

let cardSort = { key: 'playTakeRate', dir: -1 };
let moveSort = { key: 'useTakeRate', dir: -1 };
let cardOutliersOnly = true;
let cardDrawWrOnly = false;

// --- Composite power score --------------------------------------------------------------
//
// An opinionated rollup, NOT a replacement for the individual stats above. Combines four
// per-card rates (take rate, take rate/turn, win rate when played, win rate when drawn) via
// z-score -- each stat standardized to mean 0 / stdev 1 across the field before averaging, so
// take rates (which run low in absolute terms) and win rates (which cluster near 50% under
// symmetric decks) don't distort each other by raw scale. Z-score rather than rank-averaging:
// it preserves HOW FAR a card sits from the pack, not just its order, which matters when most
// of the field is bunched and one or two cards are genuine outliers.
//
// For a creature, two more inputs are added: its moves' take rate and win-rate-when-used,
// each rolled up as a TAKE-RATE-WEIGHTED MEAN across that creature's moves (a move used more
// often carries more weight in the average) -- this is what lets "played often but its moves
// go unused" pull the composite down, matching the gap a reader asking "is this card actually
// good" cannot currently see by eyeballing take rate and move take rate as two separate numbers.
// A spell has no moves and is scored on the 4-input card-only formula, so composite scores are
// only strictly comparable within a kind (creature vs. creature, spell vs. spell), not across.
//
// Cards below the min-n filter are excluded from the field used to compute mean/stdev (a thin
// sample would both get an unreliable score AND skew the baseline for everyone else), and are
// rendered as thin-n / "—" like every other rate on this page.
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

function movesByCard() {
  const byCard = {};
  for (const m of metrics.moveStats) {
    (byCard[m.cardId] ||= []).push(m);
  }
  return byCard;
}

function computeCompositeScores(cards, minN) {
  const eligible = cards.filter(c => c.offerCount >= minN && c.offerCount > 0);
  const byCard = movesByCard();

  const moveTakeRollup = {};
  const moveWinRollup = {};
  for (const c of eligible) {
    const moves = (byCard[c.cardId] || []).filter(m => m.offerCount > 0);
    moveTakeRollup[c.cardId] = weightedMean(moves, m => m.useTakeRate.rate, m => m.offerCount);
    moveWinRollup[c.cardId] = weightedMean(moves, m => m.winRateWhenUsed.rate, m => m.offerCount);
  }

  const zTake = zScores(eligible.map(c => c.playTakeRate.rate));
  const zTakePerTurn = zScores(eligible.map(c => c.playTakeRatePerTurn.rate));
  const zWinPlayed = zScores(eligible.map(c => c.winRateWhenPlayed.rate));
  const zWinDrawn = zScores(eligible.map(c => c.winRateWhenDrawn.rate));
  const zMoveTake = zScores(eligible.filter(c => cardInfo[c.cardId]?.kind === 'creature').map(c => moveTakeRollup[c.cardId]));
  const zMoveWin = zScores(eligible.filter(c => cardInfo[c.cardId]?.kind === 'creature').map(c => moveWinRollup[c.cardId]));

  const scores = {};
  for (const c of eligible) {
    const isCreature = cardInfo[c.cardId]?.kind === 'creature';
    const parts = [zTake(c.playTakeRate.rate), zTakePerTurn(c.playTakeRatePerTurn.rate),
      zWinPlayed(c.winRateWhenPlayed.rate), zWinDrawn(c.winRateWhenDrawn.rate)];
    if (isCreature) {
      parts.push(zMoveTake(moveTakeRollup[c.cardId]), zMoveWin(moveWinRollup[c.cardId]));
    }
    const present = parts.filter(p => p !== null);
    scores[c.cardId] = present.length === 0 ? null : present.reduce((a, b) => a + b, 0) / present.length;
  }
  return scores;
}

// -- Resource types ---------------------------------------------------------------------------
// Everything below joins cardInfo/moveInfo (cost data) onto the metrics, so the whole section
// hides itself when the report was built without a CardDatabase -- see renderByResource.
const RES_KEYS = ['spike', 'anvil', 'wheel'];
const RES_LABEL = { spike: 'Spike △', anvil: 'Anvil ▢', wheel: 'Wheel ◯' };

function totalCost(cost) {
  if (!cost) return 0;
  return (cost.spike || 0) + (cost.anvil || 0) + (cost.wheel || 0);
}

// Which resource pools a cost draws on. Returns every type with a nonzero component rather than
// one "primary" -- picking a single type would have to invent a tiebreak for mixed costs, and
// counting such a card under both pools is the honest reading of "what this type has to offer."
function costTypes(cost) {
  if (!cost) return [];
  return RES_KEYS.filter(k => (cost[k] || 0) > 0);
}

function cardsOfType(res) {
  return metrics.cardStats.filter(c => costTypes(cardInfo[c.cardId]?.cost).includes(res));
}

function mean(values) {
  const present = values.filter(v => typeof v === 'number' && !Number.isNaN(v));
  return present.length ? present.reduce((a, b) => a + b, 0) / present.length : null;
}

function renderResourceSummary(scores, minN) {
  document.getElementById('resource-summary').innerHTML = RES_KEYS.map(res => {
    const cards = cardsOfType(res).filter(c => c.offerCount >= minN);
    const creatures = cards.filter(c => cardInfo[c.cardId]?.kind === 'creature');
    const spells = cards.filter(c => cardInfo[c.cardId]?.kind === 'spell');
    const avg = mean(cards.map(c => scores[c.cardId]));
    const costs = cards.map(c => totalCost(cardInfo[c.cardId]?.cost));
    return statTile(RES_LABEL[res],
      `${cards.length} cards (${creatures.length}c / ${spells.length}s)`
      + `<div class="hint" style="margin:2px 0 0">avg power ${avg === null ? '—' : num(avg)}`
      + ` · avg cost ${costs.length ? num(mean(costs), 1) : '—'}</div>`);
  }).join('');
}

function renderResourceCostTable() {
  const all = metrics.cardStats
    .map(c => cardInfo[c.cardId])
    .filter(Boolean);
  const maxCost = Math.max(1, ...all.map(i => totalCost(i.cost)));
  const costCols = [];
  for (let n = 1; n <= maxCost; n++) costCols.push(n);

  document.querySelector('#resource-cost-table thead').innerHTML =
    `<tr><th>Type</th><th>Kind</th>${costCols.map(n => `<th>${n}</th>`).join('')}<th>Total</th></tr>`;

  const rows = [];
  for (const res of RES_KEYS) {
    for (const kind of ['creature', 'spell']) {
      const cards = cardsOfType(res).filter(c => cardInfo[c.cardId]?.kind === kind);
      const counts = costCols.map(n =>
        cards.filter(c => totalCost(cardInfo[c.cardId].cost) === n).length);
      rows.push(`<tr>
        <td>${kind === 'creature' ? resChip(res) + ' ' + RES_LABEL[res] : ''}</td>
        <td>${kind}</td>
        ${counts.map(n => `<td>${n || '<span class="res-none">·</span>'}</td>`).join('')}
        <td>${cards.length}</td>
      </tr>`);
    }
  }
  document.querySelector('#resource-cost-table tbody').innerHTML = rows.join('');
}

function renderResourceMetricsTable(scores, minN) {
  document.querySelector('#resource-metrics-table thead').innerHTML =
    '<tr><th>Type</th><th>Kind</th><th>n</th><th>Power score</th><th>Take%</th>'
    + '<th>Take%/turn</th><th>Win (played)</th><th>Win (drawn)</th><th>Cost pressure</th></tr>';

  const rows = [];
  for (const res of RES_KEYS) {
    for (const kind of ['creature', 'spell']) {
      const cards = cardsOfType(res)
        .filter(c => cardInfo[c.cardId]?.kind === kind && c.offerCount >= minN && c.offerCount > 0);
      if (cards.length === 0) continue;
      const avgScore = mean(cards.map(c => scores[c.cardId]));
      rows.push(`<tr>
        <td>${kind === 'creature' ? resChip(res) + ' ' + RES_LABEL[res] : ''}</td>
        <td>${kind}</td>
        <td>${cards.length}</td>
        <td>${avgScore === null ? '—' : num(avgScore)}</td>
        <td>${pct(mean(cards.map(c => c.playTakeRate.rate)))}</td>
        <td>${pct(mean(cards.map(c => c.playTakeRatePerTurn.rate)))}</td>
        <td>${pct(mean(cards.map(c => c.winRateWhenPlayed.rate)))}</td>
        <td>${pct(mean(cards.map(c => c.winRateWhenDrawn.rate)))}</td>
        <td>${pct(mean(cards.map(c => c.costPressure.rate)))}</td>
      </tr>`);
    }
  }
  document.querySelector('#resource-metrics-table tbody').innerHTML = rows.join('');
}

function renderResourceExtremes(scores, minN) {
  const movesAll = metrics.moveStats.filter(m => m.offerCount >= minN && m.offerCount > 0);

  document.getElementById('resource-extremes').innerHTML = RES_KEYS.map(res => {
    const cards = cardsOfType(res)
      .filter(c => c.offerCount >= minN && c.offerCount > 0 && scores[c.cardId] !== null
        && scores[c.cardId] !== undefined)
      .sort((a, b) => scores[b.cardId] - scores[a.cardId]);

    const moves = movesAll
      .filter(m => costTypes(moveInfo[m.cardId + '::' + m.moveName]?.cost).includes(res))
      .sort((a, b) => b.useTakeRatePerTurn.rate - a.useTakeRatePerTurn.rate);

    const cardLine = c => `<div>${c.cardId} <span class="hint" style="margin:0">${num(scores[c.cardId])}</span></div>`;
    const moveLine = m => `<div>${m.moveName} <span class="hint" style="margin:0">(${m.cardId}) ${pct(m.useTakeRatePerTurn.rate)}</span></div>`;

    return `<div class="stat">
      <div class="label">${resChip(res)} ${RES_LABEL[res]}</div>
      <div class="hint" style="margin:6px 0 2px">Strongest cards</div>
      ${cards.slice(0, 3).map(cardLine).join('') || '<div class="res-none">—</div>'}
      <div class="hint" style="margin:6px 0 2px">Weakest cards</div>
      ${cards.slice(-3).reverse().map(cardLine).join('') || '<div class="res-none">—</div>'}
      <div class="hint" style="margin:6px 0 2px">Most-used moves</div>
      ${moves.slice(0, 3).map(moveLine).join('') || '<div class="res-none">—</div>'}
      <div class="hint" style="margin:6px 0 2px">Least-used moves</div>
      ${moves.slice(-3).reverse().map(moveLine).join('') || '<div class="res-none">—</div>'}
    </div>`;
  }).join('');
}

function renderByResource() {
  // cardInfo is empty when the report was rebuilt from a saved metrics file (no CardDatabase to
  // join costs from), and every panel here is keyed on cost -- so the whole section steps aside
  // with an explanation rather than rendering three empty tables.
  if (Object.keys(cardInfo).length === 0) {
    document.getElementById('by-resource-unavailable').style.display = '';
    document.getElementById('by-resource-body').style.display = 'none';
    return;
  }

  const minN = parseInt(document.getElementById('resource-min-n').value, 10) || 0;
  const scores = computeCompositeScores(metrics.cardStats, minN);
  renderResourceSummary(scores, minN);
  renderResourceCostTable();
  renderResourceMetricsTable(scores, minN);
  renderResourceExtremes(scores, minN);
}

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

// The rows the card table is currently showing, after the min-n filter and the outliers-only
// toggle but before sorting. Extracted so the field-average row averages exactly what is on
// screen rather than re-deriving the filter and silently drifting out of step with it.
function visibleCardRows() {
  const fieldMedian = median(metrics.cardStats.filter(c => c.offerCount > 0).map(c => c.playTakeRate.rate));
  const minN = parseInt(document.getElementById('card-min-n').value, 10) || 0;
  let rows = metrics.cardStats.filter(c => c.offerCount >= minN);
  if (cardOutliersOnly && !baseline) {
    rows = rows.filter(c => c.offerCount > 0 && excludes(c.playTakeRate, fieldMedian));
  }
  if (cardDrawWrOnly) {
    const scored = metrics.cardStats.filter(c => c.offerCount > 0);
    const fieldWinDrawn = mean(scored.map(c => c.winRateWhenDrawn.rate));
    rows = rows.filter(c => separated(c.winRateWhenDrawn, fieldWinDrawn));
  }
  return rows;
}

function cardRows() {
  const fieldMedian = median(metrics.cardStats.filter(c => c.offerCount > 0).map(c => c.playTakeRate.rate));
  const minN = parseInt(document.getElementById('card-min-n').value, 10) || 0;
  const baselineById = baseline ? Object.fromEntries(baseline.cardStats.map(c => [c.cardId, c])) : null;
  const composite = computeCompositeScores(metrics.cardStats, minN);

  // Win-rate reference lines are the FIELD MEAN, not 50%: under symmetric decks the whole field
  // sits near 50% by construction, so "beats a coin flip" is not the interesting comparison --
  // "beats the other 35 cards" is.
  const scored = metrics.cardStats.filter(c => c.offerCount > 0);
  const fieldWinPlayed = mean(scored.map(c => c.winRateWhenPlayed.rate));
  const fieldWinDrawn = mean(scored.map(c => c.winRateWhenDrawn.rate));

  let rows = visibleCardRows();

  rows = [...rows].sort((a, b) => {
    const get = (c) => cardSort.key === 'composite'
      ? composite[c.cardId]
      : cardSort.key.split('.').reduce((o, k) => o && o[k], c);
    const av = get(a), bv = get(b);
    if (av === bv || (av == null && bv == null)) return 0;
    if (av == null) return 1;
    if (bv == null) return -1;
    return (av > bv ? 1 : -1) * cardSort.dir;
  });

  return rows.map(c => {
    const thin = c.offerCount < 20;
    const bl = baselineById ? baselineById[c.cardId] : null;
    const info = cardInfo[c.cardId];
    let deltaCell = '';
    let moveClass = '';
    if (bl) {
      const delta = c.playTakeRate.rate - bl.playTakeRate.rate;
      const moved = excludes(c.playTakeRate, bl.playTakeRate.rate) || excludes(bl.playTakeRate, c.playTakeRate.rate);
      moveClass = moved ? 'moved' : 'not-moved';
      deltaCell = `<td class="${moveClass}">${delta >= 0 ? '+' : ''}${pct(delta)}</td>`;
    }
    // Conditional formatting: a take rate whose interval sits fully above/below the field
    // median is bolded and colored, matching the "auto-include vs. dead card" watch items --
    // green for a card that keeps getting chosen, red for one that keeps getting passed over.
    const takeClass = c.offerCount === 0 ? '' : excludes(c.playTakeRate, fieldMedian)
      ? (c.playTakeRate.rate > fieldMedian ? 'hi' : 'lo')
      : '';
    // Win-rate intervals are wide enough relative to the field that the numbers alone under-sell
    // how much they overlap; the bar makes it visual. Zoomed to 30-70% rather than the 0-100%
    // the take-rate bars use -- at full scale every card's win rate is the same short dash in
    // the middle, which is true but unreadable.
    const drawBar = c.winRateWhenDrawn.trials === 0 ? '' : intervalBar(
      zoomInterval(c.winRateWhenDrawn), zoomRate(fieldWinDrawn),
      bl ? zoomInterval(bl.winRateWhenDrawn) : undefined);
    const score = composite[c.cardId];
    const scoreClass = score === null || score === undefined ? '' : (score > 0.5 ? 'hi' : score < -0.5 ? 'lo' : '');
    const scoreCell = score === null || score === undefined ? '—' : (score >= 0 ? '+' : '') + num(score, 2);
    return `<tr class="${thin ? 'thin-n' : ''} ${cardOutliersOnly ? 'outlier' : ''}">
      <td>${c.cardId}${info ? ` <span class="hint" style="margin:0">${info.name}</span>` : ''}</td>
      <td>${info ? resChip(info.attackType) : '—'}</td>
      <td>${info ? costText(info.cost) : '—'}</td>
      <td>${info && info.kind === 'creature' ? info.health : '—'}</td>
      <td class="effect-text">${info ? info.effectText : ''}</td>
      <td class="take ${scoreClass}" title="Composite power score: z-score average of take rate, take rate/turn, win rate (played), win rate (drawn), plus (for creatures) take-rate-weighted move take rate and move win rate. Opinionated rollup -- read the individual columns before trusting it.">${scoreCell}</td>
      <td class="take ${takeClass}">${pct(c.playTakeRate.rate)}</td>
      <td>${intervalBar(c.playTakeRate, bl ? undefined : fieldMedian, bl ? bl.playTakeRate : undefined)}</td>
      ${deltaCell}
      <td>${c.offerCount}</td>
      <td>${c.playCount}</td>
      <td>${pct(c.playTakeRatePerTurn.rate)}</td>
      <td>${c.offeredInTurns}</td>
      <td class="wr ${wrClass(c.winRateWhenPlayed, fieldWinPlayed)}">${pctInterval(c.winRateWhenPlayed)}</td>
      <td class="wr ${wrClass(c.winRateWhenDrawn, fieldWinDrawn)}">${pctInterval(c.winRateWhenDrawn)}${drawBar}</td>
      <td>${pct(c.costPressure.rate)}</td>
      <td>${c.survivalSteps.count > 0 ? num(c.survivalSteps.mean, 1) : '—'}</td>
      <td>${c.survivalSteps.count > 0 ? pct(c.scoredWhileAliveRate.rate) : '—'}</td>
    </tr>`;
  }).join('');
}

function cardHeader() {
  const cols = [
    ['cardId', 'Card'],
    [null, 'Type'],
    [null, 'Cost'],
    [null, 'Health'],
    [null, 'Effect'],
    ['composite', 'Power score'],
    ['playTakeRate.rate', 'Take rate'],
    [null, 'Interval'],
    ...(baseline ? [[null, 'Δ vs baseline']] : []),
    ['offerCount', 'Offers (n)'],
    ['playCount', 'Plays'],
    ['playTakeRatePerTurn.rate', 'Take rate/turn'],
    ['offeredInTurns', 'Turns offered (n)'],
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

// Field averages for the numeric columns, rendered as a pinned row under the header.
//
// The reason it lives here rather than in the reader's head: every rate in these tables is only
// meaningful RELATIVE to the field (a 38% take rate is high or low depending on what the other 35
// cards did), and the composite power score is already z-scored against exactly this mean. Having
// the baseline on screen turns "is this card an outlier" from a memory exercise into a comparison.
//
// Unweighted mean over the SAME rows the table is currently showing -- so it respects the min-n
// filter and the outliers-only toggle, and re-computes when either changes. Each card counts once
// regardless of how many games it appeared in: this is "the average card," not "the average
// play," which is the question a per-card table is asking.
function averageRow(rows, columns) {
  const mean = (get) => {
    const vals = rows.map(get).filter(v => typeof v === 'number' && !Number.isNaN(v));
    return vals.length ? vals.reduce((a, b) => a + b, 0) / vals.length : null;
  };
  const cells = columns.map(col => {
    if (!col) return '<td></td>';
    const v = mean(col.get);
    return `<td>${v === null ? '—' : col.fmt(v)}</td>`;
  });
  return `<tr class="avg-row"><td>field average</td>${cells.join('')}</tr>`;
}

function renderCardTable() {
  const rows = visibleCardRows();
  const composite = computeCompositeScores(
    metrics.cardStats, parseInt(document.getElementById('card-min-n').value, 10) || 0);
  const cols = [
    null, null, null, null,                                             // type, cost, health, effect
    { get: c => composite[c.cardId], fmt: v => (v >= 0 ? '+' : '') + num(v, 2) },
    { get: c => c.playTakeRate.rate, fmt: pct },
    null,                                                               // interval bar
    ...(baseline ? [null] : []),                                        // delta
    { get: c => c.offerCount, fmt: v => num(v, 0) },
    { get: c => c.playCount, fmt: v => num(v, 0) },
    { get: c => c.playTakeRatePerTurn.rate, fmt: pct },
    { get: c => c.offeredInTurns, fmt: v => num(v, 0) },
    { get: c => c.winRateWhenPlayed.rate, fmt: pct },
    { get: c => c.winRateWhenDrawn.rate, fmt: pct },
    { get: c => c.costPressure.rate, fmt: pct },
    { get: c => c.survivalSteps.count > 0 ? c.survivalSteps.mean : null, fmt: v => num(v, 1) },
    { get: c => c.survivalSteps.count > 0 ? c.scoredWhileAliveRate.rate : null, fmt: pct },
  ];
  document.querySelector('#card-table thead').innerHTML = cardHeader() + averageRow(rows, cols);
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
    const info = moveInfo[m.cardId + '::' + m.moveName];
    const fieldMedian = median(metrics.moveStats.filter(x => x.offerCount > 0).map(x => x.useTakeRate.rate));
    const takeClass = m.offerCount === 0 ? '' : excludes(m.useTakeRate, fieldMedian)
      ? (m.useTakeRate.rate > fieldMedian ? 'hi' : 'lo')
      : '';
    // The move's OWN cost sits beside its creature's play cost deliberately: the design
    // expectation is that a more expensive creature earns better moves, and that trend is only
    // readable with both numbers on the same row. A cheap creature whose moves out-take an
    // expensive one's is the shape worth catching.
    const parent = cardInfo[m.cardId];
    return `<tr class="${thin ? 'thin-n' : ''}">
      <td>${m.moveName}</td>
      <td>${m.cardId}</td>
      <td>${parent ? costText(parent.cost) : '—'}</td>
      <td>${info ? resChip(info.attackType) : '—'}</td>
      <td>${info ? costText(info.cost) : '—'}</td>
      <td class="effect-text">${info ? info.effectText : ''}</td>
      <td class="take ${takeClass}">${pct(m.useTakeRate.rate)}</td>
      <td>${intervalBar(m.useTakeRate)}</td>
      <td>${m.offerCount}</td>
      <td>${m.useCount}</td>
      <td>${pct(m.useTakeRatePerTurn.rate)}</td>
      <td>${m.offeredInTurns}</td>
      <td>${pct(m.winRateWhenUsed.rate)}</td>
    </tr>`;
  }).join('');
}

function moveHeader() {
  const cols = [
    ['moveName', 'Move'],
    ['cardId', 'Card'],
    [null, 'Creature cost'],
    [null, 'Type'],
    [null, 'Move cost'],
    [null, 'Effect'],
    ['useTakeRate.rate', 'Take rate'],
    [null, 'Interval'],
    ['offerCount', 'Offers (n)'],
    ['useCount', 'Uses'],
    ['useTakeRatePerTurn.rate', 'Take rate/turn'],
    ['offeredInTurns', 'Turns offered (n)'],
    ['winRateWhenUsed.rate', 'Win rate'],
  ];
  return '<tr>' + cols.map(([key, label]) => {
    if (!key) return `<th>${label}</th>`;
    const arrow = moveSort.key === key ? (moveSort.dir === 1 ? '▲' : '▼') : '';
    return `<th data-key="${key}">${label} <span class="arrow">${arrow}</span></th>`;
  }).join('') + '</tr>';
}

function renderMoveTable() {
  const minN = parseInt(document.getElementById('move-min-n').value, 10) || 0;
  const rows = metrics.moveStats.filter(m => m.offerCount >= minN);
  const cols = [
    null, null, null, null,                                     // card, creature cost, type, move cost
    null,                                                       // effect
    { get: m => m.useTakeRate.rate, fmt: pct },
    null,                                                       // interval bar
    { get: m => m.offerCount, fmt: v => num(v, 0) },
    { get: m => m.useCount, fmt: v => num(v, 0) },
    { get: m => m.useTakeRatePerTurn.rate, fmt: pct },
    { get: m => m.offeredInTurns, fmt: v => num(v, 0) },
    { get: m => m.winRateWhenUsed.rate, fmt: pct },
  ];
  document.querySelector('#move-table thead').innerHTML = moveHeader() + averageRow(rows, cols);
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
document.getElementById('card-drawwr-btn').addEventListener('click', (e) => {
  cardDrawWrOnly = !cardDrawWrOnly;
  e.target.classList.toggle('active', cardDrawWrOnly);
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

document.getElementById('resource-min-n').addEventListener('input', renderByResource);

renderProvenance();
renderSummary();
renderMarginChart();
renderHandChart();
renderBoardPresenceCharts();
renderEconomy();
renderByResource();
renderCardTable();
renderMoveTable();
</script>
</body>
</html>
""";
}
