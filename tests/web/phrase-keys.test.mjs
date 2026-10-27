// Tests for stepping between phrases from the keyboard (#126), run in Node on the compiled module:
//   dotnet build src/MotiffLab.Web && node --test tests/web

import assert from "node:assert/strict";
import { test } from "node:test";

import { stepFor } from "../../src/MotiffLab.Web/wwwroot/js/phrases/phrase-keys.js";

const key = (name, extra = {}) => ({ key: name, altKey: false, ctrlKey: false, metaKey: false, shiftKey: false, target: { tagName: "BODY" }, ...extra });

test("Page Up and Page Down step to the previous and the next phrase", () => {
    assert.equal(stepFor(key("PageUp")), -1);
    assert.equal(stepFor(key("PageDown")), 1);
    assert.equal(stepFor(key("ArrowRight")), null);
    assert.equal(stepFor(key("]")), null);
});

test("keys with a modifier or typed into a field are left alone", () => {
    assert.equal(stepFor(key("PageDown", { shiftKey: true })), null);
    assert.equal(stepFor(key("PageDown", { ctrlKey: true })), null);
    for (const tagName of ["INPUT", "TEXTAREA", "SELECT", "select"]) {
        assert.equal(stepFor(key("PageDown", { target: { tagName } })), null);
    }
    assert.equal(stepFor(key("PageUp", { target: { tagName: "DIV", isContentEditable: true } })), null);
    assert.equal(stepFor(key("PageUp", { target: null })), -1);
});
