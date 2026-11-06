// The example piece (testing log item 26, 2026-11-06): the whole of Für Elise, one piano part on two staves,
// written by tools/fur-elise/build.mjs from the Mutopia Project's public-domain edition.
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";

import { OUTPUT, eventsOf, musicXml, readMidi } from "../../tools/fur-elise/build.mjs";
import createVerovioModule from "../../src/MotiffLab.Web/wwwroot/lib/verovio/verovio-module.mjs";
import { VerovioToolkit } from "../../src/MotiffLab.Web/wwwroot/lib/verovio/verovio.mjs";

const root = new URL("../../", import.meta.url);
const midi = readFileSync(new URL("tools/fur-elise/mutopia-931.mid", root));
const written = readFileSync(new URL(OUTPUT, root), "utf8");

test("the file in the apps is what the build script writes", () => {
    assert.equal(written, musicXml(midi), "run node tools/fur-elise/build.mjs");
});

test("every note of the edition is written, the graces before the notes they lead to", () => {
    const [, right, left] = readMidi(midi);
    const graces = eventsOf(right).flatMap((event) => event.graces).length;
    const notes = (written.match(/<note>(?!<rest)/g) ?? []).length;
    assert.equal(notes, right.length + left.length);
    assert.equal((written.match(/<grace/g) ?? []).length, graces);
    assert.ok(graces > 0);
});

test("it is one part on two staves, a pickup and then the bars, with the edition's repeats", async () => {
    assert.equal((written.match(/<score-part /g) ?? []).length, 1);
    assert.match(written, /<measure number="0" implicit="yes">/);
    assert.equal((written.match(/<repeat direction="backward"\/>/g) ?? []).length, 2);
    const engraver = new VerovioToolkit(await createVerovioModule());
    assert.ok(engraver.loadData(written));
    const svg = engraver.renderToSVG(1);
    assert.equal((svg.match(/class="measure"/g) ?? []).length > 10, true);
    assert.equal(svg.match(/class="measure"[\s\S]*?class="measure"/)[0].match(/class="staff"/g).length, 2);
});
