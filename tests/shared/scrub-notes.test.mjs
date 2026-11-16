// What a held playhead sounds, measured end to end on the score and on the roll with the same music (#582 items 12 and 13):
// one bar of sixteenths with repeated keys, a bar with an eighth, two thirty-seconds and a half note tied over the barline,
// and the bar the tie ends in. The score is engraved by the vendored Verovio and dragged along its notes (playhead-grab.ts),
// the roll along its time (roll-head-drag.ts); both go through PlayheadDrag, the playback model (PlaybackOwner) and the
// engine (playback-processor.ts), whose notes are music-core's: the tie merged into its first written note. In Node on the
// compiled modules:   dotnet build src/shared && node --test tests/shared

import assert from "node:assert/strict";
import { test } from "node:test";

import { PlaybackOwner } from "../../src/shared/wwwroot/js/playback/playback-owner.js";
import { Engine, SCRUB_SPREAD_SECONDS } from "../../src/shared/wwwroot/js/playback/playback-processor.js";
import { PlayheadDrag } from "../../src/shared/wwwroot/js/playback/playhead-drag.js";
import { laneDragTick } from "../../src/shared/wwwroot/js/roll/roll-head-drag.js";
import { ScoreGeometry, readPage } from "../../src/shared/wwwroot/js/score/score-geometry.js";
import { scoreDragTick } from "../../src/shared/wwwroot/js/score/playhead-grab.js";
import { ScoreTimeline } from "../../src/shared/wwwroot/js/score/score-timeline.js";
import createVerovioModule from "../../src/shared/wwwroot/lib/verovio/verovio-module.mjs";
import { VerovioToolkit } from "../../src/shared/wwwroot/lib/verovio/verovio.mjs";

const RATE = 48_000;
const BLOCK = 128;
/** One frame of the view: 6 blocks, 16 ms, about a 60 Hz screen. */
const FRAME = 6 * BLOCK;

const STEP = { C: 0, D: 2, E: 4, F: 5, G: 7, A: 9, B: 11 };
const midi = (step, octave) => 12 * (octave + 1) + STEP[step];

/** A MusicXML note at 8 divisions a quarter; `tie` "start" or "stop". */
function note(step, octave, type, duration, tie = null) {
    const tieMark = tie === null ? "" : `<tie type="${tie}"/>`;
    const tied = tie === null ? "" : `<notations><tied type="${tie}"/></notations>`;
    return `<note><pitch><step>${step}</step><octave>${octave}</octave></pitch><duration>${duration}</duration>${tieMark}<voice>1</voice><type>${type}</type>${tied}</note>`;
}

// Bar 1: sixteenths, each key twice. Bar 2: a quarter, an eighth, two thirty-seconds, a sixteenth and a half note tied
// into bar 3. Bar 3: the tie's quarter, a quarter and a half.
const SIXTEENTHS = [["G", 4], ["A", 4], ["B", 4], ["C", 5], ["D", 5], ["E", 5], ["F", 5], ["G", 5]].flatMap((key) => [key, key]);
const MUSIC = `<?xml version="1.0" encoding="UTF-8"?>
<score-partwise version="3.1"><part-list><score-part id="P1"><part-name>Piano</part-name></score-part></part-list><part id="P1">
<measure number="1"><attributes><divisions>8</divisions><time><beats>4</beats><beat-type>4</beat-type></time><clef><sign>G</sign><line>2</line></clef></attributes>
${SIXTEENTHS.map(([step, octave]) => note(step, octave, "16th", 2)).join("")}</measure>
<measure number="2">${note("C", 5, "quarter", 8)}${note("D", 5, "eighth", 4)}${note("E", 5, "32nd", 1)}${note("F", 5, "32nd", 1)}${note("G", 5, "16th", 2)}${note("A", 4, "half", 16, "start")}</measure>
<measure number="3">${note("A", 4, "quarter", 8, "stop")}${note("B", 4, "quarter", 8)}${note("C", 5, "half", 16)}</measure>
</part></score-partwise>`;

/** The same music as music-core plays it, [onset, duration, pitch]: the tie is one note from its first, 1440 ticks long. */
const NOTES = [
    ...SIXTEENTHS.map(([step, octave], index) => [index * 120, 120, midi(step, octave)]),
    [1920, 480, midi("C", 5)],
    [2400, 240, midi("D", 5)],
    [2640, 60, midi("E", 5)],
    [2700, 60, midi("F", 5)],
    [2760, 120, midi("G", 5)],
    [2880, 1440, midi("A", 4)],
    [4320, 480, midi("B", 4)],
    [4800, 960, midi("C", 5)],
];
const END = 5760;
const TIE = 2880;
const TIE_CONTINUED = 3840;

/** Each key's sample is silent but for one sample this far in, as loud as the key is high: a struck note shows as one spike. */
const SPIKE_AT = 40;
const spike = (pitch) => (pitch + 1) / 256;

/**
 * An engine whose sound is listened to: each spike in what it renders is a key struck, its frame and its key read from
 * where and how loud the spike is (the loudness of a key at full velocity measured once, `unit`).
 */
class HeardEngine extends Engine {
    heard = [];
    frames = 0;
    unit = 1;

    run(count) {
        const block = new Float32Array(BLOCK);
        for (let done = 0; done < count; done += BLOCK) {
            this.render(block, null, this.frames);
            block.forEach((value, index) => {
                if (Math.abs(value) > 1e-6) {
                    this.heard.push({ frame: this.frames + index - SPIKE_AT, pitch: Math.round((value / this.unit) * 256) - 1, value });
                }
            });
            this.frames += BLOCK;
        }
    }
}

function engine() {
    const regions = Array.from({ length: 128 }, (_, pitch) => {
        const data = new Float32Array(RATE);
        data[SPIKE_AT] = spike(pitch);
        return { pitch, tuneCents: 0, data };
    });
    const calibrate = new HeardEngine(RATE);
    calibrate.handle({ type: "samples", regions });
    calibrate.handle({ type: "noteOn", pitch: 60, velocity: 127 });
    calibrate.run(BLOCK);
    const heard = new HeardEngine(RATE);
    assert.equal(calibrate.heard.length, 1);
    heard.unit = calibrate.heard[0].value / spike(60);
    heard.handle({ type: "samples", regions });
    heard.handle({ type: "timeline", tempos: [0, 500_000], beats: [], bars: [0, 1920, 480, 4], end: END });
    heard.handle({ type: "source", id: "a", notes: NOTES.flatMap(([onset, duration, pitch]) => [onset, duration, pitch, 127, 0]) });
    heard.handle({ type: "activeSources", ids: ["a"] });
    return heard;
}

/** A player as the web's: the model's steps to the engine, the views' drag messages to the model. */
function player(heard) {
    const owner = new PlaybackOwner((step) => {
        switch (step.type) {
            case "scrub":
                heard.handle({ type: "scrub", from: step.from, to: step.to });
                break;
            case "seek":
                heard.handle({ type: "seek", tick: step.at });
                break;
            case "loop":
            case "loopOff":
                break;
            default:
                heard.handle({ type: step.type });
        }
    });
    owner.held = true;
    const place = () => ({ tick: heard.tick, transport: heard.transportState });
    let frame = null;
    const drag = new PlayheadDrag((message) => owner.gesture(message, place()), (run) => (frame = run), () => heard.frames);
    /** One frame of the screen: the newest place goes to the player, then the engine renders that long. */
    const tick = () => {
        const run = frame;
        frame = null;
        run?.();
        heard.run(FRAME);
    };
    return { drag, tick };
}

let engraved;

/** The score engraved as the score view does on a page, with its geometry and timeline. */
async function score() {
    if (engraved === undefined) {
        const toolkit = new VerovioToolkit(await createVerovioModule());
        toolkit.setOptions({ scale: 30, pageWidth: 2100, pageMarginLeft: 40, pageMarginRight: 40, pageMarginTop: 40, pageMarginBottom: 40, adjustPageHeight: true, breaks: "auto", header: "none", footer: "none", svgViewBox: true });
        assert.ok(toolkit.loadData(MUSIC));
        const page = readPage(toolkit.renderToSVG(1), 1);
        const timeline = ScoreTimeline.fromTimemap(toolkit.renderToTimemap({ includeRests: true, includeMeasures: true }));
        const geometry = new ScoreGeometry([page], [{ left: 0, top: 0, scale: 1 }], (id) => timeline.measureOnset(id), END);
        engraved = { geometry, timeline };
    }
    return engraved;
}

/**
 * A drag of the black playhead from where it stands at the piece's start to the end of bar 3, at a steady speed of
 * `pace` pixels a frame (`places` turns a pixel into the view's tick): which keys sound on which audio frame, and the
 * tick the view drew in each frame.
 */
function dragAcross(places, from, to, pace) {
    const heard = engine();
    const { drag, tick } = player(heard);
    drag.grab("black", 1);
    const drawn = [];
    for (let x = from; x <= to; x += pace) {
        const at = places(x);
        if (at !== undefined) {
            drag.move(1, at);
        }
        drawn.push({ frame: heard.frames, tick: drag.heard(heard.tick) });
        tick();
    }
    drag.release(1);
    tick();
    return { heard: heard.heard, drawn };
}

async function scoreDrag(pace) {
    const { geometry, timeline } = await score();
    const [first, , third] = geometry.bars;
    const y = (first.staves[0].top + first.staves[0].bottom) / 2;
    const onsetOf = (id) => timeline.onsetOf(id);
    const start = geometry.playhead(0, onsetOf).x;
    return dragAcross((x) => scoreDragTick(geometry, onsetOf, "black", { x, y }, null), start, third.right - 1, pace);
}

/** The roll at its default zoom, 96 pixels a quarter: the black playhead's place under a pixel (piano-roll.ts tickAtX). */
function rollDrag(pace) {
    const zoom = 96;
    const bars = [0, 1920, 3840].map((start) => ({ start, end: start + 1920 }));
    const onsets = [...new Set(NOTES.map(([onset]) => onset))];
    return dragAcross((x) => laneDragTick("black", (x * 480) / zoom, bars, null, END, onsets), 0, (END / 480) * zoom - 1, pace);
}

/**
 * The notes as they sounded on a drag forward: each strike's note is the earliest of its key not heard yet whose start the
 * playhead was drawn at or past by then; null when there is none (a note struck again, or before it is reached).
 */
function struckNotes(heard, drawn) {
    const claimed = new Set();
    return [...heard].sort((a, b) => a.frame - b.frame).map((strike) => {
        const shown = [...drawn].reverse().find((frame) => frame.frame <= strike.frame)?.tick ?? 0;
        const index = NOTES.findIndex(([onset, , pitch], at) => pitch === strike.pitch && onset <= shown && !claimed.has(at));
        claimed.add(index);
        return { ...strike, onset: index < 0 ? null : NOTES[index][0] };
    });
}

/** Every note once, in time order, each on its own frame, never before the playhead is drawn on it nor long after. */
function assertNoteByNote(heard, drawn, label) {
    const struck = struckNotes(heard, drawn);
    assert.deepEqual(struck.map((one) => one.onset), NOTES.map(([onset]) => onset), `${label}: every note once, in the order of their starts, the tie from its first note`);
    const frames = struck.map((one) => one.frame);
    assert.equal(new Set(frames).size, frames.length, `${label}: note by note, never two starts struck together as a chord`);
    for (const one of struck) {
        // The note under the playhead as the drag begins sounds as it moves off it.
        const reached = drawn.find((frame) => frame.tick >= one.onset && frame.tick > drawn[0].tick);
        assert.ok(reached !== undefined && one.frame >= reached.frame, `${label}: the note at ${one.onset} sounds once the playhead is drawn there`);
        assert.ok(one.frame - reached.frame <= FRAME + SCRUB_SPREAD_SECONDS * RATE, `${label}: the note at ${one.onset} sounds as the playhead reaches it`);
    }
}

test("the engraved score has a note of its own where the tie goes on, so a drag reaches it", async () => {
    const { timeline } = await score();
    const onsets = timeline.spans.map((span) => span.onset);
    assert.ok(onsets.includes(TIE) && onsets.includes(TIE_CONTINUED), "the tie's first note and the note it goes on to");
    assert.equal(timeline.spans.length, NOTES.length + 1, "one engraved note more than music-core's notes: the tie's second");
});

test("dragged along the score, every note sounds at its own start, the thirty-seconds and the tie's first note too (#582 item 12)", async () => {
    for (const pace of [3, 8, 20]) {
        const { heard, drawn } = await scoreDrag(pace);
        assertNoteByNote(heard, drawn, `score, ${pace} px a frame`);
    }
});

test("dragged along the roll, every note sounds at its own start, the same as on the score (#582 item 13)", async () => {
    for (const pace of [3, 8, 20]) {
        const { heard, drawn } = rollDrag(pace);
        assertNoteByNote(heard, drawn, `roll, ${pace} px a frame`);
        const roll = struckNotes(heard, drawn).map(({ onset, pitch }) => [onset, pitch]);
        const onScore = await scoreDrag(pace);
        assert.deepEqual(roll, struckNotes(onScore.heard, onScore.drawn).map(({ onset, pitch }) => [onset, pitch]), `${pace} px a frame: the same note events`);
    }
});

test("dragged slowly, the playhead stands on every note, the thirty-seconds included, as it sounds (#582 item 12)", async () => {
    for (const { heard, drawn } of [await scoreDrag(2), rollDrag(2)]) {
        const stood = new Set(drawn.map((frame) => frame.tick));
        for (const [onset] of NOTES) {
            assert.ok(stood.has(onset), `the playhead is drawn at ${onset}`);
        }
        assert.equal(heard.length, NOTES.length);
    }
});

test("reaching the note a tie goes on to sounds nothing, forward or back; back, the tie sounds at its first note (#582 item 12)", async () => {
    const { geometry, timeline } = await score();
    const [, second, third] = geometry.bars;
    const y = (third.staves[0].top + third.staves[0].bottom) / 2;
    const onsetOf = (id) => timeline.onsetOf(id);
    const heard = engine();
    const { drag, tick } = player(heard);
    // Put on the tie's second note, then dragged back to the start of bar 2.
    drag.grab("black", 1);
    drag.place(1, TIE_CONTINUED);
    tick();
    assert.deepEqual(heard.heard, [], "put on the tie's second note: nothing sounds");
    for (let x = geometry.playhead(TIE_CONTINUED, onsetOf).x; x >= second.left; x -= 3) {
        drag.move(1, scoreDragTick(geometry, onsetOf, "black", { x, y }, null));
        tick();
    }
    const keys = heard.heard.map((strike) => strike.pitch);
    assert.deepEqual(keys, [midi("A", 4), midi("G", 5), midi("F", 5), midi("E", 5), midi("D", 5), midi("C", 5)], "the tie once, at its first note, then the notes before it");
});

test("a long press puts the playhead down without sounding what it jumped over; moved on, the note it leaves sounds first (#582 item 12)", () => {
    const heard = engine();
    const { drag, tick } = player(heard);
    drag.grab("black", 1, true);
    drag.place(1, 1920);
    tick();
    assert.equal(heard.tick, 1920, "the engine is there");
    assert.deepEqual(heard.heard, [], "nothing of bar 1 sounds");
    drag.move(1, 2400);
    tick();
    assert.deepEqual(heard.heard.map((strike) => strike.pitch), [midi("C", 5), midi("D", 5)], "the note it was put on, then the one it reached");
    drag.release(1);
    tick();
    drag.grab("black", 2, true);
    drag.place(1, 2880);
    drag.place(2, 2880);
    tick();
    assert.equal(heard.heard.length, 2, "a second press on the tie's first note is silent until it moves");
});
