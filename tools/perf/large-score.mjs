// The large-score benchmark piece (#419): a synthetic piano piece the size of a transcribed half-hour concert recording,
// about 830 bars and 8,200 notes on two hands, which engraves to about 70 A4 pages. The player's own recording is
// copyrighted, so the benchmark is made here, from a fixed seed: sections in different keys, a right hand of quavers
// and crotchets over a left hand of broken chords, as a standard MIDI file (format 1, a track per hand).
//   tests/e2e/fixtures/large-score.mid   opened by tools/perf/measure.mjs
// Run after changing this script:   node tools/perf/large-score.mjs
// tests/web/large-score.test.mjs fails when the written file is not what this script gives.

import { writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

export const OUTPUT = "tests/e2e/fixtures/large-score.mid";

export const BARS = 829;
const QUARTER = 480;
const EIGHTH = QUARTER / 2;
const BAR = 4 * QUARTER;
/** Crotchet = 104: 829 bars of 4/4 last about 32 minutes. */
const TEMPO_US = Math.round(60_000_000 / 104);
const SECTION_BARS = 16;

/** The right hand's rhythms, in quavers, each filling a bar. */
const RIGHT_RHYTHMS = [
    [1, 1, 1, 1, 1, 1, 2],
    [2, 1, 1, 2, 2],
    [1, 1, 2, 1, 1, 2],
    [1, 1, 1, 1, 1, 1, 1, 1],
    [1, 1, 1, 1, 2, 2],
    [3, 1, 2, 2],
];

/** The keys the sections move through, as the semitones of their tonic above C. */
const KEYS = [0, 7, 2, 9, 4, 5, 10, 3, 0, 9];
const MAJOR = [0, 2, 4, 5, 7, 9, 11];
/** The progression of a section's bars, as scale degrees of their chord's root. */
const PROGRESSION = [0, 3, 4, 0, 5, 1, 4, 0, 0, 5, 3, 4, 2, 5, 4, 0];

/** A small linear congruential generator, so the piece is the same on every run. */
function random(seed) {
    let state = seed >>> 0;
    return () => {
        state = (Math.imul(state, 1664525) + 1013904223) >>> 0;
        return state / 0x1_0000_0000;
    };
}

/** The pitch of a scale degree (any integer, 0 the tonic) in a key, from a base octave. */
function degreePitch(tonic, degree, base) {
    const octave = Math.floor(degree / 7);
    const step = ((degree % 7) + 7) % 7;
    return base + tonic + 12 * octave + MAJOR[step];
}

/** The notes of both hands, [onset, end, pitch, velocity] in ticks of 480 to the quarter. */
export function notes(bars = BARS, seed = 419) {
    const next = random(seed);
    const right = [];
    const left = [];
    let melody = 9;
    for (let bar = 0; bar < bars; bar++) {
        const section = Math.floor(bar / SECTION_BARS);
        const tonic = KEYS[section % KEYS.length];
        const root = PROGRESSION[bar % SECTION_BARS];
        const start = bar * BAR;
        let at = start;
        for (const length of RIGHT_RHYTHMS[Math.floor(next() * RIGHT_RHYTHMS.length)]) {
            // A step most of the time, a leap to a chord tone now and then, kept in the treble.
            melody += next() < 0.75 ? (next() < 0.5 ? 1 : -1) : root + 2 * Math.floor(next() * 3) - melody % 7;
            melody = Math.max(4, Math.min(16, melody));
            right.push([at, at + length * EIGHTH, degreePitch(tonic, melody, 60), 64 + Math.floor(next() * 24)]);
            at += length * EIGHTH;
        }
        // Broken chords in crotchets: root, fifth, third, fifth; every eighth bar a minim and a two-note chord.
        if (bar % 8 === 7) {
            left.push([start, start + 2 * QUARTER, degreePitch(tonic, root, 36), 60]);
            left.push([start + 2 * QUARTER, start + BAR, degreePitch(tonic, root + 2, 48), 56]);
            left.push([start + 2 * QUARTER, start + BAR, degreePitch(tonic, root + 4, 48), 56]);
        } else {
            [0, 4, 2, 4].forEach((degree, beat) => {
                const onset = start + beat * QUARTER;
                left.push([onset, onset + QUARTER, degreePitch(tonic, root + degree, 48), 52 + Math.floor(next() * 16)]);
            });
        }
    }
    return { right, left };
}

function variableLength(value) {
    const bytes = [value & 0x7f];
    for (let rest = value >> 7; rest > 0; rest >>= 7) {
        bytes.unshift((rest & 0x7f) | 0x80);
    }
    return bytes;
}

function chunk(type, data) {
    const header = Buffer.alloc(8);
    header.write(type, 0, "ascii");
    header.writeUInt32BE(data.length, 4);
    return Buffer.concat([header, Buffer.from(data)]);
}

/** A track's events: its name, then note offs before note ons at the same tick, so repeated keys sound again. */
function track(name, hand, channel, meta = []) {
    const events = [];
    for (const [onset, end, pitch, velocity] of hand) {
        events.push({ tick: end, order: 0, data: [0x80 | channel, pitch, 0] });
        events.push({ tick: onset, order: 1, data: [0x90 | channel, pitch, velocity] });
    }
    events.sort((a, b) => a.tick - b.tick || a.order - b.order || a.data[1] - b.data[1]);
    const label = [...Buffer.from(name, "ascii")];
    const data = [0, 0xff, 0x03, label.length, ...label, ...meta];
    let tick = 0;
    for (const event of events) {
        data.push(...variableLength(event.tick - tick), ...event.data);
        tick = event.tick;
    }
    data.push(0, 0xff, 0x2f, 0);
    return chunk("MTrk", data);
}

/** The piece as a standard MIDI file. */
export function midi(bars = BARS) {
    const { right, left } = notes(bars);
    const header = Buffer.alloc(6);
    header.writeUInt16BE(1, 0);
    header.writeUInt16BE(2, 2);
    header.writeUInt16BE(QUARTER, 4);
    const tempo = [0, 0xff, 0x51, 3, (TEMPO_US >> 16) & 0xff, (TEMPO_US >> 8) & 0xff, TEMPO_US & 0xff, 0, 0xff, 0x58, 4, 4, 2, 24, 8];
    return Buffer.concat([chunk("MThd", [...header]), track("Right hand", right, 0, tempo), track("Left hand", left, 1)]);
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
    const path = fileURLToPath(new URL(`../../${OUTPUT}`, import.meta.url));
    writeFileSync(path, midi());
    const { right, left } = notes();
    console.log(`${OUTPUT}: ${BARS} bars, ${right.length + left.length} notes`);
}
