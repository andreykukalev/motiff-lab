// Tests for the playheads of version 0.5 (the experiment Two playheads and modes; product spec two-playheads; v0.5.2), run in
// Node on the compiled modules:   dotnet build src/MotiffLab.Web && node --test tests/web

import assert from "node:assert/strict";
import { test } from "node:test";

import { playsOrPauses } from "../../src/MotiffLab.Web/wwwroot/js/layout/mode-keys.js";
import { onePlace, placesOf, trackOf } from "../../src/MotiffLab.Web/wwwroot/js/score/playheads.js";

test("while the piece has the player, the global playhead is the player's place and the selection's waits", () => {
    const places = placesOf({ selectionHolds: false, global: 0, selection: 960 }, 2400, true);
    assert.deepEqual(places, { global: 2400, selection: 960, globalPlays: true });
});

test("while the selection has the player, its playhead is the player's place and the global one waits", () => {
    const places = placesOf({ selectionHolds: true, global: 4800, selection: 960 }, 1200, true);
    assert.deepEqual(places, { global: 4800, selection: 1200, globalPlays: false });
    assert.equal(placesOf({ selectionHolds: false, global: 0, selection: null }, 480, false).globalPlays, false, "stopped, the dot is hollow");
});

test("the iPad's one position is the selection's cobalt line inside it and the ink track outside it, never both (#479 item 25)", () => {
    const selection = { start: 960, end: 1920 };
    assert.deepEqual(onePlace(selection, 480, true), { global: 480, selection: null, globalPlays: true });
    assert.deepEqual(onePlace(selection, 960, true), { global: null, selection: 960, globalPlays: false });
    assert.deepEqual(onePlace(selection, 1919, false), { global: null, selection: 1919, globalPlays: false });
    assert.deepEqual(onePlace(selection, 1920, false), { global: 1920, selection: null, globalPlays: false }, "the end is outside");
    assert.deepEqual(onePlace(null, 1200, true), { global: 1200, selection: null, globalPlays: true });
});

test("the track runs under the lowest ink of its line, across its bar, filled up to the place (#479 item 19)", () => {
    const bar = { left: 100, right: 300, top: 50, bottom: 150 };
    assert.deepEqual(trackOf({ x: 160, top: 60, bottom: 140, bar }, 180), { left: 103, top: 190, width: 194, filled: 57 });
    assert.equal(trackOf({ x: 160, top: 60, bottom: 140, bar }, 100).top, 150, "never over the staves");
    assert.equal(trackOf({ x: 90, top: 60, bottom: 140, bar }, 180).filled, 0);
    assert.equal(trackOf({ x: 400, top: 60, bottom: 140, bar }, 180).filled, 194);
});

const space = (extra = {}) => ({ key: " ", code: "Space", altKey: false, ctrlKey: false, metaKey: false, shiftKey: false, defaultPrevented: false, target: { closest: () => null }, ...extra });

test("Space plays or pauses outside a field or a dialog, and not when the roll took it first", () => {
    assert.equal(playsOrPauses(space()), true);
    assert.equal(playsOrPauses(space({ defaultPrevented: true })), false);
    assert.equal(playsOrPauses(space({ ctrlKey: true })), false);
    assert.equal(playsOrPauses(space({ target: { closest: () => ({}) } })), false);
    assert.equal(playsOrPauses(space({ key: "a" })), false);
});
