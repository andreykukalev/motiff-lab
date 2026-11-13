// Tests for the zoom on each axis of the iPad's roll (v0.5.2, #479 item 23; Scripts/roll/roll-zoom.ts), run in Node on the
// compiled output: the bars and the octaves in view as the controls say them, the steps, and which axes a pinch zooms.
import assert from "node:assert/strict";
import test from "node:test";
import {
    barShareInView,
    barsInView,
    octavesInView,
    pinchAxes,
    pinchFactors,
    rowForOctaves,
    sameZoom,
    stepBars,
    stepOctaves,
} from "../../src/MotiffLab.Web/wwwroot/js/roll/roll-zoom.js";

/** Bars of 3/8 at 480 per quarter (720 ticks), as in Für Elise. */
const bars = Array.from({ length: 20 }, (_, index) => ({ onset: index * 720, end: (index + 1) * 720 }));

test("the bars in view count each bar by its share in view, at least one", () => {
    assert.equal(barsInView(bars, 0, 9 * 720), 9);
    assert.equal(barsInView(bars, 360, 9 * 720), 9, "eight and a half bars say 9");
    assert.equal(barsInView(bars, 0, 2 * 720 + 100), 2, "two bars and a sliver say 2");
    assert.equal(barsInView(bars, 0, 100), 1, "a sliver of a bar says 1");
    assert.equal(barsInView(bars, 30 * 720, 40 * 720), 0, "past the end, none");
    assert.equal(barShareInView(bars, 360, 1080), 1);
});

test("a step of the time zoom changes the number shown, by a quarter and at least one bar", () => {
    assert.equal(stepBars(9, 1), 7, "+ shows fewer bars");
    assert.equal(stepBars(9, -1), 11, "− shows more");
    assert.equal(stepBars(2, 1), 1);
    assert.equal(stepBars(1, 1), 1, "never under one bar");
    assert.equal(stepBars(40, -1), 50);
    assert.equal(stepBars(8.6, 1), 7, "from the count shown");
});

test("the octaves in view are the rows over twelve, at least one, and a step is one octave", () => {
    assert.equal(octavesInView(36), 3);
    assert.equal(octavesInView(41), 3);
    assert.equal(octavesInView(5), 1);
    assert.equal(stepOctaves(3, 1), 2, "+ one octave fewer");
    assert.equal(stepOctaves(1, 1), 1);
    assert.equal(stepOctaves(3, -1), 4, "− one more");
    assert.equal(rowForOctaves(2, 480, 5, 40), 20);
    assert.equal(rowForOctaves(1, 960, 5, 40), 40, "within the rows' limits");
    assert.equal(rowForOctaves(20, 480, 5, 40), 5);
});

test("a pinch zooms the axis its fingers spread along, both on a diagonal", () => {
    assert.deepEqual(pinchAxes(200, 20), { time: true, pitch: false });
    assert.deepEqual(pinchAxes(10, -180), { time: false, pitch: true });
    assert.deepEqual(pinchAxes(120, 100), { time: true, pitch: true });
    assert.deepEqual(pinchFactors({ dx: 200, dy: 20 }, { dx: 400, dy: 60 }), { time: 2, pitch: 1 }, "a pinch across leaves the rows");
    assert.deepEqual(pinchFactors({ dx: 10, dy: -100 }, { dx: 10, dy: -50 }), { time: 1, pitch: 0.5 });
    assert.deepEqual(pinchFactors({ dx: 12, dy: 10 }, { dx: 100, dy: 100 }), { time: 1, pitch: 1 }, "fingers too close start no zoom");
});

test("the host hears of the zoom only when it changes", () => {
    const zoom = { bars: 9, octaves: 3, timeIn: true, timeOut: true, pitchIn: true, pitchOut: true };
    assert.equal(sameZoom(null, zoom), false);
    assert.equal(sameZoom(zoom, { ...zoom }), true);
    assert.equal(sameZoom(zoom, { ...zoom, octaves: 2 }), false);
});
