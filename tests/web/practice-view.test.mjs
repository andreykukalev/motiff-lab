// Tests for practice view (#533 11), run in Node on the compiled module:
//   dotnet build src/MotiffLab.Web && node --test tests/web

import assert from "node:assert/strict";
import { test } from "node:test";

import {
    NORMAL,
    REVEAL_MS,
    TAP_SLOP,
    isTap,
    practiceChrome,
    practiceKey,
    practiceShown,
    practiceStep,
    practiced,
    revealLeft,
} from "../../src/MotiffLab.Web/wwwroot/js/layout/practice-view.js";

const key = (code, extra = {}) => ({ code, key: extra.key ?? code, altKey: false, ctrlKey: false, metaKey: false, shiftKey: false, repeat: false, defaultPrevented: false, target: { closest: () => null }, ...extra });

test("one action enters practice view and the same leaves it; entering shows no controls", () => {
    const on = practiceStep(NORMAL, "toggle", 1000);
    assert.deepEqual(on, { on: true, revealUntil: 0 });
    assert.equal(practiceShown(on, 1000), "on");
    assert.deepEqual(practiceStep(on, "toggle", 2000), NORMAL);
    assert.deepEqual(practiceStep(NORMAL, "enter", 0), { on: true, revealUntil: 0 });
    assert.deepEqual(practiceStep(on, "leave", 0), NORMAL);
    assert.deepEqual(practiceStep(on, "enter", 5000), on);
});

test("a tap in practice view shows the controls for a few seconds, then they hide again", () => {
    const on = practiceStep(NORMAL, "enter", 0);
    const tapped = practiceStep(on, "tap", 1000);
    assert.equal(practiceShown(tapped, 1000), "reveal");
    assert.equal(practiceShown(tapped, 1000 + REVEAL_MS - 1), "reveal");
    assert.equal(practiceShown(tapped, 1000 + REVEAL_MS), "on");
    assert.equal(revealLeft(tapped, 1500), REVEAL_MS - 500);
    assert.equal(revealLeft(tapped, 1000 + REVEAL_MS + 1), 0);
    // Another tap starts the time again; a tap is still the page's (a playhead placed), so it never hides them.
    assert.equal(practiceShown(practiceStep(tapped, "tap", 3000), 3000 + REVEAL_MS - 1), "reveal");
});

test("a touch on the shown controls keeps them a while longer, and brings back none that hid", () => {
    const tapped = practiceStep(practiceStep(NORMAL, "enter", 0), "tap", 0);
    assert.equal(practiceStep(tapped, "touch", 2000).revealUntil, 2000 + REVEAL_MS);
    const hidden = practiceStep(tapped, "touch", REVEAL_MS + 1);
    assert.equal(practiceShown(hidden, REVEAL_MS + 1), "on");
});

test("when their time is over the controls hide, and practice view stays", () => {
    const tapped = practiceStep(practiceStep(NORMAL, "enter", 0), "tap", 0);
    // The timer may fire a moment early: hiding does not depend on the clock.
    assert.deepEqual(practiceStep(tapped, "hide", REVEAL_MS - 1), { on: true, revealUntil: 0 });
    assert.equal(practiceStep(NORMAL, "hide", 0), NORMAL);
});

test("outside practice view a tap or a touch changes nothing: the normal workspace stays", () => {
    assert.equal(practiceStep(NORMAL, "tap", 10), NORMAL);
    assert.equal(practiceStep(NORMAL, "touch", 10), NORMAL);
    assert.equal(practiceShown(NORMAL, 10), "off");
    assert.equal(revealLeft(NORMAL, 10), 0);
});

test("practice view hides the menu, the pill, the blue playhead, the loop strip and the hints, and keeps the black playhead", () => {
    assert.deepEqual(practiceChrome("off"), { menu: true, transport: true, selectionHead: true, pieceHead: true, loopStrip: true, hints: true, controls: false });
    assert.deepEqual(practiceChrome("on"), { menu: false, transport: false, selectionHead: false, pieceHead: true, loopStrip: false, hints: false, controls: false });
    assert.deepEqual(practiceChrome("reveal"), { menu: false, transport: false, selectionHead: false, pieceHead: true, loopStrip: false, hints: false, controls: true });
});

test("the roll draws its playheads without the blue one in practice view, and as they are otherwise", () => {
    const places = { black: 960, blue: 1440, playing: true };
    assert.equal(practiced(places, "off"), places);
    assert.deepEqual(practiced(places, "on"), { black: 960, blue: null, playing: true });
    assert.deepEqual(practiced(places, "reveal"), { black: 960, blue: null, playing: true });
    const alone = { black: 0, blue: null };
    assert.equal(practiced(alone, "on"), alone);
});

test("a press that moved past a tap's reach is a drag, not a tap", () => {
    assert.equal(isTap({ x: 0, y: 0 }, { x: 3, y: 4 }), true);
    assert.equal(isTap({ x: 0, y: 0 }, { x: TAP_SLOP, y: 0 }), true);
    assert.equal(isTap({ x: 0, y: 0 }, { x: TAP_SLOP, y: 1 }), false);
});

test("F enters and leaves practice view by its place on the keyboard; Escape leaves it only while it is on", () => {
    assert.equal(practiceKey(key("KeyF", { key: "f" }), false), "toggle");
    assert.equal(practiceKey(key("KeyF", { key: "а" }), true), "toggle");
    assert.equal(practiceKey(key("Escape"), true), "leave");
    assert.equal(practiceKey(key("Escape"), false), null);
    assert.equal(practiceKey(key("Space", { key: " " }), true), null);
});

test("keys with a modifier, repeated, taken already or typed into a field are left alone", () => {
    assert.equal(practiceKey(key("KeyF", { metaKey: true }), false), null);
    assert.equal(practiceKey(key("KeyF", { shiftKey: true }), false), null);
    assert.equal(practiceKey(key("KeyF", { repeat: true }), false), null);
    assert.equal(practiceKey(key("Escape", { defaultPrevented: true }), true), null);
    assert.equal(practiceKey(key("KeyF", { target: { closest: (selector) => (selector.includes("input") ? {} : null) } }), false), null);
});
