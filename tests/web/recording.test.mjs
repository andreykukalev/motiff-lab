// Tests for taking the sound out of a recording in the browser (0014), run in Node on the compiled module:
//   dotnet build src/MotiffLab.Web && node --test tests/web

import assert from "node:assert/strict";
import { test } from "node:test";

import { SAMPLE_RATE, encodeWav, forget, load, mixToMono, peaksOf, soundBetween } from "../../src/MotiffLab.Web/wwwroot/js/import/recording.js";

test("stereo is averaged to mono and cut to the length asked for", () => {
    const left = Float32Array.from([1, 0.5, -1, 0.25]);
    const right = Float32Array.from([0, 0.5, 1, 0.75]);
    assert.deepEqual([...mixToMono([left, right], 3)], [0.5, 0.5, 0]);
    assert.deepEqual([...mixToMono([], 2)], [0, 0]);
});

test("the WAV file is 16-bit mono PCM at 22,050 Hz, with clipped samples", () => {
    const wav = encodeWav(Float32Array.from([0, 1, -1, 2]), SAMPLE_RATE);
    const view = new DataView(wav.buffer);
    const text = (offset, length) => String.fromCharCode(...wav.subarray(offset, offset + length));
    assert.equal(text(0, 4), "RIFF");
    assert.equal(text(8, 4), "WAVE");
    assert.equal(view.getUint16(20, true), 1);
    assert.equal(view.getUint16(22, true), 1);
    assert.equal(view.getUint32(24, true), 22050);
    assert.equal(view.getUint16(34, true), 16);
    assert.equal(view.getUint32(40, true), 8);
    assert.equal(wav.length, 44 + 8);
    assert.deepEqual([0, 1, 2, 3].map(i => view.getInt16(44 + i * 2, true)), [0, 32767, -32768, 32767]);
});

test("with no file, a file too large, or no audio decoding in this environment, nothing is held to send", async () => {
    assert.equal(await load({ files: [] }, 1000), null);
    assert.equal(await load({ files: [{ size: 8, arrayBuffer: async () => new ArrayBuffer(8) }] }, 1000), null);
    assert.equal(await load({ files: [{ size: 2000, arrayBuffer: async () => new ArrayBuffer(8) }] }, 1000), null);
    assert.equal(soundBetween(0, 1000), null);
    forget();
});

test("the shape of a recording is the loudest sample of each equal stretch", () => {
    const samples = Float32Array.from([0.1, -0.5, 0.2, 0.3, -0.9, 0, 0.25, 2]);
    assert.deepEqual(peaksOf(samples, 4), [0.5, 0.3, 0.9, 1]);
    assert.deepEqual(peaksOf(samples, 3), [0.5, 0.9, 1]);
    assert.deepEqual(peaksOf(Float32Array.from([0.5]), 400), [0.5]);
    assert.deepEqual(peaksOf(new Float32Array(0), 400), []);
});

test("a decoded recording is held, and only the chosen stretch of it is written as a WAV file", async () => {
    const rate = SAMPLE_RATE;
    const left = new Float32Array(rate * 3).fill(0.5);
    const right = new Float32Array(rate * 3).fill(0.5);
    globalThis.OfflineAudioContext = class {
        async decodeAudioData() {
            return { numberOfChannels: 2, length: left.length, getChannelData: i => (i === 0 ? left : right) };
        }
    };
    const urls = [];
    const createObjectURL = URL.createObjectURL;
    const revokeObjectURL = URL.revokeObjectURL;
    URL.createObjectURL = () => "blob:recording";
    URL.revokeObjectURL = url => urls.push(url);
    try {
        const loaded = await load({ files: [{ size: 100, arrayBuffer: async () => new ArrayBuffer(8) }] }, 1000);
        assert.equal(loaded.milliseconds, 3000);
        assert.equal(loaded.url, "blob:recording");
        assert.equal(loaded.peaks.length, 400);
        assert.ok(loaded.peaks.every(peak => peak === 0.5));

        const sound = soundBetween(1000, 2500);
        assert.equal(sound.seconds, 1.5);
        assert.equal(sound.wav.length, 44 + rate * 1.5 * 2);
        assert.equal(soundBetween(2500, 9000).seconds, 0.5);

        forget();
        assert.deepEqual(urls, ["blob:recording"]);
        assert.equal(soundBetween(0, 1000), null);
    } finally {
        delete globalThis.OfflineAudioContext;
        URL.createObjectURL = createObjectURL;
        URL.revokeObjectURL = revokeObjectURL;
    }
});
