// Selection by the place of the touch (v0.6.2, #601 H items 1, 4, 8; the selection study's section F):
// Scripts/selection/selection-places.ts, one rule for the score and the roll. The place decides, never the time: an edge wins
// over inside, inside extends, outside draws anew (a replace), a tap outside selects, on the margin clears; a finger beside
// the stylus scrolls and holds; the stroke's vertical extent picks the hand.
import assert from "node:assert/strict";
import { test } from "node:test";

import {
    EDGE_REACH,
    DOUBLE_TAP_MS,
    doubleTap,
    drawnRange,
    edgeHandles,
    headAt,
    movedEdge,
    onEdgeHandle,
    placeAt,
    placeChange,
    placeEnded,
    placeHeld,
    placeMoved,
    placePress,
    staffAt,
    strokeHand,
    targetLines,
} from "../../src/shared/wwwroot/js/selection/selection-places.js";

const span = { start: 1920, end: 5760 };
// Two runs: the end of one system and the start of the next.
const runs = [
    { left: 300, right: 700, top: 100, bottom: 300 },
    { left: 60, right: 260, top: 400, bottom: 600 },
];

test("an edge is taken anywhere along its height, within the reach, and wins over inside", () => {
    assert.equal(placeAt(span, 300, 110, runs), "edge-start");
    assert.equal(placeAt(span, 300 - EDGE_REACH, 290, runs), "edge-start");
    assert.equal(placeAt(span, 300 + EDGE_REACH - 1, 200, runs), "edge-start", "inside the band, near the edge, the edge wins");
    assert.equal(placeAt(span, 260, 590, runs), "edge-end");
    assert.equal(placeAt(span, 260 + EDGE_REACH, 450, runs), "edge-end");
    assert.equal(placeAt(span, 300 - EDGE_REACH - 1, 200, runs), "outside");
});

test("the circles' reach: an edge is taken a reach above and below its band, not further", () => {
    assert.equal(placeAt(span, 300, 100 - EDGE_REACH, runs), "edge-start");
    assert.equal(placeAt(span, 300, 100 - EDGE_REACH - 1, runs), "outside");
    assert.equal(placeAt(span, 260, 600 + EDGE_REACH, runs), "edge-end");
});

test("inside the band, away from the edges, is inside, on any of its runs", () => {
    assert.equal(placeAt(span, 500, 200, runs), "inside");
    assert.equal(placeAt(span, 150, 500, runs), "inside");
    assert.equal(placeAt(span, 500, 350, runs), "outside", "between the systems is not inside");
});

test("a short selection keeps an inside: its edges reach a quarter of it inward and the whole reach outward", () => {
    const short = [{ left: 100, right: 124, top: 0, bottom: 100 }];
    assert.equal(placeAt(span, 112, 50, short), "inside");
    assert.equal(placeAt(span, 105, 50, short), "edge-start");
    assert.equal(placeAt(span, 119, 50, short), "edge-end");
    assert.equal(placeAt(span, 100 - EDGE_REACH, 50, short), "edge-start");
    assert.equal(placeAt(span, 124 + EDGE_REACH, 50, short), "edge-end");
});

test("the edges' handles stand at the middle of the start's edge and of the end's, and take a press within the reach of their centre", () => {
    const handles = edgeHandles(runs);
    assert.deepEqual(handles, [{ x: 300, y: 200 }, { x: 260, y: 500 }]);
    assert.deepEqual(edgeHandles([]), []);
    assert.equal(onEdgeHandle(handles, 300, 200), true);
    assert.equal(onEdgeHandle(handles, 300, 200 + EDGE_REACH), true);
    assert.equal(onEdgeHandle(handles, 260 - EDGE_REACH, 500), true, "the end's handle");
    assert.equal(onEdgeHandle(handles, 300, 200 + EDGE_REACH + 1), false, "further down the edge: not its handle");
    assert.equal(onEdgeHandle(handles, 300, 100), false, "at the edge's top, where the blue playhead's knob stands: not the handle");
});

test("without a selection every press is outside, or on the margin off the music", () => {
    assert.equal(placeAt(null, 300, 200, runs), "outside");
    assert.equal(placeAt(null, 300, 200, runs, false), "margin");
    assert.equal(placeAt(span, 300, 200, [], true), "outside");
    assert.equal(placeAt(span, 900, 50, runs, false), "margin");
    assert.equal(placeAt(span, 300, 200, runs, false), "edge-start", "an edge reaching over the margin still takes the press");
});

test("the stylus never waits: a move past the drag distance does what the place says at once", () => {
    for (const [place, step] of [["edge-start", "edge"], ["edge-end", "edge"], ["inside", "extend"], ["outside", "draw"]]) {
        const gesture = placePress(place, "now");
        assert.equal(placeMoved(gesture, 5), "none");
        assert.equal(placeHeld(gesture), "none", "the stylus has no hold");
        assert.equal(placeMoved(gesture, 6), step, place);
        assert.equal(placeMoved(gesture, 40), "none", "once only");
        assert.equal(placeEnded(gesture), step);
    }
});

test("a move from the margin draws a new passage, as from outside (v0.6.3, Andrei: «Да, рисует»); a finger there scrolls", () => {
    const gesture = placePress("margin", "now");
    assert.equal(placeMoved(gesture, 5), "none");
    assert.equal(placeMoved(gesture, 30), "draw");
    assert.equal(placeEnded(gesture), "draw");
    const finger = placePress("margin", "longPress");
    assert.equal(placeHeld(finger), "none", "a finger held on the margin holds no bar");
    assert.equal(placeMoved(finger, 30), "pass", "and its move scrolls");
});

test("taps: outside selects what is under it, the margin clears, inside and on an edge nothing (inside counts for a double tap)", () => {
    assert.equal(placeEnded(placePress("outside", "now")), "tap");
    assert.equal(placeEnded(placePress("margin", "now")), "clear");
    assert.equal(placeEnded(placePress("inside", "now")), "tapInside");
    assert.equal(placeEnded(placePress("edge-end", "now")), "none");
});

test("a finger beside the stylus scrolls; held on a bar it selects that bar, moved on draws, and the way back extends or moves the edge", () => {
    const scrolls = placePress("outside", "longPress");
    assert.equal(placeMoved(scrolls, 7), "none", "within the hold's slop");
    assert.equal(placeMoved(scrolls, 9), "pass");
    assert.equal(placeHeld(scrolls), "none", "a finger that scrolled is not held");
    assert.equal(placeEnded(scrolls), "none");

    const bar = placePress("outside", "longPress");
    assert.equal(placeHeld(bar), "bar");
    assert.equal(placeEnded(bar), "bar", "held still, the bar");
    assert.equal(placeMoved(bar, 7), "none", "within the hold's slop the bar held stays");
    // Andrei, 2026-11-17: moved on after the hold, it draws from where it pressed, as the stylus does, not from the bar's start.
    assert.equal(placeMoved(bar, 50), "draw");
    assert.equal(placeMoved(bar, 80), "none", "once only");
    assert.equal(placeEnded(bar), "draw");

    const inside = placePress("inside", "longPress");
    assert.equal(placeHeld(inside), "extend");
    const edge = placePress("edge-start", "longPress");
    assert.equal(placeHeld(edge), "edge");
    assert.equal(placeHeld(placePress("margin", "longPress")), "none");
    assert.equal(placeEnded(placePress("outside", "longPress")), "none", "a finger's tap is the page's own");
});

test("how the selection changed: a new one over one there was is a replace, the margin with none is nothing", () => {
    assert.equal(placeChange("draw", true), "replace");
    assert.equal(placeChange("draw", false), "create");
    assert.equal(placeChange("tap", true), "replace");
    assert.equal(placeChange("bar", false), "create");
    assert.equal(placeChange("edge", true), "edit");
    assert.equal(placeChange("extend", true), "edit");
    assert.equal(placeChange("clear", true), "clear");
    assert.equal(placeChange("clear", false), "none");
    assert.equal(placeChange("tapInside", true), "none");
});

test("a double tap is a second tap soon and near", () => {
    const first = { time: 1000, x: 100, y: 100 };
    assert.equal(doubleTap(null, first), false);
    assert.equal(doubleTap(first, { time: 1000 + DOUBLE_TAP_MS, x: 110, y: 110 }), true);
    assert.equal(doubleTap(first, { time: 1001 + DOUBLE_TAP_MS, x: 100, y: 100 }), false);
    assert.equal(doubleTap(first, { time: 1100, x: 140, y: 100 }), false);
});

const beats = [0, 480, 960, 1440, 1920, 2400, 2880];

test("a passage drawn from outside snaps outward to the lines, either way, at least one step", () => {
    assert.deepEqual(drawnRange(500, 1500, beats), { start: 480, end: 1920 });
    assert.deepEqual(drawnRange(1500, 500, beats), { start: 480, end: 1920 }, "drawn backward");
    assert.deepEqual(drawnRange(600, 600, beats), { start: 480, end: 960 });
    assert.deepEqual(drawnRange(480, 480, beats), { start: 480, end: 960 }, "on a line, one step from it");
    assert.equal(drawnRange(-10, 100, beats), undefined);
    assert.equal(drawnRange(2880, 2880, beats), undefined, "nothing after the last line");
});

test("an edge's grip moves the edge as far as the pointer, to the line it snaps to, never past the other edge", () => {
    const selection = { start: 960, end: 1920 };
    assert.deepEqual(movedEdge(selection, "end", 1900, 2600, beats), { start: 960, end: 2400 });
    assert.deepEqual(movedEdge(selection, "start", 980, 200, beats), { start: 0, end: 1920 });
    assert.deepEqual(movedEdge(selection, "start", 980, 2500, beats), { start: 1440, end: 1920 });
    assert.deepEqual(targetLines(selection, beats), [0, 480, 960, 1920, 2400, 2880]);
});

const staves = [{ top: 100, bottom: 140 }, { top: 200, bottom: 240 }];

test("a stroke within one staff narrows to its hand; across the gap or onto the other staff it takes both", () => {
    assert.equal(staffAt(staves, 120), 0);
    assert.equal(staffAt(staves, 50), 0, "above the system, the upper staff");
    assert.equal(staffAt(staves, 155), 0, "the nearer part of the gap counts as the staff");
    assert.equal(staffAt(staves, 170), -1);
    assert.equal(staffAt(staves, 300), 1);
    assert.equal(strokeHand(staves, 110, 135, true), "right");
    assert.equal(strokeHand(staves, 205, 260, true), "left");
    assert.equal(strokeHand(staves, 110, 220, true), null);
    assert.equal(strokeHand(staves, 110, 170, true), null, "into the gap");
    assert.equal(strokeHand(staves, 110, 120, false), null, "onto another system");
    assert.equal(strokeHand([staves[0]], 110, 120, true), null, "one staff: no hand");
});

test("a note head is taken within its reach, the nearest of two", () => {
    const heads = [{ id: "a", x: 100, y: 100 }, { id: "b", x: 110, y: 100 }];
    assert.equal(headAt(heads, 104, 103)?.id, "a");
    assert.equal(headAt(heads, 107, 100)?.id, "b");
    assert.equal(headAt(heads, 100, 108), undefined);
    assert.equal(headAt([], 0, 0), undefined);
});

test("a gesture near the top or the bottom of the view scrolls the page, faster nearer the edge", async () => {
    const { scrollStep, SCROLL_ZONE, SCROLL_STEP } = await import("../../src/shared/wwwroot/js/selection/selection-places.js");
    assert.equal(scrollStep(500, 0, 1000), 0);
    assert.equal(scrollStep(SCROLL_ZONE, 0, 1000), 0);
    assert.equal(scrollStep(0, 0, 1000), -SCROLL_STEP);
    assert.ok(scrollStep(SCROLL_ZONE / 2, 0, 1000) < 0 && scrollStep(SCROLL_ZONE / 2, 0, 1000) > -SCROLL_STEP);
    assert.equal(scrollStep(1000, 0, 1000), SCROLL_STEP);
    assert.equal(scrollStep(-40, 0, 1000), -SCROLL_STEP, "never faster than the step");
    assert.ok(scrollStep(100, 80, 900) < 0, "within the zone of the view's own top, under the floating bar");
    assert.equal(scrollStep(140, 80, 900), 0, "past the zone of the view's top");
    assert.equal(scrollStep(40, 80, 900, SCROLL_ZONE, SCROLL_STEP, 30), 0, "a stroke drawn down from under the bar never scrolls up");
    assert.equal(scrollStep(40, 80, 900, SCROLL_ZONE, SCROLL_STEP, 60), -SCROLL_STEP, "moved up toward the top, it scrolls");
    assert.equal(scrollStep(880, 80, 900, SCROLL_ZONE, SCROLL_STEP, 890), 0, "begun at the bottom and drawn up, never down");
    assert.ok(scrollStep(880, 80, 900, SCROLL_ZONE, SCROLL_STEP, 500) > 0, "drawn down to the bottom, it scrolls down");
});

test("a hand narrows the band to its staff of two; both hands, or a system that is not two staves, take every staff", async () => {
    const { handStaves, onMusic, MUSIC_REACH } = await import("../../src/shared/wwwroot/js/score/selection-boxes.js");
    const two = { staves: [{ n: 1 }, { n: 2 }] };
    const geometry = { barAt: (tick) => (tick < 1920 ? two : { staves: [{ n: 1 }] }), systems: [{ left: 100, right: 900, top: 100, bottom: 300 }] };
    assert.deepEqual(handStaves(geometry, 0, "right"), [1]);
    assert.deepEqual(handStaves(geometry, 0, "left"), [2]);
    assert.equal(handStaves(geometry, 0, null), undefined);
    assert.equal(handStaves(geometry, 1920, "right"), undefined);
    assert.equal(onMusic(geometry, 500, 200), true);
    assert.equal(onMusic(geometry, 100 - MUSIC_REACH, 300 + MUSIC_REACH), true);
    assert.equal(onMusic(geometry, 500, 300 + MUSIC_REACH + 1), false, "the page's margin");
});
