// Tests for the roll's viewport (Scripts/roll/roll-viewport.ts, W3.12), run in Node on the compiled output over a host
// stub: where ticks and rows are on the roll, and which zoom steps it reports left (OnRollZoom, #479 item 23).
import assert from "node:assert/strict";
import test from "node:test";
import { RollModel } from "../../src/shared/wwwroot/js/roll/roll-model.js";
import { GUTTER, RollViewport } from "../../src/shared/wwwroot/js/roll/roll-viewport.js";

/** A piece of `bars` bars of 3/4 (1440 ticks), one quarter note in each. */
function piece(bars) {
    const ids = [];
    const notes = [];
    const packed = [];
    for (let bar = 0; bar < bars; bar++) {
        ids.push(`n${bar}`);
        notes.push(bar * 1440, 480, 60 + (bar % 12), 0, 0, 2, 0);
        packed.push(bar * 1440, 1440, 480, bar + 1);
    }
    return RollModel.fromPayload({ ids, notes, bars: packed, end: bars * 1440 });
}

/** A viewport over a roll `width` wide and `height` tall, read by touch as on the iPad. */
function viewport(model, { width = 542, height = 264 } = {}) {
    return new RollViewport({
        width: () => width,
        height: () => height,
        header: () => 24,
        model: () => model,
        touchReading: () => true,
        playhead: () => 0,
        zoomed: () => {},
        changed: () => {},
    });
}

test("a tick and a row are where the zoom and the view put them, and back", () => {
    const model = piece(8);
    const view = viewport(model);
    assert.equal(view.xOf(0), GUTTER);
    assert.equal(view.xOf(480), GUTTER + 48, "a quarter is 48 pixels at 100 %");
    assert.equal(view.tickAtX(GUTTER + 96), 960);
    assert.equal(view.tickAtX(0), 0, "never before the piece's start");
    const { high, row } = view.pitchRange();
    assert.equal(view.pitchAt(24), high, "the top row under the bar numbers");
    assert.equal(view.pitchAt(24 + row * 2 + 1), high - 2);
    assert.equal(view.yOf(high - 2), 24 + row * 2);
    assert.equal(view.exactPitchAt(24 + row / 2), high, "a row's centre is its pitch");
});

test("the time zoom's + is offered only while it shows fewer bars: not with one bar in view or less", () => {
    const model = piece(40);
    const view = viewport(model);
    assert.equal(view.axisZoom(model, false).timeIn, true, "several bars in view");
    for (let step = 0; step < 20; step++) {
        view.zoomTime(model, 1);
    }
    const one = view.axisZoom(model, false);
    assert.equal(one.bars, 1);
    assert.equal(one.timeIn, false, "+ would show one bar again (stepBars never goes under 1)");
    assert.equal(one.timeOut, true);
    // A pinch may lay less than a bar across the roll: + would zoom out there.
    view.zoomTo(view.zoom * 3, view.rowScale, 0);
    assert.equal(view.axisZoom(model, false).timeIn, false, "less than a bar in view");
    const before = view.zoom;
    view.zoomTime(model, -1);
    assert.ok(view.zoom < before, "− still shows more");
});

test("a piece without bars offers no step of the time zoom, as neither would do anything", () => {
    const model = RollModel.fromPayload({ ids: [], notes: [], bars: [], end: 0 });
    const view = viewport(model);
    const zoom = view.axisZoom(model, false);
    assert.deepEqual([zoom.bars, zoom.timeIn, zoom.timeOut], [0, false, false]);
    const before = view.zoom;
    view.zoomTime(model, 1);
    view.zoomTime(model, -1);
    assert.equal(view.zoom, before);
});
