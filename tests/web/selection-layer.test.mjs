// Where the contextual menu goes over the selection band (Scripts/score/selection-layer.ts, 0018 section 3).
import assert from "node:assert/strict";
import { test } from "node:test";

import { menuPlace, ordered } from "../../src/MotiffLab.Web/wwwroot/js/score/selection-layer.js";
import { BRACKET_LIFT } from "../../src/MotiffLab.Web/wwwroot/js/score/score-geometry.js";

const window = { width: 1180, height: 820 };
const menu = { width: 420, height: 44 };

test("the menu sits centred above the band's first run, clear of the bracket and its number", () => {
    const place = menuPlace({ left: 500, right: 900, top: 400 }, { bottom: 700 }, menu, window);
    assert.equal(place.below, false);
    assert.equal(place.left, 700 - 210);
    assert.ok(place.top + menu.height < 400 - BRACKET_LIFT - 14, "above the bracket's number");
    assert.equal(place.pointer, 210);
});

test("with no room above, the menu goes under the band's last run, and it stays inside the window", () => {
    const place = menuPlace({ left: 0, right: 100, top: 90 }, { bottom: 300 }, menu, window);
    assert.equal(place.below, true);
    assert.equal(place.top, 314);
    assert.equal(place.left, 16);
    // The pointer still points at the band, as far as the menu's rounded end allows.
    assert.equal(place.pointer, 34);
    const right = menuPlace({ left: 1100, right: 1180, top: 500 }, { bottom: 600 }, menu, window);
    assert.equal(right.left, 1180 - 16 - 420);
    assert.equal(right.pointer, 1140 - right.left);
});

test("two bars give the selection's first and last in time, whichever was pressed first", () => {
    const a = { tick: 960 };
    const b = { tick: 0 };
    assert.deepEqual(ordered(a, b), [b, a]);
    assert.deepEqual(ordered(b, a), [b, a]);
});

test("the menu keeps to the right of a drawer over the left of the window", () => {
    const menu = { width: 300, height: 44 };
    const place = menuPlace({ left: 100, right: 300, top: 400 }, { bottom: 700 }, menu, { width: 1180, height: 820, left: 332 });
    assert.equal(place.left, 348);
    assert.equal(place.pointer, 20);
});

test("the menu keeps left of a sheet on the right and above a drawer over the foot of the window", () => {
    const menu = { width: 300, height: 200 };
    const right = menuPlace({ left: 700, right: 900, top: 400 }, { bottom: 500 }, { width: 300, height: 44 }, { width: 1180, height: 820, right: 800 });
    assert.equal(right.left, 800 - 16 - 300);
    // A tall card under the band is lifted to end above the drawer, never above the bar's line.
    const foot = menuPlace({ left: 100, right: 300, top: 90 }, { bottom: 300 }, menu, { width: 1180, height: 820, bottom: 500 });
    assert.equal(foot.below, true);
    assert.equal(foot.top, 500 - 16 - 200);
    const low = menuPlace({ left: 100, right: 300, top: 90 }, { bottom: 300 }, menu, { width: 1180, height: 820, bottom: 200 });
    assert.equal(low.top, 72);
});
