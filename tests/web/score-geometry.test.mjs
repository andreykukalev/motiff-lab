// Tests for the score's geometry (decision record 0018, preparation item 11), run in Node on the compiled
// module and the vendored Verovio build, on Für Elise laid out as A4 pages and as one strip:
//   dotnet build src/MotiffLab.Web && node --test tests/web

import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";

import { BRACKET_LIFT, RING_GROWTH, ScoreGeometry, placeInBar, readPage } from "../../src/MotiffLab.Web/wwwroot/js/score/score-geometry.js";
import { A4_LAYOUT, titleBlock } from "../../src/MotiffLab.Web/wwwroot/js/score/score-page.js";
import { ScoreTimeline } from "../../src/MotiffLab.Web/wwwroot/js/score/score-timeline.js";
import createVerovioModule from "../../src/MotiffLab.Web/wwwroot/lib/verovio/verovio-module.mjs";
import { VerovioToolkit } from "../../src/MotiffLab.Web/wwwroot/lib/verovio/verovio.mjs";

const FUR_ELISE = readFileSync(new URL("../../design/kit/elise.musicxml", import.meta.url), "utf8");

/** The strip as the score view lays it out (score-view.ts), for a view 1200 px wide. */
const STRIP = {
    scale: 40,
    pageWidth: 3000,
    pageMarginLeft: 40,
    pageMarginRight: 40,
    pageMarginTop: 40,
    pageMarginBottom: 40,
    adjustPageHeight: true,
    breaks: "auto",
    header: "none",
    footer: "none",
    justifyVertically: false,
    svgViewBox: true,
};

let module;

async function engrave(options) {
    module ??= await createVerovioModule();
    const toolkit = new VerovioToolkit(module);
    toolkit.setOptions(options);
    assert.ok(toolkit.loadData(FUR_ELISE));
    const svgs = [];
    for (let page = 1; page <= toolkit.getPageCount(); page++) {
        svgs.push(toolkit.renderToSVG(page));
    }
    const timeline = ScoreTimeline.fromTimemap(toolkit.renderToTimemap({ includeRests: true, includeMeasures: true }));
    toolkit.destroy?.();
    return { pages: svgs.map((svg, index) => readPage(svg, index + 1)), timeline };
}

/** Pages one under another, each shown at its own width scaled to `width` pixels, 20 px apart. */
function stacked(pages, width) {
    let top = 0;
    return pages.map((page) => {
        const scale = width / page.width;
        const placement = { left: 0, top, scale };
        top += page.height * scale + 20;
        return placement;
    });
}

test("a page is read in its own units: bars, their two staves and the notes on them", async () => {
    const { pages } = await engrave({ ...A4_LAYOUT });
    const first = pages[0];

    assert.equal(first.number, 1);
    // At scale 50 the view box is the A4 page in fifths of a millimetre.
    assert.equal(first.width, 1050);
    assert.equal(first.height, 1485);
    assert.ok(first.systems.length >= 4);
    assert.ok(first.bars.length >= 10);
    for (const bar of first.bars) {
        assert.equal(bar.staves.length, 2);
        // Staves 7.2 mm high: four spaces of 1.8 mm, in fifths of a millimetre.
        assert.ok(Math.abs(bar.staves[0].space - 9) < 0.5, `space ${bar.staves[0].space}`);
        assert.ok(bar.left >= 75 - 1 && bar.right <= 1050 - 75 + 1);
        assert.ok(bar.staves[0].bottom < bar.staves[1].top);
        assert.equal(bar.top, bar.staves[0].top);
        assert.equal(bar.bottom, bar.staves[1].bottom);
        assert.ok(bar.ink.top <= bar.top && bar.ink.bottom >= bar.bottom);
    }
    // Bars on one system share their staves' height and follow each other left to right.
    for (const system of first.systems) {
        const bars = system.bars.map((index) => first.bars[index]);
        bars.slice(1).forEach((bar, index) => {
            assert.equal(bar.top, bars[index].top);
            assert.ok(Math.abs(bar.left - bars[index].right) < 1);
        });
    }
    // A note's head sits on its staff, within a space of it.
    for (const note of first.notes) {
        const staff = first.bars[note.bar].staves[note.staff - 1];
        assert.ok(note.cy >= staff.top - 8 * staff.space && note.cy <= staff.bottom + 8 * staff.space);
        assert.ok(note.left >= first.bars[note.bar].left - staff.space && note.right <= first.bars[note.bar].right + staff.space);
    }
});

test("the geometry puts every bar in time and maps bars to pages as the PDF does", async () => {
    const { pages, timeline } = await engrave({ ...A4_LAYOUT });
    const geometry = new ScoreGeometry(pages, stacked(pages, 800), (id) => timeline.measureOnset(id), 99_999);

    assert.ok(pages.length >= 2);
    const ticks = geometry.bars.map((bar) => bar.tick);
    assert.deepEqual(ticks, [...ticks].sort((a, b) => a - b));
    geometry.bars.slice(0, -1).forEach((bar, index) => assert.equal(bar.end, geometry.bars[index + 1].tick));
    assert.equal(geometry.bars.at(-1).end, 99_999);

    const map = geometry.barToPage();
    assert.equal(map.size, geometry.bars.length);
    assert.equal(map.get(0), 1);
    assert.equal(map.get(geometry.bars.at(-1).tick), pages.length);
    assert.equal(geometry.pageOf(geometry.bars.at(-1).tick + 1), pages.length);
    // Pages lie one under another, so a later page's bars are lower down.
    const lastOfFirst = geometry.bars.filter((bar) => bar.page === 1).at(-1);
    const firstOfSecond = geometry.bars.find((bar) => bar.page === 2);
    assert.ok(firstOfSecond.top > lastOfFirst.bottom);
    // Every engraved note the timeline knows has a box.
    const found = timeline.spans.filter((span) => geometry.note(span.id) !== undefined).length;
    assert.ok(found / timeline.spans.length > 0.95, `${found} of ${timeline.spans.length}`);
});

test("a passage across a system break has a run on each system, a bracket over it and a band round it", async () => {
    const { pages, timeline } = await engrave({ ...A4_LAYOUT });
    const geometry = new ScoreGeometry(pages, stacked(pages, 800), (id) => timeline.measureOnset(id));
    const system = geometry.systems[0];
    const lastBar = geometry.bars[system.bars.at(-1)];
    const nextBar = geometry.bars[system.bars.at(-1) + 1];

    const runs = geometry.runs(lastBar.tick, nextBar.end);
    assert.equal(runs.length, 2);
    assert.deepEqual(runs.map((run) => [run.first, run.last]), [[true, false], [false, true]]);
    assert.equal(runs[0].left, lastBar.left);
    assert.equal(runs[0].right, lastBar.right);
    assert.equal(runs[1].left, nextBar.left);
    assert.ok(runs[1].top > runs[0].bottom);

    const bracket = geometry.bracket(lastBar.tick, nextBar.end);
    assert.equal(bracket[0].top, lastBar.top - BRACKET_LIFT);
    assert.equal(bracket[0].left, lastBar.left + 2);
    const band = geometry.band(lastBar.tick, nextBar.end);
    assert.equal(band[1].bottom, nextBar.bottom + 8);
    // Only the bottom staff: the left hand.
    const left = geometry.band(lastBar.tick, lastBar.end, 0, [2]);
    assert.equal(left[0].top, lastBar.staves[1].top);
    assert.deepEqual(geometry.runs(lastBar.end, lastBar.end), []);
});

test("a point on the score finds its bar, and a handle let go finds the nearest barline", async () => {
    const { pages, timeline } = await engrave({ ...A4_LAYOUT });
    const geometry = new ScoreGeometry(pages, stacked(pages, 800), (id) => timeline.measureOnset(id));
    const system = geometry.systems[1];
    const bars = system.bars.map((index) => geometry.bars[index]);
    const second = bars[1];
    const middle = (second.top + second.bottom) / 2;

    // Inside a bar, and above its system: the same bar.
    assert.equal(geometry.barNear(second.left + 3, middle), second);
    assert.equal(geometry.barNear(second.left + 3, system.top - 6), second);
    // Beside the system: its first or last bar.
    assert.equal(geometry.barNear(system.left - 50, middle), bars[0]);
    assert.equal(geometry.barNear(system.right + 50, middle), bars.at(-1));

    // A start let go just left of the second bar's left barline goes to the second bar; an end just right of the first bar's right barline, to the first.
    assert.equal(geometry.edgeNear(second.left - 4, middle, "start"), second);
    assert.equal(geometry.edgeNear(bars[0].right + 4, middle, "end"), bars[0]);
    assert.equal(new ScoreGeometry([], [], () => 0).barNear(0, 0), undefined);
});

test("rings, flags and the pencil's frames come from the same boxes", async () => {
    const { pages, timeline } = await engrave({ ...A4_LAYOUT });
    const geometry = new ScoreGeometry(pages, stacked(pages, 800), (id) => timeline.measureOnset(id));
    const note = geometry.notes[0];

    const ring = geometry.ring(note.id);
    assert.equal(ring.cx, note.cx);
    assert.equal(ring.diameter, Math.max(note.right - note.left, note.bottom - note.top) + RING_GROWTH);
    assert.equal(geometry.ring("missing"), undefined);

    const bar = geometry.bars[3];
    assert.deepEqual(geometry.flag(bar.tick + 1), { x: bar.right, y: bar.staves[0].top });

    const frames = geometry.barFrames();
    assert.equal(frames.length, geometry.bars.length);
    const line = geometry.systems[bar.system].bars.map((index) => geometry.bars[index]);
    assert.deepEqual(frames[3], {
        tick: bar.tick, left: bar.left, right: bar.right, top: bar.ink.top, bottom: bar.ink.bottom, staffTop: bar.staves[0].top, space: bar.staves[0].space,
        line: bar.system, lineLeft: line[0].left, lineRight: line[line.length - 1].right,
    });
    // Every bar of a line shares its edges: the first bar's left and the last bar's right.
    assert.ok(line.length > 1);
    for (const index of geometry.systems[bar.system].bars) {
        assert.equal(frames[index].lineLeft, line[0].left);
        assert.equal(frames[index].lineRight, line[line.length - 1].right);
    }
    // At 800 px for 2100 units, a space of 18 units is about 6.9 px.
    assert.ok(Math.abs(frames[3].space - (18 * 800) / 2100) < 0.2);
});

test("A4 pages and the strip give the same bars in time, and the same overlays where the bars fall", async () => {
    const a4 = await engrave({ ...A4_LAYOUT });
    const strip = await engrave(STRIP);
    const onA4 = new ScoreGeometry(a4.pages, stacked(a4.pages, 800), (id) => a4.timeline.measureOnset(id));
    // The strip's pages run on without a gap, 10 px in from the left.
    const onStrip = new ScoreGeometry(strip.pages, stacked(strip.pages, 1200).map((at, index) => ({ ...at, left: 10, top: at.top - 20 * index })), (id) => strip.timeline.measureOnset(id));

    assert.ok(strip.pages.length <= a4.pages.length);
    assert.deepEqual(onStrip.bars.map((bar) => bar.tick), onA4.bars.map((bar) => bar.tick));
    assert.ok(onStrip.systems.length < onA4.systems.length);
    assert.ok(onStrip.bars.every((bar) => bar.left >= 10));

    // The same passage has runs on both, each over its own bars.
    const start = onA4.bars[4].tick;
    const end = onA4.bars[9].tick;
    for (const geometry of [onA4, onStrip]) {
        const runs = geometry.runs(start, end);
        const bars = geometry.barsIn(start, end);
        assert.equal(bars.length, 5);
        assert.equal(runs[0].left, bars[0].left);
        assert.equal(runs.at(-1).right, bars.at(-1).right);
        assert.equal(new Set(bars.map((bar) => bar.system)).size, runs.length);
    }
});

test("a placement moves and scales the page, and a page without one is left out", async () => {
    const { pages, timeline } = await engrave({ ...A4_LAYOUT });
    const onset = (id) => timeline.measureOnset(id);
    const unit = new ScoreGeometry(pages, pages.map(() => ({ left: 0, top: 0, scale: 1 })), onset);
    const half = new ScoreGeometry(pages, pages.map(() => ({ left: 100, top: 50, scale: 0.5 })), onset);
    const firstOnly = new ScoreGeometry(pages, [{ left: 0, top: 0, scale: 1 }], onset);

    assert.equal(half.bars[0].left, 100 + unit.bars[0].left / 2);
    assert.equal(half.bars[0].staves[0].top, 50 + unit.bars[0].staves[0].top / 2);
    assert.equal(half.bars[0].staves[0].space, unit.bars[0].staves[0].space / 2);
    assert.ok(firstOnly.bars.every((bar) => bar.page === 1));
    assert.ok(firstOnly.bars.length < unit.bars.length);
});

test("an ossia above an excerpt is smaller and its bars line up with the original's", async () => {
    // As NotationGenerator writes an excerpt with an ossia (0018, preparation item 10): the ossia's staff first, at 75%.
    const mei = `<?xml version="1.0" encoding="UTF-8"?>
<mei xmlns="http://www.music-encoding.org/ns/mei" meiversion="5.1"><meiHead><fileDesc><titleStmt><title>Ossia</title></titleStmt><pubStmt/></fileDesc></meiHead>
<music><body><mdiv><score><scoreDef meter.count="4" meter.unit="4" keysig="0"><staffGrp>
<staffGrp><label>Variation</label><staffDef n="1" lines="5" clef.shape="G" clef.line="2" scale="75%"/></staffGrp>
<staffGrp symbol="brace" bar.thru="true"><staffDef n="2" lines="5" clef.shape="G" clef.line="2"/><staffDef n="3" lines="5" clef.shape="F" clef.line="4"/></staffGrp>
</staffGrp></scoreDef><section>
<measure n="5"><staff n="1"><layer n="1"><note xml:id="ossia-a" pname="e" oct="5" dur="8" dots="1"/><note xml:id="ossia-b" pname="d" oct="5" dur="16"/><note xml:id="ossia-c" pname="e" oct="5" dur="4"/><rest dur="2"/></layer></staff>
<staff n="2"><layer n="1"><note xml:id="a" pname="e" oct="5" dur="4"/><note xml:id="b" pname="d" oct="5" dur="4"/><note xml:id="c" pname="e" oct="5" dur="2"/></layer></staff>
<staff n="3"><layer n="1"><note xml:id="d" pname="c" oct="3" dur="1"/></layer></staff></measure>
<measure n="6"><staff n="1"><layer n="1"><mRest/></layer></staff><staff n="2"><layer n="1"><note xml:id="e" pname="g" oct="5" dur="1"/></layer></staff><staff n="3"><layer n="1"><mRest/></layer></staff></measure>
</section></score></mdiv></body></music></mei>`;
    module ??= await createVerovioModule();
    const toolkit = new VerovioToolkit(module);
    toolkit.setOptions(STRIP);
    assert.ok(toolkit.loadData(mei));
    const page = readPage(toolkit.renderToSVG(1), 1);
    const timeline = ScoreTimeline.fromTimemap(toolkit.renderToTimemap({ includeRests: true, includeMeasures: true }));
    toolkit.destroy?.();
    const geometry = new ScoreGeometry([page], [{ left: 0, top: 0, scale: 1 }], (id) => timeline.measureOnset(id));

    assert.equal(geometry.bars.length, 2);
    for (const bar of geometry.bars) {
        const [ossia, treble, bass] = bar.staves;
        assert.equal(bar.staves.length, 3);
        assert.ok(Math.abs(ossia.space / treble.space - 0.75) < 0.01, `${ossia.space} against ${treble.space}`);
        assert.equal(Math.round(ossia.left), Math.round(treble.left));
        assert.equal(Math.round(ossia.right), Math.round(bass.right));
    }
    // A band over the original only leaves the ossia out; its notes are found by their own ids.
    const original = geometry.band(0, 1920, 0, [2, 3]);
    assert.equal(original[0].top, geometry.bars[0].staves[1].top);
    assert.equal(geometry.note("ossia-a").staff, 1);
    assert.equal(geometry.note("a").staff, 2);
    assert.ok(geometry.note("ossia-a").cx < geometry.note("b").cx);
});

test("the title block is read from Verovio's header alone, not from what follows it on the page", () => {
    const svg = '<svg><g class="page-margin"><g id="h1" class="pgHead">'
        + '<g class="rend"><title class="labelAttr">title</title>\n  <text><tspan>Waltz&#160;sketch</tspan></text></g>'
        + '<g class="rend"><title class="labelAttr">composer</title><text><tspan>A. N. Other</tspan></text></g>'
        + '</g><g class="system"><style>@font-face { font-family: Leland }</style><text>9</text></g></g></svg>';
    assert.deepEqual(titleBlock(svg), { title: "Waltz sketch", lines: ["A. N. Other"] });
    assert.equal(titleBlock("<svg><g class=\"system\"></g></svg>"), null);
});

test("the playhead moves from note to note in a bar and runs on to the barline after the last", () => {
    const bar = { left: 100, right: 300, top: 0, bottom: 50, tick: 0, end: 1920 };
    const points = [{ tick: 0, x: 120 }, { tick: 960, x: 200 }];
    assert.equal(placeInBar(bar, points, 0), 120);
    assert.equal(placeInBar(bar, points, 480), 160);
    assert.equal(placeInBar(bar, points, 960), 200);
    assert.equal(placeInBar(bar, points, 1440), 250);
    // Before the bar's first note it comes in at the pace of the notes after it, never left of the barline.
    assert.equal(placeInBar(bar, [{ tick: 960, x: 200 }], 0), 100);
    assert.ok(placeInBar(bar, [{ tick: 480, x: 140 }], 240) < 140);
    // A bar with no notes is crossed evenly.
    assert.equal(placeInBar(bar, [], 960), 200);
});

test("the playhead of Für Elise stays in its bar, spans the system's staves and only moves on in time", async () => {
    const { pages, timeline } = await engrave({ ...A4_LAYOUT });
    const geometry = new ScoreGeometry(pages, stacked(pages, 800), (id) => timeline.measureOnset(id), 99_999);
    const onsetOf = (id) => timeline.onsetOf(id);
    for (const bar of geometry.bars.slice(0, 12)) {
        let previous = -Infinity;
        for (let tick = bar.tick; tick < bar.end; tick += 60) {
            const at = geometry.playhead(tick, onsetOf);
            assert.equal(at.bar, bar);
            assert.ok(at.x >= bar.left - 1 && at.x <= bar.right + 1, `x ${at.x} in ${bar.left}–${bar.right}`);
            assert.ok(at.x >= previous - 0.001, `the playhead went back at tick ${tick}`);
            assert.equal(at.top, bar.staves[0].top);
            assert.equal(at.bottom, bar.staves[1].bottom);
            previous = at.x;
        }
    }
    assert.equal(geometry.playhead(-1, onsetOf), undefined);
});
