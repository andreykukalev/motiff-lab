// Tests for pencil marks on the score (decision record 0015), run in Node on the compiled module:
//   dotnet build src/MotiffLab.Web && node --test tests/web

import assert from "node:assert/strict";
import { test } from "node:test";

import {
    anchorBar,
    anchorMark,
    densify,
    eraseAlong,
    framesByTick,
    NO_RUBBING,
    nearestBar,
    outlinePath,
    placeMark,
    radiusAt,
    readMarks,
    rubAlong,
    rubbedMarks,
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
    assert.equal(left.margin, "left");
    const right = anchorMark(frames, [{ x: 445, y: 300, pressure: 0.5 }, { x: 450, y: 320, pressure: 0.5 }], style);
    assert.equal(right.bar, 7680);
    assert.equal(right.u, 1.3);
    assert.equal(right.margin, "right");

    // Above the staves, nearer a tall bar further in, the margin still goes to the line's first bar.
    const tall = [{ ...frames[0], top: 30 }, { ...frames[1], top: -200 }, frames[2]];
    assert.equal(nearestBar(tall, 95, -150).tick, 1920);
    assert.equal(anchorBar(tall, 95, -150).tick, 0);
    assert.equal(anchorBar(tall, 250, 60).tick, 1920);

    // A PDF's pages have no lines: a stroke beside a page is kept against the page, as before.
    const page = bar(1, 100, 300, 50);
    assert.equal(anchorBar([page, bar(2, 400, 600, 50)], 340, 60).tick, 1);
    assert.equal(anchorMark([page], [{ x: 60, y: 60, pressure: 0.5 }], style).u, -0.2);
    assert.equal("margin" in anchorMark([page], [{ x: 60, y: 60, pressure: 0.5 }], style), false);
    // A stroke begun in its bar says no margin.
    assert.equal("margin" in anchorMark(frames, [{ x: 150, y: 60, pressure: 0.5 }], style), false);
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

    // A stroke written before 0.4 has no margin: u below 0 or above 1 places it exactly as the stroke that says one.
    const { margin: leftSide, ...oldLeft } = left;
    const { margin: rightSide, ...oldRight } = right;
    assert.deepEqual([leftSide, rightSide], ["left", "right"]);
    assert.deepEqual(placeMark(oldLeft, wider), placeMark(left, wider));
    assert.deepEqual(placeMark(oldRight, wider), placeMark(right, wider));
});

test("a stroke that says its margin is counted from that edge of its line, even begun right at the line's end", () => {
    // Begun a hair past the last bar's right edge, u rounds to 1: only the margin says it is outside the bar.
    const mark = { id: "m9", tool: "pen", colour: "graphite", size: 0.16, bar: 3840, u: 1, v: 1, points: [], pressure: 0.5, margin: "right" };
    const wider = framesByTick(line(2, [1920, 3840, 5760, 7680, 9600], 0, 100, 400));
    assert.equal(placeMark(mark, wider).points[0].x, 500);
    const { margin, ...inBar } = mark;
    assert.equal(margin, "right");
    assert.equal(placeMark(inBar, wider).points[0].x, 200);
    assert.equal(placeMark({ ...mark, bar: 5760, u: 0, margin: "left" }, wider).points[0].x, 0);
    // Without a line (a PDF's pages), a margin is from the frame's own edge.
    assert.equal(placeMark({ ...mark, u: 1.1 }, framesByTick([bar(3840, 300, 400, 50)])).points[0].x, 410);
});

test("a stroke's margin is saved and read back; one this build does not know is left out, and the stroke kept", () => {
    const left = { id: "m1", tool: "pen", colour: "graphite", size: 0.16, bar: 0, u: -0.3, v: 1, points: [], pressure: 0.5, margin: "left" };
    assert.deepEqual(readMarks(writeMarks([left])), [left]);
    const [kept] = readMarks(JSON.stringify({ version: 2, marks: [{ ...left, margin: "top" }] }));
    assert.equal("margin" in kept, false);
    assert.equal(kept.u, -0.3);
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

test("the line is walked in short steps, its pressure taken along it", () => {
    const walked = densify([{ x: 0, y: 0, pressure: 0 }, { x: 10, y: 0, pressure: 1 }], 2.5);
    assert.deepEqual(walked.map((p) => p.x), [0, 2.5, 5, 7.5, 10]);
    assert.deepEqual(walked.map((p) => p.pressure), [0, 0.25, 0.5, 0.75, 1]);
    assert.deepEqual(densify([], 1), []);
});

test("the eraser cuts a stroke where it passes and leaves the rest (#479 item 7)", () => {
    const line = Array.from({ length: 11 }, (_, i) => ({ x: i * 10, y: 0, pressure: 0.5 }));
    const pieces = eraseAlong(line, 1, [{ x: 50, y: -20 }, { x: 50, y: 20 }], 5);
    assert.equal(pieces.length, 2, "a pass across the middle leaves both ends");
    assert.ok(pieces[0].at(-1).x < 44);
    assert.ok(pieces[1][0].x > 56);
    assert.equal(pieces[0][0].x, 0);
    assert.equal(pieces[1].at(-1).x, 100);
    assert.equal(eraseAlong(line, 1, [{ x: 50, y: 40 }], 5), null, "a path that misses leaves the stroke alone");
    assert.deepEqual(eraseAlong(line, 1, [{ x: -10, y: 0 }, { x: 110, y: 0 }], 5), [], "a path along it takes all of it");
});

// The eraser rubs out as it moves (BUG-05): each move cuts at once from where the last left off, and the press is one step.
const rubFrames = [bar(0, 0, 400, 50), bar(1920, 0, 400, 250)];
const rubLayout = (() => {
    let next = 0;
    return { frames: rubFrames, byTick: framesByTick(rubFrames), reach: 5, newId: () => `p${++next}` };
})();
const across = (y, id = "m1") => anchorMark(rubFrames, Array.from({ length: 11 }, (_, i) => ({ x: 100 + i * 20, y, pressure: 0.5 })), { ...style, id });

test("each move of the eraser cuts at once, and a piece cut again is replaced, not counted twice (BUG-05)", () => {
    const marks = [across(70)];
    const first = rubAlong(NO_RUBBING, marks, [{ x: 200, y: 60 }, { x: 200, y: 80 }], rubLayout);
    assert.equal(first.cut, true, "the first move takes what it passes over");
    assert.deepEqual(first.rubbing.removed.map((mark) => mark.id), ["m1"]);
    assert.equal(first.rubbing.pieces.length, 2, "both ends are left");
    const shown = rubbedMarks(first.rubbing, marks);
    assert.deepEqual(shown.map((mark) => mark.id), first.rubbing.pieces.map((mark) => mark.id), "the page shows the pieces, not the mark");
    assert.ok(shown.every((mark) => mark.colour === "carmine" && mark.size === 0.2 && mark.bar === 0));

    // Round below the line and up across it again further right.
    const second = rubAlong(first.rubbing, marks, [{ x: 200, y: 100 }, { x: 260, y: 100 }, { x: 260, y: 60 }], rubLayout);
    assert.equal(second.cut, true);
    assert.deepEqual(second.rubbing.removed.map((mark) => mark.id), ["m1"], "only the saved mark goes when the eraser lifts");
    assert.equal(second.rubbing.pieces.length, 3, "a second pass cuts the right-hand piece in two");
    assert.ok(second.rubbing.pieces.every((piece) => !first.rubbing.pieces.slice(1).some((old) => old.id === piece.id)));

    const missed = rubAlong(second.rubbing, marks, [{ x: 260, y: 20 }], rubLayout);
    assert.equal(missed.cut, false, "a move over nothing takes nothing");
    assert.equal(missed.rubbing.pieces.length, 3);
    assert.deepEqual(rubAlong(NO_RUBBING, marks, [], rubLayout), { rubbing: NO_RUBBING, cut: false });
});

test("the eraser rubs out between the points it reports, so a fast move leaves no gap (BUG-05)", () => {
    const marks = [across(70)];
    // Neither point is near the line; the way between them crosses it.
    const down = rubAlong(NO_RUBBING, marks, [{ x: 200, y: 20 }], rubLayout);
    assert.equal(down.cut, false);
    const moved = rubAlong(down.rubbing, marks, [{ x: 210, y: 130 }], rubLayout);
    assert.equal(moved.cut, true, "the move from the last point to this one passes over the line");
    assert.equal(moved.rubbing.pieces.length, 2);
});

test("one press takes several marks, short and long, on any bar it passes (BUG-05)", () => {
    const dot = anchorMark(rubFrames, [{ x: 300, y: 270, pressure: 0.5 }, { x: 301, y: 270, pressure: 0.5 }], { ...style, id: "dot" });
    const marks = [across(70, "top"), across(270, "low"), dot];
    const down = rubAlong(NO_RUBBING, marks, [{ x: 300, y: 40 }, { x: 300, y: 300 }], rubLayout);
    assert.deepEqual(down.rubbing.removed.map((mark) => mark.id), ["top", "low", "dot"]);
    assert.ok(down.rubbing.pieces.every((piece) => piece.id !== "dot"), "a short mark under the eraser goes whole");
    assert.deepEqual(new Set(down.rubbing.pieces.map((piece) => piece.bar)), new Set([0, 1920]), "each piece is kept against its own bar");
    // The saved list may already hold the pieces (music-core's answer): they are not shown twice.
    const saved = [...rubbedMarks(down.rubbing, marks)];
    assert.equal(rubbedMarks(down.rubbing, saved).length, saved.length);
});
