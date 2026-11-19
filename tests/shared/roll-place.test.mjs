// Tests for selection by where you touch on the roll (#601, once the experiment experimental.placeSelection): the box and the lasso, the span
// of picked notes, narrowing at an edge, moves of picked notes, and the press read by its place (roll-place-gesture.ts).
// Run in Node on the compiled modules:   dotnet build src/shared && node --test tests/shared

import assert from "node:assert/strict";
import { test } from "node:test";

import { addedPicks, insidePath, keptSteps, moveSteps, movedNote, narrowedPicks, notesInShape, pickedSpan } from "../../src/shared/wwwroot/js/roll/roll-place.js";
import { RollPlaceGesture } from "../../src/shared/wwwroot/js/roll/roll-place-gesture.js";

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

test("a rectangle drawn from outside is the span its sides cover, notes or none (v0.6.4, spec 4)", () => {
    const r = roll({ start: 1920, end: 3840, lit: new Set(), picked: false });
    assert.equal(r.drag(r.at(100, 79), r.at(1300, 70)), true);
    assert.deepEqual(r.asked, [{ pickSpan: { start: 0, end: 1440 }, how: "replace" }]);
    // Above every note, and over no note at all: the same span of time, never nothing.
    const high = roll({ start: 1920, end: 3840, lit: new Set(), picked: false });
    high.drag(high.at(100, 100), high.at(1300, 96));
    assert.deepEqual(high.asked, r.asked);
    const empty = roll(null);
    empty.drag(empty.at(2000, 40), empty.at(2600, 42));
    assert.deepEqual(empty.asked, [{ pickSpan: { start: 1920, end: 2880 }, how: "create" }]);
});

test("a drag from inside the span extends it, whatever notes it covers", () => {
    const r = roll({ start: 0, end: 960, lit: new Set([1]), picked: true });
    r.drag(r.at(200, 79.4), r.at(1300, 73.6));
    assert.deepEqual(r.asked, [{ pickSpan: { start: 0, end: 1440 }, how: "edit" }]);
    const empty = roll({ start: 2800, end: 3840, lit: new Set(), picked: false });
    empty.drag(empty.at(3000, 60), empty.at(2400, 62));
    assert.deepEqual(empty.asked, [{ pickSpan: { start: 2400, end: 3840 }, how: "edit" }]);
});

test("the lasso still rings notes, and a span when it rings none", () => {
    const r = roll({ start: 1920, end: 3840, lit: new Set(), picked: false }, "lasso");
    r.gesture.begin(1, r.at(100, 79), false);
    for (const [tick, pitch] of [[1300, 79], [1300, 70], [100, 70], [100, 79]]) {
        r.gesture.move(1, r.at(tick, pitch));
    }
    r.gesture.end(1);
    assert.deepEqual(r.asked, [{ pickNotes: [1, 2, 3], change: "replace", gesture: "string" }]);
    const none = roll(null, "lasso");
    none.drag(none.at(2000, 40), none.at(2600, 42));
    assert.deepEqual(none.asked, [{ pickSpan: { start: 1920, end: 2880 }, how: "create" }]);
});

test("while a rectangle is drawn the roll previews its span, the rectangle and the old selection dashed", () => {
    const r = roll({ start: 1920, end: 3840, lit: new Set(), picked: false }, "box");
    r.gesture.begin(1, r.at(100, 79), false);
    assert.equal(r.gesture.preview(), null);
    r.gesture.move(1, r.at(700, 75));
    r.gesture.move(1, r.at(1300, 70));
    const preview = r.gesture.preview();
    assert.equal(preview.kind, "span");
    assert.deepEqual(preview.span, { start: 0, end: 1440 });
    assert.deepEqual(preview.old, { start: 1920, end: 3840 });
    assert.deepEqual(preview.trace.map((point) => point.tick), [100, 1300]);
    assert.equal(preview.edge, null);
    assert.equal(r.gesture.drawing, true);
    r.gesture.cancel();
    assert.equal(r.gesture.active, false);
    assert.deepEqual(r.asked, []);
});

test("an edge taken anywhere down the roll moves that boundary alone, both ways, picked or not", () => {
    const plain = roll({ start: 0, end: 1920, lit: new Set([0, 1, 2, 3, 4]), picked: false });
    plain.drag(plain.at(1920, 60), plain.at(960, 60));
    assert.deepEqual(plain.asked, [{ pickSpan: { start: 0, end: 960 }, how: "edit" }]);
    const out = roll({ start: 0, end: 1920, lit: new Set(), picked: false });
    out.drag(out.at(1920, 30), out.at(2880, 30));
    assert.deepEqual(out.asked, [{ pickSpan: { start: 0, end: 2880 }, how: "edit" }], "out over no notes, it stays out");
    const left = roll({ start: 960, end: 1920, lit: new Set(), picked: false });
    left.drag(left.at(960, 60), left.at(480, 60));
    assert.deepEqual(left.asked, [{ pickSpan: { start: 480, end: 1920 }, how: "edit" }]);
    // A selection of picked notes: the edge stays where it is let go (no snap back to the notes' beats).
    const picked = roll({ start: 0, end: 1920, lit: new Set([1, 2, 3, 4]), picked: true });
    picked.drag(picked.at(1920, 60), picked.at(2880, 60));
    assert.deepEqual(picked.asked, [{ pickSpan: { start: 0, end: 2880 }, how: "edit" }]);
    // While it moves, the edge taken shows taken.
    const live = roll({ start: 0, end: 1920, lit: new Set(), picked: false });
    live.gesture.begin(1, live.at(1920, 60), false);
    live.gesture.move(1, live.at(2400, 60));
    assert.equal(live.gesture.preview().edge, "end");
    assert.deepEqual(live.gesture.preview().span, { start: 0, end: 2400 });
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
    // A note of a plain span is not picked: a drag from it extends the span.
    const plain = roll({ start: 0, end: 1920, lit: new Set([1, 2]), picked: false });
    plain.drag(plain.at(200, 72), plain.at(2300, 77.4));
    assert.deepEqual(plain.asked, [{ pickSpan: { start: 0, end: 2400 }, how: "edit" }]);
});

test("the selected span's handles are the kit's hollow circles at the middle of its edges, the one taken filled", async () => {
    const { drawPlaceEdges, HANDLE } = await import("../../src/shared/wwwroot/js/roll/roll-place-draw.js");
    const discs = [];
    let arc = null;
    const context = {
        fillStyle: "",
        strokeStyle: "",
        lineWidth: 0,
        save: () => {},
        restore: () => {},
        beginPath: () => { arc = null; },
        arc: (x, y, radius) => { arc = { x, y, radius }; },
        fill: () => { arc.fill = context.fillStyle; },
        stroke: () => discs.push({ ...arc, stroke: context.strokeStyle, ring: context.lineWidth }),
        fillRect: () => assert.fail("no rule and no corner knob"),
    };
    const frame = { xOf: (tick) => tick / 4, yOf: () => 0, row: 10, header: 24, left: 62, right: 800, height: 300 };
    drawPlaceEdges(context, frame, { start: 480, end: 1920 }, { accent: "cobalt", tint: "tint", surface: "white" });
    assert.deepEqual(discs.map((disc) => [disc.x, disc.y, disc.fill, disc.stroke, disc.ring]), [[120, 162, "white", "cobalt", 2], [480, 162, "white", "cobalt", 2]], "hollow, at the middle of the notes");
    assert.equal(discs[0].radius * 2 + HANDLE.ring, HANDLE.diameter, "14 px across with its ring, as ds.css .handle");
    discs.length = 0;
    drawPlaceEdges(context, frame, { start: 480, end: 1920 }, { accent: "cobalt", tint: "tint", surface: "white" }, "end");
    assert.deepEqual(discs.map((disc) => disc.fill), ["white", "cobalt"], "the end taken is filled");
    discs.length = 0;
    drawPlaceEdges(context, frame, { start: 0, end: 4000 }, { accent: "cobalt", tint: "tint", surface: "white" });
    assert.deepEqual(discs, [], "edges out of view are not drawn");
});

test("a press takes an edge only on its handle, where it is drawn; elsewhere down the edge a playhead is taken first (0.8 finding 1)", async () => {
    const { onPlaceHandle } = await import("../../src/shared/wwwroot/js/roll/roll-place-draw.js");
    const frame = { xOf: (tick) => tick / 4, header: 24, height: 300 };
    const span = { start: 480, end: 1920 };
    assert.equal(onPlaceHandle(frame, span, 120, 162), true, "the start's handle");
    assert.equal(onPlaceHandle(frame, span, 485, 170), true, "the end's, within its reach");
    assert.equal(onPlaceHandle(frame, span, 120, 12), false, "on the bar numbers, where the playhead's knob is");
    assert.equal(onPlaceHandle(frame, span, 120, 280), false, "low down the edge");
});
