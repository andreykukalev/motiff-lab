// Tests for the piano roll's model (S1-W2, S1-W3), run in Node on the compiled module:
//   dotnet build src/MotiffLab.Web && node --test tests/web

import assert from "node:assert/strict";
import { test } from "node:test";

import {
    ENGLISH_ROLL_LABELS,
    FLAG_HAND_INFERRED,
    FLAG_NOTE_INFERRED,
    HAND_LEFT,
    HAND_NONE,
    HAND_RIGHT,
    RollModel,
    fill,
    isBlackKey,
    pitchName,
} from "../../src/MotiffLab.Web/wwwroot/js/roll/roll-model.js";

/** Notes as [id, onset, duration, pitch, track, voice, hand, flags], in any order. */
function model(notes, bars = [[0, 1920, 480, 1], [1920, 1920, 480, 2]], end = 3840) {
    return RollModel.fromPayload({
        ids: notes.map((note) => note[0]),
        notes: notes.flatMap((note) => note.slice(1)),
        bars: bars.flat(),
        end,
    });
}

test("notes unpack from the payload in time order, with hand and what is inferred", () => {
    const roll = model([
        ["b", 480, 480, 64, 0, 1, HAND_RIGHT, 0],
        ["a", 0, 960, 48, 1, 0, HAND_LEFT, FLAG_HAND_INFERRED],
        ["c", 480, 240, 60, 0, 2, HAND_NONE, FLAG_NOTE_INFERRED],
    ]);

    assert.deepEqual(roll.notes.map((note) => note.id), ["a", "c", "b"]);
    assert.deepEqual(roll.notes[0], {
        id: "a",
        onset: 0,
        end: 960,
        pitch: 48,
        track: 1,
        voice: 0,
        hand: HAND_LEFT,
        noteInferred: false,
        handInferred: true,
    });
    assert.equal(roll.notes[1].noteInferred, true);
    assert.equal(roll.notes[1].handInferred, false);
    assert.equal(roll.lowest, 48);
    assert.equal(roll.highest, 64);
    assert.equal(roll.indexOf("b"), 2);
    assert.equal(roll.indexOf("missing"), undefined);
});

test("sounding notes started at or before the tick and end after it", () => {
    const roll = model([
        ["long", 0, 3840, 48, 0, 0, 0, 0],
        ["short", 1440, 120, 60, 0, 0, 0, 0],
    ]);

    const ids = (tick) => roll.soundingAt(tick).map((index) => roll.notes[index].id);
    assert.deepEqual(ids(0), ["long"]);
    assert.deepEqual(ids(1440), ["long", "short"]);
    assert.deepEqual(ids(1559), ["long", "short"]);
    assert.deepEqual(ids(1560), ["long"]);
    assert.deepEqual(ids(3840), []);
    assert.deepEqual(ids(-1), []);
});

test("a click finds the note drawn at that tick and pitch, the later one when two overlap", () => {
    const roll = model([
        ["first", 0, 960, 60, 0, 0, 0, 0],
        ["second", 480, 960, 60, 1, 0, 0, 0],
        ["other", 0, 480, 64, 0, 0, 0, 0],
    ]);

    const at = (tick, pitch) => {
        const index = roll.noteAt(tick, pitch);
        return index === undefined ? undefined : roll.notes[index].id;
    };
    assert.equal(at(100, 60), "first");
    assert.equal(at(600, 60), "second");
    assert.equal(at(100, 64), "other");
    assert.equal(at(100, 62), undefined);
    assert.equal(at(2000, 60), undefined);
});

test("notes in view include those that started before it and are still sounding", () => {
    const roll = model([
        ["held", 0, 2000, 60, 0, 0, 0, 0],
        ["gone", 0, 100, 62, 0, 0, 0, 0],
        ["inside", 1000, 100, 64, 0, 0, 0, 0],
        ["after", 3000, 100, 65, 0, 0, 0, 0],
    ]);

    assert.deepEqual(roll.inRange(500, 1500).map((index) => roll.notes[index].id), ["held", "inside"]);
});

test("the grid has numbered bar lines and beat lines, a pickup bar included", () => {
    const roll = model([], [[0, 480, 480, 0], [480, 1440, 480, 1], [1920, 1440, 480, 2]], 3360);

    assert.deepEqual(roll.grid(0, 3360), [
        { tick: 0, bar: 0 },
        { tick: 480, bar: 1 },
        { tick: 960, bar: null },
        { tick: 1440, bar: null },
        { tick: 1920, bar: 2 },
        { tick: 2400, bar: null },
        { tick: 2880, bar: null },
    ]);
    assert.deepEqual(roll.grid(1000, 2000), [{ tick: 1440, bar: null }, { tick: 1920, bar: 2 }]);
});

test("lanes are tracks, or voices within the tracks that have them", () => {
    const roll = model([
        ["a", 0, 480, 60, 0, 1, 0, 0],
        ["b", 0, 480, 64, 0, 2, 0, 0],
        ["c", 0, 480, 48, 1, 0, 0, 0],
    ]);

    assert.deepEqual(roll.lanes("track").map((lane) => lane.label), ["Track 1", "Track 2"]);
    assert.deepEqual(roll.lanes("voice").map((lane) => lane.label), ["Track 1, voice 1", "Track 1, voice 2", "Track 2"]);
});

test("hand lanes put the right hand first, then the left, then notes with no hand", () => {
    const roll = model([
        ["a", 0, 480, 48, 0, 1, HAND_LEFT, 0],
        ["b", 0, 480, 64, 0, 1, HAND_RIGHT, 0],
        ["c", 480, 480, 60, 0, 1, HAND_NONE, 0],
    ]);

    assert.deepEqual(roll.lanes("hand").map((lane) => lane.label), ["Right hand", "Left hand", "No hand"]);
});

test("lanes take their names from the labels given, as in Russian (#134)", () => {
    const roll = model([
        ["a", 0, 480, 60, 0, 1, HAND_RIGHT, 0],
        ["b", 0, 480, 64, 0, 2, HAND_LEFT, 0],
        ["c", 0, 480, 48, 1, 0, HAND_NONE, 0],
    ]);
    const labels = { ...ENGLISH_ROLL_LABELS, track: "Дорожка {0}", trackVoice: "{0}, голос {1}", rightHand: "Правая рука", leftHand: "Левая рука", noHand: "Рука не указана" };

    assert.deepEqual(roll.lanes("voice", labels).map((lane) => lane.label), ["Дорожка 1, голос 1", "Дорожка 1, голос 2", "Дорожка 2"]);
    assert.deepEqual(roll.lanes("hand", labels).map((lane) => lane.label), ["Правая рука", "Левая рука", "Рука не указана"]);
    assert.equal(fill("{0} из {1}, снова {0}", 3, "пяти"), "3 из пяти, снова 3");
});

test("the arrow keys step through the notes in time order and stop at the ends", () => {
    const roll = model([
        ["a", 0, 480, 60, 0, 0, 0, 0],
        ["b", 480, 480, 62, 0, 0, 0, 0],
    ]);

    assert.equal(roll.step(undefined, 1), 0);
    assert.equal(roll.step(undefined, -1), 1);
    assert.equal(roll.step(0, 1), 1);
    assert.equal(roll.step(1, 1), 1);
    assert.equal(roll.step(0, -1), 0);
    assert.equal(model([]).step(undefined, 1), undefined);
});

test("pitch names and black keys follow music-core's spelling, middle C as C4", () => {
    assert.equal(pitchName(60), "C4");
    assert.equal(pitchName(21), "A0");
    assert.equal(pitchName(70), "A♯4");
    assert.deepEqual([60, 61, 62, 63, 64, 65, 66].map(isBlackKey), [false, true, false, true, false, false, true]);
});
