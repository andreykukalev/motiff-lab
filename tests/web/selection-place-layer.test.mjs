// Selection by the place of the touch on the score's page (v0.6.2, #601 H items 1-4, 8, 9): Scripts/score/
// selection-place-layer.ts over a small stand-in for the page (one system of four bars, two staves). The Pencil draws a new
// passage from outside (the old one dashed meanwhile), moves an edge from its grip, extends from inside, taps a bar, a note
// head or the margin, double taps inside to zoom; a finger beside it scrolls; with the rule off nothing is taken.
import assert from "node:assert/strict";
import { test } from "node:test";

class FakeElement {
    constructor() {
        this.className = "";
        this.style = {};
        this.attributes = {};
        this.parent = null;
    }
    setAttribute(name, value) {
        this.attributes[name] = value;
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
    }
    addEventListener(name, listener) {
        this.listeners[name] = listener;
    }
    removeEventListener() {}
    getBoundingClientRect() {
        return { left: 0, top: 0 };
    }
    setPointerCapture() {}
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
globalThis.window = {
    addEventListener() {},
    removeEventListener() {},
    setTimeout: (callback, ms) => setTimeout(callback, ms),
    requestAnimationFrame: () => 0,
    cancelAnimationFrame() {},
    scrollBy() {},
    innerHeight: 1000,
};
globalThis.Element = FakeElement;

const { PlaceLayer } = await import("../../src/MotiffLab.Web/wwwroot/js/score/selection-place-layer.js");

const BAR = 1920;
const staves = [{ n: 1, top: 100, bottom: 180 }, { n: 2, top: 220, bottom: 300 }];
const bars = [0, 1, 2, 3].map((i) => ({ tick: i * BAR, end: (i + 1) * BAR, left: 100 + i * 200, right: 300 + i * 200, top: 100, bottom: 300, system: 0, staves }));
const xOf = (tick) => 100 + (tick / BAR) * 200;
const geometry = {
    bars,
    systems: [{ left: 100, right: 900, top: 100, bottom: 300 }],
    notes: [{ id: "n1", cx: 150, cy: 140 }],
    barNear: (x) => bars.find((bar) => x >= bar.left && x < bar.right) ?? (x < 100 ? bars[0] : bars[3]),
    barAt: (tick) => bars.find((bar) => tick >= bar.tick && tick < bar.end),
    band: (start, end, _padding, only) => {
        const lanes = only === undefined ? staves : staves.filter((staff) => only.includes(staff.n));
        return [{ left: xOf(start), right: xOf(end), top: lanes[0].top - 8, bottom: lanes[lanes.length - 1].bottom + 8, system: 0 }];
    },
};

function page({ selection = { start: BAR, end: 2 * BAR, hand: null }, on = true } = {}) {
    const root = new FakeRoot();
    const said = [];
    const drawBand = (range) => {
        for (const band of root.querySelectorAll(":scope > .sel-band")) {
            band.remove();
        }
        if (range !== null) {
            const band = new FakeElement();
            band.className = "sel-band";
            const [run] = geometry.band(range.start, range.end);
            Object.assign(band.style, { left: `${run.left}px`, top: `${run.top}px`, width: `${run.right - run.left}px`, height: `${run.bottom - run.top}px` });
            root.append(band);
        }
    };
    drawBand(selection);
    const layer = new PlaceLayer(root, {
        geometry: () => geometry,
        beats: { lines: () => null, lineX: (_bar, line) => xOf(line) },
        on: () => on,
        rule: (type) => (type === "touch" ? "longPress" : "now"),
        selection: () => selection,
        show: (range) => {
            said.push({ show: range });
            drawBand(range ?? selection);
        },
        chosen: (range, how) => said.push({ chosen: range, how }),
        picked: (id, how) => said.push({ picked: id, how }),
        cleared: () => said.push({ cleared: true }),
        zoom: () => said.push({ zoom: true }),
    });
    let time = 0;
    const event = (x, y, pointerType = "pen") => ({ pointerId: 1, pointerType, isPrimary: true, button: 0, clientX: x, clientY: y, target: null, timeStamp: (time += 100) });
    const gesture = (points, pointerType) => {
        root.listeners.pointerdown(event(...points[0], pointerType));
        for (const point of points.slice(1)) {
            root.listeners.pointermove(event(...point, pointerType));
        }
        root.listeners.pointerup(event(...points[points.length - 1], pointerType));
    };
    return { root, layer, said, gesture, classes: () => root.children.map((child) => child.className) };
}

test("the Pencil draws a new passage from outside at once, the old one dashed meanwhile, and it replaces the selection", () => {
    const { root, said, layer } = page();
    root.listeners.pointerdown({ pointerId: 1, pointerType: "pen", isPrimary: true, button: 0, clientX: 650, clientY: 140, target: null });
    root.listeners.pointermove({ pointerId: 1, pointerType: "pen", clientX: 850, clientY: 150 });
    assert.ok(root.children.some((child) => child.className === "sel-ghost"), "the old selection is dashed");
    assert.equal(layer.active(), true);
    root.listeners.pointerup({ pointerId: 1, pointerType: "pen", clientX: 850, clientY: 150, timeStamp: 1 });
    const chosen = said.find((step) => step.chosen);
    assert.deepEqual(chosen, { chosen: { start: 2 * BAR, end: 4 * BAR, hand: "right" }, how: "replace" }, "a stroke within the upper staff takes the right hand");
    assert.equal(root.children.some((child) => child.className === "sel-ghost"), false);
    assert.equal(layer.swallowsClick(), true, "the click after it is no tap on the page");
});

test("a stroke across the gap between the staves takes both hands", () => {
    const { said, gesture } = page();
    gesture([[650, 140], [860, 260]]);
    assert.equal(said.find((step) => step.chosen).chosen.hand, null);
});

test("an edge's grip, anywhere along the band's height, moves that edge", () => {
    const { said, gesture, root } = page();
    gesture([[505, 290], [700, 290]]);
    assert.deepEqual(said.find((step) => step.chosen), { chosen: { start: BAR, end: 3 * BAR, hand: null }, how: "edit" });
    assert.ok(root.children.filter((child) => child.className.startsWith("sel-edge")).length === 2, "the edges are drawn over the band again");
});

test("a move from inside extends the selection, the addition dashed while it goes", () => {
    const { said, root } = page();
    root.listeners.pointerdown({ pointerId: 1, pointerType: "pen", isPrimary: true, button: 0, clientX: 400, clientY: 200, target: null });
    root.listeners.pointermove({ pointerId: 1, pointerType: "pen", clientX: 130, clientY: 200 });
    assert.ok(root.children.some((child) => child.className === "sel-add"));
    root.listeners.pointerup({ pointerId: 1, pointerType: "pen", clientX: 130, clientY: 200, timeStamp: 1 });
    assert.deepEqual(said.find((step) => step.chosen), { chosen: { start: 0, end: 2 * BAR, hand: null }, how: "edit" });
});

test("taps: a bar outside selects it, a note head selects the note, the margin clears, inside nothing", () => {
    const bar = page();
    bar.gesture([[750, 260]]);
    assert.deepEqual(bar.said.find((step) => step.chosen), { chosen: { start: 3 * BAR, end: 4 * BAR, hand: null }, how: "replace" });
    const note = page();
    note.gesture([[152, 143]]);
    assert.deepEqual(note.said.find((step) => step.picked), { picked: "n1", how: "replace" });
    const margin = page();
    margin.gesture([[500, 600]]);
    assert.ok(margin.said.some((step) => step.cleared));
    const inside = page();
    inside.gesture([[400, 200]]);
    assert.equal(inside.said.some((step) => step.chosen || step.cleared || step.zoom), false);
});

test("a double tap inside zooms to the selection", () => {
    const { said, gesture } = page();
    gesture([[400, 200]]);
    gesture([[402, 201]]);
    assert.ok(said.some((step) => step.zoom));
});

test("a finger beside the Pencil scrolls: its move takes nothing, and its tap is the page's", () => {
    const { said, gesture, layer } = page();
    gesture([[650, 140], [850, 150]], "touch");
    assert.equal(said.some((step) => step.chosen), false);
    gesture([[750, 260]], "touch");
    assert.equal(said.some((step) => step.chosen), false);
    assert.equal(layer.swallowsClick(), false);
});

test("with the rule off the layer takes no press and draws no edges", () => {
    const { said, gesture, root, layer } = page({ on: false });
    gesture([[650, 140], [850, 150]]);
    layer.decorate();
    assert.equal(said.length, 0);
    assert.equal(root.children.some((child) => child.className.startsWith("sel-edge")), false);
});
