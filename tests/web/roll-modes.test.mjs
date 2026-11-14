// Tests for the modes of version 0.5 on the Piano Roll (the experiment Two playheads and modes), run in Node on the
// compiled modules:   dotnet build src/MotiffLab.Web && node --test tests/web

import assert from "node:assert/strict";
import { test } from "node:test";

import { actsOnRoll, drawPlayheads, drawPositionLine, gestureFor, notesInBox, rollPlaces, selectsOnRoll } from "../../src/MotiffLab.Web/wwwroot/js/roll/roll-modes.js";
import { viewOf } from "../../src/MotiffLab.Web/wwwroot/js/playback/playback-model.js";
import { practiced } from "../../src/MotiffLab.Web/wwwroot/js/layout/practice-view.js";

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

test("the roll shows one playhead at the position: blue inside the selection, black outside, playing or stopped (#543 6)", () => {
    const selection = { start: 960, end: 1920 };
    const at = (tick, loop, transport) => rollPlaces(viewOf({ selection, loop, tick, transport }));
    assert.deepEqual(at(1200, false, "playing"), { black: 1200, blue: 1200, playing: true }, "inside: blue, at the position");
    assert.deepEqual(at(1200, true, "stopped"), { black: 1200, blue: 1200, playing: false });
    assert.deepEqual(at(2400, false, "playing"), { black: 2400, blue: null, playing: true }, "past the end: black, no blue one waiting at the edge");
    assert.deepEqual(at(480, true, "stopped"), { black: 480, blue: null, playing: false }, "before it with Цикл on: black, no blue one at the start");
    assert.deepEqual(at(1920, false, "stopped"), { black: 1920, blue: null, playing: false }, "at the end is outside: black");
    assert.deepEqual(rollPlaces(viewOf({ selection: null, loop: false, tick: 300, transport: "stopped" })), { black: 300, blue: null, playing: false });
    // Across the selection's edge only the colour changes: the place is the position each frame.
    const crossing = [1900, 1919, 1920, 1940].map((tick) => at(tick, false, "playing"));
    assert.deepEqual(crossing.map((places) => places.blue ?? places.black), [1900, 1919, 1920, 1940]);
    assert.deepEqual(crossing.map((places) => places.blue !== null), [true, true, false, false]);
    // Practice view hides the selection's playhead: black inside too (#533 11).
    assert.deepEqual(rollPlaces(practiced(viewOf({ selection, loop: false, tick: 1200, transport: "playing" }), "on")), { black: 1200, blue: null, playing: true });
});


test("outside the selection the web's roll draws an ink line with its dot, hollow when it stands; no grey column (#479 item 19)", () => {
    const { context, drawn } = recorder();
    drawPlayheads(context, frame, { black: 960, blue: null, playing: false }, inks);
    assert.deepEqual(drawn.rects, [{ x: 157, y: 24, w: 2, h: 276, colour: "ink" }], "one 2 px line, nothing a beat wide");
    assert.deepEqual(drawn.discs.map((disc) => disc.colour), ["surface", "ink", "surface"], "a ring, the dot, and its hollow middle");

    const playing = recorder();
    drawPlayheads(playing.context, frame, { black: 960, blue: null, playing: true }, inks);
    assert.deepEqual(playing.drawn.discs.map((disc) => disc.colour), ["surface", "ink"], "solid while playing");
});

test("inside it the one line is cobalt with its knob, and no black dot hangs under it (#543 6)", () => {
    const { context, drawn } = recorder();
    drawPlayheads(context, frame, { black: 1200, blue: 1200, playing: false }, inks);
    assert.deepEqual(drawn.rects.map((rect) => [rect.x, rect.colour]), [[181, "accent"]], "one line, cobalt");
    assert.deepEqual(drawn.discs.map((disc) => disc.colour), ["surface", "accent"], "one knob, ringed");
    assert.equal(drawn.discs.at(-1).y, 24, "on the bar numbers' edge");
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

test("while the Pen is on the stylus changes no selection on the roll; a finger and the mouse still do (#511 4.1)", () => {
    assert.equal(selectsOnRoll("pen", true), false);
    assert.equal(selectsOnRoll("touch", true), true);
    assert.equal(selectsOnRoll("mouse", true), true);
    assert.equal(selectsOnRoll("pen", false), true, "out of the Pen the stylus selects as before");
});

test("in Expand no pointer changes the selection on the roll, while each still moves it as before (#521 19.3)", () => {
    for (const pointer of ["pen", "touch", "mouse"]) {
        assert.equal(selectsOnRoll(pointer, false, true), false, `${pointer} selects nothing in Expand`);
        assert.equal(actsOnRoll(pointer, false), true, `${pointer} still moves the roll in Expand`);
    }
    assert.equal(actsOnRoll("pen", true), false, "the Pen's stylus does nothing on the roll, in Expand or not");
    assert.equal(actsOnRoll("touch", true), true);
});
