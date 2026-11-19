// Selection by the place of the touch on the score's page (v0.6.2, #601 H items 1-4, 8, 9): Scripts/score/
// selection-place-layer.ts over a small stand-in for the page (one system of four bars, two staves). The Pencil draws a new
// passage from outside (the old one dashed meanwhile), moves an edge from its grip, extends from inside, taps a bar, a note
// head or the margin, double taps inside to zoom; a finger beside it scrolls; with the rule off nothing is taken. Since
// v0.6.3 (#608) a new passage is drawn with a dashed rectangle, from the margin too, and the stylus' touches never move the
// page while the rule is on.
import assert from "node:assert/strict";
import { test } from "node:test";

class FakeElement {
    constructor(own = false) {
        this.own = own;
        this.className = "";
        this.style = {};
        Object.defineProperty(this.style, "setProperty", { value: (name, value) => (this.style[name] = value) });
        this.attributes = {};
        this.parent = null;
    }
    setAttribute(name, value) {
        this.attributes[name] = value;
    }
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

const { PlaceLayer } = await import("../../src/shared/wwwroot/js/score/selection-place-layer.js");
const { edgeHandles } = await import("../../src/shared/wwwroot/js/score/selection-places.js");

const BAR = 1920;
const staves = [{ n: 1, top: 100, bottom: 180 }, { n: 2, top: 220, bottom: 300 }];
const bars = [0, 1, 2, 3].map((i) => ({ tick: i * BAR, end: (i + 1) * BAR, left: 100 + i * 200, right: 300 + i * 200, top: 100, bottom: 300, system: 0, staves }));
const xOf = (tick) => 100 + (tick / BAR) * 200;
const geometry = {
    bars,
    systems: [{ left: 100, right: 900, top: 100, bottom: 300, bars: [0, 1, 2, 3] }],
    notes: [{ id: "n1", cx: 150, cy: 140 }],
    barNear: (x) => bars.find((bar) => x >= bar.left && x < bar.right) ?? (x < 100 ? bars[0] : bars[3]),
    barAt: (tick) => bars.find((bar) => tick >= bar.tick && tick < bar.end),
    band: (start, end, _padding, only) => {
        const lanes = only === undefined ? staves : staves.filter((staff) => only.includes(staff.n));
        return [{ left: xOf(start), right: xOf(end), top: lanes[0].top - 8, bottom: lanes[lanes.length - 1].bottom + 8, system: 0 }];
    },
};

function page({ selection = { start: BAR, end: 2 * BAR, hand: null }, on = true, view, grid, edges } = {}) {
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
        view: view === undefined ? undefined : () => view,
        grid: grid === undefined ? undefined : () => grid,
        edges,
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

test("an edge's circle is drawn where its handle takes a press (edgeHandles), so the two cannot drift apart", () => {
    const { root, layer } = page();
    layer.decorate();
    const drawn = root.children.filter((child) => child.className.startsWith("sel-edge"))
        .map((edge) => ({ x: parseFloat(edge.style.left), y: parseFloat(edge.style.top) + parseFloat(edge.style["--handle-y"]) }));
    const band = root.children.find((child) => child.className === "sel-band");
    const box = { left: parseFloat(band.style.left), top: parseFloat(band.style.top), right: parseFloat(band.style.left) + parseFloat(band.style.width), bottom: parseFloat(band.style.top) + parseFloat(band.style.height) };
    assert.deepEqual(drawn, edgeHandles([box]));
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

test("the pen's colour bars (finding 31): with no selection and no edges every press is a new passage, as Select's", () => {
    // A drag colours what its rectangle covers, a tap a bar or a note head; the selection's band keeps no edges of its own.
    const drawn = page({ selection: null, edges: false });
    drawn.gesture([[650, 140], [860, 260]]);
    assert.deepEqual(drawn.said.find((step) => step.chosen), { chosen: { start: 2 * BAR, end: 4 * BAR, hand: null }, how: "create" });
    const bar = page({ selection: null, edges: false });
    bar.gesture([[750, 260]]);
    assert.deepEqual(bar.said.find((step) => step.chosen), { chosen: { start: 3 * BAR, end: 4 * BAR, hand: null }, how: "create" });
    const note = page({ selection: null, edges: false });
    note.gesture([[152, 143]]);
    assert.deepEqual(note.said.find((step) => step.picked), { picked: "n1", how: "create" });
    const banded = page({ edges: false });
    banded.layer.decorate();
    assert.equal(banded.classes().some((name) => name.startsWith("sel-edge")), false, "no edges over a band it does not own");
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

test("a new passage is drawn with a dashed rectangle from the press to the stylus, following it, gone when it lifts", () => {
    const { root, said } = page();
    root.listeners.pointerdown({ pointerId: 1, pointerType: "pen", isPrimary: true, button: 0, clientX: 650, clientY: 140, target: null });
    root.listeners.pointermove({ pointerId: 1, pointerType: "pen", clientX: 720, clientY: 160 });
    const trace = () => root.children.filter((child) => child.className === "sel-trace");
    assert.equal(trace().length, 1);
    assert.deepEqual(trace()[0].style, { left: "650px", top: "140px", width: "70px", height: "20px" });
    root.listeners.pointermove({ pointerId: 1, pointerType: "pen", clientX: 600, clientY: 270 });
    assert.equal(trace().length, 1, "one rectangle");
    assert.deepEqual(trace()[0].style, { left: "600px", top: "140px", width: "50px", height: "130px" }, "it follows the stylus, any way");
    assert.deepEqual(said.at(-1), { show: { start: 2 * BAR, end: 3 * BAR, hand: null } }, "the band grows under it");
    root.listeners.pointerup({ pointerId: 1, pointerType: "pen", clientX: 600, clientY: 270, timeStamp: 1 });
    assert.equal(trace().length, 0);
});

test("a stroke from the margin draws a rectangle and selects what it covers (Andrei: «Да, рисует»); a tap there clears", () => {
    const { said, gesture } = page();
    gesture([[60, 140], [620, 150]]);
    assert.deepEqual(said.find((step) => step.chosen), { chosen: { start: 0, end: 3 * BAR, hand: "right" }, how: "replace" });
    const from = page();
    from.gesture([[500, 40], [760, 320]]);
    assert.deepEqual(from.said.find((step) => step.chosen), { chosen: { start: 2 * BAR, end: 4 * BAR, hand: null }, how: "replace" }, "from above the system");
});

test("a rectangle that reaches no music changes nothing: the selection shows as it is, no ghost", () => {
    const { said, root } = page();
    root.listeners.pointerdown({ pointerId: 1, pointerType: "pen", isPrimary: true, button: 0, clientX: 300, clientY: 600, target: null });
    root.listeners.pointermove({ pointerId: 1, pointerType: "pen", clientX: 500, clientY: 650 });
    assert.ok(root.children.some((child) => child.className === "sel-trace"));
    assert.equal(root.children.some((child) => child.className === "sel-ghost"), false);
    assert.deepEqual(said.at(-1), { show: null });
    root.listeners.pointerup({ pointerId: 1, pointerType: "pen", clientX: 500, clientY: 650, timeStamp: 1 });
    assert.equal(said.some((step) => step.chosen || step.cleared), false);
});

test("the same stroke gives the same passage every time", () => {
    const results = [];
    for (let i = 0; i < 3; i++) {
        const { said, gesture } = page();
        gesture([[640, 150], [700, 155], [770, 165]]);
        results.push(said.find((step) => step.chosen));
    }
    assert.deepEqual(results[1], results[0]);
    assert.deepEqual(results[2], results[0]);
});

function touch(type, target = new FakeElement()) {
    const event = { cancelable: true, target, changedTouches: [{ touchType: type }], defaultPrevented: false };
    event.preventDefault = () => (event.defaultPrevented = true);
    return event;
}

test("while the rule is on the stylus' touches never move the page; a finger's do, and a bookmark still takes the stylus' tap", () => {
    const { root } = page();
    const start = touch("stylus");
    root.listeners.touchstart(start);
    assert.equal(start.defaultPrevented, true, "the stylus' touch is kept from WebKit's scrolling and click");
    const move = touch("stylus");
    root.listeners.touchmove(move);
    assert.equal(move.defaultPrevented, true);
    const finger = touch("direct");
    root.listeners.touchstart(finger);
    root.listeners.touchmove(finger);
    assert.equal(finger.defaultPrevented, false, "a finger scrolls");
    const bookmark = touch("stylus", new FakeElement(true));
    root.listeners.touchstart(bookmark);
    assert.equal(bookmark.defaultPrevented, false);
    const off = page({ on: false });
    const navigate = touch("stylus");
    off.root.listeners.touchstart(navigate);
    off.root.listeners.touchmove(navigate);
    assert.equal(navigate.defaultPrevented, false, "in Navigate the stylus scrolls");
});

test("the click after a stylus' tap is swallowed whenever it comes, until the next press", async () => {
    const { layer, gesture, root } = page();
    gesture([[750, 260]]);
    await new Promise((resolve) => setTimeout(resolve, 5));
    assert.equal(layer.swallowsClick(), true, "WebKit's late click is no tap on a beat");
    gesture([[750, 260]]);
    root.listeners.pointerdown({ pointerId: 2, pointerType: "touch", isPrimary: true, button: 0, clientX: 300, clientY: 600, target: null });
    assert.equal(layer.swallowsClick(), false, "a new press's click is its own");
});

test("a playhead's handle at the selection's edge takes the press; only on the edge's own handle the edge wins (0.8 finding 1)", () => {
    const { layer } = page();
    const at = (x, y, pointerType = "pen") => ({ pointerType, clientX: x, clientY: y });
    assert.equal(layer.handleAt(at(300, 200)), true, "the start edge's circle, at the middle of the band");
    assert.equal(layer.handleAt(at(500, 200)), true, "the end edge's circle");
    assert.equal(layer.handleAt(at(300, 90)), false, "at the edge's top, where the blue playhead's knob stands: the playhead");
    assert.equal(layer.handleAt(at(300, 300)), false, "down the edge, away from its circle: the playhead");
    assert.equal(layer.handleAt(at(400, 200)), false, "inside");
    assert.equal(layer.handleAt(at(300, 200, "touch")), false, "a finger takes the playhead");
    assert.equal(page({ on: false }).layer.handleAt(at(300, 200)), false);
});

test("the page scrolls by itself only at the edges of the part of the window the score shows, toward where the stylus went", () => {
    const scrolled = [];
    const frames = [];
    Object.assign(window, { requestAnimationFrame: (callback) => frames.push(callback), scrollBy: (_x, y) => scrolled.push(y) });
    try {
        const { root } = page({ view: { top: 80, bottom: 700 } });
        root.listeners.pointerdown({ pointerId: 1, pointerType: "pen", isPrimary: true, button: 0, clientX: 650, clientY: 140, target: null });
        root.listeners.pointermove({ pointerId: 1, pointerType: "pen", clientX: 700, clientY: 150 });
        frames.shift()?.();
        assert.deepEqual(scrolled, [], "away from the view's edges");
        root.listeners.pointerup({ pointerId: 1, pointerType: "pen", clientX: 700, clientY: 150, timeStamp: 1 });
        root.listeners.pointerdown({ pointerId: 1, pointerType: "pen", isPrimary: true, button: 0, clientX: 650, clientY: 140, target: null });
        root.listeners.pointermove({ pointerId: 1, pointerType: "pen", clientX: 700, clientY: 690 });
        frames.shift()?.();
        assert.ok(scrolled.length === 1 && scrolled[0] > 0, "at the bottom of the view, above the pill");
    } finally {
        Object.assign(window, { requestAnimationFrame: () => 0, scrollBy() {} });
    }
});

// The sixteenth grid of v0.6.4 (selection-grid.ts) as the iPad's page passes it.
const SIXTEENTHS = Array.from({ length: (4 * BAR) / 120 + 1 }, (_, i) => i * 120);

test("with the host's grid an edge stops on any sixteenth, and a rectangle covers part of a bar", () => {
    const { said, gesture } = page({ grid: SIXTEENTHS });
    // The end edge at x 500 moved 30 px right: 30 / 200 of a bar is 288 ticks, the nearest sixteenth 240.
    gesture([[500, 290], [530, 290]]);
    assert.deepEqual(said.find((step) => step.chosen), { chosen: { start: BAR, end: 2 * BAR + 240, hand: null }, how: "edit" });
    const drawn = page({ grid: SIXTEENTHS, selection: null });
    drawn.gesture([[612, 140], [640, 150]]);
    const chosen = drawn.said.find((step) => step.chosen).chosen;
    assert.ok(chosen.start > 2 * BAR && chosen.end < 3 * BAR, "inside one bar");
    assert.equal(chosen.start % 120, 0);
    assert.equal(chosen.end % 120, 0);
});

test("an edge let go is where a press takes it again before the owner answers, never back where it was", () => {
    const { said, gesture, layer } = page({ grid: SIXTEENTHS });
    gesture([[500, 290], [600, 290]]);
    assert.deepEqual(said.filter((step) => step.chosen).at(-1).chosen, { start: BAR, end: 2 * BAR + 960, hand: null });
    // The owner has not answered: the band shows the new end at x 600, and the end is taken there and moved on.
    gesture([[600, 290], [650, 290]]);
    assert.deepEqual(said.filter((step) => step.chosen).at(-1).chosen, { start: BAR, end: 2 * BAR + 1440, hand: null });
    layer.settle();
});
