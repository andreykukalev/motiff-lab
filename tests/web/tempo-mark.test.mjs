// The tempo mark music-core writes in its MEI (version 0.5.4, #511 section 17; NotationGenerator.WriteTempos, its exact
// shape asserted in TempoSourceTests): Verovio engraves it over the first bar, as it engraves a MusicXML metronome.
import assert from "node:assert/strict";
import { test } from "node:test";

import createVerovioModule from "../../src/MotiffLab.Web/wwwroot/lib/verovio/verovio-module.mjs";
import { VerovioToolkit } from "../../src/MotiffLab.Web/wwwroot/lib/verovio/verovio.mjs";

function mei(tempo) {
    return `<?xml version="1.0" encoding="UTF-8"?><mei xmlns="http://www.music-encoding.org/ns/mei" meiversion="5.1"><meiHead><fileDesc><titleStmt><title>t</title></titleStmt><pubStmt/></fileDesc></meiHead>`
        + `<music><body><mdiv><score><scoreDef meter.count="4" meter.unit="4"><staffGrp><staffDef n="1" lines="5" clef.shape="G" clef.line="2"/></staffGrp></scoreDef>`
        + `<section><measure n="1"><staff n="1"><layer n="1"><note pname="c" oct="4" dur="1"/></layer></staff>${tempo}</measure></section></score></mdiv></body></music></mei>`;
}

test("a mark of the notes or the player's is engraved as ♩ = N, one of a recording as ♩ ≈ N", async () => {
    const engraver = new VerovioToolkit(await createVerovioModule());
    for (const words of ["= 92", "≈ 88"]) {
        const perMinute = words.slice(2);
        assert.ok(engraver.loadData(mei(`<tempo staff="1" place="above" tstamp="1" mm="${perMinute}" mm.unit="4" midi.bpm="${perMinute}"><rend glyph.auth="smufl">\uECA5</rend> ${words}</tempo>`)));
        const svg = engraver.renderToSVG(1);
        const mark = svg.match(/<g [^>]*class="tempo"[\s\S]*?<\/g>\s*<\/g>/)?.[0] ?? "";
        assert.ok(mark.length > 0, "the tempo is engraved");
        assert.match(mark, new RegExp(words.replace(" ", "\\s*")));
        // The quarter note is SMuFL's metronome note, drawn from the music font.
        assert.match(mark, /font-family="Leipzig"[^>]*>\uECA5</);
    }
});

test("in 6/8 the mark is a dotted quarter, ♩. = N, as Verovio writes a MusicXML one", async () => {
    const engraver = new VerovioToolkit(await createVerovioModule());
    assert.ok(engraver.loadData(mei(`<tempo staff="1" place="above" tstamp="1" mm="60" mm.unit="4" mm.dots="1" midi.bpm="90"><rend glyph.auth="smufl">\uECA5 \uECB7</rend> = 60</tempo>`)));
    const mark = engraver.renderToSVG(1).match(/<g [^>]*class="tempo"[\s\S]*?<\/g>\s*<\/g>/)?.[0] ?? "";
    assert.match(mark, /=\s*60/);
    // The note and its augmentation dot, both from the music font.
    assert.match(mark, /\uECA5/);
    assert.match(mark, /\uECB7/);
});

test("without a mark nothing is engraved over the bar", async () => {
    const engraver = new VerovioToolkit(await createVerovioModule());
    assert.ok(engraver.loadData(mei("")));
    assert.doesNotMatch(engraver.renderToSVG(1), /class="tempo"/);
});
