// The metronome (decision record 0023; v0.5.5, #521 section 15): the tempo stays in range and tapping finds it; it follows
// the piece tempo until the player gives it one, a slide after the hold sets that tempo, and what is kept says which.
import assert from "node:assert/strict";
import { test } from "node:test";

import {
    MAX_BPM,
    MIN_BPM,
    MetronomeSetting,
    PIECE_DEFAULT_BPM,
    clampBpm,
    draggedBpm,
    keptText,
    readKept,
    tappedBpm,
} from "../../src/MotiffLab.Web/wwwroot/js/metronome/metronome.js";

test("a tempo stays between the slowest and the fastest offered, in whole beats", () => {
    assert.equal(clampBpm(72.4), 72);
    assert.equal(clampBpm(5), MIN_BPM);
    assert.equal(clampBpm(400), MAX_BPM);
    assert.equal(clampBpm(Number.NaN), 80);
});

test("taps give the tempo of their mean gap once there are two, and a long pause starts again", () => {
    assert.equal(tappedBpm([1000]), null);
    assert.equal(tappedBpm([1000, 1500]), 120);
    assert.equal(tappedBpm([0, 600, 1200, 1800, 2400, 3000]), 100);
    assert.equal(tappedBpm([0, 500, 4000]), null);
    // A slip in the middle is evened out by the taps around it.
    assert.equal(tappedBpm([0, 480, 1020, 1500]), 120);
});

test("a slide after the hold sets the tempo: up or right faster, down or left slower, within the range", () => {
    assert.equal(draggedBpm(80, 0, -40, 4), 90);
    assert.equal(draggedBpm(80, 20, 0, 4), 85);
    assert.equal(draggedBpm(80, -8, 12, 4), 75);
    assert.equal(draggedBpm(80, 0, 400, 4), MIN_BPM);
    assert.equal(draggedBpm(200, 400, 0, 4), MAX_BPM);
    assert.equal(draggedBpm(80, 0, 0, 4), 80, "a hold without a slide keeps the tempo");
    assert.equal(draggedBpm(80, 0, -40, Number.NaN), 80, "no step token, no change");
});

test("what is kept for a piece says whether the metronome follows the piece tempo, as the iPad keeps it", () => {
    assert.deepEqual(readKept("-,3"), { own: null, beats: 3 });
    assert.deepEqual(readKept("100,5"), { own: 100, beats: 4 });
    assert.deepEqual(readKept("500,4"), { own: MAX_BPM, beats: 4 });
    assert.equal(readKept("x,4"), null);
    assert.equal(readKept(null), null);
    assert.equal(keptText(null, 2), "-,2");
    assert.equal(keptText(96, 4), "96,4");
});

test("the metronome follows the piece tempo, not the pace, until the player gives it one", () => {
    const setting = new MetronomeSetting();
    setting.load("p", null, 96, 2);
    assert.equal(setting.bpm, 96, "the piece tempo, as the score's mark shows it");
    assert.equal(setting.own, null);
    assert.equal(setting.beats, 2, "the first bar's beats");

    assert.equal(setting.change(100, 2), "100,2");
    setting.load("p", "100,2", 120, 2);
    assert.equal(setting.bpm, 100, "the player's own tempo stays when the piece tempo changes");

    assert.equal(setting.change(120, 2), "-,2", "set back to the piece tempo, it follows the piece again");
    setting.load("p", "-,2", 90, 2);
    assert.equal(setting.bpm, 90);
    setting.load("p", "-,2");
    assert.equal(setting.bpm, 90, "the PDF view of the same piece keeps its piece tempo");

    setting.load("q", null);
    assert.equal(setting.bpm, PIECE_DEFAULT_BPM, "another piece with no tempo is at the default");
    assert.equal(setting.beats, 4);
    setting.load("q", "88,3");
    assert.deepEqual([setting.bpm, setting.beats], [88, 3], "a tempo kept before v0.5.5 is the player's own");
});
