// Tests for the two playheads of version 0.5 (the experiment Two playheads and modes; product spec two-playheads), run in
// Node on the compiled modules:   dotnet build src/MotiffLab.Web && node --test tests/web

import assert from "node:assert/strict";
import { test } from "node:test";

import { playsOrPauses } from "../../src/MotiffLab.Web/wwwroot/js/layout/mode-keys.js";
import { beatOf, placesOf } from "../../src/MotiffLab.Web/wwwroot/js/score/playheads.js";

test("while the piece has the player, the global playhead is the player's place and the selection's waits", () => {
    const places = placesOf({ selectionHolds: false, global: 0, selection: 960, fade: true }, 2400, true);
    assert.deepEqual(places, { global: 2400, selection: 960, globalPlays: true, fade: true });
});

test("while the selection has the player, its playhead is the player's place, the global one waits and nothing fades", () => {
    const places = placesOf({ selectionHolds: true, global: 4800, selection: 960, fade: true }, 1200, true);
    assert.deepEqual(places, { global: 4800, selection: 1200, globalPlays: false, fade: false });
});

test("stopped, nothing fades and the cap is hollow; the fade is a choice", () => {
    assert.equal(placesOf({ selectionHolds: false, global: 0, selection: null, fade: true }, 480, false).fade, false);
    assert.equal(placesOf({ selectionHolds: false, global: 0, selection: null, fade: false }, 480, true).fade, false);
    assert.equal(placesOf({ selectionHolds: false, global: 0, selection: null, fade: true }, 480, false).globalPlays, false);
});

test("the column takes the beat a tick falls in, from one beat line to the next", () => {
    const lines = [0, 240, 480, 720];
    assert.deepEqual(beatOf(lines, 0), { start: 0, end: 240 });
    assert.deepEqual(beatOf(lines, 300), { start: 240, end: 480 });
    assert.deepEqual(beatOf(lines, 719), { start: 480, end: 720 });
    assert.equal(beatOf([0], 10), undefined);
});

const space = (extra = {}) => ({ key: " ", code: "Space", altKey: false, ctrlKey: false, metaKey: false, shiftKey: false, defaultPrevented: false, target: { closest: () => null }, ...extra });

test("Space plays or pauses outside a field or a dialog, and not when the roll took it first", () => {
    assert.equal(playsOrPauses(space()), true);
    assert.equal(playsOrPauses(space({ defaultPrevented: true })), false);
    assert.equal(playsOrPauses(space({ ctrlKey: true })), false);
    assert.equal(playsOrPauses(space({ target: { closest: () => ({}) } })), false);
    assert.equal(playsOrPauses(space({ key: "a" })), false);
});
