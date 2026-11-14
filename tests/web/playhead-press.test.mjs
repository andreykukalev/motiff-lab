// The long press that puts the black playhead where it is held (v0.5.7, #543 sections 3 and 5, the lead's decisions 1 and 2;
// Scripts/playback/playhead-press.ts): only the tool decides, in Navigate a finger or the pen; held still it takes the
// playhead to the place under it and the music stands (T14), moved on it scrubs (T15), lifted it plays on (T16); moved before
// the hold it lets the page scroll; a stale press never blocks the next. In Node on the compiled modules:
//   dotnet build src/MotiffLab.Web && node --test tests/web
import assert from "node:assert/strict";
import { test } from "node:test";

import { PlayheadDrag } from "../../src/MotiffLab.Web/wwwroot/js/playback/playhead-drag.js";
import { PlayheadPress, pressPlaces } from "../../src/MotiffLab.Web/wwwroot/js/playback/playhead-press.js";
import { HOLD_MS, HOLD_SLOP } from "../../src/MotiffLab.Web/wwwroot/js/score/selection-gesture.js";

/** Timers run by hand: `run()` fires every one due. */
function handTimers() {
    const due = new Map();
    let next = 0;
    return {
        set: (run, ms) => { next += 1; due.set(next, { run, ms }); return next; },
        clear: (id) => due.delete(id),
        run: () => { for (const [id, { run, ms }] of [...due]) { assert.equal(ms, HOLD_MS); due.delete(id); run(); } },
        pending: () => due.size,
    };
}

/** A press over a line of music where x / 2 is the tick, the grid left to the host; off the music below y = 100. */
function setUp() {
    const sent = [];
    const timers = handTimers();
    const drag = new PlayheadDrag((message) => sent.push(message), (run) => run());
    let taken = null;
    let moves = 0;
    const press = new PlayheadPress({
        tickAt: (point, first) => (first && point.y > 100 ? undefined : Math.round(point.x / 2)),
        took: (pointer) => { taken = pointer; },
        moved: () => { moves += 1; },
    }, drag, timers);
    return { press, drag, sent, timers, taken: () => taken, moves: () => moves };
}

test("the tool decides what a long press means: in Navigate a finger or the pen puts the playhead, never the mouse", () => {
    assert.equal(pressPlaces("touch", undefined, "navigate"), true);
    assert.equal(pressPlaces("pen", undefined, "navigate"), true);
    assert.equal(pressPlaces("mouse", undefined, "navigate"), false, "the mouse drags the page and its click places the playhead");
    assert.equal(pressPlaces("pen", undefined, "select"), false, "in Select a long press selects");
    assert.equal(pressPlaces("touch", undefined, "pen"), false);
    assert.equal(pressPlaces("touch", undefined, null), false, "before the modes nothing changes");
    assert.equal(pressPlaces("touch", true, "pen"), true, "the iPad's rule wins: with the Pen a finger that does not write");
    assert.equal(pressPlaces("pen", false, "navigate"), false);
});

test("held still, the press takes the black playhead to its place at once; moved on it scrubs; lifted it lets go", () => {
    const { press, sent, timers, taken, moves } = setUp();
    assert.equal(press.down(7, { x: 1000, y: 40 }), true);
    assert.equal(press.pending, true);
    assert.deepEqual(sent, [], "nothing before the hold");
    timers.run();
    assert.equal(press.holding, true);
    assert.equal(taken(), 7, "the view keeps the pointer");
    assert.deepEqual(sent, [{ phase: "grab", handle: "black" }, { phase: "move", handle: "black", tick: 500 }], "the music stands (T14), the place is the one pressed");
    assert.equal(press.move(7, { x: 3000, y: 400 }), true, "after the hold the place follows anywhere, another system too");
    assert.deepEqual(sent.at(-1), { phase: "move", handle: "black", tick: 1500 });
    assert.equal(press.up(7), true, "its release is no tap");
    assert.deepEqual(sent.at(-1), { phase: "release", handle: "black" }, "let go it plays on from there when it played (T16)");
    assert.equal(press.holding, false);
    assert.ok(moves() >= 3);
});

test("moved before the hold, the press lets the page scroll; lifted before it, it does nothing", () => {
    const { press, sent, timers } = setUp();
    press.down(1, { x: 100, y: 40 });
    assert.equal(press.move(1, { x: 100 + HOLD_SLOP / 2, y: 40 }), false);
    assert.equal(press.pending, true, "a wobble keeps it");
    press.move(1, { x: 100 + HOLD_SLOP + 1, y: 40 });
    assert.equal(press.pending, false);
    assert.equal(timers.pending(), 0);
    press.down(2, { x: 100, y: 40 });
    assert.equal(press.up(2), false);
    timers.run();
    assert.deepEqual(sent, [], "a tap is the tap's");
});

test("a long press off the music takes nothing, and nothing stays held", () => {
    const { press, sent, timers } = setUp();
    press.down(1, { x: 100, y: 300 });
    timers.run();
    assert.equal(press.holding, false);
    assert.equal(press.pending, false);
    assert.deepEqual(sent, []);
});

test("a new primary press ends a stale one whose end was lost, so it never blocks the next (#543 9)", () => {
    const { press, sent, timers, drag } = setUp();
    press.down(1, { x: 100, y: 40 });
    timers.run();
    assert.equal(press.holding, true);
    // The pen's pointerup never came: the next press of the pen ends the old one first, letting its playhead go.
    assert.equal(press.down(2, { x: 400, y: 40 }), true);
    assert.deepEqual(sent.at(-1), { phase: "release", handle: "black" });
    timers.run();
    assert.equal(drag.handle, "black");
    assert.deepEqual(sent.at(-1), { phase: "move", handle: "black", tick: 200 });
    assert.equal(press.down(3, { x: 600, y: 40 }, false), false, "a second finger beside the press is not a new one");
    assert.equal(press.end(), true);
    assert.equal(drag.handle, null);
});

test("a playhead held by its handle already is not taken again by a press", () => {
    const { press, drag, timers } = setUp();
    assert.equal(drag.grab("blue", 9), true);
    press.down(1, { x: 100, y: 40 });
    timers.run();
    assert.equal(press.holding, false, "one playhead held at a time");
    assert.equal(drag.handle, "blue");
});
