// Tests for the zoom on each axis of the iPad's roll (v0.5.2, #479 item 23; Scripts/roll/roll-zoom.ts), run in Node on the
// compiled output: the bars and the octaves in view as the controls say them, the steps, and how a pinch zooms both axes.
import assert from "node:assert/strict";
import test from "node:test";
import {
    barShareInView,
    barsInView,
    octavesInView,
    PINCH_LEAST,
    pinchStep,
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

test("a pinch zooms both axes in every move, whichever way it began, and may turn without lifting (#521 4)", () => {
    const across = pinchStep({ dx: 200, dy: 60 }, { dx: 400, dy: 60 });
    assert.deepEqual([across.time, across.pitch], [2, 1], "spread across, the time zooms and the rows stay");
    const both = pinchStep({ dx: 200, dy: 100 }, { dx: 100, dy: 200 });
    assert.deepEqual([both.time, both.pitch], [0.5, 2], "both axes answer in one move");
    // A pinch that begins up and down (the fingers in line across) and then turns sideways zooms the time from there.
    let spread = { dx: 10, dy: -150 };
    let time = 1;
    let pitch = 1;
    for (const now of [{ dx: 12, dy: -300 }, { dx: PINCH_LEAST, dy: -300 }, { dx: 2 * PINCH_LEAST, dy: -300 }, { dx: 4 * PINCH_LEAST, dy: -150 }]) {
        const step = pinchStep(spread, now);
        time *= step.time;
        pitch *= step.pitch;
        spread = step.spread;
    }
    assert.equal(pitch, 1, "the rows went out and came back");
    assert.equal(time, 4, "the time zoomed from where the fingers first spread across, without a jump");
    const close = pinchStep({ dx: 12, dy: 10 }, { dx: 30, dy: 20 });
    assert.deepEqual([close.time, close.pitch], [1, 1], "fingers in line along both start no zoom");
    assert.deepEqual(close.spread, { dx: 12, dy: 10 }, "and keep what they were measured at");
});

test("the host hears of the zoom only when it changes", () => {
    const zoom = { bars: 9, octaves: 3, timeIn: true, timeOut: true, pitchIn: true, pitchOut: true, zoomOut: false };
    assert.equal(sameZoom(null, zoom), false);
    assert.equal(sameZoom(zoom, { ...zoom }), true);
    assert.equal(sameZoom(zoom, { ...zoom, octaves: 2 }), false);
    assert.equal(sameZoom(zoom, { ...zoom, zoomOut: true }), false, "Zoom Out shown or hidden is news too (#521 2.2)");
});
