// The playhead the player moves on the roll (Scripts/roll/roll-playhead.ts, Andrei, v0.2): dragged it scrubs inside the loop,
// the arrows and VoiceOver nudge it by a beat, and the roll shows the new place until the player reports it.
//   dotnet build src/MotiffLab.Web && node --test tests/web
import assert from "node:assert/strict";
import { test } from "node:test";

import { RollModel, ENGLISH_ROLL_LABELS } from "../../src/MotiffLab.Web/wwwroot/js/roll/roll-model.js";
import {
    PlayheadControl,
    beatAt,
    beatFrom,
    nudged,
    onPlayhead,
    placeInWords,
    placeWithin,
} from "../../src/MotiffLab.Web/wwwroot/js/roll/roll-playhead.js";

/** Two bars of 4/4 and one of 3/4, at 480 ticks a quarter. */
const bars = RollModel.fromPayload({ ids: [], notes: [], bars: [0, 1920, 480, 1, 1920, 1920, 480, 2, 3840, 1440, 480, 3], end: 5280 }).bars;

test("the playhead stays inside the loop, before its end, and inside the piece with no loop", () => {
    const loop = { start: 1920, end: 3840 };
    assert.equal(placeWithin(2400.4, loop, 5280), 2400);
    assert.equal(placeWithin(100, loop, 5280), 1920);
    assert.equal(placeWithin(9000, loop, 5280), 3839, "never on the loop's end, where playing would run past it");
    assert.equal(placeWithin(-50, null, 5280), 0);
    assert.equal(placeWithin(9000, null, 5280), 5280);
    assert.equal(placeWithin(700, { start: 960, end: 960 }, 5280), 700, "an empty span is no span");
});

test("a tap on the roll's strips in Navigate goes to the beat it lies in (#543 2, T12)", () => {
    assert.equal(beatAt(bars, 0), 0);
    assert.equal(beatAt(bars, 479), 0);
    assert.equal(beatAt(bars, 480), 480, "on a beat line, that beat");
    assert.equal(beatAt(bars, 4000), 3840, "in the 3/4 bar");
    assert.equal(beatAt(bars, 6000), 4800, "past the end, the last beat");
    assert.equal(beatAt([], 700), 0);
});

test("a beat on or back follows the bars' beats, across a change of metre", () => {
    assert.equal(beatFrom(bars, 0, 1), 480);
    assert.equal(beatFrom(bars, 500, 1), 960);
    assert.equal(beatFrom(bars, 500, -1), 480);
    assert.equal(beatFrom(bars, 480, -1), 0, "from a beat, the beat before");
    assert.equal(beatFrom(bars, 1440, 1), 1920, "the last beat of a bar to the next bar's first");
    assert.equal(beatFrom(bars, 4800, 1), 4800, "no beat after the last");
    assert.equal(beatFrom(bars, 0, -1), 0, "no beat before the first");
});

test("a nudge stays inside the loop: back to its start at most, never past its last beat", () => {
    const context = { bars, span: { start: 1920, end: 3840 }, end: 5280 };
    assert.equal(nudged(context, 2000, 1), 2400);
    assert.equal(nudged(context, 2000, -1), 1920);
    assert.equal(nudged(context, 1920, -1), null, "at the loop's start it does not move back");
    assert.equal(nudged(context, 3360, 1), null, "the next beat is the loop's end: it stays");
    assert.equal(nudged({ ...context, span: null }, 3360, 1), 3840);
});

test("the place in words is the bar's printed number and the beat in it", () => {
    assert.equal(placeInWords(bars, 0, ENGLISH_ROLL_LABELS), "Bar 1, beat 1");
    assert.equal(placeInWords(bars, 2900, ENGLISH_ROLL_LABELS), "Bar 2, beat 3");
    assert.equal(placeInWords(bars, 9999, ENGLISH_ROLL_LABELS), "Bar 3, beat 3", "past the end, the last beat of the last bar");
    assert.equal(placeInWords([], 0, ENGLISH_ROLL_LABELS), "");
});

test("a press takes the playhead on its handle under the bar numbers, and down its line only in touch reading", () => {
    const header = 24;
    assert.equal(onPlayhead(210, 30, 200, header, 44, false), true, "on the handle");
    assert.equal(onPlayhead(230, 30, 200, header, 44, false), false, "more than half a touch target away");
    assert.equal(onPlayhead(200, 10, 200, header, 44, false), false, "the bar numbers keep selecting bars");
    assert.equal(onPlayhead(200, 200, 200, header, 44, false), false, "below the handle the notes are the notes");
    assert.equal(onPlayhead(215, 200, 200, header, 44, true), true, "a finger down the line in touch reading");
});

/** A stand-in for the few parts of the DOM the control uses. */
function fakeDocument() {
    const element = {
        style: {},
        attributes: {},
        listeners: {},
        value: "0",
        addEventListener(type, listener) {
            this.listeners[type] = listener;
        },
        setAttribute(name, value) {
            this.attributes[name] = value;
        },
    };
    globalThis.document = { createElement: () => element, activeElement: null };
    return element;
}

function control(span = { start: 1920, end: 3840 }) {
    const element = fakeDocument();
    const seeks = [];
    const state = { tick: 1920 };
    const playhead = new PlayheadControl(
        () => ({ bars, span, end: 5280, tick: state.tick }),
        (tick, play) => seeks.push([tick, play]),
        () => {},
    );
    return { playhead, element, seeks, state };
}

test("a drag scrubs inside the loop, and the roll shows the place until the player reports it", () => {
    const { playhead, seeks } = control();
    playhead.begin(1, 300);
    assert.equal(playhead.move(2, 400, 2400), false, "another pointer is not this drag");
    assert.equal(playhead.move(1, 302, 1950), true);
    assert.deepEqual(seeks, [], "a move within a tap's slop is still a tap");
    assert.equal(playhead.move(1, 400, 2400.6), true);
    assert.deepEqual(seeks, [[2401, false]], "it moves, and does not start or stop playing");
    assert.equal(playhead.dragging, true);
    assert.equal(playhead.heard(1920), 2401, "the place dragged to, not the player's late report");
    playhead.move(1, 900, 9000);
    assert.equal(playhead.end(1), "dragged");
    assert.deepEqual(seeks.at(-1), [3839, false], "let go past the loop: its last tick, the last place always sent");
    assert.equal(playhead.heard(1920), 3839, "until the player gets there");
    assert.equal(playhead.heard(3839), 3839);
    assert.equal(playhead.heard(1000), 1000, "once it got there, the player's place again");
});

test("a press on the playhead that does not move is a tap, and moves nothing", () => {
    const { playhead, seeks } = control();
    playhead.begin(1, 300);
    assert.equal(playhead.end(1), "tapped");
    assert.equal(playhead.end(1), null, "only once");
    assert.deepEqual(seeks, []);
});

test("the arrows nudge by a beat, VoiceOver's swipes too, and the control says the bar and beat", () => {
    const { playhead, element, seeks, state } = control();
    let prevented = false;
    element.listeners.keydown({ key: "ArrowRight", preventDefault: () => (prevented = true), stopPropagation: () => {} });
    assert.equal(prevented, true);
    assert.deepEqual(seeks, [[2400, false]]);
    state.tick = 2400;
    element.listeners.keydown({ key: "ArrowLeft", preventDefault: () => {}, stopPropagation: () => {} });
    assert.deepEqual(seeks.at(-1), [1920, false]);
    state.tick = 1920;
    playhead.heard(1920);
    // VoiceOver's increment changes the value by a step: the control takes it as a beat on and puts the value back.
    element.value = "1921";
    element.listeners.input();
    assert.deepEqual(seeks.at(-1), [2400, false]);
    assert.equal(element.value, "1920");
    state.tick = 2400;
    playhead.heard(2400);
    playhead.place(500, 24, 44, { bars, span: { start: 1920, end: 3840 }, end: 5280, tick: 2400 }, ENGLISH_ROLL_LABELS);
    assert.equal(element.attributes["aria-label"], "Playhead");
    assert.equal(element.attributes["aria-valuetext"], "Bar 2, beat 2");
    assert.equal(element.min, "1920");
    assert.equal(element.max, "3839");
    assert.equal(element.style.left, "478px");
});

test("a tap inside the loop plays from there", () => {
    const { playhead, seeks } = control();
    playhead.go(2600, true);
    assert.deepEqual(seeks, [[2600, true]]);
    assert.equal(playhead.heard(1920), 2600);
});
