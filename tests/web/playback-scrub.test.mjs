// Tests for the engine's scrub (v0.5.2, #495 UX-09, slice W3, Scripts/playback/playback-processor.ts): the notes a dragged
// playhead passes, the same cases the iPad's engine runs (ipad/App/MotiffLabTests/PlaybackTests.swift, Scrubbing). In Node on
// the compiled module:   dotnet build src/MotiffLab.Web && node --test tests/web

import assert from "node:assert/strict";
import { test } from "node:test";

import { Engine, SCRUB_GROUPS, SCRUB_SECONDS } from "../../src/MotiffLab.Web/wwwroot/js/playback/playback-processor.js";

const RATE = 48_000;
const BLOCK = 128;
/** Frames a quarter lasts at 120 BPM, the timeline's tempo. */
const QUARTER_FRAMES = 24_000;
/** The spike sample is silent but for one sample this far in, so each struck note shows as one spike. */
const SPIKE_AT = 40;

/** An engine at 120 BPM over four bars of 4/4: with a sample that spikes once, or one that sounds steadily for four seconds. */
function scrubEngine({ steady = false, samples = true, beats = [] } = {}) {
    const engine = new Engine(RATE);
    if (samples) {
        const data = new Float32Array(steady ? RATE * 4 : RATE);
        if (steady) {
            data.fill(0.5);
        } else {
            data[SPIKE_AT] = 1;
        }
        engine.handle({ type: "samples", regions: [{ pitch: 60, tuneCents: 0, data }] });
    }
    engine.handle({ type: "timeline", tempos: [0, 500_000], beats, bars: [0, 1920, 480, 4], end: 1920 * 4 });
    return engine;
}

/** Notes as [onset, duration, pitch] (and a track, 0 by default), full velocity, made the one active source. */
function hear(engine, notes) {
    engine.handle({ type: "source", id: "a", notes: notes.flatMap(([onset, duration, pitch, track = 0]) => [onset, duration, pitch, 127, track]) });
    engine.handle({ type: "activeSources", ids: ["a"] });
}

/** The left channel of `count` frames, rendered a block at a time as the audio thread does. */
function sound(engine, count) {
    const blocks = Math.ceil(count / BLOCK);
    const out = new Float32Array(blocks * BLOCK);
    const block = new Float32Array(BLOCK);
    for (let index = 0; index < blocks; index++) {
        engine.render(block, null, index * BLOCK);
        out.set(block, index * BLOCK);
    }
    return out;
}

/** The frames the spike sample's notes start on. */
function struck(out) {
    return [...out.keys()].filter((frame) => Math.abs(out[frame]) > 0.01).map((frame) => frame - SPIKE_AT);
}

const seconds = (value) => Math.round(value * RATE);

test("a drag forward sounds the starts it passes and goes there", () => {
    const engine = scrubEngine();
    hear(engine, [[0, 240, 60], [480, 240, 62], [960, 240, 64], [1440, 240, 65]]);
    engine.handle({ type: "scrub", from: 0, to: 1000 });

    assert.equal(engine.tick, 1000);
    assert.equal(engine.transportState, "stopped");
    assert.equal(engine.voiceCount, 2, "the starts at 480 and 960: the one it left is behind it");
    assert.ok(struck(sound(engine, 1024)).length > 0, "they sound at once");
});

test("a drag back sounds the starts it passes the other way", () => {
    const engine = scrubEngine();
    hear(engine, [[0, 240, 60], [480, 240, 62], [960, 240, 64], [1440, 240, 65]]);
    engine.handle({ type: "scrub", from: 1440, to: 480 });

    assert.equal(engine.tick, 480);
    assert.equal(engine.voiceCount, 2, "back, the place reached sounds and the one it left does not: 480 and 960");
});

test("a chord a drag passes sounds whole", () => {
    const engine = scrubEngine();
    hear(engine, [[480, 240, 60], [480, 240, 64], [480, 240, 67], [960, 240, 72]]);
    engine.handle({ type: "scrub", from: 0, to: 600 });

    assert.equal(engine.voiceCount, 3);
});

test("a fast drag strikes only the starts nearest where it went", () => {
    const engine = scrubEngine();
    hear(engine, Array.from({ length: 20 }, (_, index) => [(index + 1) * 120, 120, 41 + index]));
    engine.handle({ type: "scrub", from: 0, to: 2400 });

    assert.equal(SCRUB_GROUPS, 6);
    assert.equal(engine.voiceCount, SCRUB_GROUPS);
});

test("muted tracks and the metronome stay silent while a drag scrubs", () => {
    const clicks = scrubEngine({ samples: false, beats: [0, 1, 480, 0, 960, 0] });
    clicks.handle({ type: "metronome", on: true });
    clicks.handle({ type: "scrub", from: 0, to: 1000 });
    assert.ok(sound(clicks, 4096).every((value) => value === 0), "no click");

    const muted = scrubEngine();
    hear(muted, [[480, 240, 60, 1]]);
    muted.handle({ type: "tracks", muted: [1], soloed: [] });
    muted.handle({ type: "scrub", from: 0, to: 1000 });
    assert.equal(muted.voiceCount, 0);
});

test("a note a drag strikes sounds its length, at most a second and a half, then fades and no voice hangs", () => {
    // 240 ticks at 120 BPM are a quarter of a second; 3840 are four seconds, held a second and a half.
    for (const [duration, held] of [[240, 0.25], [3840, SCRUB_SECONDS]]) {
        const engine = scrubEngine({ steady: true });
        hear(engine, [[480, duration, 60]]);
        engine.handle({ type: "scrub", from: 0, to: 480 });
        const out = sound(engine, seconds(held + 0.6));
        const full = Math.abs(out[seconds(0.01)]);

        assert.ok(full > 0, `${duration}: it sounds`);
        assert.ok(Math.abs(Math.abs(out[seconds(held - 0.01)]) - full) <= full * 0.01, `${duration}: held its length`);
        assert.ok(Math.abs(out[seconds(held + 0.5)]) < full * 0.05, `${duration}: then fading`);
        sound(engine, RATE);
        assert.equal(engine.voiceCount, 0, `${duration}: no voice hangs`);
    }
    assert.equal(SCRUB_SECONDS, 1.5);
});

test("letting go fades what the drag sounds, and the position stays where it left it", () => {
    const engine = scrubEngine({ steady: true });
    hear(engine, [[480, 3840, 60]]);
    engine.handle({ type: "scrub", from: 0, to: 480 });
    const before = sound(engine, seconds(0.1));
    engine.handle({ type: "scrubEnd" });
    const after = sound(engine, seconds(0.6));

    assert.ok(Math.abs(after.at(-1)) < Math.abs(before.at(-1)) * 0.05);
    sound(engine, RATE);
    assert.equal(engine.voiceCount, 0);
    assert.equal(engine.tick, 480);
});

test("a drag is ignored while playing", () => {
    const engine = scrubEngine();
    hear(engine, [[0, 1920, 60], [960, 240, 64]]);
    engine.handle({ type: "play" });
    sound(engine, QUARTER_FRAMES);
    const tick = engine.tick;
    const voices = engine.voiceCount;

    engine.handle({ type: "scrub", from: tick, to: 1500 });

    assert.equal(engine.tick, tick);
    assert.equal(engine.voiceCount, voices);
});

test("Play after a drag starts clean from where it went", () => {
    const engine = scrubEngine();
    // Past the end of the notes it struck, so playback has none of them still sounding to strike again.
    // The note playback strikes is the sample's own pitch, so its spike lands on its frame untransposed.
    hear(engine, [[480, 240, 60], [960, 240, 64], [1440, 240, 60]]);
    engine.handle({ type: "scrub", from: 0, to: 1250 });
    sound(engine, 1024);
    engine.handle({ type: "play" });

    const out = sound(engine, QUARTER_FRAMES * 2);

    assert.deepEqual(struck(out), [Math.ceil(((1440 - 1250) * QUARTER_FRAMES) / 480)], "the piece's next note, on its frame");
});

test("the Play after a drag fades its notes", () => {
    const engine = scrubEngine({ steady: true });
    hear(engine, [[480, 3840, 60]]);
    // Dragged past the note's end: playback from there has nothing of its own to sound.
    engine.handle({ type: "scrub", from: 0, to: 4400 });
    const before = sound(engine, seconds(0.1));
    engine.handle({ type: "play" });
    const after = sound(engine, seconds(0.6));

    assert.ok(Math.abs(after.at(-1)) < Math.abs(before.at(-1)) * 0.05);
});
