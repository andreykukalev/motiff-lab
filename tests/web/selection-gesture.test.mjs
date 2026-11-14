// The one selection gesture of the score and the roll (#511 sections 2 and 6.3): Scripts/score/selection-gesture.ts and
// Scripts/roll/roll-strip-gesture.ts. Acceptance 13.1: inside and move edits, outside and move leaves the selection, a
// hold outside clears it, a move after that selects anew; the Pen keeps the stylus (selection-layer.test.mjs).
import assert from "node:assert/strict";
import { mock, test } from "node:test";

import {
    HOLD_MS,
    editedRange,
    gestureChange,
    heldGesture,
    movedGesture,
    nearerEdge,
    nextGestureId,
    pressGesture,
} from "../../src/MotiffLab.Web/wwwroot/js/score/selection-gesture.js";

globalThis.window ??= globalThis;
const { RollStripGesture } = await import("../../src/MotiffLab.Web/wwwroot/js/roll/roll-strip-gesture.js");

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

function stripHost(range) {
    const calls = [];
    return {
        calls,
        selection: () => range,
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

test("under the modes, in Navigate, the strips wait for the hold as the score does; a move before it moves the roll", () => {
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
