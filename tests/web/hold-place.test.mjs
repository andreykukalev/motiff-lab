// Tests for the long press that puts the playhead where it is held in Navigate (#543 3.1, lead decision 2;
// Scripts/playback/hold-place.ts), run in Node on the compiled output.
import assert from "node:assert/strict";
import { test } from "node:test";

import { HoldToPlace } from "../../src/MotiffLab.Web/wwwroot/js/playback/hold-place.js";
import { HOLD_MS, HOLD_SLOP } from "../../src/MotiffLab.Web/wwwroot/js/score/selection-gesture.js";

/** A clock the test moves by hand. */
function clock() {
    let now = 0;
    const waiting = new Map();
    let next = 0;
    return {
        timers: {
            set: (run, ms) => {
                next += 1;
                waiting.set(next, { at: now + ms, run });
                return next;
            },
            clear: (id) => waiting.delete(id),
        },
        tick(ms) {
            now += ms;
            for (const [id, timer] of [...waiting]) {
                if (timer.at <= now) {
                    waiting.delete(id);
                    timer.run();
                }
            }
        },
        pending: () => waiting.size,
    };
}

test("a press held still for the hold puts the playhead where the pointer is now, and its release is the playhead's", () => {
    const time = clock();
    const fired = [];
    const hold = new HoldToPlace((pointer, x, y) => fired.push([pointer, x, y]), time.timers);
    hold.begin(1, 100, 50);
    hold.move(1, 100 + HOLD_SLOP - 1, 50);
    time.tick(HOLD_MS - 1);
    assert.deepEqual(fired, [], "not before the hold");
    time.tick(1);
    assert.deepEqual(fired, [[1, 100 + HOLD_SLOP - 1, 50]], "where the pointer rests, a slop's drift included");
    assert.equal(hold.waits(1), false);
    assert.equal(hold.end(1), true, "let go after the hold: the playhead's");
    assert.equal(hold.end(1), false, "and only once");
});

test("a press that moves first, a tap, a cancel and a second press leave nothing waiting or held", () => {
    const time = clock();
    const fired = [];
    const hold = new HoldToPlace((pointer) => fired.push(pointer), time.timers);
    hold.begin(1, 100, 50);
    hold.move(1, 100 + HOLD_SLOP + 1, 50);
    time.tick(HOLD_MS);
    assert.deepEqual(fired, [], "a press that moved is the surface's: it moves the music");
    assert.equal(time.pending(), 0);

    hold.begin(2, 10, 10);
    assert.equal(hold.end(2), false, "let go first: a tap");
    time.tick(HOLD_MS);
    assert.deepEqual(fired, []);

    hold.begin(3, 10, 10);
    hold.cancel();
    time.tick(HOLD_MS);
    assert.deepEqual(fired, [], "a cancel leaves no timer behind");

    hold.begin(4, 10, 10);
    hold.move(5, 400, 400);
    hold.begin(6, 20, 20);
    time.tick(HOLD_MS);
    assert.deepEqual(fired, [6], "another pointer's move is not this press's; a new press forgets the one before");
    hold.cancel();
    assert.equal(hold.end(6), false, "a cancel after the hold lets the press go too");
});
