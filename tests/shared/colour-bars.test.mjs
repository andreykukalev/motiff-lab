// The pen's colour bars (v0.10, finding 31, Scripts/score/colour-bars.ts): a tap on a note colours the beats round it, as
// Select's tap takes them; the eraser takes the colour from the cells of the grid it passes over on a highlight's band,
// within that highlight, merged into stretches.
import assert from "node:assert/strict";
import { test } from "node:test";

import { mergedSpans, noteUnit, rubbedColour } from "../../src/shared/wwwroot/js/score/colour-bars.js";

const beats = [0, 480, 960, 1440, 1920, 2400];

test("a note colours the beats round it: from the line at or before its onset to the line at or after its end", () => {
    assert.deepEqual(noteUnit(600, 840, beats), { start: 480, end: 960 }, "an eighth inside beat 2");
    assert.deepEqual(noteUnit(480, 1440, beats), { start: 480, end: 1440 }, "a half note on its beats");
    assert.deepEqual(noteUnit(900, 1100, beats), { start: 480, end: 1440 }, "across a beat line, both beats");
    assert.equal(noteUnit(2500, 2600, beats), undefined, "no line after it");
});

test("spans that overlap or touch are one, in time order; empty ones go", () => {
    assert.deepEqual(mergedSpans([{ start: 960, end: 1200 }, { start: 0, end: 480 }, { start: 480, end: 600 }, { start: 1100, end: 1300 }, { start: 50, end: 50 }]), [
        { start: 0, end: 600 },
        { start: 960, end: 1300 },
    ]);
    assert.deepEqual(mergedSpans([]), []);
});

test("the eraser takes the colour from the grid's cells under it on a band, within the highlight, and nowhere else", () => {
    // One highlight from 240 to 1920 in a bar of 1920 ticks drawn from x 100 to 300; its band starts at 125. The cells are sixteenths.
    const bands = [{ start: 240, end: 1920, runs: [{ left: 125, right: 300, top: 100, bottom: 300 }] }];
    const cellAt = ({ x }) => {
        const tick = ((x - 100) / 200) * 1920;
        const start = Math.floor(tick / 120) * 120;
        return { start, end: start + 120 };
    };
    const points = [{ x: 150, y: 200 }, { x: 152, y: 200 }, { x: 165, y: 210 }, { x: 126, y: 200 }, { x: 400, y: 200 }, { x: 150, y: 50 }];
    assert.deepEqual(rubbedColour(points, bands, cellAt), [
        { start: 240, end: 360 },
        { start: 480, end: 720 },
    ], "the cells under the points on the band, merged; off the band nothing");
    const late = [{ start: 300, end: 1920, runs: [{ left: 100, right: 300, top: 100, bottom: 300 }] }];
    assert.deepEqual(rubbedColour([{ x: 126, y: 200 }], late, cellAt), [{ start: 300, end: 360 }], "a cell clipped to the highlight's span");
    assert.deepEqual(rubbedColour([{ x: 400, y: 200 }], bands, cellAt), [], "off every band");
    assert.deepEqual(rubbedColour([{ x: 150, y: 200 }], [], cellAt), [], "no highlights, nothing to take");
});
