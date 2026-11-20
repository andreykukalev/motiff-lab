// Tests for what the iPad's score page shares between its modules (Scripts/score/ipad-page/page-state.ts), run in Node on
// the compiled modules:   dotnet build src/shared && node --test tests/shared

import assert from "node:assert/strict";
import { test } from "node:test";

import { pointerRules, state } from "../../src/shared/wwwroot/js/score/ipad-page/page-state.js";

const touch = { select: "longPress", tap: true, handles: true, hold: false, colour: false };
const pen = { select: "now", tap: false, handles: false, hold: true, colour: true };

test("a finger and the Pencil read the rules Swift gave each of them", () => {
    state.input = { touch, pen, colour: "amber", places: true };
    assert.equal(pointerRules("touch"), touch);
    assert.equal(pointerRules("pen"), pen);
});

test("a mouse, or a name of the rules that is no pointer, has no rules of its own", () => {
    state.input = { touch, pen, colour: "amber", places: true };
    for (const pointer of ["mouse", "", "colour", "places"]) {
        assert.equal(pointerRules(pointer), undefined, pointer);
    }
});

test("before Swift's first rules, and for a pointer they leave out, there are none", () => {
    state.input = null;
    assert.equal(pointerRules("touch"), undefined);
    state.input = { places: true };
    assert.equal(pointerRules("pen"), undefined);
});
