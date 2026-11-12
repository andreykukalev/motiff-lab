// Tests for pencil marks on the score (decision record 0015), run in Node on the compiled module:
//   dotnet build src/MotiffLab.Web && node --test tests/web

import assert from "node:assert/strict";
import { test } from "node:test";

import {
    anchorBar,
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

/** The bars of a line from `left`, each `width` wide, as the score's geometry gives them with the line's edges. */
const line = (number, ticks, left, width, staffTop) => ticks.map((tick, i) => ({
    ...bar(tick, left + i * width, left + (i + 1) * width, staffTop),
    line: number,
    lineLeft: left,
    lineRight: left + ticks.length * width,
}));

test("a stroke begun in a line's margin is kept against the line's first or last bar, outside it", () => {
    const frames = [...line(0, [0, 1920, 3840], 100, 100, 50), ...line(1, [5760, 7680], 100, 150, 250)];
    const left = anchorMark(frames, [{ x: 60, y: 60, pressure: 0.5 }, { x: 70, y: 80, pressure: 0.5 }], style);
    assert.equal(left.bar, 0);
    assert.equal(left.u, -0.4);
    const right = anchorMark(frames, [{ x: 445, y: 300, pressure: 0.5 }, { x: 450, y: 320, pressure: 0.5 }], style);
    assert.equal(right.bar, 7680);
    assert.equal(right.u, 1.3);

    // Above the staves, nearer a tall bar further in, the margin still goes to the line's first bar.
    const tall = [{ ...frames[0], top: 30 }, { ...frames[1], top: -200 }, frames[2]];
    assert.equal(nearestBar(tall, 95, -150).tick, 1920);
    assert.equal(anchorBar(tall, 95, -150).tick, 0);
    assert.equal(anchorBar(tall, 250, 60).tick, 1920);

    // A PDF's pages have no lines: a stroke beside a page is kept against the page, as before.
    const page = bar(1, 100, 300, 50);
    assert.equal(anchorBar([page, bar(2, 400, 600, 50)], 340, 60).tick, 1);
    assert.equal(anchorMark([page], [{ x: 60, y: 60, pressure: 0.5 }], style).u, -0.2);
});

test("laid out again, a stroke in a margin stays at the edge of its bar's line, not over the neighbouring bar", () => {
    const written = line(0, [3840, 5760], 100, 100, 50);
    const left = anchorMark(written, [{ x: 70, y: 60, pressure: 0.5 }, { x: 80, y: 60, pressure: 0.5 }], style);
    const right = anchorMark(written, [{ x: 320, y: 60, pressure: 0.5 }, { x: 330, y: 60, pressure: 0.5 }], style);
    assert.deepEqual([left.bar, left.u, right.bar, right.u], [3840, -0.3, 5760, 1.2]);

    // In the layout it was written in, every build draws it where it was written.
    assert.equal(placeMark(left, framesByTick(written)).points[0].x, 70);
    assert.equal(placeMark(right, framesByTick(written)).points[0].x, 320);

    // Laid out wider, both bars sit in the middle of a line from 0 to 500: the strokes go to that line's margins.
    const wider = framesByTick(line(2, [1920, 3840, 5760, 7680, 9600], 0, 100, 400));
    assert.deepEqual(placeMark(left, wider).points.map((point) => [point.x, point.y]), [[-30, 410], [-20, 410]]);
    assert.deepEqual(placeMark(right, wider).points.map((point) => [point.x, point.y]), [[520, 410], [530, 410]]);

    // A stroke inside its bar goes with the bar as before.
    const inside = anchorMark(written, [{ x: 150, y: 60, pressure: 0.5 }], style);
    assert.equal(placeMark(inside, wider).points[0].x, 150);
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

test("the iPad app's pen sizes are the web app's, thin to thick", async () => {
    const { readFile } = await import("node:fs/promises");
    const { DEFAULT_WIDTH, HIGHLIGHTER_SIZES, PEN_SIZES } = await import("../../src/MotiffLab.Web/wwwroot/js/score/pencil-marks.js");
    const swift = await readFile(new URL("../../ipad/App/MotiffLab/PenTools.swift", import.meta.url), "utf8");
    const list = (name) => JSON.parse(swift.match(new RegExp(`static let ${name}: \\[Double\\] = (\\[[^\\]]*\\])`))[1]);
    assert.deepEqual(list("pen"), [...PEN_SIZES]);
    assert.deepEqual(list("highlighter"), [...HIGHLIGHTER_SIZES]);
    assert.equal(Number(swift.match(/static let defaultWidth = (\d+)/)[1]), DEFAULT_WIDTH);
});
