// Tests for the workspace layout's pure helpers (0012), run in Node on the compiled module:
//   dotnet build src/MotiffLab.Web && node --test tests/web

import assert from "node:assert/strict";
import { test } from "node:test";

import { KEY_STEP, splitAfterKey, splitFrom } from "../../src/MotiffLab.Web/wwwroot/js/layout/workspace-layout.js";

test("the divider follows the pointer within the canvas, kept to a fifth from either end", () => {
    assert.equal(splitFrom(300, 100, 400), 0.5);
    assert.equal(splitFrom(110, 100, 400), 0.2);
    assert.equal(splitFrom(900, 100, 400), 0.8);
    assert.equal(splitFrom(50, 100, 0), 0.6);
});

test("arrow keys move the divider by a step, Home and End to the ends", () => {
    assert.equal(KEY_STEP, 0.05);
    assert.equal(splitAfterKey("ArrowUp", 0.5), 0.45);
    assert.equal(splitAfterKey("ArrowDown", 0.5), 0.55);
    assert.equal(splitAfterKey("ArrowLeft", 0.2), 0.2);
    assert.equal(splitAfterKey("ArrowRight", 0.8), 0.8);
    assert.equal(splitAfterKey("Home", 0.5), 0.2);
    assert.equal(splitAfterKey("End", 0.5), 0.8);
    assert.equal(splitAfterKey("Enter", 0.5), null);
});
