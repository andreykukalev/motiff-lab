// The web's keys (Scripts/layout/mode-keys.ts): the modes of version 0.5 by H, S, E and P, Escape, Space and the
// selection's Undo and Redo; their cases came out of score-modes, playheads and selection-gesture with the module (W0.6).
// Run in Node on the compiled modules:   dotnet build src/shared && node --test tests/shared

import assert from "node:assert/strict";
import { test } from "node:test";

import { modeFor, playsOrPauses, removesSelection, undoFor } from "../../src/shared/wwwroot/js/layout/mode-keys.js";

const key = (code, extra = {}) => ({
    code, key: extra.key ?? code, altKey: false, ctrlKey: false, metaKey: false, shiftKey: false, defaultPrevented: false, target: { closest: () => null }, ...extra,
});

test("H, S, E and P choose the modes by the key's place, so any layout works", () => {
    assert.equal(modeFor(key("KeyH")), "navigate");
    assert.equal(modeFor(key("KeyS", { key: "ы" })), "select");
    assert.equal(modeFor(key("KeyE")), "edit");
    assert.equal(modeFor(key("KeyP")), "pen");
    assert.equal(modeFor(key("KeyB")), null);
});

test("a key typed into a field, held with a modifier or taken already chooses nothing", () => {
    assert.equal(modeFor(key("KeyS", { ctrlKey: true })), null);
    assert.equal(modeFor(key("KeyS", { shiftKey: true })), null);
    assert.equal(modeFor(key("KeyS", { defaultPrevented: true })), null);
    assert.equal(modeFor(key("KeyS", { target: { closest: () => ({}) } })), null);
});

test("Escape removes the selection, except in a field or a dialog", () => {
    assert.equal(removesSelection(key("Escape", { key: "Escape" })), true);
    assert.equal(removesSelection(key("Escape", { key: "Escape", target: { closest: () => ({}) } })), false);
});

const space = (extra = {}) => ({ key: " ", code: "Space", altKey: false, ctrlKey: false, metaKey: false, shiftKey: false, defaultPrevented: false, target: { closest: () => null }, ...extra });

test("Space plays or pauses outside a field or a dialog, and not when the roll took it first", () => {
    assert.equal(playsOrPauses(space()), true);
    assert.equal(playsOrPauses(space({ defaultPrevented: true })), false);
    assert.equal(playsOrPauses(space({ ctrlKey: true })), false);
    assert.equal(playsOrPauses(space({ target: { closest: () => ({}) } })), false);
    assert.equal(playsOrPauses(space({ key: "a" })), false);
});

test("⌘Z undoes the selection and ⇧⌘Z or Ctrl+Y redoes it, outside a field (#511 11)", () => {
    const key = (code, extra = {}) => ({ code, key: "", altKey: false, ctrlKey: false, metaKey: false, shiftKey: false, defaultPrevented: false, target: null, ...extra });
    assert.equal(undoFor(key("KeyZ", { metaKey: true })), "undo");
    assert.equal(undoFor(key("KeyZ", { metaKey: true, shiftKey: true })), "redo");
    assert.equal(undoFor(key("KeyZ", { ctrlKey: true })), "undo");
    assert.equal(undoFor(key("KeyY", { ctrlKey: true })), "redo");
    assert.equal(undoFor(key("KeyZ")), null, "Z alone is not Undo");
    const field = { closest: () => ({}) };
    assert.equal(undoFor(key("KeyZ", { metaKey: true, target: field })), null, "a field keeps its own Undo");
});
