// Tests for the score view's timeline (S1-W7, decision record 0008), run in Node on the compiled
// module and the vendored Verovio build:
//   dotnet build src/MotiffLab.Web && node --test tests/web

import assert from "node:assert/strict";
import { test } from "node:test";

import { ScoreTimeline } from "../../src/MotiffLab.Web/wwwroot/js/score/score-timeline.js";
import createVerovioModule from "../../src/MotiffLab.Web/wwwroot/lib/verovio/verovio-module.mjs";
import { VerovioToolkit } from "../../src/MotiffLab.Web/wwwroot/lib/verovio/verovio.mjs";

test("timemap positions in quarter notes become tick spans", () => {
    const timeline = ScoreTimeline.fromTimemap([
        { qstamp: 0, on: ["a", "b"] },
        { qstamp: 1, off: ["a"], on: ["c"] },
        { qstamp: 1.5, off: ["c"] },
        { qstamp: 2, off: ["b"] },
    ]);

    assert.deepEqual(timeline.spans, [
        { id: "a", onset: 0, end: 480 },
        { id: "b", onset: 0, end: 960 },
        { id: "c", onset: 480, end: 720 },
    ]);
    assert.equal(timeline.onsetOf("c"), 480);
    assert.equal(timeline.onsetOf("missing"), undefined);
});

test("sounding notes start at or before the tick and end after it", () => {
    const timeline = ScoreTimeline.fromTimemap([
        { qstamp: 0, on: ["long"] },
        { qstamp: 3, on: ["short"] },
        { qstamp: 3.25, off: ["short"] },
        { qstamp: 8, off: ["long"] },
    ]);

    assert.deepEqual(timeline.soundingAt(0), ["long"]);
    assert.deepEqual(timeline.soundingAt(1440), ["long", "short"]);
    assert.deepEqual(timeline.soundingAt(1559), ["long", "short"]);
    assert.deepEqual(timeline.soundingAt(1560), ["long"]);
    assert.deepEqual(timeline.soundingAt(3840), []);
    assert.deepEqual(timeline.soundingAt(-10), []);
});

test("triplet positions round to the nearest tick", () => {
    const timeline = ScoreTimeline.fromTimemap([
        { qstamp: 0, on: ["t1"] },
        { qstamp: 1 / 3, off: ["t1"], on: ["t2"] },
        { qstamp: 2 / 3, off: ["t2"] },
    ]);

    assert.deepEqual(timeline.spans.map((span) => [span.onset, span.end]), [[0, 160], [160, 320]]);
});

test("a note the timemap never stops lasts to the last position", () => {
    const timeline = ScoreTimeline.fromTimemap([{ qstamp: 0, on: ["held"] }, { qstamp: 4, on: ["late"] }]);

    assert.deepEqual(timeline.spans, [
        { id: "held", onset: 0, end: 1920 },
        { id: "late", onset: 1920, end: 1921 },
    ]);
});

test("the vendored Verovio keeps our note ids and times generated MEI in quarter notes", async () => {
    const toolkit = new VerovioToolkit(await createVerovioModule());
    // The shape music-core writes (NotationGenerator): a pickup, a tie over the bar line, a chord, a beam.
    const mei = `<?xml version="1.0" encoding="UTF-8"?>
<mei xmlns="http://www.music-encoding.org/ns/mei" meiversion="5.1"><meiHead><fileDesc><titleStmt><title>Test</title></titleStmt><pubStmt/></fileDesc></meiHead>
<music><body><mdiv><score><scoreDef meter.count="3" meter.unit="4" keysig="1s"><staffGrp><staffDef n="1" lines="5" clef.shape="G" clef.line="2"/></staffGrp></scoreDef>
<section>
<measure n="0" metcon="false"><staff n="1"><layer n="1"><note xml:id="n_0001" dur="4" pname="d" oct="4"/></layer></staff></measure>
<measure n="1"><staff n="1"><layer n="1"><chord xml:id="c_n_0002" dur="2"><note xml:id="n_0002" pname="g" oct="4"/><note xml:id="n_0003" pname="b" oct="4"/></chord><beam><note xml:id="n_0004" dur="8" pname="a" oct="4"/><note xml:id="n_0005" dur="8" pname="f" oct="4" accid.ges="s" tie="i"/></beam></layer></staff></measure>
<measure n="2"><staff n="1"><layer n="1"><note xml:id="n_0005-2" dur="2" dots="1" pname="f" oct="4" accid.ges="s" tie="t"/></layer></staff></measure>
</section></score></mdiv></body></music></mei>`;

    assert.ok(toolkit.loadData(mei));
    const svg = toolkit.renderToSVG(1);
    for (const id of ["n_0001", "c_n_0002", "n_0002", "n_0005", "n_0005-2"]) {
        assert.match(svg, new RegExp(`id="${id}"`));
    }

    const timeline = ScoreTimeline.fromTimemap(toolkit.renderToTimemap());
    assert.deepEqual(timeline.spans, [
        { id: "n_0001", onset: 0, end: 480 },
        { id: "n_0002", onset: 480, end: 1440 },
        { id: "n_0003", onset: 480, end: 1440 },
        { id: "n_0004", onset: 1440, end: 1680 },
        { id: "n_0005", onset: 1680, end: 1920 },
        { id: "n_0005-2", onset: 1920, end: 3360 },
    ]);
});

test("the vendored Verovio engraves MusicXML as written", async () => {
    const toolkit = new VerovioToolkit(await createVerovioModule());
    const musicXml = `<?xml version="1.0" encoding="UTF-8"?>
<score-partwise version="4.0"><part-list><score-part id="P1"><part-name>Piano</part-name></score-part></part-list>
<part id="P1"><measure number="1"><attributes><divisions>2</divisions><key><fifths>0</fifths></key><time><beats>2</beats><beat-type>4</beat-type></time><clef><sign>G</sign><line>2</line></clef></attributes>
<note><pitch><step>C</step><octave>4</octave></pitch><duration>3</duration><type>quarter</type><dot/></note>
<note><pitch><step>E</step><octave>4</octave></pitch><duration>1</duration><type>eighth</type></note>
</measure></part></score-partwise>`;

    assert.ok(toolkit.loadData(musicXml));
    const timeline = ScoreTimeline.fromTimemap(toolkit.renderToTimemap());
    assert.deepEqual(timeline.spans.map((span) => [span.onset, span.end]), [[0, 720], [720, 960]]);
});
