// Tests for the web's owner of the playback model (v0.5.2, #495 UX-10, slice W1, Scripts/playback/playback-owner.ts): the
// TypeScript Player holds the selection and Цикл there and gives the engine the model's steps. It runs the case file the
// model and the iPad's Swift port run (ipad/App/MotiffLabTests/Golden/playback-model.json), in Node on the compiled module:
//   dotnet build src/MotiffLab.Web && node --test tests/web

import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";

import { PassageGrip, PlaybackOwner } from "../../src/MotiffLab.Web/wwwroot/js/playback/playback-owner.js";

const root = new URL("../../", import.meta.url);
const { cases } = JSON.parse(readFileSync(new URL("ipad/App/MotiffLabTests/Golden/playback-model.json", root), "utf8"));

/** An owner with the model's state as a case gives it: the selection and Цикл set by the events that make them. */
function ownerOf(given) {
    const sent = [];
    const owner = new PlaybackOwner((step) => sent.push(step));
    const engine = { tick: given.tick, transport: given.transport };
    // Made while the engine stands, as a selection made while it plays behind the position leaves Цикл off (#521 5).
    const standing = { tick: given.tick, transport: "stopped" };
    if (given.selection !== null) {
        // A new selection turns Цикл on (T9); Цикл off is one press of it, which takes the position to the start (T7/T8).
        owner.apply({ type: "select", selection: given.selection }, standing);
        if (!given.loop) {
            owner.apply({ type: "cycle" }, standing);
        }
    } else if (!given.loop) {
        owner.apply({ type: "select", selection: { start: 0, end: 1 } }, standing);
        owner.apply({ type: "cycle" }, standing);
        owner.apply({ type: "select", selection: null }, standing);
    }
    sent.length = 0;
    return { owner, sent, engine };
}

for (const one of cases.filter((each) => each.event !== undefined)) {
    test(`the Player's owner, ${one.rule}: ${one.name}`, () => {
        const { owner, sent, engine } = ownerOf(one.given);
        assert.deepEqual(owner.state(engine), one.given, "the owner holds the state the case gives");
        const decided = owner.apply(one.event, engine);
        assert.deepEqual(sent, one.steps, "the engine gets the model's steps, in order");
        assert.deepEqual(owner.state(decided.engine), one.then, "the selection and Цикл the model says, the engine where it will be");
    });
}

test("the report says Цикл, the colour of Play and the place it names, not the moving position", () => {
    const { owner } = ownerOf({ selection: { start: 960, end: 1920 }, loop: false, tick: 0, transport: "stopped" });
    assert.deepEqual(owner.report({ tick: 480, transport: "stopped" }), { loop: false, context: "piece", at: null });
    assert.deepEqual(owner.report({ tick: 1200, transport: "playing" }), { loop: false, context: "selection", at: null });
    assert.deepEqual(owner.report({ tick: 2400, transport: "playing" }), { loop: false, context: "piece", at: null });
    owner.apply({ type: "cycle" }, { tick: 2400, transport: "stopped" });
    // Stopped with Цикл on outside the selection: Play is cobalt and names the selection's start, where it will play.
    assert.deepEqual(owner.report({ tick: 2400, transport: "stopped" }), { loop: true, context: "selection", at: 960 });
});

test("while another owner holds the engine's loop the model's loop waits, and hold gives it back", () => {
    const { owner, sent } = ownerOf({ selection: null, loop: true, tick: 0, transport: "stopped" });
    owner.take();
    const decided = owner.apply({ type: "select", selection: { start: 960, end: 1920 } }, { tick: 0, transport: "playing" });
    assert.equal(decided.loop, undefined, "the engine's loop is not touched");
    assert.deepEqual(sent, []);
    assert.deepEqual(owner.hold({ tick: 0, transport: "playing" }), { start: 960, end: 1920 });
    assert.deepEqual(sent, [{ type: "loop", start: 960, end: 1920 }]);
    // Once given back, the model's loop goes to the engine again.
    sent.length = 0;
    owner.apply({ type: "cycle" }, { tick: 0, transport: "playing" });
    assert.deepEqual(sent, [{ type: "loopOff" }]);
});

test("a move out of the selection with Цикл on turns it off, and the seek goes to the engine after the loop's end (T12)", () => {
    const { owner, sent } = ownerOf({ selection: { start: 960, end: 1920 }, loop: true, tick: 1200, transport: "playing" });
    const decided = owner.apply({ type: "seek", at: 3840 }, { tick: 1200, transport: "playing" });
    assert.equal(decided.loop, null);
    assert.deepEqual(sent, [{ type: "loopOff" }, { type: "seek", at: 3840 }]);
    assert.equal(owner.report(decided.engine).loop, false);
});

test("a new piece forgets the selection but keeps Цикл", () => {
    const { owner } = ownerOf({ selection: { start: 960, end: 1920 }, loop: false, tick: 0, transport: "stopped" });
    owner.reset();
    assert.deepEqual(owner.state({ tick: 0, transport: "stopped" }), { selection: null, loop: false, tick: 0, transport: "stopped" });
});

// A playhead dragged on the score or the roll (T14–T16, #495 UX-08 and UX-09, slice W3): the views send the drag to the
// Player straight, and the owner runs it as the iPad's Player does.

test("a playhead taken while playing pauses, dragged scrubs from the last place, and let go plays on from there", () => {
    const { owner, sent } = ownerOf({ selection: null, loop: true, tick: 1000, transport: "playing" });
    const engine = { tick: 1000, transport: "playing" };

    owner.gesture({ phase: "grab", handle: "black" }, engine);
    assert.ok(owner.dragging);
    assert.deepEqual(sent, [{ type: "pause" }], "T14: playback stands while it is held");

    sent.length = 0;
    // The engine has not reported the pause or the place yet: the drag goes on from the place it last went to.
    owner.gesture({ phase: "move", handle: "black", tick: 1200 }, engine);
    owner.gesture({ phase: "move", handle: "black", tick: 960 }, engine);
    assert.deepEqual(sent, [{ type: "scrub", from: 1000, to: 1200 }, { type: "scrub", from: 1200, to: 960 }], "T15: forward, then back");

    sent.length = 0;
    const released = owner.gesture({ phase: "release", handle: "black" }, engine);
    assert.deepEqual(sent, [{ type: "scrubEnd" }, { type: "play" }], "T16: the drag's notes fade, and it plays on");
    assert.deepEqual(released.engine, { tick: 960, transport: "playing" });
    assert.equal(owner.dragging, false);
});

test("let go outside the selection with Цикл on, the black playhead turns Цикл off; the blue one keeps to the selection", () => {
    const black = ownerOf({ selection: { start: 1920, end: 3840 }, loop: true, tick: 2400, transport: "stopped" });
    const engine = { tick: 2400, transport: "stopped" };
    black.owner.gesture({ phase: "grab", handle: "black" }, engine);
    black.owner.gesture({ phase: "move", handle: "black", tick: 1800 }, engine);
    black.sent.length = 0;
    const released = black.owner.gesture({ phase: "release", handle: "black" }, engine);
    assert.deepEqual(black.sent, [{ type: "scrubEnd" }, { type: "loopOff" }], "stopped as it was taken, it stays stopped");
    assert.equal(released.loop, null, "Blazor hears the loop went");
    assert.equal(black.owner.report(released.engine).loop, false);

    const blue = ownerOf({ selection: { start: 1920, end: 3840 }, loop: true, tick: 2400, transport: "stopped" });
    blue.owner.gesture({ phase: "grab", handle: "blue" }, engine);
    blue.owner.gesture({ phase: "move", handle: "blue", tick: 600 }, engine);
    assert.deepEqual(blue.sent, [{ type: "scrub", from: 2400, to: 1920 }], "held to the selection's start");
    blue.sent.length = 0;
    blue.owner.gesture({ phase: "release", handle: "blue" }, engine);
    assert.deepEqual(blue.sent, [{ type: "scrubEnd" }]);
    assert.equal(blue.owner.report({ tick: 1920, transport: "stopped" }).loop, true);
});

test("a move or a letting go with nothing held does nothing; a new piece or a second grab lets the held one go", () => {
    const { owner, sent } = ownerOf({ selection: null, loop: true, tick: 0, transport: "stopped" });
    const engine = { tick: 0, transport: "stopped" };
    assert.equal(owner.gesture({ phase: "move", handle: "black", tick: 480 }, engine), null);
    assert.equal(owner.gesture({ phase: "release", handle: "black" }, engine), null);
    assert.deepEqual(sent, []);

    owner.gesture({ phase: "grab", handle: "black" }, engine);
    owner.gesture({ phase: "move", handle: "black", tick: 480 }, engine);
    sent.length = 0;
    owner.gesture({ phase: "grab", handle: "black" }, engine);
    assert.deepEqual(sent, [{ type: "scrubEnd" }], "the old drag's notes fade before the new one");
    assert.ok(owner.dragging);

    owner.reset();
    assert.equal(owner.dragging, false);
});

test("in Expand the playhead is held over the passage: it stands, scrubs inside it either way and plays on, the loop untouched (#533 2)", () => {
    const sent = [];
    const grip = new PassageGrip((step) => sent.push(step));
    const passage = { start: 1920, end: 3840 };
    assert.deepEqual(grip.gesture({ phase: "grab", handle: "blue" }, passage, { tick: 2400, transport: "playing" }), { tick: 2400, transport: "stopped" });
    assert.equal(grip.dragging, true);
    grip.gesture({ phase: "move", handle: "blue", tick: 2040 }, passage, { tick: 2400, transport: "stopped" });
    grip.gesture({ phase: "move", handle: "blue", tick: 9000 }, passage, { tick: 2040, transport: "stopped" });
    grip.gesture({ phase: "move", handle: "blue", tick: 0 }, passage, { tick: 3839, transport: "stopped" });
    const after = grip.gesture({ phase: "release", handle: "blue" }, passage, { tick: 1920, transport: "stopped" });
    assert.deepEqual(after, { tick: 1920, transport: "playing" });
    assert.equal(grip.dragging, false);
    assert.deepEqual(sent, [
        { type: "pause" },
        { type: "scrub", from: 2400, to: 2040 },
        { type: "scrub", from: 2040, to: 3839 },
        { type: "scrub", from: 3839, to: 1920 },
        { type: "scrubEnd" },
        { type: "play" },
    ]);

    sent.length = 0;
    grip.gesture({ phase: "grab", handle: "blue" }, passage, { tick: 2400, transport: "stopped" });
    grip.gesture({ phase: "move", handle: "blue", tick: 2880 }, passage, { tick: 2400, transport: "stopped" });
    grip.gesture({ phase: "release", handle: "blue" }, passage, { tick: 2880, transport: "stopped" });
    assert.deepEqual(sent, [{ type: "scrub", from: 2400, to: 2880 }, { type: "scrubEnd" }], "stopped, it stays stopped (T16)");
    assert.equal(grip.gesture({ phase: "release", handle: "blue" }, passage, { tick: 2880, transport: "stopped" }), null, "nothing held");
});
