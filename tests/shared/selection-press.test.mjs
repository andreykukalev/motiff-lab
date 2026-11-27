// The score's own press while the place rule is off (Scripts/score/selection-press.ts, the selection gesture of v0.6.1:
// #511 2, #582 8 and 9), through SelectionLayer over a small stand-in for the page (one system of four bars): a mouse
// drag selects the bars it crosses at once, a drag from inside extends the selection, a finger's hold on a bar outside
// selects that bar in its place and a cancel leaves the selection; while the place rule is on, or on a handle, the press
// is not taken.
import assert from "node:assert/strict";
import { mock, test } from "node:test";

class FakeElement {
    constructor() {
        this.className = "";
        this.style = {};
        this.dataset = {};
        this.attributes = {};
        this.parent = null;
        this.own = false;
    }
    setAttribute(name, value) {
        this.attributes[name] = value;
    }
    addEventListener() {}
    closest() {
        return this.own ? this : null;
    }
    remove() {
        this.parent?.children.splice(this.parent.children.indexOf(this), 1);
        this.parent = null;
    }
}

class FakeRoot {
    constructor() {
        this.children = [];
        this.listeners = {};
        this.scrollLeft = 0;
        this.scrollTop = 0;
        this.captured = [];
    }
    addEventListener(name, listener) {
        this.listeners[name] = listener;
    }
    removeEventListener(name) {
        delete this.listeners[name];
    }
    getBoundingClientRect() {
        return { left: 0, top: 0, bottom: 1000 };
    }
    setPointerCapture(id) {
        this.captured.push(id);
    }
    append(...elements) {
        for (const element of elements) {
            element.parent = this;
            this.children.push(element);
        }
    }
    querySelectorAll(selector) {
        const names = selector.split(",").map((part) => part.trim().replace(":scope > .", ""));
        return this.children.filter((child) => names.some((name) => child.className.split(" ").includes(name)));
    }
}

globalThis.document = { createElement: () => new FakeElement() };
globalThis.window = { setTimeout: (callback, ms) => setTimeout(callback, ms), addEventListener() {}, removeEventListener() {}, innerWidth: 1200, innerHeight: 1000 };
globalThis.Element = FakeElement;
globalThis.ResizeObserver = class {
    observe() {}
    disconnect() {}
};

const { SelectionLayer } = await import("../../src/shared/wwwroot/js/score/selection-layer.js");
const { HOLD_MS } = await import("../../src/shared/wwwroot/js/selection/selection-gesture.js");

const BAR = 1920;
const staves = [{ n: 1, top: 100, bottom: 180, space: 20 }, { n: 2, top: 220, bottom: 300, space: 20 }];
const bars = [0, 1, 2, 3].map((i) => ({ tick: i * BAR, end: (i + 1) * BAR, left: 100 + i * 200, right: 300 + i * 200, top: 100, bottom: 300, system: 0, page: 1, staves }));
const xOf = (tick) => 100 + (tick / BAR) * 200;
const geometry = {
    bars,
    systems: [{ left: 100, right: 900, top: 100, bottom: 300, bars: [0, 1, 2, 3] }],
    barNear: (x) => bars.find((bar) => x >= bar.left && x < bar.right) ?? (x < 100 ? bars[0] : bars[3]),
    barAt: (tick) => bars.find((bar) => tick >= bar.tick && tick < bar.end),
    band: (start, end) => [{ left: xOf(start), right: xOf(end), top: 92, bottom: 308, page: 1, system: 0, first: true, last: true }],
};

function page({ places = false } = {}) {
    const root = new FakeRoot();
    const said = [];
    const layer = new SelectionLayer(root, {
        geometry: () => geometry,
        penOn: () => false,
        places: () => places,
        chosen: (first, last, byHandle, how) => said.push({ chosen: [first, last], byHandle, how }),
        cleared: () => said.push({ cleared: true }),
        cancelled: () => said.push({ cancelled: true }),
        hint: () => said.push({ hint: true }),
    });
    const event = (x, y, pointerType = "mouse", target = null) => ({ pointerId: 1, pointerType, isPrimary: true, button: 0, clientX: x, clientY: y, target, preventDefault() {} });
    const band = () => root.querySelectorAll(":scope > .sel-band").map((element) => [parseFloat(element.style.left), parseFloat(element.style.width)]);
    return { root, layer, said, event, band };
}

test("a mouse drag selects the bars it crosses at once, the band following it, and the click after it is no click", () => {
    const { root, layer, said, event, band } = page();
    root.listeners.pointerdown(event(150, 140));
    root.listeners.pointermove(event(160, 140));
    assert.deepEqual(band(), [[100, 200]], "the bar pressed shows at once");
    root.listeners.pointermove(event(550, 140));
    assert.deepEqual(band(), [[100, 600]], "the band follows to the bar under the pointer");
    root.listeners.pointerup(event(550, 140));
    assert.deepEqual(said, [{ chosen: [0, 2 * BAR], byHandle: false, how: "create" }]);
    assert.equal(layer.swallowsClick(), true);
    assert.equal(layer.swallowsClick(), false);
});

test("a drag from inside extends the selection on its side, never shrinking it, as an edit", () => {
    const { root, layer, said, event, band } = page();
    layer.setSelection({ start: BAR, end: 2 * BAR, startLabel: "", endLabel: "" });
    root.listeners.pointerdown(event(400, 140));
    root.listeners.pointermove(event(420, 140));
    root.listeners.pointermove(event(650, 140));
    assert.deepEqual(band(), [[300, 400]], "extended to the barline the pointer passed");
    root.listeners.pointerup(event(650, 140));
    assert.deepEqual(said, [{ chosen: [BAR, 2 * BAR], byHandle: true, how: "edit" }]);
});

test("a finger held on a bar outside the selection selects that bar in its place; a cancel leaves the selection as it was", () => {
    mock.timers.enable({ apis: ["setTimeout"] });
    try {
        const held = page();
        held.layer.setSelection({ start: BAR, end: 2 * BAR, startLabel: "", endLabel: "" });
        held.root.listeners.pointerdown(held.event(750, 140, "touch"));
        mock.timers.tick(HOLD_MS);
        assert.deepEqual(held.band(), [[700, 200]], "the bar held shows at once");
        assert.deepEqual(held.root.captured, [1]);
        held.root.listeners.pointerup(held.event(750, 140, "touch"));
        assert.deepEqual(held.said, [{ chosen: [3 * BAR, 3 * BAR], byHandle: false, how: "replace" }]);
        const cancelled = page();
        cancelled.layer.setSelection({ start: BAR, end: 2 * BAR, startLabel: "", endLabel: "" });
        cancelled.root.listeners.pointerdown(cancelled.event(750, 140, "touch"));
        mock.timers.tick(HOLD_MS);
        cancelled.root.listeners.pointercancel(cancelled.event(750, 140, "touch"));
        assert.deepEqual(cancelled.said, []);
        assert.deepEqual(cancelled.band(), [[300, 200]], "the selection shows again");
    } finally {
        mock.timers.reset();
    }
});

test("while the place rule is on, or on a handle, the press is not taken", () => {
    const off = page({ places: true });
    off.root.listeners.pointerdown(off.event(150, 140));
    off.root.listeners.pointermove(off.event(550, 140));
    off.root.listeners.pointerup(off.event(550, 140));
    assert.deepEqual(off.said, []);
    const on = page();
    const handle = new FakeElement();
    handle.own = true;
    on.root.listeners.pointerdown(on.event(150, 140, "mouse", handle));
    on.root.listeners.pointermove(on.event(550, 140));
    on.root.listeners.pointerup(on.event(550, 140));
    assert.deepEqual(on.said, []);
    on.layer.dispose();
    assert.deepEqual(Object.keys(on.root.listeners), [], "dispose stops listening");
});
