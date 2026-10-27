// Tests for the audio-thread playback engine (S1-W4, S1-W5), run in Node on the compiled module:
//   dotnet build src/MotiffLab.Web && node --test tests/web
// The engine renders exactly as it does in an AudioWorklet, 128 frames at a time.

import assert from "node:assert/strict";
import { test } from "node:test";

import { Engine } from "../../src/MotiffLab.Web/wwwroot/js/playback/playback-processor.js";

const RATE = 48_000;
const BLOCK = 128;
const QUARTER = 24_000; // frames per quarter note at 120 BPM
const SPIKE = 40; // the test sample is silent except for one spike, this many frames after its start

/** One sample at middle C: silence with a single spike, so every note shows as one spike in the output. */
function testRegion() {
    const data = new Float32Array(RATE);
    data[SPIKE] = 1;
    return { pitch: 60, tuneCents: 0, data };
}

function engine({ tempos = [0, 500_000], beats = [], bars = [0, 1920, 480, 4], end = 1920 * 4, samples = true } = {}) {
    const e = new Engine(RATE);
    if (samples) {
        e.handle({ type: "samples", regions: [testRegion()] });
    }
    e.handle({ type: "timeline", tempos, beats, bars, end });
    e.takeReports();
    return e;
}

/** Notes as [onset, duration, track] at middle C, full velocity. */
function source(e, id, notes) {
    e.handle({ type: "source", id, notes: notes.flatMap(([onset, duration, track = 0]) => [onset, duration, 60, 127, track]) });
}

/** Renders `frames` frames (a multiple of the block size) and returns the left channel. */
function run(e, frames, startFrame = 0) {
    const out = new Float32Array(frames);
    const left = new Float32Array(BLOCK);
    const right = new Float32Array(BLOCK);
    for (let at = 0; at < frames; at += BLOCK) {
        e.render(left, right, startFrame + at);
        out.set(left, at);
    }
    return out;
}

/** Frames where a note's spike lands, given as the note's start frame. */
function noteStarts(out) {
    const starts = [];
    for (let i = 0; i < out.length; i++) {
        if (Math.abs(out[i]) > 0.01) {
            starts.push(i - SPIKE);
        }
    }
    return starts;
}

/** Frames where a sound starts after at least `gap` frames of silence (for metronome clicks). */
function onsets(out, gap = 1000) {
    const found = [];
    let silent = gap;
    for (let i = 0; i < out.length; i++) {
        if (Math.abs(out[i]) > 1e-6) {
            if (silent >= gap) {
                found.push(i);
            }
            silent = 0;
        } else {
            silent++;
        }
    }
    return found;
}

const frames = (n) => Math.ceil(n / BLOCK) * BLOCK;

test("notes start on their exact frame", () => {
    const e = engine();
    source(e, "a", [[0, 240], [480, 240], [720, 120], [1000, 240]]);
    e.handle({ type: "activeSources", ids: ["a"] });
    e.handle({ type: "play" });

    const out = run(e, frames(QUARTER * 3));

    // Tick 1000 is 1000/480 quarters: 50 000 frames.
    assert.deepEqual(noteStarts(out), [0, QUARTER, QUARTER * 1.5, 50_000]);
});

test("tempo scales from 25% to 150%, and is clamped to that range", () => {
    for (const [rate, expected] of [[0.5, QUARTER * 2], [1.5, QUARTER / 1.5], [0.25, QUARTER * 4], [3, QUARTER / 1.5], [0.1, QUARTER * 4]]) {
        const e = engine();
        source(e, "a", [[480, 240]]);
        e.handle({ type: "activeSources", ids: ["a"] });
        e.handle({ type: "rate", value: rate });
        e.handle({ type: "play" });

        const out = run(e, frames(QUARTER * 4 + 1000));

        assert.deepEqual(noteStarts(out), [Math.ceil(expected)], `rate ${rate}`);
    }
});

test("tempo changes apply at their tick", () => {
    // 120 BPM, then 240 BPM from beat 2: tick 960 is one quarter at 120 and one at 240.
    const e = engine({ tempos: [0, 500_000, 480, 250_000] });
    source(e, "a", [[960, 240]]);
    e.handle({ type: "activeSources", ids: ["a"] });
    e.handle({ type: "play" });

    const out = run(e, frames(QUARTER * 2));

    assert.deepEqual(noteStarts(out), [QUARTER + QUARTER / 2]);
});

test("a loop wraps on the exact frame, with no gap", () => {
    const e = engine();
    source(e, "a", [[0, 240], [240, 240], [480, 240]]);
    e.handle({ type: "activeSources", ids: ["a"] });
    e.handle({ type: "loop", start: 0, end: 480 });
    e.handle({ type: "play" });

    const out = run(e, frames(QUARTER * 3 + 100));

    // The note at 480 is outside the loop and never plays.
    assert.deepEqual(noteStarts(out), [0, QUARTER / 2, QUARTER, QUARTER * 1.5, QUARTER * 2, QUARTER * 2.5, QUARTER * 3]);
});

test("a loop that does not start at a block boundary keeps exact time", () => {
    // Loop of 100 ticks: 5000 frames, not a multiple of 128.
    const e = engine();
    source(e, "a", [[1000, 50]]);
    e.handle({ type: "activeSources", ids: ["a"] });
    e.handle({ type: "loop", start: 1000, end: 1100 });
    e.handle({ type: "seek", tick: 1000 });
    e.handle({ type: "play" });

    const out = run(e, frames(5000 * 4 + 100));

    assert.deepEqual(noteStarts(out), [0, 5000, 10_000, 15_000, 20_000]);
});

test("the metronome clicks every beat", () => {
    const e = engine({ beats: [0, 1, 480, 0, 960, 0, 1440, 0, 1920, 1], samples: false });
    e.handle({ type: "metronome", on: true });
    e.handle({ type: "play" });

    const out = run(e, frames(QUARTER * 4 + 1000));

    // A click is a sine starting at phase 0, so it is first heard one frame after its beat.
    assert.deepEqual(onsets(out), [1, QUARTER + 1, QUARTER * 2 + 1, QUARTER * 3 + 1, QUARTER * 4 + 1]);
});

test("count-in plays one bar of clicks, then the music", () => {
    const e = engine({ bars: [0, 1440, 480, 3, 1440, 1440, 480, 3] });
    source(e, "a", [[1440, 240]]);
    e.handle({ type: "activeSources", ids: ["a"] });
    e.handle({ type: "countIn", bars: 1 });
    e.handle({ type: "seek", tick: 1440 });
    e.handle({ type: "play" });
    assert.equal(e.transportState, "countingIn");

    const out = run(e, frames(QUARTER * 3 + 1000));

    const clicks = onsets(out).filter((frame) => frame < QUARTER * 3);
    assert.deepEqual(clicks, [1, QUARTER + 1, QUARTER * 2 + 1]);
    assert.equal(e.transportState, "playing");
    const spike = out.findIndex((value, i) => i > QUARTER * 3 && Math.abs(value) > 0.01);
    assert.equal(spike - SPIKE, QUARTER * 3);
});

test("mute and solo per track", () => {
    for (const [muted, soloed, expected] of [
        [[1], [], [0]],
        [[], [1], [QUARTER]],
        [[], [], [0, QUARTER]],
        [[1], [1], []],
    ]) {
        const e = engine();
        source(e, "a", [[0, 240, 0], [480, 240, 1]]);
        e.handle({ type: "activeSources", ids: ["a"] });
        e.handle({ type: "tracks", muted, soloed });
        e.handle({ type: "play" });

        assert.deepEqual(noteStarts(run(e, frames(QUARTER * 2))), expected, `muted ${muted}, soloed ${soloed}`);
    }
});

test("switching source keeps the musical position", () => {
    const e = engine();
    source(e, "original", [[0, 240], [960, 240]]);
    source(e, "variation", [[480, 240], [1440, 240]]);
    e.handle({ type: "activeSources", ids: ["original"] });
    e.handle({ type: "play" });

    const first = run(e, frames(QUARTER * 1.5));
    const tickBefore = e.tick;
    e.handle({ type: "activeSources", ids: ["variation"] });
    assert.equal(e.tick, tickBefore);
    const second = run(e, frames(QUARTER * 2.5), first.length);

    assert.deepEqual(noteStarts(first), [0]);
    // The variation's note at 1440 lands where it would have without the switch.
    assert.deepEqual(noteStarts(second).map((frame) => frame + first.length), [QUARTER * 3]);
});

test("both sources can sound together", () => {
    const e = engine();
    source(e, "original", [[0, 240]]);
    source(e, "variation", [[480, 240]]);
    e.handle({ type: "activeSources", ids: ["original", "variation"] });
    e.handle({ type: "play" });

    assert.deepEqual(noteStarts(run(e, frames(QUARTER * 2))), [0, QUARTER]);
});

test("a note already sounding is struck when its source becomes active", () => {
    const e = engine();
    source(e, "original", []);
    source(e, "variation", [[0, 1920]]);
    e.handle({ type: "activeSources", ids: ["original"] });
    e.handle({ type: "play" });
    run(e, frames(QUARTER));

    e.handle({ type: "activeSources", ids: ["variation"] });

    assert.equal(e.voiceCount, 1);
});

test("pause keeps the position and play resumes from it", () => {
    const e = engine();
    source(e, "a", [[960, 240]]);
    e.handle({ type: "activeSources", ids: ["a"] });
    e.handle({ type: "play" });
    run(e, frames(QUARTER));
    e.handle({ type: "pause" });
    const paused = e.tick;
    run(e, frames(QUARTER));
    assert.equal(e.tick, paused);

    e.handle({ type: "play" });
    const out = run(e, frames(QUARTER * 2));

    assert.deepEqual(noteStarts(out), [Math.ceil((960 - paused) * (QUARTER / 480))]);
});

test("playback stops at the end and reports it", () => {
    const e = engine({ end: 960 });
    source(e, "a", [[0, 240]]);
    e.handle({ type: "activeSources", ids: ["a"] });
    e.handle({ type: "play" });

    run(e, frames(QUARTER * 3));

    assert.equal(e.transportState, "stopped");
    assert.equal(e.tick, 0);
    const reports = e.takeReports();
    assert.ok(reports.some((report) => report.type === "ended" && report.tick === 960));
});

test("positions are reported about 30 times a second with their frame", () => {
    const e = engine();
    source(e, "a", []);
    e.handle({ type: "activeSources", ids: ["a"] });
    e.handle({ type: "play" });
    e.takeReports();

    run(e, frames(RATE));

    const positions = e.takeReports().filter((report) => report.type === "position");
    assert.ok(positions.length >= 29 && positions.length <= 31, `${positions.length} reports`);
    for (const report of positions) {
        assert.equal(report.state, "playing");
        assert.ok(Math.abs(report.tick - (report.frame * 480) / QUARTER) < 1e-6, `tick ${report.tick} at frame ${report.frame}`);
        assert.equal(report.ticksPerSecond, 960);
    }
});

test("the voice count stays bounded", () => {
    const e = engine();
    const notes = Array.from({ length: 400 }, (_, i) => [i, 1920]);
    source(e, "a", notes);
    e.handle({ type: "activeSources", ids: ["a"] });
    e.handle({ type: "play" });

    run(e, frames(QUARTER));

    assert.ok(e.voiceCount <= 96);
});

test("a live note sounds at once while stopped and stops when released", () => {
    const e = engine();
    e.handle({ type: "noteOn", pitch: 60, velocity: 100 });
    assert.deepEqual(noteStarts(run(e, BLOCK)), [0]);
    e.handle({ type: "noteOff", pitch: 60 });
    const after = run(e, BLOCK * 200);
    assert.ok(after.slice(BLOCK * 100).every((sample) => Math.abs(sample) < 1e-3), "released note fades out");
});

test("a live note keeps sounding over pause, seek and source changes", () => {
    const e = engine();
    source(e, "original", [[0, 480]]);
    e.handle({ type: "activeSources", ids: ["original"] });
    e.handle({ type: "play" });
    run(e, BLOCK);
    e.handle({ type: "noteOn", pitch: 60, velocity: 127 });
    e.handle({ type: "pause" });
    e.handle({ type: "seek", tick: 960 });
    e.handle({ type: "activeSources", ids: [] });
    e.handle({ type: "tracks", muted: [0], soloed: [0] });
    // The live voice's spike lands SPIKE frames into the next block: it was not released.
    assert.deepEqual(noteStarts(run(e, BLOCK)), [0]);
});

/** The original over a quarter, then the variation over two quarters, as "One after the other" plays them (#43). */
function alternating() {
    const e = engine();
    source(e, "original", [[0, 120], [240, 120]]);
    source(e, "variation", [[120, 120], [600, 120]]);
    e.handle({ type: "alternate", passes: [{ ids: ["original"], start: 0, end: 480 }, { ids: ["variation"], start: 0, end: 960 }] });
    return e;
}

test("an alternating loop plays each pass's sources over its own range, switching on the exact frame", () => {
    const e = alternating();
    e.handle({ type: "play" });

    const out = run(e, frames(QUARTER * 4 + 1000));

    // The original over 0–480 (one quarter), the variation over 0–960 from frame QUARTER, then the original
    // again from QUARTER * 3. Neither source sounds in the other's pass.
    assert.deepEqual(noteStarts(out), [0, QUARTER / 2, QUARTER * 1.25, QUARTER * 2.25, QUARTER * 3, QUARTER * 3.5]);
});

test("an alternating loop reports which pass plays, with its range, on the frame it starts", () => {
    const e = alternating();
    e.handle({ type: "play" });
    e.takeReports();

    run(e, frames(QUARTER * 3.5));

    const positions = e.takeReports().filter((report) => report.type === "position");
    const starts = positions.filter((report, i) => i > 0 && report.pass !== positions[i - 1].pass);
    assert.deepEqual(
        // The tick carries the overshoot across the wrap, under a frame's worth.
        starts.map(({ frame, tick, pass, loopStart, loopEnd }) => ({ frame, tick: Math.round(tick * 1e6) / 1e6, pass, loopStart, loopEnd })),
        [
            { frame: QUARTER, tick: 0, pass: 1, loopStart: 0, loopEnd: 960 },
            { frame: QUARTER * 3, tick: 0, pass: 0, loopStart: 0, loopEnd: 480 },
        ],
    );
    assert.equal(positions[0].pass, 0);
});

test("an alternating loop starts at its first pass and is deterministic", () => {
    const outputs = [];
    for (let i = 0; i < 2; i++) {
        const e = engine();
        source(e, "original", [[0, 120], [240, 120]]);
        source(e, "variation", [[120, 120], [600, 120]]);
        e.handle({ type: "seek", tick: 1500 });
        e.handle({ type: "alternate", passes: [{ ids: ["original"], start: 0, end: 480 }, { ids: ["variation"], start: 0, end: 960 }] });
        assert.equal(e.tick, 0);
        e.handle({ type: "play" });
        outputs.push(Array.from(run(e, frames(QUARTER * 4))));
    }

    assert.deepEqual(outputs[0], outputs[1]);
});

test("a loop, a loop off or a choice of sources ends an alternating loop", () => {
    for (const message of [{ type: "loop", start: 0, end: 480 }, { type: "loopOff" }, { type: "activeSources", ids: ["original"] }]) {
        const e = alternating();
        e.handle(message);
        e.handle({ type: "play" });
        e.takeReports();

        run(e, frames(QUARTER * 2));

        const positions = e.takeReports().filter((report) => report.type === "position");
        assert.ok(positions.length > 0 && positions.every((report) => report.pass === -1), message.type);
    }
});

test("an alternating loop with no pass inside the piece loops nothing", () => {
    const e = engine({ end: 960 });
    source(e, "original", [[0, 120]]);
    e.handle({ type: "alternate", passes: [{ ids: ["original"], start: 2000, end: 3000 }] });
    e.takeReports();
    e.handle({ type: "play" });

    const report = e.takeReports().find((r) => r.type === "position");
    assert.equal(report.pass, -1);
    assert.equal(report.loopEnd, 0);
});
