// The one selection gesture of the score and the roll (#511 sections 2 and 6.3): Scripts/score/selection-gesture.ts and
// Scripts/roll/roll-strip-gesture.ts. Acceptance 13.1: inside and move edits, outside and move leaves the selection, a
// hold outside clears it, a move after that selects anew; the Pen keeps the stylus (selection-layer.test.mjs).
import assert from "node:assert/strict";
import { mock, test } from "node:test";

import {
    HOLD_MS,
    editedRange,
    handlesShown,
    staleGesture,
    gestureChange,
    heldGesture,
    movedGesture,
    nearerEdge,
    nextGestureId,
    pressGesture,
    snapEdge,
} from "../../src/MotiffLab.Web/wwwroot/js/score/selection-gesture.js";

globalThis.window ??= globalThis;
const { RollStripGesture, keptDrag, rangeBars } = await import("../../src/MotiffLab.Web/wwwroot/js/roll/roll-strip-gesture.js");
const { holdKeepsSelection } = await import("../../src/MotiffLab.Web/wwwroot/js/roll/roll-modes.js");

const selection = { start: 1920, end: 5760 };
const bars = [0, 1920, 3840, 5760, 7680, 9600];

test("a press inside that moves edits the nearer edge, never making a new selection (scenario A)", () => {
    assert.equal(nearerEdge(selection, 2000), "start");
    assert.equal(nearerEdge(selection, 5000), "end");
    const press = pressGesture(selection, 5000, true, "now");
    assert.equal(movedGesture(press, 3), "none", "a few pixels are not yet a move");
    assert.equal(movedGesture(press, 10), "edit");
    assert.equal(gestureChange(press), "edit");
    assert.deepEqual(editedRange(selection, "end", 5000, 7000, bars), { start: 1920, end: 7680 }, "the edge moves as far as the pointer, to the nearest barline");
    assert.deepEqual(editedRange(selection, "start", 2000, 9000, bars), { start: 3840, end: 5760 }, "never onto or past the other edge");
});

test("a press outside that moves before the hold leaves the selection; a finger scrolls, a stylus is told to hold (scenario C)", () => {
    const stylus = pressGesture(selection, 8000, false, "now");
    assert.equal(movedGesture(stylus, 20), "hint");
    assert.equal(gestureChange(stylus), "none");
    assert.equal(heldGesture(stylus), "none", "the hold after a move does nothing");
    const finger = pressGesture(selection, 8000, false, "longPress");
    assert.equal(movedGesture(finger, 20), "pass");
    assert.equal(gestureChange(finger), "none");
});

test("a hold outside clears the selection, and a move after it selects anew as one replace (scenario B)", () => {
    const press = pressGesture(selection, 8000, false, "now");
    assert.equal(heldGesture(press), "clear");
    assert.equal(gestureChange(press), "clear", "let go there, the selection stays cleared");
    assert.equal(movedGesture(press, 20), "create");
    assert.equal(gestureChange(press), "replace");
});

test("without a selection a press creates as before: at once for a mouse or a stylus, after the hold for a finger", () => {
    const now = pressGesture(null, 100, false, "now");
    assert.equal(movedGesture(now, 10), "create");
    assert.equal(gestureChange(now), "create");
    const finger = pressGesture(null, 100, false, "longPress");
    assert.equal(heldGesture(finger), "create");
    assert.equal(gestureChange(finger), "create");
    assert.equal(gestureChange(pressGesture(null, 100, false, "now")), "none", "a press let go at once changes nothing");
});

test("every gesture has its own name, so the owner keeps each as one step", () => {
    assert.notEqual(nextGestureId(), nextGestureId());
});

function stripHost(range, playing = false) {
    const calls = [];
    return {
        calls,
        selection: () => range,
        keepsSelection: () => playing,
        beginRange: (pointer, x, tick, how, gesture) => calls.push(["range", tick, how, gesture]),
        beginEdge: (pointer, anchor, offset, gesture) => calls.push(["edge", anchor, offset, gesture]),
        beginPan: () => calls.push(["pan"]),
        clear: (gesture) => calls.push(["clear", gesture]),
        cancelled: (gesture) => calls.push(["cancelled", gesture]),
        hint: () => calls.push(["hint"]),
        tap: (tick, y, inside) => calls.push(["tap", tick, inside]),
    };
}

const press = (pointerType) => ({ pointerId: 1, pointerType });

test("the roll's strips follow the same gesture in the drawer and in Expand (13.1)", () => {
    mock.timers.enable({ apis: ["setTimeout"] });
    try {
        const inside = stripHost(selection);
        const editing = new RollStripGesture(inside);
        editing.begin(press("touch"), 100, 10, 5000);
        assert.equal(editing.move(1, 130, 10), false, "an edit is the roll's drag from here");
        assert.deepEqual(inside.calls.map((call) => call[0]), ["edge"]);
        assert.equal(inside.calls[0][1], 1920, "the start stays, the end moves");
        assert.equal(inside.calls[0][2], 760, "the end edge moves by the pointer, from where it is");

        const outside = stripHost(selection);
        const holding = new RollStripGesture(outside);
        holding.begin(press("mouse"), 400, 10, 8000);
        mock.timers.tick(HOLD_MS);
        assert.deepEqual(outside.calls.map((call) => call[0]), ["clear"]);
        holding.move(1, 430, 10);
        assert.deepEqual(outside.calls[1].slice(0, 3), ["range", 8000, "replace"]);
        assert.equal(outside.calls[1][3], outside.calls[0][1], "the clear and the new selection are one gesture");

        const moving = stripHost(selection);
        const passing = new RollStripGesture(moving);
        passing.begin(press("touch"), 400, 10, 8000);
        passing.move(1, 440, 10);
        mock.timers.tick(HOLD_MS);
        assert.deepEqual(moving.calls.map((call) => call[0]), ["pan"], "a finger moves the roll; the selection stays");

        const stylus = stripHost(selection);
        const hinted = new RollStripGesture(stylus);
        hinted.begin(press("pen"), 400, 10, 8000);
        hinted.move(1, 440, 10);
        assert.deepEqual(stylus.calls.map((call) => call[0]), ["hint"]);

        const cancelled = stripHost(selection);
        const second = new RollStripGesture(cancelled);
        second.begin(press("touch"), 400, 10, 8000);
        mock.timers.tick(HOLD_MS);
        second.cancel();
        assert.deepEqual(cancelled.calls.map((call) => call[0]), ["clear", "cancelled"], "a cancelled hold gives the selection back");

        const none = stripHost(null);
        new RollStripGesture(none).begin(press("touch"), 400, 10, 8000);
        assert.deepEqual(none.calls[0].slice(0, 3), ["range", 8000, "create"], "without a selection a press selects at once, as before");

        const tapped = stripHost(selection);
        const tap = new RollStripGesture(tapped);
        tap.begin(press("touch"), 100, 10, 3000);
        assert.equal(tap.end(1), true);
        assert.deepEqual(tapped.calls, [["tap", 3000, true]], "a tap leaves the selection to what a tap does");
    } finally {
        mock.timers.reset();
    }
});

const { undoFor } = await import("../../src/MotiffLab.Web/wwwroot/js/layout/mode-keys.js");

test("⌘Z undoes the selection and ⇧⌘Z or Ctrl+Y redoes it, outside a field (#511 11)", () => {
    const key = (code, extra = {}) => ({ code, key: "", altKey: false, ctrlKey: false, metaKey: false, shiftKey: false, defaultPrevented: false, target: null, ...extra });
    assert.equal(undoFor(key("KeyZ", { metaKey: true })), "undo");
    assert.equal(undoFor(key("KeyZ", { metaKey: true, shiftKey: true })), "redo");
    assert.equal(undoFor(key("KeyZ", { ctrlKey: true })), "undo");
    assert.equal(undoFor(key("KeyY", { ctrlKey: true })), "redo");
    assert.equal(undoFor(key("KeyZ")), null, "Z alone is not Undo");
    const field = { closest: () => ({}) };
    assert.equal(undoFor(key("KeyZ", { metaKey: true, target: field })), null, "a field keeps its own Undo");
});

test("with the long-press rule the strips wait for the hold as the score does; a move before it moves the roll", () => {
    mock.timers.enable({ apis: ["setTimeout"] });
    try {
        const none = stripHost(null);
        const creating = new RollStripGesture(none);
        creating.begin(press("mouse"), 400, 10, 8000, "longPress");
        assert.deepEqual(none.calls, [], "no selection at once");
        mock.timers.tick(HOLD_MS);
        assert.deepEqual(none.calls[0].slice(0, 3), ["range", 8000, "create"]);

        const moved = stripHost(selection);
        const panning = new RollStripGesture(moved);
        panning.begin(press("mouse"), 100, 10, 5000, "longPress");
        panning.move(1, 140, 10);
        assert.deepEqual(moved.calls.map((call) => call[0]), ["pan"], "inside too, a move before the hold moves the roll");

        const held = stripHost(selection);
        const editing = new RollStripGesture(held);
        editing.begin(press("touch"), 100, 10, 5000, "longPress");
        mock.timers.tick(HOLD_MS);
        assert.deepEqual(held.calls.map((call) => call[0]), ["edge"], "held inside, the edge follows");
    } finally {
        mock.timers.reset();
    }
});

test("an edge snaps by the share of the bar it covers: under half back, half or more on (#521 3)", () => {
    // Bars of 1920 ticks: the end edge 40 % into the next bar goes back, 60 % goes on, exactly 50 % goes on.
    assert.equal(snapEdge(bars, "end", 5760 + 768), 5760);
    assert.equal(snapEdge(bars, "end", 5760 + 1152), 7680);
    assert.equal(snapEdge(bars, "end", 5760 + 960), 7680, "at exactly half the end takes the bar");
    // The start edge covers a bar from its end: 40 % of it covered stays out, 60 % takes it, exactly half takes it.
    assert.equal(snapEdge(bars, "start", 1920 - 768), 1920);
    assert.equal(snapEdge(bars, "start", 1920 - 1152), 0);
    assert.equal(snapEdge(bars, "start", 1920 - 960), 0, "at exactly half the start takes the bar");
    assert.equal(snapEdge([], "end", 100), undefined);
    assert.deepEqual(editedRange(selection, "end", 5000, 5000 + 960, bars), { start: 1920, end: 7680 }, "the score's edges follow the same rule");
    assert.deepEqual(editedRange(selection, "start", 2000, 2000 - 960, bars), { start: 0, end: 5760 });
});

test("the iPad's roll by bars: a tap its bar, a new range every bar it touches, a moved edge by the share covered (#521 3)", () => {
    const numbered = [0, 1, 2, 3, 4].map((index) => ({ onset: index * 1920, end: (index + 1) * 1920, number: index + 1 }));
    assert.deepEqual(rangeBars(numbered, 2000, 2000, true, false), { first: 2, last: 2 });
    assert.deepEqual(rangeBars(numbered, 2000, 5800, false, false), { first: 2, last: 4 }, "a new range takes every bar it touches, as before");
    // The selection is bars 2–3 (1920–5760); its end edge dragged a little into bar 4 stays, past half of it takes it.
    assert.deepEqual(rangeBars(numbered, 1920, 5760 + 200 - 1, false, true), { first: 2, last: 3 });
    assert.deepEqual(rangeBars(numbered, 1920, 5760 + 1200 - 1, false, true), { first: 2, last: 4 });
    assert.deepEqual(rangeBars(numbered, 1920, 5760 + 960 - 1, false, true), { first: 2, last: 4 }, "exactly half takes the bar");
    // Its start edge (the roll sends the end's last tick as the anchor) a little into bar 1 stays, past half takes it.
    assert.deepEqual(rangeBars(numbered, 5759, 1920 - 300, false, true), { first: 2, last: 3 });
    assert.deepEqual(rangeBars(numbered, 5759, 1920 - 1500, false, true), { first: 1, last: 3 });
    assert.deepEqual(rangeBars(numbered, 5759, 5700, false, true), { first: 3, last: 3 }, "edges that meet leave one bar");
    assert.equal(rangeBars([], 0, 10, false, true), undefined);
});

test("while the music plays a hold on the roll's strips never clears the selection, and letting go does nothing (#521 16.4)", () => {
    mock.timers.enable({ apis: ["setTimeout"] });
    try {
        const finger = stripHost(selection, true);
        const held = new RollStripGesture(finger);
        held.begin(press("touch"), 400, 10, 8000);
        mock.timers.tick(HOLD_MS);
        assert.deepEqual(finger.calls.map((call) => call[0]), ["pan"], "the finger moves the roll instead");
        assert.equal(held.end(1), false, "the press is the roll's from the hold on: no tap, no clear");
        assert.deepEqual(finger.calls.map((call) => call[0]), ["pan"]);

        const mouse = stripHost(selection, true);
        const kept = new RollStripGesture(mouse);
        kept.begin(press("mouse"), 400, 10, 8000);
        mock.timers.tick(HOLD_MS);
        kept.end(1);
        assert.deepEqual(mouse.calls, [], "nothing changes");
        assert.equal(heldGesture(pressGesture(selection, 8000, false, "now"), true), "pass");
        assert.equal(heldGesture(pressGesture(selection, 5000, true, "longPress"), true), "edit", "held inside, the edge still follows");
    } finally {
        mock.timers.reset();
    }
});

test("#543 9: in Select, playing, a hold outside the selection on the roll's strips replaces it; before, it did nothing", () => {
    mock.timers.enable({ apis: ["setTimeout"] });
    try {
        // The roll's host before 0.5.7 kept the selection on a hold whenever the music played, whatever the tool: the stylus
        // held over another bar in Select, playing, got no clear, no hint and no new selection.
        const before = stripHost(selection, true);
        const blocked = new RollStripGesture(before);
        blocked.begin(press("pen"), 400, 10, 8000);
        mock.timers.tick(HOLD_MS);
        blocked.move(1, 460, 10);
        assert.equal(blocked.end(1), false);
        assert.deepEqual(before.calls, [], "nothing happened: the old selection stayed and the press seemed blocked");

        // Now the host asks holdKeepsSelection: under Select the hold clears and the drag after it replaces, playing or not.
        for (const playing of [true, false]) {
            const host = stripHost(selection);
            host.keepsSelection = () => holdKeepsSelection("select", playing);
            const calls = host.calls;
            const replacing = new RollStripGesture(host);
            replacing.begin(press("pen"), 400, 10, 8000);
            mock.timers.tick(HOLD_MS);
            replacing.move(1, 460, 10);
            assert.deepEqual(calls.map((call) => call.slice(0, 3)), [["clear", calls[0][1]], ["range", 8000, "replace"]], `playing ${playing}`);
            assert.equal(calls[1][3], calls[0][1], "one gesture");
            // A second new selection right after works as the first did.
            replacing.end(1);
            replacing.begin(press("pen"), 700, 10, 9000);
            mock.timers.tick(HOLD_MS);
            replacing.move(1, 760, 10);
            assert.deepEqual(calls.slice(2).map((call) => call.slice(0, 3)), [["clear", calls[2][1]], ["range", 9000, "replace"]]);
        }
    } finally {
        mock.timers.reset();
    }
});

test("#543 9: the page's answer to a hold's clear never drops the new range the press drags; an edge goes with its selection", () => {
    const range = { pointer: 1, anchor: 8000, current: 8400, edge: false, how: "replace" };
    assert.equal(keptDrag(range), range, "the new range drags on to its end and is sent");
    assert.equal(keptDrag({ pointer: 1, anchor: 1920, current: 6000, edge: true, how: "edit" }), null, "an edge anchored on the old selection goes");
    assert.equal(keptDrag(null), null);
});

test("#543 9: a cancelled press, after its hold took the selection away, gives it back and leaves the next press free", () => {
    mock.timers.enable({ apis: ["setTimeout"] });
    try {
        const host = stripHost(selection);
        const strip = new RollStripGesture(host);
        strip.begin(press("pen"), 400, 10, 8000);
        mock.timers.tick(HOLD_MS);
        strip.cancel();
        strip.cancel();
        assert.deepEqual(host.calls.map((call) => call[0]), ["clear", "cancelled"], "given back once");
        assert.equal(strip.move(1, 500, 10), false, "the cancelled press is gone");
        assert.equal(strip.end(1), false);
        strip.begin(press("pen"), 400, 10, 8000);
        mock.timers.tick(HOLD_MS);
        strip.move(1, 460, 10);
        assert.deepEqual(host.calls.slice(2).map((call) => call.slice(0, 3)), [["clear", host.calls[2][1]], ["range", 8000, "replace"]]);
    } finally {
        mock.timers.reset();
    }
});

test("under the modes only Select shows the selection's handles; before them they always show (#543 4.2)", () => {
    assert.equal(handlesShown("select"), true);
    for (const mode of ["navigate", "edit", "pen"]) {
        assert.equal(handlesShown(mode), false, mode);
    }
    assert.equal(handlesShown(null), true);
    assert.equal(handlesShown(undefined), true);
});

test("a primary press of the same pointer type ends a gesture whose end was lost; another kind of pointer does not (#543 9)", () => {
    assert.equal(staleGesture({ pointerType: "pen" }, { pointerType: "pen", isPrimary: true }), true, "one pen: its new press means the old one lifted");
    assert.equal(staleGesture({ pointerType: "touch" }, { pointerType: "touch", isPrimary: true }), true, "a first finger with none down");
    assert.equal(staleGesture({ pointerType: "touch" }, { pointerType: "touch", isPrimary: false }), false, "a second finger beside the first");
    assert.equal(staleGesture({ pointerType: "pen" }, { pointerType: "touch", isPrimary: true }), false, "a finger beside the pen");
    assert.equal(staleGesture({ pointerType: "mouse" }, { pointerType: "mouse", isPrimary: true }), true);
});
