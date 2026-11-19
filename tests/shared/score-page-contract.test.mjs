// The score page's contract (decision record 0031, step 2): the functions src/ipad/App/Web/score.js offers the iPad app
// (window.motiff) and the keys of the messages it posts are exactly the lists in tests/contracts/score-page.json, which
// the app's PageCommand and PageEvent.Kind are checked against by ScorePageContractTests on macOS.
//   node --test tests/shared   (needs no build: it reads the page's source)

import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";
import { offered, posted } from "./page-contract.mjs";

const root = new URL("../../", import.meta.url);
const contract = JSON.parse(readFileSync(new URL("tests/contracts/score-page.json", root), "utf8"));
const page = readFileSync(new URL("src/ipad/App/Web/score.js", root), "utf8");

test("the page offers exactly the functions the app's PageCommand names", () => {
    assert.deepEqual(offered(page, "motiff").sort(), [...contract.commands].sort());
});

test("the page posts only the message kinds the app's PageEvent decodes, and every one of them", () => {
    assert.deepEqual(posted(page).sort(), [...contract.events].sort());
});

test("the readers see through renamed functions, shorthand keys and nested objects", () => {
    assert.deepEqual(offered("window.motiff = { show, card: showCard };", "motiff"), ["show", "card"]);
    const source = 'post({ chosen: { start, end } }); post(x ? { blank: true } : { place: { start } }); post({ handles });'
        + ' post({ drag: "tick" in m ? { ...m } : m }); postMessage("ready");';
    assert.deepEqual(posted(source).sort(), ["blank", "chosen", "drag", "handles", "place"]);
});
