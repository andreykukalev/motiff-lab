// Tests for the modes of version 0.5 on the Piano Roll (the experiment Two playheads and modes), run in Node on the
// compiled modules:   dotnet build src/MotiffLab.Web && node --test tests/web

import assert from "node:assert/strict";
import { test } from "node:test";

import { drawPlayheads, drawPositionLine, gestureFor, notesInBox } from "../../src/MotiffLab.Web/wwwroot/js/roll/roll-modes.js";

test("each mode begins its own gesture, and a press inside the selection only places a playhead in Select", () => {
    assert.equal(gestureFor("navigate", false), "pan");
    assert.equal(gestureFor("navigate", true), "pan");
    assert.equal(gestureFor("select", false), "range");
    assert.equal(gestureFor("select", true), "place");
    assert.equal(gestureFor("edit", false), "box");
    assert.equal(gestureFor("pen", false), "place");
});

/** A canvas context that keeps what was drawn: each rectangle and each disc with the colour it was filled in. */
function recorder() {
    const drawn = { rects: [], discs: [] };
    let arc = null;
    const context = {
        fillStyle: "",
        fillRect: (x, y, w, h) => drawn.rects.push({ x, y, w, h, colour: context.fillStyle }),
        beginPath: () => { arc = null; },
        arc: (x, y, radius) => { arc = { x, y, radius }; },
        fill: () => drawn.discs.push({ ...arc, colour: context.fillStyle }),
    };
    return { context, drawn };
}

const frame = { xOf: (tick) => 62 + tick / 10, header: 24, left: 62, right: 800, height: 300 };
const inks = { ink: "ink", accent: "accent", surface: "surface", paper: "paper" };

test("the piece's playhead on the roll is an ink line with a dot, hollow when it stands; no grey column (#479 item 19)", () => {
    const { context, drawn } = recorder();
    drawPlayheads(context, frame, { selectionHolds: false, global: 0, selection: null }, 960, false, inks);
    assert.deepEqual(drawn.rects, [{ x: 157, y: 24, w: 2, h: 276, colour: "ink" }], "one 2 px line, nothing a beat wide");
    assert.deepEqual(drawn.discs.map((disc) => disc.colour), ["surface", "ink", "surface"], "a ring, the dot, and its hollow middle");

    const playing = recorder();
    drawPlayheads(playing.context, frame, { selectionHolds: false, global: 0, selection: 1200 }, 960, true, inks);
    assert.deepEqual(playing.drawn.discs.map((disc) => disc.colour), ["surface", "ink", "surface", "accent"], "solid while playing; the selection's knob in cobalt");
});

test("a playback line off the notes is not drawn", () => {
    const { context, drawn } = recorder();
    drawPositionLine(context, frame, 40, { colour: "ink", knobY: 24, radius: 6, solid: true }, inks);
    assert.deepEqual(drawn, { rects: [], discs: [] });
});

const note = (id, onset, end, pitch) => ({ id, onset, end, pitch, track: 0, voice: 0, hand: 0, noteInferred: false, handInferred: false });

test("a box takes the notes sounding inside its time on its keys, drawn either way round", () => {
    const notes = [note("a", 0, 240, 60), note("b", 240, 480, 64), note("c", 480, 720, 72), note("d", 960, 1200, 64)];
    assert.deepEqual(notesInBox(notes, { fromTick: 100, toTick: 600, fromPitch: 59, toPitch: 65 }), [0, 1]);
    assert.deepEqual(notesInBox(notes, { fromTick: 600, toTick: 100, fromPitch: 73, toPitch: 59 }), [0, 1, 2]);
    assert.deepEqual(notesInBox(notes, { fromTick: 720, toTick: 900, fromPitch: 0, toPitch: 127 }), []);
});
