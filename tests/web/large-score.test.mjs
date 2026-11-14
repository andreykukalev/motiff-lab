// The large-score benchmark (#419): a synthetic piece the size of a transcribed half-hour concert recording, written by
// tools/perf/large-score.mjs and measured by tools/perf/measure.mjs. The player's own recording is never committed.
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";

import { BARS, OUTPUT, midi, notes } from "../../tools/perf/large-score.mjs";

const root = new URL("../../", import.meta.url);

test("the benchmark file is what the script writes", () => {
    assert.ok(readFileSync(new URL(OUTPUT, root)).equals(midi()), "run node tools/perf/large-score.mjs");
});

test("it is the size of the recording: about 830 bars and 8,200 notes on two hands", () => {
    const { right, left } = notes();
    assert.equal(BARS, 829);
    assert.ok(right.length + left.length >= 8000 && right.length + left.length <= 8400, `${right.length + left.length} notes`);
    assert.ok(right.length > 0 && left.length > 0);
    const end = Math.max(...[...right, ...left].map(([, stop]) => stop));
    assert.equal(end, BARS * 4 * 480);
});

test("every note lies on the piano and has a length", () => {
    for (const [onset, end, pitch, velocity] of [...notes().right, ...notes().left]) {
        assert.ok(end > onset && pitch >= 21 && pitch <= 108 && velocity > 0 && velocity < 128);
    }
});
