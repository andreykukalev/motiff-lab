// Tests for the score's geometry (decision record 0018, preparation item 11), run in Node on the compiled
// module and the vendored Verovio build, on Für Elise laid out as A4 pages and as one strip:
//   dotnet build src/MotiffLab.Web && node --test tests/web

import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";

import { BRACKET_LIFT, RING_GROWTH, ScoreGeometry, readPage } from "../../src/MotiffLab.Web/wwwroot/js/score/score-geometry.js";
import { A4_LAYOUT } from "../../src/MotiffLab.Web/wwwroot/js/score/score-page.js";
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
    assert.equal(first.width, 2100);
    assert.equal(first.height, 2970);
    assert.ok(first.systems.length >= 4);
    assert.ok(first.bars.length >= 10);
    for (const bar of first.bars) {
        assert.equal(bar.staves.length, 2);
        // Staves 7.2 mm high: four spaces of 1.8 mm, in tenths of a millimetre.
        assert.ok(Math.abs(bar.staves[0].space - 18) < 0.5, `space ${bar.staves[0].space}`);
        assert.ok(bar.left >= 150 - 1 && bar.right <= 2100 - 150 + 1);
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
    assert.deepEqual(frames[3], { tick: bar.tick, left: bar.left, right: bar.right, top: bar.ink.top, bottom: bar.ink.bottom, staffTop: bar.staves[0].top, space: bar.staves[0].space });
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
