// Tests for the web's owner of the playback model (v0.5.2, #495 UX-10, slice W1, Scripts/playback/playback-owner.ts): the
// TypeScript Player holds the selection and Цикл there and gives the engine the model's steps. It runs the case file the
// model and the iPad's Swift port run (ipad/App/MotiffLabTests/Golden/playback-model.json), in Node on the compiled module:
//   dotnet build src/MotiffLab.Web && node --test tests/web

import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";

import { PlaybackOwner } from "../../src/MotiffLab.Web/wwwroot/js/playback/playback-owner.js";

const root = new URL("../../", import.meta.url);
const { cases } = JSON.parse(readFileSync(new URL("ipad/App/MotiffLabTests/Golden/playback-model.json", root), "utf8"));

/** An owner with the model's state as a case gives it: the selection and Цикл set by the events that make them. */
function ownerOf(given) {
    const sent = [];
    const owner = new PlaybackOwner((step) => sent.push(step));
    const engine = { tick: given.tick, transport: given.transport };
    if (given.selection !== null) {
        // A new selection turns Цикл on (T9); Цикл off is one press of it, which takes the position to the start (T7/T8).
        owner.apply({ type: "select", selection: given.selection }, engine);
        if (!given.loop) {
            owner.apply({ type: "cycle" }, engine);
        }
    } else if (!given.loop) {
        owner.apply({ type: "select", selection: { start: 0, end: 1 } }, engine);
        owner.apply({ type: "cycle" }, engine);
        owner.apply({ type: "select", selection: null }, engine);
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
