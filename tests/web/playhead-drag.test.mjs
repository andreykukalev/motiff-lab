// Tests for dragging the playheads (v0.5.2, #495 UX-08 and UX-10, slice I3): the point to a tick along a bar's onsets
// (tickInBar), the sixteenth grid and the limits (snapTick, scoreDragTick, laneDragTick), which handle a press on the roll
// takes (laneHeadAt), the frame-by-frame sending (PlayheadDrag), and the score's handles, whose press goes no further, so
// a drag never selects a bar (ScoreGrab). In Node on the compiled modules:   dotnet build src/MotiffLab.Web && node --test tests/web

import assert from "node:assert/strict";
import { test } from "node:test";

import { DRAG_GRID, PlayheadDrag, barOf, snapTick } from "../../src/MotiffLab.Web/wwwroot/js/playback/playhead-drag.js";
import { knobHeadAt, laneDragTick, laneHeadAt } from "../../src/MotiffLab.Web/wwwroot/js/roll/roll-head-drag.js";
import { playheadKnobs } from "../../src/MotiffLab.Web/wwwroot/js/roll/roll-modes.js";
import { placeInBar, tickInBar } from "../../src/MotiffLab.Web/wwwroot/js/score/score-geometry.js";
import { ScoreGrab, scoreDragTick } from "../../src/MotiffLab.Web/wwwroot/js/score/playhead-grab.js";

const bar = { tick: 1920, end: 3840, left: 100, right: 500, top: 0, bottom: 80 };
const onsets = [{ tick: 1920, x: 120 }, { tick: 2400, x: 200 }, { tick: 2880, x: 360 }, { tick: 3360, x: 420 }];

test("the tick at an x is the playhead's place the other way round, along the bar's onsets", () => {
    for (const tick of [1920, 2000, 2400, 2700, 3000, 3500, 3800]) {
        assert.ok(Math.abs(tickInBar(bar, onsets, placeInBar(bar, onsets, tick)) - tick) < 1e-9, `tick ${tick}`);
    }
    assert.equal(tickInBar(bar, onsets, 40), 1920, "left of the bar: its start");
    assert.equal(tickInBar(bar, onsets, 900), 3840, "right of it: its end");
    assert.equal(tickInBar(bar, [], 300), 2880, "a bar without notes is crossed evenly");
});

test("a dragged place goes to the sixteenth grid of its bar, never past the bar's last sixteenth", () => {
    assert.equal(DRAG_GRID, 120);
    const span = { start: 1920, end: 3840 };
    assert.equal(snapTick(2000, span), 2040, "the nearest sixteenth");
    assert.equal(snapTick(1979, span), 1920);
    assert.equal(snapTick(3830, span), 3720, "the bar's last sixteenth, not the next bar");
    assert.equal(snapTick(100, span), 1920, "before the bar: its start");
    assert.equal(snapTick(2041.6, span), 2040, "a whole tick");
});

test("held to the selection, the blue playhead stays from its start to its last sixteenth", () => {
    const span = { start: 1920, end: 3840 };
    const selection = { start: 2400, end: 3360 };
    assert.equal(snapTick(2000, span, selection), 2400);
    assert.equal(snapTick(3500, span, selection), 3240, "the last sixteenth before the end");
    assert.equal(snapTick(2890, span, selection), 2880);
    assert.equal(snapTick(2950, span, { start: 2400, end: 2950 }), 2880, "an end off the grid");
    assert.equal(snapTick(1000, span, { start: 2410, end: 2420 }), 2410, "a selection shorter than a sixteenth: its start");
});

test("the bar of a tick, the last one past the end", () => {
    const bars = [{ start: 0, end: 1920 }, { start: 1920, end: 3840 }];
    assert.equal(barOf(bars, 2000), bars[1]);
    assert.equal(barOf(bars, 1920), bars[1]);
    assert.equal(barOf(bars, 9000), bars[1]);
    assert.equal(barOf(bars, -5), bars[0]);
    assert.equal(barOf([], 10), undefined);
});

/** A score with two bars on a line, as the drag reads it. */
function fakeGeometry() {
    const first = { ...bar, tick: 0, end: 1920, left: 100, right: 500, id: "m1" };
    const second = { ...bar, tick: 1920, end: 3840, left: 500, right: 900, id: "m2" };
    const bars = [first, second];
    return {
        bars,
        barNear: (x) => (x < 500 ? first : second),
        barAt: (tick) => bars.find((one) => tick >= one.tick && tick < one.end),
        tickAt: (one, x) => tickInBar(one, [], x),
        playhead: (tick) => ({ x: 0, top: 0, bottom: 80, bar: bars.find((one) => tick >= one.tick && tick < one.end) }),
    };
}

test("on the score the black playhead keeps to the bar it stood in, the blue one to the selection", () => {
    const geometry = fakeGeometry();
    const [first, second] = geometry.bars;
    const none = () => undefined;
    assert.equal(scoreDragTick(geometry, none, "black", first, { x: 300, y: 40 }, null), 960);
    assert.equal(scoreDragTick(geometry, none, "black", first, { x: 700, y: 40 }, null), 1800, "over the next bar it stays in its own");
    const selection = { start: 960, end: 2880 };
    assert.equal(scoreDragTick(geometry, none, "blue", null, { x: 600, y: 40 }, selection), 2400, "inside the selection, in the bar under it");
    assert.equal(scoreDragTick(geometry, none, "blue", null, { x: 880, y: 40 }, selection), 2760, "past the selection: its last sixteenth");
    assert.equal(scoreDragTick(geometry, none, "blue", null, { x: 120, y: 40 }, selection), 960, "before it: its start");
    assert.equal(scoreDragTick(geometry, none, "blue", null, { x: 300, y: 40 }, null), undefined, "no blue playhead without a selection");
    assert.equal(second.tick, 1920);
});

test("on the roll the blue playhead is taken in the loop strip and the black one down its line", () => {
    const heads = { black: 200, blue: 400, stripTop: 24, stripBottom: 52, reach: 22 };
    assert.equal(laneHeadAt(410, 30, heads), "blue");
    assert.equal(laneHeadAt(210, 200, heads), "black");
    assert.equal(laneHeadAt(210, 30, heads), null, "the black line runs under the loop strip");
    assert.equal(laneHeadAt(410, 200, heads), null, "the blue handle is in the loop strip only");
    assert.equal(laneHeadAt(410, 30, { ...heads, blue: null }), null);
    assert.equal(laneHeadAt(300, 200, heads), null, "away from both");
});

test("on the roll the black playhead goes across the piece and the blue one inside the selection, on the grid", () => {
    const bars = [{ start: 0, end: 1920 }, { start: 1920, end: 3840 }];
    assert.equal(laneDragTick("black", 2930, bars, null, 3840), 2880, "into another bar");
    assert.equal(laneDragTick("black", 9000, bars, null, 3840), 3720, "not past the piece's last sixteenth");
    assert.equal(laneDragTick("black", -40, bars, null, 3840), 0);
    const selection = { start: 960, end: 2880 };
    assert.equal(laneDragTick("blue", 100, bars, selection, 3840), 960);
    assert.equal(laneDragTick("blue", 3500, bars, selection, 3840), 2760);
    assert.equal(laneDragTick("blue", 1500, bars, null, 3840), undefined);
});

/** A drag that sends to a list, with the frames run by hand and the clock set by hand. */
function fakeDrag() {
    const sent = [];
    const frames = [];
    const clock = { now: 0 };
    const drag = new PlayheadDrag((message) => sent.push(message), (run) => frames.push(run), () => clock.now);
    const frame = () => frames.splice(0).forEach((run) => run());
    return { drag, sent, frame, clock };
}

test("a drag is taken by its first move, sends the newest place once a frame and the last place before letting go", () => {
    const { drag, sent, frame } = fakeDrag();
    assert.equal(drag.grab("black", 1), true);
    assert.equal(drag.grab("blue", 2), false, "one playhead at a time");
    assert.deepEqual(sent, [], "a press alone is not a drag");
    assert.equal(drag.heard(480), 480, "the player's place until it moves");
    drag.move(1, 600);
    drag.move(1, 720);
    drag.move(2, 840);
    assert.equal(drag.heard(480), 720, "the place dragged to shows at once");
    frame();
    frame();
    drag.move(1, 840);
    drag.release(1);
    frame();
    assert.deepEqual(sent, [
        { phase: "grab", handle: "black" },
        { phase: "move", handle: "black", tick: 720 },
        { phase: "move", handle: "black", tick: 840 },
        { phase: "release", handle: "black" },
    ]);
});

test("a tap on a handle sends nothing, and after a drag the place shows until the player reports it", () => {
    const { drag, sent, clock } = fakeDrag();
    drag.grab("blue", 1);
    drag.release(1);
    assert.deepEqual(sent, []);
    drag.grab("blue", 1);
    drag.move(1, 1200);
    drag.release();
    assert.equal(drag.heard(480), 1200, "the player has not caught up");
    assert.equal(drag.heard(1200), 1200);
    assert.equal(drag.heard(500), 500, "caught up, the player's place again");
    drag.grab("blue", 1);
    drag.move(1, 1320);
    drag.release(1);
    clock.now = 1000;
    assert.equal(drag.heard(480), 480, "or after a moment");
});

/** An element as the score's handles are, enough for the drag: listeners, the pointer's capture. */
class FakeElement {
    constructor() {
        this.listeners = new Map();
        this.captured = new Set();
    }

    addEventListener(name, listener) {
        this.listeners.set(name, [...(this.listeners.get(name) ?? []), listener]);
    }

    removeEventListener(name, listener) {
        this.listeners.set(name, (this.listeners.get(name) ?? []).filter((one) => one !== listener));
    }

    setPointerCapture(id) {
        this.captured.add(id);
    }

    hasPointerCapture(id) {
        return this.captured.has(id);
    }

    releasePointerCapture(id) {
        this.captured.delete(id);
    }

    fire(name, event) {
        for (const listener of this.listeners.get(name) ?? []) {
            listener(event);
        }
    }
}

function pointer(element, fields) {
    const event = { pointerId: 1, pointerType: "pen", button: 0, x: 0, y: 40, currentTarget: element, stopped: false, prevented: false, ...fields };
    event.stopPropagation = () => { event.stopped = true; };
    event.preventDefault = () => { event.prevented = true; };
    return event;
}

test("a press on a score handle is the handle's alone: captured, never passed on, so no bar is selected", () => {
    globalThis.HTMLElement = FakeElement;
    const geometry = fakeGeometry();
    const sent = [];
    const frames = [];
    let writes = false;
    const grab = new ScoreGrab({
        geometry: () => geometry,
        onsetOf: () => undefined,
        selection: () => null,
        position: () => 480,
        takes: (type) => type !== "pen" || !writes,
        point: (event) => ({ x: event.x, y: event.y }),
        moved: () => {},
    }, new PlayheadDrag((message) => sent.push(message), (run) => frames.push(run)));
    const dot = new FakeElement();
    const down = pointer(dot, { x: 160 });
    grab.begin("black", down);
    assert.equal(down.stopped, true, "the selection layer and the tap on a beat never see it");
    assert.equal(down.prevented, true);
    assert.equal(dot.hasPointerCapture(1), true, "the handle keeps the pointer");
    dot.fire("pointermove", pointer(dot, { x: 162 }));
    assert.deepEqual(sent, [], "a tap's wobble moves nothing");
    const move = pointer(dot, { x: 700 });
    dot.fire("pointermove", move);
    assert.equal(move.stopped, true);
    frames.splice(0).forEach((run) => run());
    dot.fire("pointerup", pointer(dot, {}));
    assert.deepEqual(sent, [{ phase: "grab", handle: "black" }, { phase: "move", handle: "black", tick: 1800 }, { phase: "release", handle: "black" }], "the black playhead stays in its bar");
    assert.equal(dot.hasPointerCapture(1), false);

    writes = true;
    const pen = pointer(dot, { x: 160 });
    grab.begin("black", pen);
    assert.equal(pen.stopped, false, "where the Pencil writes, it writes");
    const finger = pointer(dot, { x: 160, pointerType: "touch", pointerId: 2 });
    grab.begin("black", finger);
    assert.equal(finger.stopped, true, "a finger takes the handle there");
    dot.fire("pointermove", pointer(dot, { x: 300, pointerId: 2 }));
    dot.fire("lostpointercapture", pointer(dot, { pointerId: 2 }));
    assert.deepEqual(sent.slice(3).map((message) => message.phase), ["grab", "move", "release"], "a lost pointer lets the playhead go");
    delete globalThis.HTMLElement;
});

test("on the web's roll a press takes the blue knob or the black dot on the bar numbers' edge, the nearer one (#495 UX-08, W3)", () => {
    const apart = playheadKnobs({ xOf: (tick) => tick / 4, header: 24 }, { black: 800, blue: 1600 });
    assert.deepEqual(apart, { black: { x: 200, y: 24 }, blue: { x: 400, y: 24 } });
    assert.equal(knobHeadAt(205, 28, apart, 10), "black");
    assert.equal(knobHeadAt(396, 20, apart, 10), "blue");
    assert.equal(knobHeadAt(300, 24, apart, 10), null, "away from both");
    assert.equal(knobHeadAt(200, 80, apart, 10), null, "down the line is the notes'");
    assert.equal(knobHeadAt(200, 24, { black: apart.black, blue: null }, 10), "black", "no blue playhead without a selection");

    // Together, the black dot hangs under the blue knob, as drawn: each is taken where it shows.
    const together = playheadKnobs({ xOf: (tick) => tick / 4, header: 24 }, { black: 1600, blue: 1600 });
    assert.equal(together.blue.y, 24);
    assert.ok(together.black.y > together.blue.y);
    assert.equal(knobHeadAt(400, 24, together, 22), "blue");
    assert.equal(knobHeadAt(400, together.black.y + 2, together, 22), "black");
});
