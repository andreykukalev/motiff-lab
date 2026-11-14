// Tests for the modes of version 0.5 and their keys (the experiment Two playheads and modes), run in Node on the compiled
// modules:   dotnet build src/MotiffLab.Web && node --test tests/web

import assert from "node:assert/strict";
import { test } from "node:test";

import { modeFor, removesSelection } from "../../src/MotiffLab.Web/wwwroot/js/layout/mode-keys.js";
import { inside, modeOf } from "../../src/MotiffLab.Web/wwwroot/js/score/score-modes.js";

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
