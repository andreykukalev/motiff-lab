// Tests for the long press that opens an element's own settings (decision record 0018, section 10), run in Node on the
// compiled module: a press held in place counts, one that moves is a drag or a scroll.
//   dotnet build src/MotiffLab.Web && node --test tests/web

import assert from "node:assert/strict";
import { test } from "node:test";

import { HOLD_MS, SLOP, heldStill } from "../../src/MotiffLab.Web/wwwroot/js/layout/long-press.js";

test("a press that stays within a few pixels is held still", () => {
    assert.equal(heldStill(0, 0), true);
    assert.equal(heldStill(3, -4), true);
    assert.equal(heldStill(SLOP, 0), true);
});

test("a press that moves further is a drag or a scroll", () => {
    assert.equal(heldStill(SLOP + 1, 0), false);
    assert.equal(heldStill(6, 6), false);
});

test("the hold is long enough not to catch a tap, short enough to feel immediate", () => {
    assert.ok(HOLD_MS >= 400 && HOLD_MS <= 800);
});
