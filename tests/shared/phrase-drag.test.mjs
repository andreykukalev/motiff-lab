// Tests for the hold and the drag of a phrase's mark (Scripts/score/phrase-drag.ts; the phrases spec, «Polish of option A» 2
// and «Player's boundaries» 3, v1.2 i-3a): a tap stays a tap, held still the mark lifts and let go opens its menu, held and
// moved it drags and let go drops, moved before the hold it lets the page scroll; the note it snaps to, the two phrases it
// would make, and its caption. In Node on the compiled modules:
//   dotnet build src/shared && node --test tests/shared
import assert from "node:assert/strict";
import { test } from "node:test";

import { MarkPress, barBeatCaption, nearestOnset, neighbourSpans } from "../../src/shared/wwwroot/js/score/phrase-drag.js";
import { HOLD_MS, HOLD_SLOP } from "../../src/shared/wwwroot/js/score/selection-gesture.js";

/** Timers run by hand: `run()` fires every one due. */
function handTimers() {
    const due = new Map();
    let next = 0;
    return {
        set: (run, ms) => { next += 1; due.set(next, { run, ms }); return next; },
        clear: (id) => due.delete(id),
        run: () => { for (const [id, { run, ms }] of [...due]) { assert.equal(ms, HOLD_MS); due.delete(id); run(); } },
    };
}

function setUp() {
    const calls = [];
    const timers = handTimers();
    const press = new MarkPress({
        lifted: () => calls.push("lifted"),
        dragged: (point) => calls.push(["dragged", point.x]),
        menu: () => calls.push("menu"),
        dropped: (point) => calls.push(["dropped", point === null ? null : point.x]),
    }, timers);
    return { press, timers, calls };
}

test("a press let go before the hold is a tap: nothing lifts and its click is the mark's", () => {
    const { press, calls } = setUp();
    press.down(1, { x: 10, y: 10 });
    press.up(1, { x: 10, y: 10 });
    assert.deepEqual(calls, []);
    assert.equal(press.swallowsClick(), false, "the tap selects the phrase");
});

test("moved before the hold, the press lets go so the page scrolls: no hold follows", () => {
    const { press, timers, calls } = setUp();
    press.down(1, { x: 10, y: 10 });
    press.move(1, { x: 10, y: 10 + HOLD_SLOP + 1 });
    timers.run();
    press.up(1, { x: 10, y: 40 });
    assert.deepEqual(calls, []);
    assert.equal(press.holding, false);
});

test("held still and let go, the mark lifts and its menu opens; the click after is no tap", () => {
    const { press, timers, calls } = setUp();
    press.down(1, { x: 10, y: 10 });
    timers.run();
    assert.equal(press.holding, true, "the page stays still under a held mark");
    press.move(1, { x: 10 + HOLD_SLOP, y: 10 });
    press.up(1, { x: 10 + HOLD_SLOP, y: 10 });
    assert.deepEqual(calls, ["lifted", "menu"]);
    assert.equal(press.swallowsClick(), true);
    assert.equal(press.swallowsClick(), false, "only the one click");
});

test("held and moved, the mark drags and let go drops where the pointer is; no menu", () => {
    const { press, timers, calls } = setUp();
    press.down(1, { x: 10, y: 10 });
    timers.run();
    press.move(1, { x: 30, y: 10 });
    press.move(1, { x: 31, y: 10 });
    press.up(1, { x: 32, y: 10 });
    assert.deepEqual(calls, ["lifted", ["dragged", 30], ["dragged", 31], ["dropped", 32]]);
    assert.equal(press.swallowsClick(), true);
});

test("a drag let go with no click after it leaves the next press's tap a tap", () => {
    const { press, timers } = setUp();
    press.down(1, { x: 10, y: 10 });
    timers.run();
    press.move(1, { x: 40, y: 10 });
    press.up(1, { x: 40, y: 10 });
    press.down(2, { x: 10, y: 10 });
    press.up(2, { x: 10, y: 10 });
    assert.equal(press.swallowsClick(), false, "the tap selects its phrase");
});

test("a press waits for its hold, then holds", () => {
    const { press, timers } = setUp();
    press.down(1, { x: 10, y: 10 });
    assert.equal(press.pending, true);
    timers.run();
    assert.equal(press.pending, false);
    assert.equal(press.holding, true);
});

test("a drag cancelled goes back; another pointer's moves are not the press's", () => {
    const { press, timers, calls } = setUp();
    press.down(1, { x: 10, y: 10 });
    timers.run();
    press.move(2, { x: 50, y: 10 });
    press.move(1, { x: 30, y: 10 });
    press.cancel(1);
    assert.deepEqual(calls, ["lifted", ["dragged", 30], ["dropped", null]]);
    assert.equal(press.swallowsClick(), false);
});

test("the piece's first phrase does not drag: held and moved, it only opens its menu", () => {
    const { press, timers, calls } = setUp();
    press.down(1, { x: 10, y: 10 }, false);
    timers.run();
    press.move(1, { x: 60, y: 10 });
    press.up(1, { x: 60, y: 10 });
    assert.deepEqual(calls, ["lifted", "menu"]);
});

test("the boundary snaps to the nearest note start, the earlier of two as near; none in a bar without notes", () => {
    assert.equal(nearestOnset([0, 240, 480], 300), 240);
    assert.equal(nearestOnset([0, 240, 480], 360), 240);
    assert.equal(nearestOnset([0, 240, 480], 470), 480);
    assert.equal(nearestOnset([], 470), undefined);
});

test("the two phrases a dragged boundary would make: from the start before it to the target, and from there to the next", () => {
    assert.deepEqual(neighbourSpans([0, 960, 1920], 960, 720, 2880), [{ start: 0, end: 720 }, { start: 720, end: 1920 }]);
    assert.deepEqual(neighbourSpans([0, 960, 1920], 1920, 2400, 2880), [{ start: 960, end: 2400 }, { start: 2400, end: 2880 }], "the last runs to the piece's end");
    assert.deepEqual(neighbourSpans([0, 960], 480, 720, 2880), [], "no phrase starts there");
});

test("the caption writes the bar and the beat the target lies in, as music-core counts them", () => {
    const places = [{ at: 0, bar: 0, beat: 3 }, { at: 480, bar: 1, beat: 1 }, { at: 960, bar: 1, beat: 2 }];
    assert.equal(barBeatCaption(places, 0, "такт {0} · доля {1}"), "такт 0 · доля 3");
    assert.equal(barBeatCaption(places, 1200, "bar {0} · beat {1}"), "bar 1 · beat 2");
    assert.equal(barBeatCaption([], 1200, "bar {0} · beat {1}"), "");
});
