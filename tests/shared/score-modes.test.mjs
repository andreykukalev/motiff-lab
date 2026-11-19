// Tests for the modes of version 0.5 (once the experiment Two playheads and modes), run in Node on the compiled
// modules:   dotnet build src/shared && node --test tests/shared

import assert from "node:assert/strict";
import { test } from "node:test";

import { inside, modeOf } from "../../src/shared/wwwroot/js/score/score-modes.js";

test("the modes are named as the bar names them, and nothing else is a mode", () => {
    assert.equal(modeOf("select"), "select");
    assert.equal(modeOf(null), null);
    assert.equal(modeOf("hand"), null);
});

test("a point inside the band's runs is inside the selection", () => {
    const runs = [{ left: 10, top: 10, right: 100, bottom: 60 }, { left: 10, top: 120, right: 50, bottom: 170 }];
    assert.equal(inside(runs, 40, 30), true);
    assert.equal(inside(runs, 40, 150), true);
    assert.equal(inside(runs, 80, 150), false);
});
