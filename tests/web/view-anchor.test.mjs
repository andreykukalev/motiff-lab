// Tests for where PDF | Notes brings each view back (#511 section 10; Scripts/score/view-anchor.ts, roll-view.ts frameOf),
// run in Node on the compiled output: a place is a system or a page and a share of it, not pixels, so the same layout comes
// back exactly, a new layout comes back on the same bar, the gap between two systems is kept, and going back and forth
// does not drift. The iPad's PDF keeps the same rules in Swift (ipad/App/MotiffLabTests/ViewStatesTests.swift).
//   dotnet build src/MotiffLab.Web && node --test tests/web
import assert from "node:assert/strict";
import test from "node:test";
import { anchorAt, anchorOf, lineOf, systemSpans } from "../../src/MotiffLab.Web/wwwroot/js/score/view-anchor.js";
import { frameOf } from "../../src/MotiffLab.Web/wwwroot/js/roll/roll-view.js";

/** Three systems of 200 px with 40 px between them, starting at bars of 0, 3840 and 7680 ticks. */
const systems = [
    { key: 0, top: 100, bottom: 300 },
    { key: 3840, top: 340, bottom: 540 },
    { key: 7680, top: 580, bottom: 780 },
];

test("a line inside a system is that system and its share", () => {
    assert.deepEqual(anchorAt(systems, 390), { key: 3840, fraction: 0.25 });
    assert.deepEqual(anchorAt(systems, 340), { key: 3840, fraction: 0 });
});

test("a line in the gap above a system keeps the gap as a share below zero", () => {
    assert.deepEqual(anchorAt(systems, 320), { key: 3840, fraction: -0.1 });
    assert.deepEqual(anchorAt(systems, 0), { key: 0, fraction: -0.5 });
});

test("below the last system the place is the last system, past its end", () => {
    assert.deepEqual(anchorAt(systems, 880), { key: 7680, fraction: 1.5 });
});

test("the same layout brings the place back to the same pixel", () => {
    for (const y of [0, 100, 250, 320, 340, 539, 700, 900]) {
        assert.equal(lineOf(systems, anchorAt(systems, y)), y);
    }
});

test("switching back and forth many times does not drift", () => {
    let y = 437;
    for (let round = 0; round < 50; round++) {
        y = lineOf(systems, anchorAt(systems, y));
    }
    assert.equal(y, 437);
});

test("laid out anew, the place goes to the system that now holds its bar, at the same share", () => {
    // Engraved again wider: two systems of 300 px, the second starting at 5760 ticks, so 3840 is in the first.
    const wider = [
        { key: 0, top: 80, bottom: 380 },
        { key: 5760, top: 420, bottom: 720 },
    ];
    assert.equal(lineOf(wider, { key: 3840, fraction: 0.5 }), 230);
    // Before the first system: the first.
    assert.equal(lineOf(wider, { key: -1, fraction: 0 }), 80);
});

test("two pages to a row: the one named first, and each page by its number", () => {
    const pages = [
        { key: 2, top: 0, bottom: 1000 },
        { key: 1, top: 0, bottom: 1000 },
        { key: 3, top: 1020, bottom: 2020 },
    ];
    assert.deepEqual(anchorAt(pages, 500), { key: 1, fraction: 0.5 });
    assert.equal(lineOf(pages, { key: 3, fraction: 0.25 }), 1270);
});

test("no spans and nonsense give nothing", () => {
    assert.equal(anchorAt([], 10), null);
    assert.equal(lineOf([], { key: 0, fraction: 0 }), null);
    assert.equal(anchorAt(systems, Number.NaN), null);
    assert.equal(anchorOf(null), null);
    assert.equal(anchorOf({ key: "3", fraction: 0 }), null);
    assert.equal(anchorOf({ key: 3, fraction: Infinity }), null);
    assert.deepEqual(anchorOf({ key: 3, fraction: 0.5, extra: 1 }), { key: 3, fraction: 0.5 });
});

test("a system is named by the earliest tick of its bars", () => {
    const bars = [{ tick: 0 }, { tick: 1920 }, { tick: 3840 }, { tick: 5760 }];
    const spans = systemSpans([{ top: 0, bottom: 100, bars: [1, 0] }, { top: 120, bottom: 220, bars: [2, 3] }, { top: 240, bottom: 300, bars: [] }], bars);
    assert.deepEqual(spans, [
        { key: 0, top: 0, bottom: 100 },
        { key: 3840, top: 120, bottom: 220 },
    ]);
});

test("the roll's frame read back is four finite numbers, its zoom and rows above zero", () => {
    assert.deepEqual(frameOf({ zoom: 40, rowScale: 1.5, scroll: 1920, pitchTop: 84 }), { zoom: 40, rowScale: 1.5, scroll: 1920, pitchTop: 84 });
    assert.equal(frameOf({ zoom: 0, rowScale: 1, scroll: 0, pitchTop: 84 }), null);
    assert.equal(frameOf({ zoom: 40, rowScale: 1, scroll: 0 }), null);
    assert.equal(frameOf({ zoom: 40, rowScale: 1, scroll: Number.NaN, pitchTop: 84 }), null);
    assert.equal(frameOf("40"), null);
});
