// Tests for selection by where you touch on the roll (#601, experimental.placeSelection): the box and the lasso, the span
// of picked notes, narrowing at an edge, moves of picked notes, and the press read by its place (roll-place-gesture.ts).
// Run in Node on the compiled modules:   dotnet build src/MotiffLab.Web && node --test tests/web

import assert from "node:assert/strict";
import { test } from "node:test";

import { addedPicks, insidePath, keptSteps, moveSteps, movedNote, narrowedPicks, notesInShape, pickedSpan } from "../../src/MotiffLab.Web/wwwroot/js/roll/roll-place.js";
import { RollPlaceGesture } from "../../src/MotiffLab.Web/wwwroot/js/roll/roll-place-gesture.js";

// A rising line over a held bass: four quarter notes C5 D5 E5 F5 over a whole-bar C3 (480 ticks a quarter, 4/4).
const NOTES = [
    { id: "b", onset: 0, end: 1920, pitch: 48 },
    { id: "m1", onset: 0, end: 480, pitch: 72 },
    { id: "m2", onset: 480, end: 960, pitch: 74 },
    { id: "m3", onset: 960, end: 1440, pitch: 76 },
    { id: "m4", onset: 1440, end: 1920, pitch: 77 },
];
// Beat lines of two bars.
const LINES = [0, 480, 960, 1440, 1920, 2400, 2880, 3360, 3840];

test("a box picks the notes whose centre lies inside it, either way round", () => {
    assert.deepEqual(notesInShape(NOTES, "box", [{ tick: 100, pitch: 79 }, { tick: 1300, pitch: 70 }]), [1, 2, 3]);
    assert.deepEqual(notesInShape(NOTES, "box", [{ tick: 1300, pitch: 70 }, { tick: 100, pitch: 79 }]), [1, 2, 3]);
    // A box that only grazes a note (its centre outside) does not take it: the bass's centre is at 960.
    assert.deepEqual(notesInShape(NOTES, "box", [{ tick: 0, pitch: 47 }, { tick: 400, pitch: 49 }]), []);
    assert.deepEqual(notesInShape(NOTES, "box", [{ tick: 0, pitch: 47 }]), []);
});

test("a lasso takes the melody over the bass that a box round the same time would take too", () => {
    const lasso = [{ tick: 0, pitch: 70 }, { tick: 1920, pitch: 75 }, { tick: 1920, pitch: 79 }, { tick: 0, pitch: 74 }];
    assert.deepEqual(notesInShape(NOTES, "lasso", lasso), [1, 2, 3, 4].filter((index) => insidePath({ tick: (NOTES[index].onset + NOTES[index].end) / 2, pitch: NOTES[index].pitch }, lasso)));
    assert.ok(!notesInShape(NOTES, "lasso", lasso).includes(0));
    assert.equal(insidePath({ tick: 1, pitch: 1 }, [{ tick: 0, pitch: 0 }, { tick: 2, pitch: 2 }]), false);
});

test("picks added from inside keep those before, once each, in order", () => {
    assert.deepEqual(addedPicks(new Set([3, 1]), [2, 3]), [1, 2, 3]);
});

test("the span of picked notes goes out to the beats round them", () => {
    assert.deepEqual(pickedSpan(NOTES, [2], LINES), { start: 480, end: 960 });
    assert.deepEqual(pickedSpan([{ onset: 500, end: 700 }], [0], LINES), { start: 480, end: 960 });
    assert.deepEqual(pickedSpan(NOTES, [], LINES), null);
    // Past the last line, the notes' own time.
    assert.deepEqual(pickedSpan([{ onset: 3900, end: 4000 }], [0], LINES), { start: 3840, end: 4000 });
});

test("an edge moved narrows the picked notes to those whose onset is still inside", () => {
    assert.deepEqual(narrowedPicks(NOTES, [1, 2, 3, 4], { start: 480, end: 1440 }), [2, 3]);
    assert.deepEqual(narrowedPicks(NOTES, [2], { start: 0, end: 3840 }), [2]);
});

test("picked notes move by whole sixteenths and semitones, kept on the keys and after the start", () => {
    assert.deepEqual(moveSteps(170, 1.4), { ticks: 120, pitches: 1 });
    assert.deepEqual(moveSteps(-61, -0.6), { ticks: -120, pitches: -1 });
    assert.deepEqual(movedNote(NOTES[2], { ticks: 120, pitches: -2 }), { onset: 600, pitch: 72, length: 480 });
    assert.deepEqual(keptSteps(NOTES, [1, 2], { ticks: -240, pitches: 60 }), { ticks: 0, pitches: 53 });
    assert.deepEqual(keptSteps(NOTES, [], { ticks: 120, pitches: 1 }), { ticks: 0, pitches: 0 });
});

// The gesture over a roll 1 px a tick wide from x = 0, with the selection given; what it asks the host is recorded.
function roll(selection, shape = "box") {
    const asked = [];
    let clock = 0;
    const host = {
        selection: () => selection,
        notes: () => NOTES,
        noteAt: (point) => {
            const index = NOTES.findIndex((note) => note.pitch === Math.round(point.pitch) && point.tick >= note.onset && point.tick < note.end);
            return index < 0 ? undefined : index;
        },
        xOf: (tick) => tick / 4,
        lines: () => LINES,
        shape: () => shape,
        now: () => clock,
        pickNotes: (indices, change, gesture) => asked.push({ pickNotes: indices, change, gesture: typeof gesture }),
        pickSpan: (span, how) => asked.push({ pickSpan: span, how }),
        moveBegan: () => asked.push({ moveBegan: true }),
        moveNotes: (indices, steps) => asked.push({ moveNotes: indices, steps }),
        tap: (point, strip, inside) => asked.push({ tap: { strip, inside } }),
        fit: () => asked.push({ fit: true }),
    };
    const gesture = new RollPlaceGesture(host);
    const at = (tick, pitch, y = 100) => ({ x: tick / 4, y, tick, pitch });
    return {
        asked,
        gesture,
        drag(from, to, strip = false) {
            gesture.begin(1, from, strip);
            gesture.move(1, at((from.tick + to.tick) / 2, (from.pitch + to.pitch) / 2, (from.y + to.y) / 2));
            gesture.move(1, to);
            return gesture.end(1);
        },
        tap(point, strip = false, after = 0) {
            clock += after;
            gesture.begin(1, point, strip);
            return gesture.end(1);
        },
        at,
    };
}

test("a box drawn from outside the span picks notes anew, the old selection kept for Undo", () => {
    const r = roll({ start: 1920, end: 3840, lit: new Set(), picked: false });
    assert.equal(r.drag(r.at(100, 79), r.at(1300, 70)), true);
    assert.deepEqual(r.asked, [{ pickNotes: [1, 2, 3], change: "replace", gesture: "string" }]);
});

test("a box from inside the span adds to its notes; a box that takes nothing changes nothing", () => {
    const r = roll({ start: 0, end: 960, lit: new Set([1]), picked: true });
    r.drag(r.at(200, 79.4), r.at(1300, 73.6));
    assert.deepEqual(r.asked, [{ pickNotes: [1, 2, 3], change: "add", gesture: "string" }]);
    const empty = roll({ start: 2800, end: 3840, lit: new Set(), picked: false });
    empty.drag(empty.at(2000, 60), empty.at(2600, 62));
    assert.deepEqual(empty.asked, []);
});

test("while a box is drawn the roll previews its notes, their span and the old selection dashed", () => {
    const r = roll({ start: 1920, end: 3840, lit: new Set(), picked: false }, "box");
    r.gesture.begin(1, r.at(100, 79), false);
    assert.equal(r.gesture.preview(), null);
    r.gesture.move(1, r.at(700, 75));
    r.gesture.move(1, r.at(1300, 70));
    const preview = r.gesture.preview();
    assert.equal(preview.kind, "shape");
    assert.deepEqual(preview.picks, [1, 2, 3]);
    assert.deepEqual(preview.span, { start: 0, end: 1440 });
    assert.deepEqual(preview.old, { start: 1920, end: 3840 });
    assert.equal(r.gesture.drawing, true);
    r.gesture.cancel();
    assert.equal(r.gesture.active, false);
    assert.deepEqual(r.asked, []);
});

test("an edge taken anywhere down the roll moves that boundary; picked notes narrow to those still inside", () => {
    const plain = roll({ start: 0, end: 1920, lit: new Set([0, 1, 2, 3, 4]), picked: false });
    plain.drag(plain.at(1920, 60), plain.at(960, 60));
    assert.deepEqual(plain.asked, [{ pickSpan: { start: 0, end: 960 }, how: "edit" }]);
    const picked = roll({ start: 0, end: 1920, lit: new Set([1, 2, 3, 4]), picked: true });
    picked.drag(picked.at(1920, 60), picked.at(960, 60));
    assert.deepEqual(picked.asked, [{ pickNotes: [1, 2], change: "edit", gesture: "string" }]);
});

test("on the bar numbers a drag is a span without notes: new from outside, extended from inside", () => {
    const r = roll({ start: 0, end: 960, lit: new Set(), picked: false });
    r.drag(r.at(2000, 0, 10), r.at(3000, 0, 10), true);
    assert.deepEqual(r.asked, [{ pickSpan: { start: 1920, end: 3360 }, how: "replace" }]);
    const inside = roll({ start: 960, end: 1920, lit: new Set(), picked: false });
    inside.drag(inside.at(1400, 0, 10), inside.at(3000, 0, 10), true);
    assert.deepEqual(inside.asked, [{ pickSpan: { start: 960, end: 2880 }, how: "edit" }]);
    const none = roll(null);
    none.drag(none.at(2000, 0, 10), none.at(3000, 0, 10), true);
    assert.deepEqual(none.asked, [{ pickSpan: { start: 1920, end: 3360 }, how: "create" }]);
});

test("a tap on a note picks that note; on empty space it is the roll's tap; a double tap inside fits", () => {
    const r = roll({ start: 0, end: 1920, lit: new Set(), picked: false });
    r.tap(r.at(1000, 76));
    assert.deepEqual(r.asked.pop(), { pickNotes: [3], change: "replace", gesture: "string" });
    r.tap(r.at(3000, 60));
    assert.deepEqual(r.asked.pop(), { tap: { strip: false, inside: false } });
    r.tap(r.at(1000, 60));
    assert.deepEqual(r.asked.pop(), { tap: { strip: false, inside: true } });
    r.tap(r.at(1000, 60), false, 100);
    assert.deepEqual(r.asked.pop(), { fit: true });
    r.tap(r.at(3000, 0, 10), true);
    assert.deepEqual(r.asked.pop(), { tap: { strip: true, inside: false } });
    // On an edge a tap does nothing; with no selection a note tapped makes one.
    r.tap(r.at(1920, 60));
    assert.deepEqual(r.asked, []);
    const none = roll(null);
    none.tap(none.at(1000, 76));
    assert.deepEqual(none.asked, [{ pickNotes: [3], change: "create", gesture: "string" }]);
});

test("a drag of a picked note moves the picked notes in steps; the draft opens with the first step only", () => {
    const r = roll({ start: 0, end: 1920, lit: new Set([1, 2]), picked: true });
    r.drag(r.at(200, 72), r.at(330, 73.2));
    assert.deepEqual(r.asked, [{ moveBegan: true }, { moveNotes: [1, 2], steps: { ticks: 120, pitches: 1 } }]);
    const still = roll({ start: 0, end: 1920, lit: new Set([1, 2]), picked: true });
    still.drag(still.at(200, 72), still.at(230, 72.2));
    assert.deepEqual(still.asked, []);
    // Held where it was, it is a tap: that note alone.
    still.tap(still.at(200, 72));
    assert.deepEqual(still.asked, [{ pickNotes: [1], change: "replace", gesture: "string" }]);
    // A note of a plain span is not picked: a drag from it draws a box that adds.
    const plain = roll({ start: 0, end: 1920, lit: new Set([1, 2]), picked: false });
    plain.drag(plain.at(200, 72), plain.at(1300, 77.4));
    assert.deepEqual(plain.asked, [{ pickNotes: [1, 2, 3], change: "add", gesture: "string" }]);
});

test("the selected span's edges run down the whole roll, the start's knob on top and the end's at the bottom", async () => {
    const { drawPlaceEdges } = await import("../../src/MotiffLab.Web/wwwroot/js/roll/roll-place-draw.js");
    const rects = [];
    const discs = [];
    let arc = null;
    const context = {
        fillStyle: "",
        fillRect: (x, y, w, h) => rects.push({ x, y, w, h, colour: context.fillStyle }),
        beginPath: () => { arc = null; },
        arc: (x, y, radius) => { arc = { x, y, radius }; },
        fill: () => discs.push({ ...arc, colour: context.fillStyle }),
    };
    const frame = { xOf: (tick) => tick / 4, yOf: () => 0, row: 10, header: 24, left: 62, right: 800, height: 300 };
    drawPlaceEdges(context, frame, { start: 480, end: 1920 }, { accent: "cobalt", tint: "tint", surface: "white" });
    assert.deepEqual(rects.map((rect) => [rect.x, rect.y, rect.h, rect.colour]), [[119, 0, 300, "cobalt"], [479, 0, 300, "cobalt"]]);
    const knobs = discs.filter((disc) => disc.colour === "cobalt");
    assert.equal(knobs.length, 2);
    assert.ok(knobs[0].x === 120 && knobs[0].y < 24, "the start's knob on top");
    assert.ok(knobs[1].x === 480 && knobs[1].y > 280, "the end's knob at the bottom");
    rects.length = 0;
    drawPlaceEdges(context, frame, { start: 0, end: 4000 }, { accent: "cobalt", tint: "tint", surface: "white" });
    assert.deepEqual(rects, [], "edges out of view are not drawn");
});
