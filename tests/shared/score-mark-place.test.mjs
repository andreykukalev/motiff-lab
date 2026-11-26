// Tests for the mark a card shows under, followed on the iPad's score page (Scripts/score/ipad-page/page-mark-place.ts,
// r-1): a scroll or a new layout tells Swift where it is now, so the card stays under it. Run in Node on the compiled
// modules:   dotnet build src/shared && node --test tests/shared

import assert from "node:assert/strict";
import { test } from "node:test";

const posted = [];
globalThis.window = { webkit: { messageHandlers: { score: { postMessage: (message) => posted.push(message) } } } };
const { followMark, forgetMark, tellMarkMoved } = await import("../../src/shared/wwwroot/js/score/ipad-page/page-mark-place.js");

/** A mark on the page at `box`, connected until the test takes it away. */
function mark(box) {
    return { isConnected: true, box, getBoundingClientRect() { return this.box; } };
}

test("a mark followed tells Swift where it is once it has moved, and only then", () => {
    posted.length = 0;
    const label = mark({ left: 40, bottom: 300 });
    followMark(label, () => null);
    tellMarkMoved();
    assert.deepEqual(posted, [], "where it was tapped Swift knows already");
    label.box = { left: 40, bottom: 180 };
    tellMarkMoved();
    tellMarkMoved();
    assert.deepEqual(posted, [{ markMoved: { left: 40, bottom: 180 } }], "once for each new place");
});

test("a mark drawn anew is found again, and one gone tells nothing", () => {
    posted.length = 0;
    const old = mark({ left: 10, bottom: 20 });
    let found = mark({ left: 12, bottom: 90 });
    followMark(old, () => found);
    old.isConnected = false;
    tellMarkMoved();
    assert.deepEqual(posted, [{ markMoved: { left: 12, bottom: 90 } }]);
    found = null;
    tellMarkMoved();
    assert.equal(posted.length, 1);
});

test("a press that reached the page forgets the mark: no card shows any more", () => {
    posted.length = 0;
    const label = mark({ left: 0, bottom: 0 });
    followMark(label, () => null);
    forgetMark();
    label.box = { left: 5, bottom: 5 };
    tellMarkMoved();
    assert.deepEqual(posted, []);
});
