// Tests for the playheads of version 0.5 (the experiment Two playheads and modes; product spec two-playheads; v0.5.2), run in
// Node on the compiled modules:   dotnet build src/MotiffLab.Web && node --test tests/web

import assert from "node:assert/strict";
import { test } from "node:test";

import { playsOrPauses } from "../../src/MotiffLab.Web/wwwroot/js/layout/mode-keys.js";
import { edgeOf, playheadsOf, trackOf } from "../../src/MotiffLab.Web/wwwroot/js/score/playheads.js";
import { viewOf } from "../../src/MotiffLab.Web/wwwroot/js/playback/playback-model.js";

test("both apps draw both playheads of the playback model: black at the position, blue held to the selection (#495 UX-10)", () => {
    const selection = { start: 960, end: 1920 };
    const before = viewOf({ selection, loop: false, tick: 480, transport: "playing" });
    assert.deepEqual(playheadsOf(before, selection), { global: 480, selection: 960, globalPlays: true, selectionAtEnd: false, follow: "global" });
    const inside = viewOf({ selection, loop: true, tick: 1200, transport: "stopped" });
    assert.deepEqual(playheadsOf(inside, selection), { global: 1200, selection: 1200, globalPlays: false, selectionAtEnd: false, follow: "global" }, "together inside, the dot hollow when it stands");
    const after = viewOf({ selection, loop: false, tick: 2400, transport: "playing" });
    assert.deepEqual(playheadsOf(after, selection), { global: 2400, selection: 1920, globalPlays: true, selectionAtEnd: true, follow: "global" }, "blue stands at the end, black goes on");
    const none = viewOf({ selection: null, loop: true, tick: 1200, transport: "playing" });
    assert.equal(playheadsOf(none, null).selection, null, "no blue without a selection");
    const waiting = viewOf({ selection, loop: true, tick: 2400, transport: "stopped" });
    assert.deepEqual(playheadsOf(waiting, selection), { global: 2400, selection: 960, globalPlays: false, selectionAtEnd: false, follow: "global" }, "stopped outside with Цикл on, blue waits at the start Play will play from");
});

test("the blue playhead standing at a bar line stands at the right edge of the selection's last bar, not on the next line", () => {
    const last = { tick: 960, end: 1920, left: 100, right: 300, top: 50, bottom: 150 };
    const next = { tick: 1920, end: 2880, left: 40, right: 240, top: 250, bottom: 350 };
    const atEnd = { x: 40, top: 250, bottom: 350, bar: next };
    const beforeEnd = { x: 296, top: 50, bottom: 150, bar: last };
    assert.deepEqual(edgeOf(atEnd, beforeEnd, 1920), { x: 300, top: 50, bottom: 150, bar: last });
    const midBar = { x: 180, top: 50, bottom: 150, bar: last };
    assert.deepEqual(edgeOf(midBar, beforeEnd, 1440), midBar, "an end inside a bar stays where it lies");
    assert.deepEqual(edgeOf(undefined, beforeEnd, 1920), { x: 300, top: 50, bottom: 150, bar: last }, "the piece's end");
});

test("the track runs under the lowest ink of its line, across its bar, filled up to the place (#479 item 19)", () => {
    const bar = { left: 100, right: 300, top: 50, bottom: 150 };
    assert.deepEqual(trackOf({ x: 160, top: 60, bottom: 140, bar }, 180), { left: 103, top: 190, width: 194, filled: 57 });
    assert.equal(trackOf({ x: 160, top: 60, bottom: 140, bar }, 100).top, 150, "never over the staves");
    assert.equal(trackOf({ x: 90, top: 60, bottom: 140, bar }, 180).filled, 0);
    assert.equal(trackOf({ x: 400, top: 60, bottom: 140, bar }, 180).filled, 194);
});

const space = (extra = {}) => ({ key: " ", code: "Space", altKey: false, ctrlKey: false, metaKey: false, shiftKey: false, defaultPrevented: false, target: { closest: () => null }, ...extra });

test("Space plays or pauses outside a field or a dialog, and not when the roll took it first", () => {
    assert.equal(playsOrPauses(space()), true);
    assert.equal(playsOrPauses(space({ defaultPrevented: true })), false);
    assert.equal(playsOrPauses(space({ ctrlKey: true })), false);
    assert.equal(playsOrPauses(space({ target: { closest: () => ({}) } })), false);
    assert.equal(playsOrPauses(space({ key: "a" })), false);
});

/** The few parts of the DOM the playheads' marks use. */
class FakeMark {
    constructor() {
        this.className = "";
        this.style = {};
        this.hidden = false;
        this.children = [];
        this.parentElement = null;
        this.listeners = {};
        const owner = this;
        this.classList = {
            add: (name) => { owner.className = [...new Set([...owner.className.split(" ").filter(Boolean), name])].join(" "); },
            remove: (name) => { owner.className = owner.className.split(" ").filter((one) => one && one !== name).join(" "); },
            toggle: (name, on) => (on ? owner.classList.add(name) : owner.classList.remove(name)),
            contains: (name) => owner.className.split(" ").includes(name),
        };
    }

    setAttribute() {}

    addEventListener(name, listener) {
        this.listeners[name] = listener;
    }

    append(...children) {
        for (const child of children) {
            child.parentElement = this;
            this.children.push(child);
        }
    }

    remove() {
        this.parentElement = null;
    }
}

test("held, the black playhead stands as a line across the staves at its place, and let go the line fades (#543 5)", async () => {
    globalThis.document = { createElement: () => new FakeMark() };
    try {
        const { ScorePlayheadMarks, CURSOR_CLASS, HELD_CLASS } = await import("../../src/MotiffLab.Web/wwwroot/js/score/playheads.js");
        const host = new FakeMark();
        const bar = { left: 100, right: 300, top: 50, bottom: 150, system: 0 };
        const marks = new ScorePlayheadMarks(host, { at: (tick) => ({ x: 100 + tick / 10, top: 60, bottom: 140, bar }), lineBottom: () => 150 });
        const cursor = () => host.children.find((child) => child.className.split(" ").includes(CURSOR_CLASS));
        marks.place({ global: 480, selection: null, globalPlays: false });
        assert.equal(cursor(), undefined, "hidden by default: the score is read without it");
        marks.place({ global: 480, selection: null, globalPlays: false, held: true });
        assert.ok(cursor().classList.contains(HELD_CLASS));
        assert.equal(cursor().style.transform, "translate(147px, 60px)", "at the place, from the top staff");
        assert.equal(cursor().style.height, "80px", "across the staves");
        marks.place({ global: 1440, selection: null, globalPlays: true, held: true });
        assert.equal(cursor().style.transform, "translate(243px, 60px)", "it follows the pen, playing or not");
        marks.place({ global: 1440, selection: null, globalPlays: true });
        assert.equal(cursor().classList.contains(HELD_CLASS), false, "let go, it fades where it was");
        assert.equal(cursor().style.transform, "translate(243px, 60px)");
    } finally {
        delete globalThis.document;
    }
});
