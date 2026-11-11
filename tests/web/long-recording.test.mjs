// Tests for hearing recordings of any length (0014, version 0.3), run in Node on the compiled modules:
//   dotnet build src/MotiffLab.Web && node --test tests/web
// Decoding a block at a time (WAV, and the MP4 table of samples handed to WebCodecs), resampling, the loudness levels
// music-core puts the seams by, and sending parts several at once with retries, keeping each part heard until joined.

import assert from "node:assert/strict";
import { test } from "node:test";

import { decodeInBlocks, wavLayout } from "../../src/MotiffLab.Web/wwwroot/js/import/decode.js";
import { MOST_TRIES, Pacer, START_AT_ONCE, hearParts, keyPrefix, partKey } from "../../src/MotiffLab.Web/wwwroot/js/import/hearing.js";
import { aacLayout, codecOf, decodeMp4, mp4Track } from "../../src/MotiffLab.Web/wwwroot/js/import/mp4.js";
import { fingerprintOf, forget, levelsBetween, load } from "../../src/MotiffLab.Web/wwwroot/js/import/recording.js";
import { MonoResampler, SAMPLE_RATE, toInt16 } from "../../src/MotiffLab.Web/wwwroot/js/import/resample.js";

/** A file the decoders read in slices. */
function source(bytes) {
    return { size: bytes.length, slice: (start, end) => ({ arrayBuffer: async () => bytes.slice(start, end).buffer }) };
}

/** A 16-bit PCM WAV file of interleaved samples. */
function wav(samples, channels, rate) {
    const bytes = new Uint8Array(44 + samples.length * 2);
    const view = new DataView(bytes.buffer);
    const put = (at, text) => [...text].forEach((c, i) => view.setUint8(at + i, c.charCodeAt(0)));
    put(0, "RIFF");
    view.setUint32(4, 36 + samples.length * 2, true);
    put(8, "WAVE");
    put(12, "fmt ");
    view.setUint32(16, 16, true);
    view.setUint16(20, 1, true);
    view.setUint16(22, channels, true);
    view.setUint32(24, rate, true);
    view.setUint32(28, rate * channels * 2, true);
    view.setUint16(32, channels * 2, true);
    view.setUint16(34, 16, true);
    put(36, "data");
    view.setUint32(40, samples.length * 2, true);
    samples.forEach((s, i) => view.setInt16(44 + i * 2, s, true));
    return bytes;
}

test("sound of any rate becomes mono at 22,050 Hz, the same in blocks as at once, with no folding back of high notes", () => {
    const rate = 44100;
    const tone = freq => Float32Array.from({ length: rate }, (_, i) => 0.5 * Math.sin((2 * Math.PI * freq * i) / rate));
    const peak = samples => samples.slice(1000, -1000).reduce((m, s) => Math.max(m, Math.abs(s) / 32768), 0);

    const whole = new MonoResampler(rate);
    whole.push([tone(440), tone(440)], rate);
    const once = whole.finish();
    assert.equal(once.length, SAMPLE_RATE);
    assert.ok(Math.abs(peak(once) - 0.5) < 0.01);

    const blocks = new MonoResampler(rate);
    const a = tone(440);
    for (let at = 0; at < rate; at += 777) {
        const end = Math.min(rate, at + 777);
        blocks.push([a.subarray(at, end), a.subarray(at, end)], end - at);
    }
    assert.deepEqual(blocks.finish(), once);

    const high = new MonoResampler(rate);
    high.push([tone(15000)], rate);
    assert.ok(peak(high.finish()) < 0.01, "a 15 kHz tone would fold back to 7 kHz");

    const same = new MonoResampler(SAMPLE_RATE);
    same.push([Float32Array.from([0, 0.5, -1, 1])], 4);
    assert.deepEqual([...same.finish()], [0, toInt16(0.5), -32768, 32767]);
    assert.throws(() => new MonoResampler(0));
});

test("a WAV file is read a block at a time, whatever its size", async () => {
    const frames = 44100 * 3;
    const samples = Array.from({ length: frames * 2 }, (_, i) => (i % 2 === 0 ? 8000 : -8000));
    const file = wav(samples, 2, 44100);
    const layout = wavLayout(file.subarray(0, 64), file.length);
    assert.deepEqual(layout, { float: false, channels: 2, rate: 44100, bits: 16, dataOffset: 44, dataBytes: frames * 4 });

    // The two channels cancel out: mono is silence, three seconds of it.
    const mono = await decodeInBlocks(source(file));
    assert.equal(mono.length, SAMPLE_RATE * 3);
    assert.ok(mono.every(s => Math.abs(s) <= 1));

    assert.equal(wavLayout(new TextEncoder().encode("RIFF....AVI LIST"), 16), null);
    assert.equal(await decodeInBlocks({ size: 100, arrayBuffer: async () => new ArrayBuffer(100) }), null);
});

/** An MP4 box of a type around its contents. */
function box(type, ...parts) {
    const body = parts.flatMap(part => [...part]);
    const bytes = new Uint8Array(8 + body.length);
    new DataView(bytes.buffer).setUint32(0, bytes.length);
    [...type].forEach((c, i) => (bytes[4 + i] = c.charCodeAt(0)));
    bytes.set(body, 8);
    return bytes;
}

const u32 = (...values) => values.flatMap(v => [(v >>> 24) & 255, (v >>> 16) & 255, (v >>> 8) & 255, v & 255]);
const u16 = (...values) => values.flatMap(v => [(v >>> 8) & 255, v & 255]);

/** An M4A file with three AAC frames of 4, 5 and 6 bytes, the first two in one chunk, after its "moov". */
function m4a() {
    const asc = [0x12, 0x10];
    const esds = box("esds", u32(0), [3, 23, 0, 1, 0], [4, 15, 0x40, 0x15, 0, 0, 0], u32(0, 0), [5, 2, ...asc]);
    const mp4a = box("mp4a", [0, 0, 0, 0, 0, 0], u16(1), u16(0, 0), u32(0), u16(2, 16, 0, 0), u32(44100 * 65536), esds);
    const stbl = box(
        "stbl",
        box("stsd", u32(0, 1), mp4a),
        box("stts", u32(0, 1, 3, 1024)),
        box("stsc", u32(0, 2, 1, 2, 1, 2, 1, 1)),
        box("stsz", u32(0, 0, 3, 4, 5, 6)),
        box("stco", u32(0, 2, 0, 0)),
    );
    const trak = box("trak", box("mdia", box("mdhd", u32(0, 0, 0, 44100, 3072)), box("hdlr", u32(0, 0), [..."soun"].map(c => c.charCodeAt(0)), u32(0, 0, 0)), box("minf", stbl)));
    const ftyp = box("ftyp", [..."M4A "].map(c => c.charCodeAt(0)), u32(0));
    const mdat = box("mdat", [1, 1, 1, 1, 2, 2, 2, 2, 2, 3, 3, 3, 3, 3, 3]);
    const moov = box("moov", trak);
    const file = new Uint8Array(ftyp.length + mdat.length + moov.length);
    file.set(ftyp);
    file.set(mdat, ftyp.length);
    file.set(moov, ftyp.length + mdat.length);
    // The chunk offsets point into "mdat": the first chunk at its start, the second after the first two frames.
    const view = new DataView(file.buffer);
    const stco = file.length - 8;
    const start = ftyp.length + 8;
    view.setUint32(stco, start);
    view.setUint32(stco + 4, start + 9);
    return { file, start };
}

test("an M4A file's table of samples says where each AAC frame lies and how to decode it", async () => {
    const { file, start } = m4a();
    const track = await mp4Track(source(file));
    assert.equal(track.codec, "mp4a.40.2");
    assert.deepEqual([...track.description], [0x12, 0x10]);
    assert.equal(track.sampleRate, 44100);
    assert.equal(track.channels, 2);
    assert.deepEqual([...track.sizes], [4, 5, 6]);
    assert.deepEqual([...track.offsets], [start, start + 4, start + 9]);
    assert.deepEqual([...track.times], [0, 1024, 2048]);
    assert.deepEqual(aacLayout(Uint8Array.from([0x11, 0x90])), { sampleRate: 48000, channels: 2 });
    assert.equal(codecOf(Uint8Array.from([0, 0, 0, 0, 9, 0]), 0, 6), null);
    assert.equal(await mp4Track(source(new TextEncoder().encode("not an mp4 file at all"))), null);
});

test("the frames go to WebCodecs a block at a time, and what it decodes is resampled as it comes", async () => {
    const { file } = m4a();
    const track = await mp4Track(source(file));
    const chunks = [];
    globalThis.EncodedAudioChunk = class {
        constructor(init) {
            Object.assign(this, init);
        }
    };
    globalThis.AudioDecoder = class {
        static async isConfigSupported(config) {
            return { supported: config.codec === "mp4a.40.2" };
        }
        constructor({ output }) {
            this.output = output;
            this.state = "unconfigured";
            this.decodeQueueSize = 0;
        }
        configure() {
            this.state = "configured";
        }
        decode(chunk) {
            chunks.push([...chunk.data]);
            const frames = 1024;
            this.output({
                sampleRate: 44100,
                numberOfFrames: frames,
                numberOfChannels: 1,
                format: "f32-planar",
                copyTo: target => target.fill(0.25),
                close() {},
            });
        }
        async flush() {}
        close() {
            this.state = "closed";
        }
    };
    try {
        const mono = await decodeMp4(source(file), track);
        assert.deepEqual(chunks, [[1, 1, 1, 1], [2, 2, 2, 2, 2], [3, 3, 3, 3, 3, 3]]);
        assert.equal(mono.length, Math.round((3072 * SAMPLE_RATE) / 44100));
        assert.equal(mono[700], toInt16(0.25));
        assert.equal(await decodeMp4(source(file), { ...track, codec: "mp4a.40.5" }), null);
    } finally {
        delete globalThis.AudioDecoder;
        delete globalThis.EncodedAudioChunk;
    }
});

test("the held sound tells its loudness in levels of 100 ms and a fingerprint of its samples", async () => {
    const length = SAMPLE_RATE * 2;
    const left = Float32Array.from({ length }, (_, i) => (i < SAMPLE_RATE ? 0.5 : 0));
    globalThis.OfflineAudioContext = class {
        async decodeAudioData() {
            return { numberOfChannels: 1, length, getChannelData: () => left };
        }
    };
    const createObjectURL = URL.createObjectURL;
    URL.createObjectURL = () => "blob:recording";
    try {
        const loaded = await load({ files: [{ size: 100, arrayBuffer: async () => new ArrayBuffer(8) }] }, 1000);
        assert.equal(loaded.milliseconds, 2000);
        const levels = levelsBetween(0, 2000);
        assert.equal(levels.length, 20);
        assert.ok(levels.slice(0, 10).every(level => level === toInt16(0.5)));
        assert.ok(levels.slice(10).every(level => level === 0));
        // From 0.95 s to 1.05 s, half the samples are loud.
        assert.deepEqual(levelsBetween(950, 1150), [Math.round((toInt16(0.5) * (22050 - 20948)) / (23153 - 20948)), 0]);
        assert.equal(loaded.fingerprint, fingerprintOf(Int16Array.from({ length }, (_, i) => (i < SAMPLE_RATE ? toInt16(0.5) : 0))));
        assert.notEqual(loaded.fingerprint, fingerprintOf(new Int16Array(length)));
        forget();
        assert.deepEqual(levelsBetween(0, 2000), []);
    } finally {
        delete globalThis.OfflineAudioContext;
        URL.createObjectURL = createObjectURL;
    }
});

test("more parts go at once as they come back, and a busy answer drops it to one, once a burst", () => {
    const pacer = new Pacer(4);
    assert.equal(pacer.allowed, START_AT_ONCE);
    let ticket = pacer.ticket();
    pacer.heard(ticket, 130_000, 130);
    pacer.heard(ticket, 130_000, 130);
    assert.equal(pacer.allowed, 3);

    // Requests sent before the change do not count at the new level.
    pacer.heard(ticket, 130_000, 130);
    assert.equal(pacer.allowed, 3);
    ticket = pacer.ticket();
    for (let i = 0; i < 3; i++) {
        pacer.heard(ticket, 130_000, 130);
    }
    assert.equal(pacer.allowed, 4);

    ticket = pacer.ticket();
    pacer.failed(ticket);
    pacer.failed(ticket);
    assert.equal(pacer.allowed, 1);

    // Back to three, one below where it failed; a part that waited behind another does not count.
    ticket = pacer.ticket();
    pacer.heard(ticket, 400_000, 130);
    assert.equal(pacer.allowed, 1);
    pacer.heard(ticket, 130_000, 130);
    assert.equal(pacer.allowed, 2);
    assert.equal(new Pacer(1).allowed, 1);
});

/** A hearer standing in for the browser: a map for a store, no waiting, and a server answering as `answer` says. */
function hearer(answer, kept = new Map(), engine = "transkun 2.0") {
    let inFlight = 0;
    const log = { most: 0, sent: [], progress: [], waits: [] };
    return {
        log,
        kept,
        engine: async () => engine,
        send: async part => {
            inFlight++;
            log.most = Math.max(log.most, inFlight);
            log.sent.push(part.index);
            await new Promise(resolve => setTimeout(resolve, 1));
            inFlight--;
            return answer(part, log.sent.filter(index => index === part.index).length);
        },
        store: {
            get: async key => kept.get(key) ?? null,
            put: async (key, part) => void kept.set(key, part),
            remove: async prefix => [...kept.keys()].filter(key => key.startsWith(prefix)).forEach(key => kept.delete(key)),
        },
        progress: (done, total, retrying) => log.progress.push([done, total, retrying]),
        wait: async milliseconds => void log.waits.push(milliseconds),
        now: () => 0,
    };
}

const parts = count => Array.from({ length: count }, (_, i) => ({ index: i, sendFrom: i * 120_000, sendTo: (i + 1) * 120_000 + 5000, keepFrom: i * 120_000, keepTo: (i + 1) * 120_000 }));
const request = count => ({ address: "https://scan.example/", start: 0, end: count * 120_000, parts: parts(count), most: 4, fingerprint: "f-1" });
const heard = part => ({ heard: { index: part.index, midi: `midi-${part.index}`, engine: "transkun 2.0", message: null } });

test("parts go several at once, each kept as it comes back, and all come back in order", async () => {
    const h = hearer(heard);
    const hearing = await hearParts(request(9), h);
    assert.equal(hearing.failure, null);
    assert.deepEqual(hearing.parts.map(part => part.midi), Array.from({ length: 9 }, (_, i) => `midi-${i}`));
    assert.ok(h.log.most > 1 && h.log.most <= 4, `${h.log.most} at once`);
    assert.equal(h.kept.size, 9);
    assert.deepEqual(h.log.progress.at(-1), [9, 9, false]);
    assert.ok(partKey(request(9), parts(9)[3]).startsWith(keyPrefix(request(9))));
});

test("a busy server is asked again after a pause, and the parts heard before are not sent again", async () => {
    const r = request(4);
    const kept = new Map([[partKey(r, r.parts[0]), { midi: "kept-0", engine: "transkun 2.0", at: 1 }]]);
    const h = hearer((part, tries) => (part.index === 2 && tries < 3 ? { retry: true, failure: { status: 503, kind: "http", message: null } } : heard(part)), kept);
    const hearing = await hearParts(r, h);
    assert.equal(hearing.failure, null);
    assert.deepEqual(hearing.parts.map(part => part.midi), ["kept-0", "midi-1", "midi-2", "midi-3"]);
    assert.ok(!h.log.sent.includes(0));
    assert.equal(h.log.sent.filter(index => index === 2).length, 3);
    assert.equal(h.log.waits.length, 2);
    assert.ok(h.log.progress.some(([, , retrying]) => retrying));
});

test("parts heard by another engine are heard again, and a server that stays busy stops the hearing with the rest kept", async () => {
    const r = request(3);
    const kept = new Map([[partKey(r, r.parts[0]), { midi: "old", engine: "transkun 1.0", at: 1 }]]);
    const h = hearer(part => (part.index === 1 ? { retry: true, failure: { status: 504, kind: "http", message: null } } : heard(part)), kept);
    const hearing = await hearParts(r, h);
    assert.deepEqual(hearing.failure, { status: 504, kind: "http", message: null });
    assert.equal(h.log.sent.filter(index => index === 1).length, MOST_TRIES);
    assert.ok(h.log.sent.includes(0));
    assert.deepEqual(hearing.parts.map(part => part.midi), ["midi-0", "midi-2"]);
    assert.equal(kept.get(partKey(r, r.parts[2])).midi, "midi-2");
});

test("a server never reached is not asked again and again, and a refusal stops at once", async () => {
    const unreachable = hearer(() => ({ retry: true, failure: { status: 0, kind: "network", message: null } }), new Map(), null);
    const lost = await hearParts(request(3), unreachable);
    assert.equal(lost.failure.kind, "network");
    assert.ok(unreachable.log.sent.length <= START_AT_ONCE);

    const refused = hearer(() => ({ failure: { status: 403, kind: "http", message: "Pro" } }));
    const stopped = await hearParts(request(5), refused);
    assert.equal(stopped.failure.status, 403);
    assert.ok(refused.log.sent.length <= START_AT_ONCE);

    // A quiet part is heard, with no notes.
    const quiet = hearer(part => (part.index === 1 ? { heard: { index: 1, midi: null, engine: "transkun 2.0", message: "No notes" } } : heard(part)));
    const some = await hearParts({ ...request(2), fingerprint: "" }, quiet);
    assert.deepEqual(some.parts.map(part => part.midi), ["midi-0", null]);
    assert.equal(quiet.kept.size, 0, "a sound with no fingerprint keeps nothing");
});
