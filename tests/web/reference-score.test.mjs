// The Reference's notation (Scripts/reference/reference-score.ts, version 0.5): Verovio's layout for a chord, a scale or a
// progression, and the written tick a played one stands for when the music sounds on a piece's timeline.
//   dotnet build src/MotiffLab.Web && node --test tests/web
import assert from "node:assert/strict";
import { test } from "node:test";

import { referenceLayout, writtenTick } from "../../src/MotiffLab.Web/wwwroot/js/reference/reference-score.js";

test("a chord keeps its own width; a progression is stretched to the box and engraved smaller", () => {
    const chord = referenceLayout(700, false);
    const progression = referenceLayout(700, true);
    assert.equal(chord.adjustPageWidth, true);
    assert.equal(progression.adjustPageWidth, false);
    assert.ok(progression.scale < chord.scale);
    assert.equal(progression.pageWidth, Math.max(900, Math.round(70000 / progression.scale)));
    assert.equal(referenceLayout(100, false).pageWidth, 900, "never narrower than a bar or two");
});

test("a played tick maps back by the start and the two tempos, and nothing sounds before the start", () => {
    const placement = { at: 1920, musicTempo: 600000, timelineTempo: 400000 };
    assert.equal(writtenTick(1000, placement), -1);
    assert.equal(writtenTick(1920, placement), 0);
    // The music at a quarter of 600 ms over a timeline at 400 ms: 480 timeline ticks are 320 written ones.
    assert.equal(writtenTick(2400, placement), 320);
    assert.equal(writtenTick(2400, { at: 1920, musicTempo: 600000, timelineTempo: 600000 }), 480);
});
