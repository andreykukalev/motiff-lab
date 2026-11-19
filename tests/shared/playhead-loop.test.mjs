// Tests for the blue playhead inside the selection's loop on the iPad's score (v0.6.3, #608 item 3: it appeared outside the
// selected region), run in Node on the compiled modules and the vendored Verovio build, on Für Elise laid out as A4 pages:
//   dotnet build src/shared && node --test tests/shared

import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";

import { next, viewOf } from "../../src/shared/wwwroot/js/playback/playback-model.js";
import { ScorePlayheadMarks, bandOf, edgeOf, inBand, playheadsOf } from "../../src/shared/wwwroot/js/score/playheads.js";
import { ScoreGeometry, readPage } from "../../src/shared/wwwroot/js/score/score-geometry.js";
import { A4_LAYOUT } from "../../src/shared/wwwroot/js/score/score-page.js";
import { ScoreTimeline } from "../../src/shared/wwwroot/js/score/score-timeline.js";
import { ScoreBeats } from "../../src/shared/wwwroot/js/score/selection-beats.js";
import createVerovioModule from "../../src/shared/wwwroot/lib/verovio/verovio-module.mjs";
import { VerovioToolkit } from "../../src/shared/wwwroot/lib/verovio/verovio.mjs";

const FUR_ELISE = readFileSync(new URL("../../design/kit/elise.musicxml", import.meta.url), "utf8");

/** Für Elise on A4 pages 800 px wide, one under another, with its beats every eighth (3/8, the pickup a beat long). */
async function score() {
    const module = await createVerovioModule();
    const toolkit = new VerovioToolkit(module);
    toolkit.setOptions({ ...A4_LAYOUT });
    assert.ok(toolkit.loadData(FUR_ELISE));
    const svgs = [];
    for (let page = 1; page <= toolkit.getPageCount(); page++) {
        svgs.push(toolkit.renderToSVG(page));
    }
    const timeline = ScoreTimeline.fromTimemap(toolkit.renderToTimemap({ includeRests: true, includeMeasures: true }));
    toolkit.destroy?.();
    const pages = svgs.map((svg, index) => readPage(svg, index + 1));
    let top = 0;
    const placements = pages.map((page) => {
        const scale = 800 / page.width;
        const placement = { left: 0, top, scale };
        top += page.height * scale + 20;
        return placement;
    });
    const geometry = new ScoreGeometry(pages, placements, (id) => timeline.measureOnset(id), 99_999);
    const onsetOf = (id) => timeline.onsetOf(id);
    const lines = [];
    for (let line = 0; line < geometry.bars[geometry.bars.length - 1].end; line += 240) {
        lines.push(line);
    }
    const beats = new ScoreBeats({ beats: () => lines, tickX: (tick) => geometry.playhead(tick, onsetOf)?.x });
    return { geometry, at: (tick) => geometry.playhead(tick, onsetOf), beats, byBars: new ScoreBeats({ beats: () => null }) };
}

/** Bar 1's second beat to bar 2's second beat: a selection by beats that starts and ends inside a bar. */
const BY_BEATS = { start: 480, end: 1440 };

test("by beats the band ends before the next beat's notes, where the blue playhead ran on to: now it stays inside (#608 3)", async () => {
    const { at, beats, geometry } = await score();
    const [run] = bandOf(geometry, beats, BY_BEATS);
    // The cause: the band stops BEAT_INSET short of the notes on the beat after the selection, and the playhead's place at
    // that tick is on those notes, so near the end and standing at the end it was drawn beside the band.
    const ending = at(BY_BEATS.end - 1);
    const standing = edgeOf(at(BY_BEATS.end), at(BY_BEATS.end - 1), BY_BEATS.end);
    assert.ok(ending.x > run.right, `${ending.x} past the band's end ${run.right}`);
    assert.ok(standing.x > run.right, `${standing.x} past the band's end ${run.right}`);
    for (let tick = BY_BEATS.start; tick < BY_BEATS.end; tick += 20) {
        const place = inBand(at(tick), [run]);
        assert.ok(place.x >= run.left && place.x <= run.right, `tick ${tick} at ${place.x}, the band ${run.left}–${run.right}`);
    }
    assert.equal(inBand(standing, [run]).x, run.right, "standing at the end, at the band's right edge");
    assert.equal(inBand(at(BY_BEATS.start), [run]).x, at(BY_BEATS.start).x, "at the start, where it was: inside already");
});

test("by bars the band covers whole bars and the blue playhead is drawn where it was (#608 3)", async () => {
    const { at, byBars, geometry } = await score();
    const band = bandOf(geometry, byBars, BY_BEATS);
    assert.equal(band[0].left, geometry.bars[1].left);
    assert.equal(band[0].right, geometry.bars[2].right);
    for (const tick of [BY_BEATS.start, 1000, BY_BEATS.end - 1]) {
        assert.deepEqual(inBand(at(tick), band), at(tick));
    }
});

test("a selection of one hand: the blue playhead covers that hand's staff, as the band does, not the other's (#608 3)", async () => {
    const { at, beats, geometry } = await score();
    const place = at(960);
    const [treble, bass] = geometry.bars[2].staves;
    for (const [hand, staff] of [["right", treble], ["left", bass]]) {
        const [run] = bandOf(geometry, beats, { ...BY_BEATS, hand });
        assert.equal(run.top, staff.top, hand);
        assert.equal(run.bottom, staff.bottom, hand);
        assert.deepEqual(inBand(place, [run]), { ...place, top: staff.top, bottom: staff.bottom }, hand);
    }
    assert.ok(place.top < bass.top && place.bottom > treble.bottom, "the cause: the playhead's line crosses both staves");
    const [both] = bandOf(geometry, beats, BY_BEATS);
    assert.deepEqual([both.top, both.bottom], [place.top, place.bottom], "both hands: every staff, as before");
});

test("a selection over two systems keeps the blue playhead on the run of the system it is on (#608 3)", async () => {
    const { at, byBars, geometry } = await score();
    const last = geometry.bars.findIndex((bar, index) => geometry.bars[index + 1]?.system !== bar.system);
    const span = { start: geometry.bars[last].tick, end: geometry.bars[last + 1].end };
    const band = bandOf(geometry, byBars, span);
    assert.equal(band.length, 2);
    // At the selection's end on the next line it stands at the band's right edge there, not at the start of the bar after.
    const standing = inBand(edgeOf(at(span.end), at(span.end - 1), span.end), band);
    assert.equal(standing.bar.system, band[1].system);
    assert.equal(standing.x, band[1].right);
    assert.deepEqual(inBand(at(span.start + 10), band), at(span.start + 10), "on the first line, inside its run");
    assert.equal(inBand(undefined, band), undefined);
});

test("stopped, a new selection moves the blue playhead with no position coming: the page draws it on the selection (#608 3)", () => {
    // The page drew the playheads only when Swift sent a position; stopped, a selection made elsewhere sends none (the
    // position stays, T9–T10), so the blue playhead stayed in the old selection, outside the new band, until Play.
    const stopped = { selection: { start: 960, end: 1920 }, loop: true, tick: 1200, transport: "stopped" };
    assert.equal(viewOf(stopped).blue, 1200);
    const moved = next(stopped, { type: "select", selection: { start: 4800, end: 5760 } });
    assert.equal(moved.state.tick, 1200, "the position does not move");
    assert.ok(moved.steps.every((step) => step.type !== "seek"), "and the engine is not moved: no position report follows");
    assert.equal(viewOf(moved.state).blue, 4800, "yet the blue playhead waits at the new selection's start");
    const cleared = next(moved.state, { type: "select", selection: null });
    assert.equal(viewOf(cleared.state).blue, null, "and goes with the selection");
    const off = next(stopped, { type: "cycle" });
    assert.equal(viewOf({ ...off.state, tick: 2400 }).blue, 1920, "Цикл off, after the selection: at its end");
});

/** The few parts of the DOM the playheads' marks use (as in playheads.test.mjs). */
class FakeMark {
    constructor() {
        this.className = "";
        this.style = {};
        this.hidden = false;
        this.children = [];
        this.parentElement = null;
        const owner = this;
        this.classList = {
            add: (name) => { owner.className = [...new Set([...owner.className.split(" ").filter(Boolean), name])].join(" "); },
            remove: (name) => { owner.className = owner.className.split(" ").filter((one) => one && one !== name).join(" "); },
            toggle: (name, on) => (on ? owner.classList.add(name) : owner.classList.remove(name)),
            contains: (name) => owner.className.split(" ").includes(name),
        };
    }

    setAttribute() {}

    addEventListener() {}

    append(...children) {
        for (const child of children) {
            child.parentElement = this;
            this.children.push(child);
        }
    }

    remove() {
        this.parentElement = null;
    }
}

test("given the band, the score draws the blue line inside it; without it, as before (the web passes none)", async () => {
    globalThis.document = { createElement: () => new FakeMark(), documentElement: {} };
    globalThis.getComputedStyle = () => ({ getPropertyValue: () => "" });
    try {
        const bar = { left: 100, right: 300, top: 50, bottom: 150, system: 0, tick: 0, end: 1920 };
        const host = new FakeMark();
        const marks = new ScorePlayheadMarks(host, { at: (tick) => ({ x: 100 + tick / 10, top: 60, bottom: 140, bar }), lineBottom: () => 150 });
        const line = () => host.children.find((child) => child.className === "ph-sel");
        const selection = { start: 0, end: 1440 };
        const playing = playheadsOf(viewOf({ selection, loop: true, tick: 1430, transport: "playing" }), selection);
        marks.place(playing);
        assert.equal(line().style.transform, "translate(242px, 60px)", "without a band, at its tick");
        marks.place({ ...playing, band: [{ system: 0, left: 104, right: 230, top: 100, bottom: 140 }] });
        assert.equal(line().style.transform, "translate(229px, 100px)", "with the band, at its right edge and on its staff");
        assert.equal(line().style.height, "40px");
    } finally {
        delete globalThis.document;
        delete globalThis.getComputedStyle;
    }
});
