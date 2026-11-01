// Tests for pencil marks on the score (decision record 0015), run in Node on the compiled module:
//   dotnet build src/MotiffLab.Web && node --test tests/web

import assert from "node:assert/strict";
import { test } from "node:test";

import {
    anchorMark,
    framesByTick,
    nearestBar,
    outlinePath,
    placeMark,
    radiusAt,
    readMarks,
    touches,
    writeMarks,
} from "../../src/MotiffLab.Web/wwwroot/js/score/pencil-marks.js";

const bar = (tick, left, right, staffTop, space = 10) => ({ tick, left, right, top: staffTop - 20, bottom: staffTop + 100, staffTop, space });

const style = { id: "m1", tool: "pen", colour: "carmine", size: 0.2 };

test("a point belongs to the bar it lies in, or else the nearest", () => {
    const frames = [bar(0, 0, 100, 50), bar(1920, 100, 220, 50), bar(3840, 0, 150, 250)];
    assert.equal(nearestBar(frames, 150, 60).tick, 1920);
    // Above the first system's staves, where fingerings and dynamics are written.
    assert.equal(nearestBar(frames, 40, 5).tick, 0);
    // Between two systems, nearer the lower one.
    assert.equal(nearestBar(frames, 40, 215).tick, 3840);
    assert.equal(nearestBar([], 0, 0), undefined);
});

test("a mark is kept against its bar: a share across it, staff spaces below its top line, and its shape in staff spaces", () => {
    const frames = [bar(0, 0, 100, 50), bar(1920, 100, 300, 50, 10)];
    const mark = anchorMark(frames, [
        { x: 150, y: 40, pressure: 0.4 },
        { x: 170, y: 45, pressure: 0.6 },
    ], style);

    assert.deepEqual(mark, {
        id: "m1",
        tool: "pen",
        colour: "carmine",
        size: 0.2,
        bar: 1920,
        u: 0.25,
        v: -1,
        points: [2, 0.5, 0.6],
        pressure: 0.4,
    });
    assert.equal(anchorMark(frames, [], style), null);
    assert.equal(anchorMark([], [{ x: 1, y: 1, pressure: 0.5 }], style), null);
});

test("laid out again, a mark goes back to its bar at the staves' new size", () => {
    const written = anchorMark([bar(1920, 100, 300, 50, 10)], [
        { x: 150, y: 40, pressure: 0.5 },
        { x: 170, y: 45, pressure: 0.5 },
    ], style);
    // The bar moved to the next system, is narrower, and the window is larger: staff lines 15 px apart.
    const placed = placeMark(written, framesByTick([bar(1920, 0, 100, 400, 15)]));

    assert.deepEqual(placed.points, [
        { x: 25, y: 385, pressure: 0.5 },
        { x: 55, y: 392.5, pressure: 0.5 },
    ]);
    assert.equal(placed.radius, 1.5);
    assert.equal(placeMark(written, framesByTick([bar(0, 0, 100, 50)])), null);
});

test("pressure thins a light touch and thickens a firm one; without it the size holds", () => {
    assert.equal(radiusAt(2, 0.5, true), 2);
    assert.equal(radiusAt(2, 0, true), 1);
    assert.equal(radiusAt(2, 1, true), 3);
    assert.equal(radiusAt(2, 7, true), 3);
    assert.equal(radiusAt(2, 0.1, false), 2);
});

test("a stroke's outline is a closed path round both edges; one point is a dot", () => {
    const line = outlinePath([
        { x: 0, y: 0, pressure: 0.5 },
        { x: 10, y: 0, pressure: 0.5 },
        { x: 20, y: 0, pressure: 0.5 },
    ], 2, true);
    assert.match(line, /^M0 2Q10 2 15 2L20 2A2 2 0 0 0 20 -2Q10 -2 5 -2L0 -2A2 2 0 0 0 0 2Z$/);

    const dot = outlinePath([{ x: 5, y: 5, pressure: 0.5 }], 1, false);
    assert.equal(dot, "M4 5A1 1 0 1 0 6 5A1 1 0 1 0 4 5Z");
    assert.equal(outlinePath([], 1, false), "");
});

test("the eraser takes a stroke it comes within reach of, the stroke's thickness included", () => {
    const points = [{ x: 0, y: 0, pressure: 0.5 }, { x: 100, y: 0, pressure: 0.5 }];
    assert.equal(touches(points, 2, 50, 11, 10), true);
    assert.equal(touches(points, 2, 50, 13, 10), false);
    assert.equal(touches(points, 2, 108, 0, 10), true);
    assert.equal(touches([{ x: 0, y: 0, pressure: 0.5 }], 1, 4, 4, 6), true);
    assert.equal(touches([{ x: 0, y: 0, pressure: 0.5 }], 1, 5, 5, 6), false);
});

test("marks read back as written, in the project file's shape, and anything damaged is left out", () => {
    const mark = { id: "a", tool: "highlighter", colour: "amber", size: 1.1, bar: 0, u: 0.5, v: 2, points: [1, 0, 0.5], pressure: 0.5 };
    assert.deepEqual(readMarks(writeMarks([mark])), [mark]);
    assert.equal(JSON.parse(writeMarks([mark])).version, 2);

    const damaged = JSON.stringify({ version: 2, marks: [mark, { ...mark, id: "b", colour: "green" }, { ...mark, id: "c", points: [1, 2] }, null] });
    assert.deepEqual(readMarks(damaged).map((m) => m.id), ["a"]);
    assert.deepEqual(readMarks("not json"), []);
    assert.deepEqual(readMarks(JSON.stringify({ version: 3, marks: [mark] })), []);
    assert.deepEqual(readMarks(null), []);
    assert.deepEqual(readMarks(""), []);
});

test("a version 1 file's colours become the inks nearest them", () => {
    const stroke = (id, colour, tool = "pen") => ({ id, tool, colour, size: 0.2, bar: 0, u: 0.5, v: 2, points: [], pressure: 0.5 });
    const old = JSON.stringify({ version: 1, marks: [stroke("a", "ink"), stroke("b", "red"), stroke("c", "blue"), stroke("d", "yellow", "highlighter"), stroke("e", "carmine")] });

    assert.deepEqual(readMarks(old).map((m) => [m.id, m.colour]), [["a", "graphite"], ["b", "carmine"], ["c", "forest"], ["d", "amber"]]);
});
