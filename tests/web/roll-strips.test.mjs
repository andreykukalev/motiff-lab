// Tests for the strips of the iPad's roll (v0.5.2, #479 item 25; Scripts/roll/roll-strips.ts), run in Node on the compiled
// output: one position, ink outside the selection and cobalt inside; the loop strip; the whole piece in one line.
import assert from "node:assert/strict";
import test from "node:test";
import { drawLoopLane, drawOverview, drawPosition, insideOf, overviewTick, overviewTrack } from "../../src/MotiffLab.Web/wwwroot/js/roll/roll-strips.js";

/** A canvas context that keeps the colour of each line and disc drawn, and each text written. */
function recorder() {
    const drawn = { rects: [], discs: [], texts: [], strokes: 0 };
    let arc = null;
    const context = {
        fillStyle: "",
        strokeStyle: "",
        lineWidth: 1,
        font: "",
        textAlign: "",
        textBaseline: "",
        fillRect: (x, y, w, h) => drawn.rects.push({ x, y, w, h, colour: context.fillStyle }),
        beginPath: () => { arc = null; },
        arc: (x, y, radius) => { arc = { x, y, radius }; },
        roundRect: () => {},
        moveTo: () => {},
        lineTo: () => {},
        closePath: () => {},
        fill: () => drawn.discs.push({ ...arc, colour: context.fillStyle }),
        stroke: () => { drawn.strokes++; },
        fillText: (text, x) => drawn.texts.push({ text, x, colour: context.fillStyle }),
    };
    return { context, drawn };
}

const inks = { ink: "ink", accent: "accent", tint: "tint", surface: "surface", line: "line", muted: "muted" };
const frame = { top: 24, left: 62, right: 800, height: 300, xOf: (tick) => 62 + tick / 10 };

test("inside the selection is from its start up to, not at, its end", () => {
    const selection = { start: 960, end: 1920 };
    assert.equal(insideOf(selection, 959), false);
    assert.equal(insideOf(selection, 960), true);
    assert.equal(insideOf(selection, 1919), true);
    assert.equal(insideOf(selection, 1920), false);
    assert.equal(insideOf(null, 1000), false);
});

test("the one position is ink outside the selection and cobalt inside it", () => {
    const outside = recorder();
    drawPosition(outside.context, frame, 480, { start: 960, end: 1920 }, true, inks);
    assert.equal(outside.drawn.rects[0].colour, "ink");
    const inside = recorder();
    drawPosition(inside.context, frame, 1200, { start: 960, end: 1920 }, true, inks);
    assert.equal(inside.drawn.rects[0].colour, "accent");
    assert.equal(inside.drawn.discs.at(-1).y, 24 + 14, "its dot sits in the loop lane");
});

test("the loop strip draws nothing without a selection and a handle at each end with one", () => {
    const none = recorder();
    drawLoopLane(none.context, frame, null, true, inks);
    assert.deepEqual(none.drawn.discs, []);
    const one = recorder();
    drawLoopLane(one.context, frame, { start: 960, end: 1920 }, true, inks);
    const handles = one.drawn.discs.filter((disc) => disc.radius === 6);
    assert.deepEqual(handles.map((disc) => disc.x), [158, 254]);
    assert.equal(one.drawn.discs[0].colour, "tint", "tinted while it loops");
});

test("a tap on the whole piece finds its tick along the track, clamped to the piece", () => {
    const track = overviewTrack(1048);
    assert.deepEqual(track, { left: 48, right: 1008 });
    assert.equal(overviewTick(48, 1048, 96000), 0);
    assert.equal(overviewTick(528, 1048, 96000), 48000);
    assert.equal(overviewTick(2000, 1048, 96000), 96000);
    assert.equal(overviewTick(0, 1048, 96000), 0);
});

test("the whole piece writes the position in cobalt inside the selection and the last bar on the right", () => {
    const { context, drawn } = recorder();
    drawOverview(context, 1048, { tick: 1200, label: "9.2", end: 96000, lastBar: "105", view: { start: 0, end: 4800 }, selection: { start: 960, end: 1920 }, playing: false }, inks);
    assert.deepEqual(drawn.texts.map((text) => [text.text, text.colour]), [["9.2", "accent"], ["105", "muted"]]);
    assert.equal(drawn.discs.at(-1).colour, "surface", "stopped, the dot is hollow");
});
