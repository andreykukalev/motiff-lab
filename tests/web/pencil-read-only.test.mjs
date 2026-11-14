// The pencil layer showing marks it does not write (version 0.4): the strokes another device writes on a PDF's pages, as
// the online copy brings them (Projects/Project.cs, WritesDocumentMarks). Run in Node on the compiled module, with only as
// much of a page as the layer touches:
//   dotnet build src/MotiffLab.Web && node --test tests/web

import assert from "node:assert/strict";
import { test } from "node:test";

function element() {
    const classes = new Set();
    return {
        style: {},
        classList: {
            add: (name) => classes.add(name),
            remove: (name) => classes.delete(name),
            toggle: (name, on) => (on ? classes.add(name) : classes.delete(name)),
            contains: (name) => classes.has(name),
        },
        listeners: {},
        addEventListener(type, listener) {
            this.listeners[type] = listener;
        },
        removeEventListener() {},
        setAttribute() {},
        replaceChildren() {},
        append() {},
        appendChild: (child) => child,
        remove() {},
        setPointerCapture() {},
        hasPointerCapture: () => false,
        releasePointerCapture() {},
        getBoundingClientRect: () => ({ left: 0, top: 0 }),
        scrollLeft: 0,
        scrollTop: 0,
        scrollWidth: 800,
        scrollHeight: 1100,
    };
}

globalThis.document = { createElementNS: () => element() };
globalThis.window = { addEventListener() {}, removeEventListener() {}, setTimeout: () => 0 };
globalThis.Element = class {};

const { PencilLayer } = await import("../../src/MotiffLab.Web/wwwroot/js/score/pencil-layer.js");
const { writeMarks } = await import("../../src/MotiffLab.Web/wwwroot/js/score/pencil-marks.js");

const page = { tick: 1, left: 0, right: 800, top: 0, bottom: 1100, staffTop: 0, space: 800 / 120 };
const stroke = (id) => ({ id, tool: "pen", colour: "graphite", size: 0.16, bar: 1, u: 0.25, v: 3, points: [0, 0, 0.5, 1, 1, 0.5], pressure: 0.5 });

function layer() {
    const host = element();
    const saved = [];
    const states = [];
    const pencil = new PencilLayer(host, {
        frames: () => [page],
        tap() {},
        changed: (state) => states.push(state),
        marksChanged: (text) => saved.push(text),
    });
    pencil.relayout();
    return { host, pencil, saved, states };
}

const press = (host, pointerType) => {
    let prevented = false;
    host.listeners.pointerdown({ pointerType, button: 0, buttons: 1, pointerId: 1, clientX: 100, clientY: 100, pressure: 0.5, target: null, shiftKey: false, preventDefault: () => (prevented = true) });
    return prevented;
};

test("marks shown only are drawn as they come, and neither the pen, the mouse nor the tools change them", async () => {
    const { host, pencil, saved } = layer();
    pencil.setReadOnly(true);
    await pencil.setKey("pdf:p1", writeMarks([stroke("k1")]));
    assert.equal(pencil.state().marks, 1);

    pencil.setOn(true);
    assert.equal(pencil.state().on, false);
    pencil.setTool("eraser", null);
    assert.equal(pencil.state().on, false);
    assert.equal(press(host, "pen"), false);
    assert.equal(press(host, "mouse"), false);
    pencil.clear();
    assert.equal(pencil.state().marks, 1);

    // What the iPad wrote next comes in the same way, and nothing goes back.
    pencil.setInk(writeMarks([stroke("k1"), stroke("k2")]));
    assert.equal(pencil.state().marks, 2);
    assert.deepEqual(saved, []);
});

test("the pen writes again once the marks are this device's", async () => {
    const { host, pencil } = layer();
    pencil.setReadOnly(true);
    await pencil.setKey("pdf:p1", writeMarks([stroke("k1")]));
    pencil.setReadOnly(false);
    pencil.setOn(true);
    assert.equal(pencil.state().on, true);
    assert.equal(press(host, "pen"), true);
});

// The pen palette's Redo (#533 section 9): what Undo took back comes again, a new step drops it, and new marks from outside
// leave nothing to take back or do again.
test("Redo does again what Undo took back, until a new step", async () => {
    const { pencil } = layer();
    await pencil.setKey("pdf:p2", writeMarks([stroke("r1"), stroke("r2")]));
    assert.equal(pencil.state().canRedo, false);
    pencil.clear();
    assert.equal(pencil.state().marks, 0);
    pencil.undoLast();
    assert.deepEqual([pencil.state().marks, pencil.state().canUndo, pencil.state().canRedo], [2, false, true]);
    pencil.redoLast();
    assert.deepEqual([pencil.state().marks, pencil.state().canUndo, pencil.state().canRedo], [0, true, false]);
    pencil.undoLast();
    assert.equal(pencil.state().marks, 2);
    pencil.clear();
    assert.equal(pencil.state().canRedo, false, "a new step drops what Redo had");
    pencil.undoLast();
    pencil.setInk(writeMarks([stroke("r3")]));
    assert.deepEqual([pencil.state().canUndo, pencil.state().canRedo], [false, false]);
});
