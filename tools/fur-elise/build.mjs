// The example piece of both apps: the whole of Beethoven's Für Elise (WoO 59), one piano part on two
// staves. The notes come from the Mutopia Project's edition (Mutopia-2015/08/18-931, typeset in LilyPond
// by Stelios Samelis and placed in the public domain by him): its MIDI file, mutopia-931.mid beside
// this script, is read and written out as MusicXML with the edition's pickup, repeats and endings.
//   src/MotiffLab.Web/Playback/FurElise.musicxml   embedded in the web app, bundled in the iPad app
// Run after changing this script:   node tools/fur-elise/build.mjs
// tests/web/fur-elise.test.mjs fails when the written file is not what this script gives.

import { readFileSync, writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

const root = fileURLToPath(new URL("../../", import.meta.url));

export const OUTPUT = "src/MotiffLab.Web/Playback/FurElise.musicxml";

/** Ticks of the MIDI file and of the MusicXML: a quarter note. */
const QUARTER = 384;
const SIXTEENTH = QUARTER / 4;
const PICKUP = 2 * SIXTEENTH;
const BAR = 6 * SIXTEENTH;
const TRIPLET = (2 * SIXTEENTH) / 3;

/** LilyPond plays a grace note by taking its time from the note before; these are graces. */
const GRACE_LENGTHS = new Set([10, 21]);

/**
 * The edition's repeats, by measure: bars 1–8 with two endings, the first a short bar of two eighths
 * (the pickup follows it again), then bars 10–23 with two endings.
 */
const ENDINGS = new Map([[8, 1], [9, 2], [23, 1], [24, 2]]);
const REPEAT_BACK = new Set([8, 23]);
const REPEAT_FORWARD = new Set([10]);
const SHORT_BARS = new Map([[8, 4 * SIXTEENTH]]);

/** The notes of each track of a standard MIDI file: [onset, end, pitch] in its own ticks. */
export function readMidi(bytes) {
    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    const tracks = view.getUint16(10);
    let position = 14;
    const result = [];
    for (let t = 0; t < tracks; t++) {
        const length = view.getUint32(position + 4);
        let i = position + 8;
        const end = i + length;
        position = end;
        const quantity = () => {
            let value = 0;
            for (;;) {
                const byte = bytes[i++];
                value = (value << 7) | (byte & 0x7f);
                if (byte < 0x80) return value;
            }
        };
        let tick = 0;
        let status = 0;
        const sounding = new Map();
        const notes = [];
        while (i < end) {
            tick += quantity();
            if (bytes[i] === 0xff) {
                i += 2;
                const skip = quantity();
                i += skip;
                continue;
            }
            if (bytes[i] === 0xf0 || bytes[i] === 0xf7) {
                i++;
                const skip = quantity();
                i += skip;
                continue;
            }
            if (bytes[i] & 0x80) status = bytes[i++];
            const kind = status & 0xf0;
            if (kind === 0xc0 || kind === 0xd0) {
                i++;
                continue;
            }
            const [key, velocity] = [bytes[i], bytes[i + 1]];
            i += 2;
            if (kind === 0x90 && velocity > 0) {
                sounding.set(key, [...(sounding.get(key) ?? []), tick]);
            } else if (kind === 0x80 || kind === 0x90) {
                const [start, ...rest] = sounding.get(key);
                sounding.set(key, rest);
                notes.push([start, tick, key]);
            }
        }
        result.push(notes.sort((a, b) => a[0] - b[0] || a[2] - b[2]));
    }
    return result;
}

/** A hand's notes as events (a note or a chord, with the graces before it), every event a written length. */
export function eventsOf(notes) {
    const byOnset = new Map();
    for (const [start, end, pitch] of notes) {
        const event = byOnset.get(start) ?? { at: start, length: end - start, pitches: [] };
        if (event.length !== end - start) throw new Error(`a chord at ${start} with notes of different lengths`);
        event.pitches.push(pitch);
        byOnset.set(start, event);
    }
    const events = [];
    let graces = [];
    for (const event of [...byOnset.values()].sort((a, b) => a.at - b.at)) {
        if (GRACE_LENGTHS.has(event.length)) {
            graces.push(...event.pitches);
            continue;
        }
        // The note before the graces gave them its time: it is written to its full length.
        const before = events.at(-1);
        if (graces.length > 0 && before && before.at + before.length < event.at) before.length = event.at - before.at;
        events.push({ ...event, graces });
        graces = [];
    }
    return events;
}

const VALUES = new Map([
    [6 * SIXTEENTH, ["quarter", 1]],
    [4 * SIXTEENTH, ["quarter", 0]],
    [3 * SIXTEENTH, ["eighth", 1]],
    [2 * SIXTEENTH, ["eighth", 0]],
    [(3 * SIXTEENTH) / 2, ["16th", 1]],
    [SIXTEENTH, ["16th", 0]],
    [SIXTEENTH / 2, ["32nd", 0]],
    [TRIPLET, ["16th", 0]],
]);

/** Beams over a note of this length: an eighth 1, a 16th 2, a 32nd 3; a quarter or longer none. */
const beamsOf = (length) => (length >= 4 * SIXTEENTH ? 0 : length >= 2 * SIXTEENTH ? 1 : length >= SIXTEENTH / 1.5 ? 2 : 3);

const STEPS = ["C", "C", "D", "D", "E", "F", "F", "G", "G", "A", "B", "B"];
const ALTERS = [0, 1, 0, 1, 0, 0, 1, 0, 1, 0, -1, 0];

/** A pitch spelled in A minor: sharps, B flat, and E flat in a bar that has a B flat. */
function spell(pitch, flats) {
    const pc = pitch % 12;
    let [step, alter] = [STEPS[pc], ALTERS[pc]];
    if (pc === 3 && flats) [step, alter] = ["E", -1];
    const octave = Math.floor((pitch - alter) / 12) - 1;
    return { step, alter, octave };
}

const ACCIDENTALS = { "-1": "flat", 0: "natural", 1: "sharp" };

/** Where each measure starts and ends: the pickup, then bars of three eighths but the short ones. */
function measuresUpTo(end) {
    const result = [[0, PICKUP]];
    while (result.at(-1)[1] < end) {
        const start = result.at(-1)[1];
        result.push([start, start + (SHORT_BARS.get(result.length) ?? BAR)]);
    }
    return result;
}

/** Rests that fill [from, to) in a bar, each starting on a multiple of its length. */
function rests(from, to, barStart) {
    const result = [];
    let at = from;
    while (at < to) {
        const length = [2 * SIXTEENTH, SIXTEENTH, SIXTEENTH / 2, TRIPLET].find((value) => (at - barStart) % value === 0 && at + value <= to);
        if (!length) throw new Error(`a rest at ${at} that fits no value`);
        result.push({ at, length, rest: true });
        at += length;
    }
    return result;
}

/** The items of one staff in one bar: events and the rests between them. */
function itemsOf(events, start, end, whole) {
    const inside = events.filter((event) => event.at >= start && event.at < end);
    for (const event of inside) {
        if (event.at + event.length > end) throw new Error(`a note at ${event.at} crosses the bar line`);
        if (!VALUES.has(event.length)) throw new Error(`a note at ${event.at} of length ${event.length}`);
    }
    if (inside.length === 0 && whole) return [{ at: start, length: end - start, rest: true, measure: true }];
    const items = [];
    let at = start;
    for (const event of inside) {
        items.push(...rests(at, event.at, start), event);
        at = event.at + event.length;
    }
    items.push(...rests(at, end, start));
    return items;
}

/** Beam values per level for a run of beamed items. */
function beam(run) {
    for (let level = 1; level <= 3; level++) {
        run.forEach((item, i) => {
            if (item.beams < level) return;
            const before = i > 0 && run[i - 1].beams >= level;
            const after = i < run.length - 1 && run[i + 1].beams >= level;
            const value = before && after ? "continue" : before ? "end" : after ? "begin" : i === 0 ? "forward hook" : "backward hook";
            item.beam.push([level, value]);
        });
    }
}

function beamRuns(items) {
    let run = [];
    const close = () => {
        if (run.length > 1) beam(run);
        run = [];
    };
    for (const item of items) {
        if (item.rest || beamsOf(item.length) === 0) {
            close();
            continue;
        }
        item.beams = beamsOf(item.length);
        run.push(item);
    }
    close();
}

const xml = (strings, ...values) => String.raw({ raw: strings }, ...values);

function noteXml(pitch, item, { chord, staff, voice, accidental }) {
    const [type, dots] = VALUES.get(item.length);
    const triplet = item.length === TRIPLET;
    const parts = ["<note>"];
    if (chord) parts.push("<chord/>");
    parts.push(xml`<pitch><step>${pitch.step}</step>${pitch.alter ? `<alter>${pitch.alter}</alter>` : ""}<octave>${pitch.octave}</octave></pitch>`);
    parts.push(`<duration>${item.length}</duration><voice>${voice}</voice><type>${type}</type>`);
    for (let d = 0; d < dots; d++) parts.push("<dot/>");
    if (accidental) parts.push(`<accidental>${accidental}</accidental>`);
    if (triplet) parts.push("<time-modification><actual-notes>3</actual-notes><normal-notes>2</normal-notes></time-modification>");
    parts.push(`<staff>${staff}</staff>`);
    if (!chord) for (const [level, value] of item.beam ?? []) parts.push(`<beam number="${level}">${value}</beam>`);
    if (triplet && !chord) {
        const offset = (item.at - item.barStart) % (2 * SIXTEENTH);
        if (offset === 0) parts.push('<notations><tuplet type="start" bracket="no"/></notations>');
        else if (offset + TRIPLET === 2 * SIXTEENTH) parts.push('<notations><tuplet type="stop"/></notations>');
    }
    parts.push("</note>");
    return parts.join("");
}

function graceXml(pitches, staff, voice, accidentalOf) {
    return pitches
        .map((pitch, i) => {
            const single = pitches.length === 1;
            const beams = single ? "" : [1, 2].map((level) => `<beam number="${level}">${i === 0 ? "begin" : i === pitches.length - 1 ? "end" : "continue"}</beam>`).join("");
            const accidental = accidentalOf(pitch);
            return xml`<note><grace${single ? ' slash="yes"' : ""}/><pitch><step>${pitch.step}</step>${pitch.alter ? `<alter>${pitch.alter}</alter>` : ""}<octave>${pitch.octave}</octave></pitch><voice>${voice}</voice><type>${single ? "eighth" : "16th"}</type>${accidental ? `<accidental>${accidental}</accidental>` : ""}<staff>${staff}</staff>${beams}</note>`;
        })
        .join("");
}

/** The left hand's clef for a bar: treble when its notes sit around E4 or higher, bass otherwise, unchanged when it rests. */
function clefFor(events, previous) {
    const pitches = events.flatMap((event) => event.pitches);
    if (pitches.length === 0) return previous;
    return pitches.reduce((sum, pitch) => sum + pitch, 0) / pitches.length >= 64 ? "treble" : "bass";
}

const CLEFS = { treble: "<sign>G</sign><line>2</line>", bass: "<sign>F</sign><line>4</line>" };

/** The whole piece as MusicXML. */
export function musicXml(midi) {
    const [, right, left] = readMidi(midi);
    const hands = [eventsOf(right), eventsOf(left)];
    const end = Math.max(...hands.flat().map((event) => event.at + event.length));
    const spans = measuresUpTo(end);
    const bars = spans.length;
    const measures = [];
    let clef = "bass";
    for (let index = 0; index < bars; index++) {
        const [start, stop] = spans[index];
        const inBar = hands.map((events) => events.filter((event) => event.at >= start && event.at < stop));
        const flats = inBar.flat().some((event) => event.pitches.some((pitch) => pitch % 12 === 10) || event.graces.some((pitch) => pitch % 12 === 10));
        const parts = [`<measure number="${index}"${index === 0 ? ' implicit="yes"' : ""}>`];
        if (index === 0) {
            parts.push(
                "<attributes><divisions>384</divisions><key><fifths>0</fifths><mode>minor</mode></key><time><beats>3</beats><beat-type>8</beat-type></time><staves>2</staves>",
                `<clef number="1">${CLEFS.treble}</clef><clef number="2">${CLEFS.bass}</clef></attributes>`,
                '<direction placement="above"><direction-type><words font-weight="bold">Poco moto</words></direction-type><staff>1</staff><sound tempo="72"/></direction>',
                '<direction placement="below"><direction-type><dynamics><pp/></dynamics></direction-type><staff>1</staff></direction>',
            );
        }
        const leftClef = clefFor(inBar[1], clef);
        if (leftClef !== clef) {
            parts.push(`<attributes><clef number="2">${CLEFS[leftClef]}</clef></attributes>`);
            clef = leftClef;
        }
        if (ENDINGS.has(index) || REPEAT_FORWARD.has(index)) {
            parts.push(
                `<barline location="left">${REPEAT_FORWARD.has(index) ? '<bar-style>heavy-light</bar-style>' : ""}${ENDINGS.has(index) ? `<ending number="${ENDINGS.get(index)}" type="start"/>` : ""}${REPEAT_FORWARD.has(index) ? '<repeat direction="forward"/>' : ""}</barline>`,
            );
        }
        hands.forEach((events, hand) => {
            const staff = hand + 1;
            const voice = hand === 0 ? 1 : 5;
            if (hand === 1) parts.push(`<backup><duration>${stop - start}</duration></backup>`);
            const items = itemsOf(events, start, stop, index > 0).map((item) => ({ ...item, barStart: start, beam: [] }));
            beamRuns(items);
            const state = new Map();
            const accidentalOf = (spelled) => {
                const key = `${spelled.step}${spelled.octave}`;
                if ((state.get(key) ?? 0) === spelled.alter) return null;
                state.set(key, spelled.alter);
                return ACCIDENTALS[spelled.alter];
            };
            for (const item of items) {
                if (item.rest) {
                    const value = item.measure ? "" : `<type>${VALUES.get(item.length)[0]}</type>`;
                    parts.push(`<note><rest${item.measure ? ' measure="yes"' : ""}/><duration>${item.length}</duration><voice>${voice}</voice>${value}<staff>${staff}</staff></note>`);
                    continue;
                }
                if (item.graces.length > 0) parts.push(graceXml(item.graces.map((pitch) => spell(pitch, flats)), staff, voice, accidentalOf));
                [...item.pitches].sort((a, b) => a - b).forEach((pitch, i) => {
                    const spelled = spell(pitch, flats);
                    parts.push(noteXml(spelled, item, { chord: i > 0, staff, voice, accidental: accidentalOf(spelled) }));
                });
            }
        });
        if (ENDINGS.has(index) || index === bars - 1) {
            const ending = ENDINGS.has(index) ? `<ending number="${ENDINGS.get(index)}" type="${REPEAT_BACK.has(index) ? "stop" : "discontinue"}"/>` : "";
            const style = REPEAT_BACK.has(index) ? "<bar-style>light-heavy</bar-style>" : index === bars - 1 ? "<bar-style>light-heavy</bar-style>" : "";
            parts.push(`<barline location="right">${style}${ending}${REPEAT_BACK.has(index) ? '<repeat direction="backward"/>' : ""}</barline>`);
        }
        parts.push("</measure>");
        measures.push(parts.join(""));
    }
    return [
        '<?xml version="1.0" encoding="UTF-8"?>',
        '<!DOCTYPE score-partwise PUBLIC "-//Recordare//DTD MusicXML 4.0 Partwise//EN" "http://www.musicxml.org/dtds/partwise.dtd">',
        '<score-partwise version="4.0">',
        "<work><work-title>Für Elise</work-title></work>",
        '<identification><creator type="composer">Ludwig van Beethoven</creator>',
        "<rights>Public domain. The notes of the Mutopia Project edition Mutopia-2015/08/18-931, typeset by Stelios Samelis and placed in the public domain by him.</rights>",
        "<encoding><software>tools/fur-elise/build.mjs</software></encoding></identification>",
        '<part-list><score-part id="P1"><part-name print-object="no">Piano</part-name></score-part></part-list>',
        '<part id="P1">',
        ...measures,
        "</part>",
        "</score-partwise>",
        "",
    ].join("\n");
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
    const midi = readFileSync(`${root}tools/fur-elise/mutopia-931.mid`);
    writeFileSync(`${root}${OUTPUT}`, musicXml(midi));
    console.log(`wrote ${OUTPUT}`);
}
