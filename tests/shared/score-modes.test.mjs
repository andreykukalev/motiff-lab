// Tests for the modes of version 0.5 (once the experiment Two playheads and modes), run in Node on the compiled
// modules:   dotnet build src/shared && node --test tests/shared

import assert from "node:assert/strict";
import { test } from "node:test";

import { modeOf } from "../../src/shared/wwwroot/js/score/score-modes.js";

test("the modes are named as the bar names them, and nothing else is a mode", () => {
    assert.equal(modeOf("select"), "select");
    assert.equal(modeOf(null), null);
    assert.equal(modeOf("hand"), null);
});
