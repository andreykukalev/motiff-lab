// Tests for taking the sound out of a recording in the browser (0014), run in Node on the compiled module:
//   dotnet build src/MotiffLab.Web && node --test tests/web

import assert from "node:assert/strict";
import { test } from "node:test";

import { SAMPLE_RATE, encodeWav, mixToMono, soundOf } from "../../src/MotiffLab.Web/wwwroot/js/import/recording.js";

test("stereo is averaged to mono and cut to the length asked for", () => {
    const left = Float32Array.from([1, 0.5, -1, 0.25]);
    const right = Float32Array.from([0, 0.5, 1, 0.75]);
    assert.deepEqual([...mixToMono([left, right], 3)], [0.5, 0.5, 0]);
    assert.deepEqual([...mixToMono([], 2)], [0, 0]);
});

test("the WAV file is 16-bit mono PCM at Basic Pitch's rate, with clipped samples", () => {
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

test("with no file, or no audio decoding in this environment, there is no sound to send", async () => {
    assert.equal(await soundOf({ files: [] }, 600), null);
    assert.equal(await soundOf({ files: [{ arrayBuffer: async () => new ArrayBuffer(8) }] }, 600), null);
});
