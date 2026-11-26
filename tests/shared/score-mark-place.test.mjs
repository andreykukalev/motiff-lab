// Tests for the mark a card shows at, followed on the iPad's score page (Scripts/score/ipad-page/page-mark-place.ts,
// r-1, v1.4 a-1): a scroll or a new layout tells Swift where it is now, in the web view's points (its top too, for a card
// opened above it), so the card stays at it; a mark that leaves the visible part of the page tells Swift so and is no
// longer followed. Run in Node on the compiled modules:   dotnet build src/shared && node --test tests/shared
// The wiring (the page's scroll and resize listeners, a tap on a mark, a phrase's number held) needs the page's DOM, which
// these tests do not have: the page scenarios run the real page.

import assert from "node:assert/strict";
import { test } from "node:test";

const posted = [];
globalThis.window = { innerWidth: 1000, innerHeight: 800, webkit: { messageHandlers: { score: { postMessage: (message) => posted.push(message) } } } };
const { followMark, forgetMark, tellMarkMoved, windowPoint } = await import("../../src/shared/wwwroot/js/score/ipad-page/page-mark-place.js");

/** A box 20 wide and 20 high whose bottom left is at `left`, `bottom`. */
function box(left, bottom) {
    return { left, right: left + 20, top: bottom - 20, bottom };
}

/** A mark on the page at `left`, `bottom`, connected until a test takes it away. */
function mark(left, bottom) {
    return { isConnected: true, box: box(left, bottom), getBoundingClientRect() { return this.box; } };
}

/** Follows `label` as a tap on it does: from where it is. */
function follow(label, find = () => null) {
    followMark(label, find, windowPoint(label.box));
}

test("a mark followed tells Swift where it is once it has moved, and only then", () => {
    posted.length = 0;
    const label = mark(40, 300);
    follow(label);
    tellMarkMoved();
    assert.deepEqual(posted, [], "where it was tapped Swift knows already");
    label.box = box(40, 180);
    tellMarkMoved();
    tellMarkMoved();
    assert.deepEqual(posted, [{ markMoved: { left: 40, top: 160, bottom: 180 } }], "once for each new place");
});

test("a mark drawn anew is found once and kept; one gone is forgotten", () => {
    posted.length = 0;
    const old = mark(10, 30);
    const again = mark(12, 90);
    let looked = 0;
    follow(old, () => {
        looked += 1;
        return again;
    });
    old.isConnected = false;
    tellMarkMoved();
    again.box = box(12, 60);
    tellMarkMoved();
    assert.deepEqual(posted, [{ markMoved: { left: 12, top: 70, bottom: 90 } }, { markMoved: { left: 12, top: 40, bottom: 60 } }]);
    assert.equal(looked, 1, "not looked for again each frame");

    posted.length = 0;
    const gone = mark(0, 20);
    let asked = 0;
    follow(gone, () => {
        asked += 1;
        return null;
    });
    gone.isConnected = false;
    tellMarkMoved();
    tellMarkMoved();
    assert.deepEqual(posted, []);
    assert.equal(asked, 1, "not found: no longer followed");
});

test("forgotten (Swift's markForgotten, a press on the page, other pages), a mark tells nothing", () => {
    posted.length = 0;
    const label = mark(0, 20);
    follow(label);
    forgetMark();
    label.box = box(5, 25);
    tellMarkMoved();
    assert.deepEqual(posted, []);
});

test("a mark that leaves the visible part of the page says so once and is no longer followed (v1.4 a-1)", () => {
    posted.length = 0;
    const label = mark(40, 790);
    follow(label);
    label.box = box(40, 805);
    tellMarkMoved();
    assert.deepEqual(posted, [{ markMoved: { left: 40, top: 785, bottom: 805 } }], "partly in sight: its card still follows it");
    label.box = box(40, 830);
    tellMarkMoved();
    label.box = box(40, 300);
    tellMarkMoved();
    assert.deepEqual(posted.slice(1), [{ markAway: true }], "below the bottom: away, then nothing more");

    posted.length = 0;
    const high = mark(40, 30);
    follow(high);
    high.box = box(40, -1);
    tellMarkMoved();
    assert.deepEqual(posted, [{ markAway: true }], "scrolled off the top");
});

test("at a zoom other than 1 a place is in the web view's points, from the visible part's corner", () => {
    const corner = box(140, 260);
    assert.deepEqual(windowPoint(corner), { left: 140, top: 240, bottom: 260 }, "no visual viewport: as laid out");
    globalThis.window.visualViewport = { scale: 2, offsetLeft: 100, offsetTop: 200, width: 500, height: 400 };
    try {
        assert.deepEqual(windowPoint(corner), { left: 80, top: 80, bottom: 120 });
        posted.length = 0;
        const label = mark(140, 260);
        follow(label);
        label.box = box(140, 250);
        tellMarkMoved();
        assert.deepEqual(posted, [{ markMoved: { left: 80, top: 60, bottom: 100 } }]);
        label.box = box(140, 199);
        tellMarkMoved();
        assert.deepEqual(posted.slice(1), [{ markAway: true }], "above the visible part's top, though still on the page");
    } finally {
        delete globalThis.window.visualViewport;
    }
});
