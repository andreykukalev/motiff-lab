// Tests for the phrase card in focus (decision record 0018, section 4), run in Node on the compiled module and the
// vendored Verovio build: the card's layout, the ossia's frame round its staves, and which notes light up.
//   dotnet build src/MotiffLab.Web && node --test tests/web

import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";

import { CARD_SCALE, cardLayout, soundingIds } from "../../src/MotiffLab.Web/wwwroot/js/score/phrase-card.js";
import { readPage, staffFrames } from "../../src/MotiffLab.Web/wwwroot/js/score/score-geometry.js";
import createVerovioModule from "../../src/MotiffLab.Web/wwwroot/lib/verovio/verovio-module.mjs";
import { VerovioToolkit } from "../../src/MotiffLab.Web/wwwroot/lib/verovio/verovio.mjs";

const FUR_ELISE = readFileSync(new URL("../../design/kit/elise.musicxml", import.meta.url), "utf8");

let module;

async function engrave(options) {
    module ??= await createVerovioModule();
    const toolkit = new VerovioToolkit(module);
    toolkit.setOptions(options);
    assert.ok(toolkit.loadData(FUR_ELISE));
    const pages = [];
    for (let page = 1; page <= toolkit.getPageCount(); page++) {
        pages.push(readPage(toolkit.renderToSVG(page), page));
    }
    toolkit.destroy?.();
    return pages;
}

test("the card is one page as tall as the music, broken into systems at its width", async () => {
    const wide = cardLayout(1100);
    assert.equal(wide.scale, CARD_SCALE);
    assert.equal(wide.pageWidth, Math.round((1100 * 100) / CARD_SCALE));
    assert.equal(wide.header, "none");
    assert.equal(wide.adjustPageHeight, true);
    // A phone in portrait still gets a page wide enough for a few bars.
    assert.equal(cardLayout(200).pageWidth, cardLayout(300).pageWidth);

    const pages = await engrave(cardLayout(1100));
    assert.equal(pages.length, 1);
    assert.ok(pages[0].systems.length > 1);
    // Every system is justified to the same width, the last one too.
    const rights = pages[0].systems.map((system) => Math.round(system.right));
    assert.ok(Math.max(...rights) - Math.min(...rights) <= 2, `rights ${rights}`);
});

test("an ossia's frame goes round its staves on each system, and only round those", async () => {
    const [page] = await engrave(cardLayout(1100));
    const top = staffFrames(page, [1]);
    const bottom = staffFrames(page, [2]);
    const both = staffFrames(page, [1, 2]);
    assert.equal(top.length, page.systems.length);
    assert.equal(both.length, page.systems.length);
    page.systems.forEach((system, index) => {
        assert.ok(top[index].bottom < bottom[index].top);
        assert.equal(both[index].top, top[index].top);
        assert.equal(both[index].bottom, bottom[index].bottom);
        assert.equal(both[index].top, system.top);
        assert.equal(both[index].bottom, system.bottom);
        assert.ok(Math.abs(both[index].left - system.left) < 1 && Math.abs(both[index].right - system.right) < 1);
    });
    assert.deepEqual(staffFrames(page, [3]), []);
    assert.deepEqual(staffFrames(page, []), []);
});

test("the notes that light up are those of what is heard: the ossia's only while the variation sounds", () => {
    const ids = ["n_1", "ossia-n_1", "n_2", "ossia2-n_3"];
    const prefixes = ["ossia-", "ossia2-"];
    assert.deepEqual(soundingIds(ids, "original", prefixes), ["n_1", "n_2"]);
    assert.deepEqual(soundingIds(ids, "variation", prefixes), ["ossia-n_1", "ossia2-n_3"]);
    assert.deepEqual(soundingIds(ids, "both", prefixes), ids);
    // Without an ossia every note is the original's.
    assert.deepEqual(soundingIds(["n_1"], "variation", []), ["n_1"]);
});
