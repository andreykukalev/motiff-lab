// The one selection's grid on the iPad, the score and the roll alike (v0.6.4, Scripts/selection/selection-grid.ts): every
// sixteenth from each bar's start, a tuplet one block that no edge splits.
import assert from "node:assert/strict";
import { test } from "node:test";

import { SIXTEENTH, gridLines, tupletBlocks } from "../../src/shared/wwwroot/js/selection/selection-grid.js";
import { drawnRange, movedEdge } from "../../src/shared/wwwroot/js/selection/selection-places.js";
import { extendedRange } from "../../src/shared/wwwroot/js/selection/selection-gesture.js";

test("the grid is every sixteenth from each bar's start, the bar's end too", () => {
    assert.equal(SIXTEENTH, 120);
    const lines = gridLines([{ start: 0, end: 1920 }, { start: 1920, end: 3840 }]);
    assert.equal(lines.length, 33);
    assert.deepEqual(lines.slice(0, 5), [0, 120, 240, 360, 480]);
    assert.equal(lines.at(-1), 3840);
});

test("a pickup bar and an odd meter count their sixteenths from their own start", () => {
    // A pickup of a dotted eighth (360), then 7/8 (1680), then 4/4.
    const lines = gridLines([{ start: 0, end: 360 }, { start: 360, end: 2040 }, { start: 2040, end: 3960 }]);
    assert.deepEqual(lines.slice(0, 5), [0, 120, 240, 360, 480]);
    assert.ok(lines.includes(2040) && lines.includes(2160) && lines.includes(1920));
    assert.ok(lines.every((line, index) => index === 0 || line > lines[index - 1]), "in time order, each once");
    // A bar whose length is no whole number of sixteenths still ends on its barline.
    const odd = gridLines([{ start: 0, end: 300 }, { start: 300, end: 780 }]);
    assert.deepEqual(odd, [0, 120, 240, 300, 420, 540, 660, 780]);
});

test("a tuplet is one block: no line inside it, its start and end are lines", () => {
    // A triplet of eighths on beat 2 of a 4/4 bar (480 to 960), and a sixteenth triplet at 1440 to 1680.
    const lines = gridLines([{ start: 0, end: 1920 }], [{ start: 480, end: 960 }, { start: 1440, end: 1680 }]);
    assert.ok(!lines.some((line) => line > 480 && line < 960));
    assert.ok(!lines.some((line) => line > 1440 && line < 1680));
    assert.ok(lines.includes(480) && lines.includes(960) && lines.includes(1440) && lines.includes(1680));
    // A group off the sixteenths (a quintuplet that starts late) adds its own ends.
    const late = gridLines([{ start: 0, end: 1920 }], [{ start: 500, end: 900 }]);
    assert.ok(late.includes(500) && late.includes(900) && !late.includes(600) && !late.includes(840));
});

test("an edge near a tuplet goes to its nearer end; a rectangle takes it whole", () => {
    const lines = gridLines([{ start: 0, end: 1920 }], [{ start: 480, end: 960 }]);
    const selection = { start: 0, end: 360 };
    // The end dragged into the triplet: to its start until past the middle, then to its end.
    assert.deepEqual(movedEdge(selection, "end", 360, 600, lines), { start: 0, end: 480 });
    assert.deepEqual(movedEdge(selection, "end", 360, 760, lines), { start: 0, end: 960 });
    // Drawn over part of it, the passage takes all of it.
    assert.deepEqual(drawnRange(600, 700, lines), { start: 480, end: 960 });
    assert.deepEqual(drawnRange(130, 200, lines), { start: 120, end: 240 }, "a single sixteenth elsewhere");
    assert.deepEqual(extendedRange({ start: 1200, end: 1440 }, 700, lines), { start: 480, end: 1440 });
});

test("tuplet groups become blocks from their first start to their last end, nested ones merged", () => {
    const blocks = tupletBlocks([
        [{ start: 480, end: 640 }, { start: 640, end: 800 }, { start: 800, end: 960 }],
        [{ start: 0, end: 80 }, { start: 80, end: 160 }],
        [{ start: 500, end: 560 }],
        [],
        [{ start: Number.NaN, end: 10 }],
    ]);
    assert.deepEqual(blocks, [{ start: 0, end: 160 }, { start: 480, end: 960 }]);
});
