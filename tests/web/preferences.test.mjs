// Tests for the device preferences' pure helpers (0012), run in Node on the compiled module:
//   dotnet build src/MotiffLab.Web && node --test tests/web

import assert from "node:assert/strict";
import { test } from "node:test";

import { clampSplit, getPreference, setPreference } from "../../src/MotiffLab.Web/wwwroot/js/storage/preferences.js";

test("the score's share of the canvas stays between a fifth and four fifths", () => {
    assert.equal(clampSplit(0.5), 0.5);
    assert.equal(clampSplit("0.95"), 0.8);
    assert.equal(clampSplit(0.05), 0.2);
    assert.equal(clampSplit(null), 0.6);
    assert.equal(clampSplit("", 0.4), 0.4);
    assert.equal(clampSplit("abc"), 0.6);
});

test("preferences survive blocked storage", () => {
    // Node has no localStorage: reads give null and writes are swallowed.
    assert.equal(getPreference("ml-language"), null);
    assert.doesNotThrow(() => setPreference("ml-language", "ru"));
    assert.doesNotThrow(() => setPreference("ml-language", null));
});
