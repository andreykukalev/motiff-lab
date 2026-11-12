// Where the contextual menu goes over the selection band (Scripts/score/selection-layer.ts, 0018 section 3).
import assert from "node:assert/strict";
import { test } from "node:test";

import { menuPlace, ordered } from "../../src/MotiffLab.Web/wwwroot/js/score/selection-layer.js";
import { barLines, beatAt, keyLines, nearestLine, spanOf, stepEdge } from "../../src/MotiffLab.Web/wwwroot/js/score/selection-beats.js";
import { BRACKET_LIFT } from "../../src/MotiffLab.Web/wwwroot/js/score/score-geometry.js";

const window = { width: 1180, height: 820 };
const menu = { width: 420, height: 44 };

test("the menu sits centred above the band's first run, clear of the bracket and its number", () => {
    const place = menuPlace({ left: 500, right: 900, top: 400 }, { bottom: 700 }, menu, window);
    assert.equal(place.below, false);
    assert.equal(place.left, 700 - 210);
    assert.ok(place.top + menu.height < 400 - BRACKET_LIFT - 14, "above the bracket's number");
    assert.equal(place.pointer, 210);
});

test("with no room above, the menu goes under the band's last run, and it stays inside the window", () => {
    const place = menuPlace({ left: 0, right: 100, top: 90 }, { bottom: 300 }, menu, window);
    assert.equal(place.below, true);
    assert.equal(place.top, 314);
    assert.equal(place.left, 16);
    // The pointer still points at the band, as far as the menu's rounded end allows.
    assert.equal(place.pointer, 34);
    const right = menuPlace({ left: 1100, right: 1180, top: 500 }, { bottom: 600 }, menu, window);
    assert.equal(right.left, 1180 - 16 - 420);
    assert.equal(right.pointer, 1140 - right.left);
});

test("two bars give the selection's first and last in time, whichever was pressed first", () => {
    const a = { tick: 960 };
    const b = { tick: 0 };
    assert.deepEqual(ordered(a, b), [b, a]);
    assert.deepEqual(ordered(b, a), [b, a]);
});

test("the menu keeps to the right of a drawer over the left of the window", () => {
    const menu = { width: 300, height: 44 };
    const place = menuPlace({ left: 100, right: 300, top: 400 }, { bottom: 700 }, menu, { width: 1180, height: 820, left: 332 });
    assert.equal(place.left, 348);
    assert.equal(place.pointer, 20);
});

test("the menu keeps left of a sheet on the right and above a drawer over the foot of the window", () => {
    const menu = { width: 300, height: 200 };
    const right = menuPlace({ left: 700, right: 900, top: 400 }, { bottom: 500 }, { width: 300, height: 44 }, { width: 1180, height: 820, right: 800 });
    assert.equal(right.left, 800 - 16 - 300);
    // A tall card under the band is lifted to end above the drawer, never above the bar's line.
    const foot = menuPlace({ left: 100, right: 300, top: 90 }, { bottom: 300 }, menu, { width: 1180, height: 820, bottom: 500 });
    assert.equal(foot.below, true);
    assert.equal(foot.top, 500 - 16 - 200);
    const low = menuPlace({ left: 100, right: 300, top: 90 }, { bottom: 300 }, menu, { width: 1180, height: 820, bottom: 200 });
    assert.equal(low.top, 72);
});

// Selection by beats (#349): the drag and the handles snap to the beat lines music-core gives.
const bar = { tick: 1920, end: 3840 };
const lines = [0, 480, 960, 1440, 1920, 2400, 2880, 3360, 3840, 4320];
const xOf = (line) => 100 + (line - bar.tick) / 4;

test("a bar's beat lines run from its start to its end, the lines inside it between", () => {
    assert.deepEqual(barLines(lines, bar), [1920, 2400, 2880, 3360, 3840]);
    assert.deepEqual(barLines(lines, { tick: 3840, end: Infinity }), [3840, 4320]);
});

test("a point across a bar falls in the beat from the last line left of it to the next", () => {
    const bars = barLines(lines, bar);
    assert.deepEqual(beatAt(bars, xOf, 90), { start: 1920, end: 2400 });
    assert.deepEqual(beatAt(bars, xOf, 100 + 130), { start: 2400, end: 2880 });
    assert.deepEqual(beatAt(bars, xOf, 100 + 2000), { start: 3360, end: 3840 });
    assert.equal(beatAt([1920], xOf, 100), undefined);
});

test("a handle goes to the beat line nearest the finger", () => {
    assert.equal(nearestLine(barLines(lines, bar), xOf, 100 + 115), 2400);
    assert.equal(nearestLine(barLines(lines, bar), xOf, 100 + 470), 3840);
    assert.equal(nearestLine([], xOf, 0), undefined);
});

test("a drag over two beats takes both, whichever came first", () => {
    assert.deepEqual(spanOf({ start: 2880, end: 3360 }, { start: 1920, end: 2400 }), { start: 1920, end: 3360 });
});

test("the arrow keys move an edge one beat line and never onto the other edge", () => {
    const span = { start: 2400, end: 2880 };
    assert.deepEqual(stepEdge(lines, span, "start", -1), { start: 1920, end: 2880 });
    assert.deepEqual(stepEdge(lines, span, "end", 1), { start: 2400, end: 3360 });
    assert.equal(stepEdge(lines, span, "start", 1), undefined);
    assert.equal(stepEdge(lines, span, "end", -1), undefined);
    assert.equal(stepEdge(lines, { start: 0, end: 480 }, "start", -1), undefined);
});

test("the arrow keys move a beat edge among the beat lines and the barlines, each once and in time order", () => {
    const bars = [
        { tick: 1440, end: 2880 },
        { tick: 0, end: 1440 },
        { tick: 1440, end: 2880 },
    ];
    assert.deepEqual(keyLines([480, 960, 1440, 1920], bars), [0, 480, 960, 1440, 1920, 2880]);
});
