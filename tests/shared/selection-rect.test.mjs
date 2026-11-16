// The rectangle a new selection is drawn with on the iPad's score (v0.6.3, #608; the approved mockup's `.trace`, the lead's
// decision 2): Scripts/score/selection-rect.ts. What it selects is its geometry: time from its sides on one system, «as
// text» across several, snapped outward to the lines, at least one step; the staff it stays within, else both; nothing
// while it reaches over no system. The same rectangle always selects the same passage, whichever way it was drawn.
import assert from "node:assert/strict";
import { test } from "node:test";

import { coveredSystems, rectPassage, traceBox } from "../../src/shared/wwwroot/js/score/selection-rect.js";

const BEAT = 240;
// Two systems of two staves, a 3/8 bar of three beats every 150 pixels from x = 100; the second system goes on in time.
function system(top, firstTick) {
    const lines = [];
    for (let i = 0; i <= 12; i++) {
        lines.push({ tick: firstTick + i * BEAT, x: 100 + i * 50 });
    }
    return { left: 100, right: 700, top, bottom: top + 200, staves: [{ top, bottom: top + 80 }, { top: top + 120, bottom: top + 200 }], lines };
}
const systems = [system(100, 0), system(500, 12 * BEAT)];
const REACH = 24;

test("the rectangle runs from the press to the pointer, whichever way it was drawn", () => {
    assert.deepEqual(traceBox({ x: 300, y: 200 }, { x: 120, y: 90 }), { left: 120, right: 300, top: 90, bottom: 200 });
    assert.deepEqual(traceBox({ x: 5, y: 5 }, { x: 5, y: 5 }), { left: 5, right: 5, top: 5, bottom: 5 });
});

test("a rectangle within a bar selects the beats it covers, snapped outward: a single beat, part of a bar", () => {
    assert.deepEqual(rectPassage({ x: 260, y: 130 }, { x: 290, y: 150 }, systems, REACH), { start: 3 * BEAT, end: 4 * BEAT, hand: "right" }, "one beat");
    assert.deepEqual(rectPassage({ x: 230, y: 300 }, { x: 340, y: 230 }, systems, REACH), { start: 2 * BEAT, end: 5 * BEAT, hand: "left" }, "across a barline, lower staff");
    assert.deepEqual(rectPassage({ x: 250, y: 130 }, { x: 250, y: 130 }, systems, REACH), { start: 3 * BEAT, end: 4 * BEAT, hand: "right" }, "on a line, one step from it");
});

test("the same rectangle drawn from any corner selects the same passage", () => {
    const corners = [[{ x: 230, y: 120 }, { x: 410, y: 260 }], [{ x: 410, y: 260 }, { x: 230, y: 120 }], [{ x: 230, y: 260 }, { x: 410, y: 120 }], [{ x: 410, y: 120 }, { x: 230, y: 260 }]];
    for (const [from, to] of corners) {
        assert.deepEqual(rectPassage(from, to, systems, REACH), { start: 2 * BEAT, end: 7 * BEAT, hand: null });
    }
});

test("the staves by its height: within one staff that hand, into the gap's middle or onto the other both", () => {
    assert.equal(rectPassage({ x: 200, y: 70 }, { x: 300, y: 170 }, systems, REACH).hand, "right", "from above the system into the upper staff");
    assert.equal(rectPassage({ x: 200, y: 140 }, { x: 300, y: 200 }, systems, REACH).hand, null, "into the middle of the gap between the staves");
    assert.equal(rectPassage({ x: 200, y: 310 }, { x: 300, y: 380 }, systems, REACH).hand, "left", "below the system from the lower staff");
});

test("across systems it goes «as text»: from the upper corner on the top system to the lower corner on the bottom one", () => {
    assert.deepEqual(rectPassage({ x: 520, y: 150 }, { x: 260, y: 560 }, systems, REACH), { start: 8 * BEAT, end: 12 * BEAT + 4 * BEAT, hand: null }, "drawn down and left");
    assert.deepEqual(rectPassage({ x: 260, y: 560 }, { x: 520, y: 150 }, systems, REACH), { start: 8 * BEAT, end: 16 * BEAT, hand: null }, "the same rectangle drawn up");
    assert.deepEqual(rectPassage({ x: 180, y: 560 }, { x: 620, y: 150 }, systems, REACH), { start: 10 * BEAT, end: 14 * BEAT, hand: null }, "upper right to lower left");
});

test("from the margin or the gap between systems it selects what it reaches; reaching no system, nothing", () => {
    assert.deepEqual(rectPassage({ x: 20, y: 140 }, { x: 260, y: 150 }, systems, REACH), { start: 0, end: 4 * BEAT, hand: "right" }, "from the left margin, from the system's start");
    assert.deepEqual(rectPassage({ x: 300, y: 400 }, { x: 420, y: 550 }, systems, REACH), { start: 16 * BEAT, end: 19 * BEAT, hand: "right" }, "from between the systems down into the next");
    assert.deepEqual(rectPassage({ x: 600, y: 900 }, { x: 760, y: 560 }, systems, REACH), { start: 22 * BEAT, end: 24 * BEAT, hand: null }, "past the system's end, to its last line");
    assert.equal(rectPassage({ x: 200, y: 360 }, { x: 500, y: 440 }, systems, REACH), null, "only in the gap between the systems");
    assert.equal(rectPassage({ x: 750, y: 150 }, { x: 900, y: 200 }, systems, REACH), null, "only in the right margin");
    assert.equal(coveredSystems({ left: 200, right: 300, top: 300 + REACH, bottom: 300 + REACH }, systems, REACH).length, 1, "a system reaches as far as a press on it does");
});
