// Tests for the modes of version 0.5 on the Piano Roll (the experiment Two playheads and modes), run in Node on the
// compiled modules:   dotnet build src/MotiffLab.Web && node --test tests/web

import assert from "node:assert/strict";
import { test } from "node:test";

import { beatAt, gestureFor, notesInBox } from "../../src/MotiffLab.Web/wwwroot/js/roll/roll-modes.js";

test("each mode begins its own gesture, and a press inside the selection only places a playhead in Select", () => {
    assert.equal(gestureFor("navigate", false), "pan");
    assert.equal(gestureFor("navigate", true), "pan");
    assert.equal(gestureFor("select", false), "range");
    assert.equal(gestureFor("select", true), "place");
    assert.equal(gestureFor("edit", false), "box");
    assert.equal(gestureFor("pen", false), "place");
});

test("the beat under a tick runs from its beat line to the next, within its bar", () => {
    const bars = [{ number: 1, onset: 0, end: 720, beatTicks: 240 }, { number: 2, onset: 720, end: 1440, beatTicks: 240 }];
    assert.deepEqual(beatAt(bars, 0), { start: 0, end: 240 });
    assert.deepEqual(beatAt(bars, 500), { start: 480, end: 720 });
    assert.deepEqual(beatAt(bars, 721), { start: 720, end: 960 });
    assert.equal(beatAt(bars, 2000), undefined);
});

const note = (id, onset, end, pitch) => ({ id, onset, end, pitch, track: 0, voice: 0, hand: 0, noteInferred: false, handInferred: false });

test("a box takes the notes sounding inside its time on its keys, drawn either way round", () => {
    const notes = [note("a", 0, 240, 60), note("b", 240, 480, 64), note("c", 480, 720, 72), note("d", 960, 1200, 64)];
    assert.deepEqual(notesInBox(notes, { fromTick: 100, toTick: 600, fromPitch: 59, toPitch: 65 }), [0, 1]);
    assert.deepEqual(notesInBox(notes, { fromTick: 600, toTick: 100, fromPitch: 73, toPitch: 59 }), [0, 1, 2]);
    assert.deepEqual(notesInBox(notes, { fromTick: 720, toTick: 900, fromPitch: 0, toPitch: 127 }), []);
});
