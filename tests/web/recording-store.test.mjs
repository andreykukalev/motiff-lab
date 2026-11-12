// Tests for the sound kept on the device (0024, scenario 2; version 0.5, a piece split from a recording), run in Node on
// the compiled module:
//   dotnet build src/MotiffLab.Web && node --test tests/web

import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { test } from "node:test";

import { SAMPLE_RATE, encodeWav } from "../../src/MotiffLab.Web/wwwroot/js/import/recording.js";
import { CONCERT, LENGTH_SLACK_MILLISECONDS, SOUND, describe, isSameSound, keepHeldStretch, recordingKey, sampleOf, sha256Of } from "../../src/MotiffLab.Web/wwwroot/js/storage/recording-store.js";

test("a piece's own sound and the stretch of its recording heard are kept beside the project's versions", () => {
    assert.equal(recordingKey("p_1", SOUND), "p_1/src_sound");
    assert.equal(recordingKey("p_1", CONCERT), "p_1/src_concert");
});

test("a millisecond of sound is its first sample, within the sound", () => {
    assert.equal(sampleOf(0, 100_000), 0);
    assert.equal(sampleOf(1000, 100_000), SAMPLE_RATE);
    assert.equal(sampleOf(-5, 100_000), 0);
    assert.equal(sampleOf(10_000, 100), 100);
});

test("a WAV file is described by its size, its length and its SHA-256 in lower-case hex", async () => {
    const wav = encodeWav(new Float32Array(SAMPLE_RATE * 2).fill(0.25), SAMPLE_RATE);
    const described = await describe(wav);
    assert.equal(described.bytes, 44 + SAMPLE_RATE * 2 * 2);
    assert.equal(described.milliseconds, 2000);
    assert.equal(described.sha256, createHash("sha256").update(wav).digest("hex"));
    assert.match(await sha256Of(new Uint8Array(0)), /^e3b0c442[0-9a-f]{56}$/);
});

test("a stretch picked again is the piece's sound by its fingerprint, or else by its length within a second", () => {
    const cut = { bytes: 100, milliseconds: 60_000, sha256: "a".repeat(64) };
    assert.equal(isSameSound(cut, "a".repeat(64), 1), true);
    assert.equal(isSameSound(cut, "b".repeat(64), 60_000), false);
    assert.equal(isSameSound(cut, null, 60_000 + LENGTH_SLACK_MILLISECONDS), true);
    assert.equal(isSameSound(cut, null, 60_000 - LENGTH_SLACK_MILLISECONDS - 1), false);
    assert.equal(isSameSound(cut, null, null), true);
});

test("with no recording held, nothing is kept", async () => {
    assert.equal(await keepHeldStretch("p_1/src_concert", 0, 1000), false);
});
