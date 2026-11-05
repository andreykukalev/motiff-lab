// The metronome of the PDF view (decision record 0023): the tempo stays in range and tapping finds it.
import assert from "node:assert/strict";
import { test } from "node:test";

import { MAX_BPM, MIN_BPM, clampBpm, tappedBpm } from "../../src/MotiffLab.Web/wwwroot/js/metronome/metronome.js";

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
