// Tests for the strips of the iPad's roll (v0.5.2, #479 item 25; Scripts/roll/roll-strips.ts), run in Node on the compiled
// output: the black and the blue playheads of the playback model; the loop strip; the whole piece in one line.
import assert from "node:assert/strict";
import test from "node:test";
import {
    drawLoopLane,
    drawOverview,
    drawPosition,
    insideOf,
    laneShown,
    overviewGrab,
    overviewTick,
    overviewTrack,
    overviewX,
    viewGrabOffset,
} from "../../src/MotiffLab.Web/wwwroot/js/roll/roll-strips.js";

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

test("the roll's one playhead is a line with its marker in the loop strip, cobalt inside the selection and ink outside (#543 6)", () => {
    const outside = recorder();
    drawPosition(outside.context, frame, { black: 480, blue: null, playing: true }, inks);
    assert.deepEqual(outside.drawn.rects, [{ x: 109, y: 24, w: 2, h: 300 - 24, colour: "ink" }], "from the loop strip down");
    assert.deepEqual(outside.drawn.discs.map((disc) => [disc.x, disc.y, disc.colour]), [[110, 24 + 14, "surface"], [110, 24 + 14, "ink"]], "a black marker in the loop strip at the line");

    const inside = recorder();
    drawPosition(inside.context, frame, { black: 1200, blue: 1200, playing: true }, inks);
    assert.deepEqual(inside.drawn.rects.map((rect) => [rect.x, rect.colour]), [[181, "accent"]], "one cobalt line, no ink one under it");
    assert.deepEqual(inside.drawn.discs.map((disc) => [disc.x, disc.colour]), [[182, "surface"], [182, "accent"]], "one blue marker at the line");

    const stopped = recorder();
    drawPosition(stopped.context, frame, { black: 2400, blue: null, playing: false }, inks);
    assert.equal(stopped.drawn.discs.length, 3, "stopped, the marker still shows, hollow");
    assert.equal(stopped.drawn.discs.at(-1).colour, "surface");
});

test("in practice view the loop strip is hidden and takes no press; with the lanes otherwise it is there (#543, 0.5.6 tail)", () => {
    assert.equal(laneShown(true, "off"), true);
    assert.equal(laneShown(true, "on"), false, "hidden, its handles, its tap and its drag take nothing");
    assert.equal(laneShown(true, "reveal"), false, "nor while practice view shows its controls");
    assert.equal(laneShown(false, "off"), false, "no lanes, no strip (the web's roll)");
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
    // No loop sign inside the selection (#521 6): the strip, its two handles' fill and ring, and nothing else.
    assert.equal(one.drawn.discs.length, 3, "the strip's fill and the two handles");
    assert.equal(one.drawn.strokes, 3, "the strip's outline and the two handles' rings, no ring with an arrow");
});

test("a tap on the whole piece finds its tick along the track, clamped to the piece", () => {
    const track = overviewTrack(1048);
    assert.deepEqual(track, { left: 48, right: 1008 });
    assert.equal(overviewTick(48, 1048, 96000), 0);
    assert.equal(overviewTick(528, 1048, 96000), 48000);
    assert.equal(overviewTick(2000, 1048, 96000), 96000);
    assert.equal(overviewTick(0, 1048, 96000), 0);
});

test("the whole piece writes the position and its black dot in ink, also inside the selection, and the last bar on the right", () => {
    const { context, drawn } = recorder();
    drawOverview(context, 1048, { tick: 1200, label: "9.2", end: 96000, lastBar: "105", view: { start: 0, end: 4800 }, selection: { start: 960, end: 1920 }, playing: false }, inks);
    assert.deepEqual(drawn.texts.map((text) => [text.text, text.colour]), [["9.2", "ink"], ["105", "muted"]]);
    assert.equal(drawn.discs.at(-2).colour, "ink", "the black dot");
    assert.equal(drawn.discs.at(-1).colour, "surface", "stopped, the dot is hollow");
});

test("on the whole piece the black dot and the frame are two targets: the dot takes the position, the rest the view (#521 1)", () => {
    const width = 1048;
    const end = 96000;
    const dot = overviewX(48000, width, end);
    assert.equal(dot, 528);
    assert.equal(overviewGrab(dot + 10, width, 48000, end, 22), "position");
    assert.equal(overviewGrab(dot + 30, width, 48000, end, 22), "view", "next to the dot, inside the frame or not, the view");
    const view = { start: 40000, end: 56000 };
    assert.equal(viewGrabOffset(44000, view), 4000, "taken inside the frame, it keeps its place under the pointer");
    assert.equal(viewGrabOffset(80000, view), 8000, "taken outside it, by its middle: the frame comes to the pointer");
});
