// Tests for the playheads of version 0.5 (the experiment Two playheads and modes; product spec two-playheads; v0.5.2), run in
// Node on the compiled modules:   dotnet build src/MotiffLab.Web && node --test tests/web

import assert from "node:assert/strict";
import { test } from "node:test";

import { playsOrPauses } from "../../src/MotiffLab.Web/wwwroot/js/layout/mode-keys.js";
import { edgeOf, placesOf, playheadsOf, trackOf } from "../../src/MotiffLab.Web/wwwroot/js/score/playheads.js";
import { viewOf } from "../../src/MotiffLab.Web/wwwroot/js/playback/playback-model.js";

test("while the piece has the player, the global playhead is the player's place and the selection's waits", () => {
    const places = placesOf({ selectionHolds: false, global: 0, selection: 960 }, 2400, true);
    assert.deepEqual(places, { global: 2400, selection: 960, globalPlays: true });
});

test("while the selection has the player, its playhead is the player's place and the global one waits", () => {
    const places = placesOf({ selectionHolds: true, global: 4800, selection: 960 }, 1200, true);
    assert.deepEqual(places, { global: 4800, selection: 1200, globalPlays: false });
    assert.equal(placesOf({ selectionHolds: false, global: 0, selection: null }, 480, false).globalPlays, false, "stopped, the dot is hollow");
});

test("the iPad draws both playheads of the playback model: black at the position, blue held to the selection (#495 UX-10)", () => {
    const selection = { start: 960, end: 1920 };
    const before = viewOf({ selection, loop: false, tick: 480, transport: "playing" });
    assert.deepEqual(playheadsOf(before, selection), { global: 480, selection: 960, globalPlays: true, selectionAtEnd: false, follow: "global" });
    const inside = viewOf({ selection, loop: true, tick: 1200, transport: "stopped" });
    assert.deepEqual(playheadsOf(inside, selection), { global: 1200, selection: 1200, globalPlays: false, selectionAtEnd: false, follow: "global" }, "together inside, the dot hollow when it stands");
    const after = viewOf({ selection, loop: false, tick: 2400, transport: "playing" });
    assert.deepEqual(playheadsOf(after, selection), { global: 2400, selection: 1920, globalPlays: true, selectionAtEnd: true, follow: "global" }, "blue stands at the end, black goes on");
    const none = viewOf({ selection: null, loop: true, tick: 1200, transport: "playing" });
    assert.equal(playheadsOf(none, null).selection, null, "no blue without a selection");
});

test("the blue playhead standing at a bar line stands at the right edge of the selection's last bar, not on the next line", () => {
    const last = { tick: 960, end: 1920, left: 100, right: 300, top: 50, bottom: 150 };
    const next = { tick: 1920, end: 2880, left: 40, right: 240, top: 250, bottom: 350 };
    const atEnd = { x: 40, top: 250, bottom: 350, bar: next };
    const beforeEnd = { x: 296, top: 50, bottom: 150, bar: last };
    assert.deepEqual(edgeOf(atEnd, beforeEnd, 1920), { x: 300, top: 50, bottom: 150, bar: last });
    const midBar = { x: 180, top: 50, bottom: 150, bar: last };
    assert.deepEqual(edgeOf(midBar, beforeEnd, 1440), midBar, "an end inside a bar stays where it lies");
    assert.deepEqual(edgeOf(undefined, beforeEnd, 1920), { x: 300, top: 50, bottom: 150, bar: last }, "the piece's end");
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
