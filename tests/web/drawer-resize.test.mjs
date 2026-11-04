// Tests for the piano roll drawer's height (decision record 0018, section 4), run in Node on the compiled module:
// a drag sets the drawer's share of the screen within its bounds, and a drag nearly away closes it.
//   dotnet build src/MotiffLab.Web && node --test tests/web

import assert from "node:assert/strict";
import { test } from "node:test";

import { CLOSE_SHARE, MAX_SHARE, MIN_SHARE, shareAfter } from "../../src/MotiffLab.Web/wwwroot/js/layout/drawer-resize.js";

test("dragging the handle up makes the drawer taller, down makes it shorter", () => {
    assert.equal(shareAfter(0.4, -100, 1000), 0.5);
    assert.equal(shareAfter(0.4, 100, 1000), 0.30000000000000004);
});

test("the drawer stops at about 70% of the screen and never goes to full screen", () => {
    assert.equal(shareAfter(0.4, -900, 1000), MAX_SHARE);
    assert.ok(MAX_SHARE < 1);
});

test("dragged short it holds its smallest height, dragged nearly away it closes", () => {
    assert.equal(shareAfter(0.4, 250, 1000), MIN_SHARE);
    assert.equal(shareAfter(0.4, 1000 * (0.4 - CLOSE_SHARE) + 1, 1000), null);
});

test("a zero-height viewport does not divide by zero", () => {
    assert.equal(shareAfter(0.4, 0, 0), 0.4);
});
