// The music of the held playhead's tests (#582 items 12 and 13) as the score engraves it: one bar of sixteenths with
// repeated keys, a bar with an eighth, two thirty-seconds and a half note tied over the barline, and the bar the tie
// ends in, engraved by the vendored Verovio. In Node on the compiled modules:
//   dotnet build src/shared && node --test tests/shared

import assert from "node:assert/strict";
import { test } from "node:test";

import { readPage } from "../../src/shared/wwwroot/js/score/page-reader.js";
import { ScoreGeometry } from "../../src/shared/wwwroot/js/score/score-geometry.js";
import { ScoreTimeline } from "../../src/shared/wwwroot/js/score/score-timeline.js";
import createVerovioModule from "../../src/shared/wwwroot/lib/verovio/verovio-module.mjs";
import { VerovioToolkit } from "../../src/shared/wwwroot/lib/verovio/verovio.mjs";

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

test("the engraved score has a note of its own where the tie goes on, so a drag reaches it", async () => {
    const { timeline } = await score();
    const onsets = timeline.spans.map((span) => span.onset);
    assert.ok(onsets.includes(TIE) && onsets.includes(TIE_CONTINUED), "the tie's first note and the note it goes on to");
    assert.equal(timeline.spans.length, NOTES.length + 1, "one engraved note more than music-core's notes: the tie's second");
});
