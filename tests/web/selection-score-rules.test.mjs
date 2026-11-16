// The score's selection rules of version 0.6.1 (#582 items 6 to 9, Scripts/score/selection-gesture.ts with `barHold`, and
// Scripts/score/menu-drag.ts): a hold on a bar selects that bar, replacing a selection there was, in one gesture; a move
// from inside extends the selection either way and never shrinks it; the edges go to the beat lines inside a bar; the menu
// dragged aside stays there until it closes.
import assert from "node:assert/strict";
import { test } from "node:test";

import { DRAG_DISTANCE, extendedRange, gestureChange, heldGesture, movedGesture, pressGesture } from "../../src/MotiffLab.Web/wwwroot/js/score/selection-gesture.js";
import { MenuPin, keptInside, movedSpot } from "../../src/MotiffLab.Web/wwwroot/js/score/menu-drag.js";

// 4/4 bars of 1920 ticks and their beat lines every 480, as music-core gives them.
const beatLines = Array.from({ length: 21 }, (_, i) => i * 480);
const selection = { start: 1920, end: 3840 };

test("with no selection a hold on a bar selects that bar, by the stylus and by a finger alike (#582 8)", () => {
    for (const rule of ["now", "longPress"]) {
        const press = pressGesture(null, 2500, false, rule, true);
        assert.equal(heldGesture(press), "bar", `${rule}: the hold alone selects`);
        assert.equal(gestureChange(press), "create", "let go there, the bar stays selected");
    }
    // Before 0.6.1 (and still on the roll) a stylus held still with no selection did nothing.
    assert.equal(heldGesture(pressGesture(null, 2500, false, "now")), "none");
});

test("a hold on another bar replaces the selection with that bar in the same gesture, never only clearing it (#582 8)", () => {
    const press = pressGesture(selection, 6000, false, "now", true);
    assert.equal(heldGesture(press), "bar");
    assert.equal(press.cleared, false, "the selection is never left cleared");
    assert.equal(gestureChange(press), "replace", "one step of the history, with «Вернуть»");
    assert.equal(movedGesture(press, 40), "none", "a move after the hold takes the new selection on, it starts nothing else");
    assert.equal(gestureChange(press), "replace");
    // Long presses one after another switch from bar to bar.
    const again = pressGesture({ start: 5760, end: 7680 }, 9000, false, "longPress", true);
    assert.equal(heldGesture(again), "bar");
    assert.equal(gestureChange(again), "replace");
});

test("a press inside the selection, moved or held, extends it and a hold there changes nothing by itself (#582 9)", () => {
    const stylus = pressGesture(selection, 2500, true, "now", true);
    assert.equal(movedGesture(stylus, DRAG_DISTANCE + 1), "extend");
    assert.equal(gestureChange(stylus), "edit");
    const finger = pressGesture(selection, 2500, true, "longPress", true);
    assert.equal(heldGesture(finger), "extend", "a finger holds first, then drags");
    // The roll keeps its rule: the nearer edge moves.
    assert.equal(movedGesture(pressGesture(selection, 2500, true, "now"), DRAG_DISTANCE + 1), "edit");
});

test("the extension follows the pointer both ways, on beat lines, and the selection it began with never shrinks (#582 9)", () => {
    assert.deepEqual(extendedRange(selection, 3000, beatLines), selection, "inside, nothing is added");
    assert.deepEqual(extendedRange(selection, 4400, beatLines), { start: 1920, end: 4320 }, "past the end, to the beat it covers half of");
    assert.deepEqual(extendedRange(selection, 4000, beatLines), { start: 1920, end: 3840 }, "under half a beat, not yet");
    assert.deepEqual(extendedRange(selection, 5000, beatLines), { start: 1920, end: 4800 }, "on, further");
    assert.deepEqual(extendedRange(selection, 3900, beatLines), selection, "back again: the extension goes, the selection stays whole");
    assert.deepEqual(extendedRange(selection, 1100, beatLines), { start: 960, end: 3840 }, "the other way: the start goes back");
    assert.deepEqual(extendedRange(selection, 0, beatLines), { start: 0, end: 3840 });
    // By bars the extension goes bar by bar.
    const barlines = [0, 1920, 3840, 5760, 7680];
    assert.deepEqual(extendedRange(selection, 4400, barlines), selection);
    assert.deepEqual(extendedRange(selection, 4900, barlines), { start: 1920, end: 5760 });
});

test("a selection by beats can start and end inside one bar (#582 6)", () => {
    const beat = { start: 2400, end: 2880 };
    assert.deepEqual(extendedRange(beat, 3200, beatLines), { start: 2400, end: 3360 }, "beats two to three of one bar");
    assert.deepEqual(extendedRange(beat, 2100, beatLines), { start: 1920, end: 2880 });
});

test("the menu dragged goes as far as the pointer, inside the window and clear of the drawers (#582 7)", () => {
    const size = { width: 300, height: 44 };
    const room = { width: 1180, height: 820 };
    assert.deepEqual(movedSpot({ left: 400, top: 200 }, 50, 300, size, room), { left: 450, top: 500 });
    assert.deepEqual(movedSpot({ left: 400, top: 200 }, 2000, 2000, size, room), { left: 1180 - 16 - 300, top: 820 - 16 - 44 }, "never off the window");
    assert.deepEqual(keptInside({ left: 0, top: 0 }, size, { ...room, left: 332, bottom: 500 }), { left: 348, top: 72 }, "below the bar, right of the Contents");
    assert.equal(keptInside({ left: 400, top: 700 }, size, { ...room, bottom: 500 }).top, 500 - 16 - 44, "above a drawer over the foot");
});

test("a short move on the menu is a tap on its button; a longer one drags it; closed, it forgets where it went (#582 7)", () => {
    const pin = new MenuPin();
    const size = { width: 300, height: 44 };
    const room = { width: 1180, height: 820 };
    pin.take(1, 500, 220, { left: 400, top: 200 });
    assert.equal(pin.move(1, 503, 222, size, room), null, "still a tap");
    assert.equal(pin.move(2, 600, 600, size, room), null, "another pointer's move is not this press's");
    assert.deepEqual(pin.move(1, 520, 520, size, room), { left: 420, top: 500 });
    assert.equal(pin.moving(), true);
    assert.equal(pin.release(1), true, "its click is no tap on a button");
    assert.deepEqual(pin.spot, { left: 420, top: 500 }, "it stays where it was let go");
    pin.take(1, 430, 510, pin.spot);
    assert.equal(pin.release(1), false, "a tap on the moved menu is a tap");
    pin.reset();
    assert.equal(pin.spot, null, "the next menu opens over its band");
});
