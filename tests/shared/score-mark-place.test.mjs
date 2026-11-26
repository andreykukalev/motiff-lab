// Tests for the mark a card shows under, followed on the iPad's score page (Scripts/score/ipad-page/page-mark-place.ts,
// r-1): a scroll or a new layout tells Swift where it is now, in the web view's points, so the card stays under it. Run in
// Node on the compiled modules:   dotnet build src/shared && node --test tests/shared
// The wiring (the page's scroll and resize listeners, a tap on a mark, a phrase's number held) needs the page's DOM, which
// these tests do not have: the page scenarios run the real page.

import assert from "node:assert/strict";
import { test } from "node:test";

const posted = [];
globalThis.window = { webkit: { messageHandlers: { score: { postMessage: (message) => posted.push(message) } } } };
const { followMark, forgetMark, tellMarkMoved, windowPoint } = await import("../../src/shared/wwwroot/js/score/ipad-page/page-mark-place.js");

/** A mark on the page at `box`, connected until a test takes it away. */
function mark(box) {
    return { isConnected: true, box, getBoundingClientRect() { return this.box; } };
}

/** Follows `label` as a tap on it does: from where it is. */
function follow(label, find = () => null) {
    followMark(label, find, windowPoint(label.box));
}

test("a mark followed tells Swift where it is once it has moved, and only then", () => {
    posted.length = 0;
    const label = mark({ left: 40, bottom: 300 });
    follow(label);
    tellMarkMoved();
    assert.deepEqual(posted, [], "where it was tapped Swift knows already");
    label.box = { left: 40, bottom: 180 };
    tellMarkMoved();
    tellMarkMoved();
    assert.deepEqual(posted, [{ markMoved: { left: 40, bottom: 180 } }], "once for each new place");
});

test("a mark drawn anew is found once and kept; one gone is forgotten", () => {
    posted.length = 0;
    const old = mark({ left: 10, bottom: 20 });
    const again = mark({ left: 12, bottom: 90 });
    let looked = 0;
    follow(old, () => {
        looked += 1;
        return again;
    });
    old.isConnected = false;
    tellMarkMoved();
    again.box = { left: 12, bottom: 60 };
    tellMarkMoved();
    assert.deepEqual(posted, [{ markMoved: { left: 12, bottom: 90 } }, { markMoved: { left: 12, bottom: 60 } }]);
    assert.equal(looked, 1, "not looked for again each frame");

    posted.length = 0;
    const gone = mark({ left: 0, bottom: 0 });
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

test("forgotten (Swift's markForgotten, a press on the page, other pages or marks), a mark tells nothing", () => {
    posted.length = 0;
    const label = mark({ left: 0, bottom: 0 });
    follow(label);
    forgetMark();
    label.box = { left: 5, bottom: 5 };
    tellMarkMoved();
    assert.deepEqual(posted, []);
});

test("at a zoom other than 1 a place is in the web view's points, from the visible part's corner", () => {
    const box = { left: 140, bottom: 260 };
    assert.deepEqual(windowPoint(box), { left: 140, bottom: 260 }, "no visual viewport: as laid out");
    globalThis.window.visualViewport = { scale: 2, offsetLeft: 100, offsetTop: 200 };
    try {
        assert.deepEqual(windowPoint(box), { left: 80, bottom: 120 });
        posted.length = 0;
        const label = mark({ left: 140, bottom: 260 });
        follow(label);
        label.box = { left: 140, bottom: 250 };
        tellMarkMoved();
        assert.deepEqual(posted, [{ markMoved: { left: 80, bottom: 100 } }]);
    } finally {
        delete globalThis.window.visualViewport;
    }
});
